using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using Microsoft.AspNetCore.Components.WebView.WinUI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Web.WebView2.Core;
using WebView2Control = Microsoft.UI.Xaml.Controls.WebView2;
using System.Reflection;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.ApplicationModel;
using Windows.Storage.Streams;

namespace Microsoft.AspNetCore.Components.WebView.WebView2;

/// <summary>
/// An implementation of <see cref="WebViewManager"/> that uses the Edge WebView2 browser control
/// to render web content.
/// </summary>
internal class WebView2WebViewManager : WebViewManager
{
    // Using an IP address means that WebView2 doesn't wait for any DNS resolution,
    // making it substantially faster. Note that this isn't real HTTP traffic, since
    // we intercept all the requests within this origin.
    internal static readonly string AppHostAddress = "0.0.0.0";

    /// <summary>
    /// Gets the application's base URI. Defaults to <c>https://0.0.0.0/</c>
    /// </summary>
    protected static readonly string AppOrigin = $"https://{AppHostAddress}/";

    internal static readonly Uri AppOriginUri = new(AppOrigin);
    private readonly ILogger _logger;
    private readonly WebView2Control _webview;
    private readonly string _hostPageRelativePath;
    private readonly Task<bool> _webviewReadyTask;
    private readonly string _contentRootRelativeToAppRoot;
    private protected CoreWebView2Environment? _coreWebView2Environment;
    private readonly Action<UrlLoadingEventArgs> _urlLoading;
    private readonly Action<BlazorWebViewInitializingEventArgs> _blazorWebViewInitializing;
    private readonly Action<BlazorWebViewInitializedEventArgs> _blazorWebViewInitialized;
    private readonly BlazorWebViewDeveloperTools _developerTools;
    private static readonly bool _isPackagedApp;

    static WebView2WebViewManager()
    {
        try
        {
            _isPackagedApp = Package.Current != null;
        }
        catch
        {
            _isPackagedApp = false;
        }
    }

    /// <summary>
    /// Constructs an instance of <see cref="WebView2WebViewManager"/>.
    /// </summary>
    /// <param name="webview">A <see cref="WebView2Control"/> to access platform-specific WebView2 APIs.</param>
    /// <param name="services">A service provider containing services to be used by this class and also by application code.</param>
    /// <param name="dispatcher">A <see cref="Dispatcher"/> instance that can marshal calls to the required thread or sync context.</param>
    /// <param name="fileProvider">Provides static content to the webview.</param>
    /// <param name="jsComponents">Describes configuration for adding, removing, and updating root components from JavaScript code.</param>
    /// <param name="contentRootRelativeToAppRoot">Path to the app's content root relative to the application root directory.</param>
    /// <param name="hostPagePathWithinFileProvider">Path to the host page within the <paramref name="fileProvider"/>.</param>
    /// <param name="urlLoading">Callback invoked when a url is about to load.</param>
    /// <param name="blazorWebViewInitializing">Callback invoked before the webview is initialized.</param>
    /// <param name="blazorWebViewInitialized">Callback invoked after the webview is initialized.</param>
    /// <param name="logger">Logger to send log messages to.</param>
    internal WebView2WebViewManager(
        WebView2Control webview,
        IServiceProvider services,
        Dispatcher dispatcher,
        IFileProvider fileProvider,
        JSComponentConfigurationStore jsComponents,
        string contentRootRelativeToAppRoot,
        string hostPagePathWithinFileProvider,
        Action<UrlLoadingEventArgs> urlLoading,
        Action<BlazorWebViewInitializingEventArgs> blazorWebViewInitializing,
        Action<BlazorWebViewInitializedEventArgs> blazorWebViewInitialized,
        ILogger logger)
        : base(services, dispatcher, AppOriginUri, fileProvider, jsComponents, hostPagePathWithinFileProvider)

