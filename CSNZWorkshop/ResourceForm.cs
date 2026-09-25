using CSNZWorkshop.Archives;
using CSNZWorkshop.Models;

namespace CSNZWorkshop;

public sealed class ResourceForm : Form
{
    readonly string[] paths;
    readonly ComboBox packageFilter = new() { Width = 220, DropDownStyle = ComboBoxStyle.DropDownList };
    readonly NumericUpDown minSize = new() { Width = 72, Maximum = 512, DecimalPlaces = 2 };
    readonly NumericUpDown maxSize = new() { Width = 72, Maximum = 512, DecimalPlaces = 2 };
    readonly string? initialPackage;
    readonly CheckBox packageOnly = new() { Text = "Solo paquete abierto", AutoSize = true };
    readonly SessionStore? session;
    readonly Button refresh = new() { Text = "Actualizar índices", AutoSize = true, Enabled = false };
    readonly Button favorite = new() { Text = "Alternar favoritos", AutoSize = true, Enabled = false };
    readonly CheckBox favoritesOnly = new() { Text = "Solo favoritos", AutoSize = true };
    string cacheSummary = "";
    readonly ResourcePreview preview = new();
    readonly ResourceGallery gallery = new();
    readonly TabControl views = new() { Dock = DockStyle.Fill };
    readonly CheckBox technical = new() { Text = "Detalles técnicos", AutoSize = true };
    readonly ResourceCollection collection = new();
    readonly Button addCollection = new() { Text = "Agregar a colección", AutoSize = true, Enabled = false };
    readonly Button manageCollection = new() { Text = "Colección (0)…", AutoSize = true, Enabled = false };
    CancellationTokenSource? cancellation = new();
    readonly TextBox search = new() { Width = 320, PlaceholderText = "Buscar recurso o paquete…" };
    readonly ComboBox kind = new() { Width = 180, DropDownStyle = ComboBoxStyle.DropDownList };
    readonly Button export = new() { Text = "Exportar índice JSON", AutoSize = true, Enabled = false };
    readonly Button cancel = new() { Text = "Cancelar lectura", AutoSize = true };
    readonly Button extractSelected = new() { Text = "Extraer selección", AutoSize = true, Enabled = false };
    readonly Button extractFiltered = new() { Text = "Extraer filtrados", AutoSize = true, Enabled = false };
    readonly Button exportMod = new() { Text = "Exportar para mod…", AutoSize = true, Enabled = false };
    readonly Button viewModel = new() { Text = "Ver modelo 3D", AutoSize = true, Enabled = false };
    readonly Label status = new() { Dock = DockStyle.Fill, AutoSize = true, Text = "Preparando lectura…" };
    readonly TextBox details = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
    readonly DataGridView grid = new()
    {
        Dock = DockStyle.Fill, ReadOnly = true, VirtualMode = true, AllowUserToAddRows = false,
        AllowUserToDeleteRows = false, RowHeadersVisible = false, MultiSelect = true,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect, BackgroundColor = Color.White,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
    };
    ResourceIndex? index;
    List<ResourceEntry> visible = [];
    Dictionary<string, int> occurrences = new(StringComparer.OrdinalIgnoreCase);
    bool reading = true;
    bool extracting;
    bool opening;

