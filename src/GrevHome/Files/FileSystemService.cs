using System.IO;

namespace GrevHome.Files;

public enum FileEntryKind
{
    Drive,
    Folder,
    File
}

public sealed record FileBrowserEntry(
    string Path,
    string Name,
    FileEntryKind Kind,
    long? SizeBytes,
    DateTimeOffset? ModifiedAt,
    string Detail);

public sealed record FileHomeLocation(
    string Name,
    string Path,
    string Detail,
    FileEntryKind Kind);

public enum FileTransferMode
{
    Copy,
    Move
}

public sealed record FileTransferRequest(
    string SourcePath,
    FileTransferMode Mode);

public sealed class FileSystemService
{
    public IReadOnlyList<FileHomeLocation> GetHomeLocations(string grevHomeRoot)
    {
        var locations = new List<FileHomeLocation>();
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var testArea = EnsureTestArea(grevHomeRoot);

        AddKnownFolder(locations, "Test Area", testArea, "Disposable Grev Home file-operation sandbox");
        AddKnownFolder(locations, "Downloads", Path.Combine(userProfile, "Downloads"), "Windows Downloads");
        AddKnownFolder(locations, "Documents", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Windows Documents");
        AddKnownFolder(locations, "Pictures", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Windows Pictures");
        AddKnownFolder(locations, "Grev Home Data", grevHomeRoot, "Profiles, app data and Grev Home machine data");

        foreach (var drive in DriveInfo.GetDrives().Where(drive => drive.IsReady))
        {
            try
            {
                var label = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? "No label" : drive.VolumeLabel;
                locations.Add(new FileHomeLocation(
                    $"Drive {drive.Name.TrimEnd('\\')}",
                    drive.RootDirectory.FullName,
                    $"{label} • {drive.DriveType} • {FormatBytes(drive.AvailableFreeSpace)} free / {FormatBytes(drive.TotalSize)}",
                    FileEntryKind.Drive));
            }
            catch (IOException)
            {
                // A removable/network drive can disappear while the home view is being built.
            }
            catch (UnauthorizedAccessException)
            {
                // Ignore drives Windows refuses to query rather than breaking the whole Files surface.
            }
        }

        return locations;
    }

    public IReadOnlyList<FileBrowserEntry> GetEntries(string directoryPath)
    {
        var directory = new DirectoryInfo(NormalizeDirectory(directoryPath));
        if (!directory.Exists)
        {
            throw new DirectoryNotFoundException($"Folder not found: {directory.FullName}");
        }

        var entries = new List<FileBrowserEntry>();

        foreach (var child in directory.EnumerateDirectories())
        {
            try
            {
                entries.Add(new FileBrowserEntry(
                    child.FullName,
                    child.Name,
                    FileEntryKind.Folder,
                    null,
                    child.LastWriteTimeUtc,
                    "Folder"));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Individual inaccessible entries should not prevent the rest of the directory from rendering.
            }
        }

        foreach (var child in directory.EnumerateFiles())
        {
            try
            {
                entries.Add(new FileBrowserEntry(
                    child.FullName,
                    child.Name,
                    FileEntryKind.File,
                    child.Length,
                    child.LastWriteTimeUtc,
                    $"{FormatBytes(child.Length)} • {child.Extension.TrimStart('.').ToUpperInvariant()}".TrimEnd(' ', '•')));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Individual inaccessible entries should not prevent the rest of the directory from rendering.
            }
        }

        return entries
            .OrderBy(entry => entry.Kind == FileEntryKind.Folder ? 0 : 1)
            .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public string? GetParent(string directoryPath)
    {
        var normalized = NormalizeDirectory(directoryPath);
        return Directory.GetParent(normalized)?.FullName;
    }

    public string CreateFolder(string directoryPath, string folderName)
    {
        var parent = NormalizeDirectory(directoryPath);
        var name = ValidateName(folderName);
        var target = Path.Combine(parent, name);

        if (Directory.Exists(target) || File.Exists(target))
        {
            throw new IOException($"'{name}' already exists in this folder.");
        }

        return Directory.CreateDirectory(target).FullName;
    }

    public string Rename(string sourcePath, string newName)
    {
        var source = NormalizeExistingPath(sourcePath);
        EnsureNotRoot(source);
        var name = ValidateName(newName);
        var parent = Path.GetDirectoryName(source)
            ?? throw new InvalidOperationException("That item cannot be renamed.");
        var target = Path.Combine(parent, name);

        if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
        {
            return source;
        }

        EnsureTargetDoesNotExist(target);

        if (Directory.Exists(source))
        {
            Directory.Move(source, target);
        }
        else
        {
            File.Move(source, target);
        }

        return target;
    }

    public void Delete(string sourcePath)
    {
        var source = NormalizeExistingPath(sourcePath);
        EnsureNotRoot(source);

        if (Directory.Exists(source))
        {
            Directory.Delete(source, recursive: true);
        }
        else
        {
            File.Delete(source);
        }
    }

    public string Paste(FileTransferRequest transfer, string destinationDirectory)
    {
        var source = NormalizeExistingPath(transfer.SourcePath);
        EnsureNotRoot(source);
        var destination = NormalizeDirectory(destinationDirectory);
        var name = Path.GetFileName(source.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var target = Path.Combine(destination, name);

        EnsureTargetDoesNotExist(target);
        EnsureDestinationIsNotInsideSource(source, target);

        if (transfer.Mode == FileTransferMode.Copy)
        {
            if (Directory.Exists(source))
            {
                CopyDirectory(source, target);
            }
            else
            {
                File.Copy(source, target, overwrite: false);
            }
        }
        else
        {
            if (Directory.Exists(source))
            {
                Directory.Move(source, target);
            }
            else
            {
                File.Move(source, target);
            }
        }

        return target;
    }

    private static string EnsureTestArea(string grevHomeRoot)
    {
        var root = Path.GetFullPath(grevHomeRoot);
        Directory.CreateDirectory(root);

        var testArea = Path.Combine(root, "TestArea");
        var isNew = !Directory.Exists(testArea);
        Directory.CreateDirectory(testArea);

        if (!isNew)
        {
            return testArea;
        }

        Directory.CreateDirectory(Path.Combine(testArea, "Folder A", "Folder B"));
        Directory.CreateDirectory(Path.Combine(testArea, "Copy Move Test"));
        Directory.CreateDirectory(Path.Combine(testArea, "Destination"));

        File.WriteAllText(
            Path.Combine(testArea, "Test File 1.txt"),
            "Grev Home disposable test file 1. Safe to rename, copy, move or delete during UI testing.");
        File.WriteAllText(
            Path.Combine(testArea, "Test File 2.txt"),
            "Grev Home disposable test file 2. Safe to rename, copy, move or delete during UI testing.");

        return testArea;
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);

        try
        {
            foreach (var file in Directory.EnumerateFiles(source))
            {
                File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: false);
            }

            foreach (var directory in Directory.EnumerateDirectories(source))
            {
                CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
            }
        }
        catch
        {
            try
            {
                if (Directory.Exists(destination))
                {
                    Directory.Delete(destination, recursive: true);
                }
            }
            catch
            {
                // Best-effort rollback. Preserve the original copy failure.
            }

            throw;
        }
    }

    private static void AddKnownFolder(
        ICollection<FileHomeLocation> locations,
        string name,
        string path,
        string detail)
    {
        if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
        {
            locations.Add(new FileHomeLocation(name, Path.GetFullPath(path), detail, FileEntryKind.Folder));
        }
    }

    private static string NormalizeDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException("No folder is selected.");
        }

        return Path.GetFullPath(path);
    }

