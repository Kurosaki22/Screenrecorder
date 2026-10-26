#:property TargetFramework=net10.0-windows
#:property UseWPF=true
#:property PublishAot=false
// Renders the app icon with WPF vector drawing and writes a multi-size .ico (PNG entries) plus a preview PNG.
// Usage: dotnet run tools/make-icon.cs -- <output.ico> <preview.png>

using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

var (icoPath, previewPath) = (Path.GetFullPath(args[0]), Path.GetFullPath(args[1]));
var thread = new Thread(() =>
{
    int[] sizes = [16, 24, 32, 48, 64, 128, 256];
    var pngs = sizes.Select(s => Png(Render(s))).ToList();
    WriteIco(icoPath, sizes, pngs);
    File.WriteAllBytes(previewPath, Png(Render(512)));
});
thread.SetApartmentState(ApartmentState.STA); // WPF rendering needs an STA thread
thread.Start();
thread.Join();
Console.WriteLine($"wrote {icoPath} and {previewPath}");

static BitmapSource Render(int size)
{
    var visual = new DrawingVisual();
    using (var dc = visual.RenderOpen())
    {
        dc.PushTransform(new ScaleTransform(size / 256.0, size / 256.0)); // design in a 256 x 256 box
        var small = size <= 24;

        // Rounded tile: indigo -> violet, with a soft highlight on the upper half.
        var tile = new Rect(8, 8, 240, 240);
        var gradient = new LinearGradientBrush(Color.FromRgb(0x4F, 0x46, 0xE5), Color.FromRgb(0x93, 0x33, 0xEA), new Point(0, 0), new Point(1, 1));
        dc.DrawRoundedRectangle(gradient, null, tile, 56, 56);
        var shine = new LinearGradientBrush(new GradientStopCollection
        {
            new(Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF), 0), new(Color.FromArgb(0, 0xFF, 0xFF, 0xFF), 0.55),
        }, 90);
        dc.DrawRoundedRectangle(shine, null, tile, 56, 56);

        if (!small)
        {
            // Viewfinder corners framing the capture area.
            var pen = new Pen(Brushes.White, 14) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            const double l = 56, t = 64, r = 200, b = 192, arm = 38;
            foreach (var (x, y, dx, dy) in new[] { (l, t, 1, 1), (r, t, -1, 1), (l, b, 1, -1), (r, b, -1, -1) })
            {
                var g = new StreamGeometry();
                using (var ctx = g.Open())
                {
                    ctx.BeginFigure(new Point(x, y + dy * arm), false, false);
                    ctx.LineTo(new Point(x, y), true, true);
                    ctx.LineTo(new Point(x + dx * arm, y), true, true);
                }
                dc.DrawGeometry(null, pen, g);
            }
        }

        // Record dot with a white ring and a small gloss.
        var center = new Point(128, 128);
        var radius = small ? 66.0 : 36.0;
        dc.DrawEllipse(Brushes.White, null, center, radius + (small ? 18 : 11), radius + (small ? 18 : 11));
        var red = new RadialGradientBrush(Color.FromRgb(0xFF, 0x6B, 0x6B), Color.FromRgb(0xDC, 0x26, 0x26))
        {
            GradientOrigin = new Point(0.35, 0.3), Center = new Point(0.4, 0.35), RadiusX = 0.75, RadiusY = 0.75,
        };
        dc.DrawEllipse(red, null, center, radius, radius);
        if (!small)
            dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(0x70, 0xFF, 0xFF, 0xFF)), null,
                new Point(center.X - radius * 0.35, center.Y - radius * 0.4), radius * 0.28, radius * 0.18);
    }
    var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
    bitmap.Render(visual);
    return bitmap;
}

static byte[] Png(BitmapSource image)
{
    var encoder = new PngBitmapEncoder();
    encoder.Frames.Add(BitmapFrame.Create(image));
    using var ms = new MemoryStream();
    encoder.Save(ms);
    return ms.ToArray();
}

// ICO container with PNG-compressed entries (supported since Windows Vista).
static void WriteIco(string path, int[] sizes, List<byte[]> pngs)
{
    using var w = new BinaryWriter(File.Create(path));
    w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
    var offset = 6 + 16 * sizes.Length;
    for (var i = 0; i < sizes.Length; i++)
    {
        var dim = (byte)(sizes[i] >= 256 ? 0 : sizes[i]);
        w.Write(dim); w.Write(dim); w.Write((byte)0); w.Write((byte)0);
        w.Write((short)1); w.Write((short)32);
        w.Write(pngs[i].Length); w.Write(offset);
        offset += pngs[i].Length;
    }
    pngs.ForEach(w.Write);
}
