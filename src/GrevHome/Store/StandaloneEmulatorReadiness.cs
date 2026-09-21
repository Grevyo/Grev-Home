namespace GrevHome.Store;

public sealed record StandaloneEmulatorReadinessResult(
    bool IsReady,
    string? StoreMessage = null,
    string? LaunchMessage = null);

/// <summary>Keeps Store health and direct-launch prerequisite decisions identical.</summary>
public static class StandaloneEmulatorReadiness
{
    public static StandaloneEmulatorReadinessResult Inspect(
        string appId, string binaryRoot, string dataRoot, string? biosRoot = null)
    {
        if (appId.Equals("rpcs3", StringComparison.OrdinalIgnoreCase) &&
            !Directory.Exists(Path.Combine(binaryRoot, "dev_flash", "vsh")))
            return new(false,
                "Firmware required: open RPCS3 once and install Sony's official PS3 system software, then return to Grev Home.",
                "RPCS3 needs Sony's official PlayStation 3 firmware before games can launch. Open RPCS3 from Grev Store once, install the firmware, then try again.");

        if (appId.Equals("vita3k", StringComparison.OrdinalIgnoreCase) &&
            !Directory.Exists(Path.Combine(dataRoot, "vs0")))
            return new(false,
                "Firmware required: open Vita3K once and install the official Vita firmware packages, then install your games into Vita3K.",
                "Vita3K needs official PlayStation Vita firmware before games can launch. Open Vita3K from Grev Store once and complete firmware setup.");

        if (appId.Equals("xemu", StringComparison.OrdinalIgnoreCase))
        {
            var configPath = Path.Combine(binaryRoot, "xemu.toml");
            var config = File.Exists(configPath) ? File.ReadAllLines(configPath) : [];
            if (!HasTomlValue(config, "bootrom_path") || !HasTomlValue(config, "flashrom_path") ||
                !HasTomlValue(config, "hard_disk_path"))
            {
                var location = string.IsNullOrWhiteSpace(biosRoot) ? "the shared BIOS folder" : biosRoot;
                return new(false,
                    $"System files required: place your user-owned MCPX, flash BIOS and HDD image in {location}, then open xemu once to select them.",
                    "xemu needs your user-owned MCPX, flash BIOS and Xbox HDD image before games can launch. Open xemu from Grev Store once and select all three files.");
            }
        }

        return new(true);
    }

    private static bool HasTomlValue(IEnumerable<string> lines, string key) => lines.Any(line =>
    {
        var trimmed = line.Trim();
        if (trimmed.StartsWith('#') || !trimmed.StartsWith(key, StringComparison.OrdinalIgnoreCase)) return false;
        var separator = trimmed.IndexOf('=');
        if (separator < 0) return false;
        return trimmed[(separator + 1)..].Trim().Trim('"', '\'').Length > 0;
    });
}
