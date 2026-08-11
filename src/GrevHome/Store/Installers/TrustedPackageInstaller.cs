namespace GrevHome.Store.Installers;

public sealed record PackageInstallProgress(
    string Stage,
    string Message,
    double? Percent = null);

public sealed record PackageOperationContext(
    GrevStorePackageDefinition Package,
    string? GrevId);

public interface ITrustedPackageInstaller
{
    string InstallerId { get; }

    Task<PackageHealthSnapshot> InspectAsync(
        PackageOperationContext context,
        CancellationToken cancellationToken = default);

    Task InstallAsync(
        PackageOperationContext context,
        IProgress<PackageInstallProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task UpdateAsync(
        PackageOperationContext context,
        IProgress<PackageInstallProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task RepairAsync(
        PackageOperationContext context,
        IProgress<PackageInstallProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task UninstallAsync(
        PackageOperationContext context,
        IProgress<PackageInstallProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public sealed class TrustedPackageInstallerRegistry
{
    private readonly Dictionary<string, ITrustedPackageInstaller> _installers;

    public TrustedPackageInstallerRegistry(IEnumerable<ITrustedPackageInstaller> installers)
    {
        ArgumentNullException.ThrowIfNull(installers);
        _installers = new Dictionary<string, ITrustedPackageInstaller>(StringComparer.OrdinalIgnoreCase);

        foreach (var installer in installers)
        {
            if (string.IsNullOrWhiteSpace(installer.InstallerId))
            {
                throw new InvalidOperationException("Trusted package installers must declare a non-empty InstallerId.");
            }

            if (!_installers.TryAdd(installer.InstallerId, installer))
            {
                throw new InvalidOperationException($"Trusted installer '{installer.InstallerId}' is registered more than once.");
            }
        }
    }

    public bool TryGet(string installerId, out ITrustedPackageInstaller installer) =>
        _installers.TryGetValue(installerId, out installer!);

    public ITrustedPackageInstaller Require(GrevStorePackageDefinition package)
    {
        ArgumentNullException.ThrowIfNull(package);
        if (!_installers.TryGetValue(package.InstallerId, out var installer))
        {
            throw new InvalidOperationException($"Trusted installer '{package.InstallerId}' is not registered with Grev Home.");
        }

        return installer;
    }
}
