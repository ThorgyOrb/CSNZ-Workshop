using System.Diagnostics;
using System.Windows.Forms;
using CSNZWorkshop;
using CSNZWorkshop.Models;

static class UiChecks
{
    public static void VerifyEffectCycle(ModelBundle bundle) => OnUi(() =>
    {
        using var form = new ModelForm(bundle) { ShowInTaskbar = false, Opacity = 0 };
        form.Show(); Application.DoEvents();
        var controls = Descendants(form).ToArray();
        var variants = controls.OfType<ComboBox>().Single(c => c.Items.Cast<object>().Any(i => i.ToString() == "1: lightning2"));
        controls.OfType<Button>().Single(b => b.Text == "Ver arma con efectos").PerformClick();
        var watch = Stopwatch.StartNew();
        while (variants.SelectedIndex != 1)
        {
            Application.DoEvents(); Thread.Sleep(10);
            if (watch.ElapsedMilliseconds > 10000) throw new Exception("Effect bodygroup did not cycle");
        }
        controls.OfType<Button>().Single(b => b.Text == "Pausar").PerformClick();
        watch.Restart(); while (watch.ElapsedMilliseconds < 350) { Application.DoEvents(); Thread.Sleep(10); }
        if (variants.SelectedIndex != 1) throw new Exception("Effects continued after pause");
        controls.OfType<CheckBox>().Single(c => c.Text == "Simular variantes de efectos").Checked = false;
        if (!variants.Enabled || variants.SelectedIndex != 0) throw new Exception("Manual variant not restored");
        form.Close();
    });
    public static void VerifyExportDiagnostic(CSNZWorkshop.Archives.ResourceEntry entry) => OnUi(() =>
    {
        using var form = new ModExportForm([entry], [entry]) { ShowInTaskbar = false, Opacity = 0 };
        form.Show(); Application.DoEvents();
        var button = Descendants(form).OfType<Button>().Single(b => b.Text == "Diagnosticar selección");
        button.PerformClick(); var watch = Stopwatch.StartNew();
        while (!button.Enabled)
        {
            Application.DoEvents(); Thread.Sleep(10);
            if (watch.ElapsedMilliseconds > 10000) throw new Exception("Export diagnostic timeout");
        }
        var list = Descendants(form).OfType<ListView>().Single();
        if (list.Items[0].SubItems[3].Text == "Pendiente de análisis") throw new Exception("No export diagnostic result");
        list.Items[0].Checked = false;
        if (list.Items[0].SubItems[3].Text != "Pendiente de análisis") throw new Exception("Stale diagnostic after changing selection");
        form.Close();
    });
    public static void VerifyViewerControls(ModelBundle bundle) => OnUi(() =>
    {
        using var form = new ModelForm(bundle) { ShowInTaskbar = false, Opacity = 0 };
        form.Show(); Application.DoEvents();
        var controls = Descendants(form).ToArray();
        controls.OfType<ComboBox>().Single(c => c.Items.Contains("Pose de referencia")).SelectedIndex = 1;
        var track = controls.OfType<TrackBar>().Single(); track.Value = 3;
        var play = controls.OfType<Button>().Single(b => b.Text == "Reproducir");
        play.PerformClick();
        if (track.Value != 3 || play.Text != "Pausar") throw new Exception("Playback did not resume from selected frame");
        play.PerformClick();
        controls.OfType<Button>().Single(b => b.Text == "Fotograma ▶").PerformClick();
        if (track.Value != 4 || play.Text != "Reproducir") throw new Exception("Single-frame step failed");
        controls.OfType<ComboBox>().Single(c => c.Items.Contains("Vista libre")).SelectedIndex = 5;
        controls.OfType<CheckBox>().Single(c => c.Text == "Huesos (rayos X)").Checked = true;
        controls.OfType<ComboBox>().Single(c => c.Items.Contains("Fondo claro")).SelectedIndex = 2;
        Application.DoEvents();
        if (!controls.OfType<PictureBox>().Any(p => p.Image is not null)) throw new Exception("Viewer image missing");
        if (Environment.GetEnvironmentVariable("CSNZ_UI_SNAPSHOTS") is string folder && Directory.Exists(folder))
        {
            using var image = new System.Drawing.Bitmap(form.Width, form.Height);
            form.DrawToBitmap(image, new System.Drawing.Rectangle(0, 0, form.Width, form.Height));
            image.Save(Path.Combine(folder, "model-viewer.png"));
        }
        form.Close();
    });
    public static void VerifyGlobalSearch(string[] packages) => OnUi(() =>
    {
        using var form = new ResourceForm(packages, initialSearch: "test.dds", initialPackage: packages[0]) { ShowInTaskbar = false, Opacity = 0 };
        form.Show();
        var watch = Stopwatch.StartNew();
        while (!Descendants(form).OfType<Button>().Any(b => b.Text == "Actualizar índices" && b.Enabled))
        {
            Application.DoEvents(); Thread.Sleep(10);
            if (watch.ElapsedMilliseconds > 10000) throw new Exception("Global index timeout");
        }
        var grid = Descendants(form).OfType<DataGridView>().Single();
        if (grid.RowCount != 0) throw new Exception("Package filter ignored");
        Descendants(form).OfType<CheckBox>().Single(c => c.Text == "Solo paquete abierto").Checked = false;
        Application.DoEvents();
        if (grid.RowCount != 1 || grid.Rows[0].Cells[0].Value?.ToString()?.EndsWith("test.dds", StringComparison.OrdinalIgnoreCase) != true)
            throw new Exception("Global search did not find resource in another package");
        form.Close();
    });
    public static void VerifyComparison(ModelBundle a, ModelBundle b) => OnUi(() =>
    {
        using var form = new ModelComparisonForm(a, b) { ShowInTaskbar = false, Opacity = 0 };
        form.Show(); Application.DoEvents();
        if (Descendants(form).OfType<PictureBox>().Count(p => p.Image is not null) != 2) throw new Exception("Comparison images absent");
        form.Close();
    });
    public static void VerifyTextures(string[] packages) => OnUi(() =>
    {
        var resources = CSNZWorkshop.Archives.ArchiveIndex.Build(packages).Recursos;
        using var form = new Form { Size = new(600, 800), ShowInTaskbar = false, Opacity = 0 };
        using var preview = new ResourcePreview(); form.Controls.Add(preview); form.Show();
        foreach (var resource in resources)
        {
            preview.SelectResource(resource, resources); var watch = Stopwatch.StartNew();
            while (Descendants(preview).OfType<PictureBox>().Single().Image is null)
            {
                Application.DoEvents(); Thread.Sleep(10);
                if (watch.ElapsedMilliseconds > 10000) throw new Exception("Texture preview timeout: " + resource.Extension);
            }
            if (!Descendants(preview).OfType<Button>().Any(b => b.Text == "Guardar textura PNG…" && b.Enabled)) throw new Exception("Texture export disabled");
        }
        form.Close();
    });
    static void OnUi(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { failure = ex; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) throw new Exception("Workflow UI check failed", failure);
    }

