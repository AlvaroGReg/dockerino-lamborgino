# Análisis y decisiones: lanzador de proyectos

## 1. Contexto y necesidad

El workspace contiene varios sitios ASP.NET Core, cada uno con un frontend Vue en `app` y un backoffice Angular en `backOffice`. Para trabajar en un proyecto hay que abrir sus tres procesos y, en ocasiones, actualizar primero los paquetes core del frontend.

La experiencia deseada es:

1. Una burbuja flotante en pantalla despliega la lista de proyectos.
2. Se selecciona un proyecto y se muestra su rama actual, el selector de rama y dos acciones de arranque.
3. Una acción arranca backend, frontend y backoffice.
4. La otra actualiza primero el core del frontend y, si todo va bien, arranca los tres procesos.
5. Los procesos aparecen como sesiones de terminal integradas e interactivas, accesibles desde el menú flotante.

El alcance aquí es local y de desarrollo. No se propone desplegar, hacer `pull`, instalar dependencias automáticamente al arranque normal, ni cambiar archivos de solución.

## 2. Viabilidad

**Viabilidad: alta, con una dependencia operativa clara.**

- **Inventario:** hay 11 repositorios en la raíz del workspace y los 11 tienen `app/package.json` y `backOffice/package.json`.
- **Backend:** los proyectos web principales observados usan `net10.0`, por lo que `dotnet watch run --no-hot-reload` es coherente con el entorno actual.
- **Frontend y backoffice:** ambos declaran `npm start`. En backoffice, npm ejecutará además el hook `prestart` (`npm run combine`).
- **Ramas:** Git permite consultar ramas y cambiar la rama del repositorio seleccionado sin salir del launcher.
- **Terminal integrada:** ConPTY permite conectar shells y procesos a pseudo-consolas que la aplicación puede alojar. Job Objects son la opción que se investigará para agrupar y detener los procesos hijos de cada sesión.
- **UI flotante:** WPF permite una ventana sin marco, siempre visible y pequeña que puede expandirse desde un control tipo flecha.

La principal dificultad técnica es integrar una terminal completa, conservar interactividad/ANSI/redimensionado y administrar con seguridad los árboles de procesos. ConPTY y Job Objects deben validarse con los comandos reales. La actualización de core también puede fallar por credenciales, red o dependencias, y debe tener un flujo de error visible.

## 3. Decisiones recomendadas

### D1. Aplicación de escritorio Windows, no web local

**Decisión:** crear una app nativa local.

Una página web normal no puede operar libremente sobre repositorios del disco ni crear pseudo-consolas locales con garantías. Una aplicación de escritorio puede alojar la UI de terminal y administrar procesos locales.

### D2. WPF sobre .NET 10 como hipótesis

**Decisión provisional:** crear una aplicación de escritorio propia; WPF sobre .NET 10 es la hipótesis inicial. La integración terminal se prototipará con EasyWindowsTerminalControl y su viabilidad debe demostrarse antes de construir la UI completa.

WPF resuelve de forma directa la ventana flotante y la integración Windows/.NET. La terminal integrada es un riesgo técnico aparte: evaluar un control reutilizable o una integración nativa de ConPTY, sus dependencias, compatibilidad con .NET 10, licencias y mantenimiento antes de fijar la decisión tecnológica.

**Alternativas descartadas para el MVP:**

- **Web/Vue:** buena para interfaces web, pero requiere un host local con permisos de sistema y no satisface por sí sola el caso de ventana flotante.
- **Electron o Tauri:** viables técnicamente, pero suman stack y empaquetado para una interfaz pequeña exclusivamente Windows.
- **PowerShell/WPF como script:** podría servir para una prueba rápida, pero resulta menos mantenible como aplicación con configuración, validaciones y manejo consistente de errores.

### D3. Ventana pequeña y menú flotante de sesiones

**Decisión:** iniciar como una ventana compacta sin marco, anclada a una esquina configurable y siempre visible; la flecha abre el menú flotante de proyectos, acciones y sesiones de terminal. Desde el menú se podrán abrir terminales asociadas a proyectos/comandos y volver a sesiones existentes.

La UI del menú y las vistas de terminal podrán plegarse/cambiarse sin destruir sesiones activas. La aplicación tendrá responsabilidad explícita sobre la vida de sus procesos.

### D4. Descubrimiento restringido a las carpetas de primer nivel

**Decisión:** la ruta raíz del workspace se configura por proyecto y se persiste. La ubicación actualmente conocida es `../../00-WEBS-CMS`, relativa a la carpeta de esta documentación, y debe proponerse como valor inicial. Buscar solo en directorios hijos inmediatos de la ruta configurada. Considerar un directorio como proyecto únicamente cuando se valide una estructura con:

