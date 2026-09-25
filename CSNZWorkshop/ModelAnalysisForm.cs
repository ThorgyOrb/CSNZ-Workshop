using System.Text.Json;
using CSNZWorkshop.Archives;
using CSNZWorkshop.Models;

namespace CSNZWorkshop;

public sealed class ModelAnalysisForm : Form
{
    readonly ModelBundle bundle;
    readonly ResourceEntry source;
    readonly IReadOnlyList<ResourceEntry> library;
    readonly ResourceCollection collection;
    readonly CancellationTokenSource cancellation = new();
    readonly ListView related = new() { Dock = DockStyle.Fill, View = View.Details, CheckBoxes = true, FullRowSelect = true, MultiSelect = false, HideSelection = false };
    readonly ResourcePreview preview = new(allowAnalysis: false);
    readonly TextBox diagnosis = new() { Dock = DockStyle.Fill, ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Vertical };
    readonly Label status = new() { Dock = DockStyle.Fill, AutoSize = true, Text = "Analizando referencias y animaciones…" };
    readonly Button add = new() { Text = "Agregar marcados a colección", AutoSize = true, Enabled = false };
    readonly Button export = new() { Text = "Guardar diagnóstico JSON…", AutoSize = true, Enabled = false };
    readonly Button dependencies = new() { Text = "Marcar modelo y dependencias únicas", AutoSize = true, Enabled = false };
    readonly Button reviewExport = new() { Text = "Revisar exportación…", AutoSize = true, Enabled = false };
    RelatedResult? result;
    CompatibilityReport? report;

