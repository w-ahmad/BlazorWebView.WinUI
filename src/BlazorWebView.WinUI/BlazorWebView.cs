using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components.WebView.WebView2;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WebView2Control = Microsoft.UI.Xaml.Controls.WebView2;

namespace Microsoft.AspNetCore.Components.WebView.WinUI;

/// <summary>
/// A WinUI control for hosting Razor components locally in Windows desktop applications.
/// </summary>
public class BlazorWebView : Control, IAsyncDisposable
{
    #region Dependency property definitions
    /// <summary>
    /// The backing store for the <see cref="HostPage"/> property.
    /// </summary>
    public static readonly DependencyProperty HostPageProperty = DependencyProperty.Register(
        name: nameof(HostPage),
        propertyType: typeof(string),
        ownerType: typeof(BlazorWebView),
        typeMetadata: new PropertyMetadata(default, OnHostPagePropertyChanged));

    /// <summary>
    /// The backing store for the <see cref="StartPath"/> property.
    /// </summary>
    public static readonly DependencyProperty StartPathProperty = DependencyProperty.Register(
        name: nameof(StartPath),
        propertyType: typeof(string),
        ownerType: typeof(BlazorWebView),
        typeMetadata: new PropertyMetadata("/"));

    /// <summary>
    /// The backing store for the <see cref="RootComponent"/> property.
    /// </summary>
    public static readonly DependencyProperty RootComponentsProperty = DependencyProperty.Register(
        name: nameof(RootComponents),
        propertyType: typeof(RootComponentsCollection),
        ownerType: typeof(BlazorWebView),
        typeMetadata: new PropertyMetadata(default));

    /// <summary>
    /// The backing store for the <see cref="Services"/> property.
    /// </summary>
    public static readonly DependencyProperty ServicesProperty = DependencyProperty.Register(
        name: nameof(Services),
        propertyType: typeof(IServiceProvider),
        ownerType: typeof(BlazorWebView),
        typeMetadata: new PropertyMetadata(default, OnServicesPropertyChanged));
    #endregion

    private const string WebViewTemplateChildName = "WebView";
    private WebView2Control? _webview;
    private WebView2WebViewManager? _webviewManager;
    private bool _isDisposed;

    /// <summary>
    /// Creates a new instance of <see cref="BlazorWebView"/>.
    /// </summary>
    public BlazorWebView()
    {
        DefaultStyleKey = typeof(BlazorWebView);

        ComponentsDispatcher = new WinUIDispatcher(DispatcherQueue);

        SetValue(RootComponentsProperty, new RootComponentsCollection());
        RootComponents.CollectionChanged += HandleRootComponentsCollectionChanged;
    }

    /// <summary>
    /// Returns the inner <see cref="WebView2Control"/> used by this control.
    /// </summary>
    /// <remarks>
    /// Directly using some functionality of the inner web view can cause unexpected results because its behavior
    /// is controlled by the <see cref="BlazorWebView"/> that is hosting it.
    /// </remarks>
    [Browsable(false)]
    public WebView2Control WebView => _webview!;

    /// <summary>
    /// Path to the host page within the application's static files. For example, <code>wwwroot\index.html</code>.
    /// This property must be set to a valid value for the Razor components to start.
    /// </summary>
    public string HostPage
    {
        get => (string)GetValue(HostPageProperty);
        set => SetValue(HostPageProperty, value);
    }

    /// <summary>
    /// Path for initial Blazor navigation when the Blazor component is finished loading.
    /// </summary>
    public string StartPath
    {
        get => (string)GetValue(StartPathProperty);
        set => SetValue(StartPathProperty, value);
    }

    /// <summary>
    /// A collection of <see cref="RootComponent"/> instances that specify the Blazor <see cref="IComponent"/> types
    /// to be used directly in the specified <see cref="HostPage"/>.
    /// </summary>
    public RootComponentsCollection RootComponents => (RootComponentsCollection)GetValue(RootComponentsProperty);

