# Probar la versión Linux en esta PC

La aplicación está compilada, instalada por usuario y preparada para pruebas locales. El port sigue siendo experimental y no tiene todavía toda la galería/catálogos del original.

## Abrir

Busca **ArdysaModsTools Linux** en el menú de aplicaciones, o ejecuta:

```bash
~/.local/bin/ardysa-mods-tools serve
```

Se abre el navegador y la terminal mantiene el servidor local. Para cerrar la aplicación, usa `Ctrl+C` en esa terminal. Cada inicio abre una sesión nueva; entrar solo a una URL guardada sin el enlace de inicio no autentica la sesión.

## Prueba inicial sin modificar Dota

1. En “Tu instalación”, confirma la carpeta detectada y pulsa “Guardar”.
2. Abre “Archivos VPK” y usa el paquete de muestra creado en `artifacts/samples/sample_dir.vpk` dentro del checkout.
3. Pulsa “Verificar integridad” y “Ver contenido”. Este paquete solo contiene un archivo de texto para comprobar herramientas; **no es un mod cosmético y no se instala en Dota**.
4. Prueba “Extraer paquete” en una carpeta nueva/vacía fuera del juego. El contenido debe coincidir con el texto de prueba.
5. Prueba “Crear paquete” con una carpeta pequeña y una ruta nueva de salida fuera de esa carpeta.

La aplicación funciona con Dota abierto para estas herramientas. La instalación de mods y los cambios de FPS se bloquean mientras el juego está abierto. No hace falta cerrar tu partida para explorar la interfaz.

## Probar un mod real

Necesitas un VPK cosmético real, de origen confiable y compatible con la versión del juego. No se descargó ni se aplicó uno automáticamente. Si el VPK es dividido, todos sus archivos deben estar juntos.

1. Cierra Dota y espera a que Steam termine cualquier actualización.
2. Verifica el paquete en “Archivos VPK”.
3. En “Mods”, marca “Habilitar la instalación experimental”, introduce su ruta y pulsa “Instalar paquete”.
4. Abre Dota y comprueba el resultado visual. **Este montaje dentro del cliente Linux todavía necesita esa prueba.**
5. Cierra Dota, pulsa “Desactivar mods” y comprueba que la configuración vuelve al estado anterior.

Si aparece un conflicto, conserva el estado y los respaldos; no borres manualmente archivos del juego para forzar la instalación. Revisa `Linux/README.md` para el procedimiento de recuperación.

## Estado verificado

- Compilación Release del núcleo y publicación Linux x86_64 con runtime incluido.
- 17 pruebas NUnit aprobadas con fixtures en directorios temporales; no usan la instalación real.
- Typecheck y build de la interfaz Linux aprobados.
- Flujos Chromium: sesión, detección, preferencias, verificación/listado/extracción/creación VPK, rechazos de sesión/origen y de descarga a host no permitido.
- Capturas de escritorio y móvil inspeccionadas; sin errores de JavaScript ni desbordamiento horizontal en la prueba.
- Lectura/extracción selectiva de `items_game.txt` desde el VPK real mediante el ejecutable C#, con CRC verificado y salida fuera del juego.
- Auditoría npm sin vulnerabilidades; revisión de paquetes .NET sin hallazgos conocidos en las fuentes consultadas.

No se ha validado un mod cosmético dentro de Dota. No hay release pública de binarios ni paridad completa con la galería original.

## Descargar desde el catálogo original

En **Generar selección**, pulsa **Cargar catálogo**, elige héroe y set, y escribe una ruta nueva para el VPK. **Descargar y generar set** verifica el SHA-256 del manifiesto del CDN original y usa el items_game de la instalación actual. Después puedes ir a **Mods** e instalar ese VPK con el montaje experimental habilitado.

Este primer selector maneja ZIP completos con index.txt. No incluye paquetes divididos, cifrados, estilos/personas ni el ModsPack completo. Blightfall de Abaddon fue descargado y generado como prueba real; la apariencia dentro de Dota aún requiere comprobación.

## Selección instalada para abrir Dota

El paquete combinado preparado el 11 de octubre está en `artifacts/heroes-final_dir.vpk`; el informe completo está en `artifacts/heroes-final-report.json`. Incluye 125 de los 126 héroes del catálogo: 123 con paquetes del CDN y Bristleback/Enigma con modelos cosméticos del juego. La instalación nativa incluye además a Largo, ausente del catálogo y sin modelos cosméticos alternativos instalados: la cobertura real es 125/127. Kez conserva su apariencia original porque no tiene alternativas en el catálogo ni modelos de cosméticos alternativos en esta instalación.

Esta selección reemplaza objetos predeterminados; piezas equipadas de otros sets pueden seguir mostrando sus propios modelos. La aplicación puede desactivar todo y restaurar el gameinfo original con su respaldo. No hace falta ejecutar la aplicación antes de cada arranque de Dota. Una actualización de Steam puede requerir reaplicar el montaje o regenerar la tabla de objetos.
