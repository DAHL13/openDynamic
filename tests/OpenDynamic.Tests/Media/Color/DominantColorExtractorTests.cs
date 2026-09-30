using OpenDynamic.Core.Media.Color;
using Xunit;

namespace OpenDynamic.Tests.Media.Color;

public class DominantColorExtractorTests
{
    [Fact]
    public void ExtractDominantColor_WithClearVibrantColor_ReturnsDominantHue()
    {
        // Arrange: 32x32 image filled mostly with dark background and a solid vibrant red rectangle (R=240, G=20, B=20)
        int width = 32;
        int height = 32;
        byte[] pixels = new byte[width * height * 4];

        for (int i = 0; i < pixels.Length; i += 4)
        {
            // Background is dark navy
            pixels[i] = 10;     // B
            pixels[i + 1] = 10; // G
            pixels[i + 2] = 20; // R
            pixels[i + 3] = 255;// A
        }

        // Add prominent vibrant red patch
        for (int y = 5; y < 25; y++)
        {
            for (int x = 5; x < 25; x++)
            {
                int idx = (y * width + x) * 4;
                pixels[idx] = 10;     // B
                pixels[idx + 1] = 15; // G
                pixels[idx + 2] = 230;// R
                pixels[idx + 3] = 255;// A
            }
        }

        // Act
        var result = DominantColorExtractor.ExtractDominantColor(pixels, width, height);

        // Assert
        Assert.NotNull(result);
        var dominant = result.Value;
        Assert.True(dominant.R > 200, $"Expected high Red, got {dominant.R}");
        Assert.True(dominant.G < 40, $"Expected low Green, got {dominant.G}");
        Assert.True(dominant.B < 40, $"Expected low Blue, got {dominant.B}");
    }

    [Fact]
    public void ExtractDominantColor_WithPureGrayNoise_ReturnsNull()
    {
        // Arrange: 32x32 image filled with neutral grays (no saturation)
        int width = 32;
        int height = 32;
        byte[] pixels = new byte[width * height * 4];
        var rnd = new Random(42);

        for (int i = 0; i < pixels.Length; i += 4)
        {
            byte gray = (byte)rnd.Next(60, 180);
            pixels[i] = gray;     // B
            pixels[i + 1] = gray; // G
            pixels[i + 2] = gray; // R
            pixels[i + 3] = 255;  // A
        }

        // Act
        var result = DominantColorExtractor.ExtractDominantColor(pixels, width, height);

        // Assert: Low saturation grays must be discarded
        Assert.Null(result);
    }

    [Fact]
    public void ExtractDominantColor_WithNearBlackImage_ReturnsNull()
    {
        // Arrange: 32x32 image with nearly pitch black pixels (L < 0.15)
        int width = 32;
        int height = 32;
        byte[] pixels = new byte[width * height * 4];

        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = 12;     // B
            pixels[i + 1] = 8;  // G
            pixels[i + 2] = 15; // R
            pixels[i + 3] = 255;// A
        }

        // Act
        var result = DominantColorExtractor.ExtractDominantColor(pixels, width, height);

        // Assert: Near-blacks must be discarded
        Assert.Null(result);
    }

    [Fact]
    public void ExtractDominantColor_WithNearWhiteImage_ReturnsNull()
    {
        // Arrange: 32x32 image with near-white pixels (L > 0.88)
        int width = 32;
        int height = 32;
        byte[] pixels = new byte[width * height * 4];

        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = 240;    // B
            pixels[i + 1] = 245;// G
            pixels[i + 2] = 242;// R
            pixels[i + 3] = 255;// A
        }

        // Act
        var result = DominantColorExtractor.ExtractDominantColor(pixels, width, height);

        // Assert: Near-whites must be discarded
        Assert.Null(result);
    }

    [Fact]
    public void ExtractDominantColor_EmptyOrInvalidInput_ReturnsNull()
    {
        Assert.Null(DominantColorExtractor.ExtractDominantColor(ReadOnlySpan<byte>.Empty, 0, 0));
        Assert.Null(DominantColorExtractor.ExtractDominantColor(new byte[16], -1, 4));
    }
}

public class AccentColorAdjusterTests
{
    [Fact]
    public void AdjustColor_NullCandidate_ReturnsFallbackOrDefault()
    {
        var customFallback = RgbColor.FromRgb(200, 100, 50);

        var resultDefault = AccentColorAdjuster.AdjustColor(null);
        var resultCustom = AccentColorAdjuster.AdjustColor(null, customFallback);

        Assert.Equal(RgbColor.DefaultAccent, resultDefault);
        Assert.Equal(customFallback, resultCustom);
    }

    [Fact]
    public void AdjustColor_DarkCandidate_EnforcesMinLightness()
    {
        // Dark blue with high saturation: R=0, G=20, B=100
        var darkColor = RgbColor.FromRgb(0, 20, 100);
        darkColor.ToHsl(out double hOrig, out _, out double lOrig);
        Assert.True(lOrig < 0.30);

        var adjusted = AccentColorAdjuster.AdjustColor(darkColor, minLightness: 0.50);
        adjusted.ToHsl(out double hAdj, out double sAdj, out double lAdj);

        Assert.True(lAdj >= 0.49, $"Expected lightness >= 0.50, got {lAdj}");
        Assert.True(sAdj >= 0.50, $"Expected saturation >= 0.50, got {sAdj}");
        Assert.True(Math.Abs(hOrig - hAdj) < 5.0, $"Expected hue preserved (~{hOrig}), got {hAdj}");
    }

    [Fact]
    public void AdjustColor_PaleCandidate_EnforcesMinSaturation()
    {
        // Pale pastel red: H~0, S~0.25, L~0.60
        var pastelColor = RgbColor.FromHsl(0.0, 0.25, 0.60);

        var adjusted = AccentColorAdjuster.AdjustColor(pastelColor, minSaturation: 0.60);
        adjusted.ToHsl(out _, out double sAdj, out double lAdj);

        Assert.True(sAdj >= 0.59, $"Expected saturation >= 0.60, got {sAdj}");
        Assert.True(lAdj >= AccentColorAdjuster.DefaultMinLightness && lAdj <= AccentColorAdjuster.DefaultMaxLightness);
    }

    [Fact]
    public void AdjustColor_ExtremelyBrightCandidate_EnforcesMaxLightness()
    {
        // Blinding yellow/white: L ~ 0.95
        var brightColor = RgbColor.FromHsl(60.0, 0.80, 0.95);

        var adjusted = AccentColorAdjuster.AdjustColor(brightColor, maxLightness: 0.75);
        adjusted.ToHsl(out _, out _, out double lAdj);

        Assert.True(lAdj <= 0.76, $"Expected lightness <= 0.75, got {lAdj}");
    }

    [Fact]
    public void RgbColor_HslRoundtrip_PreservesColor()
    {
        var original = RgbColor.FromRgb(180, 60, 220);
        original.ToHsl(out double h, out double s, out double l);
        var restored = RgbColor.FromHsl(h, s, l);

        Assert.InRange(Math.Abs(original.R - restored.R), 0, 2);
        Assert.InRange(Math.Abs(original.G - restored.G), 0, 2);
        Assert.InRange(Math.Abs(original.B - restored.B), 0, 2);
    }
}
