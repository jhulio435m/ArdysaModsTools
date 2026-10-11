# Plan del port Linux

## Objetivo y decisión técnica

Ofrecer una aplicación Linux para administrar los mismos mods cosméticos que el proyecto Windows, conservar el trabajo C#/React existente y permitir volver al estado previo de la instalación. El primer destino de validación es Dota nativo en Linux x86_64, con Steam instalado por el usuario. La PC disponible tiene una instalación real del juego; hay un paquete combinado instalado de forma reversible.

El shell Linux usa React en el navegador local y C#/.NET como backend. Esto permite avanzar sin introducir una reescritura Rust/JavaScript del núcleo. Los servicios Windows acoplados a UI se portan por función: cambiar `net8.0-windows` directamente no elimina WinForms, WebView2, registro, ACL, herramientas `.exe`, updater ni dependencias nativas.

## Entrega actual: versión experimental lista para pruebas locales

Hecho en código: proyectos separados; exclusión del glob Windows; detección nativa/Flatpak y bibliotecas adicionales; rutas XDG; CLI; host local autenticado; página React con tokens y fuente originales; VPK administrado; descargas verificadas; generación por recetas/ZIP; parcheador de héroes original enlazado; instalación/desactivación experimentales; journal y recuperación; detección de cambios y reaplicación manual; límites FPS; fuentes de pruebas; scripts de build/instalación; CI que no compila.

**Verificado localmente:** restore, typecheck/build Linux, publicación self-contained, 25 pruebas NUnit, instalación por usuario y flujos Chromium. La lectura selectiva de items_game mediante C# también pasó su CRC.

**Pendiente:** comprobación visual final en partida y paridad completa. No debe marcarse como aplicación terminada.

## Fase 1: validar la base

1. Instalar/verificar el SDK y dependencias cuando se autorice compilar.
2. Compilar los proyectos separados y corregir problemas reales de tipos, dependencias y advertencias.
3. Ejecutar NUnit; agregar casos encontrados durante la validación. Los fixtures temporales de pruebas no usan la instalación real.
4. Comparar lectura, CRC, precarga y extracción VPK con archivos del juego y una implementación independiente. Verificar el VPK generado con otra herramienta.
5. Validar frontend en el navegador: sesión, errores, cancelación, operaciones largas, teclado y tamaños de pantalla.

Criterio de salida: build y tests correctos, interfaz funcional y roundtrip VPK verificado de forma independiente. Esto todavía no demuestra montaje dentro de Dota.

## Fase 2: demostrar instalación y restauración reales

1. Capturar hashes de los archivos a tocar y conservar respaldos externos de la configuración.
2. Usar un mod cosmético pequeño con procedencia conocida. Verificar que las rutas y assets sean compatibles con el cliente Linux actual.
3. Probar el montaje experimental con Dota cerrado y verificar su resultado visual dentro del juego.
4. Desactivar y comprobar byte a byte la restauración; verificar que el cliente normal abre.
5. Probar reaplicación después de un cambio de Steam y resolución de conflictos sin restaurar archivos antiguos.
6. Probar interrupciones controladas durante staging/commit y recuperación. Documentar límites ante pérdida de energía.

Criterio de salida: instalación, resultado visual, desactivación y recuperación verificados. Si el cliente rechaza el montaje, esta fase bloquea los automatismos; se revisa el diseño con evidencia del cliente nativo.

## Fase 3: paridad con el original

| Área | Trabajo restante | Criterio de aceptación |
|---|---|---|
| ModsPack de un clic | Conectar manifiestos reales, mirrors, esquema de hashes y versión instalada; generación/rebase de índices | Descargar/aplicar/desactivar una versión conocida y detectar una nueva |
| Galería de héroes | Bridge, catálogo, favoritos, slots y estilos implementados; ampliar matriz de combinaciones | Comparar casos adicionales con el pipeline Windows |
| Arcanas/Personas/Prismatic | Portar resolutores, exclusiones, compatibilidad y overrides específicos | Matriz de casos reales validada por categoría |
| Mods misceláneos | Conectar terrenos, clima, HUD, música, cursores, couriers, wards y efectos | Cada categoría se genera, activa y revierte independientemente |
| Orden y conflictos | Compartir reglas de prioridad, resolución, estado activo y presets | Conflictos reproducibles y resultados deterministas |
| Paquetes grandes | Añadir escritor VPK dividido y validarlo con varios chunks | Packs mayores de 4 GiB sin truncamiento ni chunks faltantes |
| Actualizaciones del juego | Portar watcher, debouncing y trabajos pendientes; prohibir escritura durante juego/actualización | Reaplicación después de actualización completa, sin carreras |
| Rendimiento | Portar catálogo de cvars/presets compatibles y opciones de lanzamiento Linux | Configuración conservada y efecto medido en cliente actual |
| Internacionalización | Usar locales compartidos y todas las cadenas del nuevo host | Sin mezcla de idioma, mensajes claros de recuperación |
| Logs/soporte | Logs rotados y reporte exportable sin credenciales o datos innecesarios | Diagnóstico suficiente para reproducir un fallo |

El espejo público no incluye todos los assets/herramientas operativas. Antes de implementar los adaptadores de catálogo se debe fijar qué manifiestos públicos y formatos están realmente disponibles. No se debe rellenar ese vacío con datos de muestra presentados como funcionales.

## Fase 4: integración de escritorio y distribución

1. Validar instalación por usuario y actualización de artefactos con rollback.
2. Empaquetar para x86_64; validar ARM antes de anunciar soporte.
3. Elegir AppImage o paquetes por distribución según las pruebas; Flatpak para la aplicación requiere diseñar permisos/portal de archivos además de detectar Steam Flatpak.
4. Añadir iconos originales, acceso directo, cierre limpio, instancia única y comportamiento esperado al cerrar el navegador.
5. Implementar autoarranque optativo y updater Linux solo después de tener releases Linux con integridad verificable.
6. Configurar CI de build/test solo cuando se permita compilar; conservar los jobs de CDN upstream restringidos a su repositorio.

Criterio de salida: artefacto instalable en un entorno limpio, licencia/avisos incluidos, funcionamiento sin privilegios elevados y actualización reversible.

## Fase 5: publicación

Publicar una beta Linux identificada como experimental después de superar las pruebas reales, con matriz de plataformas, funciones admitidas, límites y procedimiento de restauración. Un push de fuentes o un workflow estático verde no equivalen a una release lista para usuarios.

## Orden de ejecución y alcance

El orden crítico es base compilada → VPK independiente → montaje/restauración dentro del juego → catálogos/selección → automatismos → empaquetado/release. El código fuente adelantado de una fase no sustituye sus criterios de validación. La instrucción posterior autorizó preparar la prueba completa: se compiló y se validó la base local. Las fases que requieren resultados visuales de mods y paridad siguen pendientes.

## Galería integrada — 11 octubre 2026

Se reutilizó la galería React original con un transporte HTTP local autenticado. Se añadieron guardado automático, presets, progreso/cancelación y recomposición desde base inmutable. Prioridades, variantes, slots bloqueados, modelo de cabeza Planetfall y protección de otros héroes tienen pruebas. Quedan paquetes cifrados/divididos, rebase automático después de actualizaciones y validación visual de más combinaciones.
