using System;
using WebView2Control = Microsoft.UI.Xaml.Controls.WebView2;

namespace Microsoft.AspNetCore.Components.WebView;

/// <summary>
/// Allows configuring the underlying web view after it has been initialized.
/// </summary>
public class BlazorWebViewInitializedEventArgs : EventArgs
{
#nullable disable
    /// <summary>
    /// Gets the <see cref="WebView2Control"/> instance that was initialized.
    /// </summary>
    public WebView2Control WebView { get; internal set; }
}
