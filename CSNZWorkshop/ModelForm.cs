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
    readonly CheckBox focusGeometry = new() { Text = "Enfocar geometría principal", AutoSize = true };
    readonly CheckBox hideEffects = new() { Text = "Ocultar efectos", AutoSize = true };
    readonly CheckBox firstPerson = new() { Text = "Primera persona (aprox.)", AutoSize = true };
    readonly NumericUpDown fov = new() { Minimum = 40, Maximum = 120, Value = 90, Width = 60 };
    readonly CheckBox bones = new() { Text = "Huesos (rayos X)", AutoSize = true };
    readonly ComboBox camera = new() { Width = 140, DropDownStyle = ComboBoxStyle.DropDownList };
    readonly ComboBox speed = new() { Width = 85, DropDownStyle = ComboBoxStyle.DropDownList };
    readonly ComboBox background = new() { Width = 135, DropDownStyle = ComboBoxStyle.DropDownList };
    readonly CheckBox nexonEffects = new() { Text = "Efectos Nexon (aprox.)", AutoSize = true, Checked = true };
    readonly CheckBox additiveTexture = new() { Text = "Mezcla aditiva de esta textura (solo vista previa)", Dock = DockStyle.Bottom, Height = 34 };
    readonly HashSet<int> additiveTextures = [];
    bool updatingTexture;
    readonly Button play = new() { Text = "Reproducir", AutoSize = true };
    readonly Button prepare = new() { Text = "Preparar copia para prueba", AutoSize = true };
    readonly Label status = new() { Dock = DockStyle.Bottom, Height = 42, AutoEllipsis = true };
    readonly System.Windows.Forms.Timer timer = new() { Interval = 50 };
    readonly int[] variants;
    readonly Stopwatch clock = new();
    float yaw = .55f, pitch = -.2f, zoom = 1;
    float panX, panY;
    double animationFrame;
    bool ticking, panning;
    Point? drag;
    bool updating;
    readonly HashSet<int> cyclingGroups = [];
    readonly CheckBox simulateEffects = new() { Text = "Simular variantes de efectos", AutoSize = true };
    readonly CheckBox cycleGroup = new() { Text = "Alternar este grupo", AutoSize = true };
    readonly NumericUpDown effectInterval = new() { Minimum = 50, Maximum = 5000, Increment = 50, Value = 900, Width = 70 };
    readonly ComboBox effectStyle = new() { Width = 155, DropDownStyle = ComboBoxStyle.DropDownList };
    double effectMilliseconds;


    public ModelForm(ModelBundle bundle)
    {
        this.bundle = bundle; variants = new int[bundle.Model.Bodies.Length];
        Text = "CSNZ Workshop — " + bundle.Main.Name;
        Size = new(1200, 850); MinimumSize = new(950, 650); StartPosition = FormStartPosition.CenterParent;
        Font = new("Segoe UI", 10);
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 235, Padding = new(8), AutoScroll = true };
        var reset = new Button { Text = "Centrar", AutoSize = true };
        var compare = new Button { Text = "Comparar con modelo…", AutoSize = true };
        compare.Click += async (_, _) => { timer.Stop(); play.Text = "Reproducir"; await ModelComparisonForm.Open(this, bundle); };
        toolbar.Controls.AddRange([sequences, play, frame, skins, wire, reset, body, variant, prepare]);
        toolbar.Controls.Add(compare);
        var exportUnity = new Button { Text = "Exportar para Unity…", AutoSize = true };
        exportUnity.Click += (_, _) => { Pause(); using var dialog = new UnityExportForm(bundle); dialog.ShowDialog(this); };
        toolbar.Controls.Add(exportUnity);
        toolbar.Controls.Add(nexonEffects);
        var previousFrame = new Button { Text = "◀ Fotograma", AutoSize = true };
        var nextFrame = new Button { Text = "Fotograma ▶", AutoSize = true };
        var capture = new Button { Text = "Guardar vista PNG…", AutoSize = true };
        camera.Items.AddRange(["Vista libre", "Frontal", "Trasera", "Lateral derecha", "Lateral izquierda", "Superior"]); camera.SelectedIndex = 0;
        speed.Items.AddRange(["0.25×", "0.5×", "1×", "2×"]); speed.SelectedIndex = 2;
        background.Items.AddRange(["Fondo oscuro", "Fondo gris", "Fondo claro"]); background.SelectedIndex = 0;
        toolbar.SetFlowBreak(nexonEffects, true);
        toolbar.Controls.AddRange([camera, bones, background, previousFrame, nextFrame, speed, capture]);
        toolbar.SetFlowBreak(capture, true);
        toolbar.Controls.AddRange([focusGeometry, hideEffects, firstPerson, new Label { Text = "FOV horizontal", AutoSize = true, Padding = new(0, 5, 0, 0) }, fov]);
        toolbar.SetFlowBreak(fov, true);
        var showEffects = new Button { Text = "Ver arma con efectos", AutoSize = true };
        effectStyle.Items.AddRange(["Desvanecido suave", "Alternancia directa"]); effectStyle.SelectedIndex = 0;
        toolbar.Controls.AddRange([showEffects, simulateEffects, cycleGroup, effectStyle, new Label { Text = "Duración (ms)", AutoSize = true }, effectInterval]);
        effectStyle.SelectedIndexChanged += (_, _) => { effectMilliseconds = 0; DrawModel(); };
        for (int i = 0; i < bundle.Model.Bodies.Length; i++) if (EffectPreview.Suggested(bundle.Model, bundle.Model.Bodies[i])) cyclingGroups.Add(i);
        simulateEffects.CheckedChanged += (_, _) => { effectMilliseconds = 0; FillVariants(); };
        cycleGroup.CheckedChanged += (_, _) =>
        {
            if (updating || body.SelectedIndex < 0) return;
            if (cycleGroup.Checked) cyclingGroups.Add(body.SelectedIndex); else cyclingGroups.Remove(body.SelectedIndex);
            effectMilliseconds = 0; FillVariants();
        };
        effectInterval.ValueChanged += (_, _) => { effectMilliseconds = 0; DrawModel(); };
        showEffects.Click += (_, _) =>
        {
            Pause(); hideEffects.Checked = false; nexonEffects.Checked = true; focusGeometry.Checked = true;
            simulateEffects.Checked = true; effectMilliseconds = 0;
            int idle = Array.FindIndex(bundle.Model.Sequences, s => s.Name.StartsWith("idle", StringComparison.OrdinalIgnoreCase));
            if (idle >= 0) sequences.SelectedIndex = idle + 1;
            else if (sequences.SelectedIndex == 0 && bundle.Model.Sequences.Length > 0) sequences.SelectedIndex = 1;
            animationFrame = frame.Value; clock.Restart(); timer.Start(); play.Text = "Pausar"; DrawModel();
        };
        focusGeometry.CheckedChanged += (_, _) => DrawModel(); hideEffects.CheckedChanged += (_, _) => DrawModel();
        fov.ValueChanged += (_, _) => DrawModel();
        firstPerson.CheckedChanged += (_, _) =>
        {
            yaw = firstPerson.Checked ? 0 : .55f; pitch = firstPerson.Checked ? 0 : -.2f;
            panX = panY = 0; zoom = 1; camera.SelectedIndex = 0; camera.Enabled = !firstPerson.Checked;
            focusGeometry.Enabled = !firstPerson.Checked; DrawModel();
        };
        bones.CheckedChanged += (_, _) => DrawModel(); background.SelectedIndexChanged += (_, _) => DrawModel();
        camera.SelectedIndexChanged += (_, _) =>
        {
            if (camera.SelectedIndex == 0) return;
            (yaw, pitch) = camera.SelectedIndex switch { 1 => (0f, 0f), 2 => (MathF.PI, 0f), 3 => (MathF.PI / 2, 0f), 4 => (-MathF.PI / 2, 0f), _ => (0f, MathF.PI / 2) };
            DrawModel();
        };
        previousFrame.Click += (_, _) => StepFrame(-1); nextFrame.Click += (_, _) => StepFrame(1);
        speed.SelectedIndexChanged += (_, _) => { animationFrame = frame.Value; clock.Restart(); };
        capture.Click += (_, _) =>
        {
            Pause();
            if (view.Image is not null) ImageExport.Save(this, view.Image, [bundle.Main.Origin.Split(" :: ")[0]], "vista-modelo.png");
        };
        nexonEffects.CheckedChanged += (_, _) => { DrawTexture(); DrawModel(); };
        var tabs = new TabControl { Dock = DockStyle.Fill };
        var modelTab = new TabPage("Modelo 3D"); modelTab.Controls.Add(view);
        var textureTab = new TabPage("Texturas"); textureTab.Controls.Add(textureView); textureTab.Controls.Add(textures);
        var saveTexture = new Button { Text = "Guardar textura PNG…", Dock = DockStyle.Bottom, Height = 36 };
        saveTexture.Click += (_, _) => { if (textureView.Image is not null) ImageExport.Save(this, textureView.Image, [bundle.Main.Origin.Split(" :: ")[0]]); };
        textureTab.Controls.Add(saveTexture);
        textureTab.Controls.Add(additiveTexture);
        additiveTexture.CheckedChanged += (_, _) =>
        {
            if (updatingTexture || textures.SelectedIndex < 0) return;
            if (additiveTexture.Checked) additiveTextures.Add(textures.SelectedIndex); else additiveTextures.Remove(textures.SelectedIndex);
            DrawModel();
        };
        var details = new TextBox { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Both, Text = bundle.Describe() };
        var diagnostic = new TabPage("Diagnóstico"); diagnostic.Controls.Add(details);
        tabs.TabPages.AddRange([modelTab, textureTab, diagnostic]);
        Controls.Add(tabs); Controls.Add(toolbar); Controls.Add(status);
        UiTheme.Attach(this);
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
            animationFrame = 0; effectMilliseconds = 0; frame.Value = 0; DrawModel();
        };
        skins.SelectedIndexChanged += (_, _) => DrawModel(); wire.CheckedChanged += (_, _) => DrawModel();
        frame.ValueChanged += (_, _) => { if (!ticking) { animationFrame = frame.Value; clock.Restart(); } if (!ticking) DrawModel(); };
        frame.MouseDown += (_, _) => Pause();
        body.SelectedIndexChanged += (_, _) => FillVariants();
        variant.SelectedIndexChanged += (_, _) => { if (!updating && body.SelectedIndex >= 0 && variant.SelectedIndex >= 0) { variants[body.SelectedIndex] = variant.SelectedIndex; DrawModel(); } };
        textures.SelectedIndexChanged += (_, _) => DrawTexture();
        view.MouseDown += (_, e) => { if (e.Button is MouseButtons.Left or MouseButtons.Right or MouseButtons.Middle) { panning = e.Button != MouseButtons.Left; drag = e.Location; view.Capture = true; } view.Focus(); };
        view.MouseUp += (_, _) => { drag = null; view.Capture = false; };
        view.MouseMove += (_, e) =>
        {
            if (drag is Point previous)
            {
                if (panning) { panX += (e.X - previous.X) / (float)Math.Max(1, view.Width); panY += (e.Y - previous.Y) / (float)Math.Max(1, view.Height); }
                else { camera.SelectedIndex = 0; yaw += (e.X - previous.X) * .01f; pitch = Math.Clamp(pitch + (e.Y - previous.Y) * .01f, -MathF.PI / 2, MathF.PI / 2); }
                drag = e.Location; DrawModel();
            }
        };
        view.MouseWheel += (_, e) => { zoom = Math.Clamp(zoom * MathF.Pow(1.1f, e.Delta / 120f), .05f, 20); DrawModel(); };
        reset.Click += (_, _) => { camera.SelectedIndex = 0; yaw = firstPerson.Checked ? 0 : .55f; pitch = firstPerson.Checked ? 0 : -.2f; zoom = 1; panX = panY = 0; DrawModel(); };
        play.Click += (_, _) =>
        {
            if (sequences.SelectedIndex <= 0 && !simulateEffects.Checked) return;
            if (timer.Enabled) { timer.Stop(); play.Text = "Reproducir"; }
            else { animationFrame = frame.Value; clock.Restart(); timer.Start(); play.Text = "Pausar"; }
        };
        timer.Tick += (_, _) =>
        {
            double rate = speed.SelectedIndex switch { 0 => .25, 1 => .5, 3 => 2, _ => 1 };
            double elapsed = clock.Elapsed.TotalSeconds * rate;
            if (simulateEffects.Checked) effectMilliseconds += elapsed * 1000;
            if (sequences.SelectedIndex > 0)
            {
                var sequence = bundle.Model.Sequences[sequences.SelectedIndex - 1];
                animationFrame = (animationFrame + elapsed * sequence.Fps) % sequence.Frames;
            }
            clock.Restart(); ticking = true;
            try { frame.Value = (int)animationFrame; } finally { ticking = false; }
            DrawModel();
        };
        prepare.Click += async (_, _) => await Prepare();
        Shown += (_, _) => { DrawModel(); DrawTexture(); };
        ResizeEnd += (_, _) => DrawModel();
        FormClosing += (_, _) => { timer.Stop(); };
        tabs.SelectedIndexChanged += (_, _) => { if (tabs.SelectedIndex != 0) Pause(); };
        FormClosed += (_, _) => { timer.Dispose(); view.Image?.Dispose(); textureView.Image?.Dispose(); };
    }

    void Pause() { timer.Stop(); play.Text = "Reproducir"; }
    void StepFrame(int direction)
    {
        Pause(); frame.Value = Math.Clamp(frame.Value + direction, 0, frame.Maximum);
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
        cycleGroup.Checked = body.SelectedIndex >= 0 && cyclingGroups.Contains(body.SelectedIndex); variant.Enabled = !(simulateEffects.Checked && cycleGroup.Checked); updating = false; DrawModel();
    }

    void DrawModel()
    {
        if (updating || IsDisposed) return;
        try
        {
            var bitmap = ModelRenderer.Render(bundle.Model, view.Width, view.Height,
                new(yaw, pitch, zoom, sequences.SelectedIndex - 1, frame.Value, skins.SelectedIndex, simulateEffects.Checked ? EffectPreview.Resolve(bundle.Model, variants, cyclingGroups, effectMilliseconds, (int)effectInterval.Value) : variants, wire.Checked, FramingGroups: simulateEffects.Checked ? cyclingGroups.ToArray() : null, FadingGroups: simulateEffects.Checked && effectStyle.SelectedIndex == 0 ? cyclingGroups.ToArray() : null, EffectIntensity: EffectPreview.Fade(effectMilliseconds, (int)effectInterval.Value), AdditiveTextures: additiveTextures.ToArray(), NexonEffects: nexonEffects.Checked, PanX: panX, PanY: panY, ShowBones: bones.Checked, FocusGeometry: focusGeometry.Checked, HideEffects: hideEffects.Checked, FirstPerson: firstPerson.Checked, FieldOfView: (float)fov.Value, Background: background.SelectedIndex switch { 1 => unchecked((int)0xff666666), 2 => unchecked((int)0xffe4e9ef), _ => unchecked((int)0xff202833) }));
            var old = view.Image; view.Image = bitmap; old?.Dispose();
            if (simulateEffects.Checked && body.SelectedIndex >= 0 && cyclingGroups.Contains(body.SelectedIndex))
            {
                updating = true;
                variant.SelectedIndex = EffectPreview.Resolve(bundle.Model, variants, cyclingGroups, effectMilliseconds, (int)effectInterval.Value)[body.SelectedIndex];
                updating = false;
            }
            status.Text = $"Izquierdo: girar · Derecho/central: desplazar · Rueda: zoom · Fotograma {frame.Value}/{frame.Maximum} · Materiales aproximados" + (simulateEffects.Checked ? $" · Simulación: {cyclingGroups.Count} grupos" : "");
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
        updatingTexture = true;
        bool declared = textures.SelectedIndex >= 0 && ((bundle.Model.Textures[textures.SelectedIndex].Flags & 0x20) != 0
            || nexonEffects.Checked && ModelRenderer.IsNexonEffect(bundle.Model.Textures[textures.SelectedIndex]));
        additiveTexture.Enabled = textures.SelectedIndex >= 0 && !declared;
        additiveTexture.Checked = declared || additiveTextures.Contains(textures.SelectedIndex);
        updatingTexture = false;
        var old = textureView.Image;
        textureView.Image = textures.SelectedIndex >= 0 ? ModelRenderer.TextureBitmap(bundle.Model.Textures[textures.SelectedIndex]) : null;
        old?.Dispose();
    }

    Task Prepare()
    {
        timer.Stop(); play.Text = "Reproducir";
        using var dialog = new PreparationForm(bundle);
        dialog.ShowDialog(this);
        return Task.CompletedTask;
    }
}
