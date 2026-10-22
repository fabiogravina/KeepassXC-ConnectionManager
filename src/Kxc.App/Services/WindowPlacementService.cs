using Avalonia;
using Avalonia.Controls;
using Kxc.Core.Settings;

namespace Kxc.App.Services;

/// <summary>
/// Dimensione e posizione della finestra ricordate. Se lo schermo salvato non c'è più (monitor
/// staccato, altra configurazione in dual boot) la finestra resta centrata sullo schermo principale.
/// </summary>
internal static class WindowPlacementService
{
    public static void Restore(Window window, AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(settings);

        if (settings.Window is not { } placement)
        {
            return;
        }

        window.Width = Math.Max(placement.Width, window.MinWidth);
        window.Height = Math.Max(placement.Height, window.MinHeight);

        var topLeft = new PixelPoint(placement.X, placement.Y);
        var visible = window.Screens.All.Any(s => s.WorkingArea.Contains(topLeft + new PixelPoint(40, 40)));
        if (visible)
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Position = topLeft;
        }

        if (placement.Maximized)
        {
            window.WindowState = WindowState.Maximized;
        }
    }

    public static void Save(Window window, AppSettings settings, ISettingsStore store)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(store);

        var maximized = window.WindowState == WindowState.Maximized;

        // Da massimizzata si conserva l'ultima dimensione normale già salvata.
        settings.Window = maximized && settings.Window is { } previous
            ? previous with { Maximized = true }
            : new WindowPlacement(window.Position.X, window.Position.Y, window.Width, window.Height, maximized);
        store.Save(settings);
    }
}
