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
