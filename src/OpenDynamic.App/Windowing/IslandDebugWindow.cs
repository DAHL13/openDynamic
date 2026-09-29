#if DEBUG
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using OpenDynamic.App.Animation;
using OpenDynamic.App.Orchestration;
using OpenDynamic.App.Widgets.Demo;
using OpenDynamic.App.Widgets.Hardware;
using OpenDynamic.App.Widgets.Timer;
using OpenDynamic.Core.State;

namespace OpenDynamic.App.Windowing;

/// <summary>
/// Debugging window for testing spring physics, state transitions, widget priority resolution,
/// and transient auto-expiration lifespans.
/// STRICTLY conditioned to DEBUG builds (#if DEBUG). Zero presence in Release builds.
/// </summary>
public sealed class IslandDebugWindow : Window
{
    private readonly IslandOrchestrator _orchestrator;
    private readonly IslandAnimator _animator;

    private readonly TextBlock _statusBlock;
    private readonly TextBlock _widgetsBlock;
    private readonly TextBlock _dimensionsBlock;
    private readonly TextBlock _subscriptionBlock;
    private readonly TextBlock _timersBlock;
    private readonly TextBlock _memoryBlock;
    private readonly TextBlock _quarantineBlock;
    private readonly System.Windows.Threading.DispatcherTimer _telemetryTimer;

    private readonly Slider _stiffnessSlider;
    private readonly Slider _dampingSlider;
    private readonly TextBlock _stiffnessValueLabel;
    private readonly TextBlock _dampingValueLabel;