    {
        ArgumentNullException.ThrowIfNull(webview);

        if (services.GetService<WinUIBlazorMarkerService>() is null)
        {
            throw new InvalidOperationException(
                "Unable to find the required services. " +
                $"Please add all the required services by calling '{nameof(IServiceCollection)}.{nameof(BlazorWebViewServiceCollectionExtensions.AddWinUIBlazorWebView)}' in the application startup code.");
        }

        _logger = logger;
        _webview = webview;
        _hostPageRelativePath = hostPagePathWithinFileProvider;
        _urlLoading = urlLoading;
        _blazorWebViewInitializing = blazorWebViewInitializing;
        _blazorWebViewInitialized = blazorWebViewInitialized;
        _developerTools = services.GetRequiredService<BlazorWebViewDeveloperTools>();
        _contentRootRelativeToAppRoot = contentRootRelativeToAppRoot;

        // Unfortunately the CoreWebView2 can only be instantiated asynchronously.
        // We want the external API to behave as if initialization is synchronous,
        // so keep track of a task we can await during LoadUri.
        _webviewReadyTask = TryInitializeWebView2();
    }

    /// <inheritdoc />
    protected override void NavigateCore(Uri absoluteUri)
    {
        _ = Dispatcher.InvokeAsync(async () =>
        {
            var isWebviewInitialized = await _webviewReadyTask;

            if (isWebviewInitialized)
            {
                _logger.NavigatingToUri(absoluteUri);
                _webview.Source = absoluteUri;
            }
        });
    }

    /// <inheritdoc />
    protected override void SendMessage(string message)
    {
        _webview.CoreWebView2.PostWebMessageAsString(message);
    }

