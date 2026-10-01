using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenDynamic.Core.AgentApprovals;

/// <summary>
/// Envelope message for IPC communication over Named Pipe (protocol version 1).
/// Enforces strict payload limits (max 256 KB) and protocol version validation (Golden Rule 13).
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

    [JsonConstructor]
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
    /// </summary>
    /// <param name="json">Raw JSON string received over the pipe.</param>
    /// <returns>Validated <see cref="PipeMessage"/>.</returns>
    /// <exception cref="InvalidOperationException">Thrown when message violates protocol constraints or size limits.</exception>
    public static PipeMessage Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        ValidatePayloadSize(json);

        PipeMessage? message;
        try
        {
            message = JsonSerializer.Deserialize<PipeMessage>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Malformed pipe message JSON.", ex);
        }

        if (message == null)
        {
            throw new InvalidOperationException("Deserialized pipe message is null.");
        }

        message.ValidateVersion();

        if (string.IsNullOrWhiteSpace(message.Type))
        {
            throw new InvalidOperationException("Pipe message type cannot be empty.");
        }

        return message;
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
