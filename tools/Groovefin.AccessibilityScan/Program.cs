// Scans the windows of a running process with the Axe.Windows rules, the automated checks of Accessibility
// Insights FastPass (#51):
//
//     Groovefin.AccessibilityScan <process id> <scan id> <output directory>
//
// Prints each error with the element it was found on, and writes an .a11ytest file per window with errors
// to the output directory (it opens in Accessibility Insights). Exit code: 0 no errors, 1 errors, 2 the scan
// failed.
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

var errors = output.WindowScanOutputs.SelectMany(w => w.Errors).ToList();
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

// The element and its ancestors, innermost first: control type, name, automation id and class.
static string Describe(ElementInfo element)
{
    var parts = new List<string>();
    for (var e = element; e is not null && parts.Count < 4; e = e.Parent)
    {
        string Property(string name) => e.Properties.TryGetValue(name, out var value) ? value : string.Empty;
        parts.Add($"{Property("ControlType")} '{Property("Name")}' #{Property("AutomationId")} ({Property("ClassName")})");
    }

    return string.Join(" < ", parts);
}
