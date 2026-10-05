namespace OpenDynamic.Core.Widgets;

/// <summary>
/// Defines standard priority levels for activities in the Dynamic Island.
/// Higher numerical values represent higher priority.
/// </summary>
public static class ActivityPriority
{
    public const int Idle = 0;
    public const int AmbientClock = 5;
    public const int Hardware = 10;
    public const int Low = 10;
    public const int Media = 30;
    public const int Stopwatch = 45;
    public const int Timer = 50;
    public const int Normal = 50;
    public const int Clipboard = 55;
    public const int Device = 60;
    public const int Network = 65;
    public const int Screenshot = 75;
    public const int Volume = 80;
    public const int Privacy = 85;
    public const int EnergySaver = 88;
    public const int Battery = 90;
    public const int TimerAlert = 100;
    public const int High = 100;
    public const int TransientNotice = 200;
    public const int Critical = 500;
}
