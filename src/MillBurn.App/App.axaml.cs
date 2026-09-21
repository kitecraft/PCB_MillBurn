using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using MillBurn.App.ViewModels;
using MillBurn.App.Views;

namespace MillBurn.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var viewModel = new MainViewModel();

            desktop.MainWindow = new MainWindow
            {
                DataContext = viewModel,
            };

            // The view model holds the preview's cancellation source. Without this a preview still
            // in flight at shutdown runs on to completion on a pool thread, against a window that
            // has gone.
            desktop.ShutdownRequested += (_, _) => viewModel.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }
}