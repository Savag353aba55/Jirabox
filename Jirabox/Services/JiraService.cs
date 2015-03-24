﻿using BugSense;
using BugSense.Core.Model;
using Jirabox.Common;
using Jirabox.Common.Enumerations;
using Jirabox.Core;
using Jirabox.Core.Contracts;
using Jirabox.Model;
using Jirabox.Resources;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.IO.IsolatedStorage;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.System;
using Jirabox.Core.Exceptions;

namespace Jirabox.Services
{
    public class JiraService : IJiraService
    {
        private readonly IHttpManager httpManager;
        private readonly IJsonHttpClient jsonClient;
        private readonly IDialogService dialogService;
        private readonly ICacheService cacheService;
        public CancellationTokenSource TokenSource;

        public JiraService(IHttpManager httpManager, IJsonHttpClient jsonClient, IDialogService dialogService, ICacheService cacheDataService)
        {
            this.httpManager = httpManager;
            this.jsonClient = jsonClient;
            this.dialogService = dialogService;
            cacheService = cacheDataService;
            TokenSource = new CancellationTokenSource();
        }

        public async Task<bool> LoginAsync(string serverUrl, string username, string password, CancellationTokenSource cancellationTokenSource)
        {
            var requestUrl = string.Format("{0}{1}/", App.BaseUrl, JiraRequestType.Search.ToString().ToLower());
            var response = await httpManager.GetAsync(requestUrl, true, username, password);

            if (response == null) return false;
            if (response.IsSuccessStatusCode) return true;

            throw new HttpRequestStatusCodeException(response.StatusCode);
        }

        public async Task<ObservableCollection<Project>> GetProjects(string serverUrl, string username, string password, bool withoutCache = false)
        {
            const string cacheFileName = "Projects.cache";

            //Check cache data
            var isCacheExist = cacheService.DoesFileExist(cacheFileName);
            if (!withoutCache && isCacheExist)
            {
                var projectListFile = cacheService.Read(cacheFileName);
                return JsonConvert.DeserializeObject<ObservableCollection<Project>>(projectListFile);
            }

            var requestUrl = string.Format("{0}{1}/", App.BaseUrl, JiraRequestType.Project.ToString().ToLower());
            var projects = new ObservableCollection<Project>();
            try
            {
                var response = await httpManager.GetAsync(requestUrl, true, username, password);
                response.EnsureSuccessStatusCode();
                var responseStr = await response.Content.ReadAsStringAsync();
                if (string.IsNullOrEmpty(responseStr)) return new ObservableCollection<Project>();

                projects = JsonConvert.DeserializeObject<ObservableCollection<Project>>(responseStr);

                //Download all project images if they do not exist
                foreach (var project in projects)
                {
                    await DownloadImage(project.AvatarUrls.Size48, project.Key, true);
                }

                //Save new data to the cache
                cacheService.Save(cacheFileName, responseStr);
            }
            catch (Exception exception)
            {
                var extras = BugSenseHandler.Instance.CrashExtraData;
                extras.Add(new CrashExtraData
                {
                    Key = "Method",
                    Value = "JiraService.GetProjects"
                });

                BugSenseHandler.Instance.LogException(exception, extras);
            }
            return projects;
        }

        public async Task<Project> GetProjectByKey(string serverUrl, string username, string password, string key, bool withoutCache = false)
        {
            var cacheFileName = string.Format("ProjectByKey.{0}.cache", key);
            var extras = BugSenseHandler.Instance.CrashExtraData;

            //Check cache file is exist            
            var isCacheExist = cacheService.DoesFileExist(cacheFileName);
            if (!withoutCache && isCacheExist)
            {
                extras.Add(new CrashExtraData
                {
                    Key = "Cached Method",
                    Value = "JiraService.GetProjectByKey"
                });

                var projectFile = cacheService.Read(cacheFileName);
                return JsonConvert.DeserializeObject<Project>(projectFile);
            }

            var requestUrl = string.Format("{0}{1}/{2}", App.BaseUrl, JiraRequestType.Project.ToString().ToLower(), key);
            Project project = null;
            try
            {
                var response = await jsonClient.Get(new Uri(requestUrl), username, password, null);
                project = JsonConvert.DeserializeObject<Project>(response);

                //Download project and project lead images if they do not exist
                await DownloadImage(project.AvatarUrls.Size48, key);
                await DownloadImage(project.Lead.AvatarUrls.Size48, project.Lead.UserName);

                //Save new data to the cache
                cacheService.Save(cacheFileName, response);
            }
            catch (Exception exception)
            {
                extras.Add(new CrashExtraData
                {
                    Key = "Method",
                    Value = "JiraService.GetProjectByKey"
                });

                BugSenseHandler.Instance.LogException(exception, extras);
            }
            return project;
        }