    public ResourceForm(IEnumerable<string> paths, SessionStore? session = null, string? initialSearch = null, string? initialPackage = null)
    {
        this.session = session;
        gallery.CacheRoot = session is null ? null : Path.Combine(session.Root, "thumbnails");
        this.initialPackage = initialPackage;
        this.paths = paths.ToArray();
        Text = "CSNZ Workshop — Recursos internos";
        Size = new(1400, 850); MinimumSize = new(1100, 650); StartPosition = FormStartPosition.CenterParent;
        Font = new("Segoe UI", 10);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new(16), ColumnCount = 1, RowCount = 5 };
        layout.RowStyles.Add(new(SizeType.Absolute, 250));
        layout.RowStyles.Add(new(SizeType.Percent, 100));
        layout.RowStyles.Add(new(SizeType.Absolute, 0));
        layout.RowStyles.Add(new(SizeType.Absolute, 48));
        layout.RowStyles.Add(new(SizeType.Absolute, 32));
        kind.Items.AddRange(["Todos", "Modelos (.mdl)", "Sonidos (.wav/.mp3)", "Sprites (.spr)", "Mapas (.bsp)", "Texturas (.wad/.tga/.bmp/.dds)"]);
        kind.SelectedIndex = 0;
        var toolbar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Margin = Padding.Empty };
        toolbar.RowStyles.Add(new(SizeType.Absolute, 76));
        toolbar.RowStyles.Add(new(SizeType.Absolute, 42));
        toolbar.RowStyles.Add(new(SizeType.Absolute, 42));
        toolbar.RowStyles.Add(new(SizeType.Absolute, 44));
        toolbar.Controls.Add(UiTheme.Header("Biblioteca de recursos", "Encuentra, previsualiza y lleva tus recursos a una colección"), 0, 0);
        var searchBar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, WrapContents = false };
        packageOnly.Visible = initialPackage is not null; packageOnly.Checked = initialPackage is not null;
        searchBar.Controls.AddRange([search, kind, favoritesOnly, packageOnly]);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, WrapContents = false };
        actions.Controls.AddRange([exportMod, extractSelected, extractFiltered, viewModel, addCollection, manageCollection]);
        var tools = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, WrapContents = false };
        tools.Controls.AddRange([favorite, refresh, export, cancel, technical]);
        toolbar.Controls.Add(searchBar, 0, 1); toolbar.Controls.Add(actions, 0, 2); toolbar.Controls.Add(tools, 0, 3);
        toolbar.RowStyles.Add(new(SizeType.Absolute, 42));
        packageFilter.Items.Add("Todos los paquetes"); packageFilter.Items.AddRange(this.paths); packageFilter.SelectedIndex = 0;
        var advanced = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, WrapContents = false };
        advanced.Controls.AddRange([packageFilter, new Label { Text = "MB mín.", AutoSize = true }, minSize,
            new Label { Text = "MB máx. (0: sin límite)", AutoSize = true }, maxSize]);
        toolbar.Controls.Add(advanced, 0, 4);
        packageFilter.SelectedIndexChanged += (_, _) => Filter(); minSize.ValueChanged += (_, _) => Filter(); maxSize.ValueChanged += (_, _) => Filter();
        UiTheme.Primary(exportMod);
        layout.Controls.Add(toolbar, 0, 0);
        foreach (var column in new[] { ("Recurso", 65f), ("Tipo", 10f), ("Tamaño", 16f), ("Paquete", 30f), ("Formato", 10f) })
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = column.Item1, FillWeight = column.Item2, SortMode = DataGridViewColumnSortMode.NotSortable });
        grid.CellValueNeeded += (_, e) =>
        {
            if (e.RowIndex < 0 || e.RowIndex >= visible.Count) return;
            var item = visible[e.RowIndex];
            e.Value = e.ColumnIndex switch { 0 => item.Ruta, 1 => item.Extension, 2 => ResourcePreview.FormatSize(item.Bytes), 3 => Path.GetFileName(item.Paquete), _ => item.Formato };
        };
        grid.SelectionChanged += (_, _) => { ShowDetails(); UpdateExtractionButtons(); };
        var listTab = new TabPage("Lista"); listTab.Controls.Add(grid);
        var galleryTab = new TabPage("Galería"); galleryTab.Controls.Add(gallery);
        views.TabPages.AddRange([galleryTab, listTab]);
        var split = new SplitContainer { Dock = DockStyle.Fill, Size = new(1300, 500), SplitterDistance = 800 };
        split.Panel1.Controls.Add(views); split.Panel2.Controls.Add(preview);
        var categories = new ListBox { Dock = DockStyle.Left, Width = 142, IntegralHeight = false, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 38 };
        categories.DrawItem += (_, e) =>
        {
            if (e.Index < 0) return;
            bool selected = (e.State & DrawItemState.Selected) != 0;
            using var background = new SolidBrush(selected ? UiTheme.Selection : UiTheme.Background);
            e.Graphics.FillRectangle(background, e.Bounds);
            if (selected) { using var accent = new SolidBrush(UiTheme.Accent); e.Graphics.FillRectangle(accent, e.Bounds.X, e.Bounds.Y + 6, 3, e.Bounds.Height - 12); }
            TextRenderer.DrawText(e.Graphics, categories.Items[e.Index].ToString(), e.Font, new Rectangle(e.Bounds.X + 14, e.Bounds.Y, e.Bounds.Width - 18, e.Bounds.Height), selected ? UiTheme.Foreground : UiTheme.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
            e.DrawFocusRectangle();
        };
        categories.Items.AddRange(["Todos", "Modelos", "Sonidos", "Sprites", "Mapas", "Texturas"]); categories.SelectedIndex = kind.SelectedIndex;
        categories.SelectedIndexChanged += (_, _) => { if (categories.SelectedIndex >= 0) kind.SelectedIndex = categories.SelectedIndex; };
        kind.SelectedIndexChanged += (_, _) => categories.SelectedIndex = kind.SelectedIndex;
        split.Panel1.Controls.Add(categories);
        layout.Controls.Add(split, 0, 1); layout.Controls.Add(details, 0, 2); layout.Controls.Add(status, 0, 3);
        technical.CheckedChanged += (_, _) => layout.RowStyles[2].Height = technical.Checked ? 118 : 0;
        gallery.ResourceSelected += row =>
        {
            if (row >= grid.RowCount) return;
            grid.ClearSelection(); grid.CurrentCell = grid.Rows[row].Cells[0]; grid.Rows[row].Selected = true; ShowDetails();
        };
        views.SelectedIndexChanged += (_, _) => { if (views.SelectedIndex == 0 && index is not null) gallery.SetResources(visible, index.Recursos); };
        layout.Controls.Add(new Label { AutoSize = true, Text = "Ctrl/Mayús: seleccionar varios. Doble clic en un .mdl abre el visor. La extracción conserva el origen y no convierte modelos para CS 1.6." }, 0, 4);
        Controls.Add(layout);
        UiTheme.Attach(this);
        if (session is not null)
        {
            search.Text = session.State.Search; kind.SelectedIndex = session.State.Category;
            views.SelectedIndex = session.State.Gallery ? 0 : 1;
            search.AutoCompleteMode = AutoCompleteMode.SuggestAppend; search.AutoCompleteSource = AutoCompleteSource.CustomSource;
            search.AutoCompleteCustomSource.AddRange(session.State.RecentSearches.ToArray());
        }
        favoritesOnly.Enabled = session is not null;
        if (initialSearch is not null) { search.Text = initialSearch; kind.SelectedIndex = 0; favoritesOnly.Checked = false; }
        packageOnly.CheckedChanged += (_, _) => Filter();
        favoritesOnly.CheckedChanged += (_, _) => Filter();
        favorite.Click += (_, _) =>
        {
            if (session is null) return;
            foreach (var entry in SelectedEntries())
            {
                string key = SessionStore.ResourceKey(entry);
                if (!session.State.Favorites.Remove(key)) session.State.Favorites.Add(key);
            }
            Filter();
        };
        refresh.Click += async (_, _) =>
        {
            if (reading || extracting || opening) return;
            cancellation = new(); reading = true; cancel.Enabled = true; UpdateExtractionButtons(); await LoadIndex(true);
        };
        search.Leave += (_, _) => session?.RememberSearch(search.Text);
        FormClosed += (_, _) =>
        {
            if (session is null) return;
            session.State.Search = search.Text; session.State.Category = kind.SelectedIndex; session.State.Gallery = views.SelectedIndex == 0;
            session.RememberSearch(search.Text); session.Save();
        };
        search.TextChanged += (_, _) => Filter(); kind.SelectedIndexChanged += (_, _) => Filter();
        cancel.Click += (_, _) => { cancellation?.Cancel(); cancel.Enabled = false; status.Text = "Cancelando…"; };
        FormClosing += (_, e) => { if (!SaveCollectionBeforeClosing()) { e.Cancel = true; return; } cancellation?.Cancel(); };
        export.Click += (_, _) => Export();
        extractSelected.Click += async (_, _) => await Extract(false);
        extractFiltered.Click += async (_, _) => await Extract(true);
        exportMod.Click += (_, _) => { using var dialog = new ModExportForm(SelectedEntries(), index?.Recursos); dialog.ShowDialog(this); };
        viewModel.Click += async (_, _) => await OpenModel();
        addCollection.Click += (_, _) =>
        {
            try
            {
                int added = collection.Add(SelectedEntries());
                status.Text = $"{added} agregados a «{collection.Name}» · {collection.Resources.Count} recursos en la colección. Guarda la colección para conservarla.";
                UpdateExtractionButtons();
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Colección"); }
        };
        manageCollection.Click += (_, _) =>
        {
            if (index is null) return;
            using var dialog = new CollectionForm(collection, index.Recursos); dialog.ShowDialog(this); UpdateExtractionButtons();
        };
        preview.AnalysisRequested += (bundle, entry) =>
        {
            if (index is null) return;
            using var dialog = new ModelAnalysisForm(bundle, entry, index.Recursos, collection);
            dialog.ShowDialog(this); UpdateExtractionButtons();
        };
        grid.CellDoubleClick += async (_, e) => { if (e.RowIndex >= 0 && e.RowIndex < visible.Count && visible[e.RowIndex].Extension == ".mdl") await OpenModel(); };
        Shown += async (_, _) => await LoadIndex();
    }

    async Task LoadIndex(bool force = false)
    {
        var operation = cancellation!;
        var progress = new Progress<string>(name => { if (!IsDisposed && reading) status.Text = "Leyendo " + name + "…"; });
        try
        {
            if (session is null) index = await Task.Run(() => ArchiveIndex.Build(paths, operation.Token, progress));
            else
            {
                var loaded = await Task.Run(() => CachedIndex.Build(paths, Path.Combine(session.Root, "index"), force, operation.Token, progress));
                index = loaded.Index;
                cacheSummary = $" · {loaded.Reused} índices recuperados / {loaded.Updated} actualizados" + (loaded.Warning is null ? "" : " · " + loaded.Warning);
            }
            if (IsDisposed) return;
            occurrences = index.Recursos.GroupBy(r => Normalize(r.Ruta), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
            reading = false;
            export.Enabled = true; Filter();
            if (index.Errores.Count > 0)
                MessageBox.Show(this, string.Join(Environment.NewLine, index.Errores.Take(8).Select(e => Path.GetFileName(e.Paquete) + ": " + e.Error))
                    + "\r\nEl índice exportado incluye todos los errores.", "Paquetes no leídos");
        }
        catch (OperationCanceledException) { if (!IsDisposed) status.Text = "Lectura cancelada. Cierra esta ventana y vuelve a abrirla para reintentar."; }
        catch (Exception ex) { if (!IsDisposed) { status.Text = "No se pudo crear el índice."; MessageBox.Show(this, ex.Message, "Error"); } }
        finally
        {
            reading = false; operation.Dispose(); cancellation = null;
            if (!IsDisposed) { cancel.Enabled = false; UpdateExtractionButtons(); }
        }
    }

    static string Normalize(string path) => path.Replace('\\', '/').TrimStart('/');

    void Filter()
    {
        if (index is null || reading) return;
        string query = search.Text.Trim();
        var favoriteKeys = session?.State.Favorites.ToHashSet(StringComparer.OrdinalIgnoreCase);
        visible = index.Recursos.Where(r =>
            (packageFilter.SelectedIndex <= 0 || r.Paquete.Equals(packageFilter.Text, StringComparison.OrdinalIgnoreCase)) &&
            r.Bytes >= (long)(minSize.Value * 1048576) && (maxSize.Value == 0 || r.Bytes <= (long)(maxSize.Value * 1048576)) &&
            (!packageOnly.Checked || r.Paquete.Equals(initialPackage, StringComparison.OrdinalIgnoreCase)) &&
            (!favoritesOnly.Checked || favoriteKeys?.Contains(SessionStore.ResourceKey(r)) == true) &&
            (r.Ruta.Contains(query, StringComparison.OrdinalIgnoreCase) || r.Paquete.Contains(query, StringComparison.OrdinalIgnoreCase))
            && (kind.SelectedIndex switch
            {
                1 => r.Extension == ".mdl", 2 => r.Extension is ".wav" or ".mp3", 3 => r.Extension == ".spr",
                4 => r.Extension == ".bsp", 5 => r.Extension is ".wad" or ".tga" or ".bmp" or ".dds", _ => true
            })).OrderBy(r => ResourceSearch.Rank(r, query)).ThenBy(r => r.Ruta, StringComparer.OrdinalIgnoreCase).ToList();
        grid.RowCount = visible.Count;
        grid.ClearSelection();
        if (visible.Count > 0) { grid.CurrentCell = grid.Rows[0].Cells[0]; grid.Rows[0].Selected = true; }
        grid.Invalidate(); ShowDetails();
        if (views.SelectedIndex == 0) gallery.SetResources(visible, index.Recursos);
        UpdateExtractionButtons();
        status.Text = Counts();
    }

    string Counts() => index is null ? "" :
        $"{visible.Count:N0} visibles / {index.Recursos.Count:N0} recursos · {index.PaquetesLeidos}/{paths.Length} paquetes leídos · {index.Errores.Count} errores" + cacheSummary;

    void ShowDetails()
    {
        int row = grid.CurrentCell?.RowIndex ?? -1;
        gallery.SelectIndex(row);
        if (row < 0 || row >= visible.Count) { details.Clear(); preview.SelectResource(null, []); return; }
        var item = visible[row];
        preview.SelectResource(item, index?.Recursos ?? []);
        details.Text = $"{item.Ruta}\r\nOrigen: {item.Paquete}\r\nTamaño declarado: {item.Bytes:N0} bytes · Guardado: {item.BytesGuardados:N0} · Offset: {item.Offset:N0} · Flags: 0x{item.Flags:X}\r\nEntradas con esta ruta: {occurrences.GetValueOrDefault(Normalize(item.Ruta))}. Se conserva cada origen por separado.";
    }

    void Export()
    {
        if (index is null) return;
        using var dialog = new SaveFileDialog { Filter = "Índice JSON|*.json", FileName = "recursos-csnz.json" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try { ArchiveIndex.Export(dialog.FileName, index); MessageBox.Show(this, "Índice completo exportado; incluye todos los recursos y errores."); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "No se pudo exportar"); }
    }

    void UpdateExtractionButtons()
    {
        bool idle = !reading && !extracting && !opening;
        refresh.Enabled = idle; favorite.Enabled = idle && session is not null && grid.SelectedRows.Count > 0;
        extractSelected.Enabled = idle && grid.SelectedRows.Count > 0;
        exportMod.Enabled = extractSelected.Enabled;
        addCollection.Enabled = extractSelected.Enabled;
        manageCollection.Enabled = idle && index is not null;
        manageCollection.Text = $"Colección ({collection.Resources.Count}){(collection.Dirty ? " *" : "")}…";
        extractFiltered.Enabled = idle && visible.Count > 0;
        int row = grid.CurrentCell?.RowIndex ?? -1;
        viewModel.Enabled = idle && row >= 0 && row < visible.Count && visible[row].Extension == ".mdl";
    }

    ResourceEntry[] SelectedEntries() => grid.SelectedRows.Cast<DataGridViewRow>()
        .Select(row => row.Index).Where(i => i >= 0 && i < visible.Count).Order().Select(i => visible[i]).ToArray();

    bool SaveCollectionBeforeClosing()
    {
        if (!collection.Dirty) return true;
        var answer = MessageBox.Show(this, "¿Guardar la colección antes de cerrar la biblioteca?", "Colección sin guardar", MessageBoxButtons.YesNoCancel);
        if (answer == DialogResult.Cancel) return false;
        if (answer == DialogResult.No) return true;
        using var dialog = new SaveFileDialog { Filter = "Colección CSNZ|*.csnzcollection", DefaultExt = "csnzcollection", FileName = "mi-mod.csnzcollection" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return false;
        try { collection.Save(dialog.FileName); return true; }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "No se pudo guardar"); return false; }
    }

    async Task OpenModel()
    {
        if (reading || extracting || opening || index is null) return;
        int row = grid.CurrentCell?.RowIndex ?? -1;
        if (row < 0 || row >= visible.Count) return;
        var entry = visible[row];
        if (entry.Extension != ".mdl") return;
        var library = index.Recursos;
        opening = true;
        using var operation = new CancellationTokenSource();
        cancellation = operation;
        cancel.Text = "Cancelar modelo"; cancel.Enabled = true; UpdateExtractionButtons();
        status.Text = "Descifrando y leyendo " + entry.Ruta + "…";
        try
        {
            // Companion textures and animation groups come from the same library, so they never touch the game folder.
            var bundle = await Task.Run(() => ModelBundle.FromArchive(entry, library, operation.Token));
            if (IsDisposed) return;
            opening = false;
            using var window = new ModelForm(bundle);
            window.ShowDialog(this);
        }
        catch (OperationCanceledException) { if (!IsDisposed) status.Text = "Lectura del modelo cancelada."; }
        catch (Exception ex) { if (!IsDisposed) MessageBox.Show(this, ex.Message, "No se pudo abrir el modelo"); }
        finally
        {
            opening = false; cancellation = null;
            if (!IsDisposed)
            {
                cancel.Enabled = false; cancel.Text = "Cancelar lectura"; UpdateExtractionButtons();
                if (!status.Text.StartsWith("Lectura del modelo")) status.Text = Counts();
            }
        }
    }

    async Task Extract(bool filtered)
    {
        if (reading || extracting) return;
        var selected = filtered ? visible.ToArray() : SelectedEntries();
        if (selected.Length == 0) return;
        using var dialog = new FolderBrowserDialog
        {
            SelectedPath = session?.State.Destination ?? "",
            Description = $"Extraer {selected.Length:N0} recursos ({selected.Sum(r => r.Bytes) / 1048576d:N1} MiB declarados). Se crean carpetas por paquete y no se sobrescribe ningún archivo.",
            UseDescriptionForTitle = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        string destination = dialog.SelectedPath;
        if (session is not null) session.State.Destination = destination;
        using var operation = new CancellationTokenSource();
        cancellation = operation; extracting = true;
        search.Enabled = kind.Enabled = grid.Enabled = views.Enabled = export.Enabled = false;
        cancel.Text = "Cancelar extracción"; cancel.Enabled = true; UpdateExtractionButtons();
        var progress = new Progress<ExtractionProgress>(p =>
        {
            if (!IsDisposed && extracting) status.Text = $"Extrayendo {p.Procesados:N0}/{p.Total:N0}: {p.Recurso}";
        });
        try
        {
            var result = await Task.Run(() =>
            {
                var report = Extraction.Run(selected, destination, operation.Token, progress);
                string reportMessage;
                try { reportMessage = "Informe: " + Extraction.SaveReport(destination, report); }
                catch (Exception ex) { reportMessage = "No se pudo guardar el informe: " + ex.Message; }
                return (report, reportMessage);
            });
            if (IsDisposed) return;
            extracting = false;
            string summary = $"{result.report.Extraidos:N0} extraídos · {result.report.Omitidos:N0} omitidos · {result.report.Errores:N0} errores · {result.report.Pendientes:N0} pendientes";
            status.Text = (result.report.Cancelada ? "Cancelada: " : "Finalizada: ") + summary;
            details.Text = summary + "\r\n" + result.reportMessage + "\r\n" + string.Join("\r\n", result.report.Archivos
                .Where(a => a.Estado == "Error").Take(5).Select(a => a.Recurso + ": " + a.Detalle));
            MessageBox.Show(this, status.Text + "\r\n\r\n" + result.reportMessage, "Extracción");
        }
        catch (Exception ex) { if (!IsDisposed) { status.Text = "No se pudo completar la extracción."; MessageBox.Show(this, ex.Message, "Extracción"); } }
        finally
        {
            extracting = false; cancellation = null;
            if (!IsDisposed)
            {
                search.Enabled = kind.Enabled = grid.Enabled = views.Enabled = true; export.Enabled = index is not null;
                cancel.Enabled = false; cancel.Text = "Cancelar lectura"; UpdateExtractionButtons();
            }
        }
    }
}
