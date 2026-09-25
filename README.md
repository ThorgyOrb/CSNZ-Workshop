# CSNZ Workshop

Biblioteca de paquetes y recursos de CSNZ para Windows en C# / .NET 9. Los lectores NAR y PAK están integrados: no ejecuta extractores externos. Utiliza SharpZipLib 1.4.2 para BZip2, NLayer 3.0.0 para MP3 y Pfim 0.11.4 para TGA/DDS, restaurados mediante NuGet y conservados en `artifacts/packages`.

## Iniciar

### Sesión, caché y navegación

La aplicación recuerda la carpeta del juego, búsqueda y categoría, vista de galería/lista, último destino de extracción, tema y favoritos. Los datos se guardan en `%LOCALAPPDATA%\CSNZWorkshop`; las pruebas usan carpetas temporales independientes. Al abrir la aplicación se detectan los paquetes de la carpeta recordada si existe.

Al abrir una biblioteca se recupera el índice de cada paquete cuyo tamaño, fecha de modificación y creación siguen iguales. Los nuevos o modificados se vuelven a leer. **Actualizar índices** fuerza una lectura completa para resolver cualquier duda sobre una actualización del juego. La extracción y las vistas previas siguen releyendo el índice original antes de acceder al recurso; la caché no reemplaza esa verificación. Una caché ilegible se reconstruye.

La barra lateral permite elegir categorías. **Alternar favoritos** añade o quita los recursos seleccionados y **Solo favoritos** filtra la biblioteca. Las últimas doce búsquedas se ofrecen como sugerencias al escribir. **Tema oscuro**, en la ventana principal, se aplica también a los visores y diálogos de la aplicación; los selectores nativos conservan el tema de Windows.

### Comparar con el modelo de tu mod

En el visor o la vista previa de un MDL, pulsa **Comparar con modelo…** y elige un MDL local de CS 1.6. Sus compañeros se buscan junto al archivo. La comparación muestra ambos modelos con el mismo encuadre, escala y origen; permite elegir una secuencia distinta por lado, girar ambos modelos y comparar fotogramas por fase relativa. La reproducción recorre un ciclo normalizado de cinco segundos, no sus FPS originales. Se muestra la primera skin, variante y blend; no modifica ni adapta ninguno de los archivos.

La segunda pestaña compara las secuencias por índice, sus nombres, fotogramas, FPS y blends; además lista huesos, padres, posiciones, hitboxes y attachments. Las diferencias se presentan como datos para revisión: no se afirma que dos modelos sean intercambiables.

### Exportar para un mod

**Exportar para mod…**, disponible para la selección de biblioteca y para una colección, muestra las rutas de salida antes de escribir. Conserva las carpetas internas desde `models`, `sound`, `sprites`, `maps` o `gfx`, y coloca los WAD en la raíz elegida. Las rutas sin una carpeta reconocida requieren la extracción original. Los conflictos quedan sin marcar: elige una versión por destino. La operación rechaza destinos duplicados, omite archivos existentes, permite cancelar y genera un informe.

Esta exportación organiza los bytes originales: no descifra automáticamente el interior de los MDL ni reúne sus dependencias. Usa **Preparar para mi mod** para obtener copias descifradas con compañeros antes de probarlas. Elige una carpeta de trabajo para revisar el resultado; la exportación no instala el mod.

### Colecciones y texturas

La ventana de colecciones incluye vista previa de cada recurso, **Subir/Bajar** para ordenar una selección y **Localizar paquete…** para corregir la ubicación de un paquete movido. La reubicación solo se acepta si todas sus referencias aparecen una vez en el paquete elegido y no crea duplicados. Guarda la colección después para conservar el nuevo orden y ubicación.

Las vistas previas admiten **TGA y DDS** mediante Pfim, además de BMP. Para **WAD3**, un selector permite recorrer sus texturas miptex y qpic sin compresión y guardar la textura seleccionada como PNG. También puedes guardar texturas individuales desde la pestaña **Texturas** del visor MDL. WAD2 necesita una paleta externa y no se previsualiza; entradas WAD comprimidas, otros tipos de lump y texturas HDR muestran un aviso. Los DDS muestran la superficie principal, no todos sus mipmaps ni caras. Las dimensiones de vista previa se limitan a 4096 × 4096 y los archivos de textura a 64 MB (BMP: 16 MB).

Haz doble clic en `Iniciar.cmd`. Requiere el SDK de .NET 9 (ya disponible en este equipo).