    private static string NormalizeExistingPath(string path)
    {
        var normalized = Path.GetFullPath(path);
        if (!Directory.Exists(normalized) && !File.Exists(normalized))
        {
            throw new FileNotFoundException("The selected file or folder no longer exists.", normalized);
        }

        return normalized;
    }

    private static string ValidateName(string name)
    {
        name = name.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException("Enter a name.");
        }

        if (name is "." or ".." || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new InvalidOperationException("That name contains characters Windows cannot use in a file or folder name.");
        }

        return name;
    }

    private static void EnsureTargetDoesNotExist(string target)
    {
        if (Directory.Exists(target) || File.Exists(target))
        {
            throw new IOException($"'{Path.GetFileName(target)}' already exists in the destination.");
        }
    }

    private static void EnsureNotRoot(string path)
    {
        var root = Path.GetPathRoot(path);
        if (!string.IsNullOrEmpty(root) &&
            string.Equals(Path.GetFullPath(path).TrimEnd('\\'), Path.GetFullPath(root).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Drive roots cannot be renamed, moved, copied or deleted.");
        }
    }

    private static void EnsureDestinationIsNotInsideSource(string source, string target)
    {
        if (!Directory.Exists(source))
        {
            return;
        }

        var sourceWithSeparator = Path.GetFullPath(source).TrimEnd('\\') + Path.DirectorySeparatorChar;
        var targetFull = Path.GetFullPath(target);
        if (targetFull.StartsWith(sourceWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("A folder cannot be copied or moved inside itself.");
        }
    }

    private static string FormatBytes(long bytes)
    {
        const double gibibyte = 1024d * 1024d * 1024d;
        const double mebibyte = 1024d * 1024d;
        const double kibibyte = 1024d;

        if (bytes >= gibibyte) return $"{bytes / gibibyte:0.0} GB";
        if (bytes >= mebibyte) return $"{bytes / mebibyte:0.0} MB";
        if (bytes >= kibibyte) return $"{bytes / kibibyte:0.0} KB";
        return $"{bytes} B";
    }
}
