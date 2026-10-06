# Registro de Decisiones de Arquitectura (ADR) - openDynamic

## ADR-001: Definición del Stack Tecnológico Fijo

- **Estado:** Aceptado
- **Fecha:** 2026-09-28
- **Contexto:** openDynamic es una Dynamic Island para Windows (WPF, .NET 10), ligera, contextual y con animaciones de resorte.
- **Decisiones del Stack:**
  - **Lenguaje / Runtime:** C# 13+, .NET 10 (LTS).
  - **UI:** WPF orientado a `net10.0-windows10.0.19041.0` para acceso nativo a proyecciones modernas de WinRT.
  - **MVVM:** `CommunityToolkit.Mvvm` (8.4.x).
  - **Audio:** `NAudio` (`MMDeviceEnumerator`, `AudioEndpointVolume`) (a incorporar en la fase correspondiente).
  - **Multimedia:** WinRT `Windows.Media.Control` (GSMTC) (a incorporar en la fase correspondiente).
  - **Bandeja del sistema (Tray):** `H.NotifyIcon.Wpf` (a incorporar en la fase correspondiente).
  - **Logging:** `Serilog` + `Serilog.Sinks.File` escribiendo en `%LocalAppData%\openDynamic\logs`.
  - **Configuración:** `System.Text.Json` serializado en `%AppData%\openDynamic\settings.json`.
  - **Pruebas:** `xUnit`.
  - **Instalador:** `Inno Setup`.
- **Prohibiciones Explícitas:**
  - Sin WinForms.
  - Sin Electron ni WebView2/HTML wrapper.
  - Sin SQLite ni bases de datos pesadas locales.
  - Sin Lottie ni runtimes de renderizado costosos.
- **Reglas de Oro:**
  - Consumo de CPU ~0% en reposo: ningún temporizador o bucle de renderización activo cuando la isla no está animándose ni mostrando widgets activos.
  - Sin hooks globales de bajo nivel (`WH_MOUSE_LL`, `WH_KEYBOARD_LL`).
  - Sin inyección en procesos ajenos.
  - Toda llamada a APIs WinRT/COM debe encapsularse en bloques `try/catch` con registro de log estructurado.

---

## ADR-002: Aislamiento del Proyecto Core

- **Estado:** Aceptado
- **Fecha:** 2026-09-28
- **Contexto:** La lógica central (animación, máquinas de estado, resolución de prioridades) debe ser portable y libre de dependencias de subsistemas gráficos o específicos de plataforma.
- **Decisión:** `OpenDynamic.Core` utiliza `net10.0` puro sin dependencias de Windows ni de WPF (`PresentationFramework`, `WindowsBase`, `System.Windows.Forms`, etc.). La separación es validada mediante pruebas unitarias automatizadas en `OpenDynamic.Tests`.

---

## ADR-003: Mecanismo de Instancia Única

- **Estado:** Aceptado
- **Fecha:** 2026-09-28
- **Contexto:** Se requiere prevenir la ejecución múltiple de la aplicación en la misma sesión del usuario.
- **Decisión:** Se implementa `SingleInstanceManager` utilizando un `System.Threading.Mutex` con nombre `Local\openDynamic-single-instance`. El prefijo `Local\` garantiza el ámbito por sesión interactiva sin requerir elevación de privilegios UAC. Si el mutex ya existe, la nueva instancia termina en silencio.

---

## ADR-004: Estrategia de Resiliencia ante Excepciones no Controladas

- **Estado:** Aceptado
- **Fecha:** 2026-09-28
- **Contexto:** Fallos puntuales en widgets de terceros o eventos de Windows no deben tirar abajo la aplicación completa.
- **Decisión:** Se configuran manejadores globales:
  - `DispatcherUnhandledException`: Registra el error en Serilog y establece `e.Handled = true`.
  - `TaskScheduler.UnobservedTaskException`: Registra el error y ejecuta `e.SetObserved()`.
  - `AppDomain.UnhandledException`: Registra errores a nivel fatal antes de la terminación en casos irrecuperables.

---

## ADR-005: Licencia y Repositorio Remoto

- **Estado:** Aceptado
- **Fecha:** 2026-09-28
- **Contexto:** El repositorio remoto en `https://github.com/DAHL13/openDynamic` se encontraba vacío.
- **Decisión:** Se conecta el repositorio local al remoto existente `origin` sin crear un nuevo repositorio. No se añade un archivo de licencia arbitrario por cuenta del agente para respetar la decisión legal y de autoría del usuario; se documenta aquí.

---

## ADR-006: Ventana Overlay con Renderizado por Capas, Click-Through por Píxel y Topmost Reactivo

- **Estado:** Aceptado
- **Fecha:** 2026-09-28
- **Contexto:** La Fase 1 (M1) requiere una ventana flotante para la Dynamic Island con alta tasa de respuesta, ausencia de parpadeo, z-order permanente sobre otras ventanas y capacidad de recibir clics solo en la cápsula visible sin robar foco ni bloquear la interacción con el escritorio en las zonas vacías.
- **Decisiones:**
  - **Tamaño físico fijo:** Se fija `IslandWindow` en 640x240 DIP con `WindowStyle="None"`, `AllowsTransparency="True"` y `Background="Transparent"`. La ventana nativa jamás se redimensiona durante las animaciones (Regla de oro 6); el contenido interno (cápsula) se escalará y transformará en fases posteriores.
  - **Click-through sin WS_EX_TRANSPARENT:** Prohibido el estilo `WS_EX_TRANSPARENT`. El paso de clics a las aplicaciones subyacentes se confía al alfa 0 (completamente transparente) del motor de ventanas por capas de Windows y WPF. La cápsula mantiene un fondo opaco (`#FF000000`), garantizando que capture eventos de ratón.
  - **No activación ni robo de foco:** Se configuran en el HWND los estilos `WS_EX_TOOLWINDOW` (exclusión de Alt+Tab y barra de tareas) y `WS_EX_NOACTIVATE`. En el procedimiento de ventana (`WndProc`), se intercepta `WM_MOUSEACTIVATE` retornando `MA_NOACTIVATE (3)` para no robar el foco de teclado al hacer clic en la cápsula.
  - **Topmost reactivo sin timers:** Se implementa `ForegroundWatcher` mediante `SetWinEventHook` escuchando `EVENT_SYSTEM_FOREGROUND` con `WINEVENT_SKIPOWNPROCESS`. La posición `HWND_TOPMOST` se reafirma únicamente cuando cambia la ventana activa, consumiendo 0% CPU en reposo (Regla de oro 1).
  - **Cero dependencias de WinForms:** Todo el cálculo de monitores, resolución y DPI se efectúa mediante P/Invoke a Win32 (`MonitorFromWindow`, `MonitorFromPoint`, `GetMonitorInfo`, `GetDpiForMonitor`, `GetDpiForWindow`) en `NativeMethods`.
  - **Aislamiento del cálculo en Core:** El cálculo geométrico y la compensación de escala DPI se aíslan en la clase estática pura `IslandPositionCalculator` dentro de `OpenDynamic.Core.Positioning`, permitiendo pruebas unitarias sin dependencias de plataforma al 100%, 125%, 150% y 200% de escala.

---

## ADR-007: Motor de Física de Resortes con Sub-Stepping, Máquina de Estados Finita y Ciclo de Render Reactivo

