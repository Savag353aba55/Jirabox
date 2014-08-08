﻿using GalaSoft.MvvmLight;
using GalaSoft.MvvmLight.Command;
using Jirabox.Common;
using Jirabox.Core.Contracts;
using Jirabox.Core.ExceptionExtension;
using Jirabox.Model;
using Jirabox.Resources;
using System;
using System.Threading;

namespace Jirabox.ViewModel
{
    public class LoginViewModel : ViewModelBase
    {
        private readonly INavigationService navigationService;
        private readonly IDialogService dialogService;
        private readonly IJiraService jiraService;

        private CancellationTokenSource cancellationTokenSource = null;
        private string serverUrl;
        private string userName;        
        private string password;
        private bool isDataLoaded;
        private bool isRememberMe;
        private bool loginButtonEnabled;

        public RelayCommand LoginCommand { get; private set; }
        public RelayCommand AboutCommand { get; private set; }   

        public string ServerUrl
        {
            get { return serverUrl; }
            set 
            {
                if (serverUrl != value)
                {
                    serverUrl = value;
                    App.ServerUrl = serverUrl;
                    RaisePropertyChanged(() => ServerUrl);
                }                
            }
        }

        public bool LoginButtonEnabled
        {
            get
            {
                return loginButtonEnabled;
            }
            set
            {
                if (loginButtonEnabled != value)
                {
                    loginButtonEnabled = value;
                    RaisePropertyChanged(() => LoginButtonEnabled);
                }
            }
        }

        public string UserName
        {
            get { return userName; }
            set 
            {
                if (userName != value)
                {
                    userName = value;
                    App.UserName = userName;
                    RaisePropertyChanged(() => UserName);
                }
                
            }
        }

        public string Password
        {
            get { return password; }
            set
            {
                if (password != value)
                {
                    password = value;
                    App.Password = password;
                    RaisePropertyChanged(() => Password);
                }

            }
        }        

        public bool IsDataLoaded
        {
            get { return isDataLoaded; }
            set 
            {
                if (value != isDataLoaded)
                {
                    isDataLoaded = value;
                    RaisePropertyChanged(() => IsDataLoaded);
                }
            }
        }

        public bool IsRememberMe
        {
            get { return isRememberMe; }
            set
            {
                if (value != isRememberMe)
                {
                    isRememberMe = value;
                    RaisePropertyChanged(() => IsRememberMe);
                }
            }
        }
        
        public LoginViewModel(INavigationService navigationService, IDialogService dialogService, IJiraService jiraService)
        {            
            this.navigationService = navigationService;
            this.dialogService = dialogService;
            this.jiraService = jiraService;         
   
            InitializeData();
        }

        public async void Login()
        {
            IsDataLoaded = false;
            LoginButtonEnabled = false;
            if (!IsInputsValid())
            {
                IsDataLoaded = true;
                dialogService.ShowDialog(AppResources.UnauthorizedMessage, AppResources.LoginFailedMessage);
                return;
            }

            if (!ValidateUrl(ServerUrl))
            {
                IsDataLoaded = true;
                dialogService.ShowDialog(AppResources.InvalidServerUrlMessage, AppResources.LoginFailedMessage);                
                return;
            }
            try
            {
                //Create new cancellation token source
                cancellationTokenSource = new CancellationTokenSource();

                //Set base url for rest api
                App.BaseUrl = string.Format("{0}/rest/api/latest/", App.ServerUrl);
                var isLoginSuccess = await jiraService.LoginAsync(ServerUrl, UserName, Password, cancellationTokenSource);
                if (isLoginSuccess)
                {
                    if (IsRememberMe)
                        StorageHelper.SaveUserCredential(ServerUrl, UserName, Password);
                    else
                        StorageHelper.ClearUserCredential();

                    navigationService.Navigate<ProjectListViewModel>();                    
                }
            }         
            catch (HttpRequestStatusCodeException exception)
            {
                if (exception.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    dialogService.ShowDialog(AppResources.UnauthorizedMessage, AppResources.LoginFailedMessage);
                }
                else if (exception.StatusCode == System.Net.HttpStatusCode.Forbidden)
                {
                    dialogService.ShowDialog(AppResources.ForbiddenMessage, AppResources.LoginFailedMessage);
                }
                else
                {
                    dialogService.ShowDialog(AppResources.ConnectionErrorMessage, AppResources.LoginFailedMessage);
                }
            }
            catch (Exception ex)
            {
                dialogService.ShowDialog(string.Format(AppResources.FormattedErrorMessage, ex.Message), AppResources.LoginFailedMessage);
            }
         
           IsDataLoaded = true;
           LoginButtonEnabled = true;
        }
        public bool ValidateUrl(string url)
        {
            Uri uri;
            return Uri.TryCreate(url, UriKind.Absolute, out uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
        }

        private bool IsInputsValid()
        {
            if (string.IsNullOrEmpty(UserName) || string.IsNullOrEmpty(Password))
                return false;
            return true;            
        }

        private void InitializeData()
        {
            LoginCommand = new RelayCommand(Login);
            AboutCommand = new RelayCommand(NavigateToAboutView);
            
            IsDataLoaded = true;
            LoginButtonEnabled = true;
            var credential = StorageHelper.GetUserCredential();
            if (credential != null)
            {
                ServerUrl = credential.ServerUrl;
                UserName = credential.UserName;
                Password = credential.Password;
                IsRememberMe = true;
                Login();
            }
        }

        private void NavigateToAboutView()
        {
            navigationService.Navigate<AboutViewModel>();
        }

        public void CancelLogin()
        {
            if(cancellationTokenSource != null)
            cancellationTokenSource.Cancel();
        }

        public void ClearFields()
        {
            ServerUrl = string.Empty;
            UserName = string.Empty;
            Password = string.Empty;
            IsRememberMe = false;
        }

        public void RemoveBackEntry()
        {
            navigationService.RemoveBackEntry();
        }
    }
}
