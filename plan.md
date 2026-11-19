# Plan de desarrollo: lanzador local de proyectos

Este plan convierte las decisiones de [decisiones.md](decisiones.md) en etapas implementables. El estado del spike de Fase 1 se registra en [prototype/README.md](prototype/README.md).

## Fase 0: cerrar decisiones de producto

Las decisiones de plataforma, rutas, interfaz de terminales, botones y ramas ya están tomadas y se detallan en [README.md](README.md) y [decisiones.md](decisiones.md). Antes de cerrar esta fase:

- Aceptación manual de Fase 1 completada (prompt, teclado físico con marcador y resize visual), además de compilación y comprobación pasiva; conservar las evidencias y límites del spike antes de construir la UI.
- Elegir distribución: ejecutable autocontenido o requisito de tener instalado .NET 10. (Decidido: requisito .net 10)

## Fase 1: prototipo de integración

**Estado: bloqueo de Fase 1 resuelto y criterios de aceptación del spike aprobados el 19-11-2026.** Se aisló en el escenario reproducido el desvío de handles estándar del anfitrión: con `STARTF_USESTDHANDLES` y handles nulos el prompt llega a ConPTY y el shell permanece vivo. La regresión falló antes y pasó después; `pwsh` y `cmd` superaron la comprobación pasiva, y se observó salida 23 de un proceso de prueba. Debug/Release compilan. PID, estado, errores y salida se registran sin tratar `TermReady` como aceptación. El usuario confirmó prompt, teclado físico y redimensionado visual; se verificó el archivo marcador creado desde la terminal. Ver evidencias y límites en [prototype/README.md](prototype/README.md). No se ha iniciado la UI completa.

Crear un prototipo mínimo con **EasyWindowsTerminalControl** detrás de una interfaz/adaptador propio que pueda:

- iniciar PowerShell/pwsh y comandos de prueba en ConPTY;
- renderizar ANSI/VT, recibir teclado, mantener encoding y manejar resize;
- asignar el proceso raíz a un Job Object y comprobar que detener la sesión termina solo sus descendientes;
- comprobar si la factoría de procesos/extensión del control permite asignar el proceso a un Job Object antes de reanudarlo; si no, crear un fork mínimo para integrar este ciclo de vida;
- arrancar un contenedor Docker de prueba desde la terminal y probar por separado la salida/interrupción del cliente y la parada del contenedor;
- validar rutas de `app`, `backOffice` y el `.csproj` principal, y ejecutar los tres comandos reales del workspace en sesiones separadas.

Probar rutas con espacios y estructuras distintas, incluida EmbutidosEzequiel cuyo directorio de aplicación es `Ezequiel`.

Verificar licencia, versiones y dependencias del control seleccionado. Mantener alternativas documentadas en [README.md](README.md), pero no retrasar el prototipo por una comparación exhaustiva. Distinguir entre terminar el proceso `docker compose` y detener/eliminar sus contenedores. Para Docker Compose usar un proyecto de prueba con nombre aislado; nunca terminar el daemon de Docker ni ejecutar una parada global de contenedores.

**Salida:** decisión sobre el control ConPTY reutilizable y evidencia de interacción terminal completa, aislamiento de procesos y ciclo de vida Docker acotado. No avanzar a la UI completa si un requisito esencial falla.

## Fase 2: esqueleto WPF

- Crear una aplicación WPF .NET 10 independiente en una carpeta propia del workspace (no dentro de un sitio).
- Implementar una ventana compacta sin marco, always-on-top, que se pueda plegar y desplegar.
- Añadir menú flotante de proyectos, rama actual, comandos y sesiones de terminal.
- Integrar vistas de terminal interactivas y permitir abrir/reseleccionar sesiones desde el menú.
- Mantener vivas las sesiones al plegar/cerrar el menú y definir el comportamiento al cerrar la aplicación.
- Crear una capa de dominio/backend para sesiones y proyectos que pueda compartirse posteriormente con UI y CLI para IA.

## Fase 3: descubrimiento de proyectos

