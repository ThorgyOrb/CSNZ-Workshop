namespace CSNZWorkshop.Models;

public sealed record CompatibilityFinding(string Level, string Area, string Detail, string Action);
public sealed record CompatibilityReport(string Status, IReadOnlyList<CompatibilityFinding> Findings)
{
    public string Describe() => Status + "\r\nPerfil de referencia: SDK GoldSrc público; no certifica compatibilidad con tu cliente de CS 1.6.\r\n\r\n"
        + string.Join("\r\n\r\n", Findings.Select(f => $"[{f.Level}] {f.Area}: {f.Detail}\r\n{f.Action}"));

    public static CompatibilityReport Analyze(ModelBundle bundle, RelatedResult? related = null, bool validateAnimation = false, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        var model = bundle.Model; var header = new ModelBytes(bundle.Main.Data);
        var findings = new List<CompatibilityFinding>();
        void Add(string level, string area, string detail, string action) => findings.Add(new(level, area, detail, action));
        void Limit(string area, int actual, int limit, string action)
        {
            if (actual > limit) Add("Revisar", area, $"{actual}; referencia del SDK: {limit}.", action);
        }
        foreach (string missing in bundle.Missing) Add("Falta", "Dependencias", missing, "Localiza el compañero antes de preparar el modelo.");
        Limit("Huesos", model.Bones.Length, 128, "Reduce el esqueleto y recompila para el cliente destino.");
        Limit("Texturas", model.Textures.Length, 100, "Revisa y reduce materiales al recompilar.");
        Limit("Grupos de cuerpo", model.Bodies.Length, 32, "Reduce grupos al recompilar.");
        Limit("Grupos de animación", model.GroupNames.Length, 16, "Reorganiza las animaciones para el cliente destino.");
        Limit("Secuencias", model.Sequences.Length, 2048, "Reduce secuencias al recompilar.");
        Limit("Controladores", header.Int(148), 8, "Revisa los controladores del esqueleto.");
        foreach (var body in model.Bodies)
        {
            Limit("Submodelos de " + body.Name, body.Variants.Length, 32, "Reduce variantes del grupo.");
            foreach (var part in body.Variants)
            {
                Limit("Vértices de " + part.Name, part.Vertices.Length, 2048, "Reduce o divide la malla; revisa el compilador destino.");
                Limit("Triángulos de " + part.Name, part.Triangles.Length, 20000, "Reduce la geometría y prueba en el cliente destino.");
            }
        }
        if (model.Textures.Any(t => (t.Flags & ~127) != 0)) Add("Revisar", "Materiales", "Hay flags ajenos al conjunto clásico.", "Adapta los materiales de Nexon y recompila si es necesario.");
        if (model.Textures.Any(t => (t.Flags & 0x72) != 0)) Add("Revisar", "Apariencia", "Hay materiales chrome, alfa, aditivos o enmascarados.", "Comprueba transparencia y brillo en el juego; el visor aproxima su aspecto.");
        if (model.Textures.Length == 0 && model.Bodies.Length > 0) Add("Revisar", "Texturas", "El modelo no tiene texturas disponibles.", "Revisa el compañero T.mdl y su tabla de materiales.");
        foreach (var ev in model.Events.Where(e => e.Frame < 0 || e.Frame >= model.Sequences[e.Sequence].Frames))
            Add("Revisar", "Evento fuera de animación", $"Secuencia {ev.Sequence}, evento {ev.Code}, fotograma {ev.Frame}.", "Corrige el fotograma del evento al recompilar.");
        if (model.Events.Any(e => e.Code != 5004)) Add("Revisar", "Eventos", "Hay eventos cuya semántica no se comprueba automáticamente.", "Contrasta sus códigos y opciones con el arma o plugin destino.");
        if (model.Sequences.Any(s => s.Fps == 0)) Add("Revisar", "Velocidad", "Hay secuencias con 0 FPS.", "Comprueba si deben permanecer estáticas o corrige su velocidad.");
        if (model.Sequences.Any(s => s.Blends > 1)) Add("Revisar", "Blends", "La comprobación de poses cubre solamente el primer blend.", "Prueba las mezclas de animación adicionales en el cliente.");
        if (related is null) Add("Pendiente", "Sonidos externos", "No se consultó la biblioteca para resolver sonidos de eventos.", "Abre «Relacionados y CS 1.6» desde la biblioteca.");
        else
        {
            foreach (var item in related.Items.Where(i => i.Reference && i.Entry is null && i.Kind.StartsWith("Sonido")))
                Add("Falta", "Sonido de evento", item.Path, "Busca el sonido o cambia la referencia al adaptar el modelo.");
            foreach (var group in related.Items.Where(i => i.Reference && i.Entry is not null).GroupBy(i => (i.Kind, i.Path)))
                if (group.Count() > 1) Add("Revisar", "Origen ambiguo", group.Key.Path, "Elige una de las versiones listadas en Recursos relacionados.");
        }
        if (validateAnimation && bundle.Missing.Count == 0)
        {
            long work = model.Sequences.Sum(s => (long)s.Frames * model.Bones.Length);
            if (work > 5000000) Add("Pendiente", "Animaciones", "El modelo supera el presupuesto de comprobación de poses.", "Revisa las animaciones manualmente en el visor.");
            else
            {
                try
                {
                    for (int s = 0; s < model.Sequences.Length; s++)
                        for (int frame = 0; frame < model.Sequences[s].Frames; frame++)
                        { token.ThrowIfCancellationRequested(); _ = model.Pose(s, frame); }
                    Add("Comprobado", "Poses", "Se leyeron todos los fotogramas del primer blend.", "Falta comprobar el comportamiento dentro del juego.");
                }
                catch (InvalidDataException ex) { Add("Revisar", "Datos de animación", ex.Message, "Repara o recompila la secuencia antes de preparar la copia."); }
            }
        }
        else Add("Pendiente", "Poses", "No se han comprobado todos los fotogramas en este diagnóstico.", "Ejecuta el análisis desde la biblioteca; resuelve primero los compañeros faltantes.");
        string filename = Path.GetFileName(bundle.Main.Name);
        Add("Pendiente", "Uso en CS 1.6", filename.StartsWith("v_", StringComparison.OrdinalIgnoreCase)
            ? "Posible arma en primera persona, inferido por el prefijo v_." : "No se ha definido el recurso del mod que este modelo reemplazará.",
            "Compara orden e índices de secuencias, disparo, recarga, attachments e hitboxes con el modelo destino. Los nombres de animación no bastan.");
        if (bundle.Main.OriginalVersion is 20 or 21) Add("Información", "Cifrado", "El MDL fue descifrado a versión 10 en memoria.", "Usa Preparar copia; descifrar no adapta límites ni eventos de Nexon.");
        string status = findings.Any(f => f.Level == "Falta") ? "Faltan recursos"
            : findings.Any(f => f.Level == "Revisar") ? "Requiere revisión para CS 1.6" : "Sin incidencias detectadas en las comprobaciones realizadas; pendiente de prueba";
        return new(status, findings);
    }
}
