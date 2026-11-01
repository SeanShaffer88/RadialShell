using System.Drawing.Drawing2D;

namespace RadialShell.App;

/// <summary>Interactive visual preview for the built-in radial color themes.</summary>
internal sealed class RadialMenuPreviewForm : Form
{
    private static readonly string[] ItemLabels =
    [
        "Copy\npath", "Copy\nname", "Open\nfolder",
        "Terminal\nhere", "POSIX\npath", "Copy\ncontents"
    ];

    private const float CenterX = 250;
    private const float CenterY = 276;
    private const float OuterRadius = 174;
    private const float InnerRadius = 67;
    private int _themeIndex;
    private int _hoveredSlice = -1;
    private string _status = "Preview only — no file action will run.";

    public RadialMenuPreviewForm(RadialTheme initialTheme)
    {
        _themeIndex = Math.Max(0, RadialThemeCatalog.All.ToList().FindIndex(x => x.Name == initialTheme.Name));
        DoubleBuffered = true;
        KeyPreview = true;
        ClientSize = new Size(500, 590);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = true;
        Text = "RadialShell — Theme preview";
        BackColor = initialTheme.Window;
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        Cursor = Cursors.Hand;
    }

    private RadialTheme Theme => RadialThemeCatalog.All[_themeIndex];

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        using (var background = new LinearGradientBrush(ClientRectangle, Theme.Window,
                   ControlPaint.Light(Theme.Window, 0.08F), 35F))
            graphics.FillRectangle(background, ClientRectangle);

