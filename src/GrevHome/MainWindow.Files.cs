using System.IO;
using System.Windows.Threading;
using GrevHome.Files;
using GrevHome.Navigation;
using GrevHome.Views;

namespace GrevHome;

public partial class MainWindow
{
    private readonly FileSystemService _fileSystem = new();
    private readonly FileExplorerView _fileExplorerView = new();
    private readonly Stack<string?> _filePathHistory = new();
    private string? _fileCurrentPath;
    private FileTransferRequest? _fileTransfer;
    private FileRouteTransition _fileRouteTransition;
    private bool _fileOperationBusy;
    private bool _filesIntegrationReady;

    private void InitializeFilesIntegration()
    {
        if (_filesIntegrationReady)
        {
            return;
        }

        _filesIntegrationReady = true;
        _dashboardView.FilesRequested += (_, _) => OpenFiles();
        _navigation.RouteChanged += HandleFilesRouteChanged;

        _fileExplorerView.BackToDashboardRequested += (_, _) => CloseFilesToDashboard();
        _fileExplorerView.HomeRequested += (_, _) => NavigateFilesHome();
        _fileExplorerView.UpRequested += (_, _) => NavigateFilesUp();
        _fileExplorerView.RefreshRequested += (_, _) => RenderFiles();
        _fileExplorerView.NavigateRequested += NavigateFilesPath;
        _fileExplorerView.ModalOpened += (_, _) => PushFileModalHistory();
        _fileExplorerView.ModalClosed += (_, _) => CompleteFileModalHistory();
        _fileExplorerView.NameRequested += request => _ = HandleFileNameRequestAsync(request);
        _fileExplorerView.DeleteRequested += path => _ = DeleteFileItemAsync(path);
        _fileExplorerView.CopyRequested += path => BeginFileTransfer(path, FileTransferMode.Copy);
        _fileExplorerView.MoveRequested += path => BeginFileTransfer(path, FileTransferMode.Move);
        _fileExplorerView.PasteRequested += (_, _) => _ = PasteFileTransferAsync();
        _fileExplorerView.CancelTransferRequested += (_, _) => CancelFileTransfer();
    }

    private void OpenFiles()
    {
        if (!_session.HasSignedInUsers)
        {
            _navigation.Reset(Route.Login);
            return;
        }

        _filePathHistory.Clear();
        _fileCurrentPath = null;
        _fileRouteTransition = FileRouteTransition.Open;
        _navigation.Navigate(Route.Files);
    }

    private void HandleFilesRouteChanged(Route route)
    {
        if (route != Route.Files)
        {
            _fileRouteTransition = FileRouteTransition.None;
            return;
        }

        RouteHost.Content = _fileExplorerView;
        var transition = _fileRouteTransition;

        switch (transition)
        {
            case FileRouteTransition.Open:
            case FileRouteTransition.ForwardPath:
                RenderFiles();
                break;
            case FileRouteTransition.ModalPush:
                // Keep the current editor/delete overlay intact. This route entry exists so B
                // cancels it first, and the modal itself owns controller focus.
                break;
            case FileRouteTransition.ModalDismiss:
                // The modal has already closed through its own action. The matching same-route Back
                // transition restores the exact parent focus bookmark at shell ApplicationIdle.
                break;
            case FileRouteTransition.None:
                if (_fileExplorerView.IsModalOpen)
                {
                    // B/Escape reached the same-route modal Back entry. Close the overlay without
                    // raising ModalClosed again; shell history already owns this Back transition.
                    _fileExplorerView.CloseModals();
                    _fileExplorerView.ShowStatus("Action cancelled.");
                }
                else if (_filePathHistory.Count > 0)
                {
                    _fileCurrentPath = _filePathHistory.Pop();
                    RenderFiles();
                }
                else
                {
                    _fileCurrentPath = null;
                    RenderFiles();
                }
                break;
        }

        _fileRouteTransition = FileRouteTransition.None;

        // A same-route modal push is the one case where the local overlay owns landing focus.
        // All ordinary route/same-route navigation focus is finalized by MainWindow.ShellNavigation.
        if (transition == FileRouteTransition.ModalPush)
        {
            _fileExplorerView.RefocusModal();
        }
    }

    private void NavigateFilesPath(string path)
    {
        if (_fileOperationBusy)
        {
            return;
        }

        PushFilesPath(path);
    }

    private void NavigateFilesHome()
    {
        if (_fileCurrentPath is null)
        {
            return;
        }

        PushFilesPath(null);
    }

    private void NavigateFilesUp()
    {
        if (_fileCurrentPath is null)
        {
            return;
        }

        try
        {
            var parent = _fileSystem.GetParent(_fileCurrentPath);
            PushFilesPath(parent);
        }
        catch (Exception ex) when (IsFileOperationException(ex))
        {
            _fileExplorerView.ShowStatus(ex.Message);
        }
    }

    private void PushFilesPath(string? path)
    {
        _filePathHistory.Push(_fileCurrentPath);
        _fileCurrentPath = path;
        _fileRouteTransition = FileRouteTransition.ForwardPath;
        _navigation.NavigateWithinRoute(Route.Files);
    }

    private void PushFileModalHistory()
    {
        _fileRouteTransition = FileRouteTransition.ModalPush;
        _navigation.Navigate(Route.Files, allowSameRoute: true);
    }

