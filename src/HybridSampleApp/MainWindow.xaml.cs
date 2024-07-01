using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace HybridSampleApp
{
    /// <summary>
    /// An empty window that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            var serviceCollection = new ServiceCollection();
            serviceCollection.AddWinUIBlazorWebView();
            serviceCollection.AddBlazorWebViewDeveloperTools();

            blazorWebView.Services = serviceCollection.BuildServiceProvider();
        }

        private void NavigationView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if(sender.SelectedItem is NavigationViewItem navItem && navItem.Content is string page)
            {
                switch (page)
                {
                    case "Counter":
                        blazorWebView.Navigate("/Counter");
                        break;
                    case "Weather":
                        blazorWebView.Navigate("/Weather");
                        break;
                    case "Settings":
                        blazorWebView.Navigate("/Settings");
                        break;
                    default:
                        blazorWebView.Navigate("/");
                        break;
                }
            }
        }
    }
}
