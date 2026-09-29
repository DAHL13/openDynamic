#if DEBUG
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OpenDynamic.Core.Widgets;
using Serilog;

namespace OpenDynamic.App.Widgets.Demo;

/// <summary>
/// Demo Widget A: Simulates a media playback activity with continuous and transient states.
/// STRICTLY conditioned to DEBUG builds (#if DEBUG).
/// </summary>
public sealed class DemoWidgetA : IslandWidgetBase
{
    public override string Id => "demo-widget-a";

    private bool _shouldThrowOnView;

    public DemoWidgetA(int priority = 70, bool startActive = true) : base(priority)
    {
        CurrentActivity = new IslandActivity(
            Id: Id,
            Title: "Bohemian Rhapsody",
            Subtitle: "Queen - A Night at the Opera",
            Priority: priority);

        IsActive = startActive;
    }

    public void ToggleActive()
    {
        if (IsActive)
        {
            Deactivate();
        }
        else
        {
            Activate();
        }
    }

    public void TriggerTransientNotice(TimeSpan duration)
    {
        Log.Information("DemoWidgetA: Triggering transient alert (Duration: {Duration}s, Priority: 200).", duration.TotalSeconds);
        Activate(duration, priorityOverride: 200);
    }

    public void ArmFaultSimulation()
    {
        _shouldThrowOnView = true;
        NotifyChanged();
    }

    public override UserControl? CreateCompactView()
    {
        if (_shouldThrowOnView)
        {
            _shouldThrowOnView = false;
            throw new InvalidOperationException("Simulated deliberate exception inside DemoWidgetA.CreateCompactView");
        }

        var control = new UserControl();
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(8, 0, 8, 0)
        };

        var dot = new Border
        {
            Width = 8,
            Height = 8,
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(Color.FromRgb(34, 197, 94)), // Emerald Green
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center
        };

        var text = new TextBlock
        {
            Text = IsTransient ? "📢 Aviso Demo A" : "♫ Bohemian Rhapsody",
            Foreground = Brushes.White,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };

        panel.Children.Add(dot);
        panel.Children.Add(text);
        control.Content = panel;
        return control;
    }

    public override UserControl? CreateExpandedView()
    {
        if (_shouldThrowOnView)
        {
            _shouldThrowOnView = false;
            throw new InvalidOperationException("Simulated deliberate exception inside DemoWidgetA.CreateExpandedView");
        }

        var control = new UserControl();
        var mainGrid = new Grid { Margin = new Thickness(16) };

        mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
        mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var albumArt = new Border
        {
            Width = 56,
            Height = 56,
            CornerRadius = new CornerRadius(12),
            Background = new SolidColorBrush(Color.FromRgb(34, 197, 94)),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        albumArt.Child = new TextBlock
        {
            Text = "♫",
            FontSize = 24,
            Foreground = Brushes.Black,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(albumArt, 0);

        var details = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0)
        };

        details.Children.Add(new TextBlock
        {
            Text = "Bohemian Rhapsody",
            Foreground = Brushes.White,
            FontSize = 15,
            FontWeight = FontWeights.Bold
        });

        details.Children.Add(new TextBlock
        {
            Text = "Queen — A Night at the Opera",
            Foreground = new SolidColorBrush(Color.FromRgb(161, 161, 170)),
            FontSize = 12,
            Margin = new Thickness(0, 2, 0, 8)
        });

        var controlsRow = new StackPanel { Orientation = Orientation.Horizontal };
        controlsRow.Children.Add(new TextBlock { Text = "⏮   ⏸   ⏭", Foreground = Brushes.White, FontSize = 16 });
        details.Children.Add(controlsRow);

        Grid.SetColumn(details, 1);

        mainGrid.Children.Add(albumArt);
        mainGrid.Children.Add(details);
        control.Content = mainGrid;
        return control;
    }

    public override UserControl? CreateSplitView()
    {
        var control = new UserControl();
        var container = new Grid
        {
            Width = 36,
            Height = 36
        };

        container.Children.Add(new TextBlock
        {
            Text = "♫",
            FontSize = 16,
            Foreground = new SolidColorBrush(Color.FromRgb(34, 197, 94)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        });

        control.Content = container;
        return control;
    }
}

