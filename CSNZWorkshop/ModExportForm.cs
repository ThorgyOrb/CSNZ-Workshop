using CSNZWorkshop.Archives;
using CSNZWorkshop.Models;

namespace CSNZWorkshop;

public sealed class ModExportForm : Form
{
    readonly ListView list = new() { Dock = DockStyle.Fill, View = View.Details, CheckBoxes = true, FullRowSelect = true };
    readonly Label status = new() { Dock = DockStyle.Bottom, Height = 65 };
    readonly Button save = new() { Text = "Elegir destino y exportar…", AutoSize = true };
    readonly Button cancel = new() { Text = "Cerrar", AutoSize = true };
    CancellationTokenSource? operation;
    readonly Button diagnose = new() { Text = "Diagnosticar selección", AutoSize = true };
    readonly TextBox diagnosis = new() { Dock = DockStyle.Bottom, Height = 130, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
    readonly Dictionary<ResourceEntry, ExportDiagnostic> diagnostics = [];
    readonly IReadOnlyList<ResourceEntry> library;
    public ModExportForm(IReadOnlyList<ResourceEntry> entries, IReadOnlyList<ResourceEntry>? library = null)
    {
        this.library = library ?? entries;
        Text = "Exportar carpetas para mi mod"; Size = new(1100, 650); MinimumSize = new(850, 500); Font = new("Segoe UI", 10);
        list.Columns.Add("Destino relativo", 370); list.Columns.Add("Paquete", 210); list.Columns.Add("Estado", 410);
        var plans = entries.Select(e =>
        {
            try { return (Entry: e, Path: ModExport.Relative(e), Error: ""); }
            catch (Exception ex) when (ex is InvalidDataException or NotSupportedException) { return (Entry: e, Path: e.Ruta, Error: ex.Message); }
        }).ToArray();
        var conflicts = plans.Where(p => p.Error == "").GroupBy(p => p.Path, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var p in plans)
            list.Items.Add(new ListViewItem([p.Path, Path.GetFileName(p.Entry.Paquete), p.Error != "" ? p.Error : conflicts.Contains(p.Path) ? "Conflicto: marca una sola versión" : "Disponible"])
            { Tag = p.Error == "" ? p.Entry : null, Checked = p.Error == "" && !conflicts.Contains(p.Path) });
        list.ItemCheck += (_, e) => { if (list.Items[e.Index].Tag is null) e.NewValue = CheckState.Unchecked; };
        list.Columns.Add("Diagnóstico", 260);
        foreach (ListViewItem row in list.Items) row.SubItems.Add("Pendiente de análisis");
        list.ItemCheck += (_, _) =>
        {
            diagnostics.Clear(); diagnosis.Clear();
            foreach (ListViewItem row in list.Items) row.SubItems[3].Text = "Pendiente de análisis";
        };
        list.SelectedIndexChanged += (_, _) => diagnosis.Text = list.SelectedItems.Count == 1 && list.SelectedItems[0].Tag is ResourceEntry selected && diagnostics.TryGetValue(selected, out var result) ? result.Detail : "Selecciona una fila después de diagnosticar para ver los detalles.";
        status.Text = "Revisa las rutas y marca los recursos. Los archivos existentes se omiten.\r\nSe exportan los bytes originales: el MDL no se descifra ni adapta automáticamente. No se instala en el juego.";
        var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 45 }; actions.Controls.AddRange([diagnose, save, cancel]);
        Controls.Add(list); Controls.Add(diagnosis); Controls.Add(status); Controls.Add(actions);
        diagnose.Click += async (_, _) => await Diagnose();
        save.Click += async (_, _) => await Export();
        cancel.Click += (_, _) => { if (operation is null) Close(); else operation.Cancel(); };
        FormClosing += (_, e) => { if (operation is not null) { operation.Cancel(); e.Cancel = true; } }; UiTheme.Attach(this);
    }
    async Task Diagnose()
    {
        var selected = list.CheckedItems.Cast<ListViewItem>().Select(i => i.Tag).OfType<ResourceEntry>().ToArray();
        if (selected.Length == 0) { status.Text = "Marca los recursos que quieres comprobar."; return; }
        using var pending = new CancellationTokenSource(); operation = pending;
        save.Enabled = diagnose.Enabled = list.Enabled = false; cancel.Text = "Cancelar";
        try
        {
            foreach (var entry in selected)
            {
                status.Text = "Comprobando " + entry.Ruta;
                var report = await Task.Run(() => ExportDiagnostic.Analyze(entry, selected, library, pending.Token), pending.Token);
                diagnostics[entry] = report;
                foreach (ListViewItem row in list.Items) if (Equals(row.Tag, entry)) row.SubItems[3].Text = report.Status;
            }
            status.Text = "Diagnóstico terminado. Selecciona una fila para ver detalles. La compatibilidad final requiere prueba en CS 1.6.";
        }
        catch (OperationCanceledException) { status.Text = "Diagnóstico cancelado; los recursos pendientes no están verificados."; }
        catch (Exception ex) { status.Text = "No se pudo completar el diagnóstico: " + ex.Message; }
        finally { operation = null; save.Enabled = diagnose.Enabled = list.Enabled = true; cancel.Text = "Cerrar"; }
    }
    async Task Export()
    {
        var selected = list.CheckedItems.Cast<ListViewItem>().Select(i => i.Tag).OfType<ResourceEntry>().ToArray();
        if (selected.Length == 0) { status.Text = "Marca al menos un recurso."; return; }
        if (selected.Select(ModExport.Relative).Distinct(StringComparer.OrdinalIgnoreCase).Count() != selected.Length)
        { status.Text = "Hay destinos repetidos: deja marcada una sola versión por ruta."; return; }
        using var folder = new FolderBrowserDialog { Description = "Carpeta de salida para revisar el mod", UseDescriptionForTitle = true };
        if (folder.ShowDialog(this) != DialogResult.OK) return;
        using var pending = new CancellationTokenSource(); operation = pending; save.Enabled = diagnose.Enabled = list.Enabled = false; cancel.Text = "Cancelar";
        var progress = new Progress<ExtractionProgress>(p => status.Text = $"{p.Procesados}/{p.Total}: {p.Recurso}");
        try
        {
            var report = await Task.Run(() => Extraction.Run(selected, folder.SelectedPath, pending.Token, progress, true));
            string summary = $"{report.Extraidos} extraídos · {report.Omitidos} omitidos · {report.Errores} errores · {report.Pendientes} pendientes";
            status.Text = summary;
            try { status.Text += "\r\n" + Extraction.SaveReport(folder.SelectedPath, report); }
            catch (Exception ex) { status.Text += "\r\nNo se pudo guardar el informe: " + ex.Message; }
        }
        catch (Exception ex) { status.Text = ex.Message; }
        finally { operation = null; save.Enabled = diagnose.Enabled = list.Enabled = true; cancel.Text = "Cerrar"; }
    }
}
