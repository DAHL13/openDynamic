namespace OpenDynamic.Core.EnergySaver;

/// <summary>
/// Operating system energy saver / battery saver status.
/// Adheres strictly to Golden Rule 5 (pure logic in Core, zero Windows/WPF dependencies).
/// </summary>
public enum EnergySaverState
{
    /// <summary>
    /// Energy saver status is uninitialized or undetermined.
    /// </summary>
    Unknown,

    /// <summary>
    /// Energy saver is not supported by the platform (e.g. desktop PCs without battery).
    /// </summary>
    NotSupported,

    /// <summary>
    /// Energy saver mode is currently turned off.
    /// </summary>
    Off,

    /// <summary>
    /// Energy saver mode is currently turned on.
    /// </summary>
    On
}
