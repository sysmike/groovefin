using Groovefin.Core.About;
using Groovefin.Core.Api;
using Groovefin.Core.Images;
using Groovefin.Core.Models;
using Groovefin.Core.Navigation;
using Groovefin.Core.Notifications;
using Groovefin.Core.Playback;
using Groovefin.Core.Servers;
using Groovefin.Core.Services;
using Groovefin.Core.Settings;
using Groovefin.Core.Tests.TestDoubles;
using Groovefin.Core.ViewModels;

namespace Groovefin.Core.Tests.ViewModels;

public sealed class SettingsViewModelTests
{
    private readonly IThemeService _themeService = Substitute.For<IThemeService>();
    private readonly INavigationService _navigation = Substitute.For<INavigationService>();
    private readonly IServerManager _servers = Substitute.For<IServerManager>();
    private readonly ILibraryService _library = Substitute.For<ILibraryService>();
    private readonly IStringResources _strings = Substitute.For<IStringResources>();
    private readonly IErrorReporter _errors = Substitute.For<IErrorReporter>();
    private readonly InMemorySettingsService _settings = new();
    private readonly IImageCache _cache = Substitute.For<IImageCache>();
    private readonly ILauncherService _launcher = Substitute.For<ILauncherService>();

    private SettingsViewModel CreateSut() =>
        new(_themeService, _navigation, _servers, _library, _strings, _errors, _settings, _cache, _launcher, new AppVersion("1.0.0", "abc1234"));

    private void SignedInTo(ServerProfile server)
    {
        _servers.IsSignedIn.Returns(true);
        _servers.ActiveServer.Returns(server);
        _strings.GetString("Settings_AllLibraries").Returns("All music libraries");
        _library.GetMusicLibrariesAsync(Arg.Any<CancellationToken>())
            .Returns([new MusicLibrary("lib-1", "Music"), new MusicLibrary("lib-2", "Audiobooks")]);
    }

    private static ServerProfile Server(string? libraryId = null) =>
        new() { Id = "srv", Name = "Home", Address = new Uri("http://home:8096/"), UserId = "u", LibraryId = libraryId };

    [Fact]
    public void The_shortcut_reference_lists_every_shortcut()
    {
        _strings.GetString(Arg.Any<string>()).Returns(call => call.Arg<string>());

        var sut = CreateSut();

        sut.Shortcuts.Should().HaveCount(Core.Shortcuts.KeyboardShortcuts.All.Count);
        sut.Shortcuts[0].Should().Be(new ShortcutRow("Key_Space", "Shortcut_PlayPause"));
        sut.Shortcuts[1].Gesture.Should().Be("Key_Ctrl+Key_Right");
        sut.Shortcuts[1].AccessibleName.Should().Be("Key_Ctrl+Key_Right, Shortcut_Next");
    }

    [Fact]
    public void Resuming_the_session_is_on_by_default_and_can_be_turned_off()
    {
        var sut = CreateSut();
        var changed = new List<string?>();
        sut.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        sut.ResumeSession.Should().BeTrue();
        sut.ResumeSession = false;

        _settings.Current.Playback.ResumeSession.Should().BeFalse();
        changed.Should().Equal(nameof(SettingsViewModel.ResumeSession));
    }

    [Fact]
    public void Crossfade_is_off_by_default_and_offers_seconds()
    {
        _strings.GetString("Settings_CrossfadeOff").Returns("Off");
        _strings.GetString("Settings_Seconds").Returns("{0} s");
        var sut = CreateSut();
        var changed = new List<string?>();
        sut.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        sut.CrossfadeOptions.Should().Equal("Off", "1 s", "2 s", "3 s", "4 s", "5 s", "6 s", "8 s", "10 s", "12 s");
        sut.CrossfadeIndex.Should().Be(0);

        sut.CrossfadeIndex = 6;

        _settings.Current.Playback.CrossfadeSeconds.Should().Be(6);
        changed.Should().Equal(nameof(SettingsViewModel.CrossfadeIndex));
    }

