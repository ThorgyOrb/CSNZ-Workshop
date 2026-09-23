using System.Diagnostics;
using System.Windows.Forms;
using CSNZWorkshop;
using CSNZWorkshop.Models;

static class UiChecks
{
    public static void VerifyLibraryModel(string package)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
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
                // Other resource types must not offer the viewer.
                var search = Descendants(form).OfType<TextBox>().First(t => t.PlaceholderText.StartsWith("Buscar"));
                search.Text = "no-existe";
                Application.DoEvents();
                if (viewer.Enabled) throw new Exception("Model viewer stayed enabled without a selected model");
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
