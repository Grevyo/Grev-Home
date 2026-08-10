using GrevHome.Storage;

namespace GrevHome.Apps;

public sealed record AppResolvedPaths(
    string BinaryRoot,
    string? DataRoot,
    bool UsesNativeAccountData);

public sealed class AppPathResolver
{
    private readonly AppPaths _paths;

    public AppPathResolver(AppPaths paths)
    {
        _paths = paths;
    }

    public AppResolvedPaths Resolve(AppDefinition definition, string? grevId)
    {
        ArgumentNullException.ThrowIfNull(definition);
        AppIdentity.ValidateAppId(definition.AppId);

        var binaryRoot = definition.InstallStrategy switch
        {
            InstallStrategy.SharedBinary => _paths.GetGlobalAppRoot(definition.AppId),
            InstallStrategy.SystemInstalled => _paths.GetGlobalAppRoot(definition.AppId),
            InstallStrategy.GrevIdPortable => _paths.GetProfileAppRoot(
                RequireGrevId(grevId, definition, "a GrevID-local binary"),
                definition.AppId),
            _ => throw new ArgumentOutOfRangeException(nameof(definition.InstallStrategy))
        };

        var dataRoot = definition.DataStrategy switch
        {
            DataStrategy.Global => _paths.GetGlobalAppDataRoot(definition.AppId),
            DataStrategy.GrevId => _paths.GetProfileAppDataRoot(
                RequireGrevId(grevId, definition, "GrevID-owned app data"),
                definition.AppId),
            DataStrategy.NativeAccount => null,
            _ => throw new ArgumentOutOfRangeException(nameof(definition.DataStrategy))
        };

        return new AppResolvedPaths(
            binaryRoot,
            dataRoot,
            definition.DataStrategy == DataStrategy.NativeAccount);
    }

    private static string RequireGrevId(string? grevId, AppDefinition definition, string requirement)
    {
        if (string.IsNullOrWhiteSpace(grevId))
        {
            throw new InvalidOperationException(
                $"{definition.Name} requires a persistent local account for {requirement}.");
        }

        return grevId;
    }
}
