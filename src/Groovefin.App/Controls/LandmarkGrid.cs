using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;

namespace Groovefin.App.Controls;

// Public: XAML creates it and sets its properties at run time; no setters are generated for an internal class (#140).
/// <summary>
/// A <see cref="Grid"/> that is an element in the UI Automation tree, so its <c>AutomationProperties.Name</c> and
/// <c>AutomationProperties.LandmarkType</c> reach screen readers: Narrator jumps between landmarks such as the
/// page and the now playing bar (#51). A plain Grid has no automation element and drops both.
/// </summary>
public sealed partial class LandmarkGrid : Grid
{
    protected override AutomationPeer OnCreateAutomationPeer() => new LandmarkGridAutomationPeer(this);

    private sealed partial class LandmarkGridAutomationPeer(LandmarkGrid owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;

        protected override string GetClassNameCore() => nameof(LandmarkGrid);
    }
}
