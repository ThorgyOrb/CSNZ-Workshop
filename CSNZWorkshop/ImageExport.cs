using CSNZWorkshop.Archives;

namespace CSNZWorkshop;

public static class ImageExport
{
    public static void Save(IWin32Window owner, Image image, IEnumerable<string> sources, string filename = "textura.png")
    {
        using var dialog = new SaveFileDialog { Filter = "Imagen PNG|*.png", FileName = filename, DefaultExt = "png" };
        if (dialog.ShowDialog(owner) != DialogResult.OK) return;
        string temporary = dialog.FileName + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            SafeOutput.CheckDestination(dialog.FileName, sources);
            if (sources.Any(s => Path.GetFullPath(s).Equals(Path.GetFullPath(dialog.FileName), StringComparison.OrdinalIgnoreCase))) throw new InvalidDataException("No se puede reemplazar el archivo de origen.");
            image.Save(temporary, System.Drawing.Imaging.ImageFormat.Png);
            SafeOutput.RejectLinks(dialog.FileName); File.Move(temporary, dialog.FileName, true);
        }
        catch (Exception ex) { MessageBox.Show(owner, ex.Message, "No se pudo guardar la imagen"); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
