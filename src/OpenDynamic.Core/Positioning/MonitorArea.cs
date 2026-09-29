namespace OpenDynamic.Core.Positioning;

/// <summary>
/// Represents the physical pixel bounds of a monitor or work area.
/// </summary>
/// <param name="Left">The X coordinate of the upper-left corner in virtual screen pixels.</param>
/// <param name="Top">The Y coordinate of the upper-left corner in virtual screen pixels.</param>
/// <param name="Width">The physical width in pixels.</param>
/// <param name="Height">The physical height in pixels.</param>
public readonly record struct MonitorArea(int Left, int Top, int Width, int Height)
{
    public int Right => Left + Width;
    public int Bottom => Top + Height;

    /// <summary>
    /// Creates a <see cref="MonitorArea"/> from explicit rectangular edges.
    /// </summary>
    public static MonitorArea FromEdges(int left, int top, int right, int bottom) =>
        new(left, top, right - left, bottom - top);
}