    [Theory]
    [InlineData(7, 7)]
    [InlineData(99, 9)]
    public void A_crossfade_from_an_edited_file_shows_as_the_next_longer_choice(int seconds, int index)
    {
        _settings.Update(s => s with { Playback = s.Playback with { CrossfadeSeconds = seconds } });

        CreateSut().CrossfadeIndex.Should().Be(index);
    }

    [Fact]
    public void Taskbar_progress_is_on_by_default_and_can_be_turned_off()
    {
        var sut = CreateSut();

        sut.ShowTaskbarProgress.Should().BeTrue();
        sut.ShowTaskbarProgress = false;

        _settings.Current.Appearance.ShowTaskbarProgress.Should().BeFalse();
    }

    [Fact]
    public void The_tray_icon_and_minimizing_to_it_are_off_by_default_and_saved()
    {
        var sut = CreateSut();

        sut.ShowTrayIcon.Should().BeFalse();
        sut.MinimizeToTray.Should().BeFalse();
        sut.ShowTrayIcon = true;
        sut.MinimizeToTray = true;

        _settings.Current.Appearance.ShowTrayIcon.Should().BeTrue();
        _settings.Current.Appearance.MinimizeToTray.Should().BeTrue();
    }

    [Fact]
    public void Account_shows_the_signed_in_user()
    {
        _servers.IsSignedIn.Returns(true);
        _servers.ActiveServer.Returns(new ServerProfile { Id = "id-a", Name = "Alpha", Address = new Uri("http://alpha"), UserName = "Mike" });

        var sut = CreateSut();

        sut.UserName.Should().Be("Mike");
        sut.ServerName.Should().Be("Alpha");
        sut.SignOutCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public async Task SignOut_signs_out_and_opens_sign_in()
    {
        _servers.IsSignedIn.Returns(true);
        var sut = CreateSut();

        await sut.SignOutCommand.ExecuteAsync(null);

        await _servers.Received(1).SignOutAsync(Arg.Any<CancellationToken>());
        _navigation.Received(1).NavigateTo(PageKeys.SignIn);
    }

    [Fact]
    public void SignOut_is_disabled_when_nobody_is_signed_in()
    {
        CreateSut().SignOutCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void ManageServers_opens_the_server_page()
    {
        var sut = CreateSut();

        sut.ManageServersCommand.Execute(null);

        _navigation.Received(1).NavigateTo(PageKeys.Servers);
    }

    [Fact]
    public void Initial_theme_comes_from_the_theme_service()
    {
        _themeService.Theme.Returns(AppTheme.Dark);

        var sut = CreateSut();

        sut.Theme.Should().Be(AppTheme.Dark);
        sut.ThemeIndex.Should().Be(2);
        _themeService.DidNotReceiveWithAnyArgs().SetTheme(default);
    }

    [Fact]
    public void Changing_the_theme_applies_it()
    {
        var sut = CreateSut();

        sut.Theme = AppTheme.Light;

        _themeService.Received(1).SetTheme(AppTheme.Light);
    }

    [Fact]
    public void ThemeIndex_maps_to_themes_and_raises_change_notification()
    {
        var sut = CreateSut();
        using var monitor = sut.Monitor();

        sut.ThemeIndex = 2;

        sut.Theme.Should().Be(AppTheme.Dark);
        monitor.Should().RaisePropertyChangeFor(x => x.ThemeIndex);
        _themeService.Received(1).SetTheme(AppTheme.Dark);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    public void ThemeIndex_ignores_out_of_range_values(int index)
    {
        var sut = CreateSut();

        sut.ThemeIndex = index;

        sut.Theme.Should().Be(AppTheme.System);
    }

    [Fact]
    public async Task Libraries_list_all_first_and_select_the_stored_choice()
    {
        SignedInTo(Server("lib-2"));
        var sut = CreateSut();

        await sut.LoadLibrariesAsync(TestContext.Current.CancellationToken);

        sut.Libraries.Should().Equal(new LibraryOption(null, "All music libraries"), new LibraryOption("lib-1", "Music"), new LibraryOption("lib-2", "Audiobooks"));
        sut.SelectedLibrary.Should().Be(new LibraryOption("lib-2", "Audiobooks"));
        sut.SelectedLibraryIndex.Should().Be(2);
        sut.HasLibraryChoice.Should().BeTrue();
        _servers.DidNotReceiveWithAnyArgs().SetLibrary(default!, default);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("removed-library")]
    public async Task Default_or_unknown_choice_selects_all_libraries(string? stored)
    {
        SignedInTo(Server(stored));
        var sut = CreateSut();

        await sut.LoadLibrariesAsync(TestContext.Current.CancellationToken);

        sut.SelectedLibrary!.Id.Should().BeNull();
    }

    [Fact]
    public async Task Choosing_a_library_limits_the_active_server()
    {
        SignedInTo(Server());
        var sut = CreateSut();
        await sut.LoadLibrariesAsync(TestContext.Current.CancellationToken);

        sut.SelectedLibraryIndex = 1;
        sut.SelectedLibrary = sut.Libraries[0];
        sut.SelectedLibraryIndex = 7;

        Received.InOrder(() =>
        {
            _servers.SetLibrary("srv", "lib-1");
            _servers.SetLibrary("srv", null);
        });
        _servers.ReceivedWithAnyArgs(2).SetLibrary(default!, default);
    }

    [Fact]
    public async Task A_single_library_offers_no_choice()
    {
        SignedInTo(Server());
        _library.GetMusicLibrariesAsync(Arg.Any<CancellationToken>()).Returns([new MusicLibrary("lib-1", "Music")]);
        var sut = CreateSut();

        await sut.LoadLibrariesAsync(TestContext.Current.CancellationToken);

        sut.HasLibraryChoice.Should().BeFalse();
    }

    [Fact]
    public async Task Library_load_failure_is_reported()
    {
        SignedInTo(Server());
        _library.GetMusicLibrariesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IReadOnlyList<MusicLibrary>>(new JellyfinException("offline")));
        var sut = CreateSut();

        await sut.LoadLibrariesAsync(TestContext.Current.CancellationToken);

        sut.Libraries.Should().BeEmpty();
        sut.HasLibraryChoice.Should().BeFalse();
        _errors.Received(1).Report(Arg.Any<JellyfinException>(), Operations.LoadLibraries);
    }

    [Fact]
    public async Task Libraries_are_not_loaded_when_signed_out()
    {
        var sut = CreateSut();

        await sut.LoadLibrariesAsync(TestContext.Current.CancellationToken);

        await _library.DidNotReceiveWithAnyArgs().GetMusicLibrariesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public void Bitrate_options_start_with_original_quality()
    {
        _strings.GetString("Settings_OriginalQuality").Returns("Original quality");
        _strings.GetString("Settings_Kbps").Returns("{0} kbps");

        var sut = CreateSut();

        sut.BitrateOptions.Select(o => o.Label).Should().Equal("Original quality", "320 kbps", "256 kbps", "192 kbps", "128 kbps", "96 kbps", "64 kbps");
        sut.BitrateOptions[0].Kbps.Should().BeNull();
        sut.BitrateOptions[0].ToString().Should().Be("Original quality");
    }

    [Fact]
    public void Streaming_defaults_are_shown()
    {
        var sut = CreateSut();

        sut.LocalBitrateIndex.Should().Be(0);
        sut.RemoteBitrateIndex.Should().Be(1);
        sut.CodecIndex.Should().Be(0);
    }

    [Fact]
    public void Streaming_choices_are_saved()
    {
        var sut = CreateSut();
        var changed = new List<string?>();
        sut.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        sut.LocalBitrateIndex = 3;
        sut.RemoteBitrateIndex = 0;
        sut.CodecIndex = 1;

        _settings.Current.Streaming.Should().Be(new StreamingSettings { LocalMaxBitrateKbps = 192, RemoteMaxBitrateKbps = null, Codec = TranscodingCodec.Mp3 });
        changed.Should().Equal(nameof(sut.LocalBitrateIndex), nameof(sut.RemoteBitrateIndex), nameof(sut.CodecIndex));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(99)]
    public void Out_of_range_and_unchanged_choices_are_ignored(int index)
    {
        var sut = CreateSut();
        var raised = 0;
        sut.PropertyChanged += (_, _) => raised++;

        sut.LocalBitrateIndex = index;
        sut.RemoteBitrateIndex = index;
        sut.CodecIndex = index;
        sut.LocalBitrateIndex = 0;
        sut.RemoteBitrateIndex = 1;
        sut.CodecIndex = 0;

        raised.Should().Be(0);
        _settings.Current.Streaming.Should().Be(new StreamingSettings());
    }

    [Fact]
    public void Normalization_is_off_by_default_and_saved()
    {
        var sut = CreateSut();
        var changed = new List<string?>();
        sut.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        sut.NormalizationIndex.Should().Be(0);

        sut.NormalizationIndex = 2;
        sut.NormalizationIndex = 2;
        sut.NormalizationIndex = 5;

        _settings.Current.Playback.Normalization.Should().Be(NormalizationMode.Album);
        changed.Should().Equal(nameof(sut.NormalizationIndex));
    }

    [Fact]
    public void Instant_mix_replaces_by_default_and_the_choice_is_saved()
    {
        var sut = CreateSut();
        sut.InstantMixIndex.Should().Be(0);

        sut.InstantMixIndex = 1;
        sut.InstantMixIndex = 7;

        _settings.Current.Playback.InstantMix.Should().Be(InstantMixMode.Append);
        sut.InstantMixIndex.Should().Be(1);
    }

    [Fact]
    public void The_cache_limit_is_chosen_from_the_list()
    {
        _strings.GetString("Size_Megabytes").Returns("{0} MB");
        _strings.GetString("Size_Gigabytes").Returns("{0} GB");
        var sut = CreateSut();

        sut.CacheLimitOptions.Should().HaveCount(6);
        sut.CacheLimitOptions[2].Should().Be("500 MB");
        sut.CacheLimitIndex.Should().Be(2);

        sut.CacheLimitIndex = 3;
        sut.CacheLimitIndex = 42;

        _settings.Current.Cache.ImageCacheSizeMegabytes.Should().Be(1000);
        sut.CacheLimitIndex.Should().Be(3);
    }

    [Fact]
    public async Task The_cache_size_is_measured_and_clearing_measures_again()
    {
        _strings.GetString("Size_Megabytes").Returns("{0} MB");
        _strings.GetString("Settings_CacheUsed").Returns("{0} used");
        _cache.GetSizeAsync(Arg.Any<CancellationToken>()).Returns(120L * 1024 * 1024, 0L);
        var sut = CreateSut();

        await sut.RefreshCacheSizeAsync(TestContext.Current.CancellationToken);
        sut.CacheSizeText.Should().Be("120 MB used");

        await sut.ClearCacheCommand.ExecuteAsync(null);

        await _cache.Received(1).ClearAsync(Arg.Any<CancellationToken>());
        sut.CacheSizeText.Should().Be("0 MB used");
    }

    [Fact]
    public async Task A_cache_that_cannot_be_measured_shows_no_size()
    {
        _cache.GetSizeAsync(Arg.Any<CancellationToken>()).Returns<long>(_ => throw new IOException("gone"));
        var sut = CreateSut();

        await sut.RefreshCacheSizeAsync(TestContext.Current.CancellationToken);

        sut.CacheSizeText.Should().BeEmpty();
        _errors.DidNotReceiveWithAnyArgs().Report(default!, default!);
    }

    [Fact]
    public async Task Failures_to_clear_the_cache_or_open_the_logs_are_reported()
    {
        var clear = new IOException("locked");
        var open = new UnauthorizedAccessException("denied");
        _cache.ClearAsync(Arg.Any<CancellationToken>()).Returns(Task.FromException(clear));
        _launcher.OpenLogFolderAsync().Returns(Task.FromException(open));
        var sut = CreateSut();

        await sut.ClearCacheCommand.ExecuteAsync(null);
        await sut.OpenLogFolderCommand.ExecuteAsync(null);

        _errors.Received(1).Report(clear, Operations.ClearCache);
        _errors.Received(1).Report(open, Operations.OpenLogFolder);
    }

    [Fact]
    public async Task The_log_folder_opens()
    {
        var sut = CreateSut();

        await sut.OpenLogFolderCommand.ExecuteAsync(null);

        await _launcher.Received(1).OpenLogFolderAsync();
    }

    [Fact]
    public void About_shows_the_version_commit_and_licenses()
    {
        _strings.GetString("Settings_Version").Returns("Version {0}");
        var sut = CreateSut();

        sut.VersionText.Should().Be("Version 1.0.0");
        sut.HasCommit.Should().BeTrue();
        sut.CommitUri.Should().Be(new Uri("https://github.com/sysmike/groovefin/commit/abc1234"));
        sut.RepositoryUri.Should().Be(ProjectLinks.Repository);
        sut.Licenses.Should().BeSameAs(ThirdPartyNotices.All);
    }

    [Fact]
    public void A_build_without_commit_has_no_commit_link()
    {
        var sut = new SettingsViewModel(_themeService, _navigation, _servers, _library, _strings, _errors, _settings, _cache, _launcher, new AppVersion("1.0.0", null));

        sut.HasCommit.Should().BeFalse();
        sut.CommitUri.Should().BeNull();
    }

    [Fact]
    public void The_accent_colour_is_on_by_default_and_can_be_turned_off()
    {
        var sut = CreateSut();
        var changed = new List<string?>();
        sut.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        sut.UseAccentColor.Should().BeTrue();
        sut.UseAccentColor = false;

        _settings.Current.Appearance.UseAccentColor.Should().BeFalse();
        changed.Should().Equal(nameof(SettingsViewModel.UseAccentColor));
    }

    [Fact]
    public void The_language_follows_windows_until_one_is_chosen()
    {
        _strings.GetString(Arg.Any<string>()).Returns(call => call.Arg<string>());
        var sut = CreateSut();

        sut.LanguageOptions.Should().Equal("Settings_LanguageSystem", "Settings_LanguageEnglish", "Settings_LanguageGerman");
        sut.LanguageIndex.Should().Be(0);

        sut.LanguageIndex = 2;
        _settings.Current.Appearance.Language.Should().Be("de-DE");

        sut.LanguageIndex = 9;
        sut.LanguageIndex = 0;
        _settings.Current.Appearance.Language.Should().BeNull();
    }

    [Fact]
    public void An_unknown_stored_language_shows_as_windows_language()
    {
        _settings.Update(s => s with { Appearance = s.Appearance with { Language = "fr-FR" } });

        CreateSut().LanguageIndex.Should().Be(0);
    }

    [Fact]
    public void The_start_page_is_one_of_the_main_sections()
    {
        _strings.GetString(Arg.Any<string>()).Returns(call => call.Arg<string>());
        var sut = CreateSut();

        sut.StartPageOptions.Should().Equal(PageKeys.MainSections.Select(k => $"StartPage_{k}"));
        sut.StartPageIndex.Should().Be(0);

        sut.StartPageIndex = PageKeys.MainSections.ToList().IndexOf(PageKeys.Songs);
        sut.StartPageIndex = -1;

        _settings.Current.Appearance.StartPage.Should().Be(PageKeys.Songs);
    }

    [Fact]
    public void The_density_is_standard_until_compact_is_chosen()
    {
        var sut = CreateSut();
        sut.DensityIndex.Should().Be(0);

        sut.DensityIndex = 1;
        sut.DensityIndex = 5;

        _settings.Current.Appearance.Density.Should().Be(ListDensity.Compact);
        sut.DensityIndex.Should().Be(1);
    }
}
