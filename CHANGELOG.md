# Bitácora de Cambios (Changelog) - openDynamic

Todas las modificaciones notables en este proyecto serán documentadas en este archivo.
El formato se basa en [Keep a Changelog](https://keepachangelog.com/es-ES/1.1.0/) y cumple con [SemVer](https://semver.org/).

## [0.3.0] - 2026-09-28 (Fase 2: Animación y Estados - M2)

### Añadido
- Motor de física de oscilador armónico amortiguado `Spring` en `OpenDynamic.Core.Animation` con integración semi-implícita de Euler y sub-pasos temporales (*sub-stepping* a $h = 1/240\text{ s}$), garantizando estabilidad numérica incondicional ante picos de $\Delta t$ o tirones de fotogramas sin divergencia.
- Conservación de inercia y velocidad continua (`Velocity`) al reorientar el destino dinámicamente (`Target`), posibilitando transiciones fluidas en vuelo sin tirones visuales.
- Máquina de estados finita `IslandStateMachine` en `OpenDynamic.Core.State` con estados formales `Hidden`, `Compact`, `Split` y `Expanded`, y reglas estrictas de transición (prohibición taxativa de `Hidden -> Expanded` directo, requiriendo paso por `Compact`).
- Definición y resolución de geometrías de cápsula con `IslandLayout` (Compact 160x36 R:18, Expanded 400x160 R:24, Split 260x36 R:18, Hidden 0x0 Op:0).
- Coordinador de animación `IslandAnimator` en `OpenDynamic.App.Animation` con gestión estricta del ciclo de vida de `CompositionTarget.Rendering`: suscripción exclusiva durante el vuelo de resortes y desuscripción inmediata al reposo (`IsSettled`), garantizando 0% de uso de CPU en reposo (Regla de oro 1). Acotación de $\Delta t$ a 50 ms.
- Eliminación del margen superior estático en `IslandWindow.xaml` (`Margin="0"` en `CapsuleBorder`), delegando toda la distancia al cálculo exacto de 8 DIPs de `IslandPositionCalculator`.
- Recorte de contenido interno mediante `ClipToBounds="True"` y `RectangleGeometry` adaptativa en `CapsuleBorder` para prevenir artefactos o fugas gráficas durante los rebotes elásticos.
- Interacciones directas de ratón sobre la cápsula:
  - Detección de hover sin sobrecoste de CPU: temporizador reactivo de 150 ms para expandir y 400 ms de margen al retirar el puntero antes de contraer a `Compact`.
  - Clic primario para alternar de forma bidireccional entre `Compact` y `Expanded`.
  - Rueda de ratón (`MouseWheel`) hacia arriba para contraer/ocultar (`Expanded -> Compact -> Hidden`) y hacia abajo para revelar/expandir.
- Panel visual interactivo `IslandDebugWindow` condicionado estrictamente a compilación `#if DEBUG` con telemetría en vivo del render loop, conmutadores manuales de estado y controles deslizantes de rigidez, amortiguamiento y perfiles.
- Batería de 33 nuevas pruebas unitarias en `OpenDynamic.Tests` (convergencia, inercia, estabilidad ante $\Delta t$ extremos, sub-stepping, transiciones FSM válidas e inválidas, y layout), elevando la cobertura a 47 pruebas automáticas.

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
