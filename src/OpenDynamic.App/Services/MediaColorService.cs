using System.Collections.Concurrent;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenDynamic.Core.Media.Color;
using Serilog;

namespace OpenDynamic.App.Services;

/// <summary>
/// Service responsible for extracting dominant accent colors from album covers in the background.
/// Enforces low-resolution sampling (32x32), per-track in-memory color caching,
/// and produces frozen brushes safe for UI consumption without bitmap memory leaks.
/// Follows Golden Rules 1, 5, and 11.
/// </summary>
public sealed class MediaColorService
{
    private readonly ConcurrentDictionary<string, RgbColor> _colorCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Fallback default brush (Standard white neutral #FFFFFF), frozen for application-wide safe reuse.
    /// </summary>
    public static readonly SolidColorBrush DefaultAccentBrush = CreateFrozenBrush(RgbColor.DefaultAccent);

    /// <summary>
    /// Gets the cached or newly extracted dominant accent color for the specified album artwork.
    /// Executes extraction on a background thread (Task.Run) using low-resolution (32x32) sampling.
    /// </summary>
    /// <param name="thumbnail">Frozen <see cref="BitmapSource"/> of the album cover.</param>
    /// <param name="trackKey">Unique track key (e.g. "Title|Artist").</param>
    /// <returns>Adjusted vibrant <see cref="RgbColor"/> ready for notch UI presentation.</returns>
    public async Task<RgbColor> GetAccentColorAsync(BitmapSource? thumbnail, string trackKey)
    {
        if (string.IsNullOrWhiteSpace(trackKey) || thumbnail == null)
        {
            return RgbColor.DefaultAccent;
        }

        if (_colorCache.TryGetValue(trackKey, out var cachedColor))
        {
            return cachedColor;
        }

        try
        {
            if (thumbnail is BitmapImage { IsDownloading: true })
            {
                return RgbColor.DefaultAccent;
            }

            int origW = thumbnail.PixelWidth;
            int origH = thumbnail.PixelHeight;
            if (origW <= 0 || origH <= 0)
            {
                return RgbColor.DefaultAccent;
            }

            const int sampleDim = 32;
            const int stride = sampleDim * 4;
            byte[] pixelBuffer = new byte[sampleDim * stride];

            double scaleX = 32.0 / origW;
            double scaleY = 32.0 / origH;

            var scaled = new TransformedBitmap(thumbnail, new ScaleTransform(scaleX, scaleY));
            var formatted = new FormatConvertedBitmap(scaled, PixelFormats.Bgra32, null, 0);
            formatted.CopyPixels(pixelBuffer, stride, 0);

            var adjustedColor = await Task.Run(() =>
            {
                try
                {
                    var dominant = DominantColorExtractor.ExtractDominantColor(
                        pixelBuffer,
                        sampleDim,
                        sampleDim,
                        stride,
                        targetSampleSize: sampleDim);

                    return AccentColorAdjuster.AdjustColor(dominant);
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "Background color extraction failed.");
                    return RgbColor.DefaultAccent;
                }
            }).ConfigureAwait(false);

            _colorCache[trackKey] = adjustedColor;
            Log.Debug("Extracted cover accent color {Hex}.", adjustedColor.ToHex());
            return adjustedColor;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to extract cover accent color. Using fallback.");
            return RgbColor.DefaultAccent;
        }
    }

    /// <summary>
    /// Creates a frozen <see cref="SolidColorBrush"/> from the given <see cref="RgbColor"/>.
    /// Freezing prevents memory leaks and allows cross-thread rendering in WPF.
    /// </summary>
    public static SolidColorBrush CreateFrozenBrush(RgbColor color)
    {
        var brush = new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B));
        brush.Freeze();
        return brush;
    }

    /// <summary>
    /// Clears the color cache.
    /// </summary>
    public void ClearCache()
    {
        _colorCache.Clear();
    }
}
