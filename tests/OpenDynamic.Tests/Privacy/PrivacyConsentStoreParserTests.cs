using OpenDynamic.Core.Privacy;
using Xunit;

namespace OpenDynamic.Tests.Privacy;

public sealed class PrivacyConsentStoreParserTests
{
    [Fact]
    public void DecodeExecutablePath_EncodedPathWithHashes_ReplacesWithBackslashes()
    {
        string encoded = @"C:#Program Files#obs-studio#bin#64bit#obs64.exe";
        string expected = @"C:\Program Files\obs-studio\bin\64bit\obs64.exe";

        string decoded = PrivacyConsentStoreParser.DecodeExecutablePath(encoded);

        Assert.Equal(expected, decoded);
    }

    [Fact]
    public void DecodeExecutablePath_EmptyOrNull_ReturnsEmptyString()
    {
        Assert.Equal(string.Empty, PrivacyConsentStoreParser.DecodeExecutablePath(""));
        Assert.Equal(string.Empty, PrivacyConsentStoreParser.DecodeExecutablePath(null!));
    }

    [Fact]
    public void ExtractProcessDisplayName_StandardExePath_ReturnsCleanNameWithoutExtension()
    {
        string path = @"C:\Program Files\Discord\app-1.0.9000\Discord.exe";
        string name = PrivacyConsentStoreParser.ExtractProcessDisplayName(path);

        Assert.Equal("Discord", name);
    }

    [Fact]
    public void ExtractProcessDisplayName_ForwardSlashes_ReturnsCleanName()
    {
        string path = "C:/Tools/ffmpeg/bin/ffmpeg.exe";
        string name = PrivacyConsentStoreParser.ExtractProcessDisplayName(path);

        Assert.Equal("ffmpeg", name);
    }

    [Theory]
    [InlineData("Microsoft.WindowsCamera_8wekyb3d8bbwe", "Cámara de Windows")]
    [InlineData("Microsoft.WindowsSoundRecorder_8wekyb3d8bbwe", "Grabadora de voz")]
    [InlineData("windows.immersivecontrolpanel_cw5n1h2txyewy", "Configuración de Windows")]
    [InlineData("Microsoft.XboxGamingOverlay_8wekyb3d8bbwe", "Xbox Game Bar")]
    [InlineData("Microsoft.ScreenSketch_8wekyb3d8bbwe", "Herramienta Recortes")]
    [InlineData("Claude_pzs8sxrjxfjjc", "Claude")]
    [InlineData("38833FF26BA1D.UnigramPreview_g9c9v27vpyspw", "UnigramPreview")]
    public void FormatPackageName_VariousPackageFamilies_ReturnsFriendlyName(string packageId, string expected)
    {
        string result = PrivacyConsentStoreParser.FormatPackageName(packageId);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ParseEntry_NonPackaged_WithProductNameResolver_UsesResolvedName()
    {
        string rawKey = @"C:#Program Files#OBS#obs64.exe";
        var entry = PrivacyConsentStoreParser.ParseEntry(
            PrivacyResourceType.Microphone,
            rawKey,
            isNonPackaged: true,
            start: 1000L,
            stop: 0L,
            productNameResolver: path => "Open Broadcaster Software");

        Assert.Equal(PrivacyResourceType.Microphone, entry.Resource);
        Assert.Equal(rawKey, entry.AppId);
        Assert.Equal("Open Broadcaster Software", entry.DisplayName);
        Assert.True(entry.IsInUse);
    }

    [Fact]
    public void ParseEntry_NonPackaged_WithoutResolver_FallsBackToExecutableName()
    {
        string rawKey = @"C:#Games#Cyberpunk#bin#Cyberpunk2077.exe";
        var entry = PrivacyConsentStoreParser.ParseEntry(
            PrivacyResourceType.Camera,
            rawKey,
            isNonPackaged: true,
            start: 500L,
            stop: 1000L,
            productNameResolver: null);

        Assert.Equal(PrivacyResourceType.Camera, entry.Resource);
        Assert.Equal("Cyberpunk2077", entry.DisplayName);
        Assert.False(entry.IsInUse);
    }

    [Fact]
    public void ParseEntry_Packaged_FormatsPackageId()
    {
        string packageId = "Microsoft.WindowsCamera_8wekyb3d8bbwe";
        var entry = PrivacyConsentStoreParser.ParseEntry(
            PrivacyResourceType.Camera,
            packageId,
            isNonPackaged: false,
            start: 2000L,
            stop: 0L);

        Assert.Equal(PrivacyResourceType.Camera, entry.Resource);
        Assert.Equal("Cámara de Windows", entry.DisplayName);
        Assert.True(entry.IsInUse);
    }
}
