using System.Drawing.Drawing2D;

namespace ValheimVoiceTranslator.Overlay;

/// <summary>
/// Transparent, always-on-top overlay window that displays subtitle text.
/// </summary>
public sealed class SubtitleOverlay : Form
{
    private string _originalText = string.Empty;
    private string _translatedText = string.Empty;
    private System.Windows.Forms.Timer? _hideTimer;

    // Appearance settings
    public Font SubtitleFont { get; set; } = new Font("Malgun Gothic", 18f, FontStyle.Bold);
    public Color TextColor { get; set; } = Color.White;
    public Color OutlineColor { get; set; } = Color.Black;
    public Color BackgroundColor { get; set; } = Color.FromArgb(140, 0, 0, 0);
    public int DisplayDurationMs { get; set; } = 5000;
    public bool ShowOriginal { get; set; } = true;

    public SubtitleOverlay()
    {
        // Transparent, borderless, always-on-top overlay
        FormBorderStyle = FormBorderStyle.None;
        BackColor = Color.Black;
        TransparencyKey = Color.Black;
        TopMost = true;
        ShowInTaskbar = false;
        DoubleBuffered = true;
        StartPosition = FormStartPosition.Manual;

        // Position at bottom-center of primary screen
        var screen = Screen.PrimaryScreen!.WorkingArea;
        int w = screen.Width - 200;
        int h = 120;
        Location = new Point(screen.Left + 100, screen.Bottom - h - 60);
        Size = new Size(w, h);

        // Allow drag-to-reposition
        MouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                // Release capture and send WM_NCLBUTTONDOWN for HTCAPTION
                ReleaseCapture();
                SendMessage(Handle, 0xA1, 0x2, IntPtr.Zero);
            }
        };

        _hideTimer = new System.Windows.Forms.Timer { Interval = DisplayDurationMs };
        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            _originalText = string.Empty;
            _translatedText = string.Empty;
            Invalidate();
        };
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, IntPtr lParam);

    /// <summary>
    /// Show a translation result on the overlay.
    /// </summary>
    public void ShowSubtitle(string original, string translation)
    {
        if (InvokeRequired)
        {
            Invoke(() => ShowSubtitle(original, translation));
            return;
        }

        _originalText = original;
        _translatedText = translation;
        _hideTimer!.Interval = DisplayDurationMs;
        _hideTimer.Stop();
        _hideTimer.Start();
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        if (string.IsNullOrWhiteSpace(_translatedText))
            return;

        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

        var rect = new Rectangle(10, 5, Width - 20, Height - 10);

        // Draw semi-transparent background
        using var bgBrush = new SolidBrush(BackgroundColor);
        g.FillRoundedRectangle(bgBrush, rect, 12);

        // Layout: original (small, gray) on top; translation (large, white) below
        var lines = ShowOriginal
            ? new[] { (_originalText, 11f, Color.LightGray), (_translatedText, 18f, TextColor) }
            : new[] { (_translatedText, 18f, TextColor) };

        float y = 12f;
        foreach (var (text, size, color) in lines)
        {
            if (string.IsNullOrWhiteSpace(text)) continue;
            using var font = new Font(SubtitleFont.FontFamily, size, FontStyle.Bold);
            DrawOutlinedText(g, text, font, color, OutlineColor, rect.X + 10, (int)y, rect.Width - 20);
            y += font.GetHeight(g) + 4;
        }
    }

    private static void DrawOutlinedText(
        Graphics g, string text, Font font, Color fill, Color outline,
        int x, int y, int maxWidth)
    {
        using var path = new GraphicsPath();
        using var sf = new StringFormat(StringFormat.GenericTypographic)
        {
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap,
        };

        path.AddString(text, font.FontFamily, (int)font.Style, font.Size * 1.3f,
            new RectangleF(x, y, maxWidth, 100), sf);

        // Outline
        using var outlinePen = new Pen(outline, 3f) { LineJoin = LineJoin.Round };
        g.DrawPath(outlinePen, path);

        // Fill
        using var fillBrush = new SolidBrush(fill);
        g.FillPath(fillBrush, path);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _hideTimer?.Dispose();
            SubtitleFont.Dispose();
        }
        base.Dispose(disposing);
    }
}

internal static class GraphicsExtensions
{
    public static void FillRoundedRectangle(this Graphics g, Brush brush, Rectangle rect, int radius)
    {
        using var path = new GraphicsPath();
        path.AddArc(rect.X, rect.Y, radius * 2, radius * 2, 180, 90);
        path.AddArc(rect.Right - radius * 2, rect.Y, radius * 2, radius * 2, 270, 90);
        path.AddArc(rect.Right - radius * 2, rect.Bottom - radius * 2, radius * 2, radius * 2, 0, 90);
        path.AddArc(rect.X, rect.Bottom - radius * 2, radius * 2, radius * 2, 90, 90);
        path.CloseFigure();
        g.FillPath(brush, path);
    }
}