- una raíz Git (`.git` o `git rev-parse --show-toplevel`);
- un `package.json` bajo una carpeta `app`;
- un `package.json` bajo `backOffice`;
- un proyecto ASP.NET Core principal (`.csproj`) en la carpeta del sitio.

No recorrer todo el árbol. Así se evita tratar `node_modules`, `bin`, `obj`, `Shared`, proyectos auxiliares y paquetes comunes como proyectos seleccionables. El nombre visible será el nombre del repositorio; las rutas internas serán metadatos descubiertos.

En la estructura actual existen 11 candidatos. La carpeta de aplicación de EmbutidosEzequiel es `Ezequiel`, no `EmbutidosEzequiel`, lo que confirma que las rutas deben descubrirse.

### D5. Git se opera solo en el repositorio seleccionado

**Decisión:** usar Git CLI en la raíz del repositorio seleccionado. Mostrar la rama actual y listar ramas locales y remotas en grupos diferenciados, cada grupo con icono propio y separados por una barra visual (referencia de interacción: extensión GitHub de VS Code). Elegir una rama local cambia a ella; elegir una remota debe iniciar una acción explícita para crear/cambiar a una rama local de seguimiento, no hacer un checkout remoto implícito.

**Guardas:**

- No hacer `fetch`, `pull`, `push`, `stash`, commit ni reset automáticamente.
- Si hay cambios locales que puedan bloquear el cambio, mostrar el estado y dejar que Git rechace la operación; nunca ocultar cambios con un stash automático.
- Si la rama tiene que crearse desde una rama remota, pedir confirmación.
- Actualizar la rama mostrada después del cambio y mostrar el error de Git sin perder la selección del proyecto.

### D6. Terminales interactivas integradas con ConPTY

