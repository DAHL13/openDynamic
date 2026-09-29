namespace OpenDynamic.Core.Positioning;

/// <summary>
/// Contains the calculated physical pixel coordinates and dimensions for positioning an overlay window.
/// </summary>
/// <param name="X">Physical X coordinate on the virtual screen.</param>
/// <param name="Y">Physical Y coordinate on the virtual screen.</param>
/// <param name="Width">Physical width in pixels.</param>
/// <param name="Height">Physical height in pixels.</param>
public readonly record struct CalculatedWindowPlacement(int X, int Y, int Width, int Height);
