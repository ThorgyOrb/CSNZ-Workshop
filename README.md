# CSNZ Workshop

Biblioteca de paquetes y recursos de CSNZ para Windows en C# / .NET 9. Los lectores NAR y PAK están integrados: no ejecuta extractores externos. Utiliza SharpZipLib 1.4.2 para BZip2, restaurado mediante NuGet y conservado en `artifacts/packages`.

## Iniciar

Haz doble clic en `Iniciar.cmd`. Requiere el SDK de .NET 9 (ya disponible en este equipo).

1. Selecciona la carpeta `Data` de CSNZ; la ruta habitual viene preconfigurada.
2. Pulsa **Escanear**.
3. Busca por nombre/ruta o filtra NAR / PAK.
4. Selecciona un paquete y pulsa **Ver recursos**, o haz doble clic en él.
5. Usa **Biblioteca completa** para reunir los recursos de todos los paquetes detectados, independientemente del filtro de paquetes.
6. Busca por nombre, ruta o paquete de origen. Filtra modelos, sonidos, sprites, mapas o texturas.
7. Selecciona un recurso para ver tamaños declarados, offset, flags y coincidencias de su ruta.
8. **Exportar índice JSON** guarda todos los recursos y errores de la biblioteca abierta. Los filtros no limitan la exportación.
9. Selecciona un `.mdl` y pulsa **Ver modelo 3D**, o haz doble clic sobre él, para abrirlo en el visor.

## Extraer recursos

1. En la biblioteca, selecciona uno o varios archivos con Ctrl/Mayús.
2. Pulsa **Extraer selección**. Para extraer todos los resultados de la búsqueda actual, usa **Extraer filtrados**.
3. Elige una carpeta fuera de la instalación del juego. El selector indica la cantidad y el tamaño total declarado.
4. Espera el resumen. Puedes cancelar; los archivos ya terminados se conservan y el archivo en curso no se publica.

Se guarda un informe `informe-*.json` con cada resultado, errores, rutas, tamaños y hashes SHA-256. Los archivos existentes se omiten sin sobrescribirlos. Los resultados mantienen sus carpetas internas bajo una carpeta por paquete; el sufijo corto identifica su ruta de origen, para separar paquetes de igual nombre:

```text
Destino/
  cstrike.nar_47A49DBC/
    cstrike/models/aimtracer.mdl
  common_00004.pak_<identificador>/
    lstrike/common/models/...
  informe-....json
```

Se rechazan rutas que salgan del destino, nombres reservados de Windows y enlaces/uniones de carpetas detectados. La escritura usa un archivo temporal y publica el resultado solo después de terminar las comprobaciones. Cada recurso se limita a 512 MiB guardados y 512 MiB de salida. Los paquetes originales se abren solo para lectura y se vuelve a comprobar el índice antes de extraer.

El inventario lee hasta 32 bytes por paquete; la biblioteca lee y decodifica sus índices. Los paquetes se abren únicamente en lectura y no se cargan completos en memoria. Las carpetas enlazadas no se recorren. Se conserva cada entrada con su paquete de origen, incluso si hay rutas repetidas. No se decide qué versión tiene prioridad.

La biblioteca se carga en segundo plano y puede cancelarse. Cancelar descarta el índice de esa operación; vuelve a abrirla para reintentar. Conserva los nombres originales de los PAK: se usan para derivar sus claves.

## Ver modelos

Selecciona un recurso `.mdl` en la biblioteca y pulsa **Ver modelo 3D**, o haz doble clic sobre él. En la ventana principal, **Abrir modelo…** abre un `.mdl` ya extraído junto con los archivos que estén a su lado en la misma carpeta.

El visor descifra los modelos en memoria: las versiones 20 y 21 de Nexon se convierten con ICE al formato clásico versión 10, sin tocar el paquete original. Busca los archivos compañeros dentro de la misma biblioteca: la textura `…T.mdl` cuando el modelo no la incrusta y los grupos de animación `…01.mdl`, `…02.mdl`. Si falta alguno lo indica en el diagnóstico y sigue mostrando la geometría.

- Arrastra para girar, rueda para acercar y **Centrar** para volver a la vista inicial.
- Elige una animación y pulsa **Reproducir**, o mueve el fotograma a mano. El encuadre se calcula con toda la animación elegida, así que el zoom no cambia mientras se reproduce ni se pierden las animaciones que desplazan el modelo.
- **Malla** dibuja el alambre; los desplegables cambian skin, grupo de cuerpo y submodelo.
- La pestaña **Texturas** muestra cada textura con su tamaño. **Diagnóstico** resume huesos, hitboxes, avisos de límites y dependencias.

