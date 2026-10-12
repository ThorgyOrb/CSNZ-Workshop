# CSNZ Workshop → Unity

## Uso

1. Abre el modelo MDL en el visor de CSNZ Workshop y pulsa **Exportar para Unity…**.
2. Ajusta **Metros por unidad** si es necesario (predeterminado: `0.0254`). Elige una carpeta fuera de la instalación del juego. El exportador crea una carpeta nueva con `model.json`, `textures/texture_0000.png`, etc., y `export-report.json`. Incluye todas las skins, variantes y blends; requiere los compañeros de texturas y animaciones.
3. Copia esta carpeta `unity` completa a `Assets/CSNZWorkshop` de tu proyecto. `Runtime` contiene los datos, el selector de skins/variantes y el shader; `Editor` contiene el importador. `Tests` es opcional.
4. En Unity usa **Tools > CSNZ Workshop > Import model.json** y elige el JSON exportado; puede estar fuera del proyecto.
5. El importador crea una carpeta nueva en `Assets/CSNZImported/Model`, copia las texturas y los metadatos y guarda mallas, materiales, avatar Generic, clips, Animator Controller y `model.prefab`. Cada importación crea assets independientes.
6. Arrastra el prefab a la escena. Su componente `CSNZModelInstance` permite elegir `skin` y un índice por grupo en `variants`. También puedes usar `SelectSkin(index)` y `SelectVariant(body, index)` desde código.

Las referencias externas de Nexon (`#…bmp`, con rellenos MDL de 4×1 píxeles) se resuelven antes de exportar. Los PNG contienen los píxeles del BMP real y el JSON usa sus dimensiones reales para las UV. `source` identifica el recurso original y `sha256` permite al importador comprobar cada PNG. No se exportan referencias externas sin resolver. El importador conserva también dimensiones que no sean potencias de dos y rechaza archivos ausentes, alterados o con dimensiones distintas de las declaradas.

El controlador tiene un estado por clip, identificado como `seq_0000_blend_00`. Usa `Animator.Play("seq_0000_blend_00")` para seleccionarlo. El nombre original está en los metadatos. Los blends se guardan como clips independientes; no se crea un Blend Tree ni se inventan transiciones. No se crean eventos ejecutables a partir de códigos Nexon.

## Solo arma: conservar animaciones y quitar brazos/manos

En **Exportar para Unity…**, pulsa **Solo arma (sugerido)** para marcar las secciones identificadas como brazos/manos por sus nombres de grupo, submodelo o material. La lista marca lo que se elimina. Puedes modificar la selección por grupo, variante y material, comprobar todas las skins/variantes y reproducir las secuencias en la vista previa. **Modelo completo** restaura la selección. La exportación rechaza una selección que quite toda la geometría.

También puedes exportar el modelo completo y elegir en Unity **Tools > CSNZ Workshop > Import weapon only (keep animations)**. Esta opción utiliza la información `geometrySections` de los paquetes exportados con la versión actual de CSNZ Workshop. Si no puede identificar brazos/manos, pide seleccionar la geometría manualmente en la herramienta. Los paquetes antiguos deben volver a exportarse.

Ambas rutas eliminan solo geometría y conservan todos los huesos, sus IDs, padres, poses y muestras de animación, incluidos los huesos de brazos que también puedan mover el arma. El importador compacta los vértices usados y mantiene las variantes vacías como objetos sin renderer. Los nombres solo aportan sugerencias: no identifican con certeza brazos y arma cuando comparten material dentro de un mismo submodelo. En ese caso esta primera versión necesita una separación adicional de la malla; no corta triángulos por proximidad ni borra huesos automáticamente.

Se verificó `v_guillotine.mdl` con sus dos variantes de manos eliminadas, **56 huesos y 9 clips** conservados. `shoot1`, `catch` y `lost` permanecen disponibles. Esto prepara el modelo para la siguiente etapa del mod; no implementa el lanzamiento, daño, trayectoria o regreso de Guillotine en 7 Days to Die.

## Conversión y formato v1

La geometría se convierte una sola vez durante la exportación. No cambies ejes o escala otra vez en el importador:

