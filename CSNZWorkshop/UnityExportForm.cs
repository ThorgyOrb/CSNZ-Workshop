using CSNZWorkshop.Models;
using System.Diagnostics;

namespace CSNZWorkshop;

public sealed class UnityExportForm : Form
{
    readonly ModelBundle bundle;
    readonly GeometrySection[] sections;
    readonly CheckedListBox excluded = new() { Dock = DockStyle.Fill, CheckOnClick = true, HorizontalScrollbar = true };
    readonly PictureBox preview = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(32, 40, 51) };
    readonly ComboBox sequence = new() { Width = 180, DropDownStyle = ComboBoxStyle.DropDownList };
    readonly TrackBar frame = new() { Width = 210, Minimum = 0, Maximum = 0, TickStyle = TickStyle.None };
    readonly ComboBox body = new() { Width = 160, DropDownStyle = ComboBoxStyle.DropDownList };
    readonly ComboBox variant = new() { Width = 160, DropDownStyle = ComboBoxStyle.DropDownList };
    readonly ComboBox skin = new() { Width = 85, DropDownStyle = ComboBoxStyle.DropDownList };
    readonly NumericUpDown scale = new() { DecimalPlaces = 5, Minimum = .00001m, Maximum = 100, Value = .0254m, Increment = .001m, Width = 110 };
    readonly Button suggest = new() { Text = "Solo arma (sugerido)", AutoSize = true };
    readonly Button complete = new() { Text = "Modelo completo", AutoSize = true };
    readonly Button play = new() { Text = "Reproducir", AutoSize = true };
    readonly Button save = new() { Text = "Elegir destino y exportar…", AutoSize = true };
    readonly Button cancel = new() { Text = "Cerrar", AutoSize = true };
    readonly Label status = new() { Dock = DockStyle.Fill, AutoEllipsis = true };
    readonly System.Windows.Forms.Timer timer = new() { Interval = 100 };
    readonly Stopwatch clock = new();
    readonly int[] variants;
    readonly Panel selectionPanel = new() { Dock = DockStyle.Fill };
    readonly FlowLayoutPanel previewControls = new() { Dock = DockStyle.Bottom, Height = 120, AutoScroll = true };
    StudioModel filtered;
    CancellationTokenSource? operation;
    bool updating, refreshQueued;
    float yaw = .55f, pitch = -.2f, zoom = 1;
    Point? drag;
    int playStart;

    public UnityExportForm(ModelBundle bundle)
    {
        this.bundle = bundle; filtered = bundle.Model; sections = WeaponGeometry.Sections(bundle.Model);
        variants = new int[bundle.Model.Bodies.Length]; updating = true;
        Text = "Exportar para Unity — selección de geometría"; Size = new(1150, 760); MinimumSize = new(950, 650);
        StartPosition = FormStartPosition.CenterParent; Font = new("Segoe UI", 10);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new(16), RowCount = 5, ColumnCount = 1 };
        layout.RowStyles.Add(new(SizeType.Absolute, 65)); layout.RowStyles.Add(new(SizeType.Percent, 100));
        layout.RowStyles.Add(new(SizeType.Absolute, 45)); layout.RowStyles.Add(new(SizeType.Absolute, 50)); layout.RowStyles.Add(new(SizeType.Absolute, 45));
        layout.Controls.Add(new Label { Dock = DockStyle.Fill, Text = "Marca la geometría que quieres quitar. «Solo arma» sugiere brazos/manos; revisa todas las variantes.\r\nSe conservan todos los huesos, clips y attachments. La selección no modifica el MDL original." }, 0, 0);
        var split = new SplitContainer { Dock = DockStyle.Fill, Size = new(1050, 500), SplitterDistance = 430 };
        var choices = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 65, AutoScroll = true }; choices.Controls.AddRange([suggest, complete]);
        selectionPanel.Controls.Add(excluded); selectionPanel.Controls.Add(choices); split.Panel1.Controls.Add(selectionPanel);
        split.Panel2.Controls.Add(preview); split.Panel2.Controls.Add(previewControls);
        previewControls.Controls.AddRange([sequence, play, frame, skin, body, variant]); layout.Controls.Add(split, 0, 1);
        var options = new FlowLayoutPanel { Dock = DockStyle.Fill }; options.Controls.AddRange([new Label { Text = "Metros por unidad", AutoSize = true }, scale]); layout.Controls.Add(options, 0, 2);
        layout.Controls.Add(status, 0, 3);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill }; actions.Controls.AddRange([save, cancel]); layout.Controls.Add(actions, 0, 4);
        Controls.Add(layout); UiTheme.Attach(this);
        foreach (var section in sections) excluded.Items.Add($"{section.Label} · {section.Triangles} triángulos");
        sequence.Items.Add("Pose de referencia"); foreach (var seq in bundle.Model.Sequences) sequence.Items.Add(seq.Name); sequence.SelectedIndex = 0;
        for (int i = 0; i < Math.Max(1, bundle.Model.Skins.Length); i++) skin.Items.Add("Skin " + i); skin.SelectedIndex = 0;
        foreach (var group in bundle.Model.Bodies) body.Items.Add(group.Name); if (body.Items.Count > 0) body.SelectedIndex = 0;
        updating = false; FillVariants();
        suggest.Click += (_, _) => SetSelection(true); complete.Click += (_, _) => SetSelection(false);
        excluded.ItemCheck += (_, _) =>
        {
            if (updating || refreshQueued || !IsHandleCreated) return;
            refreshQueued = true;
            BeginInvoke(() => { refreshQueued = false; if (!IsDisposed) RefreshGeometry(); });
        };
        sequence.SelectedIndexChanged += (_, _) =>
        {
            Pause(); updating = true;
            frame.Maximum = sequence.SelectedIndex > 0 ? bundle.Model.Sequences[sequence.SelectedIndex - 1].Frames - 1 : 0;
            frame.Value = 0; updating = false; DrawPreview();
        };
        frame.ValueChanged += (_, _) => { if (!updating) { if (timer.Enabled) { playStart = frame.Value; clock.Restart(); } DrawPreview(); } };
        skin.SelectedIndexChanged += (_, _) => DrawPreview(); body.SelectedIndexChanged += (_, _) => FillVariants();
        variant.SelectedIndexChanged += (_, _) => { if (!updating && body.SelectedIndex >= 0 && variant.SelectedIndex >= 0) { variants[body.SelectedIndex] = variant.SelectedIndex; DrawPreview(); } };
        play.Click += (_, _) =>
        {
            if (timer.Enabled) { Pause(); return; }
            if (sequence.SelectedIndex <= 0 || bundle.Model.Sequences[sequence.SelectedIndex - 1].Fps == 0) return;
            playStart = frame.Value; clock.Restart(); timer.Start(); play.Text = "Pausar";
        };
        timer.Tick += (_, _) =>
        {
            var seq = bundle.Model.Sequences[sequence.SelectedIndex - 1];
            updating = true; frame.Value = (playStart + (int)(clock.Elapsed.TotalSeconds * seq.Fps)) % seq.Frames; updating = false; DrawPreview();
        };
        preview.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) { drag = e.Location; preview.Capture = true; } };
        preview.MouseUp += (_, _) => { drag = null; preview.Capture = false; };
        preview.MouseMove += (_, e) => { if (drag is Point previous) { yaw += (e.X - previous.X) * .01f; pitch = Math.Clamp(pitch + (e.Y - previous.Y) * .01f, -MathF.PI / 2, MathF.PI / 2); drag = e.Location; DrawPreview(); } };
        preview.MouseWheel += (_, e) => { zoom = Math.Clamp(zoom * (e.Delta > 0 ? 1.12f : .89f), .05f, 20); DrawPreview(); };
        save.Click += async (_, _) => await Save();
        cancel.Click += (_, _) => { if (operation is null) Close(); else operation.Cancel(); };
        Shown += (_, _) => RefreshGeometry(); ResizeEnd += (_, _) => DrawPreview();
        FormClosing += (_, e) => { if (operation is not null) { operation.Cancel(); e.Cancel = true; } else Pause(); };
        FormClosed += (_, _) => { timer.Dispose(); preview.Image?.Dispose(); };
        RefreshGeometry();
    }
    GeometrySectionKey[] Selection() => excluded.CheckedIndices.Cast<int>().Select(i => sections[i].Key).ToArray();
    void Pause() { timer.Stop(); play.Text = "Reproducir"; }
    void SetSelection(bool suggested)
    {
        Pause(); updating = true;
        for (int i = 0; i < sections.Length; i++) excluded.SetItemChecked(i, suggested && sections[i].SuggestedExclude);
        updating = false; RefreshGeometry();
        if (suggested && !sections.Any(s => s.SuggestedExclude)) status.Text = "No se identificaron brazos/manos por nombre. Selecciona las partes manualmente y revisa la vista previa.";
    }
    void FillVariants()
    {
        updating = true; variant.Items.Clear();
        if (body.SelectedIndex >= 0)
        {
            var group = bundle.Model.Bodies[body.SelectedIndex];
            for (int i = 0; i < group.Variants.Length; i++) variant.Items.Add($"{i}: {group.Variants[i].Name}");
            if (variant.Items.Count > 0) variant.SelectedIndex = variants[body.SelectedIndex];
        }
        updating = false; DrawPreview();
    }
    void RefreshGeometry()
    {
        filtered = WeaponGeometry.Apply(bundle.Model, Selection());
        int retained = filtered.Bodies.Sum(b => b.Variants.Sum(v => v.Triangles.Length)), total = sections.Sum(s => s.Triangles);
        save.Enabled = operation is null && bundle.Missing.Count == 0 && retained > 0;
        status.Text = bundle.Missing.Count > 0 ? "Faltan dependencias: " + string.Join(", ", bundle.Missing)
            : $"Se conservan {retained}/{total} triángulos (todas las variantes), {filtered.Bones.Length} huesos y {filtered.Sequences.Length} secuencias.\r\nLa integración de ataques y brazos del jugador en 7 Days to Die se prepara después.";
        if (retained == 0) status.Text = "No queda geometría para exportar. Desmarca alguna parte del arma.";
        DrawPreview();
    }
    void DrawPreview()
    {
        if (updating || IsDisposed) return;
        try
        {
            var image = ModelRenderer.Render(filtered, preview.Width, preview.Height,
                new(yaw, pitch, zoom, Sequence: sequence.SelectedIndex - 1, Frame: frame.Value, Skin: Math.Max(0, skin.SelectedIndex), Variants: variants, FocusGeometry: true));
            var previous = preview.Image; preview.Image = image; previous?.Dispose();
        }
        catch (Exception error) { Pause(); status.Text = "No se pudo previsualizar: " + error.Message; }
    }
    async Task Save()
    {
        Pause(); using var dialog = new FolderBrowserDialog { Description = "Exportar paquete para Unity", UseDescriptionForTitle = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        using var pending = new CancellationTokenSource(); operation = pending;
        float meters = (float)scale.Value; var selection = Selection();
        save.Enabled = scale.Enabled = selectionPanel.Enabled = previewControls.Enabled = false; cancel.Text = "Cancelar exportación";
        status.Text = "Exportando la geometría seleccionada, texturas y animaciones…";
        try
        {
            string folder = await Task.Run(() => UnityModelExporter.Export(bundle, dialog.SelectedPath, meters, pending.Token, selection));
            MessageBox.Show(this, "Paquete guardado en:\r\n" + folder + "\r\n\r\nEn Unity: Tools > CSNZ Workshop > Import model.json", "Exportación terminada");
        }
        catch (OperationCanceledException) { MessageBox.Show(this, "Exportación cancelada.", "Unity"); }
        catch (Exception error) { MessageBox.Show(this, error.Message, "No se pudo exportar"); }
        finally { operation = null; scale.Enabled = selectionPanel.Enabled = previewControls.Enabled = true; cancel.Text = "Cerrar"; RefreshGeometry(); }
    }
}
