using CSNZWorkshop.Archives;

namespace CSNZWorkshop.Models;

public sealed record ExportDiagnostic(string Status, string Detail)
{
    public static ExportDiagnostic Analyze(ResourceEntry entry, IReadOnlyList<ResourceEntry> selected, IReadOnlyList<ResourceEntry> library, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        try
        {
            if (entry.Extension != ".mdl")
            {
                _ = Extraction.ReadResource(entry, token);
                return new("Preparado para extracción", "Lectura verificada. El uso del recurso sigue pendiente de prueba dentro del mod.");
            }
            var bytes = Extraction.ReadResource(entry, token);
            if (bytes.Length >= 4 && BitConverter.ToInt32(bytes) == 0x51534449)
                return new("Compañero de animación", "No es un modelo independiente. Inclúyelo junto al MDL principal y prueba sus animaciones.");
            var bundle = ModelBundle.FromArchive(entry, library, token);
            var related = RelatedResources.Find(entry, bundle, library, token);
            var report = CompatibilityReport.Analyze(bundle, related, true, token);
            var absent = related.Items.Where(r => r.Reference).GroupBy(r => (r.Kind, r.Path.ToLowerInvariant()))
                .Where(g => !g.Any(r => r.Entry is not null && selected.Contains(r.Entry))).Select(g => g.Key.Item2).ToArray();
            bool adapt = bundle.Main.OriginalVersion != 10 || bundle.Companions.Any(c => c.OriginalVersion != 10)
                || bundle.Model.Textures.Any(ModelRenderer.IsNexonEffect) || report.Findings.Any(f => f.Level == "Revisar");
            string status = absent.Length > 0 || bundle.Missing.Count > 0 ? "Faltan dependencias en la selección"
                : adapt ? "Requiere adaptación / revisión" : "Preparado para prueba en CS 1.6";
            return new(status, (absent.Length > 0 ? "No incluidos: " + string.Join(", ", absent) + "\r\n" : "")
                + (bundle.Main.OriginalVersion != 10 ? "El MDL original está cifrado: usa Preparar copia para prueba.\r\n" : "")
                + (bundle.Model.Textures.Any(ModelRenderer.IsNexonEffect) ? "Los efectos $0b se aproximan en el visor; la exportación original no adapta sus materiales.\r\n" : "")
                + report.Describe());
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException or UnauthorizedAccessException)
        { return new("No se pudo verificar", ex.Message); }
    }
}
