namespace CSNZWorkshop;

internal static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        if (args.Length == 4 && args[0] == "--extract")
        {
            try
            {
                var entries = Archives.ArchiveIndex.Read(args[1]).Where(r => r.Ruta == args[2]).ToArray();
                if (entries.Length == 0) throw new FileNotFoundException("No se encontró esa ruta interna en el paquete.");
                var report = Archives.Extraction.Run(entries, args[3]);
                string reportPath = Archives.Extraction.SaveReport(args[3], report);
                Console.WriteLine(reportPath);
                return report.Errores == 0 ? 0 : 2;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }
        }
        if (args.Length == 3 && args[0] == "--models")
        {
            try
            {
                var scan = File.Exists(args[1]) ? null : Inventory.Scan(args[1]);
                var paths = scan is null ? [args[1]] : scan.Packages.Select(p => p.Ruta);
                var summary = Models.ModelReport.Build(paths);
                Models.ModelReport.Export(args[2], summary);
                Console.WriteLine($"{summary.Revisados} modelos · {summary.Leidos} leídos · {summary.Problemas.Count} no leídos"
                    + $" · {summary.Dependencias.Count} dependencias ausentes · "
                    + string.Join(" ", summary.Versiones.OrderBy(v => v.Key).Select(v => v.Key + "=" + v.Value)));
                return summary.Problemas.Count == 0 && summary.Paquetes.Count == 0 ? 0 : 2;
            }
            catch (Exception ex) { File.WriteAllText(args[2] + ".error.txt", ex.ToString()); return 1; }
        }
        if (args.Length is 2 or 3 && args[0] == "--model")
        {
            try
            {
                Models.ModelBundle bundle;
                if (args.Length == 2) bundle = Models.ModelBundle.FromFile(args[1]);
                else
                {
                    var entries = Archives.ArchiveIndex.Read(args[1]);
                    var entry = entries.FirstOrDefault(r => r.Ruta == args[2])
                        ?? throw new FileNotFoundException("No se encontró esa ruta interna en el paquete.");
                    // The package's own index resolves companions; other packages are not searched here.
                    bundle = Models.ModelBundle.FromArchive(entry, entries);
                }
                Console.WriteLine(bundle.Describe());
                return bundle.Missing.Count == 0 ? 0 : 2;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }
        }
        if (args.Length == 3 && args[0] == "--index")
        {
            try
            {
                var scan = File.Exists(args[1]) ? null : Inventory.Scan(args[1]);
                var paths = scan is null ? new[] { args[1] } : scan.Packages.Select(p => p.Ruta);
                var index = Archives.ArchiveIndex.Build(paths);
                if (scan is not null)
                    index.Errores.AddRange(scan.Errors.Select(error => new Archives.IndexError(args[1], error)));
                Archives.ArchiveIndex.Export(args[2], index);
                return index.Errores.Count == 0 ? 0 : 2;
            }
            catch (Exception ex) { File.WriteAllText(args[2] + ".error.txt", ex.ToString()); return 1; }
        }
        if (args.Length == 3 && args[0] == "--scan")
        {
            try { Inventory.Export(args[2], Inventory.Scan(args[1])); return 0; }
            catch (Exception ex) { File.WriteAllText(args[2] + ".error.txt", ex.ToString()); return 1; }
        }
        if (args.Length > 0)
        {
            Console.Error.WriteLine("Uso: --scan <carpeta> <json> | --index <carpeta/paquete> <json> | --extract <paquete> <ruta-interna> <destino>"
                + " | --model <archivo.mdl> | --model <paquete> <ruta-interna> | --models <carpeta/paquete> <json>");
            return 64;
        }
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
        return 0;
    }
}
