using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: IconBuilder <assets-directory>");
    return 2;
}

string output = Path.GetFullPath(args[0]);
Directory.CreateDirectory(output);
int[] sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];
var images = sizes.Select(RenderPng).ToArray();

using (var file = File.Create(Path.Combine(output, "BS2Lite.ico")))
using (var writer = new BinaryWriter(file))
{
    writer.Write((ushort)0);
    writer.Write((ushort)1);
    writer.Write((ushort)images.Length);
    int offset = 6 + images.Length * 16;
    for (int i = 0; i < images.Length; i++)
    {
        int size = sizes[i];
        writer.Write((byte)(size >= 256 ? 0 : size));
        writer.Write((byte)(size >= 256 ? 0 : size));
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write((ushort)1);
        writer.Write((ushort)32);
        writer.Write(images[i].Length);
        writer.Write(offset);
        offset += images[i].Length;
    }
    foreach (byte[] image in images) writer.Write(image);
}

File.WriteAllBytes(Path.Combine(output, "BS2Lite.png"), RenderPng(512));
Console.WriteLine(Path.Combine(output, "BS2Lite.ico"));
return 0;

static byte[] RenderPng(int size)
{
    using var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
    using Graphics g = Graphics.FromImage(bitmap);
    g.SmoothingMode = SmoothingMode.AntiAlias;
    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
    g.Clear(Color.Transparent);

    float u = size / 256f;
    using var card = RoundedRect(new RectangleF(8 * u, 8 * u, 240 * u, 240 * u), 54 * u);
    using var cardBrush = new LinearGradientBrush(new PointF(20 * u, 18 * u), new PointF(230 * u, 238 * u), Color.FromArgb(255, 31, 34, 39), Color.FromArgb(255, 13, 15, 18));
    g.FillPath(cardBrush, card);

    using var rim = new Pen(Color.FromArgb(255, 246, 161, 48), 18 * u) { StartCap = LineCap.Round, EndCap = LineCap.Round };
    g.DrawArc(rim, 49 * u, 48 * u, 158 * u, 158 * u, -54, 288);

    using var air = new Pen(Color.FromArgb(255, 95, 228, 163), 10 * u) { StartCap = LineCap.Round, EndCap = LineCap.Round };
    g.DrawArc(air, 67 * u, 66 * u, 122 * u, 122 * u, 38, 210);

    PointF center = new(128 * u, 128 * u);
    using var bladeBrush = new SolidBrush(Color.FromArgb(255, 95, 228, 163));
    for (int i = 0; i < 3; i++)
    {
        GraphicsState state = g.Save();
        g.TranslateTransform(center.X, center.Y);
        g.RotateTransform(i * 120);
        using var blade = new GraphicsPath();
        blade.AddBezier(8 * u, -9 * u, 42 * u, -43 * u, 65 * u, -29 * u, 56 * u, 0 * u);
        blade.AddBezier(51 * u, 20 * u, 28 * u, 21 * u, 7 * u, 10 * u, 5 * u, 2 * u);
        blade.CloseFigure();
        g.FillPath(bladeBrush, blade);
        g.Restore(state);
    }

    using var hub = new SolidBrush(Color.FromArgb(255, 246, 161, 48));
    g.FillEllipse(hub, 113 * u, 113 * u, 30 * u, 30 * u);
    using var shine = new SolidBrush(Color.FromArgb(230, 255, 255, 255));
    g.FillEllipse(shine, 122 * u, 118 * u, 9 * u, 9 * u);

    using var stream = new Pen(Color.FromArgb(225, 95, 228, 163), 9 * u) { StartCap = LineCap.Round, EndCap = LineCap.Round };
    g.DrawLine(stream, 58 * u, 204 * u, 113 * u, 204 * u);
    g.DrawLine(stream, 143 * u, 204 * u, 198 * u, 204 * u);

    using var memory = new MemoryStream();
    bitmap.Save(memory, ImageFormat.Png);
    return memory.ToArray();
}

static GraphicsPath RoundedRect(RectangleF rect, float radius)
{
    var path = new GraphicsPath();
    float d = radius * 2;
    path.AddArc(rect.X, rect.Y, d, d, 180, 90);
    path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
    path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
    path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
    path.CloseFigure();
    return path;
}
