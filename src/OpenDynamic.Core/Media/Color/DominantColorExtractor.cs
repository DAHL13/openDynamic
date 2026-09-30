namespace OpenDynamic.Core.Media.Color;

/// <summary>
/// Pure algorithmic dominant color extractor for cover artwork.
/// Samples pixel data at low resolution, groups into color buckets, weights by saturation,
/// and discards near-blacks, near-whites, and low-saturation grays.
/// Strictly adheres to Golden Rule 5 (no System.Drawing, Win32, or WPF dependencies).
/// </summary>
public static class DominantColorExtractor
{
    private const int BucketCount = 16;
    private const double BucketAngle = 360.0 / BucketCount; // 22.5 deg per hue bucket

    /// <summary>
    /// Extracts the dominant vibrant color from raw BGRA (or RGBA) pixel data.
    /// </summary>
    /// <param name="pixels">Raw pixel bytes in BGRA format.</param>
    /// <param name="width">Image width in pixels.</param>
    /// <param name="height">Image height in pixels.</param>
    /// <param name="stride">Row stride in bytes. If 0 or negative, defaults to width * 4.</param>
    /// <param name="targetSampleSize">Target sample dimension along each axis (e.g. 32x32 grid).</param>
    /// <returns>Extracted dominant <see cref="RgbColor"/>, or <c>null</c> if no vibrant candidate was found.</returns>
    public static RgbColor? ExtractDominantColor(
        ReadOnlySpan<byte> pixels,
        int width,
        int height,
        int stride = 0,
        int targetSampleSize = 32)
    {
        if (width <= 0 || height <= 0 || pixels.IsEmpty)
        {
            return null;
        }

        if (stride <= 0)
        {
            stride = width * 4;
        }

        int stepX = Math.Max(1, width / targetSampleSize);
        int stepY = Math.Max(1, height / targetSampleSize);

        Span<double> bucketWeights = stackalloc double[BucketCount];
        Span<double> bucketSumR = stackalloc double[BucketCount];
        Span<double> bucketSumG = stackalloc double[BucketCount];
        Span<double> bucketSumB = stackalloc double[BucketCount];
        Span<int> bucketPixelCounts = stackalloc int[BucketCount];

        for (int y = 0; y < height; y += stepY)
        {
            int rowOffset = y * stride;
            for (int x = 0; x < width; x += stepX)
            {
                int pixelOffset = rowOffset + (x * 4);
                if (pixelOffset + 3 >= pixels.Length)
                {
                    continue;
                }

                byte b = pixels[pixelOffset];
                byte g = pixels[pixelOffset + 1];
                byte r = pixels[pixelOffset + 2];
                byte a = pixels[pixelOffset + 3];

                // Ignore transparent or translucent pixels
                if (a < 128)
                {
                    continue;
                }

                var color = new RgbColor(r, g, b, a);
                color.ToHsl(out double h, out double s, out double l);

                // Discard near-blacks (dark backgrounds)
                if (l < 0.15 || Math.Max(r, Math.Max(g, b)) < 35)
                {
                    continue;
                }

                // Discard near-whites (light backgrounds)
                if (l > 0.88 || Math.Min(r, Math.Min(g, b)) > 225)
                {
                    continue;
                }

                // Discard desaturated grays
                if (s < 0.18 || (Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b))) < 25)
                {
                    continue;
                }

                // Ponderate weight by saturation and optimal mid-lightness
                // Peak weight occurs at high saturation and balanced lightness (l ~ 0.5)
                double saturationWeight = s * s;
                double lightnessWeight = 1.0 - Math.Abs(l - 0.5);
                double weight = saturationWeight * lightnessWeight;

                int bucketIndex = (int)(h / BucketAngle) % BucketCount;
                if (bucketIndex < 0) bucketIndex += BucketCount;

                bucketWeights[bucketIndex] += weight;
                bucketSumR[bucketIndex] += r * weight;
                bucketSumG[bucketIndex] += g * weight;
                bucketSumB[bucketIndex] += b * weight;
                bucketPixelCounts[bucketIndex]++;
            }
        }

        // Find bucket with highest accumulated weight
        int bestBucket = -1;
        double maxWeight = 0.0;

        for (int i = 0; i < BucketCount; i++)
        {
            if (bucketWeights[i] > maxWeight && bucketPixelCounts[i] > 0)
            {
                maxWeight = bucketWeights[i];
                bestBucket = i;
            }
        }

        if (bestBucket == -1 || maxWeight <= 1e-6)
        {
            return null;
        }

        byte dominantR = (byte)Math.Clamp(Math.Round(bucketSumR[bestBucket] / bucketWeights[bestBucket]), 0, 255);
        byte dominantG = (byte)Math.Clamp(Math.Round(bucketSumG[bestBucket] / bucketWeights[bestBucket]), 0, 255);
        byte dominantB = (byte)Math.Clamp(Math.Round(bucketSumB[bestBucket] / bucketWeights[bestBucket]), 0, 255);

        return new RgbColor(dominantR, dominantG, dominantB);
    }
}
