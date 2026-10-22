#:package SkiaSharp@3.119.4
#:package SkiaSharp.NativeAssets.Win32@3.119.4
#:package SkiaSharp.NativeAssets.Linux@3.119.4
#:property ManagePackageVersionsCentrally=false
#:property RestorePackagesWithLockFile=false

// Icona di KXC-ConnectionManager (proposta C, «Lucchetto con prompt», scelta il 22 ottobre 2026).
// Rigenera tutti i file dall'unica definizione qui sotto:
//   dotnet run build/icons/icon.cs
// Uscite: src/Kxc.App/Assets (ico e png per l'app), packaging/linux/icons (tema hicolor), Docs/icons.
using System.Globalization;
using System.Text;
using SkiaSharp;

var root = FindRoot();

// Palette ispirata a KeePassXC: verde e grigio ardesia.
const string Green = "#6DAE45";
const string Slate = "#2E3A42";
const string White = "#FFFFFF";

Shape[] shapes =
[
    new(RoundRect(8, 8, 240, 240, 52), Green),
    new("M84,122 V96 A44,44 0 0 1 172,96 V122", null, Slate, 22),
    new(RoundRect(54, 112, 148, 112, 22), Slate),
    new("M86,146 L110,166 L86,186", null, White, 12),
    new(RoundRect(120, 176, 46, 12, 4), Green),
];

var assets = Path.Combine(root, "src", "Kxc.App", "Assets");
Directory.CreateDirectory(assets);
WriteIco(Path.Combine(assets, "app.ico"), [16, 20, 24, 32, 40, 48, 64, 128, 256]);
WritePng(Path.Combine(assets, "app-256.png"), 256);

foreach (var size in new[] { 16, 24, 32, 48, 64, 128, 256, 512 })
{
    WritePng(Path.Combine(root, "packaging", "linux", "icons", "hicolor", $"{size}x{size}", "apps", "kxc-connectionmanager.png"), size);
}

var svg = Svg();
WriteText(Path.Combine(root, "packaging", "linux", "icons", "hicolor", "scalable", "apps", "kxc-connectionmanager.svg"), svg);
WriteText(Path.Combine(root, "Docs", "icons", "kxc-connectionmanager.svg"), svg);
WritePng(Path.Combine(root, "Docs", "icons", "kxc-connectionmanager-256.png"), 256);
Console.WriteLine("Icona rigenerata in src/Kxc.App/Assets, packaging/linux/icons e Docs/icons.");

void WritePng(string path, int size)
{
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllBytes(path, Png(size));
}

void WriteText(string path, string text)
{
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, text);
}

/// <summary>ICO con immagini PNG incorporate (supportato da Windows Vista in poi).</summary>
void WriteIco(string path, int[] sizes)
{
    var images = sizes.Select(Png).ToList();
    using var stream = File.Create(path);
    using var writer = new BinaryWriter(stream);
    writer.Write((ushort)0);
    writer.Write((ushort)1);
    writer.Write((ushort)sizes.Length);
    var offset = 6 + 16 * sizes.Length;
    for (var i = 0; i < sizes.Length; i++)
    {
        writer.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
        writer.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write((ushort)1);
        writer.Write((ushort)32);
        writer.Write(images[i].Length);
        writer.Write(offset);
        offset += images[i].Length;
    }

    foreach (var image in images)
    {
        writer.Write(image);
    }
}

byte[] Png(int size)
{
    using var bitmap = new SKBitmap(size, size);
    using (var canvas = new SKCanvas(bitmap))
    {
        canvas.Clear(SKColors.Transparent);
        canvas.Scale(size / 256f);
        foreach (var shape in shapes)
        {
            using var path = SKPath.ParseSvgPathData(shape.Path);
            if (shape.Fill is { } fill)
            {
                using var paint = new SKPaint { Color = SKColor.Parse(fill), IsAntialias = true, Style = SKPaintStyle.Fill };
                canvas.DrawPath(path, paint);
            }

            if (shape.Stroke is { } stroke)
            {
                using var paint = new SKPaint
                {
                    Color = SKColor.Parse(stroke), IsAntialias = true, Style = SKPaintStyle.Stroke,
                    StrokeWidth = (float)shape.StrokeWidth, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round,
                };
                canvas.DrawPath(path, paint);
            }
        }
    }

    using var image = SKImage.FromBitmap(bitmap);
    using var data = image.Encode(SKEncodedImageFormat.Png, 100);
    return data.ToArray();
}

string Svg()
{
    var svg = new StringBuilder("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 256 256\" width=\"256\" height=\"256\">\n");
    svg.Append("  <title>KXC-ConnectionManager</title>\n");
    foreach (var s in shapes)
    {
        var stroke = s.Stroke is null
            ? string.Empty
            : $" stroke=\"{s.Stroke}\" stroke-width=\"{F(s.StrokeWidth)}\" stroke-linecap=\"round\" stroke-linejoin=\"round\"";
        svg.Append(CultureInfo.InvariantCulture, $"  <path d=\"{s.Path}\" fill=\"{s.Fill ?? "none"}\"{stroke}/>\n");
    }

    return svg.Append("</svg>\n").ToString();
}

static string F(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

static string RoundRect(double x, double y, double w, double h, double r) =>
    $"M{F(x + r)},{F(y)} H{F(x + w - r)} A{F(r)},{F(r)} 0 0 1 {F(x + w)},{F(y + r)} V{F(y + h - r)} A{F(r)},{F(r)} 0 0 1 {F(x + w - r)},{F(y + h)} H{F(x + r)} A{F(r)},{F(r)} 0 0 1 {F(x)},{F(y + h - r)} V{F(y + r)} A{F(r)},{F(r)} 0 0 1 {F(x + r)},{F(y)} Z";

static string FindRoot()
{
    var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Kxc.sln")))
    {
        dir = dir.Parent;
    }

    return dir?.FullName ?? throw new InvalidOperationException("Avvia dalla cartella del repository.");
}

internal sealed record Shape(string Path, string? Fill, string? Stroke = null, double StrokeWidth = 0);
