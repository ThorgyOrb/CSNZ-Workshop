using CSNZWorkshop.Archives;
using CSNZWorkshop.Models;

namespace CSNZWorkshop;

public sealed class ResourceForm : Form
{
    readonly string[] paths;
    CancellationTokenSource? cancellation = new();
    readonly TextBox search = new() { Width = 320, PlaceholderText = "Buscar recurso o paquete…" };
    readonly ComboBox kind = new() { Width = 180, DropDownStyle = ComboBoxStyle.DropDownList };
    readonly Button export = new() { Text = "Exportar índice JSON", AutoSize = true, Enabled = false };
    readonly Button cancel = new() { Text = "Cancelar lectura", AutoSize = true };
    readonly Button extractSelected = new() { Text = "Extraer selección", AutoSize = true, Enabled = false };
    readonly Button extractFiltered = new() { Text = "Extraer filtrados", AutoSize = true, Enabled = false };
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

    public ResourceForm(IEnumerable<string> paths)
    {
        this.paths = paths.ToArray();
        Text = "CSNZ Workshop — Recursos internos";
        Size = new(1200, 780); MinimumSize = new(900, 580); StartPosition = FormStartPosition.CenterParent;
        Font = new("Segoe UI", 10);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new(16), ColumnCount = 1, RowCount = 5 };
        layout.RowStyles.Add(new(SizeType.Absolute, 106));
        layout.RowStyles.Add(new(SizeType.Percent, 100));
        layout.RowStyles.Add(new(SizeType.Absolute, 118));
        layout.RowStyles.Add(new(SizeType.Absolute, 48));
        layout.RowStyles.Add(new(SizeType.Absolute, 32));
        kind.Items.AddRange(["Todos", "Modelos (.mdl)", "Sonidos (.wav/.mp3)", "Sprites (.spr)", "Mapas (.bsp)", "Texturas (.wad/.tga/.bmp/.dds)"]);
        kind.SelectedIndex = 0;
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill };
        toolbar.Controls.AddRange([search, kind, export, cancel, extractSelected, extractFiltered, viewModel]); layout.Controls.Add(toolbar, 0, 0);
        foreach (var column in new[] { ("Recurso", 65f), ("Tipo", 10f), ("Bytes", 16f), ("Paquete", 30f), ("Formato", 10f) })
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = column.Item1, FillWeight = column.Item2, SortMode = DataGridViewColumnSortMode.NotSortable });
        grid.CellValueNeeded += (_, e) =>
        {
            if (e.RowIndex < 0 || e.RowIndex >= visible.Count) return;
            var item = visible[e.RowIndex];
            e.Value = e.ColumnIndex switch { 0 => item.Ruta, 1 => item.Extension, 2 => item.Bytes.ToString("N0"), 3 => Path.GetFileName(item.Paquete), _ => item.Formato };
        };
        grid.SelectionChanged += (_, _) => { ShowDetails(); UpdateExtractionButtons(); };
        layout.Controls.Add(grid, 0, 1); layout.Controls.Add(details, 0, 2); layout.Controls.Add(status, 0, 3);
        layout.Controls.Add(new Label { AutoSize = true, Text = "Ctrl/Mayús: seleccionar varios. Doble clic en un .mdl abre el visor. La extracción conserva el origen y no convierte modelos para CS 1.6." }, 0, 4);
        Controls.Add(layout);
        search.TextChanged += (_, _) => Filter(); kind.SelectedIndexChanged += (_, _) => Filter();
        cancel.Click += (_, _) => { cancellation?.Cancel(); cancel.Enabled = false; status.Text = "Cancelando…"; };
        FormClosing += (_, _) => cancellation?.Cancel();
        export.Click += (_, _) => Export();
        extractSelected.Click += async (_, _) => await Extract(false);
        extractFiltered.Click += async (_, _) => await Extract(true);
        viewModel.Click += async (_, _) => await OpenModel();
        grid.CellDoubleClick += async (_, e) => { if (e.RowIndex >= 0 && e.RowIndex < visible.Count && visible[e.RowIndex].Extension == ".mdl") await OpenModel(); };
        Shown += async (_, _) => await LoadIndex();
    }

    async Task LoadIndex()
    {
        var operation = cancellation!;
        var progress = new Progress<string>(name => { if (!IsDisposed && reading) status.Text = "Leyendo " + name + "…"; });
        try
        {
            index = await Task.Run(() => ArchiveIndex.Build(paths, operation.Token, progress));
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
        visible = index.Recursos.Where(r =>
            (r.Ruta.Contains(query, StringComparison.OrdinalIgnoreCase) || r.Paquete.Contains(query, StringComparison.OrdinalIgnoreCase))
            && (kind.SelectedIndex switch
            {
                1 => r.Extension == ".mdl", 2 => r.Extension is ".wav" or ".mp3", 3 => r.Extension == ".spr",
                4 => r.Extension == ".bsp", 5 => r.Extension is ".wad" or ".tga" or ".bmp" or ".dds", _ => true
            })).ToList();
        grid.RowCount = visible.Count; grid.Invalidate(); ShowDetails();
        UpdateExtractionButtons();
        status.Text = Counts();
    }

    string Counts() => index is null ? "" :
        $"{visible.Count:N0} visibles / {index.Recursos.Count:N0} recursos · {index.PaquetesLeidos}/{paths.Length} paquetes leídos · {index.Errores.Count} errores";

    void ShowDetails()
    {
        int row = grid.CurrentCell?.RowIndex ?? -1;
        if (row < 0 || row >= visible.Count) { details.Clear(); return; }
        var item = visible[row];
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
        extractSelected.Enabled = idle && grid.SelectedRows.Count > 0;
        extractFiltered.Enabled = idle && visible.Count > 0;
        int row = grid.CurrentCell?.RowIndex ?? -1;
        viewModel.Enabled = idle && row >= 0 && row < visible.Count && visible[row].Extension == ".mdl";
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
        var selected = filtered ? visible.ToArray() : grid.SelectedRows.Cast<DataGridViewRow>()
            .Select(row => row.Index).Where(i => i >= 0 && i < visible.Count).Order().Select(i => visible[i]).ToArray();
        if (selected.Length == 0) return;
        using var dialog = new FolderBrowserDialog
        {
            Description = $"Extraer {selected.Length:N0} recursos ({selected.Sum(r => r.Bytes) / 1048576d:N1} MiB declarados). Se crean carpetas por paquete y no se sobrescribe ningún archivo.",
            UseDescriptionForTitle = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        string destination = dialog.SelectedPath;
        using var operation = new CancellationTokenSource();
        cancellation = operation; extracting = true;
        search.Enabled = kind.Enabled = grid.Enabled = export.Enabled = false;
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
                search.Enabled = kind.Enabled = grid.Enabled = true; export.Enabled = index is not null;
                cancel.Enabled = false; cancel.Text = "Cancelar lectura"; UpdateExtractionButtons();
            }
        }
    }
}