**Decisión:** la plataforma objetivo es Windows y la aplicación tendrá su propio control de terminal interactiva. Se empezará con [EasyWindowsTerminalControl](https://github.com/MitchCapper/EasyWindowsTerminalControl), encapsulado tras una interfaz/adaptador propio. ConPTY conecta cada shell/proceso a una pseudo-consola; el control renderiza salida VT/ANSI, captura teclado y comunica cambios de tamaño. PowerShell, `pwsh` u otros shells serán procesos alojados dentro de la terminal, no terminales externas.

Si el control no satisface requisitos o no permite integrar el ciclo de vida seguro, se mantendrá un fork propio y se adaptará allí. La aplicación no dependerá directamente de los tipos concretos del paquete fuera del adaptador. Fijar versiones y revisar sus dependencias/licencia.

Desde el menú flotante se abrirán terminales por proyecto/comando y se podrán mantener varias sesiones. El arranque de backend, frontend y backoffice puede crear tres sesiones coordinadas; el botón naranja ejecuta primero `update-core:latest` en la sesión frontend y, solo si termina bien, continúa con `npm start`.

### D7. Propiedad y parada de procesos por sesión

**Decisión:** cada sesión tendrá un identificador interno y un grupo de procesos propio. Investigar Job Objects para asignar el proceso raíz y sus descendientes desde el inicio, y detener solo el grupo asociado a esa sesión. Probar la factoría de procesos/extensión que ofrece el control; si no deja asignar el proceso al Job Object en el momento requerido, hacer un fork y adaptar su creación de procesos.

No exponer `taskkill` ni una operación genérica por PID a la UI/CLI. La prueba debe cubrir descendientes de `dotnet`, `npm` y sus shells, cierre de sesión, cierre de la aplicación y fallos de creación. La parada segura es parte del objetivo, pero su implementación depende del resultado del spike.

**Docker requiere gestión adicional:** `docker compose` es un cliente local, pero los contenedores los crea y mantiene el daemon de Docker; por tanto, no pertenecen al Job Object de la terminal. El prototipo distinguirá entre interrumpir/cerrar el cliente y parar el proyecto de contenedores. Las pruebas usarán un proyecto Compose aislado; para detenerlo se utilizará una operación explícita y acotada al proyecto (por ejemplo, `docker compose stop`/`down`, nunca `down --volumes` por defecto). No se terminará Docker Desktop ni se detendrán contenedores ajenos.

### D8. Dos acciones con semántica explícita

**Arranque normal**

1. Validar que las carpetas y comandos existen.
2. Abrir tres sesiones de terminal integradas.
3. Ejecutar backend, frontend y backoffice.

**Actualizar core y arrancar**

1. Validar que existe `npm` y que `app` contiene el script `update-core:latest`.
2. Abrir tres sesiones. Backend y backoffice comienzan en sus directorios; la sesión frontend inicia la actualización.
3. En la sesión frontend, ejecutar `npm run update-core:latest` y comprobar su código de salida antes de ejecutar `npm start`.
4. Si falla la actualización, conservar el error visible y no ejecutar `npm start` del frontend. Backend y backoffice ya iniciados continúan funcionando.
5. Si termina correctamente, iniciar `npm start` en la sesión frontend.

No ejecutar simultáneamente una instalación en `app/node_modules` y `npm start`. Mantener actualización e inicio del frontend en la misma sesión permite ver la salida completa y evita lanzar el frontend antes de que termine la actualización.

### D9. Arranque normal y actualización de core son acciones diferenciadas

**Decisión:** mostrar dos botones con semántica y color diferenciados: verde para arranque normal y naranja para `update-core:latest` y arranque. No solicitar confirmación; el usuario conoce el alcance de la actualización. La interfaz debe explicar junto al botón naranja que la actualización puede modificar/reinstalar dependencias.

La razón es el comportamiento actual de `update-core:latest`: ejecuta un actualizador de paquetes core y luego `npm run phoenix`; el script `phoenix` borra `node_modules` y `package-lock.json` y reinstala. No es equivalente a un arranque normal ni se debe ejecutar de forma implícita.

### D10. CodeDeck como referencia, no como base directa

**Decisión:** usar el clon indicado en `../CodeDeck` para estudiar flujos, interfaz y conceptos de proyectos, comandos y conjuntos de lanzamiento. El producto propio tendrá una arquitectura adaptada a terminales ConPTY y sesiones con propiedad de procesos; CodeDeck no es la base directa ni se modificará como parte de este proyecto.

El clon está disponible en `../CodeDeck`, relativo a esta documentación. Se utilizará como referencia al revisar flujos, interfaz y arquitectura existente.

## 4. Flujo funcional propuesto

```text
Launcher → seleccionar proyecto → ver rama
                            ↓
                 cambiar rama (opcional)
                   ↙                 ↘
       Arrancar normal         Actualizar y arrancar
            ↓                         ↓
       abrir 3 sesiones       actualizar core en app
             ↓                  ├─ error → no iniciar frontend
      3 procesos              └─ éxito → iniciar frontend
                               backend y backoffice ya iniciados
```

Las sesiones continúan al plegar el menú; la vista de terminal permite seguir interactuando con ellas. La aplicación conserva estado/salida útil y ofrece una parada limitada a la sesión propia, si Job Objects supera las pruebas.

## 5. Riesgos y mitigaciones

| Riesgo | Mitigación |
|---|---|
| Rutas diferentes entre repositorios | Descubrir y validar directorios; no inferir nombres internos a partir del repo. |
| Directorios auxiliares detectados como sitios | Escaneo de primer nivel y validación de Git, `app`, `backOffice` y `.csproj`. |
| Cambiar de rama con cambios locales | Mostrar estado, no usar stash automático y conservar los errores de Git. |
| Terminal ConPTY incompleta (resize, ANSI, teclado o encoding) | Prototipo temprano con shells y procesos reales; usar un control probado y documentar sus dependencias/licencia. |
| Descendientes de procesos no quedan bajo control | Asignación temprana a Job Object y pruebas de árboles `dotnet`/`npm`; no implementar stop si se puede escapar el árbol. |
| Detener el cliente Docker deja contenedores activos | Identificar de forma separada procesos de cliente y proyectos/recursos Compose; operar solo sobre el proyecto iniciado por la sesión y probar `stop` frente a `down`. |
| Actualización lenta o fallida | Esperar el exit code, mostrar progreso y bloquear el arranque del frontend si falla. |
| Instalar core modifica dependencias | Botón naranja separado y explicación visible de los efectos; no hay confirmación modal. |
| Sesiones duplicadas | Identificar cada sesión, mostrarla en el menú flotante y evitar dobles clics durante el lanzamiento. |

## 6. No incluido en el MVP

- Crear, clonar o eliminar repositorios.
- `fetch`, `pull`, `push`, commits o stash.
- Seleccionar versiones de Node/.NET por proyecto.
- Instalar dependencias faltantes de forma automática.
- Administrar variables de entorno o secretos.
- Terminales externas como UX principal; pueden abrirse manualmente fuera de la aplicación, pero no forman parte del ciclo de vida administrado.
- Soporte multiplataforma.
- Cambiar la configuración o los scripts de los proyectos existentes.

## 7. Conclusión

El producto propio se inspirará en los flujos de CodeDeck, pero tendrá terminales interactivas integradas con ConPTY, abiertas y gestionadas desde el menú flotante. La hipótesis es **WPF + .NET 10 + Git CLI + ConPTY + Job Objects**. Antes de construir la UI completa hay que probar terminal rendering/input/resize, el control de procesos hijos y la viabilidad del control de terminal seleccionado. Cualquier interfaz futura (incluida CLI para IA) debe pedir acciones de sesión por identificador gestionado, nunca terminar PIDs arbitrarios.
