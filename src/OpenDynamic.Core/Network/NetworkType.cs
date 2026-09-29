namespace OpenDynamic.Core.Network;

/// <summary>
/// Physical or logical media type of the network adapter.
/// </summary>
public enum NetworkType
{
    /// <summary>
    /// Wireless 802.11 network connection.
    /// </summary>
    WiFi,

    /// <summary>
    /// Wired Ethernet connection.
    /// </summary>
    Ethernet,

    /// <summary>
    /// Cellular, Bluetooth PAN, VPN or other network media type.
    /// </summary>
    Other
}