    public IslandDebugWindow(IslandOrchestrator orchestrator)
    {
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _animator = orchestrator.Animator;

        Title = "openDynamic - Panel de Depuración y Widgets (DEBUG)";
        Width = 520;
        Height = 720;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        Background = new SolidColorBrush(Color.FromRgb(24, 24, 27));
        Foreground = Brushes.White;

        var mainPanel = new StackPanel { Margin = new Thickness(16) };

        // Header
        mainPanel.Children.Add(new TextBlock
        {
            Text = "🛠 Monitor de Orquestación y Widgets (Fase 3)",
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            Margin = new Thickness(0, 0, 0, 12)
        });

        // Telemetry Group
        var telemetryBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(39, 39, 42)),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 16)
        };
        var telemetryStack = new StackPanel();

        _statusBlock = new TextBlock
        {
            Text = $"Estado FSM: {_animator.StateMachine.CurrentState}",
            FontWeight = FontWeights.SemiBold,
            FontSize = 14,
            Foreground = Brushes.LightGreen,
            Margin = new Thickness(0, 0, 0, 4)
        };
        telemetryStack.Children.Add(_statusBlock);

        _widgetsBlock = new TextBlock
        {
            Text = "Actividad: Primario: - | Secundario: -",
            FontSize = 12,
            Foreground = Brushes.White,
            Margin = new Thickness(0, 0, 0, 4)
        };
        telemetryStack.Children.Add(_widgetsBlock);

        _subscriptionBlock = new TextBlock
        {
            Text = "Render Loop: ○ EN REPOSO (0% CPU - Desuscrito)",
            FontSize = 12,
            Foreground = Brushes.LightGray,
            Margin = new Thickness(0, 0, 0, 4)
        };
        telemetryStack.Children.Add(_subscriptionBlock);

        _timersBlock = new TextBlock
        {
            Text = "Timers Activos: Ninguno (0% CPU)",
            FontSize = 12,
            Foreground = Brushes.LightGray,
            Margin = new Thickness(0, 0, 0, 4)
        };
        telemetryStack.Children.Add(_timersBlock);

        _memoryBlock = new TextBlock
        {
            Text = "Memoria: Working Set: 0 MB | GC: 0 MB",
            FontSize = 12,
            Foreground = Brushes.Aqua,
            Margin = new Thickness(0, 0, 0, 4)
        };
        telemetryStack.Children.Add(_memoryBlock);

        _dimensionsBlock = new TextBlock
        {
            Text = $"Dimensiones: W:{_animator.CurrentDimensions.Width:F1} | H:{_animator.CurrentDimensions.Height:F1} | R:{_animator.CurrentDimensions.CornerRadius:F1} | Op:{_animator.CurrentDimensions.Opacity:F2}",
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            Foreground = Brushes.DarkGray,
            Margin = new Thickness(0, 0, 0, 4)
        };
        telemetryStack.Children.Add(_dimensionsBlock);

        _quarantineBlock = new TextBlock
        {
            Text = "Aislamiento: Ningún widget con fallo",
            FontSize = 11,
            Foreground = Brushes.LightSeaGreen
        };
        telemetryStack.Children.Add(_quarantineBlock);

        telemetryBorder.Child = telemetryStack;
        mainPanel.Children.Add(telemetryBorder);

        // Demo Widgets Controls Group
        mainPanel.Children.Add(new TextBlock
        {
            Text = "Control de Widgets de Demostración:",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8)
        });

        var demoGrid = new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, 0, 16) };

        demoGrid.Children.Add(CreateButton("Conmutar Demo A (Música)", () =>
        {
            var demoA = _orchestrator.RegisteredWidgets.OfType<DemoWidgetA>().FirstOrDefault();
            demoA?.ToggleActive();
            UpdateTelemetry();
        }));

        demoGrid.Children.Add(CreateButton("Conmutar Demo B (Timer)", () =>
        {
            var demoB = _orchestrator.RegisteredWidgets.OfType<DemoWidgetB>().FirstOrDefault();
            demoB?.ToggleActive();
            UpdateTelemetry();
        }));

        demoGrid.Children.Add(CreateButton("⚡ Aviso Transitorio A (3s, P=200)", () =>
        {
            var demoA = _orchestrator.RegisteredWidgets.OfType<DemoWidgetA>().FirstOrDefault();
            demoA?.TriggerTransientNotice(TimeSpan.FromSeconds(3));
            UpdateTelemetry();
        }));

        demoGrid.Children.Add(CreateButton("⚡ Aviso Transitorio B (3s, P=250)", () =>
        {
            var demoB = _orchestrator.RegisteredWidgets.OfType<DemoWidgetB>().FirstOrDefault();
            demoB?.TriggerTransientNotice(TimeSpan.FromSeconds(3));
            UpdateTelemetry();
        }));

        demoGrid.Children.Add(CreateButton("💥 Forzar Fallo en Demo A", () =>
        {
            var demoA = _orchestrator.RegisteredWidgets.OfType<DemoWidgetA>().FirstOrDefault();
            demoA?.ArmFaultSimulation();
            UpdateTelemetry();
        }));

        demoGrid.Children.Add(CreateButton("💥 Forzar Fallo en Demo B", () =>
        {
            var demoB = _orchestrator.RegisteredWidgets.OfType<DemoWidgetB>().FirstOrDefault();
            demoB?.ArmFaultSimulation();
            UpdateTelemetry();
        }));

        demoGrid.Children.Add(CreateButton("💻 Conmutar Monitor de Hardware", () =>
        {
            var hw = _orchestrator.RegisteredWidgets.OfType<HardwareWidget>().FirstOrDefault();
            hw?.ToggleMonitoring();
            UpdateTelemetry();
        }));

        demoGrid.Children.Add(CreateButton("⏱ Iniciar Temporizador (1 min)", () =>
        {
            var timer = _orchestrator.RegisteredWidgets.OfType<TimerWidget>().FirstOrDefault();
            timer?.Start(TimeSpan.FromMinutes(1));
            UpdateTelemetry();
        }));

        mainPanel.Children.Add(demoGrid);

        // State Transition Buttons Group
        mainPanel.Children.Add(new TextBlock
        {
            Text = "Forzar Transición Manual de Estado:",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8)
        });

        var statesGrid = new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, 0, 16) };
        statesGrid.Children.Add(CreateButton("Compact (160x36)", () => _orchestrator.TransitionTo(IslandState.Compact)));
        statesGrid.Children.Add(CreateButton("Expanded (400x160)", () => _orchestrator.RequestExpand()));
        statesGrid.Children.Add(CreateButton("Split (260x36)", () => _orchestrator.TransitionTo(IslandState.Split)));
        statesGrid.Children.Add(CreateButton("Hidden (80x4)", () => _orchestrator.RequestHide()));
        mainPanel.Children.Add(statesGrid);

        // Spring Controls Group
        mainPanel.Children.Add(new TextBlock
        {
            Text = "Afinación de Física de Resorte:",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8)
        });

        _stiffnessValueLabel = new TextBlock
        {
            Text = $"{_animator.WidthSpring.Stiffness:F0}",
            HorizontalAlignment = HorizontalAlignment.Right,
            FontWeight = FontWeights.Bold
        };

        _stiffnessSlider = new Slider
        {
            Minimum = 50,
            Maximum = 800,
            Value = _animator.WidthSpring.Stiffness,
            Margin = new Thickness(0, 0, 0, 12)
        };

        _dampingValueLabel = new TextBlock
        {
            Text = $"{_animator.WidthSpring.Damping:F0}",
            HorizontalAlignment = HorizontalAlignment.Right,
            FontWeight = FontWeights.Bold
        };

        _dampingSlider = new Slider
        {
            Minimum = 5,
            Maximum = 80,
            Value = _animator.WidthSpring.Damping,
            Margin = new Thickness(0, 0, 0, 16)
        };

        _stiffnessSlider.ValueChanged += (s, e) =>
        {
            _stiffnessValueLabel.Text = $"{e.NewValue:F0}";
            _animator.SetSpringParameters(e.NewValue, _dampingSlider.Value);
        };

        _dampingSlider.ValueChanged += (s, e) =>
        {
            _dampingValueLabel.Text = $"{e.NewValue:F0}";
            _animator.SetSpringParameters(_stiffnessSlider.Value, e.NewValue);
        };

        var stiffnessHeader = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        stiffnessHeader.Children.Add(new TextBlock { Text = "Rigidez (Stiffness / k):", HorizontalAlignment = HorizontalAlignment.Left });
        stiffnessHeader.Children.Add(_stiffnessValueLabel);
        mainPanel.Children.Add(stiffnessHeader);
        mainPanel.Children.Add(_stiffnessSlider);

        var dampingHeader = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        dampingHeader.Children.Add(new TextBlock { Text = "Amortiguamiento (Damping / c):", HorizontalAlignment = HorizontalAlignment.Left });
        dampingHeader.Children.Add(_dampingValueLabel);
        mainPanel.Children.Add(dampingHeader);
        mainPanel.Children.Add(_dampingSlider);

        Content = new ScrollViewer { Content = mainPanel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

        _animator.FrameUpdated += OnAnimatorUpdated;
        _animator.Settled += OnAnimatorUpdated;
        _animator.Started += OnAnimatorUpdated;

        _telemetryTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _telemetryTimer.Tick += (s, e) => UpdateTelemetry();
        _telemetryTimer.Start();

        UpdateTelemetry();
    }

    private void UpdateTelemetry()
    {
        Dispatcher.InvokeAsync(() =>
        {
            _statusBlock.Text = $"Estado FSM: {_animator.StateMachine.CurrentState}";
            _subscriptionBlock.Text = _animator.IsSubscribed
                ? "Render Loop: ● ACTIVO (Renderizando resortes)"
                : "Render Loop: ○ EN REPOSO (0% CPU - Desuscrito)";
            _subscriptionBlock.Foreground = _animator.IsSubscribed
                ? Brushes.Gold
                : Brushes.LightGreen;

            bool hasTransientTimer = _orchestrator.HasActiveTransientTimer;
            _timersBlock.Text = $"Timers / Render: TransientTimer={(hasTransientTimer ? "ACTIVO" : "Inactivo")} | RenderLoop={(_animator.IsSubscribed ? "ACTIVO" : "Desuscrito")}";
            _timersBlock.Foreground = (hasTransientTimer || _animator.IsSubscribed) ? Brushes.LightSkyBlue : Brushes.LightGray;

            double workingSetMb = System.Diagnostics.Process.GetCurrentProcess().WorkingSet64 / (1024.0 * 1024.0);
            double gcTotalMb = GC.GetTotalMemory(false) / (1024.0 * 1024.0);
            _memoryBlock.Text = $"Memoria: Working Set: {workingSetMb:F1} MB (Meta: < 100 MB) | Heap GC: {gcTotalMb:F1} MB";
            _memoryBlock.Foreground = workingSetMb < 100.0 ? Brushes.LightGreen : Brushes.Orange;

            var d = _animator.CurrentDimensions;
            _dimensionsBlock.Text = $"Dimensiones: W:{d.Width:F1} | H:{d.Height:F1} | R:{d.CornerRadius:F1} | Op:{d.Opacity:F2}";

            var primary = _orchestrator.ActivePrimaryWidget?.Id ?? "Ninguno";
            var secondary = _orchestrator.ActiveSecondaryWidget?.Id ?? "Ninguno";
            _widgetsBlock.Text = $"Actividad: Primario: [{primary}] | Secundario: [{secondary}]";

            if (_orchestrator.QuarantinedWidgetIds.Count > 0)
            {
                _quarantineBlock.Text = $"Aislamiento: ⚠️ Widgets en cuarentena: {string.Join(", ", _orchestrator.QuarantinedWidgetIds)}";
                _quarantineBlock.Foreground = Brushes.Tomato;
            }
            else
            {
                _quarantineBlock.Text = "Aislamiento: ✔ Ningún widget con fallo";
                _quarantineBlock.Foreground = Brushes.LightSeaGreen;
            }
        });
    }

    private void OnAnimatorUpdated(object? sender, EventArgs e)
    {
        UpdateTelemetry();
    }

    private Button CreateButton(string label, Action action)
    {
        var btn = new Button
        {
            Content = label,
            Margin = new Thickness(4),
            Padding = new Thickness(8, 6, 8, 6),
            Background = new SolidColorBrush(Color.FromRgb(63, 63, 70)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand
        };
        btn.Click += (_, _) => action();
        return btn;
    }

    protected override void OnClosed(EventArgs e)
    {
        _telemetryTimer.Stop();
        _animator.FrameUpdated -= OnAnimatorUpdated;
        _animator.Settled -= OnAnimatorUpdated;
        _animator.Started -= OnAnimatorUpdated;
        base.OnClosed(e);
    }
}
#endif
