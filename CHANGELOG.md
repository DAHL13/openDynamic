# Bitácora de Cambios (Changelog) - openDynamic

Todas las modificaciones notables en este proyecto serán documentadas en este archivo.
El formato se basa en [Keep a Changelog](https://keepachangelog.com/es-ES/1.1.0/) y cumple con [SemVer](https://semver.org/).

## [0.6.0] - 2026-09-28 (Fase 5: Volumen, Batería y Pantalla Completa)

### Añadido
- Modelos de dominio y contratos puros de audio en `OpenDynamic.Core.Audio` (Regla de oro 5):
  - `IVolumeController`: Contrato puro para lectura, modificación relativa/absoluta, mute y eventos de volumen.
  - `VolumeChangedEventArgs`: Argumentos inmutables de cambio de volumen y estado de mute.
  - `VolumeIconType`: Categorización de icono de altavoz (`Muted`, `Low`, `Medium`, `High`).
  - `VolumeCalculator`: Lógica pura de cálculo y normalización `[0.0, 1.0]`, cálculo de pasos por rueda del ratón (`CalculateLevelStep`), porcentaje entero `[0, 100]` y selección de icono.
- Modelos de dominio y tracker de alertas de energía en `OpenDynamic.Core.Power` (Regla de oro 5):
  - `IBatteryMonitor`: Contrato para monitoreo reactivo de energía y eventos de alerta sin polling.
  - `BatterySnapshot`: Snapshot inmutable de estado de batería (`Percent`, `IsCharging`, `HasBattery`).
  - `BatteryAlertKind`: Categorías de alerta (`None`, `ChargerConnected`, `ChargerDisconnected`, `LowBattery`, `CriticalBattery`).
  - `BatteryThresholdTracker`: Tracker puro de cruce de umbrales con histéresis (2%) que garantiza una sola notificación por cruce al descender del 20% y del 10% sin re-notificar en fluctuaciones de voltaje, con reinicio al conectar el cargador o subir por encima del umbral.
- Detección geométrica y de estado en `OpenDynamic.Core.Windowing`:
  - `FullscreenDetector`: Lógica pura para evaluar estados `QUNS_*` de Windows y cobertura completa de ventana sobre monitor (juegos y reproductores sin bordes).
- Configuración extendida en `AppSettings`:
  - `DefaultVolumePriority = 80`, `VolumeTransientDurationSeconds = 2.0`.
  - `DefaultBatteryPriority = 90`, `BatteryChargerTransientDurationSeconds = 3.0`, `BatteryWarningTransientDurationSeconds = 3.0`.
  - `BatteryLowThresholdPercent = 20`, `BatteryCriticalThresholdPercent = 10`.
  - `HideOnFullscreen = true`.
- Servicio nativo de audio `VolumeService` en `OpenDynamic.App.Services`:
  - Implementación con NAudio (`MMDeviceEnumerator`, `AudioEndpointVolume`).
  - Soporte de cambio de dispositivo en caliente mediante registro COM nativo de `IMMNotificationClient`.
  - Todas las operaciones COM encapsuladas en `try/catch` con registro estructurado en Serilog (Regla de oro 4).
- Widget de volumen transitorio `VolumeWidget` y vistas en `OpenDynamic.App.Widgets.Volume`:
  - Actividad transitoria (Prioridad 80, duración 2.0 s).
  - Vistas `Compact`, `Expanded` y `Split` con iconos reactivos y barra de volumen elegante.
  - Interacción directa con la rueda del ratón sobre la cápsula para ajustar volumen y reiniciar inmediatamente el temporizador de gracia de 2 segundos.
- Servicio reactivo de energía `PowerService` en `OpenDynamic.App.Services`:
  - Escucha de mensajes `WM_POWERBROADCAST` en el `WndProc` de `IslandWindow`.
  - Registro de notificaciones Win32 `RegisterPowerSettingNotification` (`GUID_ACDC_POWER_SOURCE` y `GUID_BATTERY_PERCENTAGE_REMAINING`).
  - Cero polling (Regla de oro 1: CPU ~0% en reposo, ningún timer periódico).
- Widget de batería transitorio `BatteryWidget` y vistas en `OpenDynamic.App.Widgets.Battery`:
  - Actividad transitoria (Prioridad 90, duración 3.0 s).
  - Alertas visuales inmediatas al conectar o desconectar el cargador y al cruzar los umbrales de 20% y 10%.
- Detector de pantalla completa `FullscreenWatcher` en `OpenDynamic.App.Windowing`:
  - Detección combinada de `SHQueryUserNotificationState` y `EVENT_SYSTEM_FOREGROUND` con `SetWinEventHook`.
  - Detección de juegos exclusivos DirectX/Vulkan y aplicaciones maximizadas sin bordes (YouTube en navegador, VLC).
  - Ocultamiento inmediato de la isla (`SuspendForFullscreen`), cancelación de timers activos y congelación del animador (`SnapTo(Hidden)`). Restauración suave al salir de pantalla completa (`ResumeFromFullscreen`).
- Suite de 43 nuevas pruebas unitarias en `OpenDynamic.Tests` (131 pruebas en verde en total):
  - `VolumeCalculatorTests`: Normalización, pasos con rueda del ratón, porcentaje y categorías de icono.
  - `BatteryThresholdTrackerTests`: Alertas de cargador, cruce único de umbrales 20% y 10%, histéresis ante fluctuaciones, reseteo al conectar cargador e ignorar PCs sin batería.
  - `PriorityResolverPhase5Tests`: Precedencia de Volumen (80) sobre Media (30), Batería (90) sobre Volumen (80), y auto-expiración / reinicio de tiempo de vida.
  - `FullscreenDetectorTests`: Detección de estados `QUNS_*` y evaluación de geometría de ventana vs monitor.

## [0.5.0] - 2026-09-28 (Fase 4: Widget Multimedia - GSMTC)

### Añadido
- Modelos de dominio y contratos multimedia puros en `OpenDynamic.Core.Media` (Regla de oro 5):
  - `MediaPlaybackStatus`: Estados de reproducción (`Closed`, `Opened`, `Changing`, `Stopped`, `Playing`, `Paused`).
  - `MediaPlaybackCapabilities`: Capacidades interactivas de la sesión (`CanPlay`, `CanPause`, `CanTogglePlayPause`, `CanSkipNext`, `CanSkipPrevious`, `CanSeek`).
  - `MediaPlaybackInfo`, `MediaTimelineInfo` y `MediaPropertiesInfo`: Snapshots inmutables de reproducción, límites de tiempo y metadatos libres de tipos de UI.
  - `IMediaSession` e `IMediaService`: Contratos de abstracción para sesiones y servicios de transporte multimedia de Windows.
  - `MediaProgressCalculator`: Lógica pura de cálculo de progreso extrapolado localmente, cálculo de ratio normalizado `[0.0, 1.0]` y formateo de tiempo (`mm:ss`, `h:mm:ss`).
  - `MediaActivityController`: Coordinador de ciclo de vida de sesión que gestiona la regla de tiempo de gracia de 10 segundos tras pausa, reanudación y terminación inmediata al cerrar el reproductor.
  - `AppSettings` en `OpenDynamic.Core.Settings`: Configuración con `MediaPauseGracePeriodSeconds = 10` y `DefaultMediaPriority = 30`.
- Servicio de transporte multimedia nativo `MediaService` y sesión `WinRtMediaSession` en `OpenDynamic.App.Services`:
  - Conexión a `GlobalSystemMediaTransportControlsSessionManager` (WinRT `Windows.Media.Control`).
  - Todas las invocaciones a WinRT/COM encapsuladas en `try/catch` con registro estructurado en Serilog (Regla de oro 4).
  - Desuscripción meticulosa (`-=`) de eventos al cambiar de sesión o cerrar reproductores para evitar fugas de memoria y callbacks sobre sesiones muertas.
  - Extracción de miniatura (`Thumbnail`) con lectura asíncrona a memoria local y ejecución de `BitmapImage.Freeze()` antes de su entrega al hilo de UI.
  - Activación de aplicación emisora (`TryActivateApp`) mediante `SourceAppUserModelId` o nombre de ejecutable (`SetForegroundWindow`, `ShowWindow`).
- Widget multimedia `MediaWidget` en `OpenDynamic.App.Widgets.Media`:
  - Vista `Compact`: carátula miniatura (24x24) con esquinas redondeadas y escalado de alta calidad, título y artista con recorte elíptico, y mini barras de onda de audio.
  - Vista `Expanded`: carátula grande (52x52), título, artista, álbum, barra de progreso interactiva (seek en vivo por clic o arrastre), tiempos `mm:ss / mm:ss` y controles de transporte (Anterior, Play/Pausa central destacado y Siguiente).
  - Vista `Split`: carátula circular (26x26) adaptada al satélite multitasking de 36x36 con insignia verde activa en reproducción.
  - Regla de oro 1 (CPU ~0% en reposo): Prohibición total de sondeo (polling). El temporizador `DispatcherTimer` de 1s de extrapolación de progreso se activa ÚNICAMENTE cuando la cápsula está en estado `Expanded` Y la sesión está en reproducción (`Playing`). En `Compact`, `Split`, `Hidden` o en Pausa, el timer se detiene de inmediato.
  - Tiempo de gracia de 10 segundos tras pausa: al pausar, la actividad se mantiene visible 10s antes de pasar a `IsActive = false`. Si se reanuda antes de los 10s, se cancela el temporizador y continúa activa; si el reproductor se cierra, la actividad se retira de inmediato.
- Batería de 26 nuevas pruebas unitarias en `OpenDynamic.Tests` (total 88 en verde):
  - Extrapolación de posición en reproducción, pausa, desbordamiento de límites y formato de tiempo en `MediaProgressCalculatorTests`.
  - Simulación de transiciones Play -> Pause con timeout de 10s, reanudación y cierre inmediato en `MediaActivityControllerTests`.
  - Resolución de prioridades con el nuevo widget Media en `PriorityResolverMediaTests`.

## [0.4.0] - 2026-09-28 (Fase 3: Arquitectura de Widgets y Orchestrator - Base M4)

### Añadido
- Modelos de dominio y contratos de actividad puros en `OpenDynamic.Core.Widgets` (Regla de oro 5):
  - `IActivitySource`: Contrato para emisores de actividades (`Id`, `Priority`, `IsActive`, `IsTransient`, `LastActivatedUtc`, `TransientDuration`, `Changed`).
  - `ActivityPriority`: Niveles estándar de prioridad (`Idle = 0`, `Low = 10`, `Normal = 50`, `High = 100`, `TransientNotice = 200`, `Critical = 500`).
  - `IslandActivity`: Modelo inmutable de snapshot de actividad.
  - `WidgetDisplayMode`: Enumeración de modos de renderizado (`Compact`, `Split`, `Expanded`).
  - `PriorityResult`: Resultado inmutable de resolución con fuente primaria, secundaria, estado sugerido y próximo tiempo de expiración.
- Servicio de resolución de prioridades determinista `PriorityResolver` en `OpenDynamic.Core.Widgets`:
  - Evaluación por prioridad numérica descendente (mayor valor gana).
  - Desempate determinista por recencia de activación (`LastActivatedUtc`) y por identificador (`Id`).
  - Pre-emption de actividades transitorias con tiempo de vida o expiración: toman el control como fuente primaria exclusiva en `Compact`, y al expirar devuelven automáticamente el control a las actividades en segundo plano.
  - Soporte de lista vacía o fuentes inactivas retornando estado inactivo/reposo (`Hidden`).
- Interfaz `IIslandWidget` y clase base `IslandWidgetBase` en `OpenDynamic.App.Widgets`:
  - Integración de `IActivitySource` con `IDisposable` y fábricas de vistas WPF (`CreateCompactView()`, `CreateExpandedView()`, `CreateSplitView()`).
  - Métodos de ciclo de vida: `Initialize()`, `OnExpand()`, `OnCollapse()`.
  - Integración MVVM con `CommunityToolkit.Mvvm` (`ObservableObject`).
- Bus de eventos desacoplado con `WeakReferenceMessenger` de CommunityToolkit.Mvvm:
  - Mensajes de actividad (`ActivityChangedMessage`, `WidgetRegisteredMessage`, `WidgetUnregisteredMessage`, `ExpandRequestedMessage`, `CollapseRequestedMessage`, `IslandStateChangedMessage`) que evitan fugas de memoria por referencias fuertes.
- Orquestador central `IslandOrchestrator` en `OpenDynamic.App.Orchestration`:
  - Autoridad EXCLUSIVA sobre transiciones en `IslandStateMachine`.
  - Coordinación de prioridades de widgets, entrega de vistas a `IslandView` y gestión de temporizadores de expiración de transitorios de un solo disparo (CPU ~0% en reposo).
  - Aislamiento estricto de fallos (Regla de oro 4): toda inicialización y creación de vistas de widgets está protegida en bloques `try/catch` con registro en Serilog y puesta en cuarentena de widgets con excepciones sin desestabilizar la cápsula ni la aplicación.
- Componente de presentación `IslandView.xaml` en `OpenDynamic.App.Views`:
  - Cápsula principal con animación y recorte elástico.
  - Soporte visual nativo para modo multitasking `Split`: cápsula principal más burbuja satélite circular (`36x36` con radio 18) adyacente con espacio transparente para clics intermedios.
  - Transiciones de opacidad suaves (cross-fade) al conmutar contenido entre widgets.
- Widgets de demostración `DemoWidgetA` y `DemoWidgetB` condicionados estrictamente a compilación `#if DEBUG`:
  - Simulación de actividad continua (Música y Temporizador) y avisos transitorios de 3 segundos con auto-expiración.
  - Controles interactivos y telemetría en tiempo real en `IslandDebugWindow`.
  - Cero código de demostración en compilaciones Release.
- Suite de 15 pruebas unitarias exhaustivas en `OpenDynamic.Tests.Widgets.PriorityResolverTests`, elevando el total de pruebas a 62 en verde.

## [0.3.0] - 2026-09-28 (Fase 2: Animación y Estados - M2)

### Añadido
- Motor de física de oscilador armónico amortiguado `Spring` en `OpenDynamic.Core.Animation` con integración semi-implícita de Euler y sub-pasos temporales (*sub-stepping* a $h = 1/240\text{ s}$), garantizando estabilidad numérica incondicional ante picos de $\Delta t$ o tirones de fotogramas sin divergencia.
- Conservación de inercia y velocidad continua (`Velocity`) al reorientar el destino dinámicamente (`Target`), posibilitando transiciones fluidas en vuelo sin tirones visuales.
- Máquina de estados finita `IslandStateMachine` en `OpenDynamic.Core.State` con estados formales `Hidden`, `Compact`, `Split` y `Expanded`, y reglas estrictas de transición (prohibición taxativa de `Hidden -> Expanded` directo, requiriendo paso por `Compact`).
- Definición y resolución de geometrías de cápsula con `IslandLayout` (Compact 160x36 R:18, Expanded 400x160 R:24, Split 260x36 R:18, Hidden 80x4 R:2 Op:0.01).
- Eliminación de `Visibility.Collapsed` en la cápsula para mantener activa la capacidad de hit-testing en WPF, permitiendo que la micro-muesca de reposo en `Hidden` (80x4 DIPs a 1% de opacidad) detecte `MouseWheel` (scroll down), `MouseEnter` o clic izquierdo para restaurar la cápsula a `Compact` de forma inmediata.
- Coordinador de animación `IslandAnimator` en `OpenDynamic.App.Animation` con gestión estricta del ciclo de vida de `CompositionTarget.Rendering`: suscripción exclusiva durante el vuelo de resortes y desuscripción inmediata al reposo (`IsSettled`), garantizando 0% de uso de CPU en reposo (Regla de oro 1). Acotación de $\Delta t$ a 50 ms.
- Eliminación del margen superior estático en `IslandWindow.xaml` (`Margin="0"` en `CapsuleBorder`), delegando toda la distancia al cálculo exacto de 8 DIPs de `IslandPositionCalculator`.
- Recorte de contenido interno mediante `ClipToBounds="True"` y `RectangleGeometry` adaptativa en `CapsuleBorder` para prevenir artefactos o fugas gráficas durante los rebotes elásticos.
- Interacciones directas de ratón sobre la cápsula:
  - Detección de hover sin sobrecoste de CPU: temporizador reactivo de 150 ms para expandir y 400 ms de margen al retirar el puntero antes de contraer a `Compact`.
  - Detección de `MouseEnter` en la micro-muesca de `Hidden` para despertar automáticamente la isla a `Compact`.
  - Clic primario para alternar de forma bidireccional entre `Compact` y `Expanded` (o restaurar desde `Hidden`).
  - Rueda de ratón (`MouseWheel`) hacia arriba para contraer/ocultar (`Expanded -> Compact -> Hidden`) y hacia abajo para revelar/expandir (`Hidden -> Compact -> Expanded`).
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
