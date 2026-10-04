using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using RecordVideoAudio.GMTPC.ViewModels;
using RecordVideoAudio.GMTPC.Views;

namespace RecordVideoAudio.GMTPC;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
#if DEBUG
        this.AttachDeveloperTools();
#endif
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnMainWindowClose;
            var vm = new MainViewModel();
            var mainWindow = new MainWindow
            {
                DataContext = vm
            };
            desktop.MainWindow = mainWindow;

            desktop.Exit += (s, e) =>
            {
                vm.Dispose();
                System.Environment.Exit(0);
            };
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
        {
            singleViewPlatform.MainView = new MainView
            {
                DataContext = new MainViewModel()
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}