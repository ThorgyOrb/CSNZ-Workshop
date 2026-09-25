namespace CSNZWorkshop;

public static class UiTheme
{
    public static bool Dark { get; set; } = true;
    public static Color Background => Dark ? Color.FromArgb(17, 22, 30) : Color.FromArgb(240, 244, 249);
    public static Color Surface => Dark ? Color.FromArgb(27, 35, 46) : Color.White;
    public static Color Foreground => Dark ? Color.FromArgb(228, 236, 246) : Color.FromArgb(30, 44, 63);
    public static Color Muted => Dark ? Color.FromArgb(150, 169, 190) : Color.FromArgb(89, 107, 129);
    public static Color Accent => Color.FromArgb(39, 184, 168);
    public static Color Border => Dark ? Color.FromArgb(49, 63, 79) : Color.FromArgb(210, 220, 232);
    public static Color Selection => Dark ? Color.FromArgb(36, 76, 84) : Color.FromArgb(211, 241, 237);
    public static void Primary(Button button) => button.AccessibleDescription = "primary-action";
    public static Control Header(string title, string subtitle)
    {
        var panel = new Panel { Dock = DockStyle.Fill, Padding = new(14, 4, 0, 4) };
        panel.Controls.Add(new Label { Text = subtitle, Dock = DockStyle.Bottom, Height = 25, AutoEllipsis = true, Tag = "muted" });
        panel.Controls.Add(new Label { Text = title, Dock = DockStyle.Top, Height = 35, Font = new("Segoe UI", 20, FontStyle.Bold), AutoEllipsis = true });
        panel.Paint += (_, e) => { using var brush = new SolidBrush(Accent); e.Graphics.FillRectangle(brush, 0, 8, 4, Math.Max(1, panel.Height - 16)); };
        return panel;
    }
    public static void Apply(Control control)
    {
        if (control is PictureBox) return;
        Color background = Background;
        Color surface = Surface;
        Color foreground = Foreground;
        control.BackColor = control is TextBox or ListView or ComboBox or ListBox ? surface : background;
        control.ForeColor = foreground;
        if (control.Tag as string == "muted") control.ForeColor = Muted;
        if (control is Button button)
        {
            bool primary = button.AccessibleDescription == "primary-action";
            button.FlatStyle = FlatStyle.Flat; button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.BorderColor = primary ? Accent : Border;
            button.BackColor = primary ? Accent : surface;
            button.ForeColor = primary ? Color.FromArgb(10, 33, 36) : foreground;
            button.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(70, 208, 190) : Selection;
            button.FlatAppearance.MouseDownBackColor = Selection;
            button.Paint -= PaintDisabledButton; button.Paint += PaintDisabledButton;
            if (button.Image is null && button.Tag is not int) button.Padding = new(7, 2, 7, 2);
        }
        if (control is TextBox text) text.BorderStyle = BorderStyle.FixedSingle;
        if (control is ComboBox combo) combo.FlatStyle = FlatStyle.Flat;
        if (control is ListBox list) { list.BorderStyle = BorderStyle.None; list.BackColor = background; }
        if (control is TabControl tabs)
        {
            tabs.DrawMode = TabDrawMode.OwnerDrawFixed; tabs.Padding = new Point(16, 6);
            tabs.DrawItem -= DrawTab; tabs.DrawItem += DrawTab;
        }
        if (control is DataGridView grid)
        {
            grid.BackgroundColor = surface; grid.EnableHeadersVisualStyles = false;
            grid.BorderStyle = BorderStyle.None; grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            grid.GridColor = Border; grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            grid.RowTemplate.Height = 34; grid.ColumnHeadersHeight = 38;
            grid.DefaultCellStyle.Padding = new Padding(7, 3, 7, 3);
            grid.DefaultCellStyle.SelectionBackColor = Selection; grid.DefaultCellStyle.SelectionForeColor = foreground;
            grid.AlternatingRowsDefaultCellStyle.BackColor = Dark ? Color.FromArgb(30, 39, 51) : Color.FromArgb(247, 250, 253);
            grid.DefaultCellStyle.BackColor = surface; grid.DefaultCellStyle.ForeColor = foreground;
            grid.ColumnHeadersDefaultCellStyle.BackColor = background; grid.ColumnHeadersDefaultCellStyle.ForeColor = foreground;
        }
        foreach (Control child in control.Controls) Apply(child);
        if (control is ResourceGallery gallery) gallery.RefreshSelection();
        control.Invalidate();
    }
    static void PaintDisabledButton(object? sender, PaintEventArgs e)
    {
        if (sender is not Button { Enabled: false, Image: null } button) return;
        using var brush = new SolidBrush(Surface);
        e.Graphics.FillRectangle(brush, button.ClientRectangle);
        using var pen = new Pen(Border);
        e.Graphics.DrawRectangle(pen, 0, 0, Math.Max(0, button.Width - 1), Math.Max(0, button.Height - 1));
        TextRenderer.DrawText(e.Graphics, button.Text, button.Font, button.ClientRectangle, Muted,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
    static void DrawTab(object? sender, DrawItemEventArgs e)
    {
        if (sender is not TabControl tabs || e.Index < 0) return;
        bool selected = tabs.SelectedIndex == e.Index;
        using var brush = new SolidBrush(selected ? Selection : Surface);
        e.Graphics.FillRectangle(brush, e.Bounds);
        TextRenderer.DrawText(e.Graphics, tabs.TabPages[e.Index].Text, tabs.Font, e.Bounds, selected ? Foreground : Muted,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        if (selected) { using var accent = new SolidBrush(Accent); e.Graphics.FillRectangle(accent, e.Bounds.X, e.Bounds.Bottom - 3, e.Bounds.Width, 3); }
    }
    public static void Attach(Form form) => form.Shown += (_, _) => Apply(form);
}
