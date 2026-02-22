using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ClaudeMon.Models;

namespace ClaudeMon.Services;

/// <summary>
/// Generates dynamic tray icons showing either a colored percentage badge (standard themes)
/// or a custom face image from a CustomTheme's faceImages list.
/// </summary>
public class TrayIconService : IDisposable
{
    private IntPtr _currentIconHandle;
    private bool _disposed;

    // Cached face bitmaps — loaded once from filesystem and reused, keyed by absolute path.
    private static readonly Dictionary<string, System.Drawing.Bitmap> _faceCache = [];

    /// <summary>
    /// Creates a WPF <see cref="ImageSource"/> suitable for binding to an Image control.
    /// </summary>
    public ImageSource CreateIcon(double percentage, CustomTheme? customTheme = null)
    {
        using var bitmap = RenderBitmap(percentage, customTheme);
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
    public Icon CreateNotifyIcon(double percentage, CustomTheme? customTheme = null)
    {
        CleanupCurrentHandle();

        using var bitmap = RenderBitmap(percentage, customTheme);
        _currentIconHandle = bitmap.GetHicon();
        return Icon.FromHandle(_currentIconHandle);
    }

    // ────────────────────────────────────────────────────────
    // Rendering
    // ────────────────────────────────────────────────────────

    private static System.Drawing.Bitmap RenderBitmap(double percentage, CustomTheme? customTheme)
    {
        var size = GetSystemMetrics(SM_CXICON);
        if (size < 32) size = 32;

        return customTheme?.FaceImages is { Count: > 0 }
            ? RenderFaceImage(percentage, customTheme, size)
            : RenderBadge(percentage, size);
    }

    /// <summary>Renders a custom theme face image scaled to the icon size.</summary>
    private static System.Drawing.Bitmap RenderFaceImage(double percentage, CustomTheme customTheme, int size)
    {
        var facePath = customTheme.ResolveFacePath(percentage);
        if (facePath == null)
            return RenderBadge(percentage, size);

        var faceBmp = LoadFaceFromFile(facePath);
        var bitmap = new System.Drawing.Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = System.Drawing.Graphics.FromImage(bitmap);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
        g.Clear(System.Drawing.Color.Transparent);
        g.DrawImage(faceBmp, 0, 0, size, size);
        return bitmap;
    }

    /// <summary>Loads a face bitmap from the filesystem, caching after first load.</summary>
    private static System.Drawing.Bitmap LoadFaceFromFile(string absolutePath)
    {
        if (_faceCache.TryGetValue(absolutePath, out var cached)) return cached;
        var bmp = new System.Drawing.Bitmap(absolutePath);
        _faceCache[absolutePath] = bmp;
        return bmp;
    }

    /// <summary>Renders the standard colored badge with a percentage number.</summary>
    private static System.Drawing.Bitmap RenderBadge(double percentage, int size)
    {
        var bitmap = new System.Drawing.Bitmap(size, size);

        using var g = System.Drawing.Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.HighQuality;
        g.TextRenderingHint = TextRenderingHint.AntiAlias;
        g.Clear(System.Drawing.Color.Transparent);

        var statusColor = GetStatusColor(percentage);
        var radius = Math.Max(size / 5, 3);
        var bgRect = new Rectangle(0, 0, size - 1, size - 1);

        using var brush = new SolidBrush(statusColor);
        using var path = RoundedRect(bgRect, radius);
        g.FillPath(brush, path);

        using var outlinePen = new System.Drawing.Pen(
            System.Drawing.Color.FromArgb(60, 0, 0, 0), 1f);
        g.DrawPath(outlinePen, path);

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
        return ((int)Math.Round(percentage)).ToString();
    }

    private static float GetFontSize(string text, int iconSize)
    {
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
        if (percentage < 0)   return System.Drawing.Color.FromArgb(158, 158, 158);
        if (percentage <= 50) return System.Drawing.Color.FromArgb(76, 175, 80);
        if (percentage <= 80) return System.Drawing.Color.FromArgb(255, 152, 0);
        return System.Drawing.Color.FromArgb(244, 67, 54);
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
