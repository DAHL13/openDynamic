using System.Windows;
using OpenDynamic.App.Infrastructure;

namespace OpenDynamic.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private readonly SingleInstanceManager _singleInstance = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (!_singleInstance.TryAcquire())
        {
            Shutdown();
            return;
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstance.Dispose();
        base.OnExit(e);
    }
}


