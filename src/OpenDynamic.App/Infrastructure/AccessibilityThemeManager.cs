using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using Serilog;

namespace OpenDynamic.App.Infrastructure;

/// <summary>
/// Manages system high contrast detection and dynamic color themes (Task 5).
/// When high contrast is active, switches brushes to native SystemColors and ensures a 1 DIP border.
/// </summary>
public static class AccessibilityThemeManager
{
    private static bool _initialized;

    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        ApplyTheme(SystemParameters.HighContrast);
        SystemParameters.StaticPropertyChanged += OnStaticPropertyChanged;
    }

    private static void OnStaticPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemParameters.HighContrast))
        {
            Log.Information("AccessibilityThemeManager: System high contrast property changed to {HighContrast}.", SystemParameters.HighContrast);
            ApplyTheme(SystemParameters.HighContrast);
        }
    }

    public static void ApplyTheme(bool isHighContrast)
    {
        var resources = Application.Current?.Resources;
        if (resources == null) return;

        if (isHighContrast)
        {
            // High contrast: map to dynamic system colors with 1 DIP border; notch background remains solid
            resources["AppCapsuleBackgroundBrush"] = new SolidColorBrush(Colors.Black);
            resources["AppBorderBrush"] = SystemColors.WindowTextBrush;
            resources["AppNotchBorderThickness"] = new Thickness(1);
            resources["AppTextPrimaryBrush"] = SystemColors.WindowTextBrush;
            resources["AppTextSecondaryBrush"] = SystemColors.WindowTextBrush;
            resources["AppTextMutedBrush"] = SystemColors.WindowTextBrush;
            resources["AppControlBackgroundBrush"] = SystemColors.ControlBrush;
            resources["AppAccentBrush"] = SystemColors.HighlightBrush;
            resources["AppAccentTextBrush"] = SystemColors.HighlightTextBrush;
        }
        else
        {
            // Standard dark theme
            resources["AppCapsuleBackgroundBrush"] = new SolidColorBrush(Colors.Black);
            resources["AppBorderBrush"] = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));
            resources["AppNotchBorderThickness"] = new Thickness(1, 0, 1, 1);
            resources["AppTextPrimaryBrush"] = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
            resources["AppTextSecondaryBrush"] = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA));
            resources["AppTextMutedBrush"] = new SolidColorBrush(Color.FromRgb(0x8E, 0x8E, 0x93));
            resources["AppControlBackgroundBrush"] = new SolidColorBrush(Color.FromRgb(0x2C, 0x2C, 0x2E));
            resources["AppAccentBrush"] = new SolidColorBrush(Color.FromRgb(0x1E, 0xD7, 0x60));
            resources["AppAccentTextBrush"] = new SolidColorBrush(Colors.Black);
        }

        Log.Information("AccessibilityThemeManager: Theme applied (HighContrast={IsHighContrast})", isHighContrast);
    }
}