        public async Task<Issue> GetIssueByKey(string serverUrl, string username, string password, string key)
        {
            Issue issue = null;
            var requestUrl = string.Format("{0}{1}/{2}?expand=changelog", App.BaseUrl, JiraRequestType.Issue.ToString().ToLower(CultureInfo.InvariantCulture), key);
            try
            {
                HttpResponseMessage response = await httpManager.GetAsync(requestUrl, true, username, password);
                response.EnsureSuccessStatusCode();


                var responseStr = await response.Content.ReadAsStringAsync();
                issue = JsonConvert.DeserializeObject<Issue>(responseStr);

                var assignee = issue.Fields.Assignee;
                var reporter = issue.Fields.Reporter;
                var comments = issue.Fields.Comment.Comments;

                //Download assignee and reporter images if necessary
                if (assignee != null && assignee.AvatarUrls != null)
                    await DownloadImage(assignee.AvatarUrls.Size48, assignee.UserName);

                if (reporter != null && reporter.AvatarUrls != null)
                    await DownloadImage(reporter.AvatarUrls.Size48, reporter.UserName);

                //Download comment author images if necessary
                foreach (var comment in comments)
                {
                    if (comment.Author != null && comment.Author.AvatarUrls != null)
                        await DownloadImage(comment.Author.AvatarUrls.Size48, comment.Author.UserName);
                }
            }
            catch (Exception exception)
            {
                var extras = BugSenseHandler.Instance.CrashExtraData;
                extras.Add(new CrashExtraData
                {
                    Key = "Method",
                    Value = "JiraService.GetIssueByKey"
                });

                BugSenseHandler.Instance.LogException(exception, extras);
                dialogService.ShowDialog(AppResources.ErrorMessage, "Error");
            }
            return issue;
        }

        public async Task<ObservableCollection<Issue>> Search(string searchText, bool assignedToMe = false, bool reportedByMe = false, bool isFavourite = false, CancellationTokenSource tokenSource = null)
        {
            var fields = new List<string> { "summary", "status", "assignee", "reporter", "description", "issuetype", "priority", "comment", "project" };
            var expands = new List<string> { "changelog" };
            var url = string.Format("{0}{1}", App.BaseUrl, JiraRequestType.Search.ToString().ToLower());
            var jql = string.Empty;

            if (!isFavourite)
            {
                if (!string.IsNullOrEmpty(searchText))
                {
                    jql += string.Format("text ~ {0}", searchText);
                }
                if (assignedToMe)
                {
                    if (!string.IsNullOrEmpty(searchText))
                    {
                        jql += string.Format("{0} AND assignee = currentUser()", jql);
                    }
                    else
                    {
                        jql += "assignee = currentUser()";
                    }
                }
                if (reportedByMe)
                {
                    if (!string.IsNullOrEmpty(searchText))
                    {
                        jql += string.Format("{0} AND reporter = currentUser()", jql);
                    }
                    else
                    {
                        jql += "reporter = currentUser()";
                    }
                }
            }
            else
            {
                jql = searchText;
            }

            var maxSearchResultSetting = new IsolatedStorageProperty<int>(Settings.MaxSearchResult, 50);
            var request = new SearchRequest
            {
                Fields = fields,
                Expands = expands,
                JQL = jql,
                MaxResults = maxSearchResultSetting.Value,
                StartAt = 0
            };

            var extras = BugSenseHandler.Instance.CrashExtraData;
            string data = JsonConvert.SerializeObject(request);
            try
            {
                extras.Add(new CrashExtraData
                {
                    Key = "Method",
                    Value = "JiraService.Search"
                });

                var result = await httpManager.PostAsync(url, data, true, App.UserName, App.Password, tokenSource);
                result.EnsureSuccessStatusCode();

                var responseString = await result.Content.ReadAsStringAsync();
                if (string.IsNullOrEmpty(responseString)) return new ObservableCollection<Issue>();

                extras.Add(new CrashExtraData
                {
                    Key = "Response String",
                    Value = responseString
                });

                var response = JsonConvert.DeserializeObject<SearchResponse>(responseString);
                return new ObservableCollection<Issue>(response.Issues);
            }
            catch (Exception exception)
            {
                BugSenseHandler.Instance.LogException(exception, extras);
            }
            return null;
        }