1. Selecciona la carpeta `Data` de CSNZ; la ruta habitual viene preconfigurada.
2. Pulsa **Escanear**.
3. Busca por nombre/ruta o filtra NAR / PAK.
4. Selecciona un paquete y pulsa **Ver recursos**, o haz doble clic en él.
5. Escribe un nombre o ruta de recurso en el buscador inicial y pulsa **Enter** o **Buscar en todos**. Busca en todos los paquetes detectados, independientemente del filtro de paquetes; con el buscador vacío abre la biblioteca completa. **Filtrar paquetes** solo filtra el inventario.
6. Busca por nombre, ruta o paquete de origen. Filtra modelos, sonidos, sprites, mapas o texturas.
7. Selecciona un recurso para ver tamaños declarados, offset, flags y coincidencias de su ruta.
8. **Exportar índice JSON** guarda todos los recursos y errores de la biblioteca abierta. Los filtros no limitan la exportación.
9. Selecciona un `.mdl` y pulsa **Ver modelo 3D**, o haz doble clic sobre él, para abrirlo en el visor.

Al abrir **Ver recursos** de un paquete, se carga el índice de todos los paquetes detectados y se activa **Solo paquete abierto** para limitar la lista. Desmarca esa opción para ampliar la búsqueda. Los relacionados y la resolución de compañeros usan siempre el índice completo, aunque la lista esté filtrada por paquete, texto, categoría o favoritos. El alcance corresponde a la carpeta escaneada; los paquetes que no puedan leerse se indican en el informe de errores.

## Extraer recursos

### Transparencia de efectos en el visor

**Enfocar geometría principal** calcula el encuadre usando los triángulos que no pertenecen a efectos aditivos (flags estándar, prefijo `$0b` o selección manual). **Ocultar efectos** deja de dibujar esos triángulos, también en modo malla. Si todo el modelo es un efecto, el encuadre vuelve al conjunto completo. Ambas opciones solo afectan al visor; los MDL y las exportaciones conservan todos los datos.

**Primera persona (aprox.)** coloca la cámara en el origen del modelo, mirando hacia +X, con perspectiva y **FOV horizontal** ajustable de 40° a 120°. Usa una secuencia apropiada del modelo `v_`; la pose de referencia no representa necesariamente su posición en el juego. La rueda y el desplazamiento permiten ajustar la vista. No reproduce offsets ni movimientos de cámara añadidos por el código del juego. La opción de encuadre principal se deshabilita en este modo porque no usa el centro calculado del modelo.

### Búsqueda, miniaturas y selección para exportar

Los resultados priorizan nombre exacto, prefijo, coincidencia parcial de nombre, ruta y paquete. Puedes combinar tipo, paquete, favoritos y tamaño mínimo/máximo (0 en máximo significa sin límite). El filtro adicional de paquete conserva la ruta completa para distinguir archivos con el mismo nombre.

Las miniaturas se guardan entre sesiones en `%LOCALAPPDATA%\CSNZWorkshop\thumbnails`, con un máximo de 2048 imágenes. La clave incluye la versión del render y el tamaño y fechas de todos los paquetes de la biblioteca, para invalidar también modelos cuyos compañeros cambian en otro paquete. La caché dañada se ignora; la galería mantiene además su límite de 96 imágenes en memoria.

En **Relacionados y CS 1.6**, **Marcar modelo y dependencias únicas** selecciona el MDL y sus referencias inequívocas. Revisa las versiones duplicadas, los faltantes y las sugerencias por nombre (incluidos sprites) antes de **Agregar marcados a colección**. Los efectos creados por código del juego no siempre declaran una referencia en el MDL: las sugerencias requieren selección manual. **Revisar exportación…** lleva la selección al mismo flujo disponible desde biblioteca y colecciones.

En **Exportar para mod**, **Diagnosticar selección** comprueba la lectura y analiza los modelos, los recursos referenciados que faltan en la selección y la necesidad de preparación o adaptación. Selecciona una fila para ver el detalle. Cambiar la selección invalida el diagnóstico; se puede cancelar. Los estados distinguen preparado para extracción/prueba, dependencias faltantes, adaptación/revisión y lectura no verificada. Exportar sigue copiando los bytes originales y no constituye una certificación de compatibilidad con CS 1.6.

