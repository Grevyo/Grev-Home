using System.Text.Json.Serialization;

namespace GrevHome.Apps;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AppKind
{
    Application,
    GameLauncher,
    Emulator,
    Utility,
    Media,
    SystemTool
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum InstallStrategy
{
    SharedBinary,
    GrevIdPortable,
    SystemInstalled
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DataStrategy
{
    GrevId,
    Global,
    NativeAccount
}

public sealed record AppLaunchDefinition(
    string Executable,
    string? Arguments = null,
    string? WorkingDirectory = null,
    string? ProcessName = null,
    bool SingleInstance = false,
    IReadOnlyList<string>? AdditionalProcessNames = null,
    bool? TrackDescendantProcesses = null,
    bool? ForceKillEntireProcessTree = null,
    string? ActivationUri = null,
    bool TrackForegroundUsageOnly = false)
{
    // Nullable package flags preserve the historical runtime behavior for manifests written
    // before launcher-specific process tracking existed. Packages such as Steam can opt out of
    // adopting/killing game descendants while ordinary apps keep full process-tree ownership.
    public bool EffectiveTrackDescendantProcesses => TrackDescendantProcesses ?? true;
    public bool EffectiveForceKillEntireProcessTree => ForceKillEntireProcessTree ?? true;

    public IReadOnlyList<string> DeclaredProcessNames =>
        new[] { ProcessName }
            .Concat(AdditionalProcessNames ?? Array.Empty<string>())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
}

public sealed record AppDefinition(
    string AppId,
    string Name,
    AppKind Kind,
    InstallStrategy InstallStrategy,
    DataStrategy DataStrategy,
    AppLaunchDefinition Launch,
    bool SupportsController = false,
    string? Description = null);

public sealed record InstalledAppManifest(
    AppDefinition Definition,
    string Version,
    DateTimeOffset InstalledAtUtc,
    string? OwnerGrevId = null);