    private async Task<bool> TryInitializeWebView2()
    {
        var args = new BlazorWebViewInitializingEventArgs();
        _blazorWebViewInitializing?.Invoke(args);
        var userDataFolder = args.UserDataFolder ?? GetWebView2UserDataFolder();
        _coreWebView2Environment = await CoreWebView2Environment.CreateWithOptionsAsync(
             browserExecutableFolder: args.BrowserExecutableFolder,
             userDataFolder: userDataFolder,
             options: args.EnvironmentOptions);

        _logger.StartingWebView2();
        await _webview.EnsureCoreWebView2Async();
        _logger.StartedWebView2();

        var developerTools = _developerTools;

        ApplyDefaultWebViewSettings(developerTools);
        _blazorWebViewInitialized?.Invoke(new BlazorWebViewInitializedEventArgs
        {
            WebView = _webview,
        });

        _webview.CoreWebView2.AddWebResourceRequestedFilter($"{AppOrigin}*", CoreWebView2WebResourceContext.All);

        _webview.CoreWebView2.WebResourceRequested += async (s, eventArgs) => await HandleWebResourceRequest(eventArgs);

        _webview.CoreWebView2.NavigationStarting += CoreWebView2_NavigationStarting;
        _webview.CoreWebView2.NewWindowRequested += CoreWebView2_NewWindowRequested;

        // The code inside blazor.webview.js is meant to be agnostic to specific webview technologies,
        // so the following is an adaptor from blazor.webview.js conventions to WebView2 APIs
        await _webview.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(@"
				window.external = {
					sendMessage: message => {
						window.chrome.webview.postMessage(message);
					},
					receiveMessage: callback => {
						window.chrome.webview.addEventListener('message', e => callback(e.data));
					}
				};
			");

        QueueBlazorStart();

        _webview.CoreWebView2.WebMessageReceived += (s, e) => MessageReceived(new Uri(e.Source), e.TryGetWebMessageAsString());

        return true;
    }

    /// <summary>
    /// Handles outbound URL requests.
    /// </summary>
    /// <param name="eventArgs">The <see cref="CoreWebView2WebResourceRequestedEventArgs"/>.</param>
    protected virtual async Task HandleWebResourceRequest(CoreWebView2WebResourceRequestedEventArgs eventArgs)
    {
        // Unlike server-side code, we get told exactly why the browser is making the request,
        // so we can be smarter about fallback. We can ensure that 'fetch' requests never result
        // in fallback, for example.
        var allowFallbackOnHostPage =
            eventArgs.ResourceContext is CoreWebView2WebResourceContext.Document or
            CoreWebView2WebResourceContext.Other; // e.g., dev tools requesting page source

        // Get a deferral object so that WebView2 knows there's some async stuff going on. We call Complete() at the end of this method.
        using var deferral = eventArgs.GetDeferral();

        var requestUri = QueryStringHelper.RemovePossibleQueryString(eventArgs.Request.Uri);

        _logger.HandlingWebRequest(requestUri);

        var uri = new Uri(requestUri);
        var relativePath = AppOriginUri.IsBaseOf(uri) ? AppOriginUri.MakeRelativeUri(uri).ToString() : null;

        // Check if the uri is _framework/blazor.modules.json is a special case as the built-in file provider
        // brings in a default implementation.
        if (relativePath != null &&
            string.Equals(relativePath, "_framework/blazor.modules.json", StringComparison.Ordinal) &&
            await TryServeFromFolderAsync(eventArgs, allowFallbackOnHostPage: false, requestUri, relativePath))
        {
            _logger.ResponseContentBeingSent(requestUri, 200);
        }
        else if (TryGetResponseContent(requestUri, allowFallbackOnHostPage, out var statusCode, out var statusMessage, out var content, out var headers)
            && statusCode != 404)
        {
            // First, call into WebViewManager to see if it has a framework file for this request. It will
            // fall back to an IFileProvider, but on WinUI it's always a NullFileProvider, so that will never
            // return a file.
            var headerString = GetHeaderString(headers);
            _logger.ResponseContentBeingSent(requestUri, statusCode);
            eventArgs.Response = _coreWebView2Environment!.CreateWebResourceResponse(content.AsRandomAccessStream(), statusCode, statusMessage, headerString);
        }
        else if (relativePath != null)
        {
            await TryServeFromFolderAsync(
                eventArgs,
                allowFallbackOnHostPage,
                requestUri,
                relativePath);
        }

        // Notify WebView2 that the deferred (async) operation is complete and we set a response.
        deferral.Complete();
    }

    private async Task<bool> TryServeFromFolderAsync(
            CoreWebView2WebResourceRequestedEventArgs eventArgs,
            bool allowFallbackOnHostPage,
            string requestUri,
            string relativePath)
    {
        // If the path does not end in a file extension (or is empty), it's most likely referring to a page,
        // in which case we should allow falling back on the host page.
        if (allowFallbackOnHostPage && !Path.HasExtension(relativePath))
        {
            relativePath = _hostPageRelativePath;
        }
        relativePath = Path.Combine(_contentRootRelativeToAppRoot, relativePath.Replace('/', '\\'));
        var statusCode = 200;
        var statusMessage = "OK";
        var contentType = StaticContentProvider.GetResponseContentTypeOrDefault(relativePath);
        var headers = StaticContentProvider.GetResponseHeaders(contentType);
        IRandomAccessStream? stream = null;
        if (_isPackagedApp)
        {
            var winUIItem = await Package.Current.InstalledLocation.TryGetItemAsync(relativePath);
            if (winUIItem != null)
            {
                using var contentStream = await Package.Current.InstalledLocation.OpenStreamForReadAsync(relativePath);
                stream = await CopyContentToRandomAccessStreamAsync(contentStream);
            }
        }
        else
        {
            var path = Path.Combine(AppContext.BaseDirectory, relativePath);
            if (File.Exists(path))
            {
                using var contentStream = File.OpenRead(path);
                stream = await CopyContentToRandomAccessStreamAsync(contentStream);
            }
        }

        var hotReloadedContent = Stream.Null;
        if (StaticContentHotReloadManager.TryReplaceResponseContent(_contentRootRelativeToAppRoot, requestUri, ref statusCode, ref hotReloadedContent, headers))
        {
            stream = await CopyContentToRandomAccessStreamAsync(hotReloadedContent);
        }

        if (stream != null)
        {
            var headerString = GetHeaderString(headers);

            _logger.ResponseContentBeingSent(requestUri, statusCode);

            eventArgs.Response = _coreWebView2Environment!.CreateWebResourceResponse(
                stream,
                statusCode,
                statusMessage,
                headerString);

            return true;
        }
        else
        {
            _logger.ResponseContentNotFound(requestUri);
        }

        return false;

        async Task<IRandomAccessStream> CopyContentToRandomAccessStreamAsync(Stream content)
        {
            using var memStream = new MemoryStream();
            await content.CopyToAsync(memStream);
            var randomAccessStream = new InMemoryRandomAccessStream();
            await randomAccessStream.WriteAsync(memStream.GetWindowsRuntimeBuffer());
            return randomAccessStream;
        }
    }

    /// <summary>
    /// Override this method to queue a call to Blazor.start(). Not all platforms require this.
    /// </summary>
    protected virtual void QueueBlazorStart()
    {
    }

    private void CoreWebView2_NavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs args)
    {
        if (Uri.TryCreate(args.Uri, UriKind.RelativeOrAbsolute, out var uri))
        {
            var callbackArgs = UrlLoadingEventArgs.CreateWithDefaultLoadingStrategy(uri, AppOriginUri);
            _urlLoading?.Invoke(callbackArgs);
            _logger.NavigationEvent(uri, callbackArgs.UrlLoadingStrategy);

            if (callbackArgs.UrlLoadingStrategy == UrlLoadingStrategy.OpenExternally)
            {
                LaunchUriInExternalBrowser(uri);
            }

            args.Cancel = callbackArgs.UrlLoadingStrategy != UrlLoadingStrategy.OpenInWebView;
        }
    }

