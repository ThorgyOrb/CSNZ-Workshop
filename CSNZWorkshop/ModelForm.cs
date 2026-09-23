using CSNZWorkshop.Models;
using System.Diagnostics;

namespace CSNZWorkshop;

public sealed class ModelForm : Form
{
    readonly ModelBundle bundle;
    readonly PictureBox view = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(32, 40, 51), TabStop = true };
    readonly PictureBox textureView = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.DimGray };
    readonly ComboBox sequences = new() { Width = 220, DropDownStyle = ComboBoxStyle.DropDownList };
    readonly ComboBox skins = new() { Width = 90, DropDownStyle = ComboBoxStyle.DropDownList };
    readonly ComboBox body = new() { Width = 160, DropDownStyle = ComboBoxStyle.DropDownList };
    readonly ComboBox variant = new() { Width = 160, DropDownStyle = ComboBoxStyle.DropDownList };
    readonly ComboBox textures = new() { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList };
    readonly TrackBar frame = new() { Width = 220, Minimum = 0, Maximum = 0, TickStyle = TickStyle.None };
    readonly CheckBox wire = new() { Text = "Malla", AutoSize = true };
    readonly Button play = new() { Text = "Reproducir", AutoSize = true };
    readonly Button prepare = new() { Text = "Preparar copia para prueba", AutoSize = true };
    readonly Label status = new() { Dock = DockStyle.Bottom, Height = 42, AutoEllipsis = true };
    readonly System.Windows.Forms.Timer timer = new() { Interval = 50 };
    readonly int[] variants;
    readonly Stopwatch clock = new();
    float yaw = .55f, pitch = -.2f, zoom = 1;
    Point? drag;
    bool updating, preparing;
    CancellationTokenSource? preparation;

    public ModelForm(ModelBundle bundle)
    {
        this.bundle = bundle; variants = new int[bundle.Model.Bodies.Length];
        Text = "CSNZ Workshop — " + bundle.Main.Name;
        Size = new(1200, 850); MinimumSize = new(950, 650); StartPosition = FormStartPosition.CenterParent;
        Font = new("Segoe UI", 10);
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 100, Padding = new(8), AutoScroll = true };
        var reset = new Button { Text = "Centrar", AutoSize = true };
        toolbar.Controls.AddRange([sequences, play, frame, skins, wire, reset, body, variant, prepare]);
        var tabs = new TabControl { Dock = DockStyle.Fill };
        var modelTab = new TabPage("Modelo 3D"); modelTab.Controls.Add(view);
        var textureTab = new TabPage("Texturas"); textureTab.Controls.Add(textureView); textureTab.Controls.Add(textures);
        var details = new TextBox { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Both, Text = bundle.Describe() };
        var diagnostic = new TabPage("Diagnóstico"); diagnostic.Controls.Add(details);
        tabs.TabPages.AddRange([modelTab, textureTab, diagnostic]);
        Controls.Add(tabs); Controls.Add(toolbar); Controls.Add(status);
        updating = true;
        sequences.Items.Add("Pose de referencia");
        foreach (var sequence in bundle.Model.Sequences) sequences.Items.Add(sequence.Name);
        sequences.SelectedIndex = 0;
        for (int i = 0; i < Math.Max(1, bundle.Model.Skins.Length); i++) skins.Items.Add("Skin " + i);
        skins.SelectedIndex = 0;
        foreach (var part in bundle.Model.Bodies) body.Items.Add(part.Name);
        if (body.Items.Count > 0) body.SelectedIndex = 0;
        foreach (var texture in bundle.Model.Textures) textures.Items.Add($"{texture.Name} ({texture.Width}×{texture.Height})");
        if (textures.Items.Count > 0) textures.SelectedIndex = 0;
        updating = false;
        FillVariants();
        sequences.SelectedIndexChanged += (_, _) =>
        {
            timer.Stop(); play.Text = "Reproducir";
            frame.Maximum = sequences.SelectedIndex > 0 ? bundle.Model.Sequences[sequences.SelectedIndex - 1].Frames - 1 : 0;
            frame.Value = 0; DrawModel();
        };
        skins.SelectedIndexChanged += (_, _) => DrawModel(); wire.CheckedChanged += (_, _) => DrawModel(); frame.ValueChanged += (_, _) => DrawModel();
        body.SelectedIndexChanged += (_, _) => FillVariants();
        variant.SelectedIndexChanged += (_, _) => { if (!updating && body.SelectedIndex >= 0 && variant.SelectedIndex >= 0) { variants[body.SelectedIndex] = variant.SelectedIndex; DrawModel(); } };
        textures.SelectedIndexChanged += (_, _) => DrawTexture();
        view.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) { drag = e.Location; view.Capture = true; } view.Focus(); };
        view.MouseUp += (_, _) => { drag = null; view.Capture = false; };
        view.MouseMove += (_, e) =>
        {
            if (drag is Point previous) { yaw += (e.X - previous.X) * .01f; pitch = Math.Clamp(pitch + (e.Y - previous.Y) * .01f, -1.5f, 1.5f); drag = e.Location; DrawModel(); }
        };
        view.MouseWheel += (_, e) => { zoom = Math.Clamp(zoom * MathF.Pow(1.1f, e.Delta / 120f), .05f, 20); DrawModel(); };
        reset.Click += (_, _) => { yaw = .55f; pitch = -.2f; zoom = 1; DrawModel(); };
        play.Click += (_, _) =>
        {
            if (sequences.SelectedIndex <= 0) return;
            if (timer.Enabled) { timer.Stop(); play.Text = "Reproducir"; }
            else { frame.Value = 0; clock.Restart(); timer.Start(); play.Text = "Pausar"; }
        };
        timer.Tick += (_, _) =>
        {
            if (sequences.SelectedIndex <= 0) return;
            var sequence = bundle.Model.Sequences[sequences.SelectedIndex - 1];
            frame.Value = (int)(clock.Elapsed.TotalSeconds * sequence.Fps) % sequence.Frames;
        };
        prepare.Click += async (_, _) => await Prepare();
        Shown += (_, _) => { DrawModel(); DrawTexture(); };
        ResizeEnd += (_, _) => DrawModel();
        FormClosing += (_, _) => { timer.Stop(); preparation?.Cancel(); };
        FormClosed += (_, _) => { timer.Dispose(); view.Image?.Dispose(); textureView.Image?.Dispose(); };
    }

    void FillVariants()
    {
        updating = true; variant.Items.Clear();
        if (body.SelectedIndex >= 0)
        {
            var part = bundle.Model.Bodies[body.SelectedIndex];
            for (int i = 0; i < part.Variants.Length; i++) variant.Items.Add($"{i}: {part.Variants[i].Name}");
            if (variant.Items.Count > 0) variant.SelectedIndex = variants[body.SelectedIndex];
        }
        updating = false; DrawModel();
    }

    void DrawModel()
    {
        if (updating || IsDisposed) return;
        try
        {
            var bitmap = ModelRenderer.Render(bundle.Model, view.Width, view.Height,
                new(yaw, pitch, zoom, sequences.SelectedIndex - 1, frame.Value, skins.SelectedIndex, variants, wire.Checked));
            var old = view.Image; view.Image = bitmap; old?.Dispose();
            status.Text = $"Arrastra para girar · Rueda para zoom · Fotograma {frame.Value}/{frame.Maximum} · Materiales aproximados";
        }
        catch (Exception ex)
        {
            timer.Stop(); play.Text = "Reproducir";
            var old = view.Image; view.Image = null; old?.Dispose();
            status.Text = ex.Message;
        }
    }

    void DrawTexture()
    {
        var old = textureView.Image;
        textureView.Image = textures.SelectedIndex >= 0 ? ModelRenderer.TextureBitmap(bundle.Model.Textures[textures.SelectedIndex]) : null;
        old?.Dispose();
    }

    async Task Prepare()
    {
        if (preparing) return;
        using var dialog = new FolderBrowserDialog { Description = "Guardar una copia para probar en CS 1.6, con archivos compañeros e informe. No se instala en el juego.", UseDescriptionForTitle = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        preparing = true; prepare.Enabled = false; timer.Stop(); play.Text = "Reproducir";
        using var operation = new CancellationTokenSource(); preparation = operation;
        status.Text = "Validando animaciones y preparando la copia…";
        try
        {
            string output = await Task.Run(() => bundle.Prepare(dialog.SelectedPath, operation.Token));
            if (!IsDisposed) MessageBox.Show(this, "Copia preparada en:\r\n" + output + "\r\n\r\nConsulta preparacion.json. Falta probarla en CS 1.6.", "Preparación terminada");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!IsDisposed) MessageBox.Show(this, ex.Message, "No se pudo preparar"); }
        finally { preparing = false; preparation = null; if (!IsDisposed) { prepare.Enabled = true; DrawModel(); } }
    }
}
