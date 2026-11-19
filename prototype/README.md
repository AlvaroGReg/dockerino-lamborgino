# Prototipo de integración ConPTY

Este spike valida la integración de WPF/.NET 10, EasyWindowsTerminalControl y Job Objects. No forma parte todavía del launcher ni debe ejecutarse desde uno de los repositorios del workspace de sitios.

## Ejecutar

Desde esta carpeta:

```powershell
dotnet run --project .\TerminalSpike\TerminalSpike.csproj
```

La aplicación abre `pwsh.exe -NoLogo -NoProfile -NoExit` integrado en EasyWindowsTerminalControl y asigna el proceso raíz a un Job Object exclusivo de la sesión. La barra de estado muestra el PID real y actualiza si el shell sigue activo o terminó (incluido el código de salida).

Para probar un comando real, abre una instancia por sesión y pasa su directorio:

```powershell
dotnet run --project .\TerminalSpike\TerminalSpike.csproj -- --command 'pwsh.exe -NoLogo -NoExit -Command "dotnet watch run --no-hot-reload"' --working-directory 'C:\ruta\al\backend' --title 'Backend'
dotnet run --project .\TerminalSpike\TerminalSpike.csproj -- --command 'pwsh.exe -NoLogo -NoExit -Command "npm start"' --working-directory 'C:\ruta\al\app' --title 'Frontend'
dotnet run --project .\TerminalSpike\TerminalSpike.csproj -- --command 'pwsh.exe -NoLogo -NoExit -Command "npm start"' --working-directory 'C:\ruta\al\backOffice' --title 'BackOffice'
```

`--log-file <ruta>` guarda la salida ConPTY para diagnóstico. `--title <texto>` diferencia las ventanas durante un spike de varias sesiones.

### Reproducción pasiva del arranque

```powershell
.\TerminalSpike\Test-TerminalStartup.ps1
```

El script compila Debug, abre una ventana titulada `TerminalSpike-Fase1-startup` y comprueba el PID real, conexión, handoff, prompt en la salida ConPTY y vida durante tres segundos. No inyecta teclas. Deja la ventana abierta para aceptar manualmente el teclado físico; indica un comando que crea un archivo marcador único, independiente del eco del comando. El log único queda en `%TEMP%` y contiene salida ConPTY y eventos de proceso. La salida registrada no prueba por sí sola el renderizado. Para aislar cmd: `-Command 'cmd.exe /K' -PromptPattern '[A-Za-z]:\\[^\r\n]*>' -WindowTitle 'TerminalSpike-Fase1-cmd'`. Usa `-NoBuild` si ya está compilado y hay otra ventana abierta: Windows bloquea la sustitución del ejecutable en uso.

La ventana puede quedar en primer plano al abrirse, como cualquier aplicación WPF normal. El script no intenta modificar foco, tamaño ni posición de otras ventanas.

## Diseño probado por compilación

- Se desconecta el `TermPTY` interno del control para evitar que inicie un shell propio. Tras `TermReady`, se conecta el `TermPTY` de la sesión a `EasyTerminalControl`; la salida leída antes de ese handoff se almacena y se reproduce.
- `JobObjectProcessFactory` usa la interfaz `IProcessFactory` pública del paquete y expone internamente PID/código de salida para diagnóstico.
- Crea el proceso con `CREATE_SUSPENDED`, asigna el proceso al Job Object y solo entonces llama a `ResumeThread`. No acepta ni expone PIDs en la UI.
- El Job Object se configura con `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`; el botón de parada termina únicamente ese Job Object.
- El atributo de ConPTY se conserva en la lista de atributos de creación requerida por Windows. La factoría upstream llama a `CreateProcess` con `bInheritHandles: false`; la factoría local debe compararse con ese comportamiento al diagnosticar el arranque.
- Versión fijada: `EasyWindowsTerminalControl 1.0.38`, con `net10.0-windows` disponible en el paquete.

## Resultados de la Fase 1