    public static void VerifyRelatedPreview(ModelBundle bundle, CSNZWorkshop.Archives.ResourceEntry source, IReadOnlyList<CSNZWorkshop.Archives.ResourceEntry> library)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
                using var form = new ModelAnalysisForm(bundle, source, library, new ResourceCollection()) { ShowInTaskbar = false, Opacity = 0 };
                form.Show();
                void Until(Func<bool> condition)
                {
                    var watch = Stopwatch.StartNew();
                    while (!condition())
                    {
                        Application.DoEvents(); Thread.Sleep(10);
                        if (watch.ElapsedMilliseconds > 10000) throw new Exception("Related preview timeout");
                    }
                }
                Until(() => Descendants(form).OfType<Button>().Any(b => b.Text == "Agregar marcados a colección" && b.Enabled));
                var list = Descendants(form).OfType<ListView>().Single();
                var preview = Descendants(form).OfType<ResourcePreview>().Single();
                var picture = Descendants(preview).OfType<PictureBox>().Single();
                var play = Descendants(preview).OfType<Button>().Single(b => b.Text == "Reproducir");
                var stop = Descendants(preview).OfType<Button>().Single(b => b.Text == "Detener");
                var viewer = Descendants(preview).OfType<Button>().Single(b => b.Text == "Ver modelo 3D");
                void Select(string extension)
                {
                    foreach (ListViewItem item in list.Items) item.Selected = false;
                    list.Items.Cast<ListViewItem>().Single(i => i.Tag is CSNZWorkshop.Archives.ResourceEntry e && e.Extension == extension).Selected = true;
                    Application.DoEvents();
                }
                Select(".mdl"); Until(() => viewer.Enabled && picture.Image is not null);
                bool opened = false;
                using var closeViewer = new System.Windows.Forms.Timer { Interval = 50 };
                closeViewer.Tick += (_, _) =>
                {
                    var model = Application.OpenForms.OfType<ModelForm>().FirstOrDefault();
                    if (model is null) return;
                    opened = Descendants(model).OfType<PictureBox>().Any(p => p.Image is not null); model.Close();
                };
                closeViewer.Start(); viewer.PerformClick(); closeViewer.Stop();
                if (!opened) throw new Exception("Related model viewer did not render");
                foreach (string extension in new[] { ".wav", ".mp3" })
                {
                    Select(extension); Until(() => play.Enabled); play.PerformClick();
                    if (!stop.Enabled || viewer.Enabled) throw new Exception("Related audio controls incorrect");
                }
                var tabs = Descendants(form).OfType<TabControl>().Single();
                tabs.SelectedIndex = 1; Application.DoEvents();
                if (play.Enabled || stop.Enabled || picture.Image is not null) throw new Exception("Hidden preview retained playback");
                tabs.SelectedIndex = 0;
                Select(".spr"); Until(() => play.Enabled && picture.Image is not null);
                play.PerformClick();
                var frame = Descendants(preview).OfType<TrackBar>().Single(); Until(() => frame.Value > 0);
                if (list.CheckedItems.Count != 0) throw new Exception("Preview changed collection checkboxes");
                foreach (ListViewItem item in list.Items) item.Selected = false;
                var missing = list.Items.Add(new ListViewItem(["Referencia", "missing.wav", "No encontrado", "Falta"]));
                missing.Selected = true; Application.DoEvents();
                if (play.Enabled || viewer.Enabled || picture.Image is not null) throw new Exception("Missing resource retained stale preview");
                Select(".wav"); Until(() => play.Enabled); play.PerformClick(); form.Close();
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) throw new Exception("Related preview check failed", failure);
    }

    public static void VerifyAnalysis(ModelBundle bundle, CSNZWorkshop.Archives.ResourceEntry source, IReadOnlyList<CSNZWorkshop.Archives.ResourceEntry> library)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
                var collection = new ResourceCollection();
                using var form = new ModelAnalysisForm(bundle, source, library, collection) { ShowInTaskbar = false, Opacity = 0 };
                form.Show();
                var add = Descendants(form).OfType<Button>().Single(b => b.Text == "Agregar marcados a colección");
                var watch = Stopwatch.StartNew();
                while (!add.Enabled)
                {
                    Application.DoEvents(); Thread.Sleep(10);
                    if (watch.ElapsedMilliseconds > 10000) throw new Exception("Analysis UI timeout");
                }
                var list = Descendants(form).OfType<ListView>().Single();
                if (list.CheckedItems.Count != 0) throw new Exception("Related resources were selected automatically");
                list.Items[0].Checked = list.Items[1].Checked = true; add.PerformClick();
                if (collection.Resources.Count != 2) throw new Exception("Checked related resources not added");
                if (!Descendants(form).OfType<TextBox>().Any(t => t.Text.Contains("primer blend"))) throw new Exception("Diagnosis absent");
                Descendants(form).OfType<Button>().Single(b => b.Text == "Marcar modelo y dependencias únicas").PerformClick();
                var expected = RelatedResources.Find(source, bundle, library).Items.Where(r => r.Reference && r.Entry is not null)
                    .GroupBy(r => (r.Kind, r.Path.ToLowerInvariant())).Where(g => g.Count() == 1).Select(g => g.Single().Entry!).Append(source).ToHashSet();
                if (!list.CheckedItems.Cast<ListViewItem>().Select(r => r.Tag).OfType<CSNZWorkshop.Archives.ResourceEntry>().ToHashSet().SetEquals(expected))
                    throw new Exception("Unique dependencies selection incorrect");
                form.Close();
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) throw new Exception("Analysis UI check failed", failure);
    }

    public static void VerifyMedia(string spritePackage, string wavePackage)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
                var resources = CSNZWorkshop.Archives.ArchiveIndex.Build([spritePackage, wavePackage]).Recursos;
                using var form = new Form { ShowInTaskbar = false, Opacity = 0 };
                using var preview = new ResourcePreview(); form.Controls.Add(preview); form.Show();
                var play = Descendants(preview).OfType<Button>().Single(b => b.Text == "Reproducir");
                var stop = Descendants(preview).OfType<Button>().Single(b => b.Text == "Detener");
                void Until(Func<bool> condition)
                {
                    var watch = Stopwatch.StartNew();
                    while (!condition()) { Application.DoEvents(); Thread.Sleep(10); if (watch.ElapsedMilliseconds > 10000) throw new Exception("Media UI timeout"); }
                }
                preview.SelectResource(resources.Single(r => r.Extension == ".spr"), resources);
                Until(() => play.Enabled);
                var frame = Descendants(preview).OfType<TrackBar>().Single();
                play.PerformClick(); Until(() => frame.Value == 1); stop.PerformClick();
                if (frame.Value != 0 || stop.Enabled) throw new Exception("Sprite did not stop/reset");
                preview.SelectResource(resources.Single(r => r.Extension == ".wav"), resources);
                Until(() => play.Enabled); play.PerformClick();
                preview.SelectResource(null, resources);
                if (play.Enabled || stop.Enabled || Descendants(preview).OfType<PictureBox>().Any(p => p.Image is not null))
                    throw new Exception("Media retained after clearing selection");
                preview.SelectResource(resources[0], resources); form.Close();
                using var collectionForm = new CollectionForm(new ResourceCollection(), resources);
                collectionForm.Show(); Application.DoEvents();
                if (Descendants(collectionForm).OfType<Button>().Single(b => b.Text.StartsWith("Extraer colección")).Enabled)
                    throw new Exception("Empty collection can be extracted");
                collectionForm.Close();
                var collection = new ResourceCollection(); collection.Add(resources);
                var onlySprites = resources.Where(r => r.Extension == ".spr").ToArray();
                using var partial = new CollectionForm(collection, onlySprites);
                partial.Show(); Application.DoEvents();
                var extract = Descendants(partial).OfType<Button>().Single(b => b.Text.StartsWith("Extraer colección"));
                if (extract.Enabled) throw new Exception("Collection with missing resources can be extracted");
                var list = Descendants(partial).OfType<ListView>().Single();
                list.Items.Cast<ListViewItem>().Single(i => i.SubItems[0].Text.EndsWith(".wav")).Selected = true;
                Descendants(partial).OfType<Button>().Single(b => b.Text == "Quitar selección").PerformClick();
                if (!extract.Enabled || collection.Resources.Count != 1) throw new Exception("Removing missing reference did not enable extraction");
                partial.Close();
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) throw new Exception("Media UI check failed", failure);
    }

    public static void VerifyLibraryModel(string package)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
                using var form = new ResourceForm([package]) { ShowInTaskbar = false, Opacity = 0 };
                form.Show();
                var watch = Stopwatch.StartNew();
                while (!Descendants(form).OfType<Button>().Any(b => b.Text == "Extraer filtrados" && b.Enabled))
                {
                    Application.DoEvents();
                    if (watch.Elapsed > TimeSpan.FromSeconds(10)) throw new Exception("UI loading timeout");
                    Thread.Sleep(10);
                }
                var viewer = Descendants(form).OfType<Button>().FirstOrDefault(b => b.Text == "Ver modelo 3D")
                    ?? throw new Exception("Model viewer button missing");
                var grid = Descendants(form).OfType<DataGridView>().Single();
                grid.CurrentCell = grid.Rows[0].Cells[0];
                Application.DoEvents();
                if (!viewer.Enabled) throw new Exception("Model viewer was not enabled for an .mdl resource");
                var preview = Descendants(form).OfType<ResourcePreview>().Single();
                var gallery = Descendants(form).OfType<ResourceGallery>().Single();
                watch.Restart();
                while (!Descendants(preview).OfType<PictureBox>().Any(p => p.Image is not null)
                    || !Descendants(gallery).OfType<Button>().Any(b => b.Image is not null))
                {
                    Application.DoEvents();
                    if (watch.Elapsed > TimeSpan.FromSeconds(10)) throw new Exception("Integrated preview or gallery thumbnail timeout");
                    Thread.Sleep(10);
                }
                if (!Descendants(preview).OfType<Button>().Any(b => b.Text.StartsWith("Preparar") && b.Enabled))
                    throw new Exception("Preparation was not enabled after loading a complete model");
                // Other resource types must not offer the viewer.
                if (Environment.GetEnvironmentVariable("CSNZ_UI_SNAPSHOTS") is string snapshotFolder && Directory.Exists(snapshotFolder))
                {
                    foreach (bool dark in new[] { true, false })
                    {
                        UiTheme.Dark = dark; UiTheme.Apply(form); Application.DoEvents();
                        using var snapshot = new System.Drawing.Bitmap(form.Width, form.Height);
                        form.DrawToBitmap(snapshot, new System.Drawing.Rectangle(0, 0, form.Width, form.Height));
                        snapshot.Save(Path.Combine(snapshotFolder, dark ? "library-dark.png" : "library-light.png"));
                    }
                    UiTheme.Dark = true; UiTheme.Apply(form);
                }
                var search = Descendants(form).OfType<TextBox>().First(t => t.PlaceholderText.StartsWith("Buscar"));
                search.Text = "no-existe";
                Application.DoEvents();
                if (viewer.Enabled) throw new Exception("Model viewer stayed enabled without a selected model");
                if (Descendants(preview).OfType<PictureBox>().Any(p => p.Image is not null)
                    || Descendants(preview).OfType<Button>().Any(b => b.Enabled))
                    throw new Exception("Empty search retained a stale preview or preparation action");
                search.Text = "";
                Application.DoEvents();
                // Closing while the debounced preview is pending must cancel it safely.
                form.Close();
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start(); thread.Join();
        if (failure is not null) throw new Exception("Library viewer smoke check failed", failure);
    }

    public static void VerifyModel(ModelBundle bundle)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
                using var form = new ModelForm(bundle) { ShowInTaskbar = false, Opacity = 0 };
                form.Show();
                var view = Descendants(form).OfType<PictureBox>().First(p => p.BackColor == Color.FromArgb(32, 40, 51));
                var watch = Stopwatch.StartNew();
                while (view.Image is null)
                {
                    Application.DoEvents();
                    if (watch.Elapsed > TimeSpan.FromSeconds(10)) throw new Exception("Model view timeout");
                    Thread.Sleep(10);
                }
                if (view.Image.Width < 64) throw new Exception("Model view was not rendered");
                var sequences = Descendants(form).OfType<ComboBox>()
                    .FirstOrDefault(c => c.Items.Count > 0 && (c.Items[0] as string) == "Pose de referencia")
                    ?? throw new Exception("Sequence list missing");
                if (sequences.Items.Count != bundle.Model.Sequences.Length + 1) throw new Exception("Sequences were not listed");
                if (!Descendants(form).OfType<Button>().Any(b => b.Text.StartsWith("Preparar")))
                    throw new Exception("Preparation button missing");
                using var preparation = new PreparationForm(bundle);
                preparation.Show();
                Application.DoEvents();
                var files = Descendants(preparation).OfType<ListView>().Single();
                if (files.Items.Count != bundle.Companions.Count + 1)
                    throw new Exception("Preparation review did not list every included file");
                preparation.Close();
                form.Close();
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start(); thread.Join();
        if (failure is not null) throw new Exception("Model UI smoke check failed", failure);
    }

    public static void Verify(string package)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
                using var form = new ResourceForm([package]) { ShowInTaskbar = false, Opacity = 0 };
                form.Show();
                var watch = Stopwatch.StartNew();
                while (!Descendants(form).OfType<Button>().Any(b => b.Text == "Extraer filtrados" && b.Enabled))
                {
                    Application.DoEvents();
                    if (watch.Elapsed > TimeSpan.FromSeconds(10)) throw new Exception("UI loading timeout");
                    Thread.Sleep(10);
                }
                var grid = Descendants(form).OfType<DataGridView>().Single();
                if (grid.RowCount != 1 || !grid.MultiSelect || !grid.VirtualMode)
                    throw new Exception("Resource table was not populated correctly");
                if (!Descendants(form).OfType<Button>().Any(b => b.Text == "Extraer selección" && b.Enabled))
                    throw new Exception("Selection extraction was not enabled");
                form.Close();
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start(); thread.Join();
        if (failure is not null) throw new Exception("UI smoke check failed", failure);
    }

    static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control control in parent.Controls)
        {
            yield return control;
            foreach (var child in Descendants(control)) yield return child;
        }
    }
}
