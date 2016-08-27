﻿using GalaSoft.MvvmLight;
using GalaSoft.MvvmLight.Command;
using Jirabox.Core.Contracts;
using Jirabox.Model;
using System.Collections.ObjectModel;
using System.Threading;

namespace Jirabox.ViewModel
{
    public class SearchResultViewModel : ViewModelBase
    {
        private readonly IJiraService jiraService;
        private readonly INavigationService navigationService;
        private bool isDataLoaded;
        private CancellationTokenSource cancellationTokenSource;

        public RelayCommand<Issue> ShowIssueDetailCommand { get; private set; }

        public bool IsDataLoaded
        {
            get { return isDataLoaded; }
            set
            {
                if (isDataLoaded != value)
                {
                    isDataLoaded = value;
                    RaisePropertyChanged(() => IsDataLoaded);
                }
            }
        }

        private ObservableCollection<Issue> issues;

        public ObservableCollection<Issue> Issues
        {
            get { return issues; }
            set
            {
                if (issues != value)
                {
                    issues = value;
                    RaisePropertyChanged(() => Issues);
                }
            }
        }       

        public SearchResultViewModel(IJiraService jiraService, INavigationService navigationService)
        {
            this.jiraService = jiraService;
            this.navigationService = navigationService;
            ShowIssueDetailCommand = new RelayCommand<Issue>(NavigateToIssueDetailView, issue => issue != null);
        }

        public async void Initialize()
        {
            IsDataLoaded = false;
            var searchParameter = (SearchParameter)navigationService.GetNavigationParameter();
            if (searchParameter != null)
            {
                cancellationTokenSource = new CancellationTokenSource();
                var searchText = searchParameter.IsFavourite ? searchParameter.JQL : searchParameter.SearchText;
                Issues = await jiraService.Search(searchText, searchParameter.IsAssignedToMe, searchParameter.IsReportedByMe, searchParameter.IsFavourite, cancellationTokenSource);
            }           
            IsDataLoaded = true;
        }

        public void CleanUp()
        {
            Issues = null;
        }

        public void CancelSearch()
        {
            if(cancellationTokenSource != null)
            cancellationTokenSource.Cancel();
        }

        public void SetNavigationToAssignedIssues()
        {
            var searchCriteria = new SearchParameter { IsAssignedToMe = true };
            navigationService.NavigationParameter = searchCriteria;
        }

        public void SetNavigationToIssuesReportedByMe()
        {
            var searchCriteria = new SearchParameter { IsReportedByMe = true };
            navigationService.NavigationParameter = searchCriteria;
        }

        public void SetNavigationSearchText(string searchText)
        {            
            var searchCriteria = new SearchParameter { SearchText = searchText };
            navigationService.NavigationParameter = searchCriteria;
        }

        public void NavigateToLoginView()
        {
            navigationService.Navigate<LoginViewModel>();
        }

        private void NavigateToIssueDetailView(Issue selectedIssue)
        {
            navigationService.Navigate<IssueDetailViewModel>(selectedIssue.ProxyKey);
        }
    }
}
