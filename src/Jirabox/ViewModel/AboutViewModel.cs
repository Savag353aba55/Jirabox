﻿using GalaSoft.MvvmLight;
using GalaSoft.MvvmLight.Command;
using Jirabox.Resources;
using Microsoft.Phone.Tasks;
using System;
using System.Reflection;

namespace Jirabox.ViewModel
{
    public class AboutViewModel : ViewModelBase
    {
        private string version;

        public RelayCommand EmailCommand{ get; private set; }        
        public RelayCommand ReviewCommand { get; private set; }
        public RelayCommand FeedbackCommand { get; private set; }        

        public string Version
        {
            get { return version; }
            set
            {
                if (version != value)
                {
                    version = value;
                    RaisePropertyChanged(() => Version);
                }
            }
        }

        public AboutViewModel()
        {
            EmailCommand = new RelayCommand(SendEmail);           
            ReviewCommand = new RelayCommand(ReviewThisApp);
            FeedbackCommand = new RelayCommand(Feedback);            

            var app = new AssemblyName(Assembly.GetExecutingAssembly().FullName);
            Version = app.Version.ToString();
        }
     
        private void Feedback()
        {
            var webBrowserTask = new WebBrowserTask {Uri = new Uri(AppResources.UserVoiceUrl)};
            webBrowserTask.Show(); 
        }
        private void SendEmail()
        {
            var emailComposeTask = new EmailComposeTask {To = AppResources.ApplicationEmail};
            emailComposeTask.Show();
        }

        private void ReviewThisApp()
        {
            var marketplaceReviewTask = new MarketplaceReviewTask();
            marketplaceReviewTask.Show();
        }

    }
}
