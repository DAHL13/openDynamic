using System.Text.RegularExpressions;
using OpenDynamic.Core.Screenshots;

namespace OpenDynamic.Tests.Screenshots;

public sealed class ScreenshotSecurityAndPrivacyTests
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "openDynamic.sln")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir!.FullName;
    }

    [Fact]
    public void ScreenshotAppSources_NeverLogPathsFileNamesOrUserNames()
    {
        string root = FindRepoRoot();
        string[] filesToAudit =
        [
            Path.Combine(root, "src", "OpenDynamic.App", "Services", "ScreenshotWatcherService.cs"),
            Path.Combine(root, "src", "OpenDynamic.App", "Widgets", "Screenshot", "ScreenshotWidget.cs")
        ];

        var logCallRegex = new Regex(@"Log\.(Information|Warning|Error|Debug|Fatal)\s*\(([^;]+)\);", RegexOptions.Singleline);

        foreach (string file in filesToAudit)
        {
            Assert.True(File.Exists(file), $"Expected source file to exist: {file}");
            string source = File.ReadAllText(file);

            foreach (Match match in logCallRegex.Matches(source))
            {
                string logArgs = match.Groups[2].Value;
                Assert.DoesNotContain(".FilePath", logArgs, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(".FullPath", logArgs, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(".FileName", logArgs, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("normalizedPath", logArgs, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("UserName", logArgs, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void ScreenshotWidget_UsesStrictlyDragDropEffectsCopy_AndNeverPermanentFileDelete()
    {
        string root = FindRepoRoot();
        string widgetFile = Path.Combine(root, "src", "OpenDynamic.App", "Widgets", "Screenshot", "ScreenshotWidget.cs");
        string watcherFile = Path.Combine(root, "src", "OpenDynamic.App", "Services", "ScreenshotWatcherService.cs");

        string widgetSource = File.ReadAllText(widgetFile);
        string watcherSource = File.ReadAllText(watcherFile);

        Assert.Contains("DragDropEffects.Copy", widgetSource, StringComparison.Ordinal);
        Assert.DoesNotContain("DragDropEffects.Move", widgetSource, StringComparison.Ordinal);
        Assert.DoesNotContain("File.Delete", widgetSource, StringComparison.Ordinal);
        Assert.DoesNotContain("File.Delete", watcherSource, StringComparison.Ordinal);
        Assert.Contains("BitmapCacheOption.OnLoad", watcherSource, StringComparison.Ordinal);
        Assert.Contains(".Freeze()", watcherSource, StringComparison.Ordinal);
    }

    [Fact]
    public void NativeMethods_RecycleBinHelper_UsesAllowUndoFlag()
    {
        string root = FindRepoRoot();
        string nativeMethodsFile = Path.Combine(root, "src", "OpenDynamic.App", "Native", "NativeMethods.cs");
        string source = File.ReadAllText(nativeMethodsFile);

        Assert.Contains("SHFileOperationW", source, StringComparison.Ordinal);
        Assert.Contains("FOF_ALLOWUNDO", source, StringComparison.Ordinal);
        Assert.Contains("SHGetKnownFolderPath", source, StringComparison.Ordinal);
        Assert.Contains("B7BEDE81-DF94-4682-A7D8-57A52620B86F", source, StringComparison.OrdinalIgnoreCase);
    }
}
