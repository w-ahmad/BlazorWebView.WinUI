using Microsoft.AspNetCore.Components.WebView.WinUI;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods to <see cref="IServiceCollection"/>.
/// </summary>
public static class BlazorWebViewServiceCollectionExtensions
{
    /// <summary>
    /// Configures <see cref="IServiceCollection"/> to add support for <see cref="BlazorWebView"/>.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/>.</param>
    /// <returns>The <see cref="IServiceCollection"/>.</returns>
    public static IWinUIBlazorWebViewBuilder AddWinUIBlazorWebView(this IServiceCollection services)
    {
        services.AddBlazorWebView();
        services.TryAddSingleton(new BlazorWebViewDeveloperTools { Enabled = false });
        services.TryAddSingleton(_ => new WinUIBlazorMarkerService());
        return new WinUIBlazorWebViewBuilder(services);
    }

    /// <summary>
    /// Enables Developer tools on the underlying WebView controls.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/>.</param>
    /// <returns>The <see cref="IServiceCollection"/>.</returns>
    public static IServiceCollection AddBlazorWebViewDeveloperTools(this IServiceCollection services)
    {
        return services.AddSingleton(new BlazorWebViewDeveloperTools { Enabled = true });
    }
}
