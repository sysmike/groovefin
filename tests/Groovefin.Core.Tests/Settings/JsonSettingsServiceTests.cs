using Groovefin.Core.Notifications;
using Groovefin.Core.Services;
using Groovefin.Core.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace Groovefin.Core.Tests.Settings;

public sealed class JsonSettingsServiceTests : IDisposable
{
    private readonly string _directory = Path.Join(Path.GetTempPath(), "groovefin-tests", Guid.NewGuid().ToString("N"));
    private readonly FakeLogger<JsonSettingsService> _logger = new();
    private readonly IErrorReporter _errors = Substitute.For<IErrorReporter>();

    private string FilePath => Path.Join(_directory, "settings.json");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private JsonSettingsService CreateSut(string? filePath = null) => new(new SettingsStoreOptions(filePath ?? FilePath), _errors, _logger);

    private async Task WriteFileAsync(string json)
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(FilePath, json, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Missing_file_loads_defaults_without_creating_a_file()
    {
        var sut = CreateSut();

        await sut.LoadAsync(TestContext.Current.CancellationToken);

        sut.Current.Should().Be(new AppSettings());
        File.Exists(FilePath).Should().BeFalse();
    }

    [Fact]
    public async Task Update_is_persisted_and_survives_a_reload()
    {
        var sut = CreateSut();
        await sut.LoadAsync(TestContext.Current.CancellationToken);

        sut.Update(s => s with { Appearance = s.Appearance with { Theme = AppTheme.Dark } });
        await sut.FlushAsync();

        var reloaded = CreateSut();
        await reloaded.LoadAsync(TestContext.Current.CancellationToken);
        reloaded.Current.Appearance.Theme.Should().Be(AppTheme.Dark);
    }

    [Fact]
    public async Task File_uses_camel_case_and_string_enums()
    {
        var sut = CreateSut();
        sut.Update(s => s with { Appearance = s.Appearance with { Theme = AppTheme.Light } });
        await sut.FlushAsync();

        var json = await File.ReadAllTextAsync(FilePath, TestContext.Current.CancellationToken);

        json.Should().Contain("\"schemaVersion\": 1").And.Contain("\"theme\": \"Light\"");
    }

    [Fact]
    public async Task Missing_properties_from_older_files_get_defaults()
    {
        await WriteFileAsync("{}");
        var sut = CreateSut();

        await sut.LoadAsync(TestContext.Current.CancellationToken);

        sut.Current.Should().Be(new AppSettings());
    }

    [Fact]
    public async Task Missing_properties_do_not_reset_present_values()
    {
        // Guards against the serializer replacing missing properties with default(T).
        await WriteFileAsync("""{ "appearance": { "theme": "Dark" } }""");
        var sut = CreateSut();

        await sut.LoadAsync(TestContext.Current.CancellationToken);

        sut.Current.SchemaVersion.Should().Be(AppSettings.CurrentSchemaVersion);
        sut.Current.Appearance.Theme.Should().Be(AppTheme.Dark);
    }

    [Fact]
    public async Task Partial_sections_keep_defaults_for_missing_values()
    {
        await WriteFileAsync("""{ "schemaVersion": 1, "appearance": { } }""");
        var sut = CreateSut();

        await sut.LoadAsync(TestContext.Current.CancellationToken);

        sut.Current.Appearance.Theme.Should().Be(AppTheme.System);
    }

    [Fact]
    public async Task Unknown_properties_comments_and_trailing_commas_are_accepted()
    {
        await WriteFileAsync("""
            {
              // written by a newer build
              "schemaVersion": 1,
              "futureSection": { "x": 1 },
              "appearance": { "theme": "Dark", },
            }
            """);
        var sut = CreateSut();

        await sut.LoadAsync(TestContext.Current.CancellationToken);

        sut.Current.Appearance.Theme.Should().Be(AppTheme.Dark);
    }

    [Fact]
    public async Task Newer_schema_version_is_loaded_and_rewritten_with_current_version()
    {
        await WriteFileAsync("""{ "schemaVersion": 99, "appearance": { "theme": "Dark" } }""");
        var sut = CreateSut();

        await sut.LoadAsync(TestContext.Current.CancellationToken);

        sut.Current.SchemaVersion.Should().Be(AppSettings.CurrentSchemaVersion);
        sut.Current.Appearance.Theme.Should().Be(AppTheme.Dark);
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("""{ "appearance": null }""")]
    [InlineData("""{ "appearance": { "theme": "Purple" } }""")]
    public async Task Corrupted_file_falls_back_to_defaults_and_is_kept_as_backup(string content)
    {
        await WriteFileAsync(content);
        var sut = CreateSut();

        await sut.LoadAsync(TestContext.Current.CancellationToken);

        sut.Current.Should().Be(new AppSettings());
        File.Exists(FilePath).Should().BeFalse();
        (await File.ReadAllTextAsync(FilePath + ".corrupt", TestContext.Current.CancellationToken)).Should().Be(content);
        _logger.Collector.GetSnapshot().Should().Contain(r => r.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task Changed_is_raised_with_old_and_new_settings()
    {
        var sut = CreateSut();
        SettingsChangedEventArgs? args = null;
        sut.Changed += (_, e) => args = e;

        sut.Update(s => s with { Appearance = s.Appearance with { Theme = AppTheme.Dark } });
        await sut.FlushAsync();

        args.Should().NotBeNull();
        args!.OldSettings.Appearance.Theme.Should().Be(AppTheme.System);
        args.NewSettings.Appearance.Theme.Should().Be(AppTheme.Dark);
    }

    [Fact]
    public async Task Update_without_a_change_does_not_raise_or_save()
    {
        var sut = CreateSut();
        var raised = false;
        sut.Changed += (_, _) => raised = true;

        sut.Update(s => s with { Appearance = s.Appearance with { Theme = AppTheme.System } });
        await sut.FlushAsync();

        raised.Should().BeFalse();
        File.Exists(FilePath).Should().BeFalse();
    }

    [Fact]
    public async Task Rapid_updates_end_with_the_last_value_on_disk()
    {
        var sut = CreateSut();
        AppTheme[] sequence = [AppTheme.Dark, AppTheme.Light, AppTheme.Dark, AppTheme.Light];

        foreach (var theme in sequence)
        {
            sut.Update(s => s with { Appearance = s.Appearance with { Theme = theme } });
        }

        await sut.FlushAsync();

        var reloaded = CreateSut();
        await reloaded.LoadAsync(TestContext.Current.CancellationToken);
        reloaded.Current.Appearance.Theme.Should().Be(AppTheme.Light);
        File.Exists(FilePath + ".tmp").Should().BeFalse();
    }

    [Fact]
    public async Task Save_failure_is_reported_and_keeps_the_in_memory_value()
    {
        // The parent "directory" is a file, so creating the settings directory fails.
        Directory.CreateDirectory(_directory);
        var blocker = Path.Join(_directory, "blocker");
        await File.WriteAllTextAsync(blocker, "x", TestContext.Current.CancellationToken);
        var sut = CreateSut(Path.Join(blocker, "settings.json"));

        sut.Update(s => s with { Appearance = s.Appearance with { Theme = AppTheme.Dark } });
        var flush = () => sut.FlushAsync();

        await flush.Should().NotThrowAsync();
        sut.Current.Appearance.Theme.Should().Be(AppTheme.Dark);
        _errors.Received(1).Report(Arg.Any<IOException>(), Operations.SaveSettings);
    }

    [Fact]
    public async Task Load_raises_changed_on_the_callers_synchronization_context()
    {
        await WriteFileAsync("""{ "appearance": { "theme": "Dark" } }""");
        var sut = CreateSut();
        var context = new MarkerSynchronizationContext();
        var raisedOnContext = false;
        sut.Changed += (_, _) => raisedOnContext = SynchronizationContext.Current == context;

        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            await sut.LoadAsync(TestContext.Current.CancellationToken);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        raisedOnContext.Should().BeTrue();
    }

    [Fact]
    public void Update_rejects_null()
    {
        var sut = CreateSut();

        var act = () => sut.Update(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}

/// <summary>Runs continuations inline but keeps itself as the current context, so tests can detect it.</summary>
file sealed class MarkerSynchronizationContext : SynchronizationContext
{
    public override void Post(SendOrPostCallback d, object? state)
    {
        var previous = Current;
        SetSynchronizationContext(this);
        try
        {
            d(state);
        }
        finally
        {
            SetSynchronizationContext(previous);
        }
    }
}
