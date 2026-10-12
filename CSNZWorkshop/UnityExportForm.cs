using CSNZWorkshop.Models;

namespace CSNZWorkshop;

public sealed class UnityExportForm : Form
{
    readonly ModelBundle bundle;
    readonly NumericUpDown scale = new() { DecimalPlaces = 5, Minimum = .00001m, Maximum = 100, Value = .0254m, Increment = .001m, Width = 110 };
    readonly Button save = new() { Text = "Elegir destino y exportar…", AutoSize = true };
    readonly Button cancel = new() { Text = "Cerrar", AutoSize = true };
    readonly Label status = new() { Dock = DockStyle.Fill, AutoSize = true };
    CancellationTokenSource? operation;

    public UnityExportForm(ModelBundle bundle)
    {
        this.bundle = bundle;
        Text = "Exportar modelo para Unity"; Size = new(720, 450); MinimumSize = new(580, 380);
        StartPosition = FormStartPosition.CenterParent; Font = new("Segoe UI", 10);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new(20), RowCount = 4, ColumnCount = 1 };
        layout.RowStyles.Add(new(SizeType.Percent, 100)); layout.RowStyles.Add(new(SizeType.Absolute, 48));
        layout.RowStyles.Add(new(SizeType.Absolute, 60)); layout.RowStyles.Add(new(SizeType.Absolute, 48));
        layout.Controls.Add(new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
            Text = "Exporta todas las skins, variantes y blends a una carpeta nueva con model.json, texturas PNG y export-report.json.\r\n\r\n" +
                "Copia la carpeta unity del repositorio a Assets/CSNZWorkshop de tu proyecto Unity. Después usa Tools > CSNZ Workshop > Import model.json.\r\n\r\n" +
                "Los clips usan un rig Generic. Los eventos se conservan como metadatos. Los materiales y efectos requieren revisión. Las secuencias con FPS cero se omiten y se detallan en el informe.\r\n\r\n" +
                "La escala predeterminada es 0.0254 metros por unidad GoldSrc. Confirma el tamaño esperado en tu proyecto." }, 0, 0);
        var options = new FlowLayoutPanel { Dock = DockStyle.Fill }; options.Controls.AddRange([new Label { Text = "Metros por unidad", AutoSize = true }, scale]); layout.Controls.Add(options, 0, 1);
        status.Text = bundle.Missing.Count > 0 ? "Faltan compañeros: " + string.Join(", ", bundle.Missing) : "Listo para exportar: " + bundle.Main.Name;
        save.Enabled = bundle.Missing.Count == 0; layout.Controls.Add(status, 0, 2);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill }; actions.Controls.AddRange([save, cancel]); layout.Controls.Add(actions, 0, 3);
        Controls.Add(layout); UiTheme.Attach(this);
        save.Click += async (_, _) => await Save();
        cancel.Click += (_, _) => { if (operation is null) Close(); else operation.Cancel(); };
        FormClosing += (_, e) => { if (operation is not null) { operation.Cancel(); e.Cancel = true; } };
    }

    async Task Save()
    {
        using var dialog = new FolderBrowserDialog { Description = "Exportar paquete para Unity", UseDescriptionForTitle = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        using var pending = new CancellationTokenSource(); operation = pending;
        float meters = (float)scale.Value; save.Enabled = scale.Enabled = false; cancel.Text = "Cancelar exportación";
        status.Text = "Exportando mallas, texturas y animaciones…";
        try
        {
            string folder = await Task.Run(() => UnityModelExporter.Export(bundle, dialog.SelectedPath, meters, pending.Token));
            status.Text = "Exportación terminada.";
            MessageBox.Show(this, "Paquete guardado en:\r\n" + folder + "\r\n\r\nEn Unity: Tools > CSNZ Workshop > Import model.json", "Exportar para Unity");
        }
        catch (OperationCanceledException) { status.Text = "Exportación cancelada."; }
        catch (Exception ex) { status.Text = "No se pudo exportar: " + ex.Message; }
        finally { operation = null; scale.Enabled = true; save.Enabled = bundle.Missing.Count == 0; cancel.Text = "Cerrar"; }
    }
}
