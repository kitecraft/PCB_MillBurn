using Avalonia;
using System;

namespace MillBurn.App;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static int Main(string[] args)
    {
        // `--bench` measures the Phase 0 viewport acceptance criterion headlessly.
        if (args.Contains("--bench", StringComparer.OrdinalIgnoreCase))
        {
            var segments = 500_000;
            var idx = Array.FindIndex(args, a => a.Equals("--segments", StringComparison.OrdinalIgnoreCase));
            if (idx >= 0 && idx + 1 < args.Length && int.TryParse(args[idx + 1], out var parsed))
            {
                segments = parsed;
            }

            return MillBurn.Viewer.ViewportBenchmark.Run(segments);
        }

        if (args.Contains("--probe", StringComparer.OrdinalIgnoreCase))
        {
            return MillBurn.Viewer.ViewportProbe.Run();
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
