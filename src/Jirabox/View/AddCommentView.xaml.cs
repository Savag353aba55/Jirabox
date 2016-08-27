﻿using System.Windows.Navigation;
using Jirabox.ViewModel;
using Microsoft.Phone.Controls;

namespace Jirabox.View
{
    public partial class AddCommentView : PhoneApplicationPage
    {
        public AddCommentView()
        {
            InitializeComponent();
        }
        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            var vm = this.DataContext as AddCommentViewModel;
            if (NavigationContext.QueryString.ContainsKey("param"))
            {
                var issueKey = NavigationContext.QueryString["param"];
                vm.CleanUp();
                vm.Initialize(issueKey);                
            }
        }    
    }
}