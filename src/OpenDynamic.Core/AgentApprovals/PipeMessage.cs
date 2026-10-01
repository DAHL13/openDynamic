using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenDynamic.Core.AgentApprovals;

/// <summary>
/// Envelope message for IPC communication over Named Pipe (protocol version 1).
/// Enforces strict payload limits (max 256 KB) and protocol version validation (Golden Rule 13).
/// Supports both {"v": 1} and {"version": 1} naming conventions.
/// </summary>
public sealed record PipeMessage
{
    /// <summary>
    /// Supported IPC protocol version.
    /// </summary>
    public const int CurrentProtocolVersion = 1;

    /// <summary>
    /// Strict maximum size limit for an individual IPC message payload (256 KB).
    /// </summary>
    public const int MaxPayloadSizeBytes = 256 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    [JsonPropertyName("v")]
    public int Version { get; init; } = CurrentProtocolVersion;

    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    [JsonPropertyName("payload")]
    public string? Payload { get; init; }

    public PipeMessage()
    {
    }

    [JsonConstructor]
    public PipeMessage(int? v, int? version, string type, string? payload = null)
    {
        Version = v ?? version ?? CurrentProtocolVersion;
        Type = type?.Trim() ?? string.Empty;
        Payload = payload;
    }

    public PipeMessage(int version, string type, string? payload = null)
    {
        Version = version;
        Type = type?.Trim() ?? string.Empty;
        Payload = payload;
    }

    /// <summary>
    /// Creates a request envelope wrapping an <see cref="ApprovalRequest"/>.
    /// </summary>
    public static PipeMessage CreateRequest(ApprovalRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var json = request.ToJson();
        ValidatePayloadSize(json);
        return new PipeMessage(CurrentProtocolVersion, "request", json);
    }

    /// <summary>
    /// Creates a response envelope wrapping an <see cref="ApprovalResponse"/>.
    /// </summary>
    public static PipeMessage CreateResponse(ApprovalResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        var json = response.ToJson();
        ValidatePayloadSize(json);
        return new PipeMessage(CurrentProtocolVersion, "response", json);
    }

    /// <summary>
    /// Creates a cancellation message for a pending request ID.
    /// </summary>
    public static PipeMessage CreateCancel(string requestId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        return new PipeMessage(CurrentProtocolVersion, "cancel", requestId.Trim());
    }

    /// <summary>
    /// Creates a ping heartbeat/liveness message.
    /// </summary>
    public static PipeMessage CreatePing() => new(CurrentProtocolVersion, "ping");

    /// <summary>
    /// Creates a pong heartbeat response.
    /// </summary>
    public static PipeMessage CreatePong() => new(CurrentProtocolVersion, "pong");

    /// <summary>
    /// Serializes this envelope into JSON, enforcing the 256 KB maximum size limit.
    /// </summary>
    public string Serialize()
    {
        ValidateVersion();
        var json = JsonSerializer.Serialize(this, JsonOptions);
        ValidatePayloadSize(json);
        return json;
    }

    /// <summary>
    /// Deserializes and validates a JSON string into a <see cref="PipeMessage"/>.
    /// Supports "v", "version", snake_case, raw hook payloads, and strips any UTF-8 BOM.
    /// </summary>
    public static PipeMessage Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        string clean = json.Trim().Trim('\uFEFF');
        ValidatePayloadSize(clean);

        using var doc = JsonDocument.Parse(clean);
        var root = doc.RootElement;

        // Auto-wrap raw Antigravity hook payloads or direct request JSON without envelope
        if (root.TryGetProperty("toolCall", out _) || root.TryGetProperty("ToolCall", out _) ||
            ((root.TryGetProperty("toolName", out _) || root.TryGetProperty("tool_name", out _)) && !root.TryGetProperty("type", out _)))
        {
            return new PipeMessage(CurrentProtocolVersion, "request", clean);
        }

        // Version: check "v", "version", "protocol_version" (number or string)
        int version = CurrentProtocolVersion;
        if (root.TryGetProperty("v", out var vProp) ||
            root.TryGetProperty("version", out vProp) ||
            root.TryGetProperty("protocol_version", out vProp))
        {
            if (vProp.ValueKind == JsonValueKind.Number && vProp.TryGetInt32(out int vNum))
            {
                version = vNum;
            }
            else if (vProp.ValueKind == JsonValueKind.String && int.TryParse(vProp.GetString(), out int vParsed))
            {
                version = vParsed;
            }
        }

        if (version != CurrentProtocolVersion)
        {
            throw new InvalidOperationException(
                $"Unsupported protocol version {version}. Expected protocol version {CurrentProtocolVersion}.");
        }

        // Type: check "type", "Type", "message_type"
        string type = string.Empty;
        if (root.TryGetProperty("type", out var typeProp) ||
            root.TryGetProperty("Type", out typeProp) ||
            root.TryGetProperty("message_type", out typeProp))
        {
            type = typeProp.GetString() ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(type))
        {
            throw new InvalidOperationException("Pipe message type cannot be empty.");
        }

        // Payload: check "payload", "Payload", "data"
        string? payload = null;
        if (root.TryGetProperty("payload", out var payloadProp) ||
            root.TryGetProperty("Payload", out payloadProp) ||
            root.TryGetProperty("data", out payloadProp))
        {
            payload = payloadProp.ValueKind == JsonValueKind.String
                ? payloadProp.GetString()
                : payloadProp.GetRawText();
        }

        return new PipeMessage(version, type, payload);
    }

    private void ValidateVersion()
    {
        if (Version != CurrentProtocolVersion)
        {
            throw new InvalidOperationException(
                $"Unsupported protocol version {Version}. Expected protocol version {CurrentProtocolVersion}.");
        }
    }

    private static void ValidatePayloadSize(string content)
    {
        int byteCount = System.Text.Encoding.UTF8.GetByteCount(content);
        if (byteCount > MaxPayloadSizeBytes)
        {
            throw new InvalidOperationException(
                $"Payload exceeds maximum allowed size of {MaxPayloadSizeBytes} bytes (actual: {byteCount} bytes).");
        }
    }
}