- **Build:** Debug y Release compilan en Windows con .NET SDK 10.0.404, sin errores ni advertencias. El análisis NuGet anterior no detectó paquetes vulnerables en el spike. No se cambiaron dependencias.
- **ConPTY/control:** el paquete 1.0.38 implementa `TermPTY` como backend ConPTY y conecta el `TerminalControl` al establecer `EasyTerminalControl.ConPTYTerm`; este último está hospedado como HWND. El README upstream confirma el flujo de salida/entrada y la limitación de airspace. Una salida recibida por `WriteToTerm` prueba la ruta API→ConPTY→log, no la captura física del HWND. El `AutoResize` del control conecta el cambio de tamaño del terminal con `TermPTY.Resize`, pero todavía falta la verificación visual de la aceptación de este spike.
- **Job Object:** la fábrica crea el shell suspendido, lo asigna al Job Object y después lo reanuda. La prueba inició un PowerShell descendiente; detener la sesión terminó ambos procesos y dejó vivo un proceso PowerShell ajeno. Cerrar la app también terminó la sesión.
- **Rutas:** se comprobó una ruta temporal con espacios y la ruta real `EmbutidosEzequiel\Ezequiel`, encontrando `Ezequiel.csproj` sin inferir el nombre de la carpeta desde el repo.
- **Comandos reales:** en sesiones separadas de ArvaHoteles, `dotnet watch run --no-hot-reload` inició el backend y atendió una petición local; `npm start` del frontend compiló correctamente con webpack en 36,4 s; `npm start` de backOffice ejecutó `prestart`/`combine`, compiló Angular y escuchó en `localhost:4200`. Se cerraron las tres sesiones y los puertos dejaron de escuchar.
- **Docker:** con un proyecto Compose de nombre aislado se inició Alpine en `up -d`. Cerrar la sesión de terminal dejó el contenedor ejecutándose; `docker compose stop` lo dejó detenido y `docker compose down` eliminó solo el contenedor y la red de prueba. No se usaron volúmenes ni se tocaron contenedores ajenos. Docker Desktop queda ejecutándose y la imagen `alpine:3.21` permanece en caché.
- **Limpieza de pruebas:** se restauraron los 93 archivos preexistentes de `wwwroot/dist`, los JSON versionados de backOffice y su caché Angular. El repositorio ArvaHoteles volvió a quedar limpio.

### Aceptación manual completada

La prueba física inicial quedó bloqueada: se mostró “Sesión asignada a un Job Object” aunque ese estado no probaba vida del proceso. La búsqueda inicial de `pwsh.exe` no permitía concluir el estado de la configuración anterior, que usaba `powershell.exe`. El registro inicial carecía de PID/código de salida y no observaba errores posteriores a `TermReady`.

**Causa aislada y corrección:** bajo el lanzador con `UseShellExecute=false`, el prompt aparecía en la salida de la consola del padre y no en ConPTY; `pwsh` terminaba con código 0. Cambiar únicamente `STARTUPINFO` para usar `STARTF_USESTDHANDLES` con los tres handles nulos hizo que Windows proveyera los handles de la pseudoconsola: en el mismo escenario el prompt aparece en ConPTY y `pwsh` permanece vivo. Sin ese cambio la regresión fallaba; con él pasa. Esto identifica el acoplamiento a los handles estándar del anfitrión en el escenario reproducido, no demuestra que toda posible pantalla negra tenga esa causa. Cambiar solo `bInheritHandles` no lo arregló. Se conserva la asignación suspendida al Job y la versión 1.0.38 sin cambios.

**Evidencia 19-11-2026:** Debug y Release compilan sin advertencias; `pwsh.exe -NoLogo -NoProfile -NoExit` y `cmd.exe /K` superaron la prueba pasiva de prompt y vida durante tres segundos. `cmd.exe /c exit 23` registró PID y `PROCESS_EXITED ... exitCode=23`. Una compilación intermedia falló porque nuestra propia ventana experimental mantenía bloqueado el ejecutable; tras cerrarla ambas compilaciones pasaron. Se eliminó la duplicación del log y se serializó la reproducción del buffer anterior a la conexión. El estado distingue proceso vivo/salido y conserva el código tras liberar handles; errores de arranque/lector se registran. `TermReady` sigue indicando únicamente que backend y escritor están preparados, no aceptación.

La prueba pasiva automatizada **no sustituye** los criterios de aceptación humana. El 19-11-2026 el usuario confirmó renderizado, teclado físico y redimensionado con ajuste de líneas, acompañados de capturas de `TerminalSpike-Fase1-ACEPTACION` y el PID 24436 activo. Escribió y ejecutó `Set-Content` en la terminal; se verificó que `%TEMP%\TerminalSpike-physical-24436.txt` contenía exactamente `TERMINALSPIKE_PHYSICAL_KEYBOARD_MARKER`. La segunda captura muestra el comando y el prompt tras estrechar la ventana. La prueba anterior de eventos simulados no se usa como aceptación física. Con estas evidencias y las pruebas anteriores se aprueba el bloqueo de Fase 1; no se ha comenzado la UI completa.

### Advertencias observadas, no causadas por el spike

- El backend muestra avisos NU1902/NU1903 por `System.Security.Cryptography.Pkcs 10.0.12`; no se actualizaron dependencias del sitio.
- Ejecutar los tres arranques a la vez produjo avisos transitorios de `dotnet watch` (“Too many changes at once”) mientras los frontends generaban archivos. El servidor backend llegó a iniciar y responder.
- Angular informó estilos Material duplicados, pero completó la compilación.

## Dependencias y licencia

- `EasyWindowsTerminalControl 1.0.38` declara MIT.
- Dependencias directas transitivas fijadas por ese paquete: `CI.Microsoft.Terminal.Wpf 1.25.260303002` y `Microsoft.Windows.Console.ConPTY 1.24.260710001`.
- El README upstream advierte que el backend WPF depende de paquetes/APIs beta y documenta la limitación de *airspace* del HWND hospedado.
- Antes de distribuir el launcher, revisar avisos/licencias de todo el grafo NuGet y preservar sus avisos aplicables.
