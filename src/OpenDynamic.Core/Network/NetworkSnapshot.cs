namespace OpenDynamic.Core.Network;

/// <summary>
/// Immutable snapshot representing the network connectivity state, profile name, and adapter type.
/// Adheres strictly to Golden Rule 5 (isolated in Core, zero UI or platform dependencies).
/// </summary>
public readonly record struct NetworkSnapshot(
    NetworkState State,
    string? NetworkName,
    NetworkType Type)
{
    /// <summary>
    /// Represents a disconnected baseline snapshot.
    /// </summary>
    public static NetworkSnapshot Disconnected { get; } = new(NetworkState.Disconnected, null, NetworkType.Other);
}
