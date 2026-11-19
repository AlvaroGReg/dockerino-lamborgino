# Lanzador local de proyectos WEBS-CMS

## Propósito

Analizar la viabilidad de una pequeña aplicación de escritorio para seleccionar uno de los proyectos del workspace, cambiar su rama Git y arrancar los procesos de backend, frontend y backoffice. Este documento prepara el desarrollo; no especifica una implementación ya realizada.

## Resumen de la idea

Crear una aplicación de escritorio local para Windows que permita gestionar proyectos de desarrollo, cambiar sus ramas Git y abrir terminales **integradas en la propia aplicación** desde un menú flotante. Las terminales serán interactivas: el usuario podrá ver la salida y escribir en ellas, no solo observar logs.

El flujo inicial se optimiza para los proyectos de `../../00-WEBS-CMS`: permitir abrir sesiones de terminal para backend, frontend y backoffice, además de ejecutar `update-core:latest` antes del frontend cuando se elija esa acción. La arquitectura debe permitir configurar rutas y comandos, sin quedar limitada a esos repositorios. El spike de Fase 1 valida la integración base **WPF sobre .NET 10 + ConPTY + Job Objects**; queda una comprobación manual del teclado físico antes de cerrar toda la fase.

### Ubicación de los proyectos

Los repositorios que el lanzador debe descubrir y arrancar están en `../../00-WEBS-CMS`, relativo a la carpeta de este proyecto de documentación. La aplicación deberá escanear las carpetas de primer nivel de esa ubicación y validar cada repositorio; no debe tratar esta carpeta de documentación como el workspace de proyectos.

## Pendientes antes de empezar

Las decisiones de producto iniciales ya están tomadas y se incorporan al plan de desarrollo. El spike y sus pruebas de runtime de ConPTY, Job Objects, comandos reales y Docker están documentados en [prototype/README.md](prototype/README.md). Antes de cerrar Fase 1 falta confirmar manualmente que la escritura física del teclado llega a la terminal y que el resize de ConPTY se refleja visualmente; véase [plan.md](plan.md).

## Decisiones tomadas

- **Plataforma/terminal:** Windows. La aplicación tendrá terminales integradas e interactivas basadas en ConPTY; Windows Terminal o PowerShell podrán ser los shells ejecutados dentro de ellas, no ventanas externas que sustituyan a la terminal de la app.
- **Ubicación:** la ruta raíz será configurable por proyecto, no una ruta fija. `../../00-WEBS-CMS` es la ubicación actual conocida y debe servir como valor inicial sugerido.
- **Acciones:** el botón verde ejecuta el arranque normal; el botón naranja ejecuta `update-core:latest` y después arranca. No se pedirá confirmación: el usuario conoce los efectos de la actualización.
- **Ramas:** mostrar ramas locales y remotas en secciones diferenciadas, con iconos distintos y una barra separadora (estilo de presentación inspirado en la extensión GitHub de VS Code). Elegir una rama remota requerirá una acción explícita para crear/cambiar a una rama local.
- **Sesiones y procesos:** las terminales se abren y administran desde el menú flotante. El spike confirmó ConPTY y Job Objects para detener el árbol propio de una sesión; nunca se aceptará un PID arbitrario desde la interfaz.
- **Referencia de producto:** el clon de [CodeDeck](../CodeDeck) está en `../CodeDeck`, relativo a esta documentación. Se usará para estudiar flujos, interfaz y conceptos de proyectos, comandos y conjuntos de lanzamiento; es una referencia, no la base directa del producto ni un proyecto que vayamos a modificar aquí.
- **Docker:** el spike confirmó que cerrar la sesión del cliente no detiene un contenedor detached; la parada/eliminación se realiza de forma explícita y acotada al proyecto Compose. Los contenedores los administra el daemon de Docker y no son descendientes normales del proceso local; Job Objects no bastan para pararlos.

### Control de terminal elegido inicialmente

- **Elección inicial:** [EasyWindowsTerminalControl](https://github.com/MitchCapper/EasyWindowsTerminalControl), encapsulado detrás de un adaptador/interfaz de terminal propia para poder sustituirlo.
- **Contingencia:** si el control no satisface los requisitos o no permite integrar la gestión de procesos, crear un *fork* mantenido por el proyecto y adaptarlo.
- [WPF UI Terminal](https://github.com/emako/wpfui.terminal) es un port de Easy, no una implementación independiente. [WindowsTerminalControl](https://github.com/0x5bfa/WindowsTerminalControl) y [Term1809](https://github.com/zac15987/Term1809) quedan como referencias de comparación, no como dependencias seleccionadas.
- **WebView2 + xterm.js + puente ConPTY** queda como alternativa de último recurso si el enfoque basado en el control WPF de Windows Terminal resulta inviable.

El control debe quedar detrás de una interfaz del proyecto. Fijar versiones y revisar licencia/dependencias. Si se necesita modificarlo, el fork será explícito y se mantendrá con pruebas propias; no se dispersarán cambios específicos por toda la aplicación.

## Resumen ejecutivo

La idea es viable como aplicación de escritorio local. Requiere integrar una terminal real (ConPTY), administrar sesiones de proceso propias, acceder a varios repositorios locales y ejecutar Git. Se estudiará CodeDeck como referencia de UX y flujos; el spike validó la integración base propuesta **.NET 10 WPF + ConPTY + Job Objects**. Véase [el informe de Fase 1](prototype/README.md) para el resultado y la última comprobación manual pendiente.

El workspace contiene 11 repositorios de proyectos. La mayoría siguen `<repo>/<proyecto>/<app|backOffice>`, pero EmbutidosEzequiel utiliza `Ezequiel` como carpeta de aplicación. Por tanto, el descubrimiento debe validar la estructura y no construir rutas a partir del nombre del repositorio.

El arranque estándar usaría tres procesos:

1. `dotnet watch run --no-hot-reload`, desde la carpeta del proyecto ASP.NET Core.
2. `npm start`, desde `app`.
3. `npm start`, desde `backOffice`.

El segundo botón debe abrir igualmente las tres sesiones; en la terminal del frontend ejecutará primero `npm run update-core:latest` y, solo si termina correctamente, `npm start`. Backend y backoffice pueden arrancar mientras se actualiza el core. La operación puede tardar y modifica los artefactos de dependencias.

## Documentos

- [Decisiones de arquitectura](decisiones.md): propuesta, alternativas y riesgos.
- [Plan de desarrollo](plan.md): fases, criterios de aceptación y preguntas pendientes.

## Base del análisis

Se ha contrastado la propuesta con el workspace local:

- Hay 11 repositorios de proyectos en la raíz, cada uno con su propia raíz Git.
- Los 11 tienen una carpeta `app` con `package.json`; los 11 también tienen `backOffice` con `package.json`.
- El script del frontend declara `start = npm run build:watch`.
- El script del backoffice declara `prestart = npm run combine` y `start = ng serve`; `npm start` ejecuta también el ciclo de vida `prestart` de npm.
- `update-core:latest` en el frontend ejecuta el actualizador de paquetes core y luego `npm run phoenix`. Este último elimina `node_modules` y `package-lock.json` antes de `npm install`.
- Los proyectos ASP.NET Core principales tienen como objetivo `net10.0`.

Estos datos describen el estado observado del workspace y deberán volver a comprobarse al implementar el detector de proyectos. No se han ejecutado los comandos de arranque ni de actualización.
