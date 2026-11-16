using System.Collections.Concurrent;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;
using Windows.Storage.FileProperties;

namespace OpenBar.Presentation.Dock;

/// <summary>
/// Loads real shell thumbnails for executables and shortcuts. Results are
/// cached per path/size; failures return null so the dock can show a neutral
/// fallback glyph. No icon is downloaded.
/// </summary>
public sealed class DockIconService
{
    private readonly ConcurrentDictionary<string, Task<ImageSource?>> _cache = new(StringComparer.OrdinalIgnoreCase);

    public async Task<ImageSource?> GetIconAsync(
        string? path,
        double displaySize,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        var size = (uint)Math.Clamp(Math.Round(displaySize * 2), 32, 256);
        var key = $"{path}|{size}";
        var icon = await _cache.GetOrAdd(key, _ => LoadAsync(path, size, cancellationToken)).ConfigureAwait(true);

        if (icon is null)
        {
            // Shell thumbnails can fail transiently (Explorer/thumbnail cache
            // startup). Do not keep a permanent negative cache entry.
            _ = _cache.TryRemove(key, out _);
        }

        return icon;
    }

    private static async Task<ImageSource?> LoadAsync(string path, uint size, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var file = await StorageFile.GetFileFromPathAsync(path);
            using var thumbnail = await file.GetThumbnailAsync(
                ThumbnailMode.SingleItem,
                size,
                ThumbnailOptions.UseCurrentScale);

            if (thumbnail is null || thumbnail.Size == 0)
            {
                return null;
            }

            var bitmap = new BitmapImage
            {
                DecodePixelWidth = (int)size,
                DecodePixelType = DecodePixelType.Logical
            };

            await bitmap.SetSourceAsync(thumbnail);
            return bitmap;
        }
        catch (Exception)
        {
            // Missing/broken shell thumbnails are expected for some targets;
            // the view falls back to a neutral symbol.
            return null;
        }
    }
}
