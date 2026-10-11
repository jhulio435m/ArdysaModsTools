# Evidencia y límites de validación

Fecha de preparación: 11 de octubre de 2026. Base upstream: `b923a93`. Fork confirmado por la API de GitHub: `jhulio435m/ArdysaModsTools`, padre `Anneardysa/ArdysaModsTools`.

## Comprobaciones realizadas sin compilar

- `check-source.py`: 48 comprobaciones estructurales correctas antes del commit. Incluye referencias MSBuild, imports relativos, tipografía, XML/JSON, sintaxis Bash y diff.
- `syntax-check.py`: 18 fuentes C#/TS/TSX analizadas con Tree-sitter, sin errores de sintaxis. No analiza tipos ni resuelve dependencias .NET/React.
- YAML de workflows inspeccionado con PyYAML. Los jobs heredados quedan restringidos al repositorio original; el nuevo workflow solo ejecuta checks de fuentes y usa parsers precompilados.
- El script de build sin `--compile` terminó con código 2 y el mensaje de que no se compiló.
- Inspección independiente y de solo lectura del VPK de Dota instalado: formato v2, 388112 entradas y 678 índices de archivo. Se encontró `scripts/items/items_game.txt` y se verificó su CRC sobre 52331384 bytes. Se encontró exactamente un bloque `SearchPaths` en la configuración nativa.

La inspección del juego se hizo con Python; **no demuestra que la implementación VPK C# funcione**. Sirve para confirmar el formato local y los archivos disponibles para pruebas futuras. No se copiaron ni cambiaron los archivos del juego.

## Comprobaciones no realizadas

- No se instaló el SDK .NET ni se restauraron paquetes NuGet/frontend.
- No se ejecutaron `dotnet build`, `dotnet test`, `dotnet publish`, `npm run typecheck` ni Vite.
- No se ejecutó el host Linux, la CLI C# o la interfaz React.
- No se instaló ningún mod ni se verificó su resultado visual en Dota.
- No se hizo entrega de binarios, release, prueba de actualización real, recuperación ante corte de energía ni revisión de dependencias restauradas.

El resultado entregable es una rama de código fuente experimental y su plan de validación, no una afirmación de paridad funcional completa o de compatibilidad dentro del juego.

## Preparación posterior para pruebas locales

La instrucción posterior del usuario autorizó compilar y dejar la aplicación lista para probar. Se instaló el SDK oficial .NET 8.0.425 por usuario y se publicaron artefactos self-contained Linux x86_64. Esta evidencia reemplaza el estado inicial de fuente sin compilar; las limitaciones de paridad y montaje dentro del juego siguen vigentes.

- Compilación Release y publicación del host aprobadas; se corrigió el enlace faltante a HeroDefaultItemRegistry.
- 17 pruebas NUnit aprobadas, 0 fallos y 0 omitidas. La comprobación del proceso Dota es inyectable en fixtures, para no bloquear pruebas temporales cuando el juego real está abierto; en producción conserva el bloqueo real.
- Typecheck específico de Linux y build Vite aprobados. El typecheck global del espejo original falla por módulos/tipos ausentes en pantallas Windows que no se incluyen en Linux.
- Se retiró vite-plugin-singlefile y su cadena micromatch/braces vulnerable, y se actualizó source-map-js. Auditoría npm: 0 vulnerabilidades. La compatibilidad de la página embebida Windows progress se comprobó con build e inline de JS/CSS.
- Revisión NuGet del núcleo: sin paquetes vulnerables conocidos en las fuentes consultadas.
- Chromium aprobó sesión local, detección, preferencias, VPK verify/list/extract/build, rechazo de sesión/origen no válido y descarga a host no permitido. Sin errores JavaScript; capturas escritorio/móvil inspeccionadas. Se corrigió el contraste del hover de la pestaña seleccionada.
- El ejecutable C# extrajo y verificó por CRC los 52331384 bytes de items_game.txt desde el VPK real, guardándolos únicamente en una carpeta temporal.
- La instalación por usuario y el launcher funcionan; la entrada .desktop pasó desktop-file-validate. El ejecutable y el índice HTML instalados coinciden con los publicados.
- Se preparó un VPK de texto en artifacts/samples para probar herramientas; no representa un mod cosmético.

No se instaló ningún mod en la instalación real ni se cambiaron sus configuraciones. Dota estaba abierto durante las pruebas de navegador y la aplicación bloqueó correctamente las acciones que lo modifican.
# Catálogo original — 11 octubre 2026

La carga real del CDN expuso 126 héroes. Chromium seleccionó Abaddon / Blightfall y completó descarga y generación por API (200), sin errores de página ni overflow móvil. El ZIP coincide con el SHA-256 de Assets/asset_hashes.json. El VPK generado ocupa aproximadamente 51 MiB y pasa la verificación CRC de todas sus entradas. Las 17 pruebas NUnit y el typecheck Linux pasan tras el ajuste de sets parciales. No se instaló el set ni se modificó Dota; montaje y apariencia dentro del juego siguen pendientes.

## Paquete combinado instalado — 11 octubre 2026

- Generación por catálogo: 123 héroes con assets SHA-256 verificados; fallback local del juego para Bristleback (5 slots) y Enigma (3 slots). Total: 125 de 126. Kez no tiene un set alternativo disponible.
- VPK final: 1.746 entradas, aproximadamente 322 MiB; todas las entradas pasan CRC.
- Tabla combinada: 740 bloques predeterminados encontrados y 588 referencias a modelos comprobadas contra assets del mod y del VPK del juego; cero modelos faltantes.
- 18 pruebas NUnit pasan.
- Arranque nativo de diagnóstico con Steam cerrado: el registro del motor enumera el VPK administrado en GAME y MOD antes del VPK original. Confirma el montaje, no la apariencia de los 125 héroes ni una sesión online; SteamAPI no inició en esa ejecución.
- Instalación reversible mediante ModManager y respaldo original de gameinfo.gi. No se modificaron firmas ni opciones de lanzamiento de Steam.
- Informe de selección local: artifacts/heroes-final-report.json. Regeneración, informe e instalación quedan preparados para la próxima prueba visual del usuario.

La comprobación posterior de scripts/npc/npc_heroes.txt detectó 127 héroes reales (excluyendo base y target_dummy). Largo está fuera del catálogo original y su tabla actual solo incluye objetos predeterminados. Cobertura instalada: 125/127; Kez y Largo mantienen el aspecto original. El nuevo arranque con el paquete combinado también registró GAME y MOD apuntando al VPK del mod; no se encontraron errores de parseo de items_game en el registro revisado. La apariencia individual dentro del juego sigue pendiente de la prueba del usuario.

## Earthshaker Planetfall base — 11 octubre 2026

Se descargó base_arcana_1.zip y se verificó su SHA-256 contra el manifiesto original. El overlay incluye models/heroes/earthshaker/earthshaker.vmdl_c. El VPK pasa CRC; las 1.746 entradas anteriores siguen presentes, con solo tres entradas anteriores cambiadas y ninguna fuera del overlay permitido. Se comparó también la tabla de objetos antes/después para conservar los otros héroes. Las 18 pruebas pasan. Se instaló con Dota cerrado y el paquete instalado coincide byte por byte con el generado. La apariencia Planetfall dentro del juego requiere comprobación visual del usuario.
