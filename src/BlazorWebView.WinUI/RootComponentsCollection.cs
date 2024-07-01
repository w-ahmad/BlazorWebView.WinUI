using System.Collections.ObjectModel;
using Microsoft.AspNetCore.Components.Web;

namespace Microsoft.AspNetCore.Components.WebView.WinUI;

/// <summary>
/// A collection of <see cref="RootComponent"/> items.
/// </summary>
public class RootComponentsCollection : ObservableCollection<RootComponent>, IJSComponentConfiguration
{
    /// <inheritdoc />
    public JSComponentConfigurationStore JSComponents { get; } = new();
}