- **Estado:** Aceptado
- **Fecha:** 2026-09-28
- **Contexto:** La Fase 2 (M2) demanda animaciones elásticas orgánicas (rebote leve, conservación de inercia), reglas formales de transición entre estados visuales (`Hidden`, `Compact`, `Split`, `Expanded`) y el cumplimiento estricto de la Regla de Oro 1 (consumo de CPU ~0% en reposo absoluto).
- **Decisiones:**
  - **Física desacoplada en Core (Regla de oro 5):** Se implementa `Spring` en `OpenDynamic.Core.Animation` con integración semi-implícita de Euler (Euler simpléctico) y sub-pasos temporales (*sub-stepping* a $h = 1/240\text{ s}$) para evitar divergencia numérica ante variaciones o picos de $\Delta t$.
  - **Conservación de inercia:** Al cambiar dinámicamente de destino (`Target`), se preserva la velocidad acumulada (`Velocity`), evitando saltos visuales o interrupciones abruptas de trayectoria.
  - **Parámetros elásticos base:** Rigidez $k = 320$, amortiguamiento $c = 26$, masa $m = 1.0$, logrando una razón de amortiguamiento $\zeta \approx 0.73$ que genera un rebote natural y sutil. Al asentarse (`IsSettled`), se fija `Value = Target` y `Velocity = 0.0`.
  - **Máquina de Estados Finita (FSM):** Se formaliza `IslandStateMachine` en `OpenDynamic.Core.State` con los estados `Hidden`, `Compact`, `Split` y `Expanded`. Se prohíbe taxativamente la transición directa `Hidden -> Expanded` (debe pasar por `Compact`).
  - **Suscripción estricta a render (Regla de oro 1):** `IslandAnimator` en `OpenDynamic.App.Animation` coordina los resortes (`Width`, `Height`, `CornerRadius`, `Opacity`) y se suscribe a `CompositionTarget.Rendering` ÚNICAMENTE mientras haya resortes en movimiento (`!IsSettled`). Al alcanzar reposo, se desuscribe de inmediato. $\Delta t$ real entre fotogramas se acota a un máximo de 50 ms.
  - **Alineación geométrica y recorte:** Se elimina el margen estático (`Margin="0"` en `CapsuleBorder`), delegando toda la distancia al cálculo de 8 DIPs de `IslandPositionCalculator`. Se aplica `ClipToBounds="True"` y `RectangleGeometry` en la cápsula para recortar el contenido durante los rebotes.
  - **Interacciones reactivas:** Temporizadores de hover de un único disparo (150 ms para expandir, 400 ms de margen al salir) que se detienen tras dispararse, clic para conmutar `Compact`/`Expanded` y rueda del ratón (`MouseWheel`) para contraer/ocultar.
  - **Sensor de activación en estado Hidden (Anti-bloqueo de Hit-Testing):** Se prohíbe el uso de `Visibility.Collapsed` en la cápsula, ya que desactiva por completo el árbol de hit-testing de WPF, volviendo la isla irrecuperable. En su lugar, el estado `Hidden` se modela como una micro-muesca de 80x4 DIPs con radio 2 y opacidad al 1% (`0.01`). Esto la mantiene prácticamente invisible al ojo humano mientras retiene capacidad receptora de eventos para `MouseWheel` hacia abajo, `MouseEnter` y clics en el centro superior, dejando intacto el paso de clics (*click-through*) en el resto de la pantalla.
  - **Aislamiento de depuración (#if DEBUG):** La ventana `IslandDebugWindow` y sus puntos de entrada se condicionan estrictamente a `#if DEBUG`, garantizando cero código ni dependencias visuales de depuración en compilaciones Release.

---

## ADR-008: Arquitectura de Widgets, Resolución de Prioridades Determinista y Autoridad Exclusiva del Orchestrator

- **Estado:** Aceptado
- **Fecha:** 2026-09-28
- **Contexto:** La Fase 3 (M4 Base) establece la infraestructura para desacoplar fuentes de actividad, resolver qué widgets deben presentarse en la Dynamic Island y comandar las transiciones entre modos `Compact`, `Split` y `Expanded` sin comprometer la estabilidad ni el consumo de CPU.
- **Decisiones:**
  - **Aislamiento del modelo en Core (Regla de oro 5):** `IActivitySource`, `IslandActivity`, `ActivityPriority`, `WidgetDisplayMode`, `PriorityResult` y `PriorityResolver` residen en `OpenDynamic.Core.Widgets` con lógica determinista pura y sin dependencias de WPF ni Windows.
  - **Resolución determinista de prioridades:**
    - Prioridad numérica descendente: mayor valor toma precedencia (`ActivityPriority`).
    - Desempate por recencia de activación: si dos fuentes tienen igual prioridad, prevalece la activada más recientemente (`LastActivatedUtc`).
    - Desempate determinista final: ordenación ordinal por identificador textual (`Id`), garantizando total reproducibilidad sin dependencia de orden de lista o memoria.
  - **Pre-emption de actividades transitorias y auto-expiración:** Una actividad transitoria (`IsTransient == true`) con prioridad alta toma el control exclusivo como fuente primaria (`Secondary = null`) en modo `Compact`. `PriorityResolver` calcula el próximo instante de expiración (`NextExpirationUtc`). `IslandOrchestrator` programa un único temporizador de un solo disparo (`DispatcherTimer`) que se detiene tras expirar para restablecer la actividad o modo anterior (Regla de oro 1: 0% CPU en reposo).
  - **Contrato de widgets y ciclo de vida:** Se define `IIslandWidget` en `OpenDynamic.App.Widgets` extendiendo `IActivitySource` e `IDisposable`, con fábricas de vistas WPF (`CreateCompactView()`, `CreateExpandedView()`, `CreateSplitView()`) y eventos de ciclo de vida (`Initialize()`, `OnExpand()`, `OnCollapse()`). La clase base `IslandWidgetBase` implementa `CommunityToolkit.Mvvm` (`ObservableObject`).
  - **Mensajería desacoplada con referencias débiles:** Los eventos de cambio de actividad, solicitud de expansión y transiciones se emiten a través de `WeakReferenceMessenger.Default`, impidiendo fugas de memoria (*memory leaks*) por suscripciones fuertes entre widgets de ciclo de vida independiente y la UI.
  - **Autoridad exclusiva del Orchestrator:** Únicamente `IslandOrchestrator` comanda cambios de estado en `IslandStateMachine`. Los widgets declaran su estado (`IsActive`, `Priority`, `IsTransient`) y exponen vistas; nunca tocan la máquina de estados.
  - **Aislamiento y tolerancia a fallos (Regla de oro 4):** Toda invocación a métodos de widgets (`Initialize`, creación de vistas, `OnExpand`, etc.) está encapsulada en bloques `try/catch` con registro en Serilog. Un fallo o excepción en un widget provoca su puesta en cuarentena (`QuarantinedWidgetIds`) y su aislamiento inmediato, permitiendo a la cápsula continuar operando con normalidad con las fuentes restantes.
  - **Representación visual del modo Split:** `IslandView.xaml` presenta una cápsula principal (`Border` con dimensiones elásticas) y una burbuja satélite circular (`36x36` con radio 18) separada por una brecha transparente de 10 DIPs. La ventana de capas permite el paso libre de clics (*click-through*) entre ambas piezas, emulando la Dynamic Island de hardware.
  - **Widgets de demostración (#if DEBUG):** `DemoWidgetA` (música/alta prioridad), `DemoWidgetB` (temporizador/prioridad normal) y los controles de simulación en `IslandDebugWindow` se aíslan bajo directivas `#if DEBUG`. En compilaciones Release no se incluye ninguna línea ni referencia a código de prueba o demostración.

---

## ADR-009: Integración Multimedia WinRT (GSMTC), Extrapolación de Progreso sin Polling, Desuscripción Estricta y Congelación de Miniaturas (Freeze)

- **Estado:** Aceptado
- **Fecha:** 2026-09-28
- **Contexto:** La Fase 4 demanda integración con el sistema de transporte multimedia de Windows (`Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager`) para detectar y gobernar reproductores (Spotify, Edge, Chrome, VLC) manteniendo la Regla de Oro 1 (CPU ~0% en reposo), Regla de Oro 4 (aislamiento de fallos WinRT/COM) y Regla de Oro 5 (aislamiento del dominio en Core).
- **Decisiones:**
  - **Aislamiento en Core (Regla de oro 5):** Modelos de dominio inmutables (`MediaPlaybackStatus`, `MediaPlaybackCapabilities`, `MediaPlaybackInfo`, `MediaTimelineInfo`, `MediaPropertiesInfo`, `IMediaSession`, `IMediaService`, `MediaActivityController`, `MediaProgressCalculator`) en `OpenDynamic.Core.Media` sin referencias a Windows ni WPF, testeados de forma autónoma en `OpenDynamic.Tests`.
  - **Extrapolación local de progreso (Regla de oro 1: CPU ~0% en reposo):** Prohibido el sondeo (polling) por GSMTC. La posición se extrapola matemáticamente a partir de `TimelineProperties.Position` + `(DateTime.UtcNow - LastUpdatedTime)`. El temporizador `DispatcherTimer` de 1s para refrescar la barra en UI se activa ÚNICAMENTE en estado `Expanded` y con reproducción activa (`Playing`). En `Compact`, `Split`, `Hidden` o en Pausa, se detiene de inmediato.
  - **Gestión de Miniaturas y Seguridad de Hilos:** Se lee el flujo de carátula (`IRandomAccessStreamReference`) a memoria local y se ejecuta obligatoriamente `BitmapImage.Freeze()` antes de suministrarlo a la UI, previniendo excepciones de acceso de hilo cruzado y fugas de memoria. Las vistas aplican `RenderOptions.BitmapScalingMode="HighQuality"`.
  - **Desuscripción meticulosa:** Toda sesión WinRT anterior desuscribe explícitamente sus eventos (`MediaPropertiesChanged`, `PlaybackInfoChanged`, `TimelinePropertiesChanged`, `SessionClosed`) antes de disponerse, previniendo fugas y callbacks zombies sobre procesos cerrados.
  - **Tiempo de gracia tras pausa (10 segundos):** Al pausar la reproducción, la actividad se mantiene visible durante 10 segundos antes de desactivarse (`IsActive = false`), cancelándose el temporizador si se reanuda la música antes de los 10 segundos. Si el reproductor se cierra (`SessionClosed`), la actividad se retira inmediatamente sin esperar. Configurable mediante `AppSettings.MediaPauseGracePeriodSeconds`.
  - **Activación de App emisora (PrimaryAction):** Búsqueda de ventanas y activación a primer plano (`SetForegroundWindow`, `ShowWindow`) a partir del `SourceAppUserModelId` o nombre de proceso de forma pacífica en `try/catch`.

---

## ADR-010: Control y Monitoreo Reactivo de Audio (NAudio / CoreAudio), Detección en Caliente (IMMNotificationClient) y Actividad Transitoria con Rueda del Ratón

- **Estado:** Aceptado
- **Fecha:** 2026-09-28
- **Contexto:** La Fase 5 demanda control y visualización de volumen en tiempo real mediante NAudio, captura de eventos nativos de Windows y teclas multimedia, reconexión inmediata al cambiar de dispositivo de audio predeterminado (auriculares USB, Bluetooth, DAC) y ajuste de volumen interactivo sobre la cápsula reiniciando el tiempo de gracia.
- **Decisiones:**
  - **Aislamiento en Core (Regla de oro 5):** `IVolumeController`, `VolumeChangedEventArgs`, `VolumeCalculator` y `VolumeIconType` en `OpenDynamic.Core.Audio` sin dependencias de UI ni Windows. Las operaciones de normalización, pasos de rueda (`CalculateLevelStep`) y redondeo se validan con pruebas unitarias exhaustivas en `OpenDynamic.Tests`.
  - **Servicio Nativo CoreAudio (NAudio):** `VolumeService` utiliza `MMDeviceEnumerator` y `AudioEndpointVolume`. Todas las invocaciones a interfaces COM se blindan bajo bloques `try/catch` con log en Serilog (Regla de oro 4).
  - **Reconexión en caliente mediante COM nativo:** Implementa `IMMNotificationClient` registrado sobre la interfaz COM `IMMDeviceEnumerator` con GUIDs estándar. Al dispararse `OnDefaultDeviceChanged`, desengancha el endpoint anterior, enlaza el nuevo dispositivo predeterminado y actualiza el nivel y mute sin interrupciones.
  - **Actividad Transitoria y Prolongación:** `VolumeWidget` opera como actividad transitoria con prioridad 80 y duración de 2.0 s. La interacción con la rueda del ratón (`MouseWheel`) sobre la cápsula ajusta el volumen y reinicia el temporizador de auto-expiración de 2 segundos, impidiendo transiciones indeseadas de colapso/expansión.

---

## ADR-011: Monitorización de Energía y Alertas de Batería sin Polling (WM_POWERBROADCAST) con Supresión de Re-notificaciones

- **Estado:** Aceptado
- **Fecha:** 2026-09-28
- **Contexto:** Las alertas de cargador y batería baja exigen cumplimiento estricto de la Regla de Oro 1 (CPU ~0% en reposo, prohibición absoluta de temporizadores de sondeo cada segundo) y garantía de emitir exactamente una sola alerta por cruce de umbral (20% y 10%) sin spam ante fluctuaciones de voltaje.
- **Decisiones:**
  - **Cero Polling (Regla de oro 1):** Prohibido el uso de timers periódicos de batería. La detección se basa exclusivamente en eventos de Windows: intercepción de `WM_POWERBROADCAST` en el `WndProc` de `IslandWindow` y registro de notificaciones con `RegisterPowerSettingNotification` (`GUID_ACDC_POWER_SOURCE` y `GUID_BATTERY_PERCENTAGE_REMAINING`). La consulta `GetSystemPowerStatus` se ejecuta únicamente ante dichos eventos.
  - **Tracker de umbrales con histéresis en Core (Regla de oro 5):** `BatteryThresholdTracker` gestiona de forma pura las transiciones:
    - Emite `LowBattery` (<= 20%) exactamente una sola vez por ciclo de descarga.
    - Emite `CriticalBattery` (<= 10%) exactamente una sola vez por ciclo de descarga.
    - Las fluctuaciones de voltaje (ej. 19% -> 20% -> 19%) no re-emiten alertas gracias a una banda de histéresis (2%).
    - Al conectar el cargador (`ChargerConnected`), se resetean las banderas para habilitar nuevas alertas en el siguiente ciclo.
    - En PCs de escritorio sin batería (`HasBattery == false`), se ignoran las alertas.
  - **Prioridad 90:** `BatteryWidget` se presenta como actividad transitoria de 3.0 s superando a volumen (80) y música (30).

---

## ADR-012: Detección Reactiva de Pantalla Completa (SHQueryUserNotificationState + SetWinEventHook) y Suspensión Total

- **Estado:** Aceptado
- **Fecha:** 2026-09-28
- **Contexto:** Cuando el usuario ejecuta juegos o reproduce videos a pantalla completa (YouTube en navegador, VLC), la Dynamic Island debe ocultarse de inmediato sin robar foco ni superponerse, suspendiendo todas las animaciones y timers activos (Regla de oro 1).
- **Decisiones:**
  - **Detección híbrida reactiva:** `FullscreenWatcher` escucha `EVENT_SYSTEM_FOREGROUND` con `SetWinEventHook` (cero timers de sondeo). Al activarse una nueva ventana en primer plano:
    - Evalúa `SHQueryUserNotificationState`: si el estado es `QUNS_BUSY`, `QUNS_RUNNING_D3D_FULL_SCREEN` o `QUNS_PRESENTATION_MODE`, se clasifica como pantalla completa.
    - Evalúa dimensiones de ventana vs monitor: si la ventana cubre el monitor y no es el escritorio ni el shell, detecta pantalla completa sin bordes.
  - **Suspensión de Orchestrator y Resortes:** Al entrar en pantalla completa, `IslandOrchestrator.SuspendForFullscreen()` detiene de inmediato el temporizador transitorio activo, cancela cualquier solicitud pendiente y congela el animador (`IslandAnimator.SnapTo(IslandState.Hidden)`), desuscribiéndose de `CompositionTarget.Rendering` (0% CPU).
  - **Restauración al salir:** Al recuperar el escritorio o cambiar a una ventana normal, `ResumeFromFullscreen()` evalúa nuevamente las fuentes de actividad y restaura la cápsula con su estado previo de forma suave. Configurable mediante `AppSettings.HideOnFullscreen` (por defecto activo).

---

## ADR-013: Monitorización Nativa de Hardware (Win32 GetSystemTimes / GlobalMemoryStatusEx) y Condición Estricta de Visibilidad (0% CPU en Reposo)

- **Estado:** Aceptado
- **Fecha:** 2026-09-28
- **Contexto:** La Fase 6 (Hito M4) requiere monitorizar CPU y memoria RAM (con soporte opcional de GPU) garantizando el cumplimiento estricto de la Regla de Oro 1 (0% CPU en reposo) y la Regla de Oro 4 (arquitectura nativa aislada sin dependencias pesadas de terceros como LibreHardwareMonitor completo).
- **Decisiones:**
  - **P/Invoke Win32 exclusivo:**
    - CPU: Consulta directa a `GetSystemTimes` en `kernel32.dll`. Cálculo del delta entre muestras consecutivas: $\text{TotalDelta} = (\Delta\text{Kernel} + \Delta\text{User})$, $\text{IdleDelta} = \Delta\text{Idle}$. Dado que en Windows NT el tiempo de kernel incluye el tiempo de inactividad, $\text{CPU\%} = (1.0 - \frac{\text{IdleDelta}}{\text{TotalDelta}}) \times 100$.
    - RAM: Consulta a `GlobalMemoryStatusEx` obteniendo `dwMemoryLoad` y convirtiendo `ullTotalPhys` / `ullAvailPhys` a Gigabytes.
    - GPU: Opcional y estrictamente desactivada por defecto (`AppSettings.EnableGpuMonitoring = false`). Si se habilita, inicializa contadores de rendimiento de la categoría `"GPU Engine"` fuera del hilo de UI en `Task.Run` con try/catch.
  - **Aislamiento en Core (Regla de oro 5):** `HardwareSnapshot`, `IHardwareMonitor` y `HardwareCalculator` residen en `OpenDynamic.Core.Hardware` con cálculo puro y sin dependencias de UI, testeados unitariamente con 100% de reproducibilidad.
  - **Condición Estricta de Visibilidad (Regla de oro 1: 0% CPU en reposo):**
    - El temporizador de muestreo a 2.0 segundos (`_sampleTimer`) se activa ÚNICAMENTE si `HardwareWidget` está activamente visible en pantalla (`IsVisibleOnIsland == true` y `DisplayMode == Compact || Expanded || Split`).
    - Si la isla está en `Hidden`, o si el widget no es primario ni secundario activo, el timer se detiene por completo de inmediato.
    - Al volver a ser visible, se invoca `ResetCpuBaseline()` para descartar deltas acumulados durante el período inactivo y recalibrar el cálculo sin picos anómalos.
  - **Prioridad 10:** Se sitúa como actividad continua base del sistema (`AppSettings.DefaultHardwarePriority = 10`), permitiendo convivencia en Split con música (30) o temporizador (50).

---

## ADR-014: Temporizador por Marca de Tiempo Objetivo (TargetEndTimeUtc / TimeProvider) y Modo Split Multitasking con Intercambio Interactivo de Satélite (Hito M4)

- **Estado:** Aceptado
- **Fecha:** 2026-09-28
- **Contexto:** Se requiere un widget de temporizador con soporte Pomodoro (25 min trabajo / 5 min descanso) que nunca derive por acumulación de ticks, soporte pruebas unitarias deterministas sin `Thread.Sleep`, emita una alerta transitoria crítica de prioridad 100 al finalizar, y permita convivencia en modo Split real con intercambio interactivo al hacer clic en el satélite circular.
- **Decisiones:**
  - **Temporizador por marca de tiempo objetivo (Regla de oro 5):**
    - Prohibido acumular o decrementar "ticks". El temporizador se basa en un timestamp objetivo absoluto en UTC: `TargetEndTimeUtc = now + TotalDuration`.
    - El tiempo restante se calcula en todo momento como `RemainingTime = TargetEndTimeUtc - now`.
    - Al pausar: se preserva `RemainingTime = TargetEndTimeUtc - now`.
    - Al reanudar: se recalcula `TargetEndTimeUtc = now + RemainingTime`, garantizando cero deriva temporal.
  - **Inyección de TimeProvider para pruebas deterministas:** `TimerController` en `OpenDynamic.Core.Timer` consume `System.TimeProvider`, permitiendo avanzar el tiempo en `FakeTimeProvider` sin pausas reales en los tests unitarios.
  - **Jerarquía de prioridades:**
    - Temporizador en curso: Prioridad 50 (`ActivityPriority.Timer`).
    - Temporizador finalizado: Alerta crítica transitoria de Prioridad 100 (`ActivityPriority.TimerAlert`) durante 5.0 segundos (`TimerAlertTransientDurationSeconds`), tomando el control exclusivo de la cápsula con parpadeo y sonido de sistema (`SystemSounds.Asterisk`).
  - **Modo Split Multitasking con Intercambio Interactivo (Hito M4):**
    - Convivencia: Cuando conviven dos actividades continuas (ej. Temporizador P=50 y Música P=30, o Música P=30 y Hardware P=10), `PriorityResolver` comanda `IslandState.Split`. La actividad primaria se presenta en la cápsula principal y la secundaria en el satélite circular (36x36).
    - Clic en Satélite: Un clic izquierdo sobre la burbuja satélite invoca `SwapSplitActivities()` en `IslandOrchestrator`, alternando `_isSplitSwapped`: la secundaria pasa a la cápsula principal y la primaria pasa al satélite con cross-fade suave (80ms).
    - Clic en Cápsula Principal: Si la cápsula está en Split, el clic sobre la cápsula principal expande la actividad primaria actualmente activa (`RequestExpand()`).
    - Al expirar actividades o ingresar alertas transitorias (batería P=90 o volumen P=80), la alerta toma la cápsula y al expirar se restaura el estado Split continuo de forma automática.

---

## ADR-015: Persistencia Robusta (settings.json), Bandeja del Sistema (H.NotifyIcon.Wpf), Ventana de Ajustes MVVM y Atajos Globales Win32 (RegisterHotKey) - Hito M5

- **Estado:** Aceptado
- **Fecha:** 2026-09-29
- **Contexto:** La Fase 7 (Hito M5) requiere persistencia de configuración de usuario con tolerancia a fallos, icono de bandeja nativo de WPF sin WinForms, panel de ajustes con entrada de texto aislada y aplicación de cambios en tiempo real, atajos de teclado globales pacíficos sin hooks de bajo nivel y arranque con Windows sin elevación UAC.
- **Decisiones:**
  - **Persistencia Robusta de Configuración (settings.json):**
    - Ubicación estándar en `%AppData%\openDynamic\settings.json`.
    - Versionado de esquema con `SchemaVersion = 1` y migración automática transparente.
    - Tolerancia a fallos: Si el archivo está corrupto o ilegible, crea un respaldo automático `settings.json.bak`, registra la advertencia estructurada en Serilog y regenera los valores predeterminados sin colapsar la aplicación.
    - Escritura con debounce (500 ms) utilizando temporizadores de un solo disparo para proteger unidades SSD ante movimientos continuos de controles deslizantes (sliders).
    - Métodos `SaveImmediate()` y `Dispose()` para garantizar el vaciado inmediato a disco durante el cierre de la app o antes de la finalización de procesos.
    - Pruebas unitarias exhaustivas en `OpenDynamic.Tests.Settings.SettingsServiceTests` (serialización, carga, respaldo `.bak` ante JSON corrupto, migración de esquema y debounce).
  - **Icono en la Bandeja del Sistema (`H.NotifyIcon.Wpf`):**
    - Prohibición estricta de `System.Windows.Forms` (Regla de oro 3).
    - Clic izquierdo sobre el icono alterna suavemente la cápsula entre visible (`Compact`) y oculta (`Hidden`).
    - Clic derecho despliega un menú contextual completo con diseño oscuro: *Abrir Ajustes*, *Conmutar Monitor de Hardware*, *Reiniciar Posición* y *Salir de openDynamic*.
    - Limpieza garantizada: Al salir de la aplicación, el icono se elimina inmediatamente de la bandeja mediante `Dispose()` explícito, evitando iconos fantasma al pasar el puntero del ratón.
  - **Ventana de Ajustes (`SettingsWindow`) y Aislamiento de Foco:**
    - Ventana WPF tradicional con arquitectura MVVM (`SettingsViewModel`).
    - **Regla de oro de entrada:** Toda entrada de texto o numérica reside EXCLUSIVAMENTE en `SettingsWindow`. La cápsula flotante `IslandWindow` mantiene intacto su estilo `WS_EX_NOACTIVATE` y jamás roba el foco de teclado ni contiene `TextBox`.
    - Aplicación en vivo (*Live Updates*): Los cambios en monitor, márgenes (X/Y), ancho/alto/radio de cápsula, switches de widgets (Hardware, GPU, etc.) y atajos se aplican al instante sobre la cápsula activa sin necesidad de reiniciar la app.
    - Ocultamiento reactivo: Al presionar "Cerrar" o la 'X', la ventana intercepta `OnClosing` y se oculta (`Hide()`) preservando el ciclo de vida de la aplicación.
  - **Atajos de Teclado Globales (`HotkeyService`):**
    - Prohibición estricta de hooks de teclado globales de bajo nivel (`WH_KEYBOARD_LL`) por consumo de CPU y latencia (Regla de oro 2).
    - Implementación mediante la API nativa de Win32 `RegisterHotKey` y `UnregisterHotKey` vinculada al procedimiento de ventana (`WndProc`) a través del `HwndSource` de `IslandWindow`.
    - Atajo predeterminado: `Win+Ctrl+I` para alternar la visibilidad de la isla.
    - Manejo pacífico de colisiones: Si otra aplicación tiene registrado el atajo (código de error Win32 1409), se notifica pacíficamente en la UI de Ajustes sin lanzar excepciones no controladas.
  - **Arranque con Windows (`AutostartService`):**
    - Modificación de la clave de registro del usuario actual: `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`.
    - Sin elevación de privilegios UAC (Regla de oro 3).
    - Verificación y corrección automática de la ruta del ejecutable si la aplicación cambió de directorio.

---

## ADR-015B: Optimización Estricta (TreatWarningsAsErrors), Robustez ante Eventos del Sistema (WM_POWERBROADCAST / WM_DISPLAYCHANGE), Estilizado Oscuro de Controles y Publicación ReadyToRun (R2R) - Hito Previo a M6

- **Estado:** Aceptado
- **Fecha:** 2026-09-29
- **Contexto:** La Fase 8 requiere garantizar la estabilidad absoluta del sistema, cero advertencias de compilación, resiliencia total ante eventos del ciclo de vida de Windows (suspensión, reanudación, reconexión/desconexión de monitores, reinicio de shell), eliminación de fugas de memoria con auditoría formal, estilización oscura completa de controles nativos en Ajustes y publicación optimizada para arranque instantáneo en frío.
- **Decisiones:**
  - **Compilación Estricta y Cero Advertencias:**
    - Se activa `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` de forma centralizada en `Directory.Build.props`.
    - Toda la solución compila tanto en `Debug` como en `Release` con exactamente 0 errores y 0 advertencias.
  - **Corrección de Estilo en ComboBox de Ajustes:**
    - En `SettingsWindow.xaml`, se crea una plantilla de control completa y estilos oscuros implícitos para `ComboBox` y `ComboBoxItem`.
    - Fondo oscuro `#1C1C1E` / `#2C2C2E`, texto blanco `#FFFFFF`, bordes `#3B4252` y menú desplegable (`Popup`) oscuro con resaltado de selección azul (`#2563EB` / `#1D4ED8`), erradicando cualquier texto blanco sobre fondo nativo blanco.
  - **Robustez ante Eventos del Sistema Windows:**
    - **Suspensión y Reanudación (`WM_POWERBROADCAST` & `SystemEvents.PowerModeChanged`):**
      - Al interceptar `PBT_APMSUSPEND` / `PowerModes.Suspend`, `IslandOrchestrator` ejecuta `SuspendForPower()`, cancelando temporizadores transitorios y deteniendo animaciones de resorte para asegurar 0% CPU.
      - Al interceptar `PBT_APMRESUMEAUTOMATIC` / `PBT_APMRESUMESUSPEND` / `PowerModes.Resume`, se restablecen los servicios, se recalibra la posición geométrica con `PositionWindow`, se restablece el Z-order con `ReassertTopmost`, y se actualiza el estado de energía con `PowerService.RefreshPowerStatus()`.
    - **Cambio de Resolución y Conexión/Desconexión de Pantallas (`WM_DISPLAYCHANGE` & `WM_DPICHANGED`):**
      - La ventana intercepta `WM_DISPLAYCHANGE` y `WM_DPICHANGED` en `WndProc`, recalculando automáticamente la geometría y escala en el monitor correspondiente.
      - Si el monitor de destino fue desconectado (`TargetMonitorIndex >= monitors.Count`), `WindowPositioner` registra la advertencia, resetea `TargetMonitorIndex = 0` y reubica la isla de forma automática y pacífica en la pantalla principal sin colapsar la aplicación.
    - **Reinicio del Explorador de Windows (`TaskbarCreated`):**
      - Confirmado y verificado: el hook de mensaje registrado `TaskbarCreated` invoca `TrayIconManager.Recreate()`, recreando el icono de notificación sin duplicaciones ni iconos fantasma.
    - **Reintentos en Inicialización de GSMTC:**
      - `MediaService.InitializeAsync` incorpora un bucle de reintento configurable (hasta 3 intentos con retardo exponencial progresivo) para tolerar arranques retrasados del subsistema de audio o multimedia de Windows.
  - **Auditoría de Recursos, Rendimiento Real y Cero Fugas:**
    - Verificación exhaustiva de liberación y desuscripción de eventos (`CompositionTarget.Rendering`, `DispatcherTimer.Tick`, `HwndSource` hooks, `SystemEvents.PowerModeChanged`, eventos WinRT y `WeakReferenceMessenger`).
    - Mediciones reales obtenidas en entorno local y documentadas formalmente:
      - Consumo de RAM en reposo: **27.9 MB** (Working Set, meta: < 100 MB).
      - Memoria privada comprometida: **5.2 MB**.
      - Consumo de CPU en reposo sin actividad visible: **0.00%** (meta: < 0.5%).
      - Consumo de CPU durante animación de resortes: **< 1.0%** (pico transitorio).
  - **Matriz de Pruebas Documentada (`docs/pruebas.md`):**
    - Se consolida la matriz formal de validación cubriendo pruebas automatizadas (197 pruebas unitarias en verde), pruebas de estrés de recursos y protocolo manual de 16 casos para el usuario.
  - **Optimización de Publicación con ReadyToRun (R2R):**
    - Se habilita `<PublishReadyToRun>true</PublishReadyToRun>` en `OpenDynamic.App.csproj`.
    - Publicación `win-x64` genera binarios precompilados a código nativo Ahead-of-Time para arranque instantáneo en frío, sin aplicar trimming destructivo incompatible con WPF.

---

## ADR-016: Decisión de Distribución del Runtime (.NET 10 Desktop Runtime vs. Self-Contained) y Empaquetado de Instalador (Hito M6)

- **Estado:** Aceptado
- **Fecha:** 2026-09-29
- **Contexto:**
  Para el cierre y entrega final de openDynamic v1.0.0 (Hito M6), es fundamental definir la estrategia de empaquetado y distribución del runtime de .NET 10 para Windows x64. Las opciones evaluadas son:
  1. **Distribución dependiente de framework (Framework-Dependent):** Requiere que el usuario final cuente con Microsoft .NET 10 Desktop Runtime (x64) instalado en el sistema.
  2. **Distribución auto-contenida (Self-Contained):** Empaqueta el runtime completo de .NET 10 (CoreCLR, bibliotecas base BCL, subsistema WPF nativo y assemblies) dentro del directorio de instalación de la aplicación.
  
- **Análisis Comparativo:**

| Criterio | Dependiente de Framework (.NET 10 Desktop Runtime) | Auto-Contenido (Self-Contained) |
|---|---|---|
| **Tamaño de Binarios en Disco** | **~32.0 MB** (26 archivos con `PublishReadyToRun=true`) | **~224.5 MB** (272 archivos con `PublishReadyToRun=true`) |
| **Tamaño de Descarga del Instalador (.exe)** | **~11 - 12 MB** (compresión LZMA2 en Inno Setup) | **~65 - 75 MB** (compresión LZMA2 en Inno Setup) |
| **Tamaño de Paquete Portátil (.zip)** | **~12 MB** | **~75 MB** |
| **Arranque en Frío y Rendimiento** | Excelente con ReadyToRun (R2R nativo compila métodos Ahead-of-Time). | Excelente con ReadyToRun (R2R nativo). |
| **Consumo de Memoria RAM** | Compartición de assemblies en caché del runtime global; Working Set en reposo **27.9 MB**, Memoria Privada **5.2 MB**. | Asignaciones de BCL y CoreCLR privadas por proceso; ligero incremento de huella de memoria privada. |
| **Seguridad y Parches de SO** | Las actualizaciones de seguridad de .NET 10 se aplican a nivel de sistema operativo vía Windows Update sin requerir re-empaquetar openDynamic. | Cualquier vulnerabilidad en el runtime requiere que openDynamic publique una nueva release completa. |
| **Experiencia de Usuario en Máquinas Limpias** | Si la máquina no tiene .NET 10 Desktop Runtime, el instalador detecta la ausencia y guía automáticamente al usuario con enlace directo oficial de descarga (`https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x64.exe`). | Funciona de inmediato sin prerrequisitos previos. |

- **Decisión Adoptada:**
  Se elige la **Distribución Dependiente de Framework (Framework-Dependent)** con `PublishReadyToRun=true` para x64 como estándar de producción oficial de openDynamic v1.0.0 por las siguientes razones clave:
  1. **Alineación con la Filosofía de Cero Bloatware:** openDynamic se diseñó desde el primer día para ser un overlay ultra-liviano con consumo < 30 MB de RAM y CPU 0.0% en reposo. Un instalador de apenas ~11 MB y una carpeta de instalación de ~32 MB respetan la filosofía de ligereza, a diferencia de los ~225 MB de un paquete auto-contenido.
  2. **Detección Automatizada en Instalador:** El instalador de Inno Setup (`installer/setup.iss`) comprueba la presencia de .NET 10 Desktop Runtime en el registro de Windows (`HKLM\SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App` o ejecución rápida de `dotnet --list-runtimes`). Si no se encuentra, abre el instalador web oficial o guía al usuario en un solo clic.
  3. **Seguridad y Mantenimiento:** La delegación del runtime a Windows Update garantiza que parches de seguridad críticos de Microsoft no dependan de ciclos de despliegue de openDynamic.
  4. **Publicación Dual en GitHub Releases:** El workflow de CI/CD generará el instalador oficial optimizado y el archivo portátil comprimido en el release de GitHub, documentando claramente el enlace directo al runtime oficial.

---

## ADR-017: Rediseño Visual de Cápsula Flotante a Muesca Rectangular Superior (Notch)

- **Estado:** Aceptado
- **Fecha:** 2026-09-29
- **Contexto:** Antes de proceder a la Fase 9, se requiere una actualización estética fundamental de openDynamic: transicionar del factor de forma de "Píldora flotante" (despegada de la parte superior por 8 DIPs con curvatura simétrica semicircular) a "Muesca rectangular superior" (Notch anclado al marco superior absoluto de la pantalla). Esta transformación debe mantener intactas la reactividad, las animaciones de física de resortes elásticos, la arquitectura de widgets, el paso de clics por píxel y el consumo de CPU ~0% en reposo.
- **Decisiones Técnicas:**
  - **1. Geometría y Anclaje al Bisel Superior:**
    - Margen superior predeterminado ajustado estrictamente a 0 DIP (`OffsetY = 0.0` en `AppSettings.cs`, `DefaultTopMarginDip = 0.0` en `IslandPositionCalculator.cs` y `TopMarginDip = 0.0` en `WindowPositioner.cs`). La muesca nace directamente pegada al borde superior absoluto de la pantalla.
    - En reposo (`Hidden`), la muesca descansa contra el bisel superior con una altura sutil de 4 DIPs (80x4 DIPs, opacidad 0.01), actuando como sensor receptivo para hover y rueda del ratón (`MouseWheel`) sin obstruir la pantalla.
    - En modo `Compact`, la muesca se despliega hacia abajo desde el marco superior con dimensiones 200x36 DIPs (ancho en rango 180–220 DIP, alto 36 DIP).
    - En modo `Expanded`, la muesca se extiende vertical y horizontalmente manteniendo su anclaje en el borde superior con dimensiones 400x160 DIPs (ancho en rango 360–420 DIP, alto en rango 150–170 DIP).
    - En modo `Split`, la muesca principal (234x36 DIPs) y la burbuja satélite (36x36 DIPs) nacen pegadas al bisel superior con separación de 10 DIPs (envergadura total 280 DIPs).
  - **2. Esquinas Asimétricas y Recorte Geométrico Preciso:**
    - Sustitución de `CornerRadius` simétrico (que formaba la cápsula circular completa) por esquinas asimétricas:
      * Esquinas superiores (Top-Left y Top-Right): 0 DIP (completamente ortogonales y pegadas al marco).
      * Esquinas inferiores (Bottom-Left y Bottom-Right): curvadas con radio suave de 14 DIPs (`CornerRadius="0,0,14,14"`).
    - Recorte interno (`Clip`) en `IslandView.xaml.cs` reimplementado mediante `CreateNotchClipGeometry`: genera una `PathGeometry` congelada (`Freeze()`) con borde superior plano `(0,0)->(width,0)`, aristas laterales rectas y arcos inferiores suaves con `ArcSegment` (radio $r$, sentido horario). Esto previene que los elementos hijos (álbumes, textos, barras de progreso) se desborden de las esquinas redondeadas inferiores mientras garantiza que no exista recorte en las esquinas superiores contra el marco.
    - `CornerRadiusSpring` en `IslandAnimator` se preserva para animar de forma elástica la curvatura de las esquinas inferiores durante las transiciones de estado.
  - **3. Preservación Estricta de Principios de Arquitectura:**
    - **Regla de Oro 6 cumplida:** La ventana Win32 overlay permanece con tamaño fijo (640x240 DIP) y fondo transparente (`Background="Transparent"`). Únicamente se redimensiona el `Border` interior mediante los resortes. Cero llamadas a `SetWindowPos` para redimensionamiento en tiempo de animación.
    - **Regla de Oro 1 cumplida:** El bucle de renderizado se desuscribe de `CompositionTarget.Rendering` al asentarse los resortes (~0% CPU en reposo).
    - **Compatibilidad total de widgets:** Todos los widgets existentes (Media, Hardware, Timer, Batería, Volumen) y sus respectivas vistas compactas y expandidas se adaptan con márgenes limpios y legibilidad garantizada dentro del nuevo formato notch.
    - **Migración Automática de Configuración (Schema v2):** Se incrementa `CurrentSchemaVersion = 2` en `AppSettings.cs`. En `SettingsService.Load()`, las configuraciones existentes con `SchemaVersion < 2` (procedentes de versiones previas con `OffsetY = 8.0` y `CapsuleCornerRadius = 18.0`) se normalizan automáticamente a `OffsetY = 0.0` y `CapsuleCornerRadius = 14.0`, guardándose inmediatamente en disco.
    - **Comandos de Restablecimiento en ViewModel:** Se alinean `ResetPosition()` y `ResetToDefaults()` en `SettingsViewModel.cs` con `OffsetY = 0.0`, `CapsuleCornerRadius = 14.0` y llamada explícita a `ApplyPositionLive()`.

---

## ADR-018: Perfiles de Movimiento, Detección Reactiva de Accesibilidad, Alto Contraste y UI Automation (Fase 10)

- **Estado:** Aceptado
- **Fecha:** 2026-09-29
- **Contexto:**
  Para el ciclo v1.1 (Fase 10), openDynamic implementa soporte integral de accesibilidad cumpliendo las directrices WCAG 2.1 (Criterios 2.2.2 y 2.3.3 de reducción de movimiento y animaciones por interacción) y las guías de diseño accesible de Windows 11. Los usuarios con trastornos vestibulares, sensibilidad al movimiento o usuarios de tecnologías de asistencia (como el Narrador de Windows) requieren interfaces predecibles, sin sobreimpulsos ni rebotes visuales continuos, junto a soporte de Alto Contraste del sistema y nombres accesibles estructurados en todos los componentes interactivos.

- **Decisiones Técnicas:**

  1. **Perfiles de Movimiento Puros en OpenDynamic.Core (Regla de Oro 5):**
     - Se define el enum `MotionMode` con tres estados: `Auto` (sigue la preferencia del sistema operativo), `Reduced` (sin rebote ni efectos decorativos) y `Full` (física elástica completa con resorte subamortiguado).
     - Se implementa `MotionProfile` inmutable con parámetros físicos ($k$: rigidez, $c$: amortiguamiento, $m$: masa), duración de cross-fade de contenido (`CrossFadeDurationMs`) y la política decorativa `AllowDecorative`.
     - `MotionProfileResolver` resuelve de manera determinista el perfil activo:
       * **Modo Full (o Auto con animaciones de Windows activas):** $k=280.0, c=24.0, m=1.0$ ($\zeta \approx 0.717$, resorte subamortiguado con overshoot natural de ~4%), `CrossFadeDurationMs = 250 ms`, `AllowDecorative = true`.
       * **Modo Reduced (o Auto con animaciones de Windows desactivadas):** $k=400.0, c=40.0, m=1.0$ ($\zeta = 1.0$, amortiguamiento crítico exacto sin sobreimpulso ni oscilación residual, $0.0\%$ de overshoot), `CrossFadeDurationMs = 120 ms` ($\le 150\text{ ms}$), `AllowDecorative = false`.
     - Se valida formalmente mediante pruebas unitarias en `OpenDynamic.Tests` la pureza de ensamblado (cero referencias a Windows, Win32 o WPF en Core) y la ausencia matemática absoluta de overshoot en modo reducido tanto en trayectorias crecientes como decrecientes.

  2. **Detección Reactiva de Windows sin Polling (Reglas de Oro 1 y 11):**
     - La preferencia de Windows ("Efectos de animación" / `SPI_GETCLIENTAREAANIMATION`) se detecta de forma 100% reactiva en `IslandWindow.WndProc` interceptando el mensaje nativo `WM_SETTINGCHANGE (0x001A)` y suscribiéndose a `SystemParameters.StaticPropertyChanged`.
     - Queda estrictamente prohibido el uso de timers de sondeo o consultas periódicas al registro.
     - Preservación de inercia en vuelo: Cuando el perfil cambia mientras una animación se encuentra en progreso, `IslandAnimator.ApplyProfile()` actualiza dinámicamente la rigidez ($k$) y el amortiguamiento ($c$) de los resortes activos sin alterar la posición (`Value`) ni la velocidad instantánea (`Velocity`), evitando saltos abruptos o congelamientos visuales.

  3. **Supresión de Animaciones Decorativas en Widgets:**
     - `IslandWindow` difunde el mensaje desacoplado `MotionProfileChangedMessage` mediante `WeakReferenceMessenger.Default`.
     - `MediaWidget` evalúa `IsDecorativeAllowed`: en modo reducido o con animaciones desactivadas, la propiedad `EqualizerVisibility` colapsa automáticamente las barras simuladas del ecualizador, mostrando la información estática del reproductor sin oscilaciones innecesarias.
     - `TimerWidget` desactiva parpadeos decorativos de finalización, manteniendo un indicador estático en color carmesí de alerta y la emisión de audio.

  4. **Modo de Alto Contraste y UI Automation para Lectores de Pantalla:**
     - Se introduce `AccessibilityThemeManager` en la capa de infraestructura, reaccionando a `SystemParameters.HighContrast` y a eventos del sistema para actualizar dinámicamente los recursos compartidos en `App.xaml` (`AppCapsuleBackgroundBrush`, `AppBorderBrush`, `AppNotchBorderThickness`, `AppTextPrimaryBrush`, etc.).
     - En modo de Alto Contraste, se aplican pinceles enlazados a `SystemColors.WindowTextBrushKey` y `SystemColors.HighlightBrushKey`, forzando un borde sólido visible de 1 DIP alrededor de la muesca y fondo negro opaco para garantizar contraste infinito sobre fondos claros o transparentes.
     - Se añaden atributos de accesibilidad en todas las vistas XAML (`IslandView`, `MediaCompactView`, `MediaExpandedView`, `TimerCompactView`, `TimerExpandedView`, `VolumeCompactView`, `VolumeExpandedView`, `BatteryCompactView`, `HardwareCompactView`, `HardwareExpandedView`):
       * `AutomationProperties.Name` descriptivo en todos los botones de control, sliders y tarjetas.
       * `AutomationProperties.HelpText` con instrucciones concisas para usuarios del Narrador de Windows.
       * `AutomationProperties.LiveSetting="Polite"` en indicadores dinámicos y estados de alerta, permitiendo al Narrador anunciar cambios importantes sin interrumpir la dicción actual del usuario.

  5. **Persistencia y Migración de Configuración Segura (Regla de Oro 9):**
     - Se incorpora `MotionMode` en `AppSettings.cs` con valor por defecto documentado `MotionMode.Auto`.
     - Se incrementa la versión de esquema a `CurrentSchemaVersion = 3`. `SettingsService.Load()` detecta archivos de configuración previos con `SchemaVersion < 3`, preserva todos los ajustes de usuario existentes y asigna automáticamente `MotionMode = MotionMode.Auto`, persistiendo el archivo actualizado en disco.
     - Se integra un selector de modo de animación con ComboBox accesible y diagnóstico en tiempo real del estado de Windows en la pestaña "Atajos y Sistema" de `SettingsWindow.xaml`.

---

## ADR-019: Alertas Transitorias de Red y Dispositivos Periféricos (Fase 11)

- **Estado:** Aceptado
- **Fecha:** 2026-09-29
- **Contexto:**
  La Fase 11 incorpora en openDynamic el monitoreo reactivo y la visualización transitoria en la muesca (Dynamic Island) de cambios de conectividad de red (Wi-Fi, Ethernet, desconexión) y de periféricos externos (memorias/discos USB, auriculares, ratones, teclados y otros dispositivos Bluetooth). Se exige estricta pureza en Core (Regla de oro 5), consumo 0% CPU en reposo sin sondeo (Regla de oro 1), liberación completa de watchers al desactivar las funciones (Regla de oro 11), supresión total de avisos al iniciar y tras suspensión, anonimización absoluta de telemetría/logs y resolución coordinada en la jerarquía de prioridades.

- **Decisiones Técnicas:**

  1. **Aislamiento de la Lógica de Decisión en Core (Regla de Oro 5):**
     - Se implementan `NetworkAlertPolicy` y `DeviceAlertPolicy` en `OpenDynamic.Core` sin dependencias de Windows ni WPF, utilizando `TimeProvider` inyectable para posibilitar pruebas unitarias 100% deterministas con `FakeTimeProvider`.
     - `NetworkAlertPolicy`: Aplica un debounce de 1.0 s ante cambios rápidos o inestabilidad transitoria de interfaces, cooldown de 5.0 s entre alertas de igual estado/red para evitar spam de reconexión, supresión total del estado inicial como línea base y ventana de supresión de 10.0 s tras reanudación de energía (`OnPowerResumed`).
     - `DeviceAlertPolicy`: Coalescencia de 800 ms para inserciones multifunción o ráfagas de periféricos compuestos, cooldown de 3.0 s, supresión de todos los dispositivos enumerados antes de `EnumerationCompleted` (evitando ruido al arrancar), supresión de 10.0 s tras suspensión y filtrado mediante lista configurable de nombres ignorados.

  2. **Servicios de Plataforma Reactivos en App (Sin Polling):**
     - `NetworkService`: Se suscribe de forma reactiva al evento WinRT `Windows.Networking.Connectivity.NetworkInformation.NetworkStatusChanged`. Para identificar la red activa se utiliza `ProfileName` del perfil de conexión a Internet, evitando la necesidad de solicitar permisos invasivos de ubicación que Windows 11 exige para consultar el SSID directo de Wi-Fi.
     - `DeviceService`: Combina la notificación de ventana Win32 `WM_DEVICECHANGE` (`RegisterDeviceNotification` con `GUID_DEVINTERFACE_USB_DEVICE`) para detectar inserciones/extracciones inmediatas de almacenamiento USB en el bucle de mensajes de `IslandWindow`, junto con WinRT `DeviceWatcher` (`AssociationEndpoint`) para detectar la conexión y desconexión de periféricos Bluetooth en tiempo real.
     - Lectura de Batería Bluetooth: Se extrae el nivel de carga a través de la propiedad de WinRT `System.Devices.BatteryLevel` (entero de 0 a 100) cuando el controlador del dispositivo lo proporciona; en caso contrario, se maneja de forma segura como `null` sin degradar la notificación.
     - Suspensión y Reanudación de Energía: Se capturan los mensajes `WM_POWERBROADCAST` (`PBT_APMSUSPEND`, `PBT_APMRESUMEAUTOMATIC`, `PBT_APMRESUMESUSPEND`) en `IslandWindow.WndProc` y se notifican a `NetworkService` y `DeviceService` para silenciar falsas alertas durante el despertar del equipo.

  3. **Gestión de Recursos y Desactivación Limpia (Regla de Oro 11 & Budget):**
     - Al alternar los interruptores en la ventana de Ajustes o al cerrar la aplicación, se invoca `Stop()`:
       * En `NetworkService`, se desuscribe el manejador `NetworkStatusChanged`.
       * En `DeviceService`, se detiene el `DeviceWatcher`, se desuscriben sus eventos (`Added`, `Removed`, `Updated`, `EnumerationCompleted`, `Stopped`) y se cancela la notificación nativa de ventana mediante `UnregisterDeviceNotification`.

  4. **Privacidad Estricta en Registros de Auditoría:**
     - En conformidad con las directrices de privacidad del proyecto, se prohíbe taxativamente registrar nombres amigables de dispositivos periféricos en los archivos de log de Serilog. Solo se registran categorías sanitizadas (`DeviceCategory`), tipos de evento (`Connected`/`Disconnected`) y conteos agregados.

  5. **Notificación en Muesca y Jerarquía de Prioridades:**
     - Se integran `NetworkWidget` (Prioridad 65, 3.0 s transitorio) y `DeviceWidget` (Prioridad 60, 3.0 s transitorio), provistos de vistas XAML compactas, expandidas y de modo split adaptadas a la muesca superior (`NetworkCompactView`, `NetworkExpandedView`, `NetworkSplitView`, `DeviceCompactView`, `DeviceExpandedView`, `DeviceSplitView`).
     - Se actualiza la jerarquía global de actividades en `ActivityPriority`:
       `TimerAlert (100) > Battery (90) > Volume (80) > Network (65) > Device (60) > Timer (50) > Media (30) > Hardware (10)`.
     - Validado exhaustivamente mediante pruebas unitarias en `PriorityResolverPhase11Tests`.

  6. **Migración de Esquema de Configuración v4:**
     - Se eleva `CurrentSchemaVersion = 4` en `AppSettings.cs`.
     - `SettingsService.Load()` migra automáticamente configuraciones previas con `SchemaVersion < 4`, inicializando alertas de red (65, 3.0 s), alertas de dispositivos (60, 3.0 s) y lista de exclusión de dispositivos vacía.
     - Se incorporan tarjetas de configuración con interruptores, deslizadores y gestión de lista de ignorados con accesibilidad completa (`AutomationProperties.Name`) en la pestaña "Widgets y Prioridades" de `SettingsWindow.xaml`.

---

## ADR-020: Cronómetro y Gestión de Múltiples Temporizadores Basados en Marcas de Tiempo (Fase 12)

- **Estado:** Aceptado
- **Fecha:** 2026-09-29
- **Contexto:**
  Para el ciclo v1.1 (Fase 12), openDynamic amplía las capacidades de control temporal permitiendo gestionar hasta 5 temporizadores simultáneos con etiquetas personalizables, botones de preajustes rápidos (1, 5, 10, 15 min), encolado de alertas secuenciales al finalizar y un cronómetro de alta precisión con vueltas (splits). Se exige estricta pureza en Core (Regla de oro 5), consumo de 0% CPU en reposo (Regla de oro 1), ausencia total de robo de foco o entrada de texto en la isla flotante (Regla de oro 3) y un único DispatcherTimer compartido en UI (Regla de oro 11).

- **Decisiones Técnicas:**

  1. **Lógica Pura de Core y Ausencia Absoluta de Deriva Temporal (Regla de Oro 5):**
     - Se implementa `StopwatchController` en `OpenDynamic.Core.Stopwatch`, calculando el tiempo transcurrido estrictamente mediante marcas de tiempo UTC absolutas provistas por `TimeProvider` (`ElapsedTime = _accumulated + (now - _sessionStartUtc)`). Se prohíbe taxativamente la acumulación iterativa de ticks.
     - Soporta inicio, pausa con congelamiento exacto de acumulación sin deriva temporal, reanudación y registro de vueltas (`StopwatchLap`) que calcula tanto la duración de la vuelta individual como el tiempo acumulado (*split time*), con formateo `mm:ss.cc` y `h:mm:ss`.
     - Se implementa `TimerCollection` en `OpenDynamic.Core.Timer`, administrando un tope estricto de hasta 5 instancias de `TimerController` con identificador único y etiqueta amigable.
     - Compatibilidad hacia atrás: El temporizador y el Pomodoro existentes pasan a constituir el primer elemento de la colección con identificador `"primary"`. Las pruebas unitarias originales de Fase 6 en `TimerControllerTests` permanecen 100% intactas y en verde sin modificación.

  2. **Regla del Temporizador Principal en la Cápsula:**
     - En todo momento, la cápsula compacta de la muesca visualiza el temporizador que **termina antes** entre los que se encuentran actualmente en estado `Running` (ordenados ascendentemente por `TargetEndTimeUtc`).
     - Si ninguno está en marcha pero hay pausados, se prioriza el que tenga menor tiempo restante. Si todos están detenidos, se muestra el temporizador predeterminado.

  3. **Presupuesto de Rendimiento y DispatcherTimer Único Compartido (Reglas de Oro 1 y 11):**
     - Se introduce `TimingUiCoordinator` como servicio centralizado en `OpenDynamic.App.Services`.
     - Se elimina cualquier `DispatcherTimer` interno individual en `TimerWidget` y `StopwatchWidget`.
     - Existe un **único** `DispatcherTimer` compartido para toda la interfaz visual. Dicho timer permanece activo **únicamente si el cronómetro está corriendo o si hay al menos un temporizador activo visible en la isla**.
     - Cuando todos los temporizadores y el cronómetro están pausados, detenidos o inactivos, el `DispatcherTimer` se detiene por completo garantizando 0% CPU.
     - Vencimiento en segundo plano: Los temporizadores en marcha continúan su curso mediante marcas de tiempo UTC y programan un temporizador de precisión vía `TimeProvider.CreateTimer` hacia el próximo vencimiento (`earliestTarget - now`), despertando reactivamente la UI sin necesidad de refrescos periódicos en segundo plano.

  4. **Sin Entrada de Texto en la Isla Flotante (Reglas de Oro 3 y 6):**
     - De acuerdo con la arquitectura de ventana overlay sin activación (`WS_EX_NOACTIVATE` y retorno `MA_NOACTIVATE` en `WM_MOUSEACTIVATE`), queda terminantemente prohibido incorporar campos de entrada de texto (`TextBox`) o robo de foco en la Dynamic Island.
     - La personalización de etiquetas y la creación de temporizadores con nombres específicos se realiza **exclusivamente desde Ajustes (`SettingsWindow`)**. En la vista expandida de la isla solo se ofrecen botones de preajustes rápidos (1, 5, 10, 15 min), sumadores (+1m/+5m) y controles de reproducción/borrado.

  5. **Encolado Secuencial de Alertas de Finalización:**
     - Al expirar un temporizador, se emite una alerta transitoria en la muesca de prioridad 100 (`ActivityPriority.TimerAlert`) durante 5 segundos con el nombre del temporizador finalizado y retroalimentación auditiva del sistema.
     - Si dos o más temporizadores concluyen simultáneamente o durante la exhibición de una alerta previa, `TimerCollection` encola las alertas y las reproduce en secuencia estricta de 5 segundos cada una.

  6. **Jerarquía Global de Prioridades:**
     - Se añade `ActivityPriority.Stopwatch = 45`.
     - Jerarquía consolidada:
       `TimerAlert (100) > Battery (90) > Volume (80) > Network (65) > Device (60) > Timer (50) > Stopwatch (45) > Media (30) > Hardware (10)`.
     - Con temporizador (50) y cronómetro (45) activos concurrentemente, `PriorityResolver` activa de forma determinista el modo Split, asignando el lado primario al temporizador y el secundario al cronómetro.

  7. **Persistencia Segura de Temporizadores:**
     - Se implementa `TimerPersistenceService` serializando los temporizadores en marcha o pausados en `%AppData%\openDynamic\timers.json`.
     - Al iniciar la aplicación, los temporizadores vigentes se reanudan calculando el tiempo restante real (`TargetEndTimeUtc - now`). Los temporizadores que vencieron con la app apagada se detectan y notifican una sola vez.

  8. **Migración de Configuración a Schema v5:**
     - Se incrementa `CurrentSchemaVersion = 5` en `AppSettings.cs`.
     - Se incorporan `EnableStopwatchWidget` (default: `true`), `DefaultStopwatchPriority` (default: `45`) y `TimerPresetsMinutes` (default: `[1, 5, 10, 15]`).
     - `SettingsService.Load()` efectúa la migración automática de esquemas anteriores (< 5) sin pérdida de datos del usuario, validado por pruebas unitarias automatizadas.
---

## ADR-021: Color Dinámico de Carátula y Gestos Horizontales en Multimedia (Fase 13)

- **Estado:** Aceptado
- **Fecha:** 2026-09-29
- **Contexto:**
  Para la ampliación v1.1 (Fase 13), openDynamic dinamiza visualmente la reproducción de música extrayendo el color de acento dominante de la portada del álbum para teñir armoniosamente elementos interactivos y el borde del notch, además de habilitar gestos horizontales (rueda horizontal de ratón, touchpad de dos dedos y arrastre táctil) para saltar de canción con un rebote elástico. Se exige estricta pureza en Core (Regla de oro 5), rendimiento eficiente con 0% de CPU en reposo (Regla de oro 1), seguridad de memoria y ausencia de bloqueos de GPU (Regla de oro 11), y convivencia armónica con los gestos verticales preexistentes.

- **Decisiones Técnicas:**

  1. **Aislamiento de Algoritmos Puros en Core (Regla de Oro 5):**
     - Se implementan `RgbColor`, `DominantColorExtractor` y `AccentColorAdjuster` en `OpenDynamic.Core.Media.Color` libres de dependencias de `System.Drawing`, Win32 o WPF.
     - `DominantColorExtractor`: Muestrea el mapa de píxeles BGRA sobre una cuadrícula reducida (32x32 = 1024 píxeles), clasifica en 16 cubetas angulares de matiz (Hue, 22.5° cada una), descarta de manera rigurosa casi negros ($L < 0.15$ o $RGB < 35$), casi blancos ($L > 0.88$ o $RGB > 225$) y grises desaturados ($S < 0.18$ o $\Delta < 25$), y pondera la cubeta ganadora combinando saturación cuadrática y luminosidad balanceada ($S^2 \cdot (1 - |L - 0.5|)$).
     - `AccentColorAdjuster`: Recibe el color dominante y garantiza legibilidad y viveza sobre el fondo negro azabache (`#000000`) del notch, forzando saturación mínima ($S \ge 0.50$), luminosidad acotada ($0.45 \le L \le 0.80$) y recurriendo al acento blanco neutro estándar (`#FFFFFF`) si la portada es monocromática, negra o nula.
     - `SwipeGestureDetector`: Máquina de estados pura en `OpenDynamic.Core.Media.Gestures` con `TimeProvider` inyectable. Acumula deltas horizontales con umbral configurable y aplica un período de enfriamiento (*cooldown*) estricto de 400 ms que absorbe la inercia del touchpad y previene saltos dobles accidentales de pista.

  2. **Rendimiento Gráfico, Caché y Subprocesos (Reglas de Oro 1 y 11):**
     - `MediaColorService` en `OpenDynamic.App.Services`: Ejecuta el remuestreo a 32x32 y la extracción de color fuera del hilo de interfaz (`Task.Run`).
     - Almacena en memoria una caché ligera por pista (`ConcurrentDictionary<string, RgbColor>` con clave `"Título|Artista"`). Queda taxativamente prohibido retener referencias a instancias de bitmaps antiguos o buffers de píxeles pesados.
     - Generación de `SolidColorBrush` congelados (`brush.Freeze()`): Todo pincel entregado a la UI se congela inmediatamente, permitiendo su compartición segura entre hilos y eliminando fugas de memoria en WPF.
     - **Prohibición de DropShadowEffect pesados:** Para preservar la tasa de refresco y evitar costosos pases de rasterización por GPU, se descarta el uso de efectos de desenfoque y sombras profundas. El acento en el notch se aplica mediante un trazo sutil en el borde perimetral (`BorderBrush`), animado con `ColorAnimation` corta (~300 ms) solo al cambiar de canción, e instantáneo en modo de movimiento reducido.

  3. **Convivencia de Gestos Horizontales con la Rueda Vertical:**
     - La rueda vertical existente (`WM_MOUSEWHEEL` en `CapsuleBorder.MouseWheel`) mantiene su función exclusiva: ajustar el volumen del sistema cuando `VolumeWidget` está activo o contraer/expandir/ocultar la cápsula.
     - La rueda horizontal y el deslizamiento con dos dedos en touchpads de precisión generan el mensaje Win32 nativo `WM_MOUSEHWHEEL` (`0x020E`), interceptado en el procedimiento de ventana `IslandWindow.WndProc`.
     - `WM_MOUSEHWHEEL` actúa **únicamente si el puntero se encuentra dentro de los límites visuales de la cápsula** (`IsPointerOverNotch()`), la actividad primaria es multimedia (`MediaWidget`) y la sesión GSMTC activa posee la capacidad requerida (`CanSkipNext` / `CanSkipPrevious`).
     - Arrastre táctil/ratón: Se incorpora arrastre con botón izquierdo sobre la cabecera (carátula y títulos) de `MediaExpandedView` con umbral mínimo ~40 DIPs. La zona de la barra de progreso (seek bar) se encuentra aislada en su propia fila y mantiene su comportamiento continuo de salto temporal sin colisión de gestos.

  4. **Retroalimentación Visual y Accesibilidad (`MotionMode`):**
     - Al dispararse un gesto o durante el arrastre, la carátula experimenta un desplazamiento amortiguado con resorte (`ElasticEase` con oscilación controlada) hacia la izquierda (-18 DIPs) en avance o hacia la derecha (+18 DIPs) en retroceso.
     - En conformidad con la Fase 10, cuando `MotionMode.Reduced` o `SystemParameters.ClientAreaAnimation == false` está activo, se suprimen todas las animaciones de resorte y el cambio de pista o color se realiza de forma directa e instantánea (desplazamiento 0 DIP).

  5. **Migración de Configuración a Schema v6 (Regla de Oro 9):**
     - Se incrementa `CurrentSchemaVersion = 6` en `AppSettings.cs`.
     - Se añaden `EnableDynamicMediaColor` (default: `true`), `EnableMediaGestures` (default: `true`) y `MediaGestureSensitivity` (default: `120.0`).
     - `SettingsService.Load()` actualiza transparentemente configuraciones previas (< 6) y se proveen controles accesibles con `AutomationProperties` en la pestaña de Multimedia de `SettingsWindow`.

---

## ADR-022: Historial de Portapapeles Seguro en Memoria RAM, Listener Reactivo Win32 y Exclusión Estricta de Gestores de Contraseñas (Fase 14)

- **Estado:** Aceptado
- **Fecha:** 2026-09-30
- **Contexto:**
  Para la ampliación v1.1 (Fase 14), openDynamic incorpora un widget contextual y un historial de portapapeles reciente para mostrar avisos transitorios tras copiar y permitir volver a copiar elementos recientes desde la muesca expandida. Por la naturaleza extremadamente sensible de la información que pasa por el portapapeles (credenciales, información personal, tokens de sesión), se impone una política de privacidad y seguridad absoluta (Regla de oro 10): función estrictamente opt-in (desactivada por defecto), residencia exclusiva en memoria RAM volátil sin escribir jamás a disco o logs, exclusión inmediata de formatos de administradores de contraseñas, vaciado total de memoria al bloquear sesión o suspender el equipo, escucha nativa por eventos Win32 sin sondeo (`AddClipboardFormatListener`), resiliencia ante bloqueos COM (`CLIPBRD_E_CANT_OPEN`), y respeto absoluto a no robar foco (`WS_EX_NOACTIVATE`).

- **Decisiones Técnicas:**

  1. **Lógica Pura y Aislamiento en Core (Regla de Oro 5):**
     - Se implementan `ClipboardItemKind` (`Text`, `Url`, `Image`, `Files`), `ClipboardItem`, `ClipboardFormatter` y `ClipboardHistoryManager` en `OpenDynamic.Core.Clipboard` sin ninguna referencia a Windows ni a WPF.
     - `ClipboardFormatter`: Sanitiza cadenas de texto en una sola línea colapsando espacios y saltos (`\r\n`), trunca de forma segura a 80 caracteres con elipsis, clasifica automáticamente URLs absolutas HTTP/HTTPS/FTP y genera etiquetas opacas para imágenes (`"Imagen copiada"`) y colecciones de archivos (`"{n} archivo(s)"`).
     - `ClipboardHistoryManager`: Administra la colección en memoria RAM con capacidad configurable (1 a 10 elementos, default 5) y tiempo de expiración configurable (default 10 min) utilizando `TimeProvider` inyectable para pruebas deterministas.
     - Supresión de Duplicados Consecutivos: Si el nuevo contenido coincide con el elemento superior activo, no se genera una nueva entrada, refrescando únicamente la marca de tiempo de expiración. Al volver a copiar un elemento desde la lista se evita la duplicación.

  2. **Privacidad y Seguridad Absoluta (Regla de Oro 10):**
     - **Estricto Opt-In:** `EnableClipboardWidget = false` por defecto. Si el usuario no activa explícitamente la función, ningún listener nativo se registra en Windows.
     - **Cero Persistencia a Disco:** El historial vive exclusivamente en memoria RAM. Queda terminantemente prohibido volcar contenido de portapapeles a `settings.json`, bases de datos o archivos temporales.
     - **Logs de Auditoría Sanitizados:** Queda prohibido escribir texto copiado, URLs, rutas de archivos o buffers de imagen en los archivos de log de Serilog. Únicamente se registran metadatos agregados (`Kind`, `Length`, `Count`).
     - **Purga de Memoria en Bloqueo y Suspensión:** Ante eventos de bloqueo de sesión de Windows (`SessionSwitchReason.SessionLock`) o suspensión de energía (`PowerModes.Suspend`, `PBT_APMSUSPEND`), así como al apagar la app o desactivar el interruptor, se invoca inmediatamente `Clear()` vaciando por completo el búfer en memoria RAM.

  3. **Escucha Reactiva Win32 y Exclusión de Gestores de Contraseñas:**
     - `ClipboardService` en `OpenDynamic.App.Services`: Registra reactivamente `AddClipboardFormatListener` sobre el HWND de `IslandWindow` y procesa el mensaje de ventana `WM_CLIPBOARDUPDATE` (`0x031D`). Cero polling (Regla de Oro 1).
     - Al desactivar la función o cerrar la ventana, se invoca `RemoveClipboardFormatListener` desenganchando el listener de forma limpia.
     - **Exclusión de Gestores de Contraseñas:** Se inspeccionan los formatos nativos registrados:
       * `ExcludeClipboardContentFromMonitorProcessing`
       * `Clipboard Viewer Ignore`
       * `CanIncludeInClipboardHistory` (con valor DWORD 0)
       * `CanUploadToCloudClipboard` (con valor DWORD 0)
       Si cualquiera de estos formatos está presente en el portapapeles, el procesamiento se aborta de inmediato sin leer ningún dato.
     - **Detección de Auto-Copia:** Cuando el usuario vuelve a copiar un elemento desde la vista expandida de la isla, se registra la secuencia nativa (`GetClipboardSequenceNumber`) para evitar generar un aviso transitorio recursivo o un duplicado.

  4. **Resiliencia ante Contención COM (`CLIPBRD_E_CANT_OPEN` - Regla de Oro 4):**
     - El acceso concurrente al portapapeles por navegadores u otras aplicaciones suele arrojar la excepción COM `0x800401D0` (`CLIPBRD_E_CANT_OPEN`). Se implementa un bucle de reintento desacoplado y asíncrono de hasta 3 intentos espaciados por 50 ms antes de descartar pacíficamente el intento sin congelar la interfaz ni tumbar la aplicación.

  5. **Notch UI, No Activación de Foco y Jerarquía de Prioridades:**
     - `ClipboardWidget`: Asignado a `ActivityPriority.Clipboard = 55`.
     - Aviso Transitorio: Notificación de 2.0 s en modo compacto que presenta "Copiado: <vista previa>" (o "Texto copiado" si `ShowClipboardPreview == false`).
     - Modo Expandido (`ClipboardExpandedView`): Presenta la lista reciente con indicador de estado en RAM, retroalimentación táctil/visual ("Copiado de nuevo"), botón de borrado rápido y todos los controles interactivos con `Focusable="False"` garantizando respeto a `WS_EX_NOACTIVATE` sin robar el foco de la ventana activa del usuario.
     - Jerarquía Consolidada:
       `TimerAlert (100) > Battery (90) > Volume (80) > Network (65) > Device (60) > Clipboard (55) > Timer (50) > Stopwatch (45) > Media (30) > Hardware (10)`.

  6. **Ajustes, Menú en Bandeja y Migración de Esquema v7 (Regla de Oro 9):**
     - Se incrementa `CurrentSchemaVersion = 7` en `AppSettings.cs`.
     - `SettingsService.Load()` efectúa la migración automática para esquemas `< 7`, inicializando `EnableClipboardWidget = false` (opt-in estricto), `DefaultClipboardPriority = 55`, `ClipboardTransientDurationSeconds = 2.0`, `ShowClipboardPreview = true`, `ClipboardHistoryCapacity = 5` y `ClipboardExpirationMinutes = 10`.
     - Nueva tarjeta en la pestaña "Widgets y Prioridades" de `SettingsWindow.xaml` con interruptores, deslizadores y botón de borrado inmediato.
     - Submenú en el icono de la bandeja del sistema (`TrayIconManager`) con opciones para pausar/reanudar el monitoreo y vaciar el historial en memoria.

---

## ADR-023: Detección Pasiva de Acceso a Micrófono y Cámara vía Windows ConsentStore (RegNotifyChangeKeyValue) e Insignias Integradas en el Notch (Fase 15)

- **Estado:** Aceptado
- **Fecha:** 2026-09-30
- **Contexto:**
  La ampliación v1.1 (Fase 15) incorpora un indicador visual de privacidad para informar al usuario cuando una aplicación de escritorio o paquete MSIX/UWP inicia o cesa el uso de los sensores físicos de audio (micrófono) y video (cámara/webcam). Dado el impacto en la privacidad, estabilidad y rendimiento del sistema operativo:
  - openDynamic jamás debe abrir ni capturar dispositivos físicos.
  - La monitorización debe realizarse con cero consumo de CPU en reposo (Regla de Oro 1: cero polling).
  - La lógica debe estar puramente aislada en Core (Regla de Oro 5).
  - El diseño visual debe integrarse de forma sutil en la muesca (Notch UI) sin desplazar destructivamente el widget activo.

- **Decisiones Técnicas:**

  1. **Naturaleza Informativa Pasiva y Privacidad Absoluta (Reglas de Oro 10 y 11):**
     - **No es una Herramienta de Seguridad ni un Antivirus:** Esta funcionalidad es una comodidad informativa basada en estructuras internas y claves no documentadas de Windows (`CapabilityAccessManager\ConsentStore`). No garantiza detección de software malicioso o rootkits de bajo nivel.
     - **Prohibición Estricta de Captura Física:** Queda terminantemente prohibido el uso de APIs de captura como `MediaCapture`, `DirectShow`, o interfaces de grabación WASAPI. openDynamic solo realiza lecturas pasivas del registro de Windows.
     - **Rutas de Registro:**
       * `HKCU\Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\microphone`
       * `HKCU\Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\webcam`
     - **Degradación Elegante:** Si las claves del registro no existen en el sistema o presentan fallos de lectura, la monitorización se desactiva de forma pacífica registrando una advertencia estructurada en Serilog sin abortar la aplicación.

  2. **Escucha Reactiva Nativa sin Sondeo (Regla de Oro 1: Cero Polling):**
     - `PrivacyAccessMonitor` en `OpenDynamic.App.Services`: Registra reactivamente eventos de kernel Win32 mediante `RegNotifyChangeKeyValue` con `bWatchSubtree = true` y filtros `ChangeName | ChangeLastSet`.
     - Hilo de Fondo Dedicado (`PrivacyConsentStoreWatcher`): Permanece 100% suspendido en el kernel de Windows a través de `WaitHandle.WaitAny` esperando señales de `_micEvent`, `_camEvent` o el evento de parada `_stopEvent`. Cero consumo de ciclos de CPU en reposo.
     - Al cerrar la app o desactivar los interruptores en Ajustes, se señaliza `_stopEvent`, se espera la finalización del hilo (`Join` seguro) y se liberan todos los handles del registro y del kernel (`AutoResetEvent`, `ManualResetEvent`, `SafeRegistryHandle`). Cero hilos ni handles huérfanos.

  3. **Lógica Pura y Aislamiento en Core (Regla de Oro 5):**
     - Se implementan `PrivacyResourceType`, `PrivacyAccessEntry`, `PrivacyAccessState`, `PrivacyAccessChange` y `PrivacyAccessAggregator` en `OpenDynamic.Core.Privacy` sin dependencias de Windows ni WPF.
     - **Evaluación FILETIME de 64 bits:** Una aplicación se determina activamente en uso si y solo si:
       `LastUsedTimeStart > 0 && (LastUsedTimeStop == 0 || LastUsedTimeStart > LastUsedTimeStop)`.
     - `PrivacyConsentStoreParser`: Decodifica rutas ejecutables codificadas con `#` a `\`, extrae nombres amigables de ejecutables y formatea identificadores de paquetes conocidos ("Cámara de Windows", "Grabadora de voz", etc.).
     - **Lista de Exclusión (`IgnoredPrivacyApps`):** Permite al usuario descartar aplicaciones de sistema o procesos en segundo plano. Excluye automáticamente `openDynamic`.
     - Inyección de `TimeProvider` para pruebas unitarias deterministas.

  4. **Identidad Visual Notch e Insignias Sutiles Persistentes (Regla de Oro 8):**
     - **Punto de Estado Superpuesto:** Mientras haya recursos activos, se muestra un punto sutil en la esquina superior del notch (verde `#34C759` para cámara, naranja/ámbar `#FFFF9500` para micrófono).
     - **Convivencia No Destructiva:** Los indicadores se sitúan como superposición decorativa dentro de `IslandView`, sin desplazar ni contraer el widget primario activo (Música, Temporizador, etc.).
     - **Respeto a la Visibilidad:** Si la isla está oculta por el usuario o suspendida por pantalla completa, las insignias no fuerzan la aparición de la muesca.

  5. **Aviso Transitorio (`PrivacyWidget`):**
     - Widget transitorio asignado a `ActivityPriority.Privacy = 85` (jerarquía: `TimerAlert 100 > Battery 90 > Privacy 85 > Volume 80 > Network 65 > Device 60 > Clipboard 55 > Timer 50 > Stopwatch 45 > Media 30 > Hardware 10`).
     - Duración predeterminada de 3.0 segundos. Informa: "Micrófono en uso: <app>", "Cámara en uso: <app>", "Micrófono/Cámara liberado".

  6. **Ajustes y Migración de Esquema v8 (Regla de Oro 9):**
     - Se incrementa `CurrentSchemaVersion = 8` en `AppSettings.cs`.
     - `SettingsService.Load()` migra transparentemente versiones anteriores inicializando `EnableMicrophoneIndicator = true`, `EnableCameraIndicator = true`, `EnablePrivacyAlerts = true`, `DefaultPrivacyPriority = 85`, `PrivacyTransientDurationSeconds = 3.0` e `IgnoredPrivacyApps = []`.
     - Nueva tarjeta en la pestaña "Widgets y Prioridades" de `SettingsWindow.xaml` con interruptores independientes, deslizadores de prioridad/duración y gestión de apps ignoradas.

---

## ADR-024: Visualizador de Espectro de Audio Real con FFT Propia sin Dependencias, Captura WASAPI Loopback Resiliente, Búferes Dobles Sin Bloqueos y Cero Asignaciones por Cuadro (Fase 16)

- **Estado:** Aceptado
- **Fecha:** 2026-09-30
- **Contexto:**
  Para la ampliación v1.1 (Fase 16), openDynamic sustituye las barras simuladas del widget multimedia por un espectro real y reactivo del audio del sistema. Requisitos críticos del diseño:
  - **Pureza en Core (Regla de Oro 5):** Cero librerías externas de FFT (prohibido MathNet, KissFFT, etc.).
  - **Cero asignaciones de memoria por cuadro (Regla de Oro 11):** Preasignación estricta de búferes de memoria fija reutilizables para garantizar cero presión sobre el recolector de basura (GC).
  - **Política de activación estricta (Regla de Oro 1):** La captura WASAPI y el ciclo de dibujo (~30 FPS) existen ÚNICAMENTE mientras hay música en reproducción activa, el widget es visible, la isla no está oculta y no hay pantalla completa activa. Si la música se pausa, se cambia de pista o se oculta la isla, la captura y el timer se detienen de inmediato (0% CPU).
  - **Privacidad absoluta (Regla de Oro 10):** Procesamiento exclusivamente volátil en memoria RAM; queda prohibido persistir muestras en disco, archivos temporales o registrar amplitudes/frecuencias en logs de Serilog.
  - **Resiliencia y degradación elegante (Regla de Oro 4):** Manejo de cambios en caliente de endpoint de audio y degradación pacífica a Simulado si WASAPI falla o arroja excepciones COM.
  - **Presupuesto de rendimiento:** CPU adicional estrictamente inferior al 2% en hardware real con música sonando y 0% en reposo.

- **Decisiones Técnicas:**

  1. **Algoritmo FFT Propio Cooley-Tukey Radix-2 en Core (`SpectrumAnalyzer`):**
     - Se implementa `SpectrumAnalyzer` en `OpenDynamic.Core.Audio.Spectrum` con una FFT propia iterativa Radix-2 (tamaño $N = 1024$) con ventana de Hann precalculada y solape del 50% (512 muestras de salto).
     - Tablas de twiddles y permutación bit-reversal precalculadas en el constructor (cero llamadas trigonométricas repetitivas en tiempo de ejecución).
     - Agrupación en bandas logarítmicas: 12 bandas para la vista Compacta y 24 bandas para la vista Expandida (cubriendo el rango psicoacústico de 45 Hz a 16.5 kHz).
     - Compensación de inclinación espectral (Pink noise tilt ~ -3dB/octava) y mapeo perceptual en decibelios (dBFS) a rango normalizado [0.0, 1.0].
     - Suavizado temporal con ataque rápido (0.65f) para golpes percusivos y decaimiento lento (0.85f) para fluidez visual.
     - Cero asignaciones en el heap por cuadro validadas mediante pruebas unitarias automatizadas con `GC.GetAllocatedBytesForCurrentThread() == 0`.

  2. **Política Pura de Activación (`VisualizerActivationPolicy`):**
     - La captura solo debe activarse si y solo si se cumplen simultáneamente:
       a) Modo configurado en `Real` (`AudioVisualizerMode.Real`).
       b) Sesión GSMTC en estado de reproducción activa (`IsMediaPlaying == true`).
       c) Widget multimedia visible en la isla (`IsMediaWidgetVisible == true`).
       d) La isla no está oculta (`IslandState != IslandState.Hidden`).
       e) No hay supresión activa por pantalla completa exclusiva (`IsFullscreenSuppressed == false`).
     - Si cualquiera de estas condiciones deja de cumplirse, WASAPI loopback y el ciclo de dibujo se detienen de inmediato volviendo a 0% de CPU.

  3. **Captura WASAPI Loopback Resiliente en App (`AudioSpectrumService`):**
     - Utiliza `WasapiLoopbackCapture` de NAudio sobre el dispositivo de audio predeterminado del sistema.
     - Conversión estéreo a mono ultra-rápida sin asignaciones con `MemoryMarshal.Cast<byte, float>`.
     - Doble búfer sin bloqueos (Lock-free double buffering con `Interlocked.Exchange`) para transferir bandas calculadas del hilo de captura al hilo de UI/render sin contención.
     - Reenganche automático en caliente al cambiar el dispositivo de reproducción predeterminado reutilizando la notificación de `VolumeService.DefaultDeviceChanged`.
     - Manejo de silencio: si no llegan paquetes de audio en > 60 ms (silencio en loopback compartido), se invoca `DecayOnly()` para que las barras caigan suavemente a cero.
     - Resiliencia COM: cualquier excepción COM o de hardware degrada limpiamente a modo Simulado (`AudioVisualizerMode.Simulated`) registrando una advertencia estructurada en Serilog sin abortar la aplicación.

  4. **Ciclo de Dibujo a ~30 FPS en Notch UI (`MediaCompactView` y `MediaExpandedView`):**
     - Las vistas se suscriben a `CompositionTarget.Rendering` únicamente cuando `IsVisualizerActive == true` y están cargadas en el árbol visual.
     - Bucle limitado a ~30 FPS (intervalo >= 33 ms) actualizando directamente las alturas de las barras coloreadas con el acento dinámico de la carátula (`AccentBrush`).
     - Al pausar la música, ocultar la isla o cambiar de pantalla, se desuscriben al instante y resetean a altura base.
     - Respeto de `MotionMode.Reduced`: en modo de animaciones reducidas se suprimen las animaciones visuales del ecualizador.

  5. **Privacidad Absoluta (Regla de Oro 10):**
     - Las muestras PCM viven única y exclusivamente en el búfer circular en memoria RAM durante el procesamiento FFT. Queda terminantemente prohibido volcar muestras a disco, base de datos o logs de Serilog.

  6. **Ajustes y Migración de Esquema v9 (Regla de Oro 9):**
     - Incremento a `CurrentSchemaVersion = 9` en `AppSettings.cs` con migración retrocompatible en `SettingsService.Load()`.
     - Selector de modo ("Desactivado", "Simulado", "Real") e interruptor rápido para alternar visualizador reactivo en Ajustes.

  7. **Presupuesto de Rendimiento Medido:**
     - Consumo medido en pruebas de benchmark: 50 ms de CPU para procesar 5 segundos continuos de audio a 48 kHz (1.0% de un solo núcleo, < 0.15% de CPU total del sistema). Cumple estrictamente con el presupuesto de < 2% de CPU adicional y 0% en reposo.

