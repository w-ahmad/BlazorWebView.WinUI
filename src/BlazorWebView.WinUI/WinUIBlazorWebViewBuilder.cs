using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.AspNetCore.Components.WebView.WinUI;

internal class WinUIBlazorWebViewBuilder : IWinUIBlazorWebViewBuilder
{
    public IServiceCollection Services { get; }

    public WinUIBlazorWebViewBuilder(IServiceCollection services)
    {
        Services = services;
    }
}