- Permitir configurar la ruta base del workspace por proyecto; sugerir inicialmente `../../00-WEBS-CMS` respecto a la carpeta de documentación.
- Enumerar solo sus directorios hijos inmediatos.
- Validar cada candidato comprobando Git, `app/package.json`, `backOffice/package.json` y un `.csproj` principal.
- Excluir explícitamente directorios ocultos/de paquetes y proyectos que no cumplan la estructura.
- Guardar para cada proyecto las rutas encontradas, no nombres de rutas supuestos.
- Mostrar errores de descubrimiento sin impedir el uso de los demás proyectos válidos.

## Fase 4: selector de ramas

- Consultar rama actual, ramas locales y remotas y estado de cambios.
- Mostrar ramas locales y remotas en grupos visualmente diferenciados, con iconos distintos y una barra separadora (referencia visual: extensión GitHub de VS Code).
- Permitir cambiar a una rama local usando `git switch`.
- No usar stash/reset ni sincronizar con remoto automáticamente.
- Tratar la selección de una rama remota como acción explícita para crear/cambiar a una rama local de seguimiento; no cambiar a remoto de forma implícita.
- Refrescar la UI tras el cambio y presentar errores de Git.

## Fase 5: arranque y actualización

- Implementar arranque normal en terminales interactivas integradas mediante ConPTY.
- Presentar dos acciones inequívocas: botón verde para arranque normal y botón naranja para `update-core:latest` y arranque.
- Crear una terminal interactiva por proceso para backend, frontend y backoffice.
- Implementar actualización core en la sesión frontend y condicionar `npm start` a un código de salida cero.
- Permitir que backend y backoffice arranquen en paralelo mientras se actualiza el core.
- En código distinto de cero: conservar salida visible y no arrancar el frontend.
- No pedir confirmación para `update-core:latest`; comunicar su propósito/efectos junto al botón naranja.
- Mostrar progreso/estado, evitar clics duplicados durante la operación y conservar salida útil para diagnóstico.
- Validar disponibilidad de `dotnet`, `npm`, shell y componentes de terminal requeridos; indicar cuál falta.

## Fase 6: endurecimiento y entrega

- Probar paths con espacios, rutas inválidas, proyecto incompleto y fallos de creación/redimensionado/interacción de terminal.
- Probar Docker con daemon disponible/no disponible, compose adjunto y detached, interrupción del cliente, parada del proyecto de prueba y preservación de volúmenes.
- Probar cambio de rama con árbol limpio y su rechazo/advertencia con cambios locales.
- Probar el fallo y éxito de `update-core:latest`.
- Verificar que el arranque normal no ejecuta actualizaciones.
- Documentar instalación, ejecución y configuración de ruta del workspace.
- Decidir distribución (ejecutable autocontenido o instalación con runtime .NET).
- Verificar que Job Objects y el registro de sesiones permiten detener solo los procesos descendientes de la sesión solicitada; no aceptar PID arbitrarios desde UI ni CLI.

## Criterios de aceptación del MVP

1. La app lista los 11 proyectos actuales bajo la ruta configurada y no lista carpetas auxiliares ni paquetes comunes.
2. Las rutas detectadas funcionan aunque el directorio interno difiera del nombre del repo.
3. Al seleccionar un proyecto, se muestra su rama actual.
4. Se puede cambiar a una rama local sin que la app descarte cambios locales.
5. El arranque normal abre tres terminales propias, interactivas y asociadas a sesiones aisladas, con comandos y directorios correctos.
6. El arranque normal nunca ejecuta `update-core:latest`.
7. El arranque con actualización inicia backend y backoffice y, en la terminal frontend, ejecuta el script antes de `npm start`; si el script falla, solo se omite el arranque del frontend.
8. El usuario recibe un mensaje accionable si faltan comandos, rutas o componentes de terminal.
9. Las sesiones de terminal permanecen accesibles al plegar el menú; se puede interactuar con ellas desde la aplicación y detenerlas individualmente sin afectar procesos ajenos.
10. El botón naranja explica que la actualización puede modificar/reinstalar dependencias; no requiere confirmación.
11. Las ramas locales y remotas aparecen diferenciadas; seleccionar una remota requiere una acción explícita de creación local.

## Pendientes para la implementación

- ¿La distribución será un ejecutable autocontenido o asumirá .NET 10 instalado?
- ¿Qué control ConPTY se usará y qué licencias/dependencias introduce?
- ¿Cuál será el comportamiento de sesiones al cerrar/reabrir la aplicación propia (persistir, reconectar o terminar)?