        public async Task<ObservableCollection<Issue>> GetIssuesByProjectKey(string serverUrl, string username, string password, string key, bool withoutCache = false)
        {
            var cacheFileName = string.Format("IssuesByProjectKey.{0}.cache", key);
            var isCacheExist = cacheService.DoesFileExist(cacheFileName);
            if (!withoutCache && isCacheExist)
            {
                var issuesFile = cacheService.Read(cacheFileName);
                return JsonConvert.DeserializeObject<ObservableCollection<Issue>>(issuesFile);
            }

            string jql = "project = " + key;
            var issues = await GetIssues(jql);

            //Save issues to the cache
            if (issues != null)
                cacheService.Save(cacheFileName, JsonConvert.SerializeObject(issues));
            return issues;
        }

        public async Task<ObservableCollection<Issue>> GetIssues(string jql, List<string> fields = null, int startAt = 0, int maxResult = 50)
        {
            fields = fields ?? new List<string> { "summary", "status", "assignee", "reporter", "description", "issuetype", "priority", "comment", "attachment" };
            var requestUrl = string.Format("{0}{1}", App.BaseUrl, JiraRequestType.Search.ToString().ToLower());

            var request = new SearchRequest { Fields = fields, JQL = jql, MaxResults = maxResult, StartAt = startAt };            
            try
            {
                var searchResponse = await jsonClient.Post<SearchRequest, SearchResponse>(new Uri(requestUrl), request, App.UserName, App.Password, HttpStatusCode.OK, null);
                if (searchResponse == null) return null;
                return new ObservableCollection<Issue>(searchResponse.Issues);
            }
            catch (Exception exception)
            {
                var extras = BugSenseHandler.Instance.CrashExtraData;
                extras.Add(new CrashExtraData
                {
                    Key = "Method",
                    Value = "JiraService.GetIssues"
                });

                BugSenseHandler.Instance.LogException(exception, extras);
            }
            return null;
        }

        public async Task<ObservableCollection<IssueType>> GetIssueTypesOfProject(string projectKey)
        {
            var cacheFileName = string.Format("IssueTypesOfProject.{0}.cache", projectKey);

            //Check cache data file
            var isCacheExist = cacheService.DoesFileExist(cacheFileName);
            if (isCacheExist)
            {
                var issueTypesFile = cacheService.Read(cacheFileName);
                return JsonConvert.DeserializeObject<ObservableCollection<IssueType>>(issueTypesFile);
            }

            var requestUrl = string.Format("{0}{1}/createmeta?projectKeys={2}", App.BaseUrl, JiraRequestType.Issue.ToString().ToLower(CultureInfo.InvariantCulture), projectKey);
            ObservableCollection<IssueType> issueTypes = null;
            try
            {
                var result = await httpManager.GetAsync(requestUrl, true, App.UserName, App.Password);
                result.EnsureSuccessStatusCode();
                var responseString = await result.Content.ReadAsStringAsync();
                var responseObj = JsonConvert.DeserializeObject<IssueTypeResponse>(responseString);
                if (responseObj.Projects.Count > 0)
                {
                    var issueTypeList = responseObj.Projects[0].IssueTypes.Where(item => item.IsSubtask == false);
                    var types = issueTypeList as IssueType[] ?? issueTypeList.ToArray();
                    foreach (var issueType in types)
                    {
                        await DownloadImage(issueType.IconUrl, issueType.Name);
                    }
                    issueTypes = new ObservableCollection<IssueType>(types);

                    //Save new file to the cache
                    cacheService.Save(cacheFileName, JsonConvert.SerializeObject(issueTypes));
                }
            }
            catch (Exception exception)
            {
                var extras = BugSenseHandler.Instance.CrashExtraData;
                extras.Add(new CrashExtraData
                {
                    Key = "Method",
                    Value = "JiraService.GetIssueTypesOfProject"
                });

                BugSenseHandler.Instance.LogException(exception, extras);
            }
            return issueTypes;
        }