---

## ADR-025: Arquitectura de Aprobaciones de Agente por Named Pipe y Superficie de Revisión en Notch (Fase 17)

- **Estado:** Retirada (Withdrawn) por Tarea R1 (Sustituida por ADR-027)
- **Fecha:** 2026-09-30
- **Contexto:**
  openDynamic integra el sistema de intercepción y aprobación de acciones de agentes autónomos de codificación (Google Antigravity) directamente en la superficie del Notch/Dynamic Island. Esta integración permite inspeccionar y autorizar comandos del sistema, ediciones de archivos y llamadas a herramientas sin interrumpir el flujo visual del desarrollador ni robar el foco de la terminal (`WS_EX_NOACTIVATE`).
  
  Para esta integración se aplican con el máximo rigor las Reglas de Oro de Seguridad 12, 13 y 14:
  - **Falla hacia lo seguro (Regla de Oro 12):** Ante cualquier fallo, excepción, timeout (90 s), desconexión, mensaje malformado, servidor ausente, isla oculta o pantalla completa exclusiva, el hook DEBE responder `"ask"` (delegar al diálogo nativo de Antigravity), NUNCA `"allow"`. Queda terminantemente PROHIBIDO aprobar comandos automáticamente en esta fase. Solo decide el usuario físicamente.
  - **Comunicación y privacidad del pipe (Regla de Oro 13):** Named Pipe local `openDynamic-agent-v1` restringido exclusivamente al token de seguridad del usuario actual (`PipeOptions.CurrentUserOnly`). Mensajes acotados con límite estricto de tamaño (256 KB) y versionados (protocolo v1). CERO LOGS DE COMANDOS: Queda terminantemente prohibido registrar en Serilog comandos completos, argumentos, rutas de archivos o variables de entorno. Solo se auditan metadatos agregados (`RequestId`, `ToolName`, `RiskLevel`, `Decision`, `Source`, `ElapsedMs`).
  - **Clasificador de riesgo y Notch UI (Reglas 6, 12 y 13):** Clasificación de riesgo determinista (Low, Medium, High) en Core con garantía de cero falsos bajos para comandos destructivos o encadenados. Truncado visual seguro a 200 caracteres con revisión expandida obligatoria (`RequiresExpandedReview`) si el contenido fue truncado o si el riesgo es High. Guarda anti-clic accidental de 600 ms antes de habilitar botones interactivos. Atajos de teclado dinámicos (1 permitir, 5 denegar) inhabilitados para riesgo High. Opciones de denegación predefinidas sin entrada de texto libre en la isla.
  - **Cliente hook ligero e instalador seguro (Reglas 13 y 14):** CLI ligero `OpenDynamic.Hook` optimizado con ReadyToRun para un arranque ultra-rápido (< 150 ms presupuestados, ~65 ms medidos). Salida a `stdout` estrictamente formateada en JSON `{"decision","reason"}` con código de salida 0. Instalador `AntigravityHookInstaller` con respaldo automático `.bak` de `hooks.json`, fusión no destructiva de la clave `openDynamic-approvals`, y desinstalación limpia que conserva las herramientas preexistentes del usuario.

