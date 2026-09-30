using System.ComponentModel;
using System.Windows;
using OpenDynamic.App.ViewModels;
using Serilog;

namespace OpenDynamic.App.Views;

/// <summary>
/// Interaction logic for SettingsWindow.xaml.
/// Standalone configuration window operating under MVVM.
/// Never steals keyboard focus from the floating island.
/// Hides instead of closing to allow quick reopening without shutting down the application.
/// </summary>
public partial class SettingsWindow : Window
{
    private bool _allowRealClose;

    public SettingsViewModel ViewModel { get; }

    public SettingsWindow(SettingsViewModel viewModel)
    {
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        DataContext = ViewModel;

        InitializeComponent();
    }

    /// <summary>
    /// Displays and activates the settings window.
    /// Safely dispatches to UI thread and brings the window to the foreground.
    /// </summary>
    public void ShowSettings()
    {
        if (Dispatcher != null && !Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(ShowSettings);
            return;
        }

        try
        {
            ViewModel.RefreshMonitors();

            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }

            Show();
            Activate();
            Focus();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to display SettingsWindow.");
        }
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e)
    {
        Hide();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowRealClose)
        {
            e.Cancel = true;
            Hide();
            Log.Debug("SettingsWindow hidden instead of closed (preserving application state).");
        }
        else
        {
            base.OnClosing(e);
        }
    }

    /// <summary>
    /// Closes the window permanently when the application is shutting down.
    /// </summary>
    public void ForceClose()
    {
        _allowRealClose = true;
        Close();
    }
}
