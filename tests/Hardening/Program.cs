using System.IO;
using System.Net.Http;
using System.Windows.Input;
using GrevHome.Input;
using GrevHome.Profiles;
using GrevHome.Storage;
using GrevHome.Store.Installers;
using GrevHome.Runtime;
using GrevHome.Sessions;
using GrevHome.Presentation;
using GrevHome.Games;

var root = Path.Combine(Path.GetTempPath(), "GrevHomeHardening-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    if (args.Contains("--official-installers"))
    {
        // Download and verify only. NEVER execute an installer in the test runner.
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(4) };
        foreach (var item in new[]
        {
            ("https://cdn.fastly.steamstatic.com/client/installer/SteamSetup.exe", "Valve Corp.", "SteamSetup.exe"),
            ("https://discord.com/api/downloads/distributions/app/installers/latest?channel=stable&platform=win&arch=x64", "Discord Inc.", "DiscordSetup.exe")
        })
        {
            var path = Path.Combine(root, item.Item3);
            await File.WriteAllBytesAsync(path, await http.GetByteArrayAsync(item.Item1));
            await InstallerSignatureVerifier.VerifyAsync(path, item.Item2, CancellationToken.None);
            Console.WriteLine($"Verified official publisher: {item.Item2}");
        }
        return;
    }

    var paths = new AppPaths(root);
    Check(GrevHome.Updates.GrevHomeUpdateService.IsReleaseAsset(new Uri("https://github.com/Grevyo/Grev-Home/releases/download/v0.14.1/GrevHomeSetup.exe")), "Official release asset allowed");
    foreach (var url in new[] {
        "http://github.com/Grevyo/Grev-Home/releases/download/v1/setup.exe",
        "https://example.com/Grevyo/Grev-Home/releases/download/v1/setup.exe",
        "https://github.com/another/repo/releases/download/v1/setup.exe",
        "https://github.com/Grevyo/Grev-Home/releases/download/v1/setup.exe?redirect=bad"
    }) Check(!GrevHome.Updates.GrevHomeUpdateService.IsReleaseAsset(new Uri(url)), "Untrusted update URL rejected");
    var first = new ProfileService(paths);
    var second = new ProfileService(paths);
    var legacyGuest = await first.CreateAsync("guest", AccountRole.Standard);
    var builtin = await first.EnsureBuiltInGuestAsync();
    Check(builtin.Username != legacyGuest.Username, "Existing guest username must be preserved without collision");
    Check((await second.EnsureBuiltInGuestAsync()).GrevId == builtin.GrevId, "Bootstrap must be idempotent");
    var player = await first.CreateAsync("Player", AccountRole.Standard);
    await Task.WhenAll(
        first.UpdateDisplayNameAsync(player.GrevId, "Changed"),
        second.UpdateRoleAsync(player.GrevId, AccountRole.Guest));
    var updated = (await first.GetProfilesAsync()).Single(p => p.GrevId == player.GrevId);
    Check(updated.DisplayName == "Changed" && updated.Role == AccountRole.Guest, "Concurrent updates must preserve both independent changes");
    await Task.WhenAll(Enumerable.Range(0, 30).Select(i =>
        (i % 2 == 0 ? first : second).UpdateDisplayNameAsync(player.GrevId, "Player " + i)));
    Check((await first.GetProfilesAsync()).Count == 3, "Concurrent writes must leave every profile readable");
    await ExpectAsync<InvalidOperationException>(() => first.CreateAsync(ProfileService.BuiltInGuestUsername, AccountRole.Standard));
    using var cancelled = new CancellationTokenSource();
    cancelled.Cancel();
    await ExpectAsync<OperationCanceledException>(() => first.UpdateDisplayNameAsync(player.GrevId, "Cancelled", cancelled.Token));
    Check((await first.GetProfilesAsync()).Single(p => p.GrevId == player.GrevId).DisplayName != "Cancelled", "Cancelled mutation must not write");

    Check(KeyboardRemoteInputMapper.Map(Key.MediaPlayPause) == InputAction.Accept, "Remote accept mapping");
    var protectedProfile = await first.SetControllerPasswordAsync(player.GrevId, "ABXY1234");
    Check(protectedProfile.HasControllerPassword && first.VerifyControllerPassword(protectedProfile, "ABXY1234"), "Controller password must verify");
    Check(!first.VerifyControllerPassword(protectedProfile, "wrong"), "Incorrect controller password must fail");
    Check(!File.ReadAllText(paths.GetProfileMetadata(player.GrevId)).Contains("ABXY1234", StringComparison.Ordinal), "Plaintext password must never be stored");
    await first.ClearControllerPasswordAsync(player.GrevId);
    Check(!(await first.GetProfilesAsync()).Single(p => p.GrevId == player.GrevId).HasControllerPassword, "Controller password must be removable");
    await ExpectAsync<InvalidOperationException>(() => first.DeleteAsync(ProfileService.BuiltInGuestGrevId));
    await ExpectAsync<InvalidOperationException>(() => first.DeleteAsync(legacyGuest.GrevId));
    var removable = await first.CreateAsync("DeleteMe", AccountRole.Standard);
    var recoveredAt = await first.DeleteAsync(removable.GrevId);
    Check(Directory.Exists(recoveredAt), "Deleted profile data must be moved to recoverable storage");
    Check((await first.GetProfilesAsync()).All(p => p.GrevId != removable.GrevId), "Deleted profile must leave the active profile list");
    Check(KeyboardRemoteInputMapper.Map(Key.BrowserBack) == InputAction.Back, "Remote back mapping");
    Check(KeyboardRemoteInputMapper.Map(Key.VolumeUp) is null, "Volume must remain owned by Windows/media application");
    Check(KeyboardRemoteInputMapper.Map(Key.A) is null, "Typing keys must not become shell navigation");
    var playtime = new PlaytimeService(paths);
    var sessionId = Guid.NewGuid();
    var participants = new[] { new LaunchParticipant(Guid.NewGuid(), player.GrevId, "Player", AccountKind.Local) };
    await playtime.RecordSessionAsync(sessionId, "testgame", "Test Game", participants, TimeSpan.FromSeconds(45), DateTimeOffset.UtcNow);
    await new PlaytimeService(paths).RecordSessionAsync(sessionId, "testgame", "Test Game", participants, TimeSpan.FromSeconds(45), DateTimeOffset.UtcNow);
    var time = (await playtime.GetLocalForGrevIdAsync(player.GrevId)).Apps["testgame"];
    Check(time.TotalSeconds == 45 && time.SessionCount == 1, "Completion replay after restart must not double count playtime");
    var tracked = new LaunchSessionSnapshot(sessionId, "testgame", "Test Game", player.GrevId,
        participants, DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow, LaunchSessionState.Exited,
        0, [], null, TrackedDurationSeconds: 45);
    Check(tracked.Elapsed.TotalSeconds == 45, "Foreground usage must take precedence over wall-clock runtime");
    var loader = new DashboardArtworkLoader();
    await ExpectAsync<OperationCanceledException>(() => loader.LoadAsync("missing.png", cancelled.Token));
    await ExpectAsync<FileNotFoundException>(() => loader.LoadAsync(Path.Combine(root, "missing.png"), CancellationToken.None));
    var unsigned = Path.Combine(root, "unsigned.exe");
    await File.WriteAllTextAsync(unsigned, "Not a signed executable");
    await ExpectAsync<InvalidDataException>(() => InstallerSignatureVerifier.VerifyAsync(unsigned, "Valve Corp.", CancellationToken.None));
    await ExpectAsync<OperationCanceledException>(() => InstallerSignatureVerifier.VerifyAsync(unsigned, "Valve Corp.", cancelled.Token));

    var scanRoot = Path.Combine(root, "Games");
    var ps2Root = Path.Combine(scanRoot, "PS2");
    var ps1Root = Path.Combine(scanRoot, "PS1");
    Directory.CreateDirectory(ps2Root);
    Directory.CreateDirectory(ps1Root);
    await File.WriteAllTextAsync(Path.Combine(ps2Root, "Road Trip.iso"), "test");
    await File.WriteAllTextAsync(Path.Combine(ps1Root, "Example Game.cue"), "FILE \"Example Game.bin\" BINARY");
    await File.WriteAllTextAsync(Path.Combine(ps1Root, "Example Game.bin"), "track");
    await File.WriteAllTextAsync(Path.Combine(ps1Root, "Collection.m3u"), "Example Game.cue");
    var scanner = new GameScanService();
    var scan = await scanner.ScanAsync(scanRoot, Array.Empty<GameLibraryEntry>());
    Check(scan.Candidates.Any(game => game.SuggestedName == "Road Trip" && game.Platform == GamePlatform.PlayStation2),
        "Console folder must disambiguate a PS2 ISO");
    Check(scan.Candidates.Count(game => game.SourcePath.EndsWith("Example Game.bin", StringComparison.OrdinalIgnoreCase)) == 0,
        "CUE track files must not become duplicate games");
    Check(scan.Candidates.Count(game => game.SourcePath.EndsWith("Example Game.cue", StringComparison.OrdinalIgnoreCase)) == 0,
        "M3U member discs must not become duplicate games");
    Check(scan.Candidates.Any(game => game.SourcePath.EndsWith("Collection.m3u", StringComparison.OrdinalIgnoreCase)),
        "M3U playlist must remain as the launchable game entry");
    var rescan = await scanner.ScanAsync(scanRoot, new[]
    {
        new GameLibraryEntry("game.ps2.test", "Road Trip", GamePlatform.PlayStation2,
            Path.Combine(ps2Root, "Road Trip.iso"), DateTimeOffset.UtcNow)
    });
    Check(rescan.AlreadyInLibrary == 1, "A rescan must not duplicate an existing game path");

    var themeService = new ThemeService(paths);
    var initialState = await themeService.LoadAsync();
    Check(initialState.ActiveThemeId == ThemeCatalog.DefaultThemeId, "A new machine must start on the shipped default theme");
    Check(initialState.CustomThemes.Count == 0, "A new machine must start with no custom themes");
    Check(themeService.ResolveActive(initialState) == ThemeCatalog.Default, "The default theme must resolve to the shipped Grev Default definition");

    await ExpectAsync<InvalidOperationException>(() => themeService.SaveCustomThemeAsync(ThemeCatalog.Default with { Name = "Hijacked" }));
    Check((await themeService.LoadAsync()).CustomThemes.Count == 0, "Attempting to overwrite a built-in theme must not create or alter any file");

    var draft = new ThemeDefinition("custom-test", "My Theme", "#101010", "#151515", "#333333", "#202020", "#303030", "#FF8800", "#AAAAAA", "#D8B65A", "#D94B55", "#747B88");
    await ExpectAsync<InvalidOperationException>(() => themeService.SaveCustomThemeAsync(draft with { Accent = "not-a-color" }));
    var saved = await themeService.SaveCustomThemeAsync(draft);
    Check(!saved.IsBuiltIn, "A saved custom theme must never be marked as built-in");
    await themeService.SetActiveThemeAsync(saved.Id);

    var reloaded = await themeService.LoadAsync();
    Check(reloaded.ActiveThemeId == "custom-test", "The active theme id must survive a reload");
    Check(reloaded.CustomThemes.Any(theme => theme.Id == "custom-test" && theme.Accent == "#FF8800"), "A saved custom theme must survive a reload with its exact colors");
    Check(themeService.ResolveActive(reloaded).Name == "My Theme", "ResolveActive must return the saved custom theme once it is active");

    await themeService.DeleteCustomThemeAsync("custom-test");
    var afterDelete = await themeService.LoadAsync();
    Check(afterDelete.CustomThemes.Count == 0, "A deleted custom theme must no longer be listed");
    Check(themeService.ResolveActive(afterDelete) == ThemeCatalog.Default, "Deleting the active custom theme must fall back to the shipped default rather than crashing");

    await ExpectAsync<InvalidOperationException>(() => themeService.DeleteCustomThemeAsync(ThemeCatalog.DefaultThemeId));

    Console.WriteLine("Hardening tests passed: guest migration, concurrent writes, cancellation, remote mapping, unsigned installer rejection, theme save/activate/delete round-trip.");
}
finally { Directory.Delete(root, recursive: true); }

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
static async Task ExpectAsync<T>(Func<Task> operation) where T : Exception
{
    try { await operation(); }
    catch (T) { return; }
    throw new InvalidOperationException($"Expected {typeof(T).Name}");
}