    private void CompleteFileModalHistory()
    {
        if (_navigation.Current != Route.Files)
        {
            return;
        }

        _fileRouteTransition = FileRouteTransition.ModalDismiss;
        if (!_navigation.GoBack())
        {
            _fileRouteTransition = FileRouteTransition.None;
            Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(FocusFirstButton));
        }
    }

    private void CloseFilesToDashboard()
    {
        _fileExplorerView.CloseModals();
        _filePathHistory.Clear();
        _fileCurrentPath = null;
        _fileRouteTransition = FileRouteTransition.None;
        _navigation.Reset(Route.Dashboard);
    }

    private void RenderFiles()
    {
        try
        {
            if (_fileCurrentPath is null)
            {
                _fileExplorerView.SetHome(_fileSystem.GetHomeLocations(_paths.Root), _fileTransfer);
                return;
            }

            var entries = _fileSystem.GetEntries(_fileCurrentPath);
            _fileExplorerView.SetDirectory(
                _fileCurrentPath,
                entries,
                _fileSystem.GetParent(_fileCurrentPath) is not null,
                _fileTransfer);
        }
        catch (Exception ex) when (IsFileOperationException(ex))
        {
            _fileExplorerView.ShowStatus($"Windows could not open that location: {ex.Message}");

            if (_filePathHistory.Count > 0)
            {
                _fileCurrentPath = _filePathHistory.Pop();
                _navigation.DiscardBackEntry(Route.Files);
                RenderFiles();
            }
            else
            {
                _fileCurrentPath = null;
                _fileExplorerView.SetHome(_fileSystem.GetHomeLocations(_paths.Root), _fileTransfer);
            }
        }
    }

    private async Task HandleFileNameRequestAsync(FileNameRequest request)
    {
        if (_fileOperationBusy || _fileCurrentPath is null)
        {
            return;
        }

        _fileOperationBusy = true;
        try
        {
            if (request.Mode == FileNameEditorMode.CreateFolder)
            {
                await Task.Run(() => _fileSystem.CreateFolder(_fileCurrentPath, request.Name, _paths.Root));
                _fileExplorerView.CloseEditor($"Created folder '{request.Name.Trim()}'.");
            }
            else
            {
                if (string.IsNullOrWhiteSpace(request.SourcePath))
                {
                    throw new InvalidOperationException("No item is selected for rename.");
                }

                await Task.Run(() => _fileSystem.Rename(request.SourcePath, request.Name, _paths.Root));
                _fileExplorerView.CloseEditor($"Renamed item to '{request.Name.Trim()}'.");
            }

            RenderFiles();
        }
        catch (Exception ex) when (IsFileOperationException(ex))
        {
            _fileExplorerView.ShowEditorError(ex.Message);
        }
        finally
        {
            _fileOperationBusy = false;
        }
    }

    private async Task DeleteFileItemAsync(string path)
    {
        if (_fileOperationBusy)
        {
            return;
        }

        _fileOperationBusy = true;
        _fileExplorerView.ShowStatus("Deleting…");
        try
        {
            await Task.Run(() => _fileSystem.Delete(path, _paths.Root));
            _fileExplorerView.CloseDelete($"Deleted '{Path.GetFileName(path.TrimEnd('\\'))}'.");
            RenderFiles();
        }
        catch (Exception ex) when (IsFileOperationException(ex))
        {
            _fileExplorerView.CloseDelete($"Delete failed: {ex.Message}");
        }
        finally
        {
            _fileOperationBusy = false;
        }
    }

    private void BeginFileTransfer(string path, FileTransferMode mode)
    {
        if (_fileOperationBusy)
        {
            return;
        }

        _fileTransfer = new FileTransferRequest(path, mode);
        RenderFiles();
        _fileExplorerView.ShowStatus(
            $"{mode} ready. Navigate to the destination folder and choose Paste Here.");
    }

    private async Task PasteFileTransferAsync()
    {
        if (_fileOperationBusy || _fileTransfer is null || _fileCurrentPath is null)
        {
            return;
        }

        var transfer = _fileTransfer;
        _fileOperationBusy = true;
        _fileExplorerView.ShowStatus($"{transfer.Mode} in progress…");

        try
        {
            var target = await Task.Run(() => _fileSystem.Paste(transfer, _fileCurrentPath, _paths.Root));
            if (transfer.Mode == FileTransferMode.Move)
            {
                _fileTransfer = null;
            }

            RenderFiles();
            _fileExplorerView.ShowStatus(
                $"{transfer.Mode} complete: {target}" +
                (transfer.Mode == FileTransferMode.Copy ? " • Copy remains ready for another destination." : string.Empty));
        }
        catch (Exception ex) when (IsFileOperationException(ex))
        {
            _fileExplorerView.ShowStatus($"{transfer.Mode} failed: {ex.Message}");
        }
        finally
        {
            _fileOperationBusy = false;
        }
    }

    private void CancelFileTransfer()
    {
        _fileTransfer = null;
        RenderFiles();
        _fileExplorerView.ShowStatus("Copy/Move selection cleared.");
    }

    private static bool IsFileOperationException(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or NotSupportedException;

    private enum FileRouteTransition
    {
        None,
        Open,
        ForwardPath,
        ModalPush,
        ModalDismiss
    }
}
