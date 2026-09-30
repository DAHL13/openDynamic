namespace OpenDynamic.Core.Privacy;

/// <summary>
/// Helper for decoding and sanitizing Windows ConsentStore registry keys and package identifiers.
/// Pure C# (Regla de Oro 5) with zero dependencies on Windows UI or platform-specific libraries.
/// </summary>
public static class PrivacyConsentStoreParser
{
    /// <summary>
    /// Decodes an encoded executable file path from a Windows ConsentStore 'NonPackaged' subkey
    /// where directory separators ('\') are replaced by '#' characters.
    /// </summary>
    public static string DecodeExecutablePath(string encodedKeyName)
    {
        if (string.IsNullOrWhiteSpace(encodedKeyName))
        {
            return string.Empty;
        }

        return encodedKeyName.Replace('#', '\\');
    }

    /// <summary>
    /// Extracts a user-friendly application display name from a decoded file path.
    /// </summary>
    public static string ExtractProcessDisplayName(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return string.Empty;
        }

        int lastSeparator = Math.Max(filePath.LastIndexOf('\\'), filePath.LastIndexOf('/'));
        string fileName = lastSeparator >= 0 && lastSeparator < filePath.Length - 1
            ? filePath[(lastSeparator + 1)..]
            : filePath;

        if (fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return fileName[..^4];
        }

        return fileName;
    }

    /// <summary>
    /// Converts a Windows Store / MSIX package family name or ID into a clean, human-readable name.
    /// </summary>
    public static string FormatPackageName(string packageFamilyOrId)
    {
        if (string.IsNullOrWhiteSpace(packageFamilyOrId))
        {
            return string.Empty;
        }

        if (packageFamilyOrId.Contains("Microsoft.WindowsCamera", StringComparison.OrdinalIgnoreCase))
        {
            return "Cámara de Windows";
        }
        if (packageFamilyOrId.Contains("WindowsSoundRecorder", StringComparison.OrdinalIgnoreCase))
        {
            return "Grabadora de voz";
        }
        if (packageFamilyOrId.Contains("immersivecontrolpanel", StringComparison.OrdinalIgnoreCase))
        {
            return "Configuración de Windows";
        }
        if (packageFamilyOrId.Contains("XboxGamingOverlay", StringComparison.OrdinalIgnoreCase))
        {
            return "Xbox Game Bar";
        }
        if (packageFamilyOrId.Contains("ScreenSketch", StringComparison.OrdinalIgnoreCase))
        {
            return "Herramienta Recortes";
        }

        // Clean PackageFamilyName e.g. "Claude_pzs8sxrjxfjjc" -> "Claude"
        int underscoreIdx = packageFamilyOrId.IndexOf('_');
        string baseName = underscoreIdx > 0 ? packageFamilyOrId[..underscoreIdx] : packageFamilyOrId;

        // If it starts with a publisher ID e.g. "38833FF26BA1D.UnigramPreview" -> "UnigramPreview"
        int dotIdx = baseName.LastIndexOf('.');
        if (dotIdx > 0 && dotIdx < baseName.Length - 1)
        {
            string tail = baseName[(dotIdx + 1)..];
            if (!string.IsNullOrWhiteSpace(tail))
            {
                return tail;
            }
        }

        return baseName;
    }

    /// <summary>
    /// Creates a evaluated <see cref="PrivacyAccessEntry"/> from raw registry values.
    /// </summary>
    public static PrivacyAccessEntry ParseEntry(
        PrivacyResourceType resource,
        string rawKeyName,
        bool isNonPackaged,
        long start,
        long stop,
        Func<string, string?>? productNameResolver = null)
    {
        string friendlyName;

        if (isNonPackaged)
        {
            string decodedPath = DecodeExecutablePath(rawKeyName);
            string? resolvedName = null;

            if (productNameResolver != null)
            {
                try
                {
                    resolvedName = productNameResolver(decodedPath);
                }
                catch
                {
                    // Fall back
                }
            }

            friendlyName = !string.IsNullOrWhiteSpace(resolvedName)
                ? resolvedName.Trim()
                : ExtractProcessDisplayName(decodedPath);
        }
        else
        {
            friendlyName = FormatPackageName(rawKeyName);
        }

        return new PrivacyAccessEntry
        {
            Resource = resource,
            AppId = rawKeyName,
            DisplayName = friendlyName,
            LastUsedTimeStart = start,
            LastUsedTimeStop = stop
        };
    }
}