- GoldSrc `(X,Y,Z)` → Unity `(-Y,Z,X)`: avance +Z, derecha +X, arriba +Y. Esta conversión invierte la orientación, por lo que también se invierte el orden de cada triángulo.
- Posiciones en metros; vectores normales unitarios; cuaterniones en orden `x,y,z,w`.
- Vértices y normales llevados al espacio común del modelo mediante la pose global de referencia. Cada normal usa su propio hueso de origen.
- Vértices separados cuando cambia el índice de normal, UV o textura. UV `(s/width, 1-t/height)`, normalizados para cada textura de cada skin; las skins pueden tener dimensiones diferentes.
- Peso rígido `1.0` por vértice. El importador calcula las bind poses respecto de la raíz del renderer usando `bone.worldToLocalMatrix * root.localToWorldMatrix` ([Unity: Mesh.bindposes](https://docs.unity.com/en-us/engine/6000.0/script-reference/unityengine/mesh/bindposes)).
- Poses locales de cada hueso en cada fotograma y blend; tiempo `frame/FPS`, continuidad de signo del cuaternión, indicador de loop original. Se omiten secuencias con FPS cero, indicadas en el informe. La duración del clip es `(frames-1)/FPS`; no se añade un fotograma de cierre artificial.
- Los arrays de posiciones están aplanados como `xyz`; los de rotaciones como `xyzw`. Las animaciones usan orden fotograma → hueso → componente.
- IDs numéricos para huesos, texturas y attachments; objetos y archivos generados usan estos IDs. Los nombres originales, que pueden repetirse o contener separadores, solo se guardan como metadatos.
- Attachments: posición local respecto de su hueso y tres vectores locales convertidos. El importador usa el eje X original como forward y el eje Z original como up; si son degenerados usa rotación identidad y avisa. Los nueve valores originales convertidos permanecen en el JSON.
- Eventos conservan secuencia, fotograma, código y opciones. Flags de secuencias/texturas, relaciones de skins y variantes permanecen disponibles.

## Alcance y comprobación

El código está dirigido a **Unity 2022.3 LTS o posterior con Built-in Render Pipeline**. El shader unlit incluido aproxima materiales opacos, máscara alfa y aditivos (incluido el prefijo Nexon `$0b`). Para URP/HDRP sustituye los materiales por shaders de tu pipeline. No se reproducen chrome, iluminación GoldSrc, partículas Nexon, controladores de huesos ni lógica del juego.

El importador comprueba el pipeline y los errores de compilación del shader antes de crear materiales. La ruta incluida requiere Built-in y detiene la importación en URP/HDRP con un mensaje, para evitar un prefab con materiales rosas.

Para un mod de **7 Days to Die**, utiliza el proyecto, la versión de Unity y los shaders correspondientes a la versión del juego que vas a modificar. Este paquete prepara los assets y sus texturas; todavía necesitas construir el AssetBundle y configurar el arma en el mod. `CSNZModelInstance` es un componente de herramientas propio: el juego no lo incorpora automáticamente. Antes de empaquetar el prefab para el juego, fija la skin/variante deseada y retira ese componente, o integra su código explícitamente en tu mod. Las texturas y materiales tienen referencias persistentes en los renderers y no dependen de ese componente.

Unity aplica los pesos del vértice también a su normal. Si el MDL asigna una normal a otro hueso, la pose de referencia se conserva, pero la normal animada puede diferir. `normalBoneIndices` conserva esa asociación original y el informe cuenta las esquinas afectadas. El shader unlit evita que esa diferencia cambie la iluminación; requiere atención al cambiar a materiales iluminados.

Las mallas de todas las skins y variantes se guardan por separado para respetar UV y dimensiones distintas. Solo se activa una variante por grupo y skin. Se usa `updateWhenOffscreen` para evitar recortes por bounds de la pose de referencia; revisa su coste y ajusta bounds animados si necesitas optimización.

Prueba un arma asimétrica: comprueba el lado del cerrojo, la orientación del cañón (+Z), transparencia, UV, tamaño, disparo y recarga. Este exportador no adapta el modelo a 7 Days to Die ni genera sus configuraciones.

Las comprobaciones .NET se ejecutan con:

```powershell
dotnet run --project CSNZWorkshop.Checks
```

Para generar un paquete sintético con dos skins, dos variantes y una animación:

```powershell
dotnet run --project CSNZWorkshop.Checks -- --unity-fixture artifacts/unity-fixture
```

También existe una utilidad de diagnóstico para exportar un MDL local:

```powershell
dotnet run --project CSNZWorkshop.Checks -- --export-unity C:/modelos/arma.mdl artifacts/unity-export
```

Con una licencia activa de Unity, copia los scripts a un proyecto de prueba y ejecuta el editor en batch con `-executeMethod CSNZUnityBatchChecks.Run --csnz-package <ruta/model.json>`. La comprobación de integración importa el paquete, recarga el prefab, verifica assets persistentes, bind poses, rig Generic, muestras de animación, skins y variantes. La licencia del editor es necesaria para ejecutar esta prueba.

Añade `--csnz-weapon-only` para comprobar la eliminación de brazos al importar y confirmar que las muestras de animación permanecen idénticas. Para el caso Guillotine puedes generar los paquetes completo y filtrado con `dotnet run --project CSNZWorkshop.Checks -- --guillotine-weapon-check <ruta/v_guillotine.mdl> artifacts/guillotine-weapon-check`, teniendo sus BMP externos en una carpeta `texture` junto al MDL.
