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
    string? ProcessName = null);

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
