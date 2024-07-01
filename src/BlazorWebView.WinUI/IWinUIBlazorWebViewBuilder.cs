using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.AspNetCore.Components.WebView.WinUI;

/// <summary>
/// A builder for WinUI Blazor WebViews.
/// </summary>
public interface IWinUIBlazorWebViewBuilder
{
    /// <summary>
    /// Gets the builder service collection.
    /// </summary>
    IServiceCollection Services { get; }
}