    private void CoreWebView2_NewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs args)
    {
        // Intercept _blank target <a> tags to always open in device browser.
        // The ExternalLinkCallback is not invoked.
        if (Uri.TryCreate(args.Uri, UriKind.RelativeOrAbsolute, out var uri))
        {
            LaunchUriInExternalBrowser(uri);
            args.Handled = true;
        }
    }

    private void LaunchUriInExternalBrowser(Uri uri)
    {
        _logger.LaunchExternalBrowser(uri);
        using var launchBrowser = new Process();
        launchBrowser.StartInfo.UseShellExecute = true;
        launchBrowser.StartInfo.FileName = uri.ToString();
        launchBrowser.Start();
    }

    private protected static string GetHeaderString(IDictionary<string, string> headers)
    {
        return string.Join(Environment.NewLine, headers.Select(kvp => $"{kvp.Key}: {kvp.Value}"));
    }

    private void ApplyDefaultWebViewSettings(BlazorWebViewDeveloperTools devTools)
    {
        _webview.CoreWebView2.Settings.AreDevToolsEnabled = devTools.Enabled;

        // Desktop applications typically don't want the default web browser context menu
        _webview.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;

        // Desktop applications almost never want to show a URL preview when hovering over a link
        _webview.CoreWebView2.Settings.IsStatusBarEnabled = false;
    }

    private static string? GetWebView2UserDataFolder()
    {
        if (Assembly.GetEntryAssembly() is { } mainAssembly)
        {
            // In case the application is running from a non-writable location (e.g., program files if you're not running
            // elevated), use our own convention of %LocalAppData%\YourApplicationName.WebView2.
            // We may be able to remove this if https://github.com/MicrosoftEdge/WebView2Feedback/issues/297 is fixed.
            var applicationName = mainAssembly.GetName().Name;
            var result = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                $"{applicationName}.WebView2");

            return result;
        }

        return null;
    }
}
