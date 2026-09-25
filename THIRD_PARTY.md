# Créditos y licencias

CSNZ Workshop integra código adaptado de proyectos GPL. La aplicación combinada se distribuye bajo **GPL-3.0**, cuyo texto está en `LICENSE`. Los archivos de recursos del juego no forman parte del proyecto.

## NARTools

- Fuente: https://github.com/SmilexGamer/NARTools
- Revisión consultada: `3a696940d9f174ce6648766611706b28f0b8eb55`.
- Copyright © 2010 Da_FileServer. El proyecto también acredita a omamfan por correcciones de compresión.
- Licencia de los archivos consultados: GPL-3.0-or-later.
- `Archives/NarReader.cs` adapta el formato de índice, constantes XOR y estructura de entradas de `NexonArchive.cs` y `NexonArchiveFileEntry.cs`. Se reescribió la lectura con validaciones de límites y cancelación; no se integra su ejecutable ni su DLL antigua.
- `Archives/ResourceDecoder.cs` adapta la clave XOR de `NexonArchiveFileDecoderStream.cs` y los comandos LZ de `NexonArchiveFileDecompressStream.cs`. Implementa un búfer circular de 8 KiB, límites de salida, comprobación de CRC y cancelación.
- `Models/Ice.cs` es una copia de `NARToolLib/Ice.cs`, la implementación del cifrado ICE incluida en NARTools, con el espacio de nombres adaptado y sin más cambios.
- `Models/ModelCrypt.cs` adapta de `NARToolLib/ModelHelper.cs` las claves ICE de las versiones 20 y 21 y la selección de bloques cifrados (píxeles de textura y vértices, en trozos de 1024 bytes). Añade validación de rangos, rechazo de bloques solapados y la comprobación posterior con el lector MDL antes de mostrar o preparar el modelo.

## CSO-Paker

- Fuente: https://github.com/overl4y/CSO-Paker
- Revisión: `ef187768d02fec1eec0d3cee298e54f6962ede64`.
- El repositorio acredita a Luís Leite como creador (https://git.sr.ht/~leite/cso-pak); fork mantenido por overl4y.
- Licencia: GPL-3.0.
- `Archives/SnowCipher.cs` es un port a C# de `sources/snow.cpp`, con arrays administrados, lectura de palabras y preservación de la extensión de signo del algoritmo original.
- `Archives/PakReader.cs` adapta generación de claves, cabecera y entradas de `pakfile.cpp`/`pakfile.hpp`. La lectura se realiza mediante un stream con validación de rangos y sin cargar el paquete completo.
- `Archives/ResourceDecoder.cs` adapta las claves por recurso y las dos pasadas de descifrado de `pakfile.cpp`. Usa lectura por bloques y corrige la última palabra con relleno en recursos menores de 1024 bytes. La descompresión de contenido PAK (flag 1), que `pakfile.cpp` deja sin implementar, es propia de CSNZ Workshop: usa el inflater de SharpZipLib sobre el bloque ya descifrado.
- `tools/port-snow.cjs` reproduce el port desde una copia del repositorio en `artifacts/reference-pak`. No se necesita para compilar ni ejecutar la aplicación.

## SharpZipLib 1.4.2

- Fuente: https://github.com/icsharpcode/SharpZipLib
- Paquete NuGet: https://www.nuget.org/packages/SharpZipLib/1.4.2
- Licencia: MIT. Texto en `licenses/SharpZipLib-MIT.txt`.
- Biblioteca integrada para descomprimir BZip2; no es un programa extractor externo. La primera restauración necesita descargar el paquete; después se utiliza la copia local de `artifacts/packages`.

## NLayer 3.0.0

- Fuente: https://github.com/naudio/NLayer
- Paquete NuGet: https://www.nuget.org/packages/NLayer/3.0.0
- Revisión del paquete: `046c7ce422970f8f0f0bc205d6c6341bcb7debf1`.
- Licencia: MIT. Texto en `licenses/NLayer-MIT.txt`.
- Decodificador MP3 integrado para la vista previa. Genera PCM temporal; la extracción conserva el MP3 original. No requiere un reproductor externo ni el controlador MCI MPEG de Windows.

## Pfim 0.11.4

- Fuente: https://github.com/nickbabcock/Pfim
- Paquete: https://www.nuget.org/packages/Pfim/0.11.4
- Revisión: `0a00d8152ca4dc0e0a5821c6b8fd5261fca1e84d`.
- Copyright: Nick Babcock. Licencia MIT, texto en `licenses/Pfim-MIT.txt`.
- Decodifica vistas previas TGA y DDS; se limita el tamaño de imagen y el presupuesto de asignación. El lector WAD3 es propio, a partir de las estructuras de archivo descritas en https://github.com/ValveSoftware/halflife/blob/master/utils/common/wadlib.h.

El lector MDL (`Models/StudioModel.cs`), el rasterizador (`Models/ModelRenderer.cs`), el visor y el informe por lotes son código propio de CSNZ Workshop, escritos a partir del formato público `studiomodel` de GoldSrc.

El lector `Media/SpriteClip.cs` es una implementación propia del formato IDSP v2 documentado por Valve en https://github.com/ValveSoftware/halflife/blob/master/utils/sprgen/spritegn.h y https://github.com/ValveSoftware/halflife/blob/master/devtools/image_to_spr.py. No se incorpora código de esos archivos. La salida de audio utiliza la API MCI de Windows (`winmm.dll`): https://learn.microsoft.com/en-us/previous-versions/dd757161(v=vs.85).

Las modificaciones y los nuevos lectores de CSNZ Workshop se realizaron en 2026. Se conservan las licencias de los componentes originales. Al redistribuir la aplicación, incluye estos avisos y el código fuente correspondiente conforme a GPL-3.0.

`Models/CompatibilityReport.cs` utiliza como perfil orientativo las constantes públicas de https://github.com/ValveSoftware/halflife/blob/master/engine/studio.h. `Models/RelatedResources.cs` reconoce el significado del evento de sonido 5004 descrito en https://github.com/ValveSoftware/halflife/blob/master/cl_dll/entity.cpp. Ambas implementaciones son propias y no incorporan código del SDK. El perfil no afirma compatibilidad garantizada con CS 1.6.
