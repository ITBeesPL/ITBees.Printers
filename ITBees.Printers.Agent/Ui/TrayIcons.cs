using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace ITBees.Printers.Agent.Ui;

/// <summary>The tray icon is drawn in code: a printer with a status dot (green / amber / red).</summary>
public sealed class TrayIcons : IDisposable
{
    public TrayIcons()
    {
        Connected = Create(Color.FromArgb(34, 197, 94));
        Connecting = Create(Color.FromArgb(245, 158, 11));
        Attention = Create(Color.FromArgb(239, 68, 68));
    }

    public Icon Connected { get; }
    public Icon Connecting { get; }
    public Icon Attention { get; }

    public void Dispose()
    {
        foreach (var icon in new[] { Connected, Connecting, Attention })
        {
            DestroyIcon(icon.Handle);
            icon.Dispose();
        }
    }

    private static Icon Create(Color statusColor)
    {
        using var bitmap = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            using var body = new SolidBrush(Color.FromArgb(55, 65, 81));
            using var paper = new SolidBrush(Color.White);
            using var outline = new Pen(Color.FromArgb(55, 65, 81), 2f);
            using var status = new SolidBrush(statusColor);

            // Paper going in at the top, the printer body, paper coming out at the bottom.
            g.FillRectangle(paper, 9, 3, 14, 9);
            g.DrawRectangle(outline, 9, 3, 14, 9);
            using (var path = RoundedRectangle(new Rectangle(3, 10, 26, 13), 3))
            {
                g.FillPath(body, path);
            }

            g.FillRectangle(paper, 8, 18, 16, 10);
            g.DrawRectangle(outline, 8, 18, 16, 10);

            g.FillEllipse(Brushes.White, 19, 19, 13, 13);
            g.FillEllipse(status, 21, 21, 9, 9);
        }

        return Icon.FromHandle(bitmap.GetHicon());
    }

    private static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
    {
        var diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);
}
