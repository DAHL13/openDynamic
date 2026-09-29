namespace OpenDynamic.Core.Widgets;

/// <summary>
/// Defines standard priority levels for activities in the Dynamic Island.
/// Higher numerical values represent higher priority.
/// </summary>
public static class ActivityPriority
{
    public const int Idle = 0;
    public const int Low = 10;
    public const int Media = 30;
    public const int Normal = 50;
    public const int High = 100;
    public const int TransientNotice = 200;
    public const int Critical = 500;
}