        DrawHeader(graphics);
        DrawWheel(graphics);
        DrawFooter(graphics);
    }

    private void DrawHeader(Graphics graphics)
    {
        using var titleFont = new Font("Segoe UI Semibold", 17F, FontStyle.Bold, GraphicsUnit.Point);
        using var subtitleFont = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        using var titleBrush = new SolidBrush(Theme.Text);
        using var mutedBrush = new SolidBrush(Theme.Muted);
        using var accentBrush = new SolidBrush(Theme.Accent);
        using var centered = new StringFormat { Alignment = StringAlignment.Center };

        graphics.DrawString("RADIALSHELL", titleFont, titleBrush, new PointF(CenterX, 24), centered);
        graphics.DrawString("THEME PREVIEW", subtitleFont, accentBrush, new PointF(CenterX, 53), centered);

        using var themeFont = new Font("Segoe UI Semibold", 10F, FontStyle.Bold, GraphicsUnit.Point);
        var themeLabel = $"{Theme.Name}  ·  {_themeIndex + 1} / {RadialThemeCatalog.All.Count}";
        graphics.DrawString(themeLabel, themeFont, titleBrush, new PointF(CenterX, 75), centered);
        graphics.DrawString("Choose a palette with ← and →", subtitleFont, mutedBrush, new PointF(CenterX, 96), centered);
    }

    private void DrawWheel(Graphics graphics)
    {
        var center = new PointF(CenterX, CenterY);
        var outer = new RectangleF(CenterX - OuterRadius, CenterY - OuterRadius, OuterRadius * 2, OuterRadius * 2);
        var hub = new RectangleF(CenterX - InnerRadius + 3, CenterY - InnerRadius + 3,
            (InnerRadius - 3) * 2, (InnerRadius - 3) * 2);

        using (var shadowPen = new Pen(Color.FromArgb(70, 0, 0, 0), 10F))
            graphics.DrawEllipse(shadowPen, outer);

        for (var index = 0; index < ItemLabels.Length; index++)
        {
            var start = -90F + index * 60F + 2.7F;
            const float sweep = 54.6F;
            using var path = CreateRingSlice(start, sweep);
            var baseColor = index % 2 == 0 ? Theme.Sector : Theme.SectorAlternate;
            using var fill = new SolidBrush(index == _hoveredSlice ? Theme.Hover : baseColor);
            using var border = new Pen(index == _hoveredSlice ? Theme.Accent : Theme.Border,
                index == _hoveredSlice ? 2.4F : 1.1F);
            graphics.FillPath(fill, path);
            graphics.DrawPath(border, path);

            var middle = -90F + index * 60F + 30F;
            DrawSliceContent(graphics, center, middle, index);
        }

        using (var ringPen = new Pen(Color.FromArgb(145, Theme.Accent), 1.4F))
            graphics.DrawEllipse(ringPen, outer);

        using var hubFill = new SolidBrush(Theme.Hub);
        using var hubOutline = new Pen(Theme.Border, 1.2F);
        graphics.FillEllipse(hubFill, hub);
        graphics.DrawEllipse(hubOutline, hub);

        using var hubTitleFont = new Font("Segoe UI Semibold", 11F, FontStyle.Bold, GraphicsUnit.Point);
        using var hubSubFont = new Font("Segoe UI", 7.5F, FontStyle.Regular, GraphicsUnit.Point);
        using var titleBrush = new SolidBrush(Theme.Text);
        using var accentBrush = new SolidBrush(Theme.Accent);
        using var centered = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        graphics.DrawString("RADIAL", hubTitleFont, titleBrush, new PointF(CenterX, CenterY - 7), centered);
        graphics.DrawString("SHELL", hubSubFont, accentBrush, new PointF(CenterX, CenterY + 12), centered);
    }

    private void DrawSliceContent(Graphics graphics, PointF center, float angleDegrees, int index)
    {
        var radians = angleDegrees * Math.PI / 180.0;
        var badgeCenter = new PointF(center.X + (float)Math.Cos(radians) * 96F,
            center.Y + (float)Math.Sin(radians) * 96F);
        var labelCenter = new PointF(center.X + (float)Math.Cos(radians) * 142F,
            center.Y + (float)Math.Sin(radians) * 142F);
        var badgeRect = new RectangleF(badgeCenter.X - 11F, badgeCenter.Y - 11F, 22F, 22F);

        using var badgeBrush = new SolidBrush(_hoveredSlice == index ? Theme.Accent : Theme.Hub);
        using var badgeOutline = new Pen(Theme.Accent, 1.2F);
        using var badgeText = new SolidBrush(_hoveredSlice == index ? Theme.Hub : Theme.Accent);
        using var badgeFont = new Font("Segoe UI Semibold", 8F, FontStyle.Bold, GraphicsUnit.Point);
        using var labelFont = new Font("Segoe UI Semibold", 8.6F, FontStyle.Bold, GraphicsUnit.Point);
        using var labelBrush = new SolidBrush(Theme.Text);
        using var centered = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter
        };

        graphics.FillEllipse(badgeBrush, badgeRect);
        graphics.DrawEllipse(badgeOutline, badgeRect);
        graphics.DrawString((index + 1).ToString(), badgeFont, badgeText, badgeCenter, centered);
        var labelRect = new RectangleF(labelCenter.X - 34F, labelCenter.Y - 22F, 68F, 44F);
        graphics.DrawString(ItemLabels[index], labelFont, labelBrush, labelRect, centered);
    }

    private GraphicsPath CreateRingSlice(float start, float sweep)
    {
        var path = new GraphicsPath();
        var outerRect = new RectangleF(CenterX - OuterRadius, CenterY - OuterRadius, OuterRadius * 2, OuterRadius * 2);
        var innerRadius = InnerRadius + 3F;
        var innerRect = new RectangleF(CenterX - innerRadius, CenterY - innerRadius, innerRadius * 2, innerRadius * 2);
        path.AddArc(outerRect, start, sweep);
        path.AddArc(innerRect, start + sweep, -sweep);
        path.CloseFigure();
        return path;
    }

    private void DrawFooter(Graphics graphics)
    {
        using var footerFont = new Font("Segoe UI", 8.5F, FontStyle.Regular, GraphicsUnit.Point);
        using var hintFont = new Font("Segoe UI", 8F, FontStyle.Regular, GraphicsUnit.Point);
        using var textBrush = new SolidBrush(Theme.Text);
        using var mutedBrush = new SolidBrush(Theme.Muted);
        using var centered = new StringFormat { Alignment = StringAlignment.Center };

        graphics.DrawString(_status, footerFont, textBrush, new PointF(CenterX, 500), centered);
        graphics.DrawString("← / → change theme     Hover a slice     Esc closes", hintFont, mutedBrush,
            new PointF(CenterX, 526), centered);
        graphics.DrawString("Built-in palettes are saved as your preference", hintFont, mutedBrush,
            new PointF(CenterX, 548), centered);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var index = GetSliceIndex(e.Location);
        if (index == _hoveredSlice) return;
        _hoveredSlice = index;
        _status = index >= 0 ? $"Preview: {ItemLabels[index].Replace('\n', ' ')}  ·  click to select" : "Preview only — no file action will run.";
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hoveredSlice = -1;
        _status = "Preview only — no file action will run.";
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left && _hoveredSlice >= 0)
        {
            _status = $"Selected preview slice: {ItemLabels[_hoveredSlice].Replace('\n', ' ')} (no action executed)";
            Invalidate();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode is Keys.Left or Keys.Right)
        {
            CycleTheme(e.KeyCode == Keys.Right ? 1 : -1);
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.Escape)
        {
            Close();
            e.Handled = true;
        }
    }

    private int GetSliceIndex(Point point)
    {
        var dx = point.X - CenterX;
        var dy = point.Y - CenterY;
        var distance = Math.Sqrt(dx * dx + dy * dy);
        if (distance < InnerRadius || distance > OuterRadius)
            return -1;

        var angle = Math.Atan2(dy, dx) * 180.0 / Math.PI;
        var normalized = (angle + 90 + 360) % 360;
        return Math.Clamp((int)(normalized / 60), 0, ItemLabels.Length - 1);
    }

    private void CycleTheme(int delta)
    {
        var count = RadialThemeCatalog.All.Count;
        _themeIndex = (_themeIndex + delta + count) % count;
        var next = Theme;
        BackColor = next.Window;
        _status = $"Theme changed to {next.Name}.";
        _ = RadialThemeSettings.Save(next.Name, out _);
        Invalidate();
    }
}
