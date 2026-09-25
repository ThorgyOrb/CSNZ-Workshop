using CSNZWorkshop.Archives;

namespace CSNZWorkshop;

public sealed class CollectionForm : Form
{
    readonly ResourceCollection collection;
    readonly List<ResourceEntry> library;
    readonly ResourcePreview preview = new(allowAnalysis: false);
    readonly TextBox name = new() { Width = 260, MaxLength = 120 };
    readonly ListView list = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = true };
    readonly Label status = new() { Dock = DockStyle.Fill, AutoSize = true };
    readonly Button extract = new() { Text = "Extraer colección…", AutoSize = true };
    readonly Button cancel = new() { Text = "Cancelar extracción", AutoSize = true, Enabled = false };
    readonly FlowLayoutPanel actions = new() { Dock = DockStyle.Fill, AutoScroll = true };
    CancellationTokenSource? operation;

    public CollectionForm(ResourceCollection collection, IReadOnlyList<ResourceEntry> library)
    {
        this.collection = collection; this.library = library.ToList();
        Text = "Colecciones para mi mod"; Size = new(1400, 850); MinimumSize = new(1100, 720);
        Font = new("Segoe UI", 10); StartPosition = FormStartPosition.CenterParent;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new(16), ColumnCount = 1, RowCount = 5 };
        layout.RowStyles.Add(new(SizeType.Absolute, 64)); layout.RowStyles.Add(new(SizeType.Absolute, 86));
        layout.RowStyles.Add(new(SizeType.Percent, 100)); layout.RowStyles.Add(new(SizeType.Absolute, 58)); layout.RowStyles.Add(new(SizeType.Absolute, 42));
        layout.Controls.Add(new Label { Dock = DockStyle.Fill, Text = "Reúne recursos de varios paquetes y guarda una colección por mod.\r\nLa extracción conserva los archivos originales; los modelos se preparan desde su vista previa." }, 0, 0);
        var fresh = new Button { Text = "Nueva", AutoSize = true };
        var open = new Button { Text = "Abrir…", AutoSize = true };
        var save = new Button { Text = "Guardar…", AutoSize = true };
        var remove = new Button { Text = "Quitar selección", AutoSize = true };
        var up = new Button { Text = "Subir", AutoSize = true }; var down = new Button { Text = "Bajar", AutoSize = true };
        var relocate = new Button { Text = "Localizar paquete…", AutoSize = true };
        var mod = new Button { Text = "Exportar para mod…", AutoSize = true };
        actions.Controls.AddRange([name, fresh, open, save, remove, up, down, relocate, extract, mod]); layout.Controls.Add(actions, 0, 1);
        list.Columns.Add("Recurso", 400); list.Columns.Add("Paquete", 200); list.Columns.Add("Estado", 260);
        var split = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        split.ColumnStyles.Add(new(SizeType.Percent, 60)); split.ColumnStyles.Add(new(SizeType.Percent, 40));
        split.Controls.Add(list, 0, 0); split.Controls.Add(preview, 1, 0);
        layout.Controls.Add(split, 0, 2); layout.Controls.Add(status, 0, 3); layout.Controls.Add(cancel, 0, 4); Controls.Add(layout);
        list.SelectedIndexChanged += (_, _) => preview.SelectResource(list.SelectedItems.Count == 1 ? ((CollectionItem)list.SelectedItems[0].Tag!).Entry : null, this.library);
        void Move(int direction)
        {
            if (list.SelectedItems.Count != 1) return;
            var item = ((CollectionItem)list.SelectedItems[0].Tag!).Reference;
            collection.Move(item, direction); RefreshItems();
            foreach (ListViewItem row in list.Items) if (((CollectionItem)row.Tag!).Reference == item) { row.Selected = true; row.EnsureVisible(); }
        }
        up.Click += (_, _) => Move(-1); down.Click += (_, _) => Move(1);
        relocate.Click += async (_, _) =>
        {
            if (list.SelectedItems.Count != 1) { status.Text = "Selecciona una referencia del paquete que quieres localizar."; return; }
            string original = ((CollectionItem)list.SelectedItems[0].Tag!).Reference.Package;
            using var file = new OpenFileDialog { Filter = "Paquetes|*.nar;*.pak", Title = "Seleccionar nueva ubicación del paquete" };
            if (file.ShowDialog(this) != DialogResult.OK) return;
            actions.Enabled = false;
            try
            {
                var entries = await Task.Run(() => ArchiveIndex.Read(file.FileName));
                if (IsDisposed) return;
                collection.RelocatePackage(original, file.FileName, entries);
                this.library.RemoveAll(e => e.Paquete.Equals(file.FileName, StringComparison.OrdinalIgnoreCase)); this.library.AddRange(entries); RefreshItems();
            }
            catch (Exception ex) { if (!IsDisposed) status.Text = ex.Message; }
            finally { if (!IsDisposed) actions.Enabled = true; }
        };
        mod.Click += (_, _) =>
        {
            var resolved = collection.Resolve(this.library);
            if (resolved.Any(r => r.Entry is null)) { status.Text = "Resuelve las referencias no disponibles antes de exportar."; return; }
            using var dialog = new ModExportForm(resolved.Select(r => r.Entry!).ToArray(), this.library); dialog.ShowDialog(this);
        };
        name.Text = collection.Name;
        name.TextChanged += (_, _) => { if (!string.IsNullOrWhiteSpace(name.Text)) collection.Rename(name.Text); };
        fresh.Click += (_, _) => { if (KeepChanges()) { collection.Clear(); name.Text = collection.Name; RefreshItems(); } };
        open.Click += (_, _) => Open();
        save.Click += (_, _) => Save();
        remove.Click += (_, _) => { collection.Remove(list.SelectedItems.Cast<ListViewItem>().Select(i => ((CollectionItem)i.Tag!).Reference)); RefreshItems(); };
        extract.Click += async (_, _) => await Extract();
        cancel.Click += (_, _) => { operation?.Cancel(); cancel.Enabled = false; };
        FormClosing += (_, e) => { preview.SelectResource(null, this.library); if (operation is not null) { operation.Cancel(); e.Cancel = true; } };
        UiTheme.Attach(this);
        RefreshItems();
    }

    void RefreshItems()
    {
        list.Items.Clear(); var resolved = collection.Resolve(library);
        foreach (var item in resolved) list.Items.Add(new ListViewItem([item.Reference.Resource, Path.GetFileName(item.Reference.Package), item.Status]) { Tag = item });
        int missing = resolved.Count(r => r.Entry is null);
        status.Text = $"{resolved.Count} recursos · {missing} no disponibles. " + (missing > 0 ? "Abre la biblioteca completa o quita las referencias no disponibles antes de extraer." : "Puedes guardar la colección y extraer todos sus recursos juntos.");
        extract.Enabled = resolved.Count > 0 && missing == 0;
    }

    bool Save()
    {
        using var dialog = new SaveFileDialog { Filter = "Colección CSNZ|*.csnzcollection", DefaultExt = "csnzcollection", FileName = "mi-mod.csnzcollection" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return false;
        try { collection.Rename(name.Text); collection.Save(dialog.FileName); status.Text = "Colección guardada: " + dialog.FileName; return true; }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "No se pudo guardar"); return false; }
    }

    bool KeepChanges()
    {
        if (!collection.Dirty) return true;
        var answer = MessageBox.Show(this, "¿Guardar la colección actual antes de reemplazarla?", "Colección sin guardar", MessageBoxButtons.YesNoCancel);
        return answer == DialogResult.No || answer == DialogResult.Yes && Save();
    }

    void Open()
    {
        if (!KeepChanges()) return;
        using var dialog = new OpenFileDialog { Filter = "Colección CSNZ|*.csnzcollection" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try { collection.Load(dialog.FileName); name.Text = collection.Name; RefreshItems(); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "No se pudo abrir la colección"); }
    }

    async Task Extract()
    {
        var resolved = collection.Resolve(library);
        if (resolved.Count == 0 || resolved.Any(r => r.Entry is null)) return;
        using var dialog = new FolderBrowserDialog { Description = $"Extraer {resolved.Count} recursos de la colección", UseDescriptionForTitle = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        using var pending = new CancellationTokenSource(); operation = pending;
        actions.Enabled = list.Enabled = false; cancel.Enabled = true;
        var progress = new Progress<ExtractionProgress>(p => status.Text = $"Extrayendo {p.Procesados}/{p.Total}: {p.Recurso}");
        try
        {
            var report = await Task.Run(() => Extraction.Run(resolved.Select(r => r.Entry!).ToArray(), dialog.SelectedPath, pending.Token, progress));
            string summary = $"{report.Extraidos} extraídos · {report.Omitidos} omitidos · {report.Errores} errores · {report.Pendientes} pendientes";
            status.Text = (report.Cancelada ? "Cancelada: " : "Finalizada: ") + summary;
            string reportPath;
            try { reportPath = Extraction.SaveReport(dialog.SelectedPath, report); }
            catch (Exception ex) { reportPath = "No se pudo guardar el informe: " + ex.Message; }
            MessageBox.Show(this, status.Text + "\r\n" + reportPath, "Extracción de colección");
        }
        catch (Exception ex) { status.Text = "No se pudo extraer: " + ex.Message; }
        finally { operation = null; actions.Enabled = list.Enabled = true; cancel.Enabled = false; }
    }
}
