# ArdysaModsTools para Linux

Fork independiente de [Anneardysa/ArdysaModsTools](https://github.com/Anneardysa/ArdysaModsTools), con licencia GPL-3.0-or-later. Se conservan la licencia y los avisos originales. Esta rama añade una entrada Linux y conserva la aplicación Windows.

**Estado: versión experimental compilada, instalada por usuario y verificada localmente para pruebas.** Pasaron 17 pruebas NUnit, el typecheck/build Linux y los flujos principales en Chromium. No se ha instalado un mod cosmético en Dota ni se ha validado su carga dentro del juego. No hay release pública de ejecutables. Consulta [TESTING.md](TESTING.md) para abrirla y probarla.

## Arquitectura

- `Core/`: biblioteca `net8.0` sin WinForms, WebView2, registro, P/Invoke ni herramientas Windows. Comparte por enlace los helpers KeyValues y el parcheador de héroes originales; no los duplica.
- `Host/`: ejecutable CLI y host ASP.NET local. Escucha exclusivamente en `127.0.0.1` con un puerto asignado por el sistema; abre la interfaz mediante `xdg-open`.
- `../Frontend/src/pages/linux/`: interfaz React que reutiliza los tokens y la tipografía del proyecto. Se sirve desde el host local en el navegador de Linux. No requiere Electron, Wine, Proton ni WebView2 para la aplicación.
- `Tests/`: 17 pruebas NUnit aprobadas, con fixtures temporales independientes del juego real.
- `scripts/`: revisión estática, compilación explícita e instalación de artefactos ya compilados.

El frontend original para WebView2 sigue separado. La nueva interfaz Linux no representa todavía todas las funciones de esas pantallas. Consulta [PLAN.md](PLAN.md) para la matriz de paridad y los criterios de aceptación.

## Funciones disponibles para probar

| Función | Implementación | Validación pendiente |
|---|---|---|
| Detección de Steam | Steam nativo, Flatpak, bibliotecas adicionales y `appmanifest_570.acf` | Detector real probado; ampliar matriz Flatpak/discos |
| Configuración | Rutas XDG y preferencias de usuario | Persistencia comprobada en navegador; ampliar casos de permisos |
| VPK | Lectura v1/v2, precarga, archivos divididos, CRC; escritura v1 de archivo único | Roundtrip/CRC aprobados y lectura selectiva del VPK real; ampliar packs grandes |
| Descargas | HTTPS, hosts upstream permitidos, SHA-256 obligatorio y espejos secuenciales | Descarga real con manifiesto confiable |
| Generación | Recetas, ZIP con rutas restringidas y parcheador original sobre `items_game` instalado | Catálogos, selección y resultados dentro del juego |
| Instalación manual | Normaliza el paquete, guarda el original de `gameinfo.gi` y añade un montaje Linux propio | Validar que el cliente nativo acepte el montaje |
| Desactivación | Restaura la configuración administrada y elimina el paquete propio | Fallos, cambios de Steam y prueba real |
| Recuperación | Journal, copias, comprobación de conflictos y rollback | Interrupciones de proceso y fallos reales del sistema de archivos |
| Cambios de Dota | Aviso de build/configuración cambiados y reaplicación manual | Verificar después de una actualización real |
| FPS | Bloque propio de `fps_max` y `fps_max_ui` en `autoexec.cfg` | Verificar lectura por el juego |
| Interfaz | Detección, preferencias, mods, VPK, recetas, descargas y FPS | Typecheck/build/Chromium aprobados; probar mods reales |

## Límites explícitos

- El montaje Linux es una hipótesis de implementación que debe validarse. Se modifica `game/dota/gameinfo.gi` mediante una entrada `Game _ArdysaModsLinux`; **no se copian las firmas, hashes o plantillas Windows ni se altera `dota.signatures`**. Si el juego rechaza esta configuración, se revisa el método antes de activar automatismos.
- El selector básico incluye variantes de estilo publicadas como ZIP completos y descarga ZIP completos desde el catálogo original y genera un set de héroe con SHA-256 verificado. Los archivos cifrados y divididos no están soportados.
- La galería visual, selección por slots, Personas, Prismatic, todos los mods misceláneos y el ModsPack automático no están conectados todavía al host Linux.
- Las recetas esperan URLs y hashes de fuentes confiables. El ejemplo vacío documenta el formato; no es un mod listo para instalar.
- El escritor crea VPK de archivo único con hasta `uint.MaxValue` bytes de datos. Para packs mayores se necesita implementar salida dividida. El lector sí admite paquetes divididos existentes.
- `VerifyAsync` verifica CRC de las entradas; no verifica firmas criptográficas VPK v2. Las descargas se autentican por el SHA-256 esperado, cuyo origen debe ser confiable.
- No hay actualización automática de la aplicación, autoarranque ni reaplicación desatendida. La interfaz revisa el estado periódicamente y permite reaplicar de forma manual.
- Las transacciones usan archivos temporales, flush y renombrado en el mismo sistema de archivos. No se promete atomicidad conjunta de varios archivos ni tolerancia certificada a cortes de energía: falta verificar la persistencia de directorios ante pérdida de energía.
- Dota debe estar cerrado para modificarlo. Steam y el usuario no deben escribir simultáneamente los mismos archivos. Hay detección de conflictos, pero no se puede bloquear un escritor externo que ignore nuestros locks.
- Esta edición requiere acceso del usuario a la biblioteca del juego. No pide `sudo` para instalar mods. La aplicación no está empaquetada como Flatpak; detecta un juego instalado por Steam Flatpak desde fuera del sandbox.

## Revisar sin compilar

Desde la raíz del repositorio:

```bash
python3 Linux/scripts/check-source.py
```

La revisión comprueba XML, JSON, referencias de proyectos, imports relativos, fuente tipográfica, sintaxis Bash, exclusión del código Linux del proyecto Windows y whitespace del diff. No ejecuta C#, TypeScript, restauración de NuGet, npm ni pruebas NUnit. El workflow `linux-source.yml` realiza únicamente esta revisión.

El workflow también analiza sintaxis con Tree-sitter instalado exclusivamente desde wheels precompilados. Para reproducir ese check en un entorno Python dedicado, instala `scripts/syntax-requirements.txt` usando `pip --only-binary=:all:` y ejecuta `python scripts/syntax-check.py` desde `Linux/`. No equivale a typecheck o compilación. La evidencia y las verificaciones pendientes están en [VALIDATION.md](VALIDATION.md).

## Repetir la compilación

La primera publicación Linux x86_64 ya se realizó localmente.

Se necesita Linux, SDK .NET 8 compatible, Node.js/npm compatibles con el frontend y `xdg-open`. Se instaló el SDK oficial 8.0.425 por usuario en `~/.local/share/ardysa-dotnet`. El script lo detecta automáticamente si no existe `dotnet` en PATH; también acepta `AMT_DOTNET`. La publicación incluye su runtime .NET, por lo que abrir la aplicación instalada no requiere el SDK.

```bash
bash Linux/scripts/build.sh --compile linux-x64
```

El script instala dependencias frontend, ejecuta el typecheck específico de Linux, compila React, ejecuta NUnit y publica el host. El typecheck global del espejo original sigue fallando en pantallas Windows con módulos/tipos faltantes; esas pantallas no forman parte del bundle Linux. Sin `--compile`, termina sin compilar. `linux-arm64` también está preparado como destino de publicación; no ha sido validado en hardware ARM. Una publicación cruzada no valida su ejecución.

El frontend Linux se compila por separado; el script original `Frontend/build.mjs` lo excluye de las páginas Windows.

## Instalar artefactos publicados

```bash
bash Linux/scripts/install.sh /ruta/absoluta/ArdysaModsTools/artifacts/linux-x64
```

Instala por usuario en `$XDG_DATA_HOME/ardysa-mods-tools/app`, crea un enlace en `~/.local/bin` y una entrada de escritorio. Rechaza reemplazar una instalación existente. El script de instalación no compila y no modifica Dota. Después, ejecuta `ardysa-mods-tools serve` o usa la entrada del menú; conserva la terminal abierta mientras usas la interfaz.

Las preferencias están en `$XDG_CONFIG_HOME/ardysa-mods-tools/preferences.json`. Si una variable XDG falta o es relativa, se usa el directorio estándar dentro de HOME. La sesión del navegador es efímera y queda en una cookie HttpOnly; el enlace de inicio utiliza un fragmento retirado por la interfaz. No hay CORS habilitado, y las mutaciones requieren el origen local esperado y sesión autenticada.

## CLI preparada

Tras una publicación validada:

```bash
ardysa-mods-tools detect
ardysa-mods-tools status --root "/ruta/steamapps/common/dota 2 beta"
ardysa-mods-tools vpk-list --input "/ruta/pak01_dir.vpk"
ardysa-mods-tools vpk-verify --input "/ruta/pak01_dir.vpk"
ardysa-mods-tools vpk-extract --input "/ruta/pak01_dir.vpk" --output "/ruta/carpeta-vacia"
ardysa-mods-tools vpk-build --source "/ruta/contenido" --output "/ruta/nuevo_dir.vpk"
ardysa-mods-tools generate --root "/ruta/steamapps/common/dota 2 beta" --recipe "/ruta/seleccion.json" --output "/ruta/seleccion_dir.vpk"
```

Instalación experimental, solo después de verificar el paquete y cerrar el juego:

```bash
ardysa-mods-tools install --root "/ruta/steamapps/common/dota 2 beta" --input "/ruta/mod_dir.vpk" --experimental
ardysa-mods-tools disable --root "/ruta/steamapps/common/dota 2 beta"
```

La carpeta administrada es `game/_ArdysaModsLinux`; contiene el VPK normalizado y el estado con el respaldo original de `gameinfo.gi`. Los archivos oficiales VPK no se sustituyen.

Si Steam reemplaza `gameinfo.gi`, `reapply --experimental` toma la nueva versión como base. Si el archivo cambió y todavía contiene nuestro marcador, se rechaza sobrescribirlo: conserva el estado y revisa el conflicto. `disable` conserva una versión nueva de Steam sin marcador en vez de restaurar un respaldo antiguo.

Una operación interrumpida conserva el journal en `.ardysa-linux-transaction`. Ejecuta `recover --root ...` con Dota cerrado. Si detecta un cambio externo, deja los respaldos para inspección; no borres esta carpeta a ciegas.

## Recetas

Las propiedades JSON se leen sin distinguir mayúsculas:

- `name`: etiqueta de la selección.
- `assets`: objetos con `path` relativo, `sha256` esperado, `urls` de espejos permitidos y `extractZip` opcional. Un ZIP debe contener rutas de contenido listas para montar. Los conflictos de destinos se rechazan.
- `patches`: objetos con `heroId`, `itemIds` y `indexPath` relativo que señala un índice descargado como asset.

Para aplicar patches se extrae y verifica `scripts/items/items_game.txt` del VPK del Dota instalado. El parcheador enlazado del proyecto original combina los bloques seleccionados. Los índices usados se retiran del contenido de salida. La generación no instala el paquete automáticamente.

No se incluyen URLs, hashes, IDs o catálogos inventados. El espejo público original tampoco incluye `tools/`; esta edición evita depender de ellos.

## Preparar varios héroes

`generate-all --output /ruta/paquete_dir.vpk --report /ruta/seleccion.json` elige el primer set compatible de cada héroe, verifica sus hashes y genera una tabla de objetos combinada. No instala automáticamente. El informe identifica héroes sin paquete compatible; no debe presentarse una selección parcial como cobertura total. Opcionalmente `--base /ruta/base_dir.vpk --base-report /ruta/base.json` conserva una selección generada anteriormente y reintenta los héroes omitidos.

Los conflictos reales de modelos impiden combinar un candidato. Se conserva el primer logo del autor cuando varios sets traen versiones distintas; modelos de otro héroe sin referencia en el set se omiten en caso de conflicto. No se modifican las firmas de Windows.

`--local-fallback` con `--base` conserva los sets descargados y, para héroes omitidos, selecciona modelos de cosméticos que ya existen en el VPK del juego. Solo cambia el modelo de objetos predeterminados del mismo héroe y slot; no convierte Personas. `--only ID_DEL_HERO` permite reintentar un héroe omitido. Los sonidos duplicados se resuelven a favor del héroe nombrado por la ruta del recurso.
