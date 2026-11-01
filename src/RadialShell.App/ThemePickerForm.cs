using System.Drawing;
using System.Windows.Forms;

namespace RadialShell.App;

/// <summary>A compact, keyboard-accessible gallery for choosing and saving a radial theme.</summary>
internal sealed class ThemePickerForm : Form
{
    private readonly ToolTip _toolTips = new() { ShowAlways = true };
    private readonly List<Bitmap> _swatches = [];

    public ThemePickerForm(RadialTheme selectedTheme)
    {
        Text = "Choose a theme";
        ClientSize = new Size(380, 440);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowIcon = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Font;
        KeyPreview = true;
        AccessibleName = "RadialShell theme picker";

        var heading = new Label
        {
            Text = "Choose a theme",
            Dock = DockStyle.Top,
            Height = 48,
            Padding = new Padding(14, 0, 0, 0),
            TextAlign = ContentAlignment.MiddleLeft,
            UseMnemonic = false,
            AccessibleRole = AccessibleRole.StaticText
        };

        var gallery = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 4,
            Padding = new Padding(10),
            GrowStyle = TableLayoutPanelGrowStyle.FixedSize,
            AccessibleName = "Available themes"
        };
        gallery.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        gallery.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        for (var row = 0; row < 4; row++)
            gallery.RowStyles.Add(new RowStyle(SizeType.Percent, 25));

        for (var index = 0; index < RadialThemeCatalog.All.Count; index++)
        {
            var theme = RadialThemeCatalog.All[index];
            var isSelected = string.Equals(theme.Name, selectedTheme.Name, StringComparison.OrdinalIgnoreCase);
            var button = new Button
            {
                Text = isSelected ? $"✓  {theme.Name}" : theme.Name,
                AccessibleName = isSelected ? $"{theme.Name} theme, currently selected" : $"Select {theme.Name} theme",
                AccessibleDescription = $"Save and use the {theme.Name} radial menu theme.",
                Dock = DockStyle.Fill,
                Margin = new Padding(5),
                Padding = new Padding(8, 0, 6, 0),
                TextAlign = ContentAlignment.MiddleLeft,
                ImageAlign = ContentAlignment.MiddleRight,
                TextImageRelation = TextImageRelation.TextBeforeImage,
                FlatStyle = FlatStyle.Flat,
                UseVisualStyleBackColor = false,
                BackColor = theme.Window,
                ForeColor = theme.Text,
                Image = CreateSwatchImage(theme),
                TabIndex = index
            };
            button.FlatAppearance.BorderColor = isSelected ? theme.Accent : theme.Border;
            button.FlatAppearance.BorderSize = isSelected ? 2 : 1;
            _toolTips.SetToolTip(button, isSelected
                ? $"{theme.Name} is selected. Activate to keep this theme."
                : $"Select the {theme.Name} theme.");
            button.Click += (_, _) => SelectTheme(theme, button);
            gallery.Controls.Add(button, index % 2, index / 2);
        }

        Controls.Add(gallery);
        Controls.Add(heading);
        Shown += (_, _) =>
        {
            var selectedButton = gallery.Controls
                .OfType<Button>()
                .FirstOrDefault(button => button.AccessibleName?.Contains("currently selected", StringComparison.Ordinal) == true);
            selectedButton?.Focus();
        };
    }

    private Bitmap CreateSwatchImage(RadialTheme theme)
    {
        const int width = 68;
        const int height = 22;
        var bitmap = new Bitmap(width, height);
        var colors = new[] { theme.Window, theme.Sector, theme.SectorAlternate, theme.Hover, theme.Accent };
        using var graphics = Graphics.FromImage(bitmap);
        for (var index = 0; index < colors.Length; index++)
        {
            using var brush = new SolidBrush(colors[index]);
            graphics.FillRectangle(brush, index * 13 + 1, 2, 12, 18);
            using var border = new Pen(theme.Border);
            graphics.DrawRectangle(border, index * 13 + 1, 2, 12, 18);
        }
        _swatches.Add(bitmap);
        return bitmap;
    }

    private void SelectTheme(RadialTheme theme, Control control)
    {
        if (!RadialThemeSettings.Save(theme.Name, out var error))
        {
            var message = $"Could not save {theme.Name}: {error ?? "unknown settings error"}";
            RadialLogger.Error("theme.picker.save_failed", message);
            _toolTips.SetToolTip(control, message);
            _toolTips.Show(message, control, 5000);
            return;
        }

        RadialLogger.Info("theme.saved", $"Theme preference saved from tray picker: {theme.Name}");
        Close();
    }

    protected override bool ProcessCmdKey(ref Message message, Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            Close();
            return true;
        }
        return base.ProcessCmdKey(ref message, keyData);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _toolTips.Dispose();
            foreach (var swatch in _swatches)
                swatch.Dispose();
            _swatches.Clear();
        }
        base.Dispose(disposing);
    }
}
