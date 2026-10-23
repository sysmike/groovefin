// Scans the windows of a running process with the Axe.Windows rules, the automated checks of Accessibility
// Insights FastPass (#51):
//
//     Groovefin.AccessibilityScan <process id> <scan id> <output directory>
//
// Prints each error with the element it was found on, and writes an .a11ytest file per window with errors
// to the output directory (it opens in Accessibility Insights). Errors on parts the frameworks draw, which
// the app cannot change, are printed as ignored (see IsFrameworkIssue). Exit code: 0 no errors, 1 errors,
// 2 the scan failed.
using System.Globalization;
using Axe.Windows.Automation;
using Axe.Windows.Automation.Data;

if (args.Length != 3 || !int.TryParse(args[0], NumberStyles.None, CultureInfo.InvariantCulture, out var processId))
{
    Console.Error.WriteLine("Usage: Groovefin.AccessibilityScan <process id> <scan id> <output directory>");
    return 2;
}

var config = Config.Builder.ForProcessId(processId)
    .WithOutputFileFormat(OutputFileFormat.A11yTest)
    .WithOutputDirectory(args[2])
    .Build();

ScanOutput output;
try
{
    output = ScannerFactory.CreateScanner(config).Scan(new ScanOptions(args[1]));
}
catch (AxeWindowsAutomationException ex)
{
    Console.Error.WriteLine($"The scan failed: {ex.Message}");
    return 2;
}

var all = output.WindowScanOutputs.SelectMany(w => w.Errors).ToList();
foreach (var ignored in all.Where(IsFrameworkIssue))
{
    Console.WriteLine($"Ignored (framework): {ignored.Rule.ID} on {Describe(ignored.Element)}");
}

var errors = all.Where(e => !IsFrameworkIssue(e)).ToList();
foreach (var error in errors)
{
    Console.WriteLine($"{error.Rule.ID}: {error.Rule.Description} On {Describe(error.Element)}");
}

// How to fix each rule once, not per element.
foreach (var rule in errors.Select(e => e.Rule).DistinctBy(r => r.ID))
{
    Console.WriteLine($"How to fix {rule.ID}: {rule.HowToFix}");
}

Console.WriteLine($"{errors.Count} accessibility error(s) in '{args[1]}'.");
return errors.Count == 0 ? 0 : 1;

// Errors the app cannot fix: they are on elements the Windows App SDK or WinUI create, not Groovefin.
// - The title bar's caption buttons that a presenter hides (the mini player cannot be minimized or
//   maximized) stay in the UI Automation tree without bounds and without IsOffscreen.
// - A popup's window host (WinUI's PopupWindowSiteBridge, e.g. for a tooltip) has an input site pane of
//   exactly its own size.
static bool IsFrameworkIssue(ScanResult error)
{
    var rule = error.Rule.ID.ToString();
    var className = Property(error.Element, "ClassName");
    var parentClass = error.Element.Parent is { } parent ? Property(parent, "ClassName") : string.Empty;
    return (rule == "BoundingRectangleNotNull" && parentClass == "ReunionWindowingCaptionControls")
        || (rule == "BoundingRectangleCompletelyObscuresContainer" && className == "InputSiteWindowClass"
            && parentClass == "Microsoft.UI.Content.PopupWindowSiteBridge");
}

static string Property(ElementInfo element, string name) =>
    element.Properties.TryGetValue(name, out var value) ? value : string.Empty;

// The element and its ancestors, innermost first: control type, name, automation id and class.
static string Describe(ElementInfo element)
{
    var parts = new List<string>();
    for (var e = element; e is not null && parts.Count < 4; e = e.Parent)
    {
        parts.Add($"{Property(e, "ControlType")} '{Property(e, "Name")}' #{Property(e, "AutomationId")} ({Property(e, "ClassName")})");
    }

    return string.Join(" < ", parts);
}
