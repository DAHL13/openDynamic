namespace OpenDynamic.Core.Screenshots;

/// <summary>
/// Pure domain filter and path validator for screenshot files.
/// Enforces allowed image extensions (.png, .jpg, .jpeg, .bmp, .gif, .webp),
/// rejects temporary/partial files (.tmp, .partial, names containing '~'),
/// rejects pre-existing files created before watcher activation,
/// and validates canonical paths and symbolic links against watched directory boundaries.
/// </summary>
public static class ScreenshotFileFilter
{
    private static readonly HashSet<string> AllowedExtensionsSet = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png",
        ".jpg",
        ".jpeg",
        ".bmp",
        ".gif",
        ".webp"
    };

    private static readonly string[] IgnoredSuffixes =
    [
        ".tmp",
        ".partial",
        ".crdownload",
        ".part"
    ];

    public static IReadOnlySet<string> AllowedExtensions => AllowedExtensionsSet;

    /// <summary>
    /// Determines whether the file path or file name has an allowed screenshot image extension
    /// and is not a temporary or partial file.
    /// </summary>
    public static bool IsAllowedExtension(string? filePathOrName)
    {
        if (string.IsNullOrWhiteSpace(filePathOrName))
        {
            return false;
        }

        string ext = Path.GetExtension(filePathOrName);
        if (string.IsNullOrWhiteSpace(ext))
        {
            return false;
        }

        return AllowedExtensionsSet.Contains(ext);
    }

    /// <summary>
    /// Determines whether the file name represents a temporary, partial, or backup file
    /// (.tmp, .partial, or containing '~').
    /// </summary>
    public static bool IsTemporaryOrIgnoredName(string? filePathOrName)
    {
        if (string.IsNullOrWhiteSpace(filePathOrName))
        {
            return true;
        }

        string fileName = Path.GetFileName(filePathOrName.Trim());
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return true;
        }

        if (fileName.Contains('~', StringComparison.Ordinal))
        {
            return true;
        }

        if (fileName.StartsWith('.'))
        {
            return true;
        }

        foreach (string suffix in IgnoredSuffixes)
        {
            if (fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        // Also reject compound temporary names like "capture.tmp.png" or "capture.partial.png"
        string nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
        foreach (string suffix in IgnoredSuffixes)
        {
            if (nameWithoutExt.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Checks whether a candidate file name is both non-temporary and has an allowed image extension.
    /// </summary>
    public static bool IsValidCandidateName(string? filePathOrName)
    {
        if (string.IsNullOrWhiteSpace(filePathOrName))
        {
            return false;
        }

        if (IsTemporaryOrIgnoredName(filePathOrName))
        {
            return false;
        }

        return IsAllowedExtension(filePathOrName);
    }

    /// <summary>
    /// Evaluates whether a candidate file meets all pure filter criteria:
    /// valid image extension, non-temporary name, size &gt; 0, and created/written at or after surveillance started.
    /// </summary>
    public static bool ShouldAcceptFile(
        string? filePath,
        long fileSizeBytes,
        DateTimeOffset fileTimestampUtc,
        DateTimeOffset watchStartedUtc)
    {
        if (!IsValidCandidateName(filePath))
        {
            return false;
        }

        if (fileSizeBytes <= 0)
        {
            return false;
        }

        if (fileTimestampUtc < watchStartedUtc)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Pure canonical path boundary validation.
    /// Verifies that <paramref name="candidatePath"/> resides inside at least one of <paramref name="watchedFolders"/>
    /// and, if a symbolic link target (<paramref name="resolvedLinkTargetPath"/>) is present, that the target
    /// also resides strictly within the watched folders.
    /// </summary>
    public static bool IsPathWithinWatchedFolders(
        string? candidatePath,
        IEnumerable<string>? watchedFolders,
        string? resolvedLinkTargetPath = null)
    {
        if (string.IsNullOrWhiteSpace(candidatePath) || watchedFolders == null)
        {
            return false;
        }

        List<string> normalizedFolders = new();
        foreach (string folder in watchedFolders)
        {
            if (string.IsNullOrWhiteSpace(folder))
            {
                continue;
            }

            try
            {
                string fullFolder = Path.GetFullPath(folder.Trim());
                fullFolder = EnsureTrailingSeparator(fullFolder);
                normalizedFolders.Add(fullFolder);
            }
            catch
            {
                // Ignore malformed folder path
            }
        }

        if (normalizedFolders.Count == 0)
        {
            return false;
        }

        string canonicalCandidate;
        try
        {
            canonicalCandidate = Path.GetFullPath(candidatePath.Trim());
        }
        catch
        {
            return false;
        }

        if (!IsCanonicalFileInsideAnyFolder(canonicalCandidate, normalizedFolders))
        {
            return false;
        }

        if (resolvedLinkTargetPath != null)
        {
            if (string.IsNullOrWhiteSpace(resolvedLinkTargetPath))
            {
                return false;
            }

            string canonicalTarget;
            try
            {
                canonicalTarget = Path.GetFullPath(resolvedLinkTargetPath.Trim());
            }
            catch
            {
                return false;
            }

            if (!IsCanonicalFileInsideAnyFolder(canonicalTarget, normalizedFolders))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Validates a file on disk prior to any action (thumbnail load, open, reveal, copy, or recycle).
    /// Ensures allowed image extension, canonical containment within watched folders, and rejects
    /// symbolic links / reparse points that point outside the watched folders.
    /// </summary>
    public static bool ValidateSafeImageFileOnDisk(string? filePath, IEnumerable<string>? watchedFolders)
    {
        if (!IsValidCandidateName(filePath) || watchedFolders == null)
        {
            return false;
        }

        try
        {
            string fullPath = Path.GetFullPath(filePath!);
            var fileInfo = new FileInfo(fullPath);
            if (!fileInfo.Exists)
            {
                return false;
            }

            string? resolvedTarget = null;

            // Check if the file itself is a symbolic link or reparse point
            if ((fileInfo.Attributes & FileAttributes.ReparsePoint) != 0 || fileInfo.LinkTarget != null)
            {
                var targetInfo = fileInfo.ResolveLinkTarget(returnFinalTarget: true);
                if (targetInfo == null || string.IsNullOrWhiteSpace(targetInfo.FullName))
                {
                    return false;
                }

                resolvedTarget = targetInfo.FullName;
                if (!IsValidCandidateName(resolvedTarget))
                {
                    return false;
                }
            }

            // Check if any parent directory up to root is a symbolic link escaping watched folders
            var dir = fileInfo.Directory;
            if (dir != null && ((dir.Attributes & FileAttributes.ReparsePoint) != 0 || dir.LinkTarget != null))
            {
                var dirTarget = dir.ResolveLinkTarget(returnFinalTarget: true);
                if (dirTarget == null || string.IsNullOrWhiteSpace(dirTarget.FullName))
                {
                    return false;
                }

                string mappedFileInTargetDir = Path.Combine(dirTarget.FullName, fileInfo.Name);
                resolvedTarget ??= mappedFileInTargetDir;
            }

            return IsPathWithinWatchedFolders(fullPath, watchedFolders, resolvedTarget);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsCanonicalFileInsideAnyFolder(string canonicalFilePath, List<string> normalizedWatchedFolders)
    {
        foreach (string folderWithSep in normalizedWatchedFolders)
        {
            if (canonicalFilePath.StartsWith(folderWithSep, StringComparison.OrdinalIgnoreCase) &&
                canonicalFilePath.Length > folderWithSep.Length)
            {
                return true;
            }
        }

        return false;
    }

    private static string EnsureTrailingSeparator(string path)
    {
        if (path.EndsWith(Path.DirectorySeparatorChar) || path.EndsWith(Path.AltDirectorySeparatorChar))
        {
            return path;
        }

        return path + Path.DirectorySeparatorChar;
    }
}
