using System.Windows.Media.Imaging;

namespace GrevHome.Presentation;

/// <summary>Bounded background decoding; frozen images can safely cross dispatchers.</summary>
public sealed class DashboardArtworkLoader
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, BitmapImage> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<string> _order = new();

    public async Task<BitmapImage> LoadAsync(string path, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var file = new FileInfo(Path.GetFullPath(path));
                var key = $"{file.FullName}|{file.LastWriteTimeUtc.Ticks}|{file.Length}";
                if (_cache.TryGetValue(key, out var cached)) return cached;
                using var stream = file.OpenRead();
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.DecodePixelWidth = 1920;
                image.StreamSource = stream;
                image.EndInit();
                image.Freeze();
                cancellationToken.ThrowIfCancellationRequested();
                while (_order.Count >= 4) _cache.Remove(_order.Dequeue());
                _cache.Add(key, image);
                _order.Enqueue(key);
                return image;
            }, cancellationToken);
        }
        finally { _gate.Release(); }
    }
}
