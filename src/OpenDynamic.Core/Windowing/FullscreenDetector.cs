namespace OpenDynamic.Core.Windowing;

/// <summary>
/// Pure calculation and detection helper for determining fullscreen exclusive and borderless states.
/// Adheres strictly to Golden Rule 5 (isolated in Core, zero UI/platform dependencies).
/// </summary>
public static class FullscreenDetector
{
    // Values corresponding to Win32 QUERY_USER_NOTIFICATION_STATE
    public const int QUNS_NOT_PRESENT = 1;
    public const int QUNS_BUSY = 2;
    public const int QUNS_RUNNING_D3D_FULL_SCREEN = 3;
    public const int QUNS_PRESENTATION_MODE = 4;
    public const int QUNS_ACCEPTS_NOTIFICATIONS = 5;
    public const int QUNS_QUIET_TIME = 6;
    public const int QUNS_APP = 7;

    /// <summary>
    /// Checks whether the user notification state indicates an exclusive fullscreen game or presentation.
    /// </summary>
    public static bool IsNotificationStateFullscreen(int qunsState)
    {
        return qunsState is QUNS_BUSY or QUNS_RUNNING_D3D_FULL_SCREEN or QUNS_PRESENTATION_MODE;
    }

    /// <summary>
    /// Checks whether a window's bounding rectangle completely covers the monitor's display rectangle,
    /// identifying borderless maximized fullscreen applications (e.g. YouTube in browser, VLC).
    /// Desktop and shell windows are strictly ignored.
    /// </summary>
    public static bool IsWindowBoundsFullscreen(
        int winLeft, int winTop, int winRight, int winBottom,
        int monLeft, int monTop, int monRight, int monBottom,
        bool isShellOrDesktop)
    {
        if (isShellOrDesktop)
        {
            return false;
        }

        return winLeft <= monLeft &&
               winTop <= monTop &&
               winRight >= monRight &&
               winBottom >= monBottom;
    }
}
