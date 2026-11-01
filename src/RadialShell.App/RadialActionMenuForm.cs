using System.Drawing.Drawing2D;
using RadialShell.Core;

namespace RadialShell.App;

/// <summary>Floating action wheel displayed for a resolved Explorer target.</summary>
internal sealed class RadialActionMenuForm : Form
{
    private const float CenterX = 190;
    private const float CenterY = 190;
    private const float OuterRadius = 172;
    private const float InnerRadius = 57;
    private readonly ShellTarget _target;
    private readonly NativePoint _triggerPoint;
    private readonly System.Windows.Forms.Timer _closeTimer;
    private int _hoveredSlice = -1;
    private string? _status;

    public RadialActionMenuForm(ShellTarget target, RadialTheme theme, NativePoint triggerPoint)
    {
        _target = target;
        Theme = theme;
        _triggerPoint = triggerPoint;
        AutoScaleMode = AutoScaleMode.None;
        DoubleBuffered = true;
        KeyPreview = true;
        ClientSize = new Size(380, 380);
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        BackColor = Theme.Window;
        Text = "RadialShell";
        Cursor = Cursors.Hand;
        Region = new Region(CreateRoundedRectangle(new Rectangle(0, 0, 380, 380), 24));

        _closeTimer = new System.Windows.Forms.Timer { Interval = 1200 };
        _closeTimer.Tick += (_, _) => { _closeTimer.Stop(); Close(); };
    }

    private RadialTheme Theme { get; }

    public void ShowAtTrigger()
    {
        var screen = Screen.FromPoint(new Point(_triggerPoint.X, _triggerPoint.Y)).WorkingArea;
        var x = Math.Clamp(_triggerPoint.X - ClientSize.Width / 2, screen.Left, screen.Right - ClientSize.Width);
        var y = Math.Clamp(_triggerPoint.Y - ClientSize.Height / 2, screen.Top, screen.Bottom - ClientSize.Height);
        Location = new Point(x, y);
        Show();
        Activate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var bounds = new RectangleF(1, 1, ClientSize.Width - 2, ClientSize.Height - 2);
        using (var background = new SolidBrush(Theme.Window))
        using (var border = new Pen(Theme.Border, 1.5F))
        using (var rounded = CreateRoundedRectangle(Rectangle.Round(bounds), 24))
        {
            g.FillPath(background, rounded);
            g.DrawPath(border, rounded);
        }

        DrawSectors(g);
        DrawHub(g);
        if (!string.IsNullOrWhiteSpace(_status))
            DrawStatus(g);
    }

    private void DrawSectors(Graphics g)
    {
        for (var index = 0; index < RadialActionCatalog.Labels.Count; index++)
        {
            var start = -90F + index * 60F + 2.8F;
            const float sweep = 54.4F;
            using var path = CreateRingSlice(start, sweep);
            var baseColor = index % 2 == 0 ? Theme.Sector : Theme.SectorAlternate;
            using var fill = new SolidBrush(index == _hoveredSlice ? Theme.Hover : baseColor);
            using var outline = new Pen(index == _hoveredSlice ? Theme.Accent : Theme.Border,
                index == _hoveredSlice ? 2.4F : 1.0F);
            g.FillPath(fill, path);
            g.DrawPath(outline, path);

            var middle = -90F + index * 60F + 30F;
            DrawSliceLabel(g, middle, index);
        }

        using var outerRing = new Pen(Color.FromArgb(170, Theme.Accent), 1.5F);
        g.DrawEllipse(outerRing, CenterX - OuterRadius, CenterY - OuterRadius,
            OuterRadius * 2, OuterRadius * 2);
    }

    private void DrawSliceLabel(Graphics g, float angleDegrees, int index)
    {
        var radians = angleDegrees * Math.PI / 180.0;
        var badgeCenter = new PointF(CenterX + (float)Math.Cos(radians) * 88F,
            CenterY + (float)Math.Sin(radians) * 88F);
        var labelCenter = new PointF(CenterX + (float)Math.Cos(radians) * 136F,
            CenterY + (float)Math.Sin(radians) * 136F);
        var badge = new RectangleF(badgeCenter.X - 10, badgeCenter.Y - 10, 20, 20);

        using var badgeFill = new SolidBrush(_hoveredSlice == index ? Theme.Accent : Theme.Hub);
        using var badgeBorder = new Pen(Theme.Accent, 1.1F);
        using var badgeText = new SolidBrush(_hoveredSlice == index ? Theme.Hub : Theme.Accent);
        using var badgeFont = new Font("Segoe UI Semibold", 7.5F, FontStyle.Bold, GraphicsUnit.Point);
        using var labelFont = new Font("Segoe UI Semibold", 8.1F, FontStyle.Bold, GraphicsUnit.Point);
        using var labelBrush = new SolidBrush(Theme.Text);
        using var centered = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };

