namespace Groovefin.App.Hosting;

/// <summary>
/// Records exceptions while the app starts and the logger does not exist yet, so a start-up crash
/// leaves more than a crash dump: <c>%LOCALAPPDATA%\Groovefin\startup-error.log</c>. The path does not
/// depend on <see cref="AppPaths"/>, which may be what failed. The first entry of a start replaces the file.
/// </summary>
internal static class StartupCrashLog
{
    [ThreadStatic]
    private static bool _writing;

    private static bool _started;

    public static string FilePath { get; } =
        Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Groovefin", "startup-error.log");

    /// <summary>Appends <paramref name="exception"/>; returns <see langword="false"/> so it can serve as an exception filter.</summary>
    public static bool Write(Exception exception, string context)
    {
        // Writing can raise first-chance exceptions itself, which would come back here.
        if (_writing)
        {
            return false;
        }

        _writing = true;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var entry = $"{DateTimeOffset.Now:O} [{context}] {exception}{Environment.NewLine}{Environment.NewLine}";
            if (_started)
            {
                File.AppendAllText(FilePath, entry);
            }
            else
            {
                File.WriteAllText(FilePath, entry);
                _started = true;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Nothing else to report to; the crash dump remains.
        }
        finally
        {
            _writing = false;
        }

        return false;
    }
}