    public ModelAnalysisForm(ModelBundle bundle, ResourceEntry source, IReadOnlyList<ResourceEntry> library, ResourceCollection collection)
    {
        this.bundle = bundle; this.source = source; this.library = library; this.collection = collection;
        Text = "Relacionados y CS 1.6 — " + bundle.Main.Name; Size = new(1400, 850); MinimumSize = new(1100, 720);
        Font = new("Segoe UI", 10); StartPosition = FormStartPosition.CenterParent;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new(16), ColumnCount = 1, RowCount = 4 };
        layout.RowStyles.Add(new(SizeType.Absolute, 66)); layout.RowStyles.Add(new(SizeType.Percent, 100));
        layout.RowStyles.Add(new(SizeType.Absolute, 52)); layout.RowStyles.Add(new(SizeType.Absolute, 46));
        layout.Controls.Add(new Label { Dock = DockStyle.Fill, Text = "Selecciona una fila para ver o escuchar el recurso. Marca las casillas para añadir archivos a la colección.\r\nLas sugerencias por nombre no confirman una dependencia; el diagnóstico no certifica compatibilidad con CS 1.6." }, 0, 0);
        var tabs = new TabControl { Dock = DockStyle.Fill };
        var resourcesTab = new TabPage("Recursos relacionados");
        var split = new SplitContainer { Dock = DockStyle.Fill, Size = new(1300, 600), SplitterDistance = 800 };
        split.Panel1.Controls.Add(related); split.Panel2.Controls.Add(preview); resourcesTab.Controls.Add(split);
        var diagnosticTab = new TabPage("Diagnóstico CS 1.6"); diagnosticTab.Controls.Add(diagnosis);
        tabs.TabPages.AddRange([resourcesTab, diagnosticTab]); layout.Controls.Add(tabs, 0, 1);
        related.Columns.Add("Tipo", 185); related.Columns.Add("Recurso", 260); related.Columns.Add("Paquete", 180); related.Columns.Add("Motivo / estado", 420);
        related.ShowItemToolTips = true;
        void PreviewSelection()
        {
            var selected = related.SelectedItems.Count == 1 ? related.SelectedItems[0] : null;
            preview.SelectResource(tabs.SelectedTab == resourcesTab ? selected?.Tag as ResourceEntry : null, library);
            if (selected is not null && tabs.SelectedTab == resourcesTab)
                status.Text = selected.Tag is ResourceEntry entry ? entry.Ruta + " · " + entry.Paquete
                    : "Recurso no encontrado en la biblioteca abierta; no hay archivo disponible para previsualizar.";
        }
        related.SelectedIndexChanged += (_, _) => PreviewSelection();
        tabs.SelectedIndexChanged += (_, _) => PreviewSelection();
        related.DoubleClick += (_, _) => preview.OpenModelViewer();
        related.ItemCheck += (_, e) => { if (related.Items[e.Index].Tag is not ResourceEntry) e.NewValue = CheckState.Unchecked; };
        layout.Controls.Add(status, 0, 2);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill }; var close = new Button { Text = "Cerrar", AutoSize = true };
        actions.AutoScroll = true;
        actions.Controls.AddRange([dependencies, add, reviewExport, export, close]); layout.Controls.Add(actions, 0, 3); Controls.Add(layout);
        dependencies.Click += (_, _) =>
        {
            if (result is null) return;
            var unique = result.Items.Where(r => r.Reference && r.Entry is not null).GroupBy(r => (r.Kind, r.Path.ToLowerInvariant()))
                .Where(g => g.Count() == 1).Select(g => g.Single().Entry!).Append(source).ToHashSet();
            foreach (ListViewItem row in related.Items) row.Checked = row.Tag is ResourceEntry entry && unique.Contains(entry);
            status.Text = "Modelo y referencias únicas marcados. Revisa faltantes, versiones duplicadas y sprites sugeridos antes de agregar o exportar.";
        };
        reviewExport.Click += (_, _) =>
        {
            var selected = related.CheckedItems.Cast<ListViewItem>().Select(i => i.Tag).OfType<ResourceEntry>().ToArray();
            if (selected.Length == 0) { status.Text = "Marca los recursos que quieres revisar."; return; }
            using var dialog = new ModExportForm(selected, library); dialog.ShowDialog(this);
        };
        UiTheme.Attach(this);
        close.Click += (_, _) => Close(); FormClosing += (_, _) => { cancellation.Cancel(); preview.SelectResource(null, library); };
        add.Click += (_, _) =>
        {
            try
            {
                var selected = related.CheckedItems.Cast<ListViewItem>().Select(i => i.Tag).OfType<ResourceEntry>().ToArray();
                int count = collection.Add(selected); status.Text = $"{count} recursos agregados a «{collection.Name}». Guarda la colección para conservarla.";
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Colección"); }
        };
        export.Click += (_, _) => Export(); Shown += async (_, _) => await Analyze();
    }

    async Task Analyze()
    {
        var token = cancellation.Token;
        try
        {
            var analysis = await Task.Run(() =>
            {
                var references = RelatedResources.Find(source, bundle, library, token);
                return (references, diagnostic: CompatibilityReport.Analyze(bundle, references, true, token));
            }, token);
            if (IsDisposed || token.IsCancellationRequested) return;
            result = analysis.references; report = analysis.diagnostic;
            related.BeginUpdate();
            related.Items.Add(new ListViewItem(["Modelo seleccionado", source.Ruta, Path.GetFileName(source.Paquete), "Marca para incluir también el modelo principal"]) { Tag = source });
            foreach (var item in result.Items)
                related.Items.Add(new ListViewItem([item.Kind, item.Path, item.Entry is null ? "No encontrado" : Path.GetFileName(item.Entry.Paquete), item.Reason])
                { Tag = item.Entry, ToolTipText = (item.Entry?.Paquete ?? item.Path) + "\r\n" + item.Reason, ForeColor = item.Entry is null ? Color.Firebrick : SystemColors.WindowText });
            related.EndUpdate();
            diagnosis.Text = report.Describe(); add.Enabled = export.Enabled = dependencies.Enabled = reviewExport.Enabled = true;
            status.Text = report.Status + (result.SuggestionsTruncated ? " · Se muestran las primeras 200 sugerencias por nombre." : $" · {result.Items.Count} resultados relacionados.");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!IsDisposed) { status.Text = "No se pudo completar el análisis."; diagnosis.Text = ex.Message; } }
    }

    void Export()
    {
        if (report is null || result is null) return;
        using var dialog = new SaveFileDialog { Filter = "Diagnóstico JSON|*.json", FileName = "diagnostico-cs16.json" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            SafeOutput.CheckDestination(dialog.FileName, library.Select(r => r.Paquete));
            if (library.Any(r => Path.GetFullPath(r.Paquete).Equals(Path.GetFullPath(dialog.FileName), StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("El informe no puede reemplazar un paquete de origen.");
            File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(new
            {
                Modelo = source.Ruta, Paquete = source.Paquete, Diagnostico = report,
                Referencias = result.Items.Select(r => new { r.Kind, r.Path, Paquete = r.Entry?.Paquete, Ruta = r.Entry?.Ruta, r.Reason, r.Reference }),
                result.SuggestionsTruncated,
                Secuencias = bundle.Model.Sequences.Select((s, i) => new { Indice = i, s.Name, s.Frames, s.Fps, s.Blends }),
                Eventos = bundle.Model.Events,
                Perfil = "SDK GoldSrc público, límites de referencia; no certificación de un cliente CS 1.6",
                Fuentes = new[] { "https://github.com/ValveSoftware/halflife/blob/master/engine/studio.h", "https://github.com/ValveSoftware/halflife/blob/master/cl_dll/entity.cpp" }
            }, new JsonSerializerOptions { WriteIndented = true }));
            status.Text = "Diagnóstico guardado: " + dialog.FileName;
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "No se pudo guardar el diagnóstico"); }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !IsDisposed) { cancellation.Cancel(); cancellation.Dispose(); }
        base.Dispose(disposing);
    }
}
