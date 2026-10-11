# Probar la galería Linux

Busca **ArdysaModsTools Linux** en el menú, o ejecuta `~/.local/bin/ardysa-mods-tools serve`. El menú abre la galería en el navegador sin depender de una terminal. El servidor local continúa mientras usas la aplicación. Si la inicias desde la CLI, conserva esa terminal y ciérrala con Ctrl+C al terminar.

1. Elige un héroe y abre sus opciones.
2. Selecciona sets completos, piezas o héroe base. En las arcanas con estilos, elige el estilo deseado y pulsa «Hecho».
3. Cierra Dota antes de pulsar **Aplicar selección**. La aplicación descarga los assets, verifica y recompone el paquete conservando los demás héroes.
4. Cuando termine, abre Dota y comprueba el héroe con su equipo predeterminado.

La selección se guarda automáticamente. Exportar/importar permite guardar presets; favoritos ayuda a localizar héroes. «Herramientas e instalación» abre detección, desactivación, recuperación y herramientas VPK.

En esta PC quedan seleccionados Earthshaker Planetfall con su cabeza y Drow Dread Retribution / Master of Madness. El paquete ya instalado sigue activo; la actualización de la aplicación no modifica el juego mientras está abierto. La apariencia final de cabeza, efectos y estilos en partida todavía requiere comprobación del usuario.

## Alcance y recuperación

El port es experimental. Admite ZIP completos del catálogo original; los paquetes cifrados/divididos y la paridad completa de mods misceláneos/updater siguen pendientes. Tras cambios de build o un reemplazo externo del VPK, aplicar se bloquea para preservar la base. Hay que revisar/regenerar esa base antes de continuar.

Para desactivar, cierra Dota y usa las herramientas de instalación. El montaje propio está en `game/_ArdysaModsLinux`; la configuración original se conserva en el estado administrado. Si hay una transacción interrumpida, usa recuperación sin borrar sus respaldos.

## Evidencia local

25 pruebas NUnit y una prueba frontend pasan. Chromium verificó 126 tarjetas, guardado de 123 selecciones, persistencia de estilos, exportación, teclado, diseño móvil sin desbordamiento y rechazo de aplicar con Dota abierto. La recomposición con assets reales se valida por CRC y comparación de bloques/recursos. Esto no sustituye la prueba visual dentro de Dota.
