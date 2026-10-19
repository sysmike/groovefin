using Groovefin.App.Services;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.Windows.ApplicationModel.Resources;

namespace Groovefin.App.Views;

/// <summary>
/// Placeholder for sections that are implemented in later milestones.
/// </summary>
internal sealed partial class SectionPage : Page
{
    private static readonly ResourceLoader Strings = new();

    public SectionPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is PageNavigationArgs args)
        {
            HeaderText.Text = Strings.GetString($"SectionTitle_{args.PageKey}");
        }
    }
}
