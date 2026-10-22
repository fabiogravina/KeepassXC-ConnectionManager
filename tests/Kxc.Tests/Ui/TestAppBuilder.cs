using Avalonia;
using Avalonia.Headless;

[assembly: AvaloniaTestApplication(typeof(Kxc.Tests.Ui.TestAppBuilder))]

namespace Kxc.Tests.Ui;

/// <summary>App headless con rendering Skia, per poter salvare le schermate dei test.</summary>
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<Kxc.App.App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