- **Decisiones Técnicas:**

  1. **Modelo de Dominio y Protocolo IPC Bounded en Core (`OpenDynamic.Core.AgentApprovals`):**
     - `ApprovalRequest`: Modelo inmutable que extrae y tipifica campos estándar de hooks PreToolUse de Antigravity (`toolCall.name`, `toolCall.args`, `conversationId`, `stepIdx`, `workspacePaths`, etc.).
     - `ApprovalResponse`: Factorías seguras para `allow`, `deny` (con motivo) y `ask` (delegación segura con motivo).
     - `PipeMessage`: Envoltorio de mensajes IPC con validación de versión (`Version = 1`), tipos de mensaje (`request`, `response`, `ping`, `pong`), y tamaño máximo acotado a 256 KB (`MaxPayloadSizeBytes = 262144`).

  2. **Clasificador de Riesgo Determinista en Core (`CommandRiskClassifier`):**
     - **High:** Comandos destructivos o de alto impacto para el sistema (`rm -rf`, `Remove-Item -Recurse`, `git push --force`, `git reset --hard`, `curl|sh`, `iex`, `reg delete`, `format`, `dd`, `mkfs`, modificaciones a particiones o servicios del sistema).
     - **Medium:** Modificaciones de estado estándar, comandos encadenados (`&&`, `;`, `|`), redirecciones (`>`, `>>`) y herramientas con efectos secundarios.
     - **Low:** Comandos de solo lectura, inspección y consulta (`git status`, `git log`, `dir`, `ls`, `pwd`, `dotnet --version`, `cat`, etc.).
     - Regla de oro: Ante la menor ambigüedad o presencia de encadenamientos/subshell, se clasifica como Medium o High, garantizando cero falsos bajos.

  3. **Presentación Visual Segura (`ApprovalPresentation`):**
     - Formatea títulos descriptivos y resúmenes legibles en lenguaje natural.
     - Aplica truncado a 200 caracteres con elipsis. Si el texto se truncó o si el riesgo es High, establece `RequiresExpandedReview = true`, bloqueando el botón de permitir hasta que el usuario expanda visualmente la vista.

  4. **Política de Sesión Pura en Core (`ApprovalSessionPolicy`):**
     - Controla la guarda anti-clic accidental de 600 ms mediante `TimeProvider`.
     - Impone timeout de sesión de 90 segundos; si transcurre sin resolución física del usuario, expira de inmediato a `AskNative`.
     - Prohíbe atajos de teclado para riesgo High (`CanAllowViaHotkey = false`).

  5. **Servidor Named Pipe Resiliente (`AgentApprovalPipeServer`):**
     - Escucha en `openDynamic-agent-v1` utilizando `PipeOptions.CurrentUserOnly | PipeOptions.Asynchronous`.
     - Si la isla está oculta (`IslandState.Hidden`), en modo juego o en pantalla completa exclusiva (`IsFullscreenSuppressed == true`), responde de inmediato con `RejectBySystemUnavailable` delegando a `ask`.
     - CERO LOGS: Audita exclusivamente `RequestId`, `ToolName`, `Risk`, `Decision`, `Source` y `ElapsedMs`. Queda prohibido registrar `CommandLine`, `CodeContent` o rutas en Serilog (validado por `ZeroLogsSecurityTests`).

  6. **Widget Notch con Prioridad 95 (`ApprovalWidget`):**
     - Prioridad 95 (superior a medios, sistema y dispositivos; solo por debajo de notificaciones críticas de privacidad).
     - Auto-expande automáticamente si `RequiresExpandedReview == true`.
     - Ventana configurada con `WS_EX_NOACTIVATE` para garantizar cero robo de foco de la terminal o del IDE durante la aparición y expansión del widget.
     - Enlace de atajos HWND en la ventana de la isla: `[1]` permitir esta vez, `[2]` permitir en conversación (Fase 18), `[3]` permitir en workspace (Fase 18), `[4]` permitir siempre (Fase 18), `[5]` denegar con menú de motivos rápidos (ej. "Enfoque incorrecto", "Comando destructivo", "Otro método"), `[Esc]` o botón "Decidir en Antigravity" para delegación segura.

  7. **Cliente Hook Ligero (`OpenDynamic.Hook`):**
     - Ensamblado ligero compilado como WinExe/Console con ReadyToRun.
     - Lectura no bloqueante de `stdin` y conexión rápida con timeout de 300 ms por defecto (configurable vía `--connect-timeout-ms` y `--wait-ms`).
     - Falla segura obligatoria (Golden Rule 12): ante cualquier excepción o timeout, emite por `stdout` `{"decision":"ask","reason":"..."}` con código de salida 0.

  8. **Instalador de Hooks Seguro (`AntigravityHookInstaller`):**
     - Respalda automáticamente `hooks.json` a `hooks.json.bak` antes de cualquier modificación.
     - Fusión no destructiva: preserva herramientas externas preexistentes del usuario, insertando o actualizando únicamente el gancho `openDynamic-approvals` en el evento `PreToolUse`.
     - Desinstalación limpia: elimina exclusivamente las claves propias de openDynamic y restaura la configuración original si no quedan otros ganchos.

  9. **Ajustes y Migración de Esquema v10 (Regla de Oro 9):**
     - `CurrentSchemaVersion = 10` en `AppSettings.cs`.
     - Interruptor `EnableAgentApprovals = false` desactivado por defecto (requiere activación explícita del usuario).
     - Tarjeta "Aprobaciones de Agente (Antigravity)" en Ajustes con visualizador de estado del hook, botones de instalación/desinstalación, botón "Enviar solicitud de prueba" e indicador de salud del servicio.

  10. **Presupuesto de Rendimiento Medido:**
      - Tiempo de arranque en frío de `OpenDynamic.Hook`: ~170 ms.
      - Tiempo de arranque en estado estacionario / caliente: ~61-66 ms (muy por debajo del presupuesto estricto de 150 ms).
      - Suite de pruebas de integración completa ejecutada en ~1.0 s sin deadlocks.



