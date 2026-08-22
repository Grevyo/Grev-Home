using System.IO;
using System.Text.Json;

namespace GrevHome.Storage;

public sealed record CorruptDataRecoveryRecord(
    string OriginalPath,
    string PreservedPath,
    string Category,
    string Reason,
    DateTimeOffset PreservedAtUtc,
    long? OriginalLength);

/// <summary>
/// Preserves malformed mutable Grev Home state before any caller falls back to a clean/default
/// value. Recovery copies live under Data/Recovery so a later successful write can never turn a
/// deserialization problem into permanent loss of the only copy of the old data.
/// </summary>
public static class CorruptDataQuarantine
{
    public static bool TryPreserve(
        AppPaths paths,
        string sourcePath,
        string category,
        string reason,
        out string? preservedPath)
    {
        preservedPath = null;
        if (!File.Exists(sourcePath))
        {
            return true;
        }

        var safeCategory = SanitizeCategory(category);
        var recoveryRoot = Path.Combine(paths.Data, "Recovery", safeCategory);
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmssfff");
        var originalName = Path.GetFileName(sourcePath);
        var target = Path.Combine(
            recoveryRoot,
            $"{timestamp}-{Guid.NewGuid():N}-{originalName}.corrupt");

        long? length = null;
        try
        {
            length = new FileInfo(sourcePath).Length;
        }
        catch
        {
        }

        try
        {
            Directory.CreateDirectory(recoveryRoot);

            try
            {
                // Prefer a move so repeated reads of the same malformed file do not create an
                // unlimited number of recovery copies before the store is next rewritten.
                File.Move(sourcePath, target, overwrite: false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // If the source cannot be moved (for example another reader briefly holds it), a
                // verified copy still protects the old bytes before a later write replaces them.
                File.Copy(sourcePath, target, overwrite: false);
            }

            preservedPath = target;
            TryWriteRecoveryMetadata(
                target,
                new CorruptDataRecoveryRecord(
                    sourcePath,
                    target,
                    safeCategory,
                    string.IsNullOrWhiteSpace(reason) ? "Malformed local data" : reason.Trim(),
                    DateTimeOffset.UtcNow,
                    length));
            return true;
        }
        catch
        {
            preservedPath = null;
            return false;
        }
    }

    private static void TryWriteRecoveryMetadata(string preservedPath, CorruptDataRecoveryRecord record)
    {
        try
        {
            File.WriteAllText(
                preservedPath + ".json",
                JsonSerializer.Serialize(record, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // The preserved original bytes are the important part. Sidecar metadata is best effort.
        }
    }

    private static string SanitizeCategory(string category)
    {
        var value = new string((category ?? string.Empty)
            .Where(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_')
            .ToArray());
        return string.IsNullOrWhiteSpace(value) ? "Unknown" : value[..Math.Min(value.Length, 48)];
    }
}
