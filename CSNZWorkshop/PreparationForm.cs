using CSNZWorkshop.Models;

namespace CSNZWorkshop;

public sealed class PreparationForm : Form
{
    readonly ModelBundle bundle;
    readonly Button save = new() { Text = "Elegir destino y preparar…", AutoSize = true };
    readonly Button cancel = new() { Text = "Cerrar", AutoSize = true };
    readonly Label status = new() { Dock = DockStyle.Fill, AutoSize = true };
    CancellationTokenSource? operation;

    public PreparationForm(ModelBundle bundle)
    {
        this.bundle = bundle;
        Text = "Preparar modelo para mi mod";
        Size = new(720, 580); MinimumSize = new(580, 460);
        StartPosition = FormStartPosition.CenterParent; Font = new("Segoe UI", 10);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new(20), RowCount = 5, ColumnCount = 1 };
        layout.RowStyles.Add(new(SizeType.Absolute, 42));
        layout.RowStyles.Add(new(SizeType.Percent, 50));
        layout.RowStyles.Add(new(SizeType.Percent, 50));
        layout.RowStyles.Add(new(SizeType.Absolute, 58));
        layout.RowStyles.Add(new(SizeType.Absolute, 42));
        layout.Controls.Add(new Label { Text = "1. Revisa los archivos incluidos", AutoSize = true, Font = new(Font, FontStyle.Bold) }, 0, 0);
        var files = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true };
        files.Columns.Add("Archivo", 330); files.Columns.Add("Tamaño", 110);
        foreach (var asset in new[] { bundle.Main }.Concat(bundle.Companions))
            files.Items.Add(new ListViewItem([asset.Name, ResourcePreview.FormatSize(asset.Data.Length)]));
        layout.Controls.Add(files, 0, 1);
        layout.Controls.Add(new TextBox { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical,
            Text = bundle.Describe() + "\r\n\r\nSe guardará una carpeta nueva con el modelo, sus compañeros y preparacion.json. Los sonidos externos no se incluyen automáticamente." }, 0, 2);
        status.Text = bundle.Missing.Count > 0 ? "Faltan archivos: " + string.Join(", ", bundle.Missing)
            : "2. Elige una carpeta de salida. Después prueba la copia en CS 1.6.";
        save.Enabled = bundle.Missing.Count == 0;
        layout.Controls.Add(status, 0, 3);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill }; actions.Controls.AddRange([save, cancel]);
        layout.Controls.Add(actions, 0, 4); Controls.Add(layout);
        UiTheme.Attach(this);
        save.Click += async (_, _) => await Save();
        cancel.Click += (_, _) => { if (operation is null) Close(); else operation.Cancel(); };
        FormClosing += (_, e) => { if (operation is not null) { operation.Cancel(); e.Cancel = true; } };
    }

    async Task Save()
    {
        using var dialog = new FolderBrowserDialog { Description = "Guardar una copia para probar en CS 1.6", UseDescriptionForTitle = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        using var pending = new CancellationTokenSource(); operation = pending;
        save.Enabled = false; cancel.Text = "Cancelar preparación";
        status.Text = "Validando animaciones y preparando archivos…";
        try
        {
            string output = await Task.Run(() => bundle.Prepare(dialog.SelectedPath, pending.Token));
            status.Text = "Copia preparada. Pendiente de probar en CS 1.6.";
            MessageBox.Show(this, "Archivos guardados en:\r\n" + output, "Preparación terminada");
        }
        catch (OperationCanceledException) { status.Text = "Preparación cancelada."; }
        catch (Exception ex) { status.Text = "No se pudo preparar: " + ex.Message; }
        finally { operation = null; save.Enabled = true; cancel.Text = "Cerrar"; }
    }
}