**Preparar copia para prueba** valida el primer blend de todas las animaciones y guarda el modelo descifrado, sus compañeros y un `preparacion.json` con hashes SHA-256 en una carpeta nueva fuera del juego. No instala nada ni convierte el modelo: queda pendiente probarlo en CS 1.6.

El visor aproxima materiales e iluminación con un rasterizador propio, sin GPU. Muestra el primer blend de cada animación, no aplica controladores de huesos ni los materiales adicionales de Nexon, y no reduce polígonos ni huesos para los límites del motor clásico.

## Alcance actual

Esta versión centraliza paquetes y **listados de recursos internos**. Valida firmas/versiones, decodifica el índice NAR con XOR/BZip2 y descifra el índice PAK v2. Comprueba longitudes y rangos dentro del paquete. Limita los índices a 64 MiB y las rutas a 16,384 bytes NAR / caracteres PAK.

La extracción soporta NAR sin cifrado, con XOR y con XOR/LZ; verifica el tamaño y el CRC de los bytes almacenados. Para PAK soporta recursos sin compresión y sus capas de cifrado, incluida la última palabra de archivos pequeños con relleno. Comprueba el tamaño y genera SHA-256, pero no implementa un CRC de contenido PAK. Reconoce cabeceras MDL, WAV y SPR como diagnóstico básico, no como validación completa.

El visor lee modelos MDL versión 10 y descifra en memoria las versiones 20 y 21, cuyo cifrado es independiente del cifrado del paquete. Comprueba huesos, mallas, texturas y animaciones antes de dibujar. **No convierte modelos para CS 1.6:** la copia preparada conserva el formato descifrado y hay que probarla en el cliente. La versión 9 y otros formatos no se convierten, y los grupos de animación `IDSQ` solo se abren desde su modelo principal.

La compresión de contenido PAK (flag 1) se descomprime con zlib. El módulo de archivos del juego, `Bin/FileSystem_Nar.dll`, enlaza zlib 1.2.11 —aparecen los mensajes de `inflate` y `deflate` y no hay rastro de LZMA, LZ4, zstd ni bzip2—, así que el bloque empaquetado se lee como flujo zlib y, si no trae esa cabecera, como deflate sin cabecera. La salida debe coincidir exactamente con el tamaño declarado y el flujo debe terminar limpio, lo que además valida el Adler-32. Un contenido gzip o bzip2 se informa como no implementado en lugar de adivinarse.

**Esta ruta no está comprobada con datos reales:** en el inventario local las 31,026 entradas PAK usan flag 6, sin compresión, así que solo se valida con bloques sintéticos construidos con el mismo orden de capas. Si aparece un paquete con flag 1 y falla, el mensaje indica en qué punto (cabecera, tamaño o final del flujo). Los recursos se clasifican por extensión.

## Validación realizada

La instalación local examinada contiene **623 paquetes** (15 NAR y 608 PAK), **81,216 entradas** y **10,654 entradas `.mdl`**. Todos los índices se leyeron sin errores. Estas cifras no garantizan modelos únicos ni utilizables directamente.

El informe por lotes (`--models`) leyó **10,640 de las 10,654 entradas `.mdl`** en 18 segundos: 10,518 en versión 10, 36 en versión 20 y 86 en versión 21; las 122 cifradas se descifraron y se leyeron enteras. El informe no dibuja: las comprobaciones cubren el dibujado con seis modelos reales. De las 14 restantes, 13 son grupos de animación `IDSQ`, que son archivos compañeros y no modelos sueltos, y una es un MDL versión 9 de `valve.nar`. Cuatro modelos declaran compañeros que no están en su mismo paquete.

Pasaron **69 comprobaciones**, 63 de ellas sin la carpeta del juego, incluidas tres pruebas de carga de la interfaz. Cubren índices, NAR sintéticos, decodificación XOR/LZ, copia solapada y vuelta de ventana, CRC incorrecto, rutas peligrosas, archivos truncados, conservación de archivos existentes, cancelación e informes. Para los modelos cubren MDL sintéticos con huesos, mallas, texturas y animaciones RLE, cada byte de truncamiento, veintiuna cabeceras dañadas, compañeros `T.mdl` y grupos externos, el descifrado ICE de ida y vuelta en las versiones 20 y 21, bloques cifrados solapados, el encuadre de animaciones que desplazan el modelo, el informe por lotes y la preparación de copias.