        g.FillEllipse(badgeFill, badge);
        g.DrawEllipse(badgeBorder, badge);
        g.DrawString((index + 1).ToString(), badgeFont, badgeText, badgeCenter, centered);
        var labelRect = new RectangleF(labelCenter.X - 37, labelCenter.Y - 20, 74, 40);
        g.DrawString(RadialActionCatalog.Labels[index], labelFont, labelBrush, labelRect, centered);
    }

    private void DrawHub(Graphics g)
    {
        var hub = new RectangleF(CenterX - InnerRadius + 3, CenterY - InnerRadius + 3,
            (InnerRadius - 3) * 2, (InnerRadius - 3) * 2);
        using var fill = new SolidBrush(Theme.Hub);
        using var outline = new Pen(Theme.Border, 1.2F);
        using var brandFont = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold, GraphicsUnit.Point);
        using var detailFont = new Font("Segoe UI", 6.5F, FontStyle.Regular, GraphicsUnit.Point);
        using var brand = new SolidBrush(Theme.Text);
        using var detail = new SolidBrush(Theme.Accent);
        using var centered = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };

        g.FillEllipse(fill, hub);
        g.DrawEllipse(outline, hub);
        g.DrawString("RADIAL", brandFont, brand, new PointF(CenterX, CenterY - 7), centered);
        var targetLabel = _target.Kind == ShellTargetKind.Folder
            ? "FOLDER"
            : $"{Math.Max(1, _target.Items.Count)} ITEM{(_target.Items.Count == 1 ? string.Empty : "S")}";
        g.DrawString(targetLabel, detailFont, detail, new PointF(CenterX, CenterY + 11), centered);
    }

    private void DrawStatus(Graphics g)
    {
        using var font = new Font("Segoe UI", 7F, FontStyle.Regular, GraphicsUnit.Point);
        using var brush = new SolidBrush(Theme.Text);
        using var centered = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        var rect = new RectangleF(30, 334, 320, 34);
        g.DrawString(_status!, font, brush, rect, centered);
    }

    private GraphicsPath CreateRingSlice(float start, float sweep)
    {
        var path = new GraphicsPath();
        var outer = new RectangleF(CenterX - OuterRadius, CenterY - OuterRadius, OuterRadius * 2, OuterRadius * 2);
        var innerRadius = InnerRadius + 3F;
        var inner = new RectangleF(CenterX - innerRadius, CenterY - innerRadius, innerRadius * 2, innerRadius * 2);
        path.AddArc(outer, start, sweep);
        path.AddArc(inner, start + sweep, -sweep);
        path.CloseFigure();
        return path;
    }

    private static GraphicsPath CreateRoundedRectangle(Rectangle rectangle, int radius)
    {
        var path = new GraphicsPath();
        var diameter = radius * 2;
        var arc = new Rectangle(rectangle.X, rectangle.Y, diameter, diameter);
        path.AddArc(arc, 180, 90);
        arc.X = rectangle.Right - diameter;
        path.AddArc(arc, 270, 90);
        arc.Y = rectangle.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        arc.X = rectangle.X;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();
        return path;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var index = GetSliceIndex(e.Location);
        if (index == _hoveredSlice) return;
        _hoveredSlice = index;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left && _hoveredSlice >= 0)
            ExecuteAction(_hoveredSlice);
        else if (e.Button == MouseButtons.Right)
            Close();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Escape)
        {
            Close();
            e.Handled = true;
        }
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        Close();
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
        return Math.Clamp((int)(normalized / 60), 0, RadialActionCatalog.Labels.Count - 1);
    }

    private void ExecuteAction(int index)
    {
        try
        {
            _status = RadialActionExecutor.Execute(index, _target);
            Invalidate();
            _closeTimer.Start();
        }
        catch (Exception ex)
        {
            _status = ex.GetBaseException().Message;
            Invalidate();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _closeTimer.Dispose();
        base.Dispose(disposing);
    }
}
