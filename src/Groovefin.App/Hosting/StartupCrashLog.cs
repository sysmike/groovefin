namespace Groovefin.App.Hosting;

/// <summary>
/// Records an exception that ends the app before logging works (the host is not built yet), so a
/// start-up crash leaves more than a crash dump: <c>%LOCALAPPDATA%\Groovefin\startup-error.log</c>.
/// The path does not depend on <see cref="AppPaths"/>, which may be what failed.
/// </summary>
internal static class StartupCrashLog
{
    public static string FilePath { get; } =
        Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Groovefin", "startup-error.log");

    /// <summary>Appends <paramref name="exception"/>; returns <see langword="false"/> so it can serve as an exception filter.</summary>
    public static bool Write(Exception exception)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.AppendAllText(FilePath, $"{DateTimeOffset.Now:O} {exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Nothing else to report to; the crash dump remains.
        }

        return false;
    }
}