El visor permite girar con el botón izquierdo, desplazar con el derecho o central y acercar con la rueda. **Centrar** restablece la cámara. El selector de vista ofrece frontal, trasera, laterales y superior. **Huesos (rayos X)** muestra la jerarquía animada por encima del modelo, incluso detrás de superficies. Puedes elegir fondo oscuro, gris o claro y guardar la vista actual como PNG (incluye fondo, malla y huesos si están activados).

La reproducción continúa desde el fotograma seleccionado. Los botones de fotograma pausan y avanzan o retroceden una posición; el selector de velocidad ofrece 0.25×, 0.5×, 1× y 2×. Al arrastrar la línea de tiempo o cambiar de pestaña, se pausa la animación. El render conserva la proporción del área visible y limita su resolución a 1200 × 900 para controlar el coste de dibujado.

Los materiales MDL con `STUDIO_NF_ADDITIVE` se dibujan sumando su color después de la geometría opaca, sin escribir profundidad. Los sprites SPR aditivos se componen sobre el fondo oscuro del visor; los modos normal, alfa indexado y máscara mantienen su tratamiento independiente.

**Efectos Nexon (aprox.)** interpreta las texturas con prefijo `$0b` como efectos aditivos sin sombreado. Es una aproximación basada en `v_gungnirex.mdl`, cuyas nueve texturas de efectos usan ese prefijo pese a tener los flags estándar en cero; no es una implementación completa del sistema de materiales de Nexon. Se puede desactivar en el visor. En **Texturas**, **Mezcla aditiva de esta textura (solo vista previa)** permite activar la mezcla manualmente para otras texturas. Estos ajustes no cambian el archivo ni se aplican a las copias exportadas y duran mientras está abierto el visor.

### Biblioteca visual y preparación guiada

La biblioteca abre inicialmente en **Galería** y después recuerda la vista elegida, con páginas de 24 recursos y miniaturas de modelos MDL, sprites SPR y texturas generadas en segundo plano. Conserva hasta 96 miniaturas en memoria durante la sesión. Pasa el cursor sobre una tarjeta para consultar su ruta y paquete de origen. La pestaña **Lista** conserva la selección múltiple con Ctrl/Mayús para extraer por lotes.

Al seleccionar un recurso, el panel derecho muestra una vista previa estática de los modelos MDL o imágenes BMP, su origen y tamaño. Para girar el modelo, reproducir animaciones o revisar texturas, usa **Ver modelo 3D**. Los offsets y flags se muestran al activar **Detalles técnicos**.

- **WAV y MP3:** pulsa **Reproducir** o **Detener**. Se muestra la duración y el sonido se detiene al cambiar la selección o cerrar la biblioteca. El MP3 se decodifica en segundo plano a PCM con NLayer; Windows reproduce una copia temporal, que se elimina al liberar la vista previa. No hay reproducción automática. El soporte WAV depende del controlador de audio de Windows.
- **SPR IDSP v2:** vista del primer fotograma, reproducción y selección manual de fotogramas. Se conservan los orígenes para evitar saltos de encuadre; los grupos usan sus intervalos y los fotogramas simples se muestran a 10 FPS. La mezcla aditiva es aproximada. Los grupos se recorren en orden como una secuencia de previsualización.
- La vista previa de audio y sprites admite hasta 64 MB por recurso; el audio MP3 decodificado también se limita a 64 MB. Los sprites se limitan a 1024 fotogramas, 4096 por dimensión de fotograma y 16 millones de píxeles indexados en total. Superar estos límites no impide extraer el recurso.

**Preparar para mi mod…** abre una revisión del modelo y sus archivos compañeros antes de elegir destino. También está disponible desde el visor mediante **Preparar copia para prueba**. Si faltan compañeros, la preparación se deshabilita y el diagnóstico indica cuáles faltan. La copia incluye el informe existente y queda pendiente de probar en CS 1.6; no incorpora sonidos externos automáticamente ni adapta límites o secuencias del modelo.

### Recursos relacionados y diagnóstico para CS 1.6

Selecciona un MDL, espera a que termine la vista previa y pulsa **Relacionados y CS 1.6…**. La ventana analiza en segundo plano y permite cerrarla para cancelar.

Selecciona una fila de **Recursos relacionados** para previsualizarla en el panel derecho: los modelos tienen vista estática y botón **Ver modelo 3D** (también doble clic tras cargar), los WAV/MP3 tienen reproducción y los SPR permiten reproducir o elegir fotogramas. Las imágenes BMP utilizan la misma vista previa de la biblioteca. Seleccionar una fila no marca su casilla ni la agrega a la colección. El sonido se detiene al cambiar de recurso, abrir la pestaña de diagnóstico o cerrar la ventana; los recursos no encontrados no tienen vista previa.