---

## ADR-026: Motor de Reglas de Aprobación, Cola FIFO de Solicitudes y Notificación de Estado del Agente (Fase 18)

- **Estado:** Retirada (Withdrawn) por Tarea R1 (Sustituida por ADR-027)
- **Fecha:** 2026-10-01
- **Contexto:**
  La Fase 18 completa la sustitución del diálogo nativo de Antigravity en Windows. En lugar de desplegar una tarjeta simplificada que delegue decisiones permanentes al diálogo inferior de Antigravity, el Notch de openDynamic despliega DIRECTAMENTE las 5 opciones estructuradas equivalentes a las opciones nativas de Antigravity:
  1. `[1] Sí, permitir esta vez`: Ejecuta el comando actual sin almacenar reglas de persistencia.
  2. `[2] Sí, y siempre en esta conversación`: Almacena una regla volátil en memoria RAM vinculada al `conversationId` activo; desaparece al cerrar la aplicación o cambiar de sesión.
  3. `[3] Sí, y siempre en este proyecto`: Almacena una regla persistente exacta en el archivo `.antigravity/approval-rules.json` ubicado en la raíz del espacio de trabajo del proyecto actual.
  4. `[4] Sí, y siempre globalmente`: Almacena una regla persistente exacta en `%USERPROFILE%/.antigravity/approval-rules.json` válida para todos los proyectos y conversaciones del usuario.
  5. `[5] No: Denegar`: Despliega un menú de motivos predefinidos ("Enfoque incorrecto", "Comando destructivo", "Otro método", etc.) para informar al agente sin necesidad de entrada de texto libre en la muesca.

  Además, se resuelven los escenarios de concurrencia cuando múltiples agentes o tareas emiten solicitudes de autorización simultáneas (cola FIFO), y se intercepta el evento de finalización del agente (`Stop`) para notificar al desarrollador de manera no intrusiva en la muesca (Prioridad 70).

