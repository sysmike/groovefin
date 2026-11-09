using Groovefin.Core.Services;
using Groovefin.Core.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;

namespace Groovefin.App.Views;

/// <summary>
/// Settings page in the style of Windows Settings: groups of cards, with details in expanders.
/// </summary>
internal sealed partial class SettingsPage : Page
{
    public SettingsPage()
    {
        ViewModel = App.Services.GetRequiredService<SettingsViewModel>();
        Equalizer = App.CreateViewModel<EqualizerViewModel>();
        Shortcuts = App.CreateViewModel<ShortcutsViewModel>();
        Downloads = App.CreateViewModel<DownloadsViewModel>();
        OutputDevice = App.CreateViewModel<OutputDeviceViewModel>();
        Startup = App.CreateViewModel<StartupViewModel>();
        History = App.Services.GetRequiredService<ListeningHistoryViewModel>();
        InitializeComponent();
        AddSectionLinks();
        Loaded += OnFirstLoaded;
        Loaded += OnLoaded;
    }

    public SettingsViewModel ViewModel { get; }

    public EqualizerViewModel Equalizer { get; }

    public ShortcutsViewModel Shortcuts { get; }

    public DownloadsViewModel Downloads { get; }

    /// <summary>Gets the output device's card (#389).</summary>
    public OutputDeviceViewModel OutputDevice { get; }

    /// <summary>Gets the Start with Windows card (#395).</summary>
    public StartupViewModel Startup { get; }

    /// <summary>Gets the listening history's card: clears the plays kept for On repeat and the statistics (#382).</summary>
    public ListeningHistoryViewModel History { get; }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        Equalizer.Dispose();
        Shortcuts.Dispose();
        Downloads.Dispose();
        OutputDevice.Dispose();
        Startup.Dispose();
    }

    /// <summary>Change on a shortcut: its Tag is the shortcut (<c>Tag="{x:Bind}"</c>, #172).</summary>
    private async void OnChangeShortcutClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is ShortcutItemViewModel item)
        {
            await ShortcutDialog.ShowAsync(XamlRoot, Shortcuts, item, App.Services.GetRequiredService<IStringResources>());
        }
    }

    /// <summary>
    /// Adds a link for each section heading (#404). The headings' text comes from their <c>x:Uid</c>, so the links
    /// need no strings of their own and a new section gets its link without a change here.
    /// </summary>
    private void AddSectionLinks()
    {
        foreach (var heading in Sections.Children.OfType<TextBlock>())
        {
            if (AutomationProperties.GetHeadingLevel(heading) != AutomationHeadingLevel.Level2)
            {
                continue;
            }

            var link = new HyperlinkButton { Content = heading.Text, Tag = heading };
            link.Click += OnSectionLinkClick;
            SectionLinks.Children.Add(link);
        }
    }

    /// <summary>Scrolls the link's section to the top and focuses its first setting, so Tab goes on from there.</summary>
    private void OnSectionLinkClick(object sender, RoutedEventArgs e)
    {
        if (sender is not HyperlinkButton { Tag: TextBlock heading } link)
        {
            return;
        }

        // Focus first: focusing brings the setting into view only as far as needed. The heading's request comes
        // after that one has been laid out, so it wins and puts the heading at the top.
        var state = link.FocusState == FocusState.Keyboard ? FocusState.Keyboard : FocusState.Programmatic;
        if (FirstSettingAfter(heading) is { } setting)
        {
            setting.Focus(state);
        }

        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => heading.StartBringIntoView(new BringIntoViewOptions
        {
            AnimationDesired = false,
            VerticalAlignmentRatio = 0,

            // The heading's top margin is the space above the section; keep some of it.
            VerticalOffset = -12,
        }));
    }

    /// <summary>The first element that takes the focus between <paramref name="heading"/> and the next heading.</summary>
    private Control? FirstSettingAfter(TextBlock heading)
    {
        var index = Sections.Children.IndexOf(heading);
        foreach (var element in Sections.Children.Skip(index + 1))
        {
            if (element is TextBlock next && AutomationProperties.GetHeadingLevel(next) == AutomationHeadingLevel.Level2)
            {
                break;
            }

            if (element.Visibility != Visibility.Visible)
            {
                continue;
            }

            if (element is Control { IsEnabled: true, IsTabStop: true } control)
            {
                return control;
            }

            if (FocusManager.FindFirstFocusableElement(element) is Control inner)
            {
                return inner;
            }
        }

        return null;
    }

    private async void OnFirstLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnFirstLoaded;
        var startup = Startup.LoadAsync();
        await ViewModel.LoadLibrariesAsync();
        await startup;
    }

    // The cache grows while browsing, so it is measured on every visit; the downloads show what is on disk.
    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.RefreshCacheSizeAsync();
        await Downloads.LoadAsync();

        // Devices come and go between visits.
        await OutputDevice.LoadAsync();
    }
}
