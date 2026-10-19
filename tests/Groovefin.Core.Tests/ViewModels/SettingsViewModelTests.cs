using Groovefin.Core.Api;
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

    private SettingsViewModel CreateSut() =>
        new(_themeService, _navigation, _servers, _library, _strings, _errors, _settings, new AppVersion("1.0.0", "abc1234"));

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
}