- **Decisiones Técnicas:**

  1. **Restricción de Seguridad Obligatoria para Alto Riesgo y Edición de Archivos (Regla de Oro 12):**
     - Si el clasificador `CommandRiskClassifier` determina que el comando es de riesgo `High` (ej. comandos destructivos como `rm -rf`, formateos, borrado masivo de claves de registro, cambios forzados de ramas en Git), o si la herramienta invocada es de escritura/modificación de archivos (`write_to_file`, `replace_file_content`, `multi_replace_file_content`), el Notch OCULTA AUTOMÁTICAMENTE las opciones 2, 3 y 4.
     - En estos casos, la interfaz muestra exclusivamente las opciones `[1] Sí, permitir esta vez` y `[5] No: Denegar`. Bajo ninguna circunstancia se permite almacenar reglas ni auto-aprobar acciones destructivas o escrituras en disco.

  2. **Motor de Reglas con Coincidencia Exacta (`ApprovalRuleMatcher` y `ApprovalRuleStore`):**
     - Las reglas se aplican exclusivamente a la herramienta `run_command` en la versión 1.
     - Coincidencia exacta estricta (*exact match*): se valida carácter por carácter tras aplicar `Trim()` y normalización de saltos de línea (`\r\n` a `\n`). Se preservan los espacios internos y el casing (mayúsculas/minúsculas). Comandos como `npm test` no coinciden con `npm  test` ni con `npm test --watch`.
     - Persistencia atómica y resiliente: escrituras en archivo temporal `.tmp` con reemplazo atómico y copia de seguridad previa `.bak`. Si el archivo JSON se corrompe por cierres abruptos, el motor recupera automáticamente el archivo `.bak` para evitar pérdida de reglas. Capacidad máxima limitada a 200 reglas por ámbito con política de poda LRU (menos recientemente utilizadas).
     - Aplicación automática instantánea: si una solicitud entrante coincide con una regla autorizada, el hook responde `allow` de inmediato sin desplegar la tarjeta interactiva ni interrumpir al desarrollador, emitiendo una notificación transitoria de 2 segundos en el Notch ("Permitido por regla: <resumen>") configurable en Ajustes.

  3. **Reglas de Prefijo Seguro Opt-In (Tarea 6b):**
     - Como funcionalidad opcional desactivada por defecto (`EnableAgentSafePrefixRules = false`), se permite crear reglas basadas en prefijo para comandos repetitivos con argumentos benignos (`git status`, `git diff`, `git log`, `dotnet build`, `dotnet test`, etc.).
     - Restricciones infranqueables:
       - Solo se permite en el ámbito de Proyecto (nunca Global ni Conversación).
       - Exige límite estricto de token (espacio en blanco tras el prefijo; ej. `git log` coincide con `git log --oneline`, pero rechaza `git logging`).
       - Valida que el resto del comando contenga únicamente caracteres benignos (alfanuméricos, espacios, y signos `. _ - : / \ = , ' "`).
       - Prohibición tajante de caracteres de encadenamiento o subshell (`;`, `&`, `|`, `>`, `<`, comillas invertidas, `$()`, saltos de línea).
       - Exclusión estricta de intérpretes y shells (`pwsh`, `cmd`, `bash`, `wsl`, `python`, `node`) y descargadores (`curl`, `wget`, `certutil`, `iex`).

  4. **Cola FIFO Multisesión de Solicitudes y Concurrencia:**
     - Manejo de múltiples solicitudes concurrentes en `ApprovalWidget` mediante cola FIFO con indicador visual de posición (`1 / N`).
     - Cada solicitud en cola mantiene su temporizador de expiración (90 s) y su guarda anti-clic accidental (600 ms) de forma completamente independiente.
     - Los atajos dinámicos de teclado (`Ctrl+Alt+1` a `Ctrl+Alt+5`, `Enter`, `A`) y las acciones visuales se vinculan exclusivamente a la solicitud visible al frente de la cola.
     - La cancelación o desconexión de un cliente no corrompe la cola; la sesión afectada se descarta limpiamente y la siguiente toma el frente.

  5. **Notificación de Estado del Agente en el Notch (`AgentStatusWidget`, Prioridad 70):**
     - Intercepción del evento `Stop` mediante el subcomando `--event stop` inyectado en `hooks.json` bajo la clave `openDynamic-status`.
     - El cliente hook emite el evento al named pipe y responde de inmediato `{"decision": "stop"}` a Antigravity sin bloquear el apagado del agente.
     - Se muestra un aviso transitorio sutil en el Notch de 4 segundos ("Antigravity terminó en <proyecto>") con sonido opcional, **únicamente si `fullyIdle == true`**. Si aún hay tareas en segundo plano (`fullyIdle == false`), la notificación se suprime para evitar ruido visual.

  6. **Decisión de Descarte de la Insignia PostToolUse (Tarea 9):**
     - La especificación autorizaba omitir la Tarea 9 (insignia de espera en Antigravity mediante hook `PostToolUse`) si agregaba latencia o complejidad innecesaria.
     - Medición y evaluación técnica: invocar un proceso externo en cada llamada de herramienta (`PostToolUse`) introduce una sobrecarga acumulada de 60-150 ms en cada paso del agente. Dado que la delegación a `AskNative` transfiere de inmediato el control al diálogo nativo de Antigravity sin requerir seguimiento de estado en la muesca, se decidió omitir `PostToolUse` preservando la velocidad nativa del agente y el consumo de CPU en reposo (0%).

  7. **Investigación sobre `permissionOverrides` de Antigravity (Tarea 10):**
     - Se investigó el comportamiento de la propiedad `permissionOverrides` reportada en la salida JSON de hooks `PreToolUse`.
     - Hallazgo: En la versión actual de Antigravity CLI / IDE, devolver `permissionOverrides` desde un hook de comando no inyecta ni persiste de forma fiable la autorización en la base de datos interna de concesiones del IDE sin integración nativa profunda a nivel de extensión. Por ende, la persistencia de reglas gestionada por openDynamic en `.antigravity/approval-rules.json` y `%USERPROFILE%/.antigravity/approval-rules.json` resulta el mecanismo más robusto, portable y seguro. No se requiere implementación adicional de `permissionOverrides`.

  8. **Privacidad y Cero Registro de Datos Sensibles (Reglas de Oro 10 y 13):**
     - Se implementó `ApprovalHistoryTracker`, un búfer circular en memoria RAM de hasta 50 elementos que almacena resúmenes sanitizados (máximo 60 caracteres) y nombres de carpetas relativos, sin rutas completas del sistema operativo ni contenido de archivos o credenciales.
     - Verificado mediante pruebas unitarias y estáticas en `ZeroLogsSecurityTests`.

  9. **Evolución del Esquema de Ajustes (v11):**
     - Se actualizó `AppSettings.cs` a `CurrentSchemaVersion = 11`.
     - Nuevas opciones: `EnableAgentRuleAutoAllow` (activado por defecto al habilitar aprobaciones), `ShowAgentRuleAutoAllowNotices` (true), `EnableAgentSafePrefixRules` (false, opt-in), `AgentApprovalHighlightedOption` (1), `EnableAgentStatusNotifications` (true), y `PlayAgentStatusSound` (true).

