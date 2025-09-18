using System;
using Microsoft.Web.WebView2.Core;
using WebView2Control = Microsoft.UI.Xaml.Controls.WebView2;

namespace Microsoft.AspNetCore.Components.WebView;

/// <summary>
/// Allows configuring the underlying web view when the application is initializing.
/// </summary>
public class BlazorWebViewInitializingEventArgs : EventArgs
{
#nullable disable
    /// <summary>
    /// Gets or sets the browser executable folder path for the <see cref="WebView2Control"/>.
    /// </summary>
    public string BrowserExecutableFolder { get; set; }

    /// <summary>
    /// Gets or sets the user data folder path for the <see cref="WebView2Control"/>.
    /// </summary>
    public string UserDataFolder { get; set; }

    /// <summary>
    /// Gets or sets the environment options for the <see cref="WebView2Control"/>.
    /// </summary>
    public CoreWebView2EnvironmentOptions EnvironmentOptions { get; set; }
}
