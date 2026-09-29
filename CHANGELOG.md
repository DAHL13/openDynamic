# Bitácora de Cambios (Changelog) - openDynamic

Todas las modificaciones notables en este proyecto serán documentadas en este archivo.
El formato se basa en [Keep a Changelog](https://keepachangelog.com/es-ES/1.1.0/) y cumple con [SemVer](https://semver.org/).

## [0.2.0] - 2026-09-28 (Fase 1: Ventana Overlay Flotante - M1)

### Añadido
- Ventana overlay `IslandWindow` en `OpenDynamic.App.Windowing` sin bordes (`WindowStyle="None"`, `AllowsTransparency="True"`, `Background="Transparent"`) con dimensiones fijas de 640x240 DIP y cápsula negra centrada (160x36 DIP).
- Configuración de estilos extendidos Win32 mediante `GetWindowLongPtr` y `SetWindowLongPtr`:
  - `WS_EX_TOOLWINDOW`: exclusión de la barra de tareas y del selector Alt+Tab.
  - `WS_EX_NOACTIVATE`: prevención de activación de ventana.
  - `WS_EX_TOPMOST`: elevación de la ventana sobre las aplicaciones estándar.
  - Prohibición explícita de `WS_EX_TRANSPARENT`, delegando el click-through al canal alfa nativo de WPF por píxel en capas.
- Intercepción de `WM_MOUSEACTIVATE` en el procedimiento de ventana (`WndProc`), retornando `MA_NOACTIVATE (3)` para garantizar que interactuar con la cápsula no robe el foco de teclado de la ventana activa.
- Sistema reactivo de z-order con `ForegroundWatcher` utilizando `SetWinEventHook` (`EVENT_SYSTEM_FOREGROUND`) para reafirmar `HWND_TOPMOST` sin sondeo ni temporizadores periódicos (CPU 0% en reposo).
- Servicio `WindowPositioner` para consulta de monitores, resolución y DPI mediante P/Invoke a Win32 (`MonitorFromWindow`, `MonitorFromPoint`, `GetMonitorInfo`, `GetDpiForMonitor`, `GetDpiForWindow`) sin dependencias de `System.Windows.Forms`.
- Reposicionamiento automático ante eventos del sistema `WM_DISPLAYCHANGE` y `WM_DPICHANGED`.
- Menú contextual temporal en la cápsula para cierre controlado de la aplicación durante desarrollo y pruebas.
- Modelos puros y motor de cálculo en `OpenDynamic.Core.Positioning`:
  - `IslandPositionCalculator`: algoritmo puro de cálculo de coordenadas físicas (X, Y, Ancho, Alto) y margen superior.
  - `MonitorArea`, `DisplayDpi`, `WindowDimensions` y `CalculatedWindowPlacement`.
- Batería de pruebas unitarias en `OpenDynamic.Tests.Positioning.IslandPositionCalculatorTests` validando escenarios a 100% (96 DPI), 125% (120 DPI), 150% (144 DPI) y 200% (192 DPI), configuraciones multimonitor y límites de entrada.

## [0.1.0] - 2026-09-28 (Fase 0: Andamiaje del proyecto)

### Añadido
- Solución principal `openDynamic.sln` con estructura modular de proyectos:
  - `src/OpenDynamic.Core`: Biblioteca `.NET 10` aislada, sin dependencias de Windows ni WPF.
  - `src/OpenDynamic.App`: Aplicación WPF con destino `net10.0-windows10.0.19041.0`.
  - `tests/OpenDynamic.Tests`: Suite de pruebas unitarias xUnit.
- Configuración global de compilación en `Directory.Build.props` (`Nullable=enable`, `ImplicitUsings=enable`, `LangVersion=latest`, `TreatWarningsAsErrors=false`).
- Archivos `.editorconfig` y `.gitignore` estándar para proyectos .NET.
- Manifiesto de aplicación Windows `app.manifest` con soporte de alta densidad DPI `PerMonitorV2`.
- Mecanismo de instancia única mediante `Mutex` nombrado (`Local\openDynamic-single-instance`).
- Sistema de registro estructurado y rotativo con `Serilog` y `Serilog.Sinks.File` en `%LocalAppData%\openDynamic\logs`.
- Manejadores globales de excepciones no controladas (`DispatcherUnhandledException`, `TaskScheduler.UnobservedTaskException`, `AppDomain.UnhandledException`) para proteger la estabilidad de la app ante fallos de widgets.
- Contenedor de Inyección de Dependencias con `Microsoft.Extensions.DependencyInjection` y ciclo de vida en segundo plano (`ShutdownMode=OnExplicitShutdown`).
- Integración de `CommunityToolkit.Mvvm` en el andamiaje del proyecto.
- Pruebas unitarias en `OpenDynamic.Tests` para verificar el entorno de testing y el aislamiento estricto de `Core` respecto a componentes de Windows.
- Flujo de integración continua automatizado en GitHub Actions (`.github/workflows/ci.yml`) ejecutándose en `windows-latest`.
