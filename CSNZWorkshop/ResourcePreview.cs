using CSNZWorkshop.Archives;
using CSNZWorkshop.Models;
using CSNZWorkshop.Media;

namespace CSNZWorkshop;

// Each panel owns its image. Loads are serialized and outdated results are discarded.
public sealed class ResourcePreview : UserControl
{
    readonly PictureBox picture = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(32, 40, 51) };
    readonly Label title = new() { Dock = DockStyle.Fill, AutoEllipsis = true, Text = "Selecciona un recurso" };
    readonly TextBox info = new() { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical };
    readonly Button prepare = new() { Text = "Preparar para mi mod…", AutoSize = true, Enabled = false };
    readonly Button analyze = new() { Text = "Relacionados y CS 1.6…", AutoSize = true, Enabled = false };
    readonly Button viewModel = new() { Text = "Ver modelo 3D", AutoSize = true, Enabled = false };
    readonly Button compare = new() { Text = "Comparar con modelo…", AutoSize = true, Enabled = false };
    public event Action<ModelBundle, ResourceEntry>? AnalysisRequested;
    readonly System.Windows.Forms.Timer delay = new() { Interval = 220 };
    readonly System.Windows.Forms.Timer playback = new() { Interval = 100 };
    readonly Button play = new() { Text = "Reproducir", AutoSize = true, Enabled = false };
    readonly Button stop = new() { Text = "Detener", AutoSize = true, Enabled = false };
    readonly TrackBar frame = new() { Width = 150, TickStyle = TickStyle.None, Visible = false };
    readonly Label frameLabel = new() { AutoSize = true, Padding = new(0, 8, 0, 0) };
    readonly ComboBox wadTextures = new() { Width = 220, DropDownStyle = ComboBoxStyle.DropDownList, Visible = false };
    readonly Button saveTexture = new() { Text = "Guardar textura PNG…", AutoSize = true, Enabled = false, Visible = false };
    WadArchive? wad;
    int textureRevision;
    readonly SemaphoreSlim gate = new(1);
    CancellationTokenSource? pending;
    ResourceEntry? entry;
    IReadOnlyList<ResourceEntry> library = [];
    ModelBundle? bundle;
    SpriteClip? sprite;
    AudioPreview? audio;

    public ResourcePreview(bool allowAnalysis = true)
    {
        Dock = DockStyle.Fill;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new(10) };
        layout.RowStyles.Add(new(SizeType.Absolute, 54)); layout.RowStyles.Add(new(SizeType.Percent, 100));
        layout.RowStyles.Add(new(SizeType.Absolute, 76));
        layout.RowStyles.Add(new(SizeType.Absolute, 155)); layout.RowStyles.Add(new(SizeType.Absolute, 80));
        layout.Controls.Add(title, 0, 0); layout.Controls.Add(picture, 0, 1);
        var media = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true };
        media.Controls.AddRange([play, stop, frame, frameLabel, wadTextures, saveTexture]); layout.Controls.Add(media, 0, 2);
        wadTextures.SelectedIndexChanged += async (_, _) => await DrawWad();
        saveTexture.Click += (_, _) => { if (picture.Image is not null && entry is not null) ImageExport.Save(this, picture.Image, [entry.Paquete]); };
        var modelActions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true };
        analyze.Visible = allowAnalysis;
        modelActions.Controls.AddRange([viewModel, prepare, analyze, compare]);
        compare.Click += async (_, _) => { if (bundle is not null) await ModelComparisonForm.Open(this, bundle); };
        layout.Controls.Add(info, 0, 3); layout.Controls.Add(modelActions, 0, 4); Controls.Add(layout);
        analyze.Click += (_, _) => { if (bundle is not null && entry is not null) AnalysisRequested?.Invoke(bundle, entry); };
        viewModel.Click += (_, _) => OpenModelViewer();
        delay.Tick += async (_, _) => { delay.Stop(); await LoadPreview(); };
        prepare.Click += (_, _) => { if (bundle is not null) { using var dialog = new PreparationForm(bundle); dialog.ShowDialog(this); } };
        play.Click += (_, _) => MediaAction(() =>
        {
            if (sprite is not null) { playback.Interval = sprite.Frames[frame.Value].Milliseconds; playback.Start(); }
            else if (audio is not null) { audio.Play(); playback.Interval = 200; playback.Start(); }
            play.Enabled = false; stop.Enabled = true;
        });
        stop.Click += (_, _) => MediaAction(StopPlayback);
        frame.ValueChanged += (_, _) => MediaAction(DrawSprite);
        playback.Tick += (_, _) => MediaAction(() =>
        {
            if (sprite is not null) frame.Value = (frame.Value + 1) % sprite.Frames.Length;
            else if (audio is not null && !audio.IsPlaying) StopPlayback();
        });
    }

    public void OpenModelViewer()
    {
        if (bundle is null) return;
        using var viewer = new ModelForm(bundle);
        viewer.ShowDialog(this);
    }

    void MediaAction(Action action)
    {
        try { action(); }
        catch (Exception ex) { playback.Stop(); audio?.Dispose(); audio = null; play.Enabled = stop.Enabled = false; info.Text = ex.Message; }
    }

    void StopPlayback()
    {
        playback.Stop(); audio?.Stop();
        if (sprite is not null) frame.Value = 0;
        play.Enabled = audio is not null || sprite?.Frames.Length > 1; stop.Enabled = false;
    }

    void DrawSprite()
    {
        if (sprite is null) return;
        var bitmap = sprite.Render(frame.Value); var old = picture.Image; picture.Image = bitmap; old?.Dispose();
        frameLabel.Text = $"{frame.Value + 1}/{sprite.Frames.Length}";
        playback.Interval = sprite.Frames[frame.Value].Milliseconds;
    }

    void ClearMedia()
    {
        textureRevision++; wad = null; wadTextures.Items.Clear(); wadTextures.Visible = false; saveTexture.Enabled = saveTexture.Visible = false;
        playback.Stop(); audio?.Dispose(); audio = null; sprite = null;
        play.Enabled = stop.Enabled = false; frame.Visible = false; frame.Value = 0; frameLabel.Text = "";
        play.Visible = stop.Visible = false;
    }

    async Task DrawWad()
    {
        var current = wad; int selected = wadTextures.SelectedIndex, revision = ++textureRevision;
        if (current is null || selected < 0) return;
        saveTexture.Enabled = false;
        var previous = picture.Image; picture.Image = null; previous?.Dispose();
        try
        {
            Bitmap bitmap;
            await gate.WaitAsync();
            try
            {
                if (IsDisposed || revision != textureRevision) return;
                bitmap = await Task.Run(() => current.Render(selected));
            }
            finally { gate.Release(); }
            if (IsDisposed || revision != textureRevision) { bitmap.Dispose(); return; }
            picture.Image = bitmap; saveTexture.Enabled = true;
            info.Text = $"{current.Textures[selected].Name} · {bitmap.Width} × {bitmap.Height}";
        }
        catch (Exception ex) { if (!IsDisposed && revision == textureRevision) info.Text = ex.Message; }
    }

    public static string FormatSize(long bytes) => bytes < 1024 ? $"{bytes} B" : bytes < 1048576 ? $"{bytes / 1024d:N1} KB" : $"{bytes / 1048576d:N1} MB";

    public void SelectResource(ResourceEntry? selected, IReadOnlyList<ResourceEntry> resources)
    {
        if (entry == selected) return;
        delay.Stop(); pending?.Cancel(); entry = selected; library = resources; bundle = null; prepare.Enabled = analyze.Enabled = viewModel.Enabled = compare.Enabled = false;
        ClearMedia();
        var old = picture.Image; picture.Image = null; old?.Dispose();
        title.Text = selected is null ? "Selecciona un recurso" : Path.GetFileName(selected.Ruta) + "\r\n" + FormatSize(selected.Bytes) + " · " + Path.GetFileName(selected.Paquete);
        info.Text = selected is null ? "Busca un recurso o elige una categoría." : "Cargando vista previa…";
        if (selected is not null) delay.Start();
    }

    async Task LoadPreview()
    {
        var selected = entry;
        if (selected is null) return;
        using var operation = new CancellationTokenSource(); pending = operation;
        var token = operation.Token;
        Bitmap? bitmap = null;
        try
        {
            await gate.WaitAsync(token);
            ModelBundle? loaded = null;
            SpriteClip? loadedSprite = null;
            byte[]? sound = null;
            WadArchive? loadedWad = null;
            try
            {
                bitmap = await Task.Run(() =>
                {
                    token.ThrowIfCancellationRequested();
                    if (selected.Extension == ".mdl")
                    {
                        loaded = ModelBundle.FromArchive(selected, library, token);
                        token.ThrowIfCancellationRequested();
                        return ModelRenderer.Render(loaded.Model, 480, 360, new());
                    }
                    if (selected.Extension == ".bmp" && selected.Bytes <= 16 * 1024 * 1024)
                    {
                        using var stream = new MemoryStream(Extraction.ReadResource(selected, token));
                        using var source = Image.FromStream(stream);
                        TextureCodec.Dimensions(source.Width, source.Height);
                        return new Bitmap(source);
                    }
                    if (selected.Extension is ".tga" or ".dds" or ".wad")
                    {
                        if (selected.Bytes > 64 * 1024 * 1024) throw new InvalidDataException("Textura demasiado grande para la vista previa (64 MB).");
                        byte[] data = Extraction.ReadResource(selected, token);
                        if (selected.Extension == ".wad") { loadedWad = new WadArchive(data); return null; }
                        return TextureCodec.Decode(data, selected.Extension);
                    }
                    if (selected.Extension is ".spr" or ".wav" or ".mp3")
                    {
                        if (selected.Bytes > 64 * 1024 * 1024) throw new InvalidDataException("Recurso demasiado grande para la vista previa (64 MB).");
                        var data = Extraction.ReadResource(selected, token);
                        if (selected.Extension == ".spr") { loadedSprite = SpriteClip.Parse(data, token); return loadedSprite.Render(0); }
                        sound = selected.Extension == ".mp3" ? AudioPreview.DecodeMp3(data, token) : data;
                    }
                    return null;
                }, token);
            }
            finally { gate.Release(); }
            if (IsDisposed || token.IsCancellationRequested || entry != selected) return;
            picture.Image = bitmap; bitmap = null; bundle = loaded;
            if (loadedWad is not null)
            {
                wad = loadedWad; wadTextures.Items.AddRange(wad.Textures.Cast<object>().ToArray());
                wadTextures.Visible = saveTexture.Visible = true;
                info.Text = $"WAD3 · {wad.Textures.Count} entradas. Elige una textura.";
                if (wad.Textures.Count > 0) wadTextures.SelectedIndex = 0;
                return;
            }
            if (loadedSprite is not null)
            {
                play.Visible = stop.Visible = true;
                sprite = loadedSprite; frame.Maximum = sprite.Frames.Length - 1; frame.Visible = true;
                frameLabel.Text = $"1/{sprite.Frames.Length}"; play.Enabled = sprite.Frames.Length > 1;
                info.Text = $"Sprite IDSP v2 · {sprite.Frames.Length} fotogramas · {sprite.Bounds.Width} × {sprite.Bounds.Height}\r\n"
                    + "Reproduce o elige un fotograma. Fotogramas simples: 10 FPS; grupos: intervalos del archivo.\r\nLa mezcla aditiva es aproximada.";
                return;
            }
            if (sound is not null)
            {
                play.Visible = stop.Visible = true;
                audio = new AudioPreview(sound, ".wav"); play.Enabled = true;
                info.Text = $"Sonido {selected.Extension.ToUpperInvariant()} · {audio.DurationMilliseconds / 1000d:N1} s\r\nPulsa Reproducir para escucharlo. Se detiene al cambiar de recurso.";
                return;
            }
            prepare.Enabled = loaded is not null && loaded.Missing.Count == 0;
            analyze.Enabled = loaded is not null;
            viewModel.Enabled = loaded is not null;
            compare.Enabled = loaded is not null;
            saveTexture.Enabled = saveTexture.Visible = picture.Image is not null && selected.Extension is ".bmp" or ".tga" or ".dds";
            info.Text = loaded is null ? (picture.Image is null
                ? "Vista previa no disponible para este formato. Puedes extraerlo con los botones de la biblioteca."
                : $"Textura {selected.Extension.ToUpperInvariant()} · {picture.Image.Width} × {picture.Image.Height}. Puedes guardar PNG o extraer el original.")
                : (loaded.Missing.Count > 0 ? "Faltan archivos compañeros" : "Listo para preparar; pendiente de prueba en CS 1.6") + "\r\n\r\n" + loaded.Describe();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!IsDisposed && !token.IsCancellationRequested) info.Text = "No se pudo previsualizar: " + ex.Message; }
        finally { bitmap?.Dispose(); if (ReferenceEquals(pending, operation)) pending = null; }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { ClearMedia(); playback.Dispose(); delay.Dispose(); pending?.Cancel(); picture.Image?.Dispose(); picture.Image = null; }
        base.Dispose(disposing);
    }
}
