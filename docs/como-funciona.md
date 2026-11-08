# Cómo funciona

## Estructura

- `windows/`: widget para Windows (WPF de .NET Framework 4.8, construido por código, C# 5). `build.cmd` compila con el `csc.exe` incluido en Windows y `install.ps1` instala.
  - `Data/`: fuentes de datos (`CodexSource.cs`, `ClaudeUsageSource.cs`), sondeo (`UsagePoller.cs`) y detección de actividad (`ActivityMonitor.cs`).
  - `UI/`: anillos, logotipos, mascotas y columnas.
  - `App/WidgetWindow.cs`: ventana, menús, ocultación en los bordes y animación.
  - `System/`: instalación, ajustes, procesos auxiliares y llamadas a Windows.
  - `Tests/`: pruebas sin interfaz, red ni credenciales (`Tests\run-tests.cmd`).
- `mac/`: versión experimental para macOS.
- `.github/workflows/compilar.yml`: compila Windows y macOS, y corre las pruebas de Windows, con cada cambio.

## Datos y estados

- Se consulta cada 5 minutos. Ante errores, la espera crece progresivamente (1, 2, 4, 8, 16 y hasta 30 minutos) y respeta `Retry-After`.
- Un dato ausente o un error nunca se muestra como 0 %. Si ya pasó la hora de reinicio y aún no hay una lectura nueva, el anillo queda vacío con «Falta actualizar».
- Estados que se distinguen: reciente, antiguo (más de 15 minutos), sin conexión, demasiadas consultas, sesión requerida y error del servicio.
- Codex se consulta con un proceso `codex app-server` que se cierra al terminar. Va asociado a un Job Object de Windows para que nunca quede vivo si el widget se cierra.

## Archivos en el equipo

- Programa, fuentes y ajustes: `%LOCALAPPDATA%\UsageOrbit` (`settings.json` guarda la posición, la apariencia, la vista y los asistentes; nunca credenciales).
- Accesos directos: Escritorio, menú Inicio (`Usage Orbit`) y la carpeta de Inicio (`Usage Orbit (inicio).lnk`, que abre el widget con `--inicio-diferido` para esperar tres minutos).

## Datos de prueba

`UsageOrbit.exe --prueba <escenario>` abre una instancia aparte con datos ficticios, marcada con la banda «DATOS DE PRUEBA». Escenarios: `reciente`, `antiguo`, `sinconexion`, `sesion`, `sindatos`, `vencido`, `limite`, `mascotas`, `animacion`, `soloclaude`, `solocodex`.