    /// <summary>
    /// Allows customizing how links are opened.
    /// By default, opens internal links in the webview and external links in an external app.
    /// </summary>
    public event EventHandler<UrlLoadingEventArgs>? UrlLoading;

    /// <summary>
    /// Allows customizing the web view before it is created.
    /// </summary>
    public event EventHandler<BlazorWebViewInitializingEventArgs>? BlazorWebViewInitializing;
    /// <summary>
    /// Allows customizing the web view after it is created.
    /// </summary>
    public event EventHandler<BlazorWebViewInitializedEventArgs>? BlazorWebViewInitialized;

    /// <summary>
    /// Gets or sets an <see cref="IServiceProvider"/> containing services to be used by this control and also by application code.
    /// This property must be set to a valid value for the Razor components to start.
    /// </summary>
    public IServiceProvider Services
    {
        get => (IServiceProvider)GetValue(ServicesProperty);
        set =>  SetValue(ServicesProperty, value);
    }

    private static void OnServicesPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((BlazorWebView)d).OnServicesPropertyChanged(e);
    }

    private void OnServicesPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        StartWebViewCoreIfPossible();
    }

    private static void OnHostPagePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((BlazorWebView)d).OnHostPagePropertyChanged(e);
    }

    private void OnHostPagePropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        StartWebViewCoreIfPossible();
    }

    private bool RequiredStartupPropertiesSet => _webview != null && HostPage != null && Services != null;

    /// <inheritdoc cref="FrameworkElement.OnApplyTemplate" />
    protected override void OnApplyTemplate()
    {
        CheckDisposed();

        // Called when the control is created after its child control (the WebView2) is created from the Template property
        base.OnApplyTemplate();

        if (_webview == null)
        {
            _webview = (WebView2Control)GetTemplateChild(WebViewTemplateChildName);
            StartWebViewCoreIfPossible();
        }
    }

    private void StartWebViewCoreIfPossible()
    {
        CheckDisposed();

        if (!RequiredStartupPropertiesSet || _webviewManager != null)
        {
            return;
        }

        var logger = Services.GetService<ILogger<BlazorWebView>>() ?? NullLogger<BlazorWebView>.Instance;

        // We assume the host page is always in the root of the content directory, because it's
        // unclear there's any other use case. We can add more options later if so.
        var entryAssemblyLocation = Assembly.GetEntryAssembly()?.Location;
        var appRootDir = !string.IsNullOrEmpty(entryAssemblyLocation) ? Path.GetDirectoryName(entryAssemblyLocation)! : AppContext.BaseDirectory;
        var hostPageFullPath = Path.GetFullPath(Path.Combine(appRootDir, HostPage));
        var contentRootDirFullPath = Path.GetDirectoryName(hostPageFullPath)!;
        var hostPageRelativePath = Path.GetRelativePath(contentRootDirFullPath, hostPageFullPath);
        var contentRootDirRelativePath = Path.GetRelativePath(appRootDir, contentRootDirFullPath);

        logger.CreatingFileProvider(contentRootDirFullPath, hostPageRelativePath);
        var fileProvider = CreateFileProvider(contentRootDirFullPath);

        _webviewManager = new WebView2WebViewManager(
            _webview!,
            Services,
            ComponentsDispatcher,
            fileProvider,
            RootComponents.JSComponents,
            contentRootDirRelativePath,
            hostPageRelativePath,
            (args) => UrlLoading?.Invoke(this, args),
            (args) => BlazorWebViewInitializing?.Invoke(this, args),
            (args) => BlazorWebViewInitialized?.Invoke(this, args),
            logger);

        StaticContentHotReloadManager.AttachToWebViewManagerIfEnabled(_webviewManager);

        foreach (var rootComponent in RootComponents)
        {
            logger.AddingRootComponent(rootComponent.ComponentType.FullName ?? string.Empty, rootComponent.Selector, rootComponent.Parameters?.Count ?? 0);

            // Since the page isn't loaded yet, this will always complete synchronously
            _ = rootComponent.AddToWebViewManagerAsync(_webviewManager);
        }

        logger.StartingInitialNavigation(StartPath);
        _webviewManager.Navigate(StartPath);
    }

    /// <summary>
    /// Navigates to the specified URI.
    /// </summary>
    /// <param name="path">The URI to navigate to.</param>
    public void Navigate(string path)
    {
        _webviewManager?.Navigate(path);
    }

    private WinUIDispatcher ComponentsDispatcher { get; }

    private void HandleRootComponentsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs eventArgs)
    {
        CheckDisposed();

        // If we haven't initialized yet, this is a no-op
        if (_webviewManager != null)
        {
            // Dispatch because this is going to be async, and we want to catch any errors
            _ = ComponentsDispatcher.InvokeAsync(async () =>
            {
                var newItems = (eventArgs.NewItems ?? Array.Empty<RootComponent>()).Cast<RootComponent>();
                var oldItems = (eventArgs.OldItems ?? Array.Empty<RootComponent>()).Cast<RootComponent>();

                foreach (var item in newItems.Except(oldItems))
                {
                    await item.AddToWebViewManagerAsync(_webviewManager);
                }

                foreach (var item in oldItems.Except(newItems))
                {
                    await item.RemoveFromWebViewManagerAsync(_webviewManager);
                }
            });
        }
    }

    /// <summary>
    /// Creates a file provider for static assets used in the <see cref="BlazorWebView"/>. The default implementation
    /// serves files from disk. Override this method to return a custom <see cref="IFileProvider"/> to serve assets such
    /// as <c>wwwroot/index.html</c>. Call the base method and combine its return value with a <see cref="CompositeFileProvider"/>
    /// to use both custom assets and default assets.
    /// </summary>
    /// <param name="contentRootDir">The base directory to use for all requested assets, such as <c>wwwroot</c>.</param>
    /// <returns>Returns a <see cref="IFileProvider"/> for static assets.</returns>
    public virtual IFileProvider CreateFileProvider(string contentRootDir)
    {
        if (Directory.Exists(contentRootDir))
        {
            // Typical case after publishing, or if you're copying content to the bin dir in development for some nonstandard reason
            return new PhysicalFileProvider(contentRootDir);
        }
        else
        {
            // Typical case in development, as the files come from Microsoft.AspNetCore.Components.WebView.StaticContentProvider
            // instead and aren't copied to the bin dir
            return new NullFileProvider();
        }
    }

    /// <summary>
    /// Calls the specified <paramref name="workItem"/> asynchronously and passes in the scoped services available to Razor components.
    /// </summary>
    /// <param name="workItem">The action to call.</param>
    /// <returns>Returns a <see cref="Task"/> representing <c>true</c> if the <paramref name="workItem"/> was called, or <c>false</c> if it was not called because Blazor is not currently running.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="workItem"/> is <c>null</c>.</exception>
    public virtual Task<bool> TryDispatchAsync(Action<IServiceProvider> workItem)
    {
        throw new NotImplementedException();
    }

    private void CheckDisposed()
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(GetType().Name);
        }
    }

    /// <summary>
    /// Allows asynchronous disposal of the <see cref="BlazorWebView" />.
    /// </summary>
    protected virtual async ValueTask DisposeAsyncCore()
    {
        // Dispose this component's contents that user-written disposal logic and Razor component disposal logic will
        // complete first. Then dispose the WebView2 control. This order is critical because once the WebView2 is
        // disposed it will prevent and Razor component code from working because it requires the WebView to exist.
        if (_webviewManager != null)
        {
            await _webviewManager.DisposeAsync()
                .ConfigureAwait(false);
            _webviewManager = null;
        }

        _webview = null;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return;
        }
        _isDisposed = true;

        // Perform async cleanup.
        await DisposeAsyncCore();

        // Suppress finalization.
        GC.SuppressFinalize(this);
    }
}
