using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ClaudeMon.Services;

/// <summary>
/// Generates dynamic tray icons that display a colored rounded square with a
/// usage-percentage number. Renders at the system's DPI-aware icon size for
/// maximum clarity. Colour thresholds: green (0-50), amber (51-80),
/// red (81-100), grey (no data / negative).
/// </summary>
public class TrayIconService : IDisposable
{
    private IntPtr _currentIconHandle;
    private bool _disposed;

    /// <summary>
    /// Creates a WPF <see cref="ImageSource"/> suitable for binding to an Image control.
    /// </summary>
    public ImageSource CreateIcon(double percentage)
    {
        using var bitmap = RenderBitmap(percentage);
        var hBitmap = bitmap.GetHbitmap();
        try
        {
            return Imaging.CreateBitmapSourceFromHBitmap(
                hBitmap,
                IntPtr.Zero,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
        }
        finally
        {
            DeleteObject(hBitmap);
        }
    }

    /// <summary>
    /// Creates a <see cref="System.Drawing.Icon"/> suitable for
    /// <c>Hardcodet.NotifyIcon.Wpf.TaskbarIcon.Icon</c>.
    /// Tracks the native handle for proper cleanup.
    /// </summary>
    public Icon CreateNotifyIcon(double percentage)
    {
        // Clean up the previous native icon handle.
        CleanupCurrentHandle();

        using var bitmap = RenderBitmap(percentage);
        _currentIconHandle = bitmap.GetHicon();
        return Icon.FromHandle(_currentIconHandle);
    }

    // ────────────────────────────────────────────────────────
    // Rendering
    // ────────────────────────────────────────────────────────

    private static Bitmap RenderBitmap(double percentage)
    {
        // Use the standard icon size (SM_CXICON) rather than the small icon size
        // so the tray icon renders at 32×32 or larger, matching other tray icons.
        var size = GetSystemMetrics(SM_CXICON);
        if (size < 32) size = 32;

        var bitmap = new Bitmap(size, size);

        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.HighQuality;
        g.TextRenderingHint = TextRenderingHint.AntiAlias;
        g.Clear(System.Drawing.Color.Transparent);

        // Rounded rectangle background - more room for text than a circle.
        var statusColor = GetStatusColor(percentage);
        var radius = Math.Max(size / 5, 3);
        var bgRect = new Rectangle(0, 0, size - 1, size - 1);

        using var brush = new SolidBrush(statusColor);
        using var path = RoundedRect(bgRect, radius);
        g.FillPath(brush, path);

        // Subtle dark outline for visibility on light taskbars.
        using var outlinePen = new System.Drawing.Pen(
            System.Drawing.Color.FromArgb(60, 0, 0, 0), 1f);
        g.DrawPath(outlinePen, path);

        // Percentage text, white, centered.
        var text = GetDisplayText(percentage);
        var fontSize = GetFontSize(text, size);
        using var font = new Font("Segoe UI", fontSize, System.Drawing.FontStyle.Bold, GraphicsUnit.Pixel);
        using var textBrush = new SolidBrush(System.Drawing.Color.White);
        using var sf = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };

        var textRect = new RectangleF(0, 0, size, size);
        g.DrawString(text, font, textBrush, textRect, sf);

        return bitmap;
    }

    private static GraphicsPath RoundedRect(Rectangle rect, int radius)
    {
        var path = new GraphicsPath();
        var d = radius * 2;
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static string GetDisplayText(double percentage)
    {
        if (percentage < 0) return "?";
        if (percentage >= 100) return "!";

        var rounded = (int)Math.Round(percentage);
        return rounded.ToString();
    }

    private static float GetFontSize(string text, int iconSize)
    {
        // Scale font relative to icon size. A 16px icon gets ~10-12px font.
        var baseFraction = text.Length switch
        {
            >= 3 => 0.55f,
            2 => 0.65f,
            _ => 0.72f,
        };
        return Math.Max(iconSize * baseFraction, 8f);
    }

    private static System.Drawing.Color GetStatusColor(double percentage)
    {
        if (percentage < 0) return System.Drawing.Color.FromArgb(158, 158, 158);  // grey  - no data
        if (percentage <= 50) return System.Drawing.Color.FromArgb(76, 175, 80);   // green
        if (percentage <= 80) return System.Drawing.Color.FromArgb(255, 152, 0);   // amber
        return System.Drawing.Color.FromArgb(244, 67, 54);                         // red
    }

    // ────────────────────────────────────────────────────────
    // Cleanup
    // ────────────────────────────────────────────────────────

    private void CleanupCurrentHandle()
    {
        if (_currentIconHandle != IntPtr.Zero)
        {
            DestroyIcon(_currentIconHandle);
            _currentIconHandle = IntPtr.Zero;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        CleanupCurrentHandle();
        GC.SuppressFinalize(this);
    }

    // ────────────────────────────────────────────────────────
    // P/Invoke
    // ────────────────────────────────────────────────────────

    private const int SM_CXICON = 11;

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);
}
