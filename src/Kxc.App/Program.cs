using Avalonia;

namespace Kxc.App;

internal static class Program
{
    // Prima di AppMain non si usano API di Avalonia né codice che dipende dal SynchronizationContext.
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    // Usato anche dall'anteprima visuale.
    public static AppBuilder BuildAvaloniaApp()
    {
        var builder = AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont();

#if DEBUG
        // S12: i log di Avalonia (anche errori di binding con i valori) solo durante lo sviluppo.
        builder = builder.LogToTrace();
#endif
        return builder;
    }
}
