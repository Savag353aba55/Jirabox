﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Jirabox.Core.Contracts;
using Jirabox.Model;
using Jirabox.ViewModel;
using Microsoft.Phone.Controls;
using Microsoft.VisualStudio.TestPlatform.UnitTestFramework;
using Moq;

namespace Jirabox.Tests.ViewModel
{
    [TestClass]
    public class ProjectListViewModelTests
    {
        private Mock<INavigationService> navigationServiceMock;
        private Mock<IDialogService> dialogServiceMock;
        private Mock<IJiraService> jiraServiceMock;
        private Mock<ICacheService> cacheServiceMock;
        private ProjectListViewModel projectListViewModel;

        [TestInitialize]
        public void Initialize()
        {
            dialogServiceMock = new Mock<IDialogService>();
            jiraServiceMock = new Mock<IJiraService>();
            navigationServiceMock = new Mock<INavigationService>();
            cacheServiceMock = new Mock<ICacheService>();
            
            projectListViewModel = new ProjectListViewModel(navigationServiceMock.Object, dialogServiceMock.Object, jiraServiceMock.Object, cacheServiceMock.Object);
            SynchronizationContext.SetSynchronizationContext(new SynchronizationContext());
        }

        [TestMethod]
        public void InitializeData_OnNavigated_ReturnsProjects()
        {
            //Arrange
            var projectList = new List<Project>
            {
                new Project { Key = "ALM", Name = "Application Lifecycle Management" }
            };

            jiraServiceMock.Setup(x => x.GetProjects(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), false)).Returns(Task.FromResult(new ObservableCollection<Project>(projectList)));
            jiraServiceMock.Setup(x => x.GetUserProfileAsync(It.IsAny<string>())).Returns(Task.FromResult(new User()));
            jiraServiceMock.Setup(x => x.GetDisplayPicture(It.IsAny<string>())).Verifiable();
            jiraServiceMock.Setup(x => x.GetFavourites()).Returns(Task.FromResult(new ObservableCollection<Favourite>()));

            //Act
            var waitHandle = new AutoResetEvent(false);
            Deployment.Current.Dispatcher.BeginInvoke(async () =>
            {
                await projectListViewModel.InitializeData();
                waitHandle.Set();
            });
            waitHandle.WaitOne(TimeSpan.FromSeconds(1000));

            //Assert
            Assert.IsTrue(projectListViewModel.FlatProjects.Count == 1);
            Assert.IsTrue(projectListViewModel.Favourites.Count == 0);
            
        }

        [TestMethod]
        public void When_LogoutCommand_Executed_NavigatesToLoginPage()
        {
            //Arrange 
            CustomMessageBox customMessageBox = null;
            Deployment.Current.Dispatcher.BeginInvoke(() => customMessageBox = new CustomMessageBox());
            Thread.Sleep(500);
            navigationServiceMock.Setup(x => x.Navigate<LoginViewModel>(It.IsAny<object>())).Verifiable();
            dialogServiceMock.Setup(x => x.ShowPromptDialog(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).Returns(() =>
                {
                    navigationServiceMock.Object.Navigate<LoginViewModel>(null);                    
                    return customMessageBox;
                });

            //Act
            projectListViewModel.LogoutCommand.Execute(null);

            //Assert
            navigationServiceMock.Verify(x => x.Navigate<LoginViewModel>(null));

        }

        [TestMethod]
        public void RemoveBackEntry_ShouldRemoveHistory()
        {
            //Arrange
            navigationServiceMock.Setup(x => x.RemoveBackEntry()).Verifiable();

            //Act
            projectListViewModel.RemoveBackEntry();

            //Assert
            navigationServiceMock.Verify(x => x.RemoveBackEntry());
        }

        [TestMethod]
        public void When_ShowUserInformationCommand_Executed_NavigatesToUserProfileViewModel()
        {
            //Arrange
            navigationServiceMock.Setup(x => x.Navigate<UserProfileViewModel>(null)).Verifiable();

            //Act
            projectListViewModel.ShowUserInformationCommand.Execute(null);

            //Assert
            navigationServiceMock.Verify(x=>x.Navigate<UserProfileViewModel>(It.IsAny<object>()));
        }

        [TestMethod]
        public void When_ShowSettingsCommand_Executed_NavigatesToSettingsViewModel()
        {
            //Arrange
            navigationServiceMock.Setup(x => x.Navigate<SettingsViewModel>(null)).Verifiable();

            //Act
            projectListViewModel.ShowSettingsCommand.Execute(null);

            //Assert
            navigationServiceMock.Verify(x => x.Navigate<SettingsViewModel>(It.IsAny<object>()));
        }

        [TestMethod]
        public void When_ShowAboutViewCommand_Executed_NavigatesToAboutViewModel()
        {
            //Arrange
            navigationServiceMock.Setup(x => x.Navigate<AboutViewModel>(null)).Verifiable();

            //Act
            projectListViewModel.ShowAboutViewCommand.Execute(null);

            //Assert
            navigationServiceMock.Verify(x => x.Navigate<AboutViewModel>(It.IsAny<object>()));
        }

        [TestMethod]
        public void When_ShowAssignedIssuesCommand_Executed_NavigatesToSearchResultViewModel()
        {
            //Arrange
            navigationServiceMock.Setup(x => x.Navigate<SearchResultViewModel>(null)).Verifiable();

            //Act
            projectListViewModel.ShowAssignedIssuesCommand.Execute(null);

            //Assert
            navigationServiceMock.Verify(x => x.Navigate<SearchResultViewModel>(It.IsAny<object>()));
        }

        [TestMethod]
        public void When_ShowIssuesReportedByMeCommand_Executed_NavigatesToSearchResultViewModel()
        {
            //Arrange
            navigationServiceMock.Setup(x => x.Navigate<SearchResultViewModel>(null)).Verifiable();

            //Act
            projectListViewModel.ShowIssuesReportedByMeCommand.Execute(null);

            //Assert
            navigationServiceMock.Verify(x => x.Navigate<SearchResultViewModel>(It.IsAny<object>()));
        }

        [TestMethod]
        public void When_ProjectDetailCommand_Executed_NavigatesToProjectDetailViewModel()
        {
            //Arrange
            navigationServiceMock.Setup(x => x.Navigate<ProjectDetailViewModel>(It.IsAny<string>())).Verifiable();

            //Act
            projectListViewModel.ProjectDetailCommand.Execute(new Project());

            //Assert
            navigationServiceMock.Verify(x => x.Navigate<ProjectDetailViewModel>(It.IsAny<string>()));
        }

        [TestMethod]
        public void When_SearchCommand_Executed_NavigatesToSearchResultViewModel()
        {
            //Arrange
            navigationServiceMock.Setup(x => x.Navigate<SearchResultViewModel>(null)).Verifiable();

            //Act
            projectListViewModel.SearchCommand.Execute(null);

            //Assert
            navigationServiceMock.Verify(x => x.Navigate<SearchResultViewModel>(It.IsAny<object>()));
        }

        [TestMethod]
        public void When_SearchWithFavouriteCommand_Executed_NavigatesToSearchResultViewModel()
        {
            //Arrange
            navigationServiceMock.Setup(x => x.Navigate<SearchResultViewModel>(null)).Verifiable();

            //Act
            projectListViewModel.SearchWithFavouriteCommand.Execute(new Favourite());

            //Assert
            navigationServiceMock.Verify(x => x.Navigate<SearchResultViewModel>(It.IsAny<object>()));
        }
    }
}
