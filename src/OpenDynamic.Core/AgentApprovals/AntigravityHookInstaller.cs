using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OpenDynamic.Core.AgentApprovals;

/// <summary>
/// Manages non-destructive merging, preview, backup, and disconnection of openDynamic hooks
/// in Antigravity's lifecycle hooks configuration (hooks.json) (Golden Rule 14).
/// Pure Core implementation without WPF or native dependencies.
/// </summary>
public static class AntigravityHookInstaller
{
    public const string HookKey = "openDynamic-approvals";
    public const string DefaultMatcher = "run_command|write_to_file|replace_file_content|multi_replace_file_content";
    public const int DefaultHookTimeoutSeconds = 120;

    /// <summary>
    /// Gets default machine-global hooks file path: ~/.gemini/config/hooks.json
    /// </summary>
    public static string GetDefaultGlobalHooksFilePath()
    {
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(userProfile, ".gemini", "config", "hooks.json");
    }

    /// <summary>
    /// Locates the OpenDynamic.Hook.exe binary, searching app install directory and dev build paths.
    /// </summary>
    public static string ResolveHookExecutablePath(string? customPath = null)
    {
        if (!string.IsNullOrWhiteSpace(customPath) && File.Exists(customPath))
        {
            return Path.GetFullPath(customPath);
        }

        string baseDir = AppDomain.CurrentDomain.BaseDirectory;

        // 1. Packaged location: {app}\hook\OpenDynamic.Hook.exe
        string packagedPath = Path.Combine(baseDir, "hook", "OpenDynamic.Hook.exe");
        if (File.Exists(packagedPath)) return packagedPath;

        // 2. Colocated in base directory: {app}\OpenDynamic.Hook.exe
        string colocatedPath = Path.Combine(baseDir, "OpenDynamic.Hook.exe");
        if (File.Exists(colocatedPath)) return colocatedPath;

        // 3. Dev / test publish output
        string publishPath = Path.Combine(baseDir, "..", "..", "..", "..", "OpenDynamic.Hook", "bin", "Release", "publish", "OpenDynamic.Hook.exe");
        if (File.Exists(publishPath)) return Path.GetFullPath(publishPath);

        // 4. Dev debug output
        string debugPath = Path.Combine(baseDir, "..", "..", "..", "..", "OpenDynamic.Hook", "bin", "Debug", "net10.0", "OpenDynamic.Hook.exe");
        if (File.Exists(debugPath)) return Path.GetFullPath(debugPath);

        return packagedPath;
    }

    /// <summary>
    /// Formats the command string for Windows, quoting paths with spaces.
    /// </summary>
    public static string FormatCommand(string exePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(exePath);
        string trimmed = exePath.Trim();
        if (trimmed.Contains(' ') && !trimmed.StartsWith('\"'))
        {
            return $"\"{trimmed}\"";
        }
        return trimmed;
    }

