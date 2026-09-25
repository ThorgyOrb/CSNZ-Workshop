using CSNZWorkshop.Archives;
using CSNZWorkshop.Models;
using CSNZWorkshop.Media;

namespace CSNZWorkshop;

public sealed class ResourceGallery : UserControl
{
    const int PageSize = 24;
    readonly FlowLayoutPanel cards = new() { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.FromArgb(240, 243, 247) };
    readonly Button previous = new() { Text = "← Anterior", AutoSize = true };
    readonly Button next = new() { Text = "Siguiente →", AutoSize = true };
    readonly Label pageLabel = new() { AutoSize = true, Padding = new(8) };
    readonly Dictionary<ResourceEntry, Bitmap> cache = [];
    readonly ToolTip tips = new();
    readonly SemaphoreSlim gate = new(1);
    CancellationTokenSource? pending;
    IReadOnlyList<ResourceEntry> entries = [], library = [];
    int page;
    int selectedIndex = -1;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string? CacheRoot { get; set; }
    ThumbnailStore? disk;
    public event Action<int>? ResourceSelected;

    public ResourceGallery()
    {
        Dock = DockStyle.Fill;
        var navigation = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 42 };
        navigation.Controls.AddRange([previous, pageLabel, next]);
        Controls.Add(cards); Controls.Add(navigation);
        previous.Click += (_, _) => { page--; ShowPage(); };
        next.Click += (_, _) => { page++; ShowPage(); };
    }

    public void SetResources(IReadOnlyList<ResourceEntry> resources, IReadOnlyList<ResourceEntry> all)
    {
        if (!ReferenceEquals(library, all))
        {
            foreach (var bitmap in cache.Values) bitmap.Dispose(); cache.Clear();
            try { disk = CacheRoot is null ? null : new ThumbnailStore(CacheRoot, all); } catch (IOException) { disk = null; } catch (UnauthorizedAccessException) { disk = null; }
        }
        entries = resources; library = all; page = 0; ShowPage();
    }

    public void SelectIndex(int index)
    {
        selectedIndex = index;
        foreach (Button card in cards.Controls)
        {
            bool selected = card.Tag is int row && row == index;
            card.BackColor = selected ? UiTheme.Selection : UiTheme.Surface;
            card.FlatAppearance.BorderColor = selected ? UiTheme.Accent : UiTheme.Border;
            card.FlatAppearance.BorderSize = selected ? 2 : 1;
        }
    }
    internal void RefreshSelection() => SelectIndex(selectedIndex);

    async void ShowPage()
    {
        pending?.Cancel();
        using var operation = new CancellationTokenSource(); pending = operation;
        var token = operation.Token;
        var thumbnailStore = disk; var indexedLibrary = library;
        foreach (Button control in cards.Controls.Cast<Button>().ToArray()) { control.Image?.Dispose(); control.Image = null; control.Dispose(); }
        cards.Controls.Clear();
        int pages = Math.Max(1, (entries.Count + PageSize - 1) / PageSize);
        previous.Enabled = page > 0; next.Enabled = page + 1 < pages;
        pageLabel.Text = entries.Count == 0 ? "Sin resultados" : $"Página {page + 1} de {pages}";
        var work = new List<(ResourceEntry Entry, Button Card)>();
        for (int i = page * PageSize; i < Math.Min(entries.Count, (page + 1) * PageSize); i++)
        {
            int selected = i; var entry = entries[i];
            var card = new Button { Width = 184, Height = 178, Margin = new(8), Padding = new(10), FlatStyle = FlatStyle.Flat,
                Tag = i, BackColor = i == selectedIndex ? Color.FromArgb(215, 234, 255) : Color.White,
                AutoEllipsis = true, TextAlign = ContentAlignment.BottomCenter, ImageAlign = ContentAlignment.TopCenter,
                Text = Path.GetFileName(entry.Ruta) + "\r\n" + entry.Extension.ToUpperInvariant() + " · " + ResourcePreview.FormatSize(entry.Bytes),
                AccessibleName = entry.Ruta + " — " + Path.GetFileName(entry.Paquete) };
            card.Click += (_, _) =>
            {
                SelectIndex(selected); ResourceSelected?.Invoke(selected);
            };
            tips.SetToolTip(card, entry.Ruta + "\r\n" + Path.GetFileName(entry.Paquete));
            cards.Controls.Add(card); work.Add((entry, card));
            UiTheme.Apply(card);
        }
        cards.BackColor = UiTheme.Background;
        SelectIndex(selectedIndex);
        try
        {
            await gate.WaitAsync(token);
            try
            {
                foreach (var (entry, card) in work)
                {
                    token.ThrowIfCancellationRequested();
                    if (entry.Extension is not (".mdl" or ".spr" or ".tga" or ".dds")) continue;
                    if (!cache.TryGetValue(entry, out var thumbnail))
                    {
                        Bitmap? image = null;
                        try
                        {
                            image = await Task.Run(() =>
                            {
                                var stored = thumbnailStore?.Read(entry); if (stored is not null) return stored;
                                if (entry.Extension is ".tga" or ".dds")
                                {
                                    if (entry.Bytes > 64 * 1024 * 1024) throw new InvalidDataException("Textura demasiado grande.");
                                    using var texture = TextureCodec.Decode(Extraction.ReadResource(entry, token), entry.Extension);
                                    float scale = Math.Min(152f / texture.Width, 98f / texture.Height);
                                    return new Bitmap(texture, Math.Max(1, (int)(texture.Width * scale)), Math.Max(1, (int)(texture.Height * scale)));
                                }
                                if (entry.Extension == ".spr")
                                {
                                    if (entry.Bytes > 64 * 1024 * 1024) throw new InvalidDataException("Sprite demasiado grande.");
                                    return SpriteClip.Parse(Extraction.ReadResource(entry, token), token).Render(0, 152, 98);
                                }
                                var bundle = ModelBundle.FromArchive(entry, indexedLibrary, token);
                                token.ThrowIfCancellationRequested();
                                return ModelRenderer.Render(bundle.Model, 152, 98, new());
                            }, token);
                            if (!token.IsCancellationRequested && thumbnailStore is not null) await Task.Run(() => thumbnailStore.Write(entry, image));
                            if (token.IsCancellationRequested || IsDisposed) { image.Dispose(); token.ThrowIfCancellationRequested(); return; }
                            // Keep a bounded in-memory cache; images on current cards are copies.
                            if (cache.Count >= 96) { var first = cache.First(); first.Value.Dispose(); cache.Remove(first.Key); }
                            cache[entry] = image; thumbnail = image;
                        }
                        catch (OperationCanceledException) { image?.Dispose(); throw; }
                        catch { if (!card.IsDisposed) card.AccessibleDescription = "Miniatura no disponible. Selecciona para consultar el diagnóstico."; continue; }
                    }
                    if (!card.IsDisposed) card.Image = new Bitmap(thumbnail);
                }
            }
            finally { gate.Release(); }
        }
        catch (OperationCanceledException) { }
        finally { if (ReferenceEquals(pending, operation)) pending = null; }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            pending?.Cancel();
            tips.Dispose();
            foreach (Button card in cards.Controls) { card.Image?.Dispose(); card.Image = null; }
            foreach (var image in cache.Values) image.Dispose(); cache.Clear();
        }
        base.Dispose(disposing);
    }
}