---

## ADR-027: Retiro Completo de la Integración con Antigravity (Fases 17 y 18 - Tarea R1)

- **Estado:** Aceptado
- **Fecha:** 2026-10-01
- **Contexto:**
  Tras evaluar el impacto operativo, la complejidad arquitectónica y la experiencia de usuario de la integración con Google Antigravity desarrollada en las Fases 17 y 18, la dirección del proyecto decidió retirar íntegramente dicha integración. El objetivo es mantener openDynamic enfocado con máxima pureza en su propósito base como Dynamic Island de Windows, eliminando dependencias de IPC bidireccionales, clientes CLI intermedios, inyecciones en `hooks.json` y la complejidad añadida de motores de reglas y colas de solicitudes de agentes.

- **Alcance de Publicación Determinado (Tarea 2):**
  - Se verificó el historial de Git mediante `git tag --contains` para los commits de las Fases 17 y 18.
  - Resultado: Las Fases 17 y 18 **nunca fueron incluidas en ninguna versión o release público** (la única etiqueta existente en el repositorio es `v1.0.0` sobre el commit `a54f5a6`, anterior a la Fase 10).
  - Decisión consecuente: No se añade código de limpieza en tiempo de ejecución de la aplicación para `hooks.json` (manteniendo el binario libre de cualquier referencia a Antigravity). En su lugar, se proporciona el script auxiliar `scripts/limpiar-hooks-antigravity.ps1` con soporte para `-WhatIf` y copias de seguridad `.bak`.

- **Estrategia de Retiro Elegida (Tarea 4):**
  - **Estrategia B (Retiro Estructurado y Limpio):** Se ejecutó un retiro manual y sistemático basado en el inventario documentado, organizando la supresión de código, pruebas, empaquetado y documentación en pasos lógicos (`refactor`, `test`, `chore`, `docs`).
  - **Justificación:** Si bien la reversión de código no presentó conflictos técnicos sobre el código fuente, la Fase 17 no poseía un commit de fusión único (consistía en 12 commits lineales en `main`), y un `git revert` ciego eliminaba indebidamente los registros históricos ADR-025 y ADR-026 en `DECISIONS.md`, violando las directrices de integridad documental. La estrategia estructurada preserva el historial de decisiones, mantiene intactas las fases 0 a 16 y garantiza que tanto `dotnet build` como `dotnet test` pasen limpiamente en cada fase.