    /// <summary>
    /// Checks whether the openDynamic hook key is present in the specified hooks.json.
    /// </summary>
    public static bool IsHookInstalled(string hooksFilePath)
    {
        if (string.IsNullOrWhiteSpace(hooksFilePath) || !File.Exists(hooksFilePath))
        {
            return false;
        }

        try
        {
            string content = File.ReadAllText(hooksFilePath);
            using var doc = JsonDocument.Parse(content);
            return doc.RootElement.ValueKind == JsonValueKind.Object &&
                   doc.RootElement.TryGetProperty(HookKey, out _);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Generates a preview of the merged hooks.json without writing to disk.
    /// </summary>
    public static string GeneratePreview(string hooksFilePath, string hookExePath)
    {
        var rootNode = LoadOrCreateRootNode(hooksFilePath);
        InjectHookDefinition(rootNode, hookExePath);

        var options = new JsonSerializerOptions { WriteIndented = true };
        return rootNode.ToJsonString(options);
    }

    /// <summary>
    /// Installs openDynamic hooks into the target hooks.json with automatic backup.
    /// Preserves all other hook configurations. Aborts without modification if existing JSON is invalid.
    /// </summary>
    public static (bool Success, string Message, string? BackupPath) Install(
        string hooksFilePath,
        string hookExePath,
        DateTimeOffset? timestamp = null,
        Action<string>? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hooksFilePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(hookExePath);

        string? backupPath = null;

        try
        {
            var dir = Path.GetDirectoryName(hooksFilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            JsonObject rootNode;
            if (File.Exists(hooksFilePath))
            {
                string existingJson = File.ReadAllText(hooksFilePath);
                try
                {
                    var node = JsonNode.Parse(existingJson);
                    if (node is not JsonObject obj)
                    {
                        return (false, "El archivo hooks.json existente no es un objeto JSON válido.", null);
                    }
                    rootNode = obj;
                }
                catch (JsonException ex)
                {
                    logger?.Invoke($"Existing hooks.json is invalid JSON. Aborting install: {ex.Message}");
                    return (false, $"El archivo hooks.json contiene JSON inválido. Se abortó la instalación: {ex.Message}", null);
                }

                // Create backup before modification (hooks.json.openDynamic-<timestamp>.bak)
                var ts = timestamp ?? DateTimeOffset.UtcNow;
                string tsString = ts.ToString("yyyyMMdd-HHmmss");
                backupPath = $"{hooksFilePath}.openDynamic-{tsString}.bak";
                File.Copy(hooksFilePath, backupPath, overwrite: true);
                logger?.Invoke($"Created backup of hooks.json at {backupPath}");
            }
            else
            {
                rootNode = new JsonObject();
            }

            InjectHookDefinition(rootNode, hookExePath);

            var options = new JsonSerializerOptions { WriteIndented = true };
            string outputJson = rootNode.ToJsonString(options);

            File.WriteAllText(hooksFilePath, outputJson);
            logger?.Invoke($"openDynamic hook installed successfully into {hooksFilePath}");

            return (true, "Hook conectado exitosamente.", backupPath);
        }
        catch (Exception ex)
        {
            logger?.Invoke($"Failed to install openDynamic hook: {ex.Message}");
            return (false, $"Error al instalar el hook: {ex.Message}", backupPath);
        }
    }

    /// <summary>
    /// Uninstalls only the openDynamic-approvals key from hooks.json, preserving all other keys.
    /// Aborts without modification if existing JSON is invalid.
    /// </summary>
    public static (bool Success, string Message) Uninstall(
        string hooksFilePath,
        Action<string>? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hooksFilePath);

        if (!File.Exists(hooksFilePath))
        {
            return (true, "El archivo de hooks no existe. Nada que desconectar.");
        }

        try
        {
            string existingJson = File.ReadAllText(hooksFilePath);
            JsonObject rootNode;
            try
            {
                var node = JsonNode.Parse(existingJson);
                if (node is not JsonObject obj)
                {
                    return (false, "El archivo hooks.json existente no es un objeto JSON válido.");
                }
                rootNode = obj;
            }
            catch (JsonException ex)
            {
                logger?.Invoke($"Existing hooks.json is invalid JSON. Aborting uninstall: {ex.Message}");
                return (false, $"El archivo hooks.json contiene JSON inválido. Se abortó la desinstalación: {ex.Message}");
            }

            if (rootNode.ContainsKey(HookKey))
            {
                rootNode.Remove(HookKey);

                var options = new JsonSerializerOptions { WriteIndented = true };
                string outputJson = rootNode.ToJsonString(options);

                File.WriteAllText(hooksFilePath, outputJson);
                logger?.Invoke($"Removed {HookKey} from {hooksFilePath}");
            }

            return (true, "Hook desconectado exitosamente.");
        }
        catch (Exception ex)
        {
            logger?.Invoke($"Failed to uninstall openDynamic hook: {ex.Message}");
            return (false, $"Error al desconectar el hook: {ex.Message}");
        }
    }

    private static JsonObject LoadOrCreateRootNode(string hooksFilePath)
    {
        if (File.Exists(hooksFilePath))
        {
            try
            {
                var node = JsonNode.Parse(File.ReadAllText(hooksFilePath));
                if (node is JsonObject obj) return obj;
            }
            catch
            {
                // Return fresh on error for preview
            }
        }
        return new JsonObject();
    }

    private static void InjectHookDefinition(JsonObject rootNode, string hookExePath)
    {
        string command = FormatCommand(hookExePath);

        var hookItem = new JsonObject
        {
            ["type"] = "command",
            ["command"] = command,
            ["timeout"] = DefaultHookTimeoutSeconds
        };

        var preToolUseEntry = new JsonObject
        {
            ["matcher"] = DefaultMatcher,
            ["hooks"] = new JsonArray { hookItem }
        };

        var hookDefinition = new JsonObject
        {
            ["PreToolUse"] = new JsonArray { preToolUseEntry }
        };

        rootNode[HookKey] = hookDefinition;
    }
}
