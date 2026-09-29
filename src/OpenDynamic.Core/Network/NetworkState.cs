namespace OpenDynamic.Core.Network;

/// <summary>
/// Represents the overall connectivity state of the network.
/// </summary>
public enum NetworkState
{
    /// <summary>
    /// No active internet or local network connection.
    /// </summary>
    Disconnected,

    /// <summary>
    /// Active network connection available.
    /// </summary>
    Connected
}