/// <summary>
/// Demo Widget B: Simulates a timer/hardware activity with continuous and transient states.
/// STRICTLY conditioned to DEBUG builds (#if DEBUG).
/// </summary>
public sealed class DemoWidgetB : IslandWidgetBase
{
    public override string Id => "demo-widget-b";

    private bool _shouldThrowOnView;

    public DemoWidgetB(int priority = 50, bool startActive = true) : base(priority)
    {
        CurrentActivity = new IslandActivity(
            Id: Id,
            Title: "Temporizador",
            Subtitle: "12:45 restantes",
            Priority: priority);

        IsActive = startActive;
    }

    public void ToggleActive()
    {
        if (IsActive)
        {
            Deactivate();
        }
        else
        {
            Activate();
        }
    }

    public void TriggerTransientNotice(TimeSpan duration)
    {
        Log.Information("DemoWidgetB: Triggering transient alert (Duration: {Duration}s, Priority: 250).", duration.TotalSeconds);
        Activate(duration, priorityOverride: 250);
    }

    public void ArmFaultSimulation()
    {
        _shouldThrowOnView = true;
        NotifyChanged();
    }

    public override UserControl? CreateCompactView()
    {
        if (_shouldThrowOnView)
        {
            _shouldThrowOnView = false;
            throw new InvalidOperationException("Simulated deliberate exception inside DemoWidgetB.CreateCompactView");
        }

        var control = new UserControl();
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(8, 0, 8, 0)
        };

        var dot = new Border
        {
            Width = 8,
            Height = 8,
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(Color.FromRgb(249, 115, 22)), // Vibrant Orange
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center
        };

        var text = new TextBlock
        {
            Text = IsTransient ? "⚡ Alerta Demo B" : "⏱ Temporizador 12:45",
            Foreground = Brushes.White,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };

        panel.Children.Add(dot);
        panel.Children.Add(text);
        control.Content = panel;
        return control;
    }

    public override UserControl? CreateExpandedView()
    {
        if (_shouldThrowOnView)
        {
            _shouldThrowOnView = false;
            throw new InvalidOperationException("Simulated deliberate exception inside DemoWidgetB.CreateExpandedView");
        }

        var control = new UserControl();
        var mainGrid = new Grid { Margin = new Thickness(16) };

        mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
        mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var timerIcon = new Border
        {
            Width = 56,
            Height = 56,
            CornerRadius = new CornerRadius(12),
            Background = new SolidColorBrush(Color.FromRgb(249, 115, 22)),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        timerIcon.Child = new TextBlock
        {
            Text = "⏱",
            FontSize = 24,
            Foreground = Brushes.Black,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(timerIcon, 0);

        var details = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0)
        };

        details.Children.Add(new TextBlock
        {
            Text = "Enfoque Productivo",
            Foreground = Brushes.White,
            FontSize = 15,
            FontWeight = FontWeights.Bold
        });

        details.Children.Add(new TextBlock
        {
            Text = "Tiempo restante: 12 min 45 s",
            Foreground = new SolidColorBrush(Color.FromRgb(161, 161, 170)),
            FontSize = 12,
            Margin = new Thickness(0, 2, 0, 8)
        });

        var controlsRow = new StackPanel { Orientation = Orientation.Horizontal };
        controlsRow.Children.Add(new TextBlock { Text = "+1 min   |   Pausar   |   Detener", Foreground = Brushes.White, FontSize = 13 });
        details.Children.Add(controlsRow);

        Grid.SetColumn(details, 1);

        mainGrid.Children.Add(timerIcon);
        mainGrid.Children.Add(details);
        control.Content = mainGrid;
        return control;
    }

    public override UserControl? CreateSplitView()
    {
        var control = new UserControl();
        var container = new Grid
        {
            Width = 36,
            Height = 36
        };

        container.Children.Add(new TextBlock
        {
            Text = "⏱",
            FontSize = 16,
            Foreground = new SolidColorBrush(Color.FromRgb(249, 115, 22)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        });

        control.Content = container;
        return control;
    }
}
#endif