        public async Task<CreateIssueResponse> CreateIssue(CreateIssueRequest request)
        {
            var url = string.Format("{0}{1}/", App.BaseUrl, JiraRequestType.Issue.ToString().ToLower(CultureInfo.InvariantCulture));

            try
            {
                var result = await jsonClient.Post<CreateIssueRequest, CreateIssueResponse>(new Uri(url), request, App.UserName, App.Password, HttpStatusCode.Created, TokenSource);

                return result;
            }
            catch (Exception exception)
            {
                var extras = BugSenseHandler.Instance.CrashExtraData;
                extras.Add(new CrashExtraData
                {
                    Key = "Method",
                    Value = "JiraService.CreateIssue"
                });

                BugSenseHandler.Instance.LogException(exception, extras);
            }
            return null;
        }

        public async Task<ObservableCollection<Priority>> GetPriorities()
        {
            const string cacheFileName = "Priorities.cache";
            var isCacheExist = cacheService.DoesFileExist(cacheFileName);
            if (isCacheExist)
            {
                var prioritiesFile = cacheService.Read(cacheFileName);
                return JsonConvert.DeserializeObject<ObservableCollection<Priority>>(prioritiesFile);
            }

            var url = string.Format("{0}{1}", App.BaseUrl, JiraRequestType.Priority.ToString().ToLower());
            ObservableCollection<Priority> priorityList = null;
            try
            {
                var result = await httpManager.GetAsync(url, true, App.UserName, App.Password);
                result.EnsureSuccessStatusCode();
                var responseString = await result.Content.ReadAsStringAsync();
                priorityList = new ObservableCollection<Priority>(JsonConvert.DeserializeObject<List<Priority>>(responseString));

                foreach (var priority in priorityList)
                {
                    await DownloadImage(priority.IconUrl, priority.Name);
                }

                //Save new data to the cache
                cacheService.Save(cacheFileName, JsonConvert.SerializeObject(priorityList));

            }
            catch (Exception exception)
            {
                var extras = BugSenseHandler.Instance.CrashExtraData;
                extras.Add(new CrashExtraData
                {
                    Key = "Method",
                    Value = "JiraService.GetPriorities"
                });

                BugSenseHandler.Instance.LogException(exception, extras);
            }
            return priorityList;
        }

        public async Task<User> GetUserProfileAsync(string username)
        {
            var cacheFileName = string.Format("UserProfile.{0}.cache", username);

            //Check cache file 
            var isCacheExist = cacheService.DoesFileExist(cacheFileName);
            if (isCacheExist)
            {
                var userProfileFile = cacheService.Read(cacheFileName);
                return JsonConvert.DeserializeObject<User>(userProfileFile);
            }

            string url = string.Format("{0}user?username={1}", App.BaseUrl, username);
            var response = await httpManager.GetAsync(url, true, username, App.Password);
            response.EnsureSuccessStatusCode();
            User user = null;
            try
            {
                var responseStr = await response.Content.ReadAsStringAsync();
                user = JsonConvert.DeserializeObject<User>(responseStr);
                var userDisplayPictureUrl = user.AvatarUrls.Size48.Substring(0, user.AvatarUrls.Size48.Length - 2) + "183";

                await DownloadImage(userDisplayPictureUrl, user.UserName);
                App.User = user;

                //Save new data to the cache
                cacheService.Save(cacheFileName, responseStr);

            }
            catch (Exception exception)
            {
                var extras = BugSenseHandler.Instance.CrashExtraData;
                extras.Add(new CrashExtraData
                {
                    Key = "Method",
                    Value = "JiraService.GetUserProfileAsync"
                });

                BugSenseHandler.Instance.LogException(exception, extras);
            }
            return user;
        }

        public async Task<bool> AddComment(string issueKey, string comment)
        {
            HttpResponseMessage response = null;
            var requestUrl = string.Format("{0}{1}/{2}/comment", App.BaseUrl, JiraRequestType.Issue.ToString().ToLower(CultureInfo.InvariantCulture), issueKey);

            var commentRequest = new AddCommentRequest {Body = comment};

            var data = JsonConvert.SerializeObject(commentRequest);
            try
            {
                response = await httpManager.PostAsync(requestUrl, data, true, App.UserName, App.Password);
                response.EnsureSuccessStatusCode();
            }
            catch (Exception exception)
            {
                var extras = BugSenseHandler.Instance.CrashExtraData;
                extras.Add(new CrashExtraData
                {
                    Key = "Method",
                    Value = "JiraService.AddComment"
                });

                BugSenseHandler.Instance.LogException(exception, extras);
            }
            
// ReSharper disable once PossibleNullReferenceException
            if (response.StatusCode == HttpStatusCode.Created)
                return true;
            return false;
        }

