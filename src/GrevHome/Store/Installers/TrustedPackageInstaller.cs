namespace GrevHome.Store.Installers;

public sealed record PackageInstallProgress(
    string Stage,
    string Message,
    double? Percent = null);

public sealed record PackageOperationContext(
    GrevStorePackageDefinition Package,
    string? GrevId);

public enum TrustedPackageOperationKind
{
    Install,
    Update,
    Repair,
    Uninstall
}

public sealed record TrustedPackageOperationResult(
    TrustedPackageOperationKind Operation,
    GrevStorePackageDefinition Package,
    string? GrevId,
    bool Succeeded,
    string? ErrorMessage);

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

            var observed = new ObservedTrustedPackageInstaller(installer, RaiseOperationCompleted);
            if (!_installers.TryAdd(installer.InstallerId, observed))
            {
                throw new InvalidOperationException($"Trusted installer '{installer.InstallerId}' is registered more than once.");
            }
        }
    }

    /// <summary>
    /// Raised only for explicit install/update/repair/uninstall work. Health inspection is excluded
    /// because it runs frequently while Store/Admin surfaces refresh and is not user activity.
    /// Subscriber failures are isolated from the trusted package operation itself.
    /// </summary>
    public event Action<TrustedPackageOperationResult>? OperationCompleted;

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

    private void RaiseOperationCompleted(TrustedPackageOperationResult result)
    {
        var handlers = OperationCompleted;
        if (handlers is null)
        {
            return;
        }

        foreach (Action<TrustedPackageOperationResult> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(result);
            }
            catch
            {
                // Activity/telemetry observers must never change the package operation result.
            }
        }
    }

    private sealed class ObservedTrustedPackageInstaller : ITrustedPackageInstaller
    {
        private readonly ITrustedPackageInstaller _inner;
        private readonly Action<TrustedPackageOperationResult> _completed;

        public ObservedTrustedPackageInstaller(
            ITrustedPackageInstaller inner,
            Action<TrustedPackageOperationResult> completed)
        {
            _inner = inner;
            _completed = completed;
        }

        public string InstallerId => _inner.InstallerId;

        public Task<PackageHealthSnapshot> InspectAsync(
            PackageOperationContext context,
            CancellationToken cancellationToken = default) =>
            _inner.InspectAsync(context, cancellationToken);

        public Task InstallAsync(
            PackageOperationContext context,
            IProgress<PackageInstallProgress>? progress = null,
            CancellationToken cancellationToken = default) =>
            RunObservedAsync(
                TrustedPackageOperationKind.Install,
                context,
                () => _inner.InstallAsync(context, progress, cancellationToken));

        public Task UpdateAsync(
            PackageOperationContext context,
            IProgress<PackageInstallProgress>? progress = null,
            CancellationToken cancellationToken = default) =>
            RunObservedAsync(
                TrustedPackageOperationKind.Update,
                context,
                () => _inner.UpdateAsync(context, progress, cancellationToken));

        public Task RepairAsync(
            PackageOperationContext context,
            IProgress<PackageInstallProgress>? progress = null,
            CancellationToken cancellationToken = default) =>
            RunObservedAsync(
                TrustedPackageOperationKind.Repair,
                context,
                () => _inner.RepairAsync(context, progress, cancellationToken));

        public Task UninstallAsync(
            PackageOperationContext context,
            IProgress<PackageInstallProgress>? progress = null,
            CancellationToken cancellationToken = default) =>
            RunObservedAsync(
                TrustedPackageOperationKind.Uninstall,
                context,
                () => _inner.UninstallAsync(context, progress, cancellationToken));

        private async Task RunObservedAsync(
            TrustedPackageOperationKind operation,
            PackageOperationContext context,
            Func<Task> action)
        {
            try
            {
                await action();
                _completed(new TrustedPackageOperationResult(
                    operation,
                    context.Package,
                    context.GrevId,
                    Succeeded: true,
                    ErrorMessage: null));
            }
            catch (Exception ex)
            {
                _completed(new TrustedPackageOperationResult(
                    operation,
                    context.Package,
                    context.GrevId,
                    Succeeded: false,
                    ErrorMessage: ex.Message));
                throw;
            }
        }
    }
}