- **Recursos relacionados:** muestra compañeros MDL por la convención de nombres del lector y sonidos declarados en eventos 5004. Conserva las distintas coincidencias de un sonido para que puedas elegir su paquete; una referencia ausente se indica y no puede marcarse. Las variantes `v_`/`p_`/`w_`, sonidos y sprites con nombre o carpeta similar aparecen como **Sugerencia por nombre**, sin confirmar su relación. Se muestran hasta 200 sugerencias; las referencias explícitas no se recortan con ese límite.
- Marca los recursos que quieras y pulsa **Agregar marcados a colección**. Ningún recurso se marca automáticamente. El modelo principal también aparece para incluirlo si lo necesitas; la selección no convierte ni prepara sus archivos.
- **Diagnóstico CS 1.6:** revisa compañeros faltantes, sonidos de eventos no encontrados, orígenes ambiguos, eventos fuera de rango, flags de materiales y cantidades de huesos, vértices, triángulos, texturas y grupos. Los umbrales son referencias del SDK GoldSrc público, no una certificación de una versión concreta de CS 1.6.
- Comprueba los fotogramas del primer blend de todas las secuencias cuando los compañeros están disponibles y el trabajo no supera cinco millones de pares hueso/fotograma. Indica expresamente qué quedó pendiente. El orden de animaciones, los blends adicionales, los eventos específicos de Nexon y el comportamiento del arma o plugin requieren revisión en el destino.
- **Guardar diagnóstico JSON…** guarda hallazgos, acciones sugeridas, referencias, paquetes, secuencias con sus índices, eventos y fuentes del perfil. El visor y la preparación incluyen también una revisión estructural; la resolución de sonidos contra la biblioteca se realiza en esta ventana.

Las referencias de formato y límites están en [studio.h de Valve](https://github.com/ValveSoftware/halflife/blob/master/engine/studio.h); el evento de sonido 5004 está documentado en [el cliente del SDK](https://github.com/ValveSoftware/halflife/blob/master/cl_dll/entity.cpp). La búsqueda no inspecciona scripts ni código de plugins y no puede descubrir todas las dependencias de un arma solamente desde su MDL.

### Guardar y extraer colecciones

1. Abre **Biblioteca completa**, selecciona recursos y pulsa **Agregar a colección**. Puedes acumular resultados de distintas búsquedas y paquetes; agregar la misma referencia dos veces no la duplica.
2. Pulsa **Colección (N)…** para revisar la selección, ponerle nombre o quitar elementos.
3. Usa **Guardar…** para conservar un archivo `.csnzcollection`, y **Abrir…** para recuperarlo en otra sesión. **Nueva** permite comenzar otra colección. El asterisco indica cambios sin guardar; al cerrar la biblioteca se ofrece guardarlos.
4. **Extraer colección…** extrae todos sus recursos juntos, con cancelación, carpetas por paquete e informe JSON. Conserva los modelos originales: para descifrarlos y reunir sus compañeros, utiliza la preparación desde el visor.

Las colecciones guardan referencias, no los recursos: requieren los paquetes originales en sus rutas absolutas. Las referencias se resuelven contra el índice actual de la biblioteca, sin reutilizar offsets guardados. Una referencia ausente o ambigua se muestra explícitamente y bloquea la extracción de la colección hasta resolverla o quitarla. No se agregan dependencias de forma automática.

### Comprobaciones de esta ampliación

La batería sin instalación del juego incluye lectores SPR con truncamientos, transparencia y grupos; guardado y reapertura de colecciones, referencias ausentes o ambiguas y extracción de varios paquetes; reproducción de WAV y MP3 sintéticos silenciosos, limpieza de temporales y controles de reproducción en la interfaz. Requiere una sesión de Windows con controlador de audio. Estas pruebas no sustituyen la comprobación con recursos reales de CSNZ.

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

La aplicación incorpora adaptaciones de NARTools y CSO-Paker bajo GPL, y SharpZipLib, NLayer y Pfim bajo MIT. Consulta `THIRD_PARTY.md`, `LICENSE` y las licencias en `licenses/`. No necesita los repositorios de referencia para compilar y no incluye recursos del juego.
