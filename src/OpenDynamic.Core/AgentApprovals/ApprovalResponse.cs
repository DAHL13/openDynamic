using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenDynamic.Core.AgentApprovals;

/// <summary>
/// Response payload returned by the approval workflow to Antigravity.
/// Implements the official Antigravity PreToolUse hook output contract:
/// { "decision": "allow" | "deny" | "ask" | "force_ask", "reason": "..." }
/// </summary>
public sealed record ApprovalResponse
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    [JsonPropertyName("decision")]
    public string Decision { get; init; } = "ask";

    [JsonPropertyName("reason")]
    public string? Reason { get; init; }

    [JsonConstructor]
    public ApprovalResponse(string decision, string? reason = null)
    {
        if (string.IsNullOrWhiteSpace(decision))
        {
            Decision = "ask";
        }
        else
        {
            var normalized = decision.Trim().ToLowerInvariant();
            Decision = normalized switch
            {
                "allow" => "allow",
                "deny" => "deny",
                "ask" => "ask",
                "force_ask" => "force_ask",
                "deny_unless_prior_grant" => "deny_unless_prior_grant",
                _ => "ask" // Golden Rule 12: fail safe to "ask"
            };
        }

        Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
    }

    /// <summary>
    /// Grants one-time approval for the tool call.
    /// </summary>
    public static ApprovalResponse Allow() => new("allow");

    /// <summary>
    /// Denies tool execution with a mandatory or structured reason for the agent.
    /// </summary>
    public static ApprovalResponse Deny(string reason) =>
        new("deny", string.IsNullOrWhiteSpace(reason) ? "Acción denegada por el usuario." : reason);

    /// <summary>
    /// Delegates decision back to Antigravity's native dialog prompt (Fail-Safe default).
    /// </summary>
    public static ApprovalResponse AskNative(string? reason = null) =>
        new("ask", reason);

    /// <summary>
    /// Serializes response strictly formatted for Antigravity stdout hook contract.
    /// </summary>
    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    /// <summary>
    /// Deserializes an <see cref="ApprovalResponse"/> from JSON safely, returning AskNative on error.
    /// </summary>
    public static ApprovalResponse FromJsonSafe(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return AskNative("Respuesta vacía o nula");
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<ApprovalResponse>(json, JsonOptions);
            return parsed ?? AskNative("JSON deserializado nulo");
        }
        catch
        {
            return AskNative("Error deserializando JSON");
        }
    }
}
