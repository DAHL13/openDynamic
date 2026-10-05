using OpenDynamic.Core.Screenshots;

namespace OpenDynamic.Tests.Screenshots;

public sealed class ScreenshotFileFilterTests
{
    private static readonly DateTimeOffset BaseWatchStart = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("screenshot.png", true)]
    [InlineData("Screenshot_2026.PNG", true)]
    [InlineData("capture.jpg", true)]
    [InlineData("capture.JPEG", true)]
    [InlineData("screen.bmp", true)]
    [InlineData("anim.gif", true)]
    [InlineData("modern.webp", true)]
    [InlineData("document.pdf", false)]
    [InlineData("script.exe", false)]
    [InlineData("script.bat", false)]
    [InlineData("script.ps1", false)]
    [InlineData("no_extension", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsAllowedExtension_ValidatesImageFormatsStrictly(string? fileName, bool expected)
    {
        Assert.Equal(expected, ScreenshotFileFilter.IsAllowedExtension(fileName));
    }

    [Theory]
    [InlineData("capture.tmp", true)]
    [InlineData("capture.png.tmp", true)]
    [InlineData("capture.partial", true)]
    [InlineData("capture.png.partial", true)]
    [InlineData("capture.tmp.png", true)]
    [InlineData("capture.partial.png", true)]
    [InlineData("~capture.png", true)]
    [InlineData("capture~1.png", true)]
    [InlineData(".hidden.png", true)]
    [InlineData("Captura de pantalla (1).png", false)]
    [InlineData("Screenshot 2026-10-05.webp", false)]
    public void IsTemporaryOrIgnoredName_DetectsTempPartialAndTildeNames(string fileName, bool expectedIgnored)
    {
        Assert.Equal(expectedIgnored, ScreenshotFileFilter.IsTemporaryOrIgnoredName(fileName));
    }

    [Fact]
    public void ShouldAcceptFile_RejectsPreexistingFilesCreatedBeforeWatchStart()
    {
        var preexistingTime = BaseWatchStart.AddMilliseconds(-1);
        bool accepted = ScreenshotFileFilter.ShouldAcceptFile(
            @"C:\Users\Test\Pictures\Screenshots\old.png",
            fileSizeBytes: 24_500,
            fileTimestampUtc: preexistingTime,
            watchStartedUtc: BaseWatchStart);

        Assert.False(accepted);
    }

    [Fact]
    public void ShouldAcceptFile_AcceptsNewFileWithPositiveSizeAndValidExtension()
    {
        var newFileTime = BaseWatchStart.AddMilliseconds(50);
        bool accepted = ScreenshotFileFilter.ShouldAcceptFile(
            @"C:\Users\Test\Pictures\Screenshots\new_capture.png",
            fileSizeBytes: 48_120,
            fileTimestampUtc: newFileTime,
            watchStartedUtc: BaseWatchStart);

        Assert.True(accepted);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    public void ShouldAcceptFile_RejectsZeroOrNegativeFileSize(long sizeBytes)
    {
        bool accepted = ScreenshotFileFilter.ShouldAcceptFile(
            @"C:\Users\Test\Pictures\Screenshots\empty.png",
            fileSizeBytes: sizeBytes,
            fileTimestampUtc: BaseWatchStart.AddSeconds(1),
            watchStartedUtc: BaseWatchStart);

        Assert.False(accepted);
    }

    [Fact]
    public void IsPathWithinWatchedFolders_AcceptsCanonicalChildPath()
    {
        string[] watched = [@"C:\Users\Demo\Pictures\Screenshots"];
        bool inside = ScreenshotFileFilter.IsPathWithinWatchedFolders(
            @"C:\Users\Demo\Pictures\Screenshots\capture_01.png",
            watched);

        Assert.True(inside);
    }

    [Fact]
    public void IsPathWithinWatchedFolders_RejectsSiblingPrefixCollisionAndTraversal()
    {
        string[] watched = [@"C:\Users\Demo\Pictures\Screenshots"];

        // Prefix collision ("Screenshots_Other" starts with "Screenshots" string-wise)
        Assert.False(ScreenshotFileFilter.IsPathWithinWatchedFolders(
            @"C:\Users\Demo\Pictures\Screenshots_Other\capture.png",
            watched));

        // Relative traversal escaping watched folder
        Assert.False(ScreenshotFileFilter.IsPathWithinWatchedFolders(
            @"C:\Users\Demo\Pictures\Screenshots\..\secret.png",
            watched));

        // Completely outside directory
        Assert.False(ScreenshotFileFilter.IsPathWithinWatchedFolders(
            @"C:\Windows\System32\cmd.exe",
            watched));
    }

    [Fact]
    public void IsPathWithinWatchedFolders_RejectsSymlinkTargetOutsideWatchedFolders()
    {
        string[] watched = [@"C:\Users\Demo\Pictures\Screenshots"];

        // Symlink resides inside watched folder, but points outside
        bool allowed = ScreenshotFileFilter.IsPathWithinWatchedFolders(
            candidatePath: @"C:\Users\Demo\Pictures\Screenshots\link.png",
            watchedFolders: watched,
            resolvedLinkTargetPath: @"C:\Users\Demo\Documents\sensitive.png");

        Assert.False(allowed);

        // Symlink pointing inside another allowed watched folder is accepted
        string[] twoWatched =
        [
            @"C:\Users\Demo\Pictures\Screenshots",
            @"C:\Users\Demo\Pictures\SnippingTool"
        ];
        bool allowedInsideSecond = ScreenshotFileFilter.IsPathWithinWatchedFolders(
            candidatePath: @"C:\Users\Demo\Pictures\Screenshots\link.png",
            watchedFolders: twoWatched,
            resolvedLinkTargetPath: @"C:\Users\Demo\Pictures\SnippingTool\real.png");

        Assert.True(allowedInsideSecond);
    }

    [Fact]
    public void ValidateSafeImageFileOnDisk_VerifiesRealFileInsideWatchedDirectory()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "openDynamic_FilterTest_" + Guid.NewGuid().ToString("N"));
        string watchedDir = Path.Combine(tempRoot, "Screenshots");
        string outsideDir = Path.Combine(tempRoot, "Outside");
        Directory.CreateDirectory(watchedDir);
        Directory.CreateDirectory(outsideDir);

        try
        {
            string validFile = Path.Combine(watchedDir, "test.png");
            File.WriteAllBytes(validFile, [1, 2, 3, 4]);

            string outsideFile = Path.Combine(outsideDir, "outside.png");
            File.WriteAllBytes(outsideFile, [1, 2, 3, 4]);

            string exeFile = Path.Combine(watchedDir, "bad.exe");
            File.WriteAllBytes(exeFile, [1, 2, 3, 4]);

            Assert.True(ScreenshotFileFilter.ValidateSafeImageFileOnDisk(validFile, [watchedDir]));
            Assert.False(ScreenshotFileFilter.ValidateSafeImageFileOnDisk(outsideFile, [watchedDir]));
            Assert.False(ScreenshotFileFilter.ValidateSafeImageFileOnDisk(exeFile, [watchedDir]));
            Assert.False(ScreenshotFileFilter.ValidateSafeImageFileOnDisk(Path.Combine(watchedDir, "missing.png"), [watchedDir]));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }
}
