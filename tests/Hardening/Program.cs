using System.IO;
using System.Net.Http;
using System.Windows.Input;
using GrevHome.Input;
using GrevHome.Profiles;
using GrevHome.Storage;
using GrevHome.Store.Installers;

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
    Check(KeyboardRemoteInputMapper.Map(Key.BrowserBack) == InputAction.Back, "Remote back mapping");
    Check(KeyboardRemoteInputMapper.Map(Key.VolumeUp) is null, "Volume must remain owned by Windows/media application");
    Check(KeyboardRemoteInputMapper.Map(Key.A) is null, "Typing keys must not become shell navigation");
    var unsigned = Path.Combine(root, "unsigned.exe");
    await File.WriteAllTextAsync(unsigned, "Not a signed executable");
    await ExpectAsync<InvalidDataException>(() => InstallerSignatureVerifier.VerifyAsync(unsigned, "Valve Corp.", CancellationToken.None));
    await ExpectAsync<OperationCanceledException>(() => InstallerSignatureVerifier.VerifyAsync(unsigned, "Valve Corp.", cancelled.Token));
    Console.WriteLine("Hardening tests passed: guest migration, concurrent writes, cancellation, remote mapping, unsigned installer rejection.");
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
