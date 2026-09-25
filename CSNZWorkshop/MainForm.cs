namespace CSNZWorkshop;

public sealed class MainForm : Form
{
    readonly SessionStore session = new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CSNZWorkshop"));
    readonly TextBox folder = new() { Text = Inventory.DefaultPath, Dock = DockStyle.Fill };
    readonly TextBox search = new() { PlaceholderText = "Buscar recursos en todos los paquetes…", Width = 330 };
    readonly TextBox packageSearch = new() { PlaceholderText = "Filtrar paquetes…", Width = 220 };
    readonly ComboBox format = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 100 };
    readonly Button scan = new() { Text = "Escanear", AutoSize = true };
    readonly Button export = new() { Text = "Exportar inventario", AutoSize = true, Enabled = false };
    readonly Button resources = new() { Text = "Ver recursos", AutoSize = true, Enabled = false };
    readonly Button library = new() { Text = "Buscar en todos", AutoSize = true, Enabled = false };
    readonly Button openModel = new() { Text = "Abrir modelo…", AutoSize = true };
    readonly Label status = new() { Text = "Selecciona la carpeta Data de CSNZ y pulsa Escanear.", Dock = DockStyle.Fill, AutoSize = true };
    readonly DataGridView grid = new()
    {
        Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false,
        AllowUserToDeleteRows = false, AutoGenerateColumns = false, RowHeadersVisible = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false,
        BackgroundColor = Color.White, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
    };
    readonly TextBox details = new() { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical };
    ScanResult? result;

    public MainForm()
    {
        Text = "CSNZ Workshop — Biblioteca de paquetes";
        Size = new(1150, 760);
        MinimumSize = new(850, 550);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new("Segoe UI", 10);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new(18), ColumnCount = 1, RowCount = 7 };
        foreach (var height in new float[] { 82, 42, 88 }) layout.RowStyles.Add(new(SizeType.Absolute, height));
        layout.RowStyles.Add(new(SizeType.Percent, 100));
        layout.RowStyles.Add(new(SizeType.Absolute, 100));
        layout.RowStyles.Add(new(SizeType.Absolute, 40));
        layout.RowStyles.Add(new(SizeType.Absolute, 36));
        layout.Controls.Add(UiTheme.Header("CSNZ Workshop", "Explora los recursos del juego · Prepara tu próximo mod"), 0, 0);
        UiTheme.Primary(library); UiTheme.Primary(scan);
        var location = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3 };
        location.ColumnStyles.Add(new(SizeType.Percent, 100));
        location.ColumnStyles.Add(new(SizeType.Absolute, 105));
        location.ColumnStyles.Add(new(SizeType.Absolute, 105));
        var browse = new Button { Text = "Carpeta…", AutoSize = true };
        location.Controls.Add(folder, 0, 0); location.Controls.Add(browse, 1, 0); location.Controls.Add(scan, 2, 0);
        layout.Controls.Add(location, 0, 1);
        var filters = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new(0, 5, 0, 0) };
        format.Items.AddRange(["Todos", "NAR", "PAK"]); format.SelectedIndex = 0;
        filters.Controls.AddRange([search, library, packageSearch, format, export, resources, openModel]); layout.Controls.Add(filters, 0, 2);
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Paquete", DataPropertyName = "Nombre", FillWeight = 35 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Formato", DataPropertyName = "Formato", FillWeight = 12 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Bytes", DataPropertyName = "Bytes", FillWeight = 20, DefaultCellStyle = new() { Format = "N0" } });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Ruta", DataPropertyName = "Ruta", FillWeight = 80 });
        layout.Controls.Add(grid, 0, 3); layout.Controls.Add(details, 0, 4); layout.Controls.Add(status, 0, 5);
        layout.Controls.Add(new Label { Text = "Abre los recursos de un paquete o la biblioteca completa para buscar, ver modelos y extraer archivos.", AutoSize = true }, 0, 6);
        Controls.Add(layout);
        folder.Text = session.State.GameFolder;
        var dark = new CheckBox { Text = "Tema oscuro", AutoSize = true, Checked = session.State.Dark };
        filters.Controls.Add(dark); UiTheme.Dark = dark.Checked; UiTheme.Attach(this);
        dark.CheckedChanged += (_, _) => { UiTheme.Dark = session.State.Dark = dark.Checked; UiTheme.Apply(this); };
        FormClosing += (_, _) =>
        {
            session.State.GameFolder = folder.Text.Trim();
            if (!session.Save()) MessageBox.Show(this, session.Warning, "Preferencias");
        };
        if (session.Warning is not null) status.Text = session.Warning;
        Shown += async (_, _) => { if (Directory.Exists(session.State.GameFolder)) await Scan(); };
        browse.Click += (_, _) => { using var dialog = new FolderBrowserDialog(); if (dialog.ShowDialog(this) == DialogResult.OK) folder.Text = dialog.SelectedPath; };
        scan.Click += async (_, _) => await Scan();
        resources.Click += (_, _) => OpenResources(false);
        library.Click += (_, _) => OpenResources(true);
        openModel.Click += async (_, _) => await OpenModelFile();
        grid.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0 && scan.Enabled) OpenResources(false); };
        packageSearch.TextChanged += (_, _) => Filter(); format.SelectedIndexChanged += (_, _) => Filter();
        search.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.Handled = e.SuppressKeyPress = true;
            if (library.Enabled) OpenResources(true);
        };
        grid.SelectionChanged += (_, _) =>
        {
            resources.Enabled = scan.Enabled && grid.CurrentRow?.DataBoundItem is Package;
            details.Text = grid.CurrentRow?.DataBoundItem is Package p
                ? $"{p.Ruta}\r\nEstado: {p.Estado}\r\nPrimeros 32 bytes (hex): {p.Cabecera}" : "";
        };
        export.Click += (_, _) =>
        {
            if (result is null) return;
            using var dialog = new SaveFileDialog { Filter = "Inventario JSON|*.json", FileName = "inventario-csnz.json" };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            try { Inventory.Export(dialog.FileName, result); MessageBox.Show(this, "Inventario completo exportado."); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "No se pudo exportar"); }
        };
    }

    async Task Scan()
    {
        scan.Enabled = false; export.Enabled = false; resources.Enabled = false; library.Enabled = false;
        status.Text = "Leyendo paquetes…";
        var path = folder.Text.Trim();
        try
        {
            result = await Task.Run(() => Inventory.Scan(path));
            if (IsDisposed) return;
            Filter(); export.Enabled = true; library.Enabled = result.Packages.Count > 0;
            if (result.Errors.Count > 0) MessageBox.Show(this, string.Join(Environment.NewLine, result.Errors.Take(10)), "Archivos o carpetas no accesibles");
        }
        catch (Exception ex)
        {
            if (IsDisposed) return;
            result = null; grid.DataSource = null; details.Clear(); status.Text = "No se pudo completar el escaneo.";
            MessageBox.Show(this, ex.Message, "Error de lectura");
        }
        finally { if (!IsDisposed) { scan.Enabled = true; resources.Enabled = grid.CurrentRow?.DataBoundItem is Package; } }
    }

    async Task OpenModelFile()
    {
        using var dialog = new OpenFileDialog { Filter = "Modelos MDL|*.mdl", Title = "Abrir un modelo ya extraído (no se modifica el archivo)" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        string path = dialog.FileName;
        openModel.Enabled = false;
        status.Text = "Leyendo " + Path.GetFileName(path) + "…";
        try
        {
            // Companion files (T.mdl y grupos de animación) se buscan junto al archivo elegido.
            var bundle = await Task.Run(() => Models.ModelBundle.FromFile(path));
            if (IsDisposed) return;
            using var window = new ModelForm(bundle);
            window.ShowDialog(this);
        }
        catch (Exception ex) { if (!IsDisposed) MessageBox.Show(this, ex.Message, "No se pudo abrir el modelo"); }
        finally
        {
            if (!IsDisposed)
            {
                openModel.Enabled = true;
                if (result is not null) Filter(); else status.Text = "Selecciona la carpeta Data de CSNZ y pulsa Escanear.";
            }
        }
    }

    void OpenResources(bool all)
    {
        if (result is null) return;
        var paths = result.Packages.Select(p => p.Ruta).ToArray();
        string? selectedPackage = !all && grid.CurrentRow?.DataBoundItem is Package package ? package.Ruta : null;
        if (!all && selectedPackage is null) return;
        if (paths.Length == 0) return;
        session.State.GameFolder = folder.Text.Trim();
        using var window = new ResourceForm(paths, session, all ? search.Text.Trim() : null, selectedPackage);
        window.ShowDialog(this);
    }

    void Filter()
    {
        if (result is null) return;
        var visible = result.Packages.Where(p => (format.SelectedIndex == 0 || p.Formato == format.Text)
            && p.Ruta.Contains(packageSearch.Text.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        grid.DataSource = visible;
        status.Text = $"{visible.Count} visibles / {result.Packages.Count} paquetes · {result.Packages.Sum(p => p.Bytes) / 1073741824d:N2} GiB · {result.Errors.Count} errores";
    }
}
