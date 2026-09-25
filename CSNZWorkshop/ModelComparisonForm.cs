using CSNZWorkshop.Models;
using System.Numerics;

namespace CSNZWorkshop;

public sealed class ModelComparisonForm : Form
{
    readonly ModelBundle left, right;
    readonly PictureBox a = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom };
    readonly PictureBox b = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom };
    readonly ComboBox sa = new() { Width = 250, DropDownStyle = ComboBoxStyle.DropDownList };
    readonly ComboBox sb = new() { Width = 250, DropDownStyle = ComboBoxStyle.DropDownList };
    readonly TrackBar phase = new() { Width = 200, Maximum = 1000, TickStyle = TickStyle.None };
    readonly TrackBar angle = new() { Width = 160, Minimum = -180, Maximum = 180, Value = 32, TickStyle = TickStyle.None };
    readonly Label status = new() { Dock = DockStyle.Bottom, Height = 44 };
    readonly System.Windows.Forms.Timer timer = new() { Interval = 100 };
    readonly Button play = new() { Text = "Reproducir", AutoSize = true };
    (Vector3 Center, float Radius) framing;

    public ModelComparisonForm(ModelBundle left, ModelBundle right)
    {
        this.left = left; this.right = right;
        Text = "Comparar modelos — origen / destino"; Size = new(1350, 850); MinimumSize = new(1050, 650); Font = new("Segoe UI", 10);
        var tabs = new TabControl { Dock = DockStyle.Fill };
        var visual = new TabPage("Comparación visual");
        var split = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
        split.ColumnStyles.Add(new(SizeType.Percent, 50)); split.ColumnStyles.Add(new(SizeType.Percent, 50));
        split.RowStyles.Add(new(SizeType.Absolute, 35)); split.RowStyles.Add(new(SizeType.Percent, 100));
        split.Controls.Add(new Label { Text = "Origen: " + left.Main.Name, AutoSize = true }, 0, 0);
        split.Controls.Add(new Label { Text = "Destino: " + right.Main.Name, AutoSize = true }, 1, 0);
        split.Controls.Add(a, 0, 1); split.Controls.Add(b, 1, 1); visual.Controls.Add(split);
        var data = new TabPage("Secuencias, huesos y puntos de unión");
        var list = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true };
        list.Columns.Add("Dato / índice", 180); list.Columns.Add("Origen", 530); list.Columns.Add("Destino", 530);
        foreach (var row in ModelComparison.Rows(left, right)) list.Items.Add(new ListViewItem(row));
        data.Controls.Add(list); tabs.TabPages.AddRange([visual, data]);
        var controls = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 110, AutoScroll = true };
        controls.Controls.AddRange([sa, sb, new Label { Text = "Fase común", AutoSize = true }, phase, play, new Label { Text = "Giro", AutoSize = true }, angle]);
        Controls.Add(tabs); Controls.Add(controls); Controls.Add(status);
        foreach (var (box, model) in new[] { (sa, left.Model), (sb, right.Model) })
        {
            box.Items.Add("Pose de referencia");
            for (int i = 0; i < model.Sequences.Length; i++) box.Items.Add($"#{i}: {model.Sequences[i].Name}");
            box.SelectedIndex = 0; box.SelectedIndexChanged += (_, _) => FrameModels();
        }
        phase.ValueChanged += (_, _) => Draw(); angle.ValueChanged += (_, _) => Draw();
        play.Click += (_, _) => { timer.Enabled = !timer.Enabled; play.Text = timer.Enabled ? "Pausar" : "Reproducir"; };
        timer.Tick += (_, _) => phase.Value = (phase.Value + 20) % 1001;
        Shown += (_, _) => FrameModels(); ResizeEnd += (_, _) => Draw();
        FormClosing += (_, _) => timer.Stop(); UiTheme.Attach(this);
    }
    void FrameModels()
    {
        timer.Stop(); play.Text = "Reproducir";
        try { framing = ModelComparison.Frame(left.Model, right.Model, sa.SelectedIndex - 1, sb.SelectedIndex - 1); Draw(); }
        catch (Exception ex) { status.Text = ex.Message; }
    }
    void Draw()
    {
        try
        {
            int sizeX = Math.Max(64, Math.Min(a.Width, b.Width)), sizeY = Math.Max(64, Math.Min(a.Height, b.Height));
            foreach (var (view, model, sequence) in new[] { (a, left.Model, sa.SelectedIndex - 1), (b, right.Model, sb.SelectedIndex - 1) })
            {
                int frame = sequence >= 0 ? (int)((model.Sequences[sequence].Frames - 1) * phase.Value / 1000d) : 0;
                var image = ModelRenderer.Render(model, sizeX, sizeY, new(Yaw: angle.Value * MathF.PI / 180, Sequence: sequence, Frame: frame, SharedCenter: framing.Center, SharedRadius: framing.Radius));
                var old = view.Image; view.Image = image; old?.Dispose();
            }
            status.Text = "Misma escala y origen · Primera variante/skin/blend · Reproducción por fase normalizada (ciclo de 5 s, no FPS originales).";
        }
        catch (Exception ex) { timer.Stop(); play.Text = "Reproducir"; status.Text = ex.Message; }
    }
    public static async Task Open(IWin32Window owner, ModelBundle source)
    {
        using var file = new OpenFileDialog { Filter = "Modelo destino de CS 1.6|*.mdl", Title = "Seleccionar modelo para comparar" };
        if (file.ShowDialog(owner) != DialogResult.OK) return;
        try
        {
            var target = await Task.Run(() => ModelBundle.FromFile(file.FileName));
            if (owner is Control control && control.IsDisposed) return;
            using var form = new ModelComparisonForm(source, target); form.ShowDialog(owner);
        }
        catch (Exception ex) { if (owner is not Control control || !control.IsDisposed) MessageBox.Show(owner, ex.Message, "Comparación"); }
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing && !IsDisposed) { timer.Dispose(); a.Image?.Dispose(); b.Image?.Dispose(); }
        base.Dispose(disposing);
    }
}