Para la compresión PAK hay nueve comprobaciones con bloques sintéticos: zlib con las cuatro combinaciones de capas de cifrado, deflate sin cabecera, recursos de 1 a 1027 bytes con el relleno de la última palabra, un megabyte incompresible, tamaños declarados que no cuadran, bloques truncados o alterados, contenedores gzip y bzip2, y la cancelación. Las entradas sin comprimir se comprueban igual para confirmar que la ruta anterior no cambió.

La integración opcional extrae siete muestras reales de modelos, sprites, sonidos, texturas y WAD, e incluye una textura PAK pequeña de 546 bytes para comprobar el relleno final. Además abre seis modelos reales, dos de ellos cifrados (versiones 20 y 21), y comprueba que se dibujan. Estas muestras se validan contra la instalación examinada; una actualización del juego puede cambiar sus rutas o datos.

```powershell
dotnet build .\CSNZWorkshop.Checks\CSNZWorkshop.Checks.csproj -c Release
dotnet .\CSNZWorkshop.Checks\bin\Release\net9.0-windows\CSNZWorkshop.Checks.dll 'C:\Program Files (x86)\Steam\steamapps\common\CSNZ\Data'
```

## Compilar y escanear sin interfaz

```powershell
dotnet build .\CSNZWorkshop\CSNZWorkshop.csproj -c Release
& .\CSNZWorkshop\bin\Release\net9.0-windows\CSNZWorkshop.exe --scan 'C:\Program Files (x86)\Steam\steamapps\common\CSNZ\Data' '.\inventario-csnz.json'
dotnet .\CSNZWorkshop\bin\Release\net9.0-windows\CSNZWorkshop.dll --index 'C:\Program Files (x86)\Steam\steamapps\common\CSNZ\Data' '.\recursos-csnz.json'
dotnet .\CSNZWorkshop\bin\Release\net9.0-windows\CSNZWorkshop.dll --extract 'C:\Program Files (x86)\Steam\steamapps\common\CSNZ\Data\cstrike.nar' '/cstrike/models/aimtracer.mdl' '.\Extraidos'
```

`--index` acepta una carpeta o un paquete individual. Devuelve 0 sin errores, 2 si hay paquetes o carpetas no leídos y 1 si falla la operación. Los errores por paquete están en el JSON; los generales, en un archivo adicional `.error.txt`.

`--extract` extrae una ruta interna exacta del paquete indicado y guarda un informe en el destino. Devuelve 0 sin errores (incluidas omisiones por archivos existentes), 2 si falló algún recurso y 1 si falló la operación; los errores generales se escriben en stderr. Argumentos no reconocidos devuelven 64, sin abrir la interfaz.

```powershell
dotnet .\CSNZWorkshop\bin\Release\net9.0-windows\CSNZWorkshop.dll --model 'C:\Program Files (x86)\Steam\steamapps\common\CSNZ\Data\cstrike.nar' '/cstrike/models/v_ak47red.mdl'
dotnet .\CSNZWorkshop\bin\Release\net9.0-windows\CSNZWorkshop.dll --models 'C:\Program Files (x86)\Steam\steamapps\common\CSNZ\Data' '.\modelos-csnz.json'
```

`--model` imprime el diagnóstico de un modelo: acepta un `.mdl` del disco o una ruta interna de un paquete, y en ese caso busca los compañeros dentro del mismo paquete. Devuelve 0 si está completo, 2 si falta algún compañero y 1 si no se pudo leer.

`--models` revisa todos los `.mdl` de una carpeta o de un paquete y guarda un informe JSON con versiones, modelos no leídos, dependencias ausentes y paquetes ilegibles. Abre cada paquete una sola vez y no escribe ningún modelo. Devuelve 0 sin incidencias, 2 si algún modelo o paquete no se pudo leer y 1 si falla la operación.

## Código y licencias

`Inventory.cs` contiene el escáner; `Archives/`, los lectores y el cifrado de paquetes; `Models/`, el descifrado ICE, el lector MDL, el rasterizador y el informe por lotes; `MainForm.cs`, `ResourceForm.cs` y `ModelForm.cs`, la interfaz. `CSNZWorkshop.Checks/` contiene las comprobaciones sin framework de pruebas adicional.

La aplicación incorpora adaptaciones de NARTools y CSO-Paker bajo GPL, y SharpZipLib bajo MIT. Consulta `THIRD_PARTY.md`, `LICENSE` y `licenses/SharpZipLib-MIT.txt`. No necesita los repositorios de referencia para compilar y no incluye recursos del juego.