        public byte[] GetDisplayPicture(string username)
        {
            try
            {
                using (var isf = IsolatedStorageFile.GetUserStoreForApplication())
                {
                    var path = Path.Combine("Images", username + ".png");
                    if (!isf.FileExists(path)) return null;

                    byte[] data;
                    using (var fs = isf.OpenFile(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        data = new byte[fs.Length];
                        if (data.Length == 0) return null;
                        fs.Read(data, 0, data.Length);
                    }
                    return data;
                }
            }
            catch (Exception exception)
            {
                var extras = BugSenseHandler.Instance.CrashExtraData;
                extras.Add(new CrashExtraData
                {
                    Key = "Method",
                    Value = "JiraService.GetDisplayPicture"
                });

                BugSenseHandler.Instance.LogException(exception, extras);
            }
            return null;
        }

        private async Task DownloadImage(string url, string filename, bool isProjectAvatar = false)
        {
            try
            {
                using (var isf = IsolatedStorageFile.GetUserStoreForApplication())
                {
                    if (!isf.DirectoryExists("Images"))
                    {
                        isf.CreateDirectory("Images");
                    }

                    var path = Path.Combine("Images", filename.Replace(":", ".").Replace(" ", "") + ".png");
                    if (isf.FileExists(path)) return;

                    var result = await httpManager.GetAsync(url, true, App.UserName, App.Password);
                    if (result.IsSuccessStatusCode)
                    {
                        var contentResult = await result.Content.ReadAsByteArrayAsync();

                        var image = new BitmapImage();
                        using (var stream = new MemoryStream(contentResult))
                        {
                            image.SetSource(stream);
                        }

                        var wb = new WriteableBitmap(image);

                        using (var fs = isf.CreateFile(path))
                        {
                            PngWriter.WritePNG(wb, fs);
                        }
                        if (isProjectAvatar)
                        {
                            CreateCustomeLiveTile(image, filename);
                        }
                    }
                }
            }
            catch (IsolatedStorageException storageException)
            {
                var extras = BugSenseHandler.Instance.CrashExtraData;
                extras.Add(new CrashExtraData
                {
                    Key = "Method",
                    Value = "JiraService.DownloadImage.IsolatedStorageException"
                });

                BugSenseHandler.Instance.LogException(storageException, extras);

            }
            catch (Exception exception)
            {
                var extras = BugSenseHandler.Instance.CrashExtraData;
                extras.Add(new CrashExtraData
                {
                    Key = "Method",
                    Value = "JiraService.DownloadImage"
                });

                BugSenseHandler.Instance.LogException(exception, extras);
            }
        }

        public async Task<ObservableCollection<Transition>> GetTransitions(string issueKey)
        {
            var url = string.Format("{0}issue/{1}/transitions", App.BaseUrl, issueKey);
            ObservableCollection<Transition> transitions = new ObservableCollection<Transition>();
            try
            {
                var result = await httpManager.GetAsync(url, true, App.UserName, App.Password);
                result.EnsureSuccessStatusCode();
                var responseString = await result.Content.ReadAsStringAsync();
                var transitionObject = JsonConvert.DeserializeObject<TransitionObject>(responseString);

                if (transitionObject != null)
                    transitions = new ObservableCollection<Transition>(transitionObject.Transitions);

                return transitions;
            }
            catch (Exception exception)
            {
                var extras = BugSenseHandler.Instance.CrashExtraData;
                extras.Add(new CrashExtraData
                {
                    Key = "Method",
                    Value = "JiraService.GetTransitions"
                });

                BugSenseHandler.Instance.LogException(exception, extras);
            }
            return transitions;
        }

        public async Task<bool> PerformTransition(string issueKey, string transitionId)
        {
            HttpResponseMessage response = null;
            var requestUrl = string.Format("{0}issue/{1}/transitions", App.BaseUrl, issueKey);

            var transitionRequest = new TransitionRequest
            {
                Transition = new Transition { Id = transitionId }
            };

            var data = JsonConvert.SerializeObject(transitionRequest);
            try
            {
                response = await httpManager.PostAsync(requestUrl, data, true, App.UserName, App.Password);
                response.EnsureSuccessStatusCode();
            }
            catch (Exception exception)
            {
                var extras = BugSenseHandler.Instance.CrashExtraData;
                extras.Add(new CrashExtraData
                {
                    Key = "Method",
                    Value = "JiraService.PerformTransition"
                });

                BugSenseHandler.Instance.LogException(exception, extras);
            }
           
// ReSharper disable once PossibleNullReferenceException
            if (response.StatusCode == HttpStatusCode.NoContent)
                return true;
            return false;
        }

        public async Task<ObservableCollection<Favourite>> GetFavourites()
        {
            var url = string.Format("{0}filter/favourite", App.BaseUrl);
            var favourites = new ObservableCollection<Favourite>();
            try
            {
                var result = await httpManager.GetAsync(url, true, App.UserName, App.Password);
                result.EnsureSuccessStatusCode();
                var responseString = await result.Content.ReadAsStringAsync();
                var favouriteObject = JsonConvert.DeserializeObject<List<Favourite>>(responseString);

                if (favouriteObject != null)
                    favourites = new ObservableCollection<Favourite>(favouriteObject);

                return favourites;
            }
            catch (Exception exception)
            {
                var extras = BugSenseHandler.Instance.CrashExtraData;
                extras.Add(new CrashExtraData
                {
                    Key = "Method",
                    Value = "JiraService.GetTransitions"
                });

                BugSenseHandler.Instance.LogException(exception, extras);
            }
            return favourites;
        }

        public async Task<bool> LogWork(string issueKey, string startedDate, string worked, string comment, CancellationTokenSource tokenSource = null)
        {
            OperationResult operationResult = null;
            var extras = BugSenseHandler.Instance.CrashExtraData;
            var requestUrl = string.Format("{0}issue/{1}/worklog", App.BaseUrl, issueKey);

            var logWorkRequest = new LogWorkRequest
            {
                Started = startedDate,
                TimeSpent = worked,
                Comment = comment
            };

            var data = JsonConvert.SerializeObject(logWorkRequest);
            try
            {
                operationResult = await jsonClient.PostData(new Uri(requestUrl), data, App.UserName, App.Password, HttpStatusCode.Created, tokenSource);

            }
            catch (Exception exception)
            {
                extras.Add(new CrashExtraData
                {
                    Key = "Method",
                    Value = "JiraService.LogWork"
                });
                BugSenseHandler.Instance.LogException(exception, extras);
            }

            // ReSharper disable once PossibleNullReferenceException
            return operationResult.IsValid;
        }

        public async Task<bool> DownloadAttachment(string fileUrl, string fileName)
        {
            HttpResponseMessage response = null;

            try
            {
                response = await httpManager.DownloadAttachment(fileUrl, true, App.UserName, App.Password);
                response.EnsureSuccessStatusCode();

                var data = await response.Content.ReadAsByteArrayAsync();
                await StorageHelper.WriteDataToIsolatedStorageFile(fileName, data);

                var local = Windows.ApplicationModel.Package.Current.InstalledLocation;
                var dataFolder = await local.GetFolderAsync("Attachments");
                var storageFile = await dataFolder.GetFileAsync(fileName);
                await Launcher.LaunchFileAsync(storageFile);
            }
            catch (Exception exception)
            {
                var extras = BugSenseHandler.Instance.CrashExtraData;
                extras.Add(new CrashExtraData
                {
                    Key = "Method",
                    Value = "JiraService.DownloadAttachment"
                });

                BugSenseHandler.Instance.LogException(exception, extras);
            }
          
// ReSharper disable once PossibleNullReferenceException
            if (response.StatusCode == HttpStatusCode.OK)
                return true;
            return false;
        }

        private void CreateCustomeLiveTile(BitmapImage imageSource, string projectKey)
        {
            var b = new WriteableBitmap(173, 173);

            var canvas = new Grid
            {
                Width = b.PixelWidth,
                Height = b.PixelHeight,
                Background = new SolidColorBrush(Colors.LightGray),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            var image = new Image
            {
                Height = 74,
                Width = 74,
                Margin = new Thickness(b.PixelWidth / 2 - 37, b.PixelHeight / 2 - 37, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Source = imageSource
            };
            canvas.Children.Add(image);

            b.Render(canvas, null);
            b.Invalidate();

            //Save bitmap as jpeg file in Isolated Storage
            const string imageFolder = @"\Shared\ShellContent";
            var fileName = string.Format("{0}.png", projectKey.Trim());
            using (var isf = IsolatedStorageFile.GetUserStoreForApplication())
            {
                using (var stream = isf.CreateFile(Path.Combine(imageFolder, fileName)))
                {
                    PngWriter.WritePNG(b, stream);
                }
            }
        }
    }
}