- **Inventario de Componentes Retirados:**
  1. **Core (`OpenDynamic.Core.AgentApprovals`):**
     - Protocolo IPC (`PipeMessage`, `ApprovalRequest`, `ApprovalResponse`, `RiskLevel`).
     - Clasificador de riesgos (`CommandRiskClassifier`).
     - Motor de reglas y almacén atómico (`ApprovalRule`, `ApprovalRuleMatcher`, `ApprovalRuleStore`, `ApprovalRuleScope`, `SafePrefixMatcher`).
     - Políticas de sesión y presentación (`ApprovalSessionPolicy`, `ApprovalPresentation`, `ApprovalHistoryTracker`, `AgentStatusEvent`).
     - Instalador de hooks (`AntigravityHookInstaller`).
  2. **App (`OpenDynamic.App`):**
     - Servidor Named Pipe `AgentApprovalPipeServer`.
     - Activador Win32 `AntigravityWindowActivator` y P/Invokes no compartidos en `NativeMethods`.
     - Widgets de isla: `ApprovalWidget` (prioridad 95) y `AgentStatusWidget` (prioridad 70), junto con sus vistas compactas y expandidas.
     - Pestaña de Antigravity y sección de reglas en `SettingsWindow.xaml` y `SettingsViewModel.cs`.
     - Registro y enlaces dinámicos de atajos (`Ctrl+Alt+1` a `5`, `Enter`, `A`).
  3. **Proyecto CLI `OpenDynamic.Hook`:**
     - Eliminado el proyecto `OpenDynamic.Hook.csproj` y `Program.cs`.
     - Removido de la solución `openDynamic.sln`.
     - Removido de la publicación ReadyToRun en `.github/workflows/release.yml`.
  4. **Empaquetado e Instalador Inno Setup:**
     - Agregada sección `[InstallDelete]` en `installer/setup.iss` para eliminar `{app}\hook` y `{app}\OpenDynamic.Hook.exe` en actualizaciones.
  5. **Ajustes y Datos Locales:**
     - Mantenido `SchemaVersion = 11` en `AppSettings.cs` para evitar degradaciones.
     - Propiedades de Antigravity retiradas de `AppSettings.cs`; la deserialización ignora propiedades obsoletas sin fallar y se omiten al guardar.
     - Migración automática en `SettingsService.Load()` que elimina de forma segura archivos residuales `approval-rules.json` y `approval-rules.json.bak` en `%AppData%\openDynamic\` sin registrar comandos en logs.

- **Línea Base de Pruebas:**
  - Pruebas iniciales antes del retiro: 551 pruebas.
  - Pruebas eliminadas: 161 pruebas (13 archivos en `tests/OpenDynamic.Tests/AgentApprovals/`).
  - Nuevas pruebas de regresión añadidas: 2 pruebas en `SettingsServiceTests` (validación de carga retrocompatible ignorando campos obsoletos y eliminación segura de `approval-rules.json`).
  - Total de pruebas resultantes: 392 pruebas en verde (línea base original de 390 + 2 nuevas).

---

## ADR-028: Reloj Ambiental en Reposo (Fase 19)

- **Estado:** Aceptado
- **Fecha:** 2026-10-02
- **Contexto:**
  openDynamic carecía de un comportamiento contextual visual en la Dynamic Island cuando no existían actividades en curso (reproducción multimedia, hardware, temporizadores, volumen, etc.). El usuario solicitó la incorporación de un reloj ambiental discreto que se active al pasar el cursor sobre la muesca cuando la isla está en reposo (`Hidden`), mostrando la hora, fecha y opcionalmente la semana ISO, sin comprometer el presupuesto de 0% de uso de CPU en reposo ni interferir con la jerarquía de widgets existente.

- **Decisiones Técnicas:**
  1. **Modo de Activación `OnHover` y Prioridad Mínima (Regla de Oro 1):**
     - Se introdujo `ActivityActivationMode` (`Event` vs `OnHover`) en `IActivitySource` y `IslandWidgetBase`.
     - `AmbientClockWidget` se configura con `ActivationMode = ActivityActivationMode.OnHover` y prioridad 5 (`ActivityPriority.AmbientClock`), la más baja de la isla.
     - `PriorityResolver` garantiza que las actividades de tipo `Event` prevalecen de forma absoluta. Las actividades `OnHover` solo son elegibles cuando no hay eventos activos y el cursor se encuentra sobre el sensor de la muesca (`isHovering == true`).
     - El reloj nunca mantiene visible la isla de forma autónoma ni bloquea la transición al estado `Hidden`.
  2. **Presupuesto de 0% CPU en Reposo y Ciclo de Vida del Temporizador (Reglas de Oro 1 y 11):**
     - El temporizador de actualización (`DispatcherTimer`) solo existe y corre mientras el widget está visible en pantalla (`SetDisplayState(..., isVisible: true)`).
     - Al ocultarse la muesca, el temporizador se detiene, desenlaza sus eventos y se destruye inmediatamente (`StopTimer()`), garantizando 0% de CPU en reposo.
     - Mediante `ClockTickScheduler`, el primer tick se sincroniza exactamente al segundo cero del minuto entrante (`:00.000`), evitando llamadas innecesarias por segundo cuando los segundos están desactivados.
  3. **Sincronización Reactiva con Win32 `WM_TIMECHANGE` (Cero Polling):**
     - En `IslandWindow.xaml.cs`, el procedimiento de ventana nativo intercepta `WM_TIMECHANGE = 0x001E` y escucha `SystemEvents.TimeChanged`.
     - Se despacha un mensaje reactivo desacoplado `SystemTimeChangedMessage` que refresca instantáneamente la hora y recalcula el retardo del scheduler si el widget está visible, sin bucles de polling en segundo plano.
  4. **Localización y Formateo Cultural:**
     - `ClockFormatter` utiliza `CultureInfo.CurrentCulture` para la obtención de nombres de días, meses y patrones horarios.
     - Soporta modos `Auto` (obtenido del formato del sistema), `TwelveHour` (12 horas con AM/PM) y `TwentyFourHour` (24 horas), alternancia de segundos, fecha y cálculo ISO 8601 del número de semana (`CalendarWeekRule.FirstFourDayWeek`, `DayOfWeek.Monday`).
  5. **Configuración, Migración y Ajustes (Esquema v12):**
     - Se promovió `CurrentSchemaVersion` de 11 a 12 en `AppSettings.cs`.
     - Opciones añadidas: `EnableAmbientClock`, `ClockTimeFormat`, `ClockShowSeconds`, `ClockShowDate`, `ClockShowWeekNumber` y `DefaultAmbientClockPriority`.
     - Migración limpia en `SettingsService.Load()` manteniendo retrocompatibilidad y preservando configuraciones previas.
     - Tarjeta moderna en `SettingsWindow.xaml` integrada con `SettingsViewModel`.
  6. **Interacción y Umbral de Activación de Reposo:**
     - En `IslandWindow.xaml.cs`, cuando la isla está en reposo (`Hidden`), se detecta el sobrevuelo del cursor en el área de la muesca con un retraso antirrebote intencional de 250 ms antes de desplegar el reloj, y un retardo de 350 ms al salir del sensor para evitar transiciones accidentales.

- **Consecuencias y Verificación:**
  - 426 pruebas unitarias automáticas en verde (100% de la suite).
  - Pruebas existentes de `PriorityResolver` intactas y superadas al 100%.
  - Compilación Release limpia con 0 advertencias (`TreatWarningsAsErrors`).

---

## ADR-029: Monitorización Reactiva del Ahorro de Energía de Windows y Perfil Puro de Recursos en Core (Fase 20)

- **Estado:** Aceptado
- **Fecha:** 2026-10-02
- **Contexto:**
  El sistema operativo Windows incorpora el modo de Ahorro de Energía (*Energy Saver / Battery Saver*), activado por umbral de batería baja o por decisión del usuario en el Centro de Control. openDynamic requería:
  1. Detectar transiciones de estado de forma 100% reactiva sin bucles de sondeo (*zero polling*, 0% de CPU en reposo).
  2. Implementar una política pura de recursos en `OpenDynamic.Core` sin dependencias de Windows ni WPF que determine el perfil de recursos (`ResourceProfile`: Standard vs Efficient), adaptando la tasa de cuadros/física de resortes, el modo del visualizador de espectro y la cadencia de telemetría de hardware, respetando de forma prioritaria las configuraciones explícitas del usuario.
  3. Manejo resiliente de PCs de escritorio y hardware sin batería (`EnergySaverState.NotSupported`), sin lanzar excepciones ni degradar el servicio.
  4. Supresión estricta de alertas espurias durante el arranque de la aplicación y en una ventana de gracia de 10 segundos tras reanudar el sistema desde suspensión/hibernación, con enfriamiento (*cooldown*) de 5 segundos entre alertas consecutivas.
  5. Interfaz de usuario integrada con widget transitorio en la muesca (`EnergySaverWidget`, prioridad 88, 3 segundos) y controles completos en Ajustes con migración limpia a esquema v13 en `AppSettings`.

- **Decisiones Técnicas:**
  1. **Monitoreo Reactivo Multicapa de Energía (WNF `ntdll.dll` + Win32 `WM_POWERBROADCAST` + `GetSystemPowerStatus`) (Reglas de Oro 1 y 11):**
     - En Windows 11 (24H2), el mosaico de Configuración Rápida "Ahorro de energía" (`SettingsHandlers_OneCore_BatterySaver.dll`) publica las conmutaciones manuales del usuario a través de **Windows Notification Facility (WNF)** en `ntdll.dll` sobre el estado `WNF_PO_ENERGY_SAVER_OVERRIDE` (`0x41C6013DA3BC3075`: `1` = Forzado Activo / *Enabled*, `2` = Forzado Inactivo / *Disabled*, `0` = Automático por política) y el estado automático en `WNF_PO_ENERGY_SAVER_STATE` (`0x41C6013DA3BC2075`: `2` = Activo, `1` = Inactivo), sin modificar `SYSTEM_POWER_STATUS.SystemStatusFlag` ni `PowerManager.EnergySaverStatus` cuando el nivel de batería supera el umbral automático.
     - `EnergySaverService` se suscribe de forma 100% reactiva (cero polling) mediante `RtlSubscribeWnfStateChangeNotification` a `WNF_PO_ENERGY_SAVER_OVERRIDE` y `WNF_PO_ENERGY_SAVER_STATE`, manteniendo el delegado fijado en memoria y liberando las suscripciones con `RtlUnsubscribeWnfStateChangeNotification` en `Dispose()`.
     - Adicionalmente, `IslandWindow` registra `GUID_POWER_SAVING_STATUS` oficial de `winnt.h` (`E00958C0-C213-4ACE-AC77-FECCED2EEEA5`) y `GUID_ENERGY_SAVER_POLICY` (`5C5BB349-AD29-4EE2-9D0B-2B25270F7A81`), e intercepta `WM_POWERBROADCAST` (`PBT_APMPOWERSTATUSCHANGE` y `PBT_POWERSETTINGCHANGE`).
     - `EnergySaverService.QueryLiveEnergySaverState()` consulta en vivo `NtQueryWnfStateData` y `GetSystemPowerStatus`, resolviendo el estado mediante el mapeador puro `EnergySaverStateMapper.FromWnf(wnfOverride, wnfState, systemStatusFlag, hasBattery)`.
     - **Detección Fidedigna de Hardware con Batería:** Para evitar falsos positivos en laptops conectadas a la corriente, se comprueba la presencia física de batería con `GetSystemPowerStatus`. `EnergySaverState.NotSupported` solo se emite si el hardware carece de batería (`BatteryFlag == 128` o `BatteryLifePercent == 255`).
  2. **Política Pura de Alertas (`EnergySaverAlertPolicy`) con `TimeProvider` (Regla de Oro 5):**
     - Ubicada en `OpenDynamic.Core.EnergySaver`, sin referencias a UI.
     - Implementa supresión en el arranque (`Initialize`), impidiendo notificaciones flotantes al iniciar openDynamic.
     - Implementa ventana de supresión de 10 segundos tras reanudación de suspensión/hibernación (`NotifySuspended()`, `NotifyResumedFromSuspend()`), amortiguando lecturas inestables transitorias del subsistema ACPI de Windows.
     - Implementa enfriamiento reactivo antirrebote (*cooldown*) para evitar saturación de la isla ante fluctuaciones rápidas de hardware.
  3. **Política Determinista de Recursos (`ResourceProfilePolicy`):**
     - Resuelve el `ResourceProfile` activo combinando el estado de energía, las opciones globales de modo eficiente y las preferencias explícitas del usuario.
     - Reglas de precedencia estrictas:
       - Si el usuario configuró explícitamente `MotionMode.Full`, el modo de energía no reduce las animaciones (solo lo hace si `MotionMode == Auto` y `EnergySaverReduceAnimations == true`).
       - Si el visualizador de audio está en `Reactive` y `EnergySaverCapAudioVisualizer == true`, se reduce a `Simulated` para liberar la captura de bucle loopback y el procesamiento FFT. Si el usuario configuró `Off`, se mantiene en `Off`.
       - Si `EnergySaverThrottleHardwareSampling == true`, el intervalo de muestreo de hardware se espacia al valor configurado (por defecto 5.0 s en lugar de 2.0 s).
  4. **Adaptación en Caliente en App y Widgets:**
     - `IslandWindow.xaml.cs`: Escucha `ResourceProfileChanged` del `EnergySaverService` y reconfigura los resortes elásticos en vivo; intercepta `WM_POWERBROADCAST` (`PBT_APMPOWERSTATUSCHANGE`, `PBT_APMSUSPEND`, `PBT_APMRESUMEAUTOMATIC`, `PBT_APMRESUMESUSPEND`) para notificar transiciones, suspensión y reanudación.
     - `HardwareWidget`: Recibe `IResourceProfileProvider` e intercambia su cadencia de muestreo en tiempo real ante cambios de perfil.
     - `MediaWidget`: Recibe `IResourceProfileProvider` y conmuta el modo visualizador dinámicamente entre reactivo y simulado según el perfil activo.
  5. **Widget de Muesca Transitorio (`EnergySaverWidget`) y Retención al Desplegar:**
     - Prioridad 88 (`ActivityPriority.EnergySaver`), con duración transitoria base de 3 segundos en modo compacto.
     - Vistas XAML `EnergySaverCompactView`, `EnergySaverExpandedView` y `EnergySaverSplitView` adaptadas al Upper Notch UI con esquinas asimétricas, icono de batería/hoja y acentos verdes (#34D399).
     - **Comportamiento al Desplegar por Clic:** Al hacer clic sobre la notificación transitoria en la muesca, `IslandWindow` invoca `RequestExpand()`. El widget entra en `OnExpand()`, detiene el temporizador de auto-expiración (`_transientTimer?.Stop()`) y limpia `TransientDuration = null`. La tarjeta expandida permanece abierta de forma persistente mientras el cursor del usuario se encuentre sobre el notch interactivo expandido. Al salir del área (`OnPointerLeave` / `_hoverLeaveTimer`), `OnCollapse()` se ejecuta y desactiva la alerta limpiamente retornando al reposo.
  6. **Ajustes y Migración de Esquema v13 (Regla de Oro 9):**
     - Se promovió `CurrentSchemaVersion` de 12 a 13 en `AppSettings.cs`.
     - Nuevas opciones persistidas: `EnableEnergySaverAlerts`, `DefaultEnergySaverPriority`, `EnergySaverTransientDurationSeconds`, `EnableEnergySaverEfficientMode`, `EnergySaverReduceAnimations`, `EnergySaverCapAudioVisualizer`, `EnergySaverThrottleHardwareSampling` y `EnergySaverHardwareSamplingIntervalSeconds`.
     - Migración automática v12 -> v13 en `SettingsService.cs` sin pérdida de configuraciones existentes.
     - Tarjeta "🌱 Ahorro de Energía de Windows" en `SettingsWindow.xaml` con indicador de estado en tiempo real (`EnergySaverStateSummaryText`) suscrito a `EnergySaverStatusChangedMessage`.

- **Consecuencias y Verificación:**
  - 525 pruebas unitarias automáticas en verde (100% de la suite; 72 nuevas pruebas incorporadas en la fase).
  - Cero bucles de sondeo; consumo de CPU estrictamente del 0% en reposo.
  - Compilación Release limpia con 0 errores y 0 advertencias (`TreatWarningsAsErrors`).

---

## ADR-030: Vista Previa Reactiva de Capturas de Pantalla, Estabilidad de Archivo No Bloqueante y Acciones Seguras (Fase 21)

- **Estado:** Aceptado
- **Fecha:** 2026-10-05
- **Contexto:**
  Cuando el usuario realiza una captura de pantalla guardada en disco (mediante `Win + Impr Pant` o al guardar desde la herramienta Recortes / Snipping Tool), openDynamic debe presentar una miniatura instantánea en la muesca superior con acciones rápidas (copiar imagen al portapapeles, abrir en el visor predeterminado, mostrar en el Explorador de archivos, arrastrar hacia otra aplicación y enviar a la Papelera de reciclaje), además de un historial reciente en memoria de las últimas 5 capturas.
  Esta funcionalidad está sujeta a restricciones técnicas y de privacidad estrictas:
  1. **Monitoreo 100% reactivo (Reglas de Oro 1 y 11):** Vigilancia exclusiva mediante `FileSystemWatcher` sobre la carpeta `KnownFolder` del sistema (`FOLDERID_Screenshots` vía `SHGetKnownFolderPath`, con respaldo a `%UserProfile%\Pictures\Screenshots`) y una carpeta adicional opcional configurada por el usuario. El `FileSystemWatcher` existe y permanece activo únicamente cuando la función está habilitada en Ajustes y la carpeta existe; al desactivarla, se detiene y destruye (`Dispose`) de inmediato sin dejar bucles de sondeo (*zero polling*).
  2. **Espera asíncrona de escritura y cero bloqueo de archivo (Reglas de Oro 4 y 10):** Los archivos recién creados o renombrados desde archivos temporales pueden tardar decenas de milisegundos en terminar de escribirse. Se debe verificar la estabilidad de escritura fuera del hilo de UI y decodificar la miniatura en memoria (`BitmapCacheOption.OnLoad`, `DecodePixelWidth <= 320`, `Freeze()`) cerrando inmediatamente el `FileStream`, de modo que el archivo en disco jamás quede bloqueado y pueda borrarse o moverse libremente tras la vista previa.
  3. **Operaciones seguras sobre archivos:** El arrastre (*Drag & Drop*) debe usar estrictamente `DragDropEffects.Copy` (jamás `Move`); el envío a la Papelera exige doble confirmación explícita en la UI y utiliza `SHFileOperationW` con `FO_DELETE | FOF_ALLOWUNDO` (jamás eliminación permanente `File.Delete`); y solo se actúa sobre rutas canónicas validadas dentro de las carpetas vigiladas, rechazando enlaces simbólicos (*symlinks* / *reparse points*) que escapen de dichas carpetas.
  4. **Privacidad absoluta en logs y memoria:** En Serilog solo se registran extensión, tamaño en bytes y dimensiones; nunca nombres de usuario, rutas completas ni nombres de archivo. La miniatura vive únicamente en memoria RAM mientras el aviso está activo y se libera (`CurrentThumbnail = null`) al cerrarse.

- **Decisiones Técnicas:**
  1. **Política Pura en Core (`OpenDynamic.Core.Screenshots`) (Regla de Oro 5):**
     - `ScreenshotFileFilter`: Validador puro y determinista sin dependencias de WPF ni Win32. Acepta exclusivamente extensiones de imagen permitidas (`.png`, `.jpg`, `.jpeg`, `.bmp`, `.gif`, `.webp`), descarta archivos temporales o parciales (`.tmp`, `.partial`, `.crdownload`, `.part`, nombres con `~` o prefijo `.`), rechaza archivos con tamaño $\le 0$ o anteriores al inicio de la vigilancia (`fileTimestampUtc < watchStartedUtc`), y valida la contención canónica dentro de las carpetas vigiladas (`IsPathWithinWatchedFolders` y `ValidateSafeImageFileOnDisk`), rechazando enlaces simbólicos o puntos de reanálisis (`FileAttributes.ReparsePoint` / `ResolveLinkTarget`) cuyo destino salga de las carpetas vigiladas.
     - `FileStabilityPolicy` y `FileStabilityTracker`: Política determinista inyectada con `TimeProvider`. Un archivo se considera completo cuando su tamaño ($> 0$) permanece inalterado durante `300 ms` y puede abrirse en modo de lectura compartida (`FileShare.Read`), con un tiempo máximo de espera de `3 s` tras el cual se descarta sin bloquear el hilo de interfaz.
     - `ScreenshotHistory` y `ScreenshotEntry`: Historial volátil en memoria RAM (capacidad por defecto de 5 rutas, retención temporal configurable por defecto de 30 minutos, vaciado al cerrar la app, bloquear sesión o suspender el equipo). Evalúa dinámicamente la existencia del archivo para marcar entradas faltantes como `"No disponible"` y permitir quitarlas de la lista.
  2. **Vigilancia Reactiva y Decodificación No Bloqueante (`ScreenshotWatcherService`):**
     - Resuelve `FOLDERID_Screenshots` (`{B7BEDE81-DF94-4682-A7D8-57A52620B86F}`) mediante P/Invoke a `SHGetKnownFolderPath` en `shell32.dll` (liberando el puntero con `Marshal.FreeCoTaskMem`), con respaldo a `%UserProfile%\Pictures\Screenshots`.
     - Escucha eventos `Created` y `Renamed` con deduplicación por ruta en vuelo (`_inFlightPaths`) y ventana antirrebote de `1500 ms` (`_recentlyProcessedUtc`) para evitar notificaciones duplicadas cuando herramientas como Recortes crean un archivo temporal y lo renombran.
     - `TryLoadFrozenBitmapFromDisk`: Lee los bytes del archivo en un bloque `using (var fileStream = new FileStream(..., FileShare.ReadWrite | FileShare.Delete))` hacia un `MemoryStream` local y cierra el descriptor del sistema operativo inmediatamente. Sobre el `MemoryStream` en RAM obtiene las dimensiones reales (`BitmapDecoder`) y construye el `BitmapImage` con `BitmapCacheOption.OnLoad`, `DecodePixelWidth = 320` y `Freeze()`. El archivo en disco queda 100% libre de bloqueos.
  3. **Integración con Portapapeles sin Auto-Disparo (`ClipboardService.CopyImageToClipboardAsync`):**
     - La acción **Copiar imagen** decodifica el bitmap completo congelado en memoria y lo escribe en el portapapeles de Windows con hasta 3 reintentos de `50 ms` ante `CLIPBRD_E_CANT_OPEN`.
     - Registra `GetClipboardSequenceNumber()` y la marca temporal interna en `ClipboardService` para que el listener `WM_CLIPBOARDUPDATE` de la Fase 14 ignore este cambio propio y no emita un aviso duplicado.
  4. **Widget de Muesca (`ScreenshotWidget`, Prioridad 75) y Flujo de Doble Confirmación de Papelera:**
     - Registrado con prioridad `75` (`ActivityPriority.Screenshot`), ubicándose por debajo de Volumen (`80`) y por encima de Red (`65`), Dispositivos (`60`) y Portapapeles (`55`).
     - Duración transitoria por defecto de `6.0 s` en modo compacto; se pausa automáticamente mientras el cursor permanece sobre la muesca (`OnViewMouseEnter` / `OnViewMouseLeave`) o al expandir la vista (`OnExpand`). Al colapsarse (`OnCollapse`), libera `CurrentThumbnail = null`.
     - **Arrastrar y Soltar Seguro:** Inicia `DragDrop.DoDragDrop` con un `DataObject(DataFormats.FileDrop, new[] { entry.FilePath })` y estrictamente `DragDropEffects.Copy` (nunca `Move`).
     - **Envío a Papelera con Doble Confirmación:** El botón `Papelera` transita por dos pasos explícitos en la interfaz (`Paso 1/2: ¿Enviar captura a la Papelera?` $\rightarrow$ `Continuar` $\rightarrow$ `Paso 2/2: Confirmación final: ¿Reciclar archivo?` $\rightarrow$ `Sí, reciclar`) antes de invocar `NativeMethods.SendFileToRecycleBin`, que ejecuta `SHFileOperationW` con `FO_DELETE | FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI`.
  5. **Ajustes, Documentación de Limitación de `Win+Shift+S` y Migración v14 (Regla de Oro 9):**
     - Promovido `AppSettings.CurrentSchemaVersion` de `13` a `14` con migración automática v13 $\rightarrow$ v14 en `SettingsService.cs`.
     - Añadida tarjeta **"📸 Vista Previa de Capturas de Pantalla"** en `SettingsWindow.xaml` con aviso explícito documentando que `Win + Shift + S` por sí solo solo copia al portapapeles sin crear archivo en disco (atendido por la Fase 14), mientras que `Win + Impr Pant` y las capturas guardadas en archivo sí activan esta vista previa.

- **Consecuencias y Verificación:**
  - 586 pruebas unitarias automáticas en verde (100% de la suite; 61 nuevas pruebas unitarias y de seguridad/privacidad añadidas en la Fase 21).
  - Cero bloqueos de archivo tras la vista previa, cero rutas/nombres de archivo en logs y 0% CPU en reposo cuando la función está en espera o desactivada.
  - Compilación Release limpia con 0 errores y 0 advertencias (`TreatWarningsAsErrors`).

---

## ADR-031: Auditoría Integral de Fin a Fin, Cero Timers en Reposo y Preparación de Release v2.0.0

- **Estado:** Aceptado
- **Fecha:** 2026-10-05
- **Contexto:**
  Previo al lanzamiento mayor **v2.0.0** (que consolida 10 fases de ingeniería posteriores a `v1.0.0`: Fases 10–16 y 19–21), se ejecutó una auditoría integral de extremo a extremo (Pasada A de solo lectura y Pasada B de corrección y optimización) sobre los 203 archivos de código fuente de la solución (`OpenDynamic.Core`, `OpenDynamic.App` y `OpenDynamic.Tests`), midiendo el binario real en configuración `Release` y evaluando los 30 hallazgos identificados (`AUD-001` a `AUD-030`).

- **Decisiones Técnicas Aprobadas:**
  1. **Eliminación de Sondeo en Reposo y Reducción de Franja Sensora (`AUD-001`, `AUD-005` — Reglas de Oro 1 y 11):**
     - Se eliminó por completo `_restingHoverWatcherTimer` (que ejecutaba `GetCursorPos` cada `100 ms` de forma perpetua en estado `Hidden`).
     - Se redujo `RestingSensorNotch` de `200x36 DIP` a una franja mínima superior de **`120x4 DIP`** centrada en el borde superior (`OffsetY = 0`), la cual intercepta `WM_NCHITTEST` (`HTCLIENT`) única y exclusivamente cuando `EnableAmbientClock == true`, `!IsFullscreenSuppressed` y `!IsPowerSuspended`.
     - Cuando la muesca está en estado `Hidden`, existen **0 timers activos** en el proceso y las pestañas superiores de navegadores o barras de título bajo la zona central son 100% clicables sin interferencia.
  2. **Aplicación en Caliente de Interruptores `Enable*Widget` (`AUD-002`):**
     - `AppSettings` se inyecta en `MediaWidget`, `VolumeWidget`, `BatteryWidget`, `TimerWidget` y `StopwatchWidget`, y `App.ApplySettingsToServices` detiene o inicia en tiempo real los servicios asociados (`MediaService`, `AudioSpectrumService`, `TimingUiCoordinator`) al conmutar cada ajuste en la ventana de Ajustes.
     - Al desactivar un widget en caliente, este limpia sus temporizadores internos y llama a `Deactivate()` de inmediato.
  3. **Desacoplamiento Asíncrono y Liberación de Contadores GPU PDH (`AUD-008` — Regla de Oro 4):**
     - `HardwareService` ejecuta la enumeración de instancias `GPU Engine` y la lectura `NextValue()` de `PerformanceCounter` en tareas de fondo (`Task.Run`) protegidas con una guarda atómica no solapada (`Interlocked.CompareExchange`), evitando bloqueos de `15–400 ms` en el hilo de UI de WPF.
     - Al desactivar `EnableGpuMonitoring` o `EnableHardwareMonitoring`, todos los `PerformanceCounter` se liberan de inmediato mediante `Dispose()`.
  4. **Sustitución de `NAudio` por `NAudio.Wasapi` y Métrica Oficial de Memoria (`AUD-013`, `AUD-014`):**
     - Se reemplazó el metapaquete `NAudio` (`3.1.0`) por `NAudio.Wasapi` (`2.3.0`), eliminando ensamblados innecesarios (`NAudio.WinForms.dll`, `NAudio.Midi.dll`, `NAudio.Asio.dll`) del directorio de publicación.
     - Se establece `PrivateMemorySize64` (`< 80 MB`, medido en `~41–44 MB`) como la métrica oficial de memoria privada comprometida del proceso en .NET 10 + WPF D3D11, documentando que `WorkingSet64` (`~150–160 MB`) incluye páginas mapeadas compartidas de DirectX/GPU/OS entre todos los procesos de escritorio.
  5. **Privacidad en Registros de Producción y Saneamiento de Configuración (`AUD-004`, `AUD-009`, `AUD-010`):**
     - En compilaciones `Release`, `LoggingConfiguration` fija el nivel mínimo de Serilog en `Information` (`Debug` solo en `#if DEBUG`), y se redactaron todas las trazas que contenían SSIDs de redes Wi-Fi, títulos/artistas multimedia, nombres de dispositivos USB/Bluetooth, etiquetas de temporizadores o rutas locales con nombre de usuario.
     - `AppSettings.SanitizeAndClamp()` y `LenientEnumConverter<TEnum>` garantizan que valores fuera de rango o enumeraciones inválidas en `settings.json` se recorten a límites seguros sin colapsar el arranque, soportando migración limpia desde `v1.0.0` (con o sin propiedad `"SchemaVersion"` explícita) hasta `CurrentSchemaVersion = 14`.
  6. **Gobernanza Open-Source y Empaquetado v2.0.0 (`AUD-011`, `AUD-012`):**
     - Se centralizó la versión `2.0.0` en `Directory.Build.props`, sincronizada con `app.manifest` (`2.0.0.0`) e `installer/setup.iss` (`2.0.0` con `LicenseFile=..\LICENSE`).
     - Se incorporaron `THIRD-PARTY-NOTICES.md`, `SECURITY.md`, `CONTRIBUTING.md`, plantillas de Issues/PRs, exclusión de `.pdb` en los artefactos de distribución, generación de `SHA256SUMS.txt` y lanzamiento en modo borrador (`draft: true`) en `.github/workflows/release.yml`.

- **Consecuencias y Verificación:**
  - 619 pruebas unitarias automáticas en verde al 100% en 3 corridas consecutivas en `Release` (0 advertencias con `TreatWarningsAsErrors=true` y `dotnet format --verify-no-changes` limpio).
  - Consumo de CPU en reposo certificado en **0.00%** con cero timers activos en estado `Hidden`.



