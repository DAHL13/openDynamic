# Informe de Auditoría Integral Pre-Lanzamiento — openDynamic `v2.0.0` (Pasada A)

**Fecha de auditoría:** 5 de octubre de 2026  
**Rama de auditoría:** `audit/v2.0.0` (creada desde `main` @ `e734faf`)  
**Etiqueta de respaldo pre-auditoría:** `pre-auditoria-v2` (`e734faf`)  
**Modalidad:** Pasada A — Auditoría integral de solo lectura y medición empírica en `Release` (cero modificaciones de código de producto en `src/` o `tests/`)

---

## 1. Resumen Ejecutivo

El repositorio `openDynamic` tras el cierre de la Fase 21 (`e734faf`) presenta una arquitectura limpia entre `OpenDynamic.Core` (`net10.0`, puro y libre de dependencias de UI/Win32) y `OpenDynamic.App` (`net10.0-windows10.0.22621.0`, WPF + Win32/COM/WinRT), compilando en `Release` con **0 advertencias y 0 errores** y superando las **586 pruebas unitarias en ~580 ms** con **0 paquetes NuGet vulnerables** y **0 conexiones de red activas**.

Sin embargo, la inspección exhaustiva línea por línea combinada con el perfilado empírico del binario `Release` (`ReadyToRun`) y el análisis de los registros reales de ejecución reveló **30 hallazgos técnicos** (**4 Críticos**, **9 Altos**, **11 Medios** y **6 Bajos/Info**) que **impiden declarar el estado `Go` inmediato para `v2.0.0` sin ejecutar antes la Pasada B**:

1. **Incumplimiento de la Regla de Oro 1 (0 % CPU en reposo / cero timers en `Hidden`) [`AUD-001` — CRÍTICO]**: `IslandWindow` mantiene activo un `DispatcherTimer` de **100 ms (10 Hz)** (`_restingHoverWatcherTimer`) de forma permanente mientras la isla está en estado `Hidden` (reposo), despertando el hilo UI 10 veces por segundo para invocar P/Invoke `GetCursorPos` + `PointFromScreen`, sin detenerse siquiera en suspensión de energía (`PBT_APMSUSPEND`) ni en pantalla completa.
2. **Cinco interruptores de activación de widgets en Configuración son ignorados en tiempo de ejecución [`AUD-002` — CRÍTICO]**: Los ajustes `EnableMediaWidget`, `EnableVolumeWidget`, `EnableBatteryWidget`, `EnableTimerWidget` y `EnableStopwatchWidget` existen en `AppSettings` y `SettingsWindow`, pero **nunca son consultados** por sus respectivos widgets/servicios al dispararse eventos del sistema (p. ej., desactivar el widget de Multimedia, Volumen o Batería en Configuración no impide que sigan mostrándose en la isla).
3. **Fallo permanente de `StartBluetoothWatcher()` en Windows 11 (`COMException 0x8002802B`) y 3 alertas falsas de conexión de audio en cada arranque [`AUD-003` — CRÍTICO]**: `DeviceService.StartBluetoothWatcher()` pasa propiedades AEP no canónicas (`System.Devices.Aep.Bluetooth.Cod.MajorDeviceClass` y `System.Devices.BatteryLevel`) a `DeviceInformation.CreateWatcher`, lanzando siempre `COMException (0x8002802B: Property key syntax error)`. Además, su bloque `catch` invoca prematuramente `_policy.NotifyEnumerationCompleted()` antes de que `StartAudioRenderWatcher()` enumere los dispositivos existentes, provocando que **en cada arranque de la aplicación se disparen 3 alertas transitorias falsas de «Audífonos / Audio Conectado»**.
4. **Fuga de datos sensibles (SSID Wi-Fi, nombre de app usando cámara/micrófono, títulos de canciones, nombres de dispositivos y rutas con usuario de Windows) en logs de `Release` [`AUD-004` — CRÍTICO]**: Aunque `NetworkService` y `PrivacyAccessMonitor` evitan registrar datos privados, `NetworkWidget` registra el `Title` con el SSID Wi-Fi (`"Conectado a {SSID}"`) a nivel `INF`, `PrivacyWidget` registra el nombre de la aplicación que usa el micrófono o la cámara a nivel `INF`, `MediaColorService` registra `"{TrackTitle}|{TrackArtist}"` a nivel `DBG`, `VolumeService`/`VolumeWidget` registran el nombre amigable del dispositivo de audio (`FriendlyName`), y `LoggingConfiguration` mantiene `.MinimumLevel.Debug()` activo en `Release`.

### Evaluación Preliminar frente a Criterios Go/No-Go (Sección 6)

| Criterio Go/No-Go | Estado Actual | Evidencia / Bloqueante |
|---|---|---|
| `dotnet build -c Release` con 0 errores y 0 advertencias | **CUMPLE (GO)** | 0 advertencias, 0 errores en `2.37 s`. |
| 100 % de la suite de tests pasando en `Release` (3 pasadas) | **CUMPLE (GO)** | 586/586 pruebas superadas en 3 pasadas consecutivas (`601 ms`, `571 ms`, `582 ms`), 0 *flaky*. |
| 0 hallazgos **CRÍTICOS** o **ALTOS** abiertos | **NO-GO (Pendiente Pasada B)** | 4 hallazgos Críticos (`AUD-001` a `AUD-004`) y 9 Altos (`AUD-005` a `AUD-013`). |
| Cumplimiento verificado de las 11 Reglas de Oro | **NO-GO (Pendiente Pasada B)** | Incumplimiento en Regla 1 (`AUD-001`) y Regla 10 (`AUD-004`); cumplimiento parcial en Reglas 3 (`NAudio.WinForms`), 6 (`AUD-005`) y 8 (`AUD-009`). |
| Métricas reales en `Release` dentro de presupuesto | **PARCIAL** | CPU reposo `0.009 %` (< `0.5 %`), `PrivateMemorySize64` en reposo `71.04 MB` (< `80 MB`), pero `WorkingSet64` es `160.5 MB` por páginas compartidas de WPF/DirectX/ReadyToRun (`AUD-013`); bloqueo de `1.5 s` en hilo UI al activar GPU (`AUD-008`). |
| Instalador y ZIP portable generados y verificados | **PARCIAL** | ZIP (`9.22 MB`) e Instalador (`7.91 MB`) compilan, pero incluyen `.pdb` y `NAudio.WinForms.dll`, y la versión sigue en `1.0.0` (`AUD-012`, `AUD-022`). |
| Documentación, `CHANGELOG.md` y `THIRD-PARTY-NOTICES.md` listos | **NO-GO (Pendiente Pasada B)** | Faltan `THIRD-PARTY-NOTICES.md`, `SECURITY.md`, `CONTRIBUTING.md`, plantillas GitHub, y `CHANGELOG.md` aún no tiene el bloque `[2.0.0]` (`AUD-011`). |

---

## 2. Entorno y Línea Base Medida (A0 y A5)

### 2.1 Especificaciones del Equipo de Pruebas
- **Sistema Operativo:** Microsoft Windows 11 Pro (`10.0.26300` Build `26300`)
- **Procesador (CPU):** AMD Ryzen 5 7530U with Radeon Graphics (6 núcleos físicos, 12 procesadores lógicos)
- **Memoria Física (RAM):** 29.8 GB
- **Gráficos (GPU):** AMD Radeon (TM) Graphics
- **Pantalla y Escala DPI:** Monitor primario `1920x1080`, `AppliedDPI = 96` (Escala `100 %`)
- **Toolchain .NET:** .NET SDK `10.0.401` (Runtime `.NET 10.0.12`, MSBuild `18.9.11`, RID `win-x64`)
- **Compilador de Instalador:** Inno Setup 6 (`ISCC.exe`)

### 2.2 Tabla de Línea Base de Métricas (Sección A0.4 y A5)

| Métrica | Valor Medido (Línea Base) | Presupuesto Objetivo | Estado |
|---|---|---|---|
| Tiempo de `dotnet build -c Release` | `2.37 s` | — | OK |
| Advertencias de compilación (`Release`) | `0` | `0` | **Cumple** |
| Total de tests / Pasados / Fallidos / Omitidos | `586 / 586 / 0 / 0` | `100 % pasados` | **Cumple** |
| Tiempo de `dotnet test -c Release --no-build` (3 corridas) | `601 ms` / `571 ms` / `582 ms` | — | **Cumple (0 flaky)** |
| Tamaño de carpeta `publish/` (`ReadyToRun`, 26 archivos) | `31.68 MB` (`33,218,325` bytes) | — | Incluye `.pdb` y `NAudio.WinForms` |
| Tamaño de `openDynamic-v2.0.0-win-x64-portable.zip` | `9.22 MB` (`9,672,487` bytes) | — | OK |
| Tamaño de instalador (`openDynamic-setup.exe`) | `7.91 MB` (`8,297,587` bytes) | — | OK |
| Tiempo de arranque en frío (3 repeticiones: `OnStartup` → Listo / Proceso total) | `692 ms` / `724 ms` / `728 ms` (media `714.7 ms`; total desde creación de proceso `~855 ms`) | `< 2000 ms` (`< 2 s`) | **Cumple** |
| RAM en reposo (`WorkingSet64` / `PrivateMemorySize64`, 5 min) | `160.51–161.11 MB` WS / `71.04–72.73 MB` Privada | `< 80 MB` | **Privada cumple (`71.04 MB`); WS excede (`160.5 MB`)** |
| CPU en reposo (% promedio en 5 min, 12 hilos lógicos) | `0.007 % – 0.020 %` (media `0.0116 %`) | `0.0 % – 0.5 %` | **Cumple presupuesto numérico (pero con timer activo de 10 Hz `AUD-001`)** |
| RAM / CPU con música activa (GSMTC) + visualizador WASAPI | `165.51–165.57 MB` WS (`77.22 MB` Priv) / `0.004 % – 0.15 %` CPU | `< 110 MB` / `< 3 %` | **Privada y CPU cumplen; WS `165.5 MB`** |
| RAM / CPU con widget de hardware visible (solo CPU+RAM, `GPU=false`) | `162.71 MB` WS (`74.09 MB` Priv) / `0.391 %` CPU / `863` handles | `< 110 MB` / `< 2 %` | **Privada y CPU cumplen; WS `162.7 MB`** |
| RAM / CPU con widget de hardware visible (`GPU=true`, 118 contadores PDH) | `169.34–179.77 MB` WS (`77.43–87.27 MB` Priv) / `0.473 % – 0.586 %` CPU / `1,031` handles | `< 110 MB` / `< 2 %` | **+168 handles y bloqueo UI de `1,507 ms` al iniciar (`AUD-008`)** |
| Handles / Hilos tras 15 min de sesión multi-estado | `847–866` handles (`999` tras inicializar PDH GPU; estable y decreciente tras GC) / `22–27` hilos | Estable (sin crecimiento lineal) | **Estable (sin fuga continua)** |
| Conexiones de red TCP / UDP (`Get-NetTCPConnection` / `Get-NetUDPEndpoint`) | `0 TCP` / `0 UDP` en todos los escenarios | `0` | **Cumple** |

### 2.3 Detalle de Series Temporales Medidas (`scripts/medir-rendimiento-v2.ps1`)

#### A. Arranque en Frío (3 repeticiones sobre `publish\OpenDynamic.App.exe`)
| Repetición | Inicio `ConfigureLogging` | Fin `OnStartup` (Servicios + Ventana listos) | Delta Inicialización (`ms`) | `WorkingSet64` Inicial (`MB`) | `PrivateMemorySize64` Inicial (`MB`) | `Handles` |
|---|---|---|---|---|---|---|
| #1 | `16:21:00.717` | `16:21:01.409` | `692.0 ms` | `163.88 MB` | `78.04 MB` | `870` |
| #2 | `16:21:06.862` | `16:21:07.586` | `724.0 ms` | `166.35 MB` | `80.75 MB` | `865` |
| #3 | `16:21:12.879` | `16:21:13.607` | `728.0 ms` | `163.50 MB` | `78.14 MB` | `868` |

#### B. Reposo (Idle) durante 5 minutos (Muestreo cada 60 s)
| Muestra | Tiempo (`s`) | `WorkingSet64` (`MB`) | `PrivateMemorySize64` (`MB`) | `CPU_Avg_%` | `Handles` | `Threads` | `TCP` / `UDP` |
|---|---|---|---|---|---|---|---|
| 1/5 | `60 s` | `161.11 MB` | `72.73 MB` | `0.020 %` | `866` | `27` | `0 / 0` |
| 2/5 | `120 s` | `161.11 MB` | `72.73 MB` | `0.011 %` | `866` | `27` | `0 / 0` |
| 3/5 | `180 s` | `160.51 MB` | `71.04 MB` | `0.007 %` | `851` | `22` | `0 / 0` |
| 4/5 | `240 s` | `160.55 MB` | `71.04 MB` | `0.009 %` | `851` | `22` | `0 / 0` |
| 5/5 | `300 s` | `160.77 MB` | `71.04 MB` | `0.011 %` | `847` | `22` | `0 / 0` |

#### C. Escenarios Activos y Sesión Continua Alternando Estados
| Escenario | Muestra | `WorkingSet64` (`MB`) | `PrivateMemorySize64` (`MB`) | `CPU_Avg_%` | `Handles` | `Threads` | `TCP` |
|---|---|---|---|---|---|---|---|
| Música GSMTC + Espectro WASAPI (Compacto) | 1 (`30 s`) | `165.51 MB` | `77.26 MB` | `0.000 %` | `862` | `30` | `0` |
| Música GSMTC + Espectro WASAPI (Expandido) | 2 (`60 s`) | `165.57 MB` | `77.22 MB` | `0.004 %` | `856` | `26` | `0` |
| Hardware Widget (`GPU=false`, CPU+RAM) | 1 (`30 s`) | `162.71 MB` | `74.09 MB` | `0.391 %` | `863` | `31` | `0` |
| Hardware Widget (`GPU=true`, 118 contadores PDH) | 1 (`30 s`) | `169.34 MB` | `77.43 MB` | `0.586 %` | `1031` | `33` | `0` |
| Hardware Widget (`GPU=true`, Expandido) | 2 (`60 s`) | `179.77 MB` | `87.27 MB` | `0.473 %` | `1027` | `30` | `0` |
| Sesión Continua Multi-Estado (Hover/Volumen/Compacto/Oculto) | 1 (`30 s`) | `171.58 MB` | `78.86 MB` | `0.482 %` | `1026` | `29` | `0` |
| Sesión Continua Multi-Estado | 2 (`60 s`) | `166.90 MB` | `79.26 MB` | `0.386 %` | `1013` | `25` | `0` |
| Sesión Continua Multi-Estado | 3 (`90 s`) | `166.77 MB` | `77.77 MB` | `0.352 %` | `1003` | `25` | `0` |
| Sesión Continua Multi-Estado | 4 (`120 s`) | `166.60 MB` | `77.50 MB` | `0.369 %` | `999` | `25` | `0` |

---

## 3. Cumplimiento de las 11 Reglas de Oro

| # | Regla de Oro | Estado | Evidencia (`archivo:línea`) | Observaciones Técnicas |
|---|---|---|---|---|
| **1** | **Rendimiento en reposo (~0 % CPU, < 80 MB RAM, cero timers en `Hidden`)** | **Incumple** | `src/OpenDynamic.App/Windowing/IslandWindow.xaml.cs:37,101-106,163-166,403-406,1166-1178`<br>`src/OpenDynamic.App/App.xaml.cs:124-125` | `_restingHoverWatcherTimer` (`100 ms`) corre continuamente mientras `CurrentState == IslandState.Hidden` (`AUD-001`). Además, `PrivacyAccessMonitor`, `FullscreenWatcher`, `EnergySaverService`, `PowerService` y `VolumeService` se inician en el arranque aunque sus ajustes estén desactivados (`AUD-006`). `PrivateMemorySize64` es `71.04 MB`, pero `WorkingSet64` es `160.5 MB` (`AUD-013`). |
| **2** | **100 % Local y Privado (cero telemetría, cero red)** | **Cumple** | `src/OpenDynamic.App/Services/NetworkService.cs:1-215`<br>`Get-NetTCPConnection` = `0` | Ningún proyecto referencia `HttpClient`, `WebClient` ni sockets. `0` conexiones TCP/UDP verificadas empíricamente en todos los escenarios. |
| **3** | **Sin WinForms, sin Electron, sin WebView2** | **Cumple parcial** | `src/OpenDynamic.App/OpenDynamic.App.csproj:18`<br>`publish/NAudio.WinForms.dll` | El código propio no usa WinForms (`UseWindowsForms` no está habilitado), pero `OpenDynamic.App.csproj:18` referencia el metapaquete `NAudio 2.3.0` en lugar de `NAudio.Wasapi`, arrastrando `NAudio.WinForms.dll` (`80 KB`) y otros 4 ensamblados innecesarios a `publish/` (`AUD-014`). |
| **4** | **Aislamiento de fallos (cuarentena por widget)** | **Cumple** | `src/OpenDynamic.App/Orchestration/IslandOrchestrator.cs:231-239,673-678,698-715`<br>`src/OpenDynamic.App/App.xaml.cs:210-235` | `IslandOrchestrator` aísla excepciones en `SafeCreateView` y pone en cuarentena widgets defectuosos (`_quarantinedWidgetIds`). Manejadores globales en `Dispatcher`, `AppDomain` y `TaskScheduler`. |
| **5** | **Pureza de `OpenDynamic.Core`** | **Cumple** | `src/OpenDynamic.Core/OpenDynamic.Core.csproj:1-14` | Target `net10.0` puro; única dependencia `CommunityToolkit.Mvvm 8.4.2`. Cero referencias a WPF, WinForms, WinRT o P/Invoke. |
| **6** | **No robar el foco ni bloquear clics** | **Cumple parcial** | `src/OpenDynamic.App/Windowing/IslandWindow.xaml:18-25`<br>`src/OpenDynamic.App/Windowing/IslandWindow.xaml.cs:270-272,435-452`<br>`src/OpenDynamic.App/Orchestration/IslandOrchestrator.cs:456,725-732` | `WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW` y `WM_MOUSEACTIVATE -> MA_NOACTIVATE` funcionan correctamente. Sin embargo, `RestingSensorNotch` mide `240x44 DIP` con `WM_NCHITTEST -> HTCLIENT` en estado `Hidden`, bloqueando clics en pestañas de navegadores maximizados y permitiendo que el hover reactive el reloj encima de apps en pantalla completa (`AUD-005`). |
| **7** | **Autoridad única de estado (`IslandOrchestrator`)** | **Cumple parcial** | `src/OpenDynamic.App/Orchestration/IslandOrchestrator.cs:453-655`<br>`src/OpenDynamic.App/Windowing/IslandWindow.xaml.cs:375-378` | `IslandOrchestrator` centraliza las transiciones, aunque `IslandWindow.CheckAndApplyHiddenVisibility` (`línea 377`) invoca `_orchestrator.StateMachine.TryTransitionTo(IslandState.Hidden)` directamente sobre la máquina de estados interna. |
| **8** | **Reloj abstracto (`TimeProvider`)** | **Cumple parcial** | `src/OpenDynamic.Core/Settings/SettingsService.cs:18,288`<br>`src/OpenDynamic.Core/Media/MediaActivityController.cs:12,27`<br>`src/OpenDynamic.Core/Widgets/PriorityResolver.cs:29`<br>`src/OpenDynamic.Core/Screenshots/FileStabilityPolicy.cs:81` | La mayoría de controladores usan `TimeProvider`, pero `SettingsService` usa `System.Threading.Timer` directo (`SettingsServiceTests.cs:497` usa `Task.Delay(250)`), `MediaActivityController` usa `Func<DateTimeOffset>`, `PriorityResolver.Resolve` usa `DateTimeOffset.UtcNow` por defecto y `FileStabilityPolicy` usa `Task.Delay` directo (`AUD-009`). |
| **9** | **Accesibilidad y respeto al usuario** | **Cumple** | `src/OpenDynamic.App/Infrastructure/AccessibilityThemeManager.cs:17-73`<br>`src/OpenDynamic.App/Windowing/IslandWindow.xaml.cs:558-598` | Respeta `SPI_GETCLIENTAREAANIMATION`, `SystemParameters.HighContrast`, modo de movimiento (`Auto`/`Full`/`Reduced`) y propiedades `AutomationProperties.Name` / `LiveSetting`. (Nota: pinceles en `ApplyTheme(false)` no llaman a `.Freeze()`, `AUD-026`). |
| **10** | **Privacidad estricta en logs y memoria** | **Incumple** | `src/OpenDynamic.App/Infrastructure/LoggingConfiguration.cs:21,31`<br>`src/OpenDynamic.App/Widgets/Network/NetworkWidget.cs:120-121`<br>`src/OpenDynamic.App/Widgets/Privacy/PrivacyWidget.cs:186-187`<br>`src/OpenDynamic.App/Services/MediaColorService.cs:83,89,94`<br>`src/OpenDynamic.App/Services/VolumeService.cs:141,218` | El portapapeles y las capturas respetan la volatilidad en memoria, pero en los archivos de log se escriben SSIDs Wi-Fi (`NetworkWidget`), nombres de apps usando cámara/micrófono (`PrivacyWidget`), título y artista de canciones (`MediaColorService`), nombres de dispositivos de audio (`VolumeService`/`VolumeWidget`) y rutas con el nombre de usuario de Windows (`AUD-004`). |
| **11** | **Cero asignaciones en bucles de alta frecuencia** | **Cumple parcial** | `src/OpenDynamic.Core/Audio/Spectrum/SpectrumAnalyzer.cs:48-168`<br>`src/OpenDynamic.App/Services/AudioSpectrumService.cs:35-48,329-402`<br>`src/OpenDynamic.Core/Stopwatch/StopwatchController.cs:186` | `SpectrumAnalyzer` y `AudioSpectrumService` usan buffers preasignados y `stackalloc float[12/24]`. Sin embargo, `StopwatchController.CreateSnapshot()` ejecuta `_laps.ToList().AsReadOnly()` en cada tick de UI (~30 FPS cuando el cronómetro está visible) (`AUD-018`), y `MediaCompactView`/`MediaExpandedView` mantienen ambos bucles `CompositionTarget.Rendering` activos simultáneamente (`AUD-007`). |

---

## 4. Verificación de Hallazgos Históricos (Sección 2)

| Fase / ADR | Mecanismo Histórico | Estado Confirmado | Referencia Exacta (`archivo:línea`) |
|---|---|---|---|
| **Fase 1 (ADR-003, ADR-004, ADR-005)** | Instancia única (`Local\openDynamic-single-instance`), manejadores globales de excepciones, `SettingsService` con `.tmp` + `.bak` y debounce de `150 ms`. | **Confirmado (con observaciones en `SettingsService`)** | `SingleInstanceManager.cs:12-13,45-53`<br>`App.xaml.cs:210-235`<br>`SettingsService.cs:22,288-358` (Ver `AUD-009` y `AUD-010`). |
| **Fase 2 (ADR-006, ADR-007)** | Ventana overlay fija `640x240 DIP`, sin `WS_EX_TRANSPARENT`, estilos `WS_EX_TOOLWINDOW \| WS_EX_NOACTIVATE \| WS_EX_TOPMOST`, `WM_MOUSEACTIVATE -> MA_NOACTIVATE`, `IslandDebugWindow` y `DemoWidgets` bajo `#if DEBUG`. | **Confirmado (con regresión en `RestingSensorNotch` y `_restingHoverWatcherTimer`)** | `IslandWindow.xaml:7-8,18-25`<br>`IslandWindow.xaml.cs:101-106,270-272,435-452`<br>`IslandDebugWindow.cs:1,374`<br>`DemoWidgets.cs:1,363` (Ver `AUD-001` y `AUD-005`). |
| **Fase 3 (ADR-008)** | `PriorityResolver` determinista, modo `Split` con intercambio de cápsulas y cuarentena automática de widgets. | **Confirmado** | `PriorityResolver.cs:25-145`<br>`IslandOrchestrator.cs:470-503,698-715`. |
| **Fase 4 (ADR-009)** | `MediaService` vía GSMTC, extrapolación local de progreso (`1 s` solo en `Expanded` + `Playing`), `BitmapImage.Freeze()` con `DecodePixelWidth = 128` y gracia de `10 s` en pausa. | **Confirmado (pero falta respetar `EnableMediaWidget`)** | `MediaService.cs:48-115`<br>`MediaWidget.cs:615-646,679-716`<br>`WinRtMediaSession.cs:175-182` (Ver `AUD-002`). |
| **Fase 5 (ADR-010)** | `VolumeService` CoreAudio + `IMMNotificationClient` hot-swap + rueda del ratón sobre cápsula. | **Confirmado (pero falta respetar `EnableVolumeWidget` en `VolumeWidget`)** | `VolumeService.cs:59,128,330-385`<br>`VolumeWidget.cs:125-148,175-191` (Ver `AUD-002` y `AUD-020`). |
| **Fase 6 (ADR-011, ADR-012)** | `PowerService` por eventos `WM_POWERBROADCAST` + `BatteryThresholdTracker`; `FullscreenWatcher` con `SHQueryUserNotificationState` + comparación de rectángulos. | **Confirmado (pero falta respetar `EnableBatteryWidget` y optimizar `EVENT_OBJECT_LOCATIONCHANGE`)** | `PowerService.cs:53-115`<br>`BatteryThresholdTracker.cs:25-95`<br>`FullscreenWatcher.cs:51-77,156-249` (Ver `AUD-002`, `AUD-006`, `AUD-019`). |
| **Fase 7 (ADR-013, ADR-014)** | `HardwareService` con `GetSystemTimes` y `GlobalMemoryStatusEx` (timer activo solo si visible); `Timer` y `Stopwatch` por timestamps. | **Confirmado (con bloqueo de UI cuando `EnableGpuMonitoring = true`)** | `HardwareService.cs:49-182`<br>`HardwareWidget.cs:218-261`<br>`TimerController.cs:33-228` (Ver `AUD-008`, `AUD-016`, `AUD-017`). |
| **Fase 8 y 8B (ADR-015, ADR-015B, ADR-016)** | `TrayIconManager` (`H.NotifyIcon.Wpf` + recreación en `TaskbarCreated`), `WindowPositioner` multi-monitor/DPI (`WM_DISPLAYCHANGE`/`WM_DPICHANGED`), `HotkeyService` (`RegisterHotKey`), `AutostartService` (`HKCU\...\Run`). | **Confirmado (con fallo `ForceCreate` tras cierre abrupto y fuga `HICON`)** | `App.xaml.cs:143-152`<br>`TrayIconManager.cs:82,101-122,357-358`<br>`WindowPositioner.cs:35-140`<br>`HotkeyService.cs:62-145` (Ver `AUD-013B`/`AUD-015`). |
| **Fase 9 (ADR-017)** | Rediseño visual a muesca superior (`OffsetY = 0`, `CornerRadius="0,0,14,14"`, clip `PathGeometry` congelado con `.Freeze()`). | **Confirmado** | `IslandView.xaml:23,50`<br>`IslandView.xaml.cs:66,128-171`. |
| **Fase 10 (ADR-018)** | `MotionProfile` reactivo (`WM_SETTINGCHANGE`) y `AccessibilityThemeManager` (`HighContrast`). | **Confirmado** | `IslandWindow.xaml.cs:480-496,558-598`<br>`AccessibilityThemeManager.cs:17-73`. |
| **Fase 11 (ADR-019)** | `NetworkService` (`NetworkChange` + `NetworkInformation`) y `DeviceService` (`RegisterDeviceNotification` + `DeviceWatcher`) con supresión de tormenta inicial. | **Incumple en Bluetooth (`0x8002802B`) y fuga de SSID en `NetworkWidget`** | `NetworkService.cs:50-115`<br>`DeviceService.cs:394-434`<br>`NetworkWidget.cs:120-121` (Ver `AUD-003` y `AUD-004`). |
| **Fase 12 (ADR-020)** | `TimingUiCoordinator` único compartido, múltiples temporizadores con persistencia en `timers.json`, Pomodoro y vueltas de cronómetro. | **Confirmado (con defectos en `TimerCollection` y `TimerPersistenceService`)** | `TimingUiCoordinator.cs:67-121`<br>`TimerCollection.cs:220-242,286-339`<br>`TimerPersistenceService.cs:96-169` (Ver `AUD-016` y `AUD-017`). |
| **Fase 13 (ADR-021)** | `DominantColorExtractor` sobre miniatura `32x32` fuera del hilo UI con caché LRU y pinceles congelados; gestos `WM_MOUSEHWHEEL` y drag con cooldown de `400 ms`. | **Confirmado (con log de `trackKey` en `MediaColorService`)** | `DominantColorExtractor.cs:25-165`<br>`MediaColorService.cs:41-98`<br>`SwipeGestureDetector.cs:20-85` (Ver `AUD-004`). |
| **Fase 14 (ADR-022)** | `ClipboardService` estrictamente *opt-in*, solo en RAM, con `AddClipboardFormatListener`, exclusión de gestores de contraseñas y purga al bloquear sesión o suspender. | **Confirmado** | `ClipboardService.cs:63-130,158-281,374-436`<br>`ClipboardHistory.cs:25-160`. |
| **Fase 15 (ADR-023)** | `PrivacyAccessMonitor` pasivo (`RegNotifyChangeKeyValue` sobre `ConsentStore` de micrófono y cámara, cero polling). | **Confirmado (con arranque incondicional y log de `AppName` en `PrivacyWidget`)** | `PrivacyAccessMonitor.cs:62-154`<br>`App.xaml.cs:124-125`<br>`PrivacyWidget.cs:186-187` (Ver `AUD-004` y `AUD-006`). |
| **Fase 16 (ADR-024)** | `SpectrumAnalyzer` FFT Radix-2 (`1024` muestras, ventana Hann, cero asignaciones por frame) + `AudioSpectrumService` (`WasapiLoopbackCapture` con doble buffer y degradación a `Simulated`). | **Confirmado (con renderizado duplicado en `MediaCompactView`/`MediaExpandedView` y `IsFullscreenSuppressed: false`)** | `SpectrumAnalyzer.cs:48-168`<br>`AudioSpectrumService.cs:38-183`<br>`MediaWidget.cs:773`<br>`MediaCompactView.xaml.cs:70-88` (Ver `AUD-007`). |
| **Fases 17 y 18 (ADR-027)** | Retiro completo de integración con Antigravity (`OpenDynamic.Hook`, servidor Named Pipe, widget y reglas) y limpieza de `approval-rules.json`. | **Confirmado al 100 %** | `SettingsService.cs:371-427`<br>`0` coincidencias en `src/` y `tests/`. |
| **Fase 19 (ADR-028)** | `AmbientClockWidget` (`Priority 5`, `ActivationMode.OnHover`, sincronización al borde de minuto/segundo y `WM_TIMECHANGE`). | **Confirmado (con regresión por `_restingHoverWatcherTimer` y revelación inicial cancelada a los 9 ms)** | `AmbientClockWidget.cs:101,172-273`<br>`App.xaml.cs:188-205` (Ver `AUD-001` y `AUD-021`). |
| **Fase 20 (ADR-029)** | `EnergySaverService` (WNF + `WM_POWERBROADCAST`) y `ResourceProfilePolicy`. | **Confirmado (con cooldown de `500 ms` en lugar de `5 s`)** | `EnergySaverService.cs:76,106-156`<br>`ResourceProfilePolicy.cs:15-60` (Ver `AUD-020B`). |
| **Fase 21 (ADR-030)** | `ScreenshotWatcherService` (`FileSystemWatcher` + `FileStabilityPolicy`), decodificación `MemoryStream` sin bloquear archivo, Drag & Drop `Copy` y envío a Papelera con doble confirmación. | **Confirmado** | `ScreenshotWatcherService.cs:294-415`<br>`ScreenshotWidget.cs:405-554`<br>`NativeMethods.cs:546-594`. |

---

## 5. Matriz Completa de Hallazgos

### Resumen Cuantitativo por Severidad

| Severidad | Cantidad | IDs de Hallazgos |
|---|---|---|
| **CRÍTICO (Bloqueante de release)** | **4** | `AUD-001`, `AUD-002`, `AUD-003`, `AUD-004` |
| **ALTO** | **9** | `AUD-005`, `AUD-006`, `AUD-007`, `AUD-008`, `AUD-009`, `AUD-010`, `AUD-011`, `AUD-012`, `AUD-013` |
| **MEDIO** | **11** | `AUD-014`, `AUD-015`, `AUD-016`, `AUD-017`, `AUD-018`, `AUD-019`, `AUD-020`, `AUD-021`, `AUD-022`, `AUD-023`, `AUD-024` |
| **BAJO / INFO** | **6** | `AUD-025`, `AUD-026`, `AUD-027`, `AUD-028`, `AUD-029`, `AUD-030` |
| **TOTAL** | **30** | `AUD-001` – `AUD-030` |

---

### 5.1 Hallazgos CRÍTICOS (Bloqueantes de Release)

#### `AUD-001` — Timer de sondeo continuo a 10 Hz (`100 ms`) activo en estado `Hidden` (Reposo)
- **Severidad:** CRÍTICO
- **Categoría:** Rendimiento / Regla de Oro 1
- **Ubicación:** `src/OpenDynamic.App/Windowing/IslandWindow.xaml.cs:37, 101-106, 118-122, 163-166, 403-406, 608-618, 687-695, 1166-1178`
- **Descripción técnica:** `IslandWindow` instancia `_restingHoverWatcherTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(100) }` y lo inicia en el constructor (`línea 106`) y cada vez que la isla entra o se asienta en `IslandState.Hidden` (`líneas 165 y 405`). En `OnRestingHoverWatcherTick` (`líneas 1166-1178`), el timer ejecuta `IsPhysicalCursorOverInteractiveZone()` (`GetCursorPos` + `PointFromScreen`) 10 veces por segundo de manera indefinida mientras la isla está oculta. Además, no se detiene en `PBT_APMSUSPEND` (`líneas 608-618`), ni en `OnFullscreenChanged(true)` (`líneas 749-754`), ni cuando `EnableAmbientClock == false`.
- **Impacto:** Viola directamente la Regla de Oro 1 (*"~0 % CPU en reposo; ningún DispatcherTimer, Timer ni bucle de renderizado puede quedar activo cuando la isla está en estado Hidden"*). Impide que el hilo UI de WPF entre en reposo profundo durante sesiones de inactividad o suspensión moderna.
- **Propuesta de corrección:** Eliminar `_restingHoverWatcherTimer` o restringirlo exclusivamente a detección basada en eventos de entrada de ratón de WPF (`MouseEnter`/`MouseMove`/`WM_NCHITTEST`/`WM_SETCURSOR` sobre la zona sensor de la muesca cuando `EnableAmbientClock == true` y `!IsFullscreenSuppressed`), garantizando que en estado `Hidden` existan **cero** timers activos.

#### `AUD-002` — Cinco ajustes `Enable*Widget` de `AppSettings` son ignorados en tiempo de ejecución
- **Severidad:** CRÍTICO
- **Categoría:** Funcionalidad / Configuración (`A3`)
- **Ubicación:**
  - `src/OpenDynamic.App/Widgets/Media/MediaWidget.cs:383-442` y `src/OpenDynamic.App/App.xaml.cs:74-78` (`EnableMediaWidget`)
  - `src/OpenDynamic.App/Widgets/Volume/VolumeWidget.cs:125-148` (`EnableVolumeWidget`)
  - `src/OpenDynamic.App/Widgets/Battery/BatteryWidget.cs:114-142` (`EnableBatteryWidget`)
  - `src/OpenDynamic.App/Widgets/Timer/TimerWidget.cs:200-260` y `src/OpenDynamic.App/Widgets/Stopwatch/StopwatchWidget.cs:75-120` (`EnableTimerWidget`, `EnableStopwatchWidget`)
- **Descripción técnica:** Los interruptores `EnableMediaWidget`, `EnableVolumeWidget`, `EnableBatteryWidget`, `EnableTimerWidget` y `EnableStopwatchWidget` se muestran en `SettingsWindow` (`SettingsViewModel.cs:102, 159, 169, 241, 267`) y se guardan en `settings.json`, pero:
  1. `EnableMediaWidget` no se comprueba en ningún lugar de `MediaWidget` ni `MediaService`.
  2. `EnableVolumeWidget` solo se comprueba al girar la rueda del ratón sobre la cápsula (`IslandWindow.xaml.cs:1089`), pero **no** en `VolumeWidget.OnVolumeChanged` (`VolumeWidget.cs:125`), por lo que cambiar el volumen del sistema sigue mostrando la píldora de volumen aunque el usuario haya desactivado el widget.
  3. `EnableBatteryWidget` no se comprueba en `BatteryWidget.OnAlertTriggered` (`BatteryWidget.cs:116`) ni en `PowerService`.
  4. `EnableTimerWidget` y `EnableStopwatchWidget` no se comprueban en `TimerWidget`, `StopwatchWidget`, `TrayIconManager` ni `SettingsWindow`.
- **Impacto:** El usuario desactiva cualquiera de estos 5 widgets en la ventana de Configuración y los widgets continúan activándose y mostrando alertas en la isla.
- **Propuesta de corrección:** Verificar el flag ` _settings.Enable*Widget` correspondiente en cada widget (`MediaWidget`, `VolumeWidget`, `BatteryWidget`, `TimerWidget`, `StopwatchWidget`) y reaccionar en caliente cuando el usuario cambie el ajuste en `SettingsViewModel` (desactivando inmediatamente `IsActive = false` y deteniendo los servicios subyacentes si no son requeridos por otro componente).

#### `AUD-003` — `DeviceService.StartBluetoothWatcher()` falla siempre en Windows 11 (`COMException 0x8002802B`) y provoca 3 alertas falsas de conexión de audio en cada arranque
- **Severidad:** CRÍTICO
- **Categoría:** Robustez WinRT / Dispositivos (`A2`, `A5`)
- **Ubicación:** `src/OpenDynamic.App/Services/DeviceService.cs:394-434, 468-515`
- **Descripción técnica:**
  1. En `DeviceService.StartBluetoothWatcher()` (`líneas 400-408`), se invoca `DeviceInformation.CreateWatcher(aqs, new[] { "System.Devices.Aep.IsConnected", "System.Devices.Aep.Bluetooth.Cod.MajorDeviceClass", "System.Devices.BatteryLevel" }, DeviceInformationKind.AssociationEndpoint)`. En Windows 11, `"System.Devices.Aep.Bluetooth.Cod.MajorDeviceClass"` y `"System.Devices.BatteryLevel"` no son nombres canónicos válidos del Property System para AEP, por lo que WinRT lanza siempre `COMException (0x8002802B): Property key syntax error`.
  2. En el bloque `catch` (`línea 432`), `StartBluetoothWatcher()` llama inmediatamente a `_policy.NotifyEnumerationCompleted()` **antes** de que `StartAudioRenderWatcher()` (`línea 101`) inicie y complete la enumeración inicial de los endpoints de audio del sistema.
  3. Cuando `StartAudioRenderWatcher()` enumera los dispositivos de audio ya presentes en el equipo 9 ms después (`OnAudioRenderDeviceAdded`), `_policy.IsEnumerationCompleted` ya es `true`, por lo que `DeviceService` emite alertas transitorias falsas de `"Audífonos / Audio Conectado"` en cada inicio de la aplicación.
- **Impacto:** El monitoreo de dispositivos Bluetooth está completamente inoperativo en Windows 11 y el usuario recibe alertas fantasma de conexión de audio cada vez que abre `openDynamic`.
- **Propuesta de corrección:** Usar únicamente propiedades canónicas válidas en `CreateWatcher` (p. ej. `"System.Devices.Aep.IsConnected"`, `"System.Devices.Aep.Category"`, `"System.Devices.Aep.DeviceAddress"`) o formato `{fmtid} pid` válido, y coordinar `_policy.NotifyEnumerationCompleted()` para que solo se marque cuando **ambos** watchers (`Bluetooth` y `AudioRender`) hayan disparado `EnumerationCompleted` (o tras el timeout de seguridad de 5 s).

#### `AUD-004` — Fuga de información privada del usuario en archivos de log y nivel `Debug` activo en `Release`
- **Severidad:** CRÍTICO
- **Categoría:** Seguridad y Privacidad / Regla de Oro 10 (`A4`)
- **Ubicación:**
  - `src/OpenDynamic.App/Infrastructure/LoggingConfiguration.cs:21, 31`
  - `src/OpenDynamic.App/Widgets/Network/NetworkWidget.cs:120-121, 137-138`
  - `src/OpenDynamic.App/Widgets/Privacy/PrivacyWidget.cs:155, 161, 168, 174, 186-187`
  - `src/OpenDynamic.App/Services/MediaColorService.cs:83, 89, 94` y `src/OpenDynamic.App/Widgets/Media/MediaWidget.cs:476`
  - `src/OpenDynamic.App/Services/VolumeService.cs:141-142, 218-219, 336, 359, 373, 379` y `src/OpenDynamic.App/Widgets/Volume/VolumeWidget.cs:145-146`
  - `src/OpenDynamic.App/Widgets/Timer/TimerWidget.cs:383-384, 510` y `src/OpenDynamic.App/ViewModels/SettingsViewModel.cs:1463`
  - `src/OpenDynamic.App/Infrastructure/TrayIconManager.cs:308`, `src/OpenDynamic.App/ServiceCollectionExtensions.cs:59-60`, `src/OpenDynamic.App/Services/AutostartService.cs:43-44`
- **Descripción técnica:**
  1. `LoggingConfiguration.cs:21` configura `.MinimumLevel.Debug()` incondicionalmente tanto en `Debug` como en `Release`.
  2. `NetworkWidget.cs:120-121` registra `Title` (`"Conectado a {SSID}"`) a nivel `Information`, contradiciendo `NetworkService.cs:111` (*"Never log the network name (SSID) to preserve user privacy"*).
  3. `PrivacyWidget.cs:186-187` registra `Title` y `Subtitle` que incluyen `alert.AppName` (nombre de la aplicación usando micrófono o cámara) a nivel `Information`, contradiciendo `PrivacyAccessMonitor.cs:204` (*"NEVER log app names or user paths"*).
  4. `MediaColorService.cs:83, 89, 94` registra `trackKey` (`"{TrackTitle}|{TrackArtist}"`).
  5. `VolumeService.cs` y `VolumeWidget.cs` registran el nombre amigable del dispositivo de audio (`_currentDevice.FriendlyName`, que suele incluir el nombre del usuario, p. ej. `"AirPods de ..."`) y los `DeviceId` completos.
  6. `TimerWidget.cs` y `SettingsViewModel.cs` registran etiquetas personalizadas de temporizadores del usuario (`alert.Label`).
  7. `LoggingConfiguration.cs:31`, `TrayIconManager.cs:308`, `ServiceCollectionExtensions.cs:59-60` y `AutostartService.cs:43-44` registran rutas absolutas en disco que contienen el nombre de usuario del sistema operativo (`C:\Users\<usuario>\...`).
- **Impacto:** Incumple la Regla de Oro 10 y la Sección A4.2 (*"Confirmar que ningún Log.Information, Log.Warning, Log.Debug o Log.Error interpola: texto del portapapeles, rutas completas de capturas o archivos del usuario, nombres de redes Wi-Fi / SSID, títulos de ventanas privadas o metadatos sensibles"*). Además, el nivel `Debug` en `Release` genera escritura frecuente a disco (incluyendo coordenadas de ratón en `IslandWindow.xaml.cs:861`).
- **Propuesta de corrección:**
  1. En `LoggingConfiguration.cs`, usar `.MinimumLevel.Debug()` bajo `#if DEBUG` y `.MinimumLevel.Information()` en `#else` (`Release`).
  2. Eliminar la interpolación de `Title`/`Subtitle` con SSID en `NetworkWidget.cs`, de `AppName` en `PrivacyWidget.cs`, de `trackKey` en `MediaColorService.cs`, de `FriendlyName`/`DeviceId` sin sanitizar en `VolumeService.cs`/`VolumeWidget.cs`, de `Label` en `TimerWidget.cs`/`SettingsViewModel.cs`, y reemplazar rutas de usuario en logs por identificadores simbólicos (`%LocalAppData%`, `%AppData%`).

---

### 5.2 Hallazgos ALTOS

#### `AUD-005` — `RestingSensorNotch` (`240x44 DIP`) intercepta clics en ventanas maximizadas y el hover reactiva la isla encima de aplicaciones en pantalla completa
- **Severidad:** ALTO
- **Categoría:** Ventana / Pantalla Completa / Regla de Oro 6 (`A2`, `A3`)
- **Ubicación:** `src/OpenDynamic.App/Windowing/IslandWindow.xaml:18-25`, `src/OpenDynamic.App/Windowing/IslandWindow.xaml.cs:277-278, 440-452, 749-754, 891-898`, `src/OpenDynamic.App/Orchestration/IslandOrchestrator.cs:456, 725-732`
- **Descripción técnica:**
  1. `RestingSensorNotch` tiene un tamaño de `240x44 DIP` (`IslandWindow.xaml:19-20`) con `Background="#01000000"` y `IsHitTestVisible="True"`. En estado `Hidden`, `WM_NCHITTEST` (`IslandWindow.xaml.cs:440-452`) devuelve `HTCLIENT` sobre toda esa caja de `240x44 DIP` en el borde superior central del monitor, impidiendo hacer clic en pestañas del navegador o barras de título de ventanas maximizadas en esa región (contradiciendo ADR-007 y ADR-017 que especificaban un sensor mínimo de `80x4 DIP` pegado al bisel superior).
  2. En `IslandOrchestrator.UpdateOrchestration()` (`línea 456`), la condición `if (_isFullscreenSuppressed && !_isHovering) return;` permite que `_isHovering == true` ignore la supresión de pantalla completa. Como `RestingSensorNotch` permanece visible y activo durante pantalla completa (`OnFullscreenChanged` en `IslandWindow.xaml.cs:749-754` solo oculta `IslandHostView`), acercar el cursor al borde superior central durante un juego o vídeo en pantalla completa activa el hover y despliega la isla encima de la aplicación fullscreen.
- **Impacto:** Bloqueo de clics en la franja superior de `240x44 DIP` en ventanas maximizadas e intrusión visual sobre juegos/vídeos en pantalla completa.
- **Propuesta de corrección:**
  1. Reducir `RestingSensorNotch` a una franja mínima pegada al borde superior (`Width="120" Height="4"` o `80x4 DIP`) y devolver `HTTRANSPARENT` en `WM_NCHITTEST` cuando `_orchestrator.IsFullscreenSuppressed` sea `true` o cuando `EnableAmbientClock == false` en estado `Hidden`.
  2. En `IslandOrchestrator.cs:456` y `SetHovering`, bloquear cualquier activación por hover mientras `_isFullscreenSuppressed` sea `true`.

#### `AUD-006` — Hook global `EVENT_OBJECT_LOCATIONCHANGE` sin filtro de proceso ni *debounce* en `FullscreenWatcher`, desalineación multimonitor y servicios iniciados aunque estén deshabilitados
- **Severidad:** ALTO
- **Categoría:** Rendimiento / Win32 Hooks (`A2`, `A5`)
- **Ubicación:** `src/OpenDynamic.App/Windowing/FullscreenWatcher.cs:70-77, 156-234`, `src/OpenDynamic.App/App.xaml.cs:124-125`, `src/OpenDynamic.App/Windowing/IslandWindow.xaml.cs:210-245`
- **Descripción técnica:**
  1. `FullscreenWatcher` instala `SetWinEventHook` para `EVENT_OBJECT_LOCATIONCHANGE` (`0x800B`) con `idProcess = 0, idThread = 0` (`líneas 70-77`). En Windows, este evento se dispara globalmente ante movimientos de cursor, caret de texto y controles hijos en cualquier aplicación, enviando mensajes al hilo UI de WPF. Además, al arrastrar o redimensionar la ventana activa, `EvaluateFullscreenState` ejecuta 7 llamadas P/Invoke síncronas por cada píxel de movimiento sin *debounce*.
  2. `FullscreenWatcher.EvaluateFullscreenState` (`líneas 216-231`) comprueba si la ventana en primer plano cubre su propio monitor (`MonitorFromWindow(foregroundHwnd)`), sin verificar si ese monitor coincide con el monitor donde está posicionada `IslandWindow`. En configuraciones multimonitor, un vídeo a pantalla completa en el monitor secundario oculta la isla en el monitor primario.
  3. `PrivacyAccessMonitor.Start()` (`App.xaml.cs:125`), `FullscreenWatcher.Start()` (`IslandWindow.xaml.cs:245`) y `EnergySaverService` se inician en el arranque aunque sus respectivos ajustes en `AppSettings` estén deshabilitados.
- **Impacto:** Tráfico innecesario en la cola de mensajes del hilo UI, ocultación errónea de la isla en configuraciones multimonitor y recursos nativos (hilo `PrivacyConsentStoreWatcher`, hooks WinEvent) abiertos cuando las funciones están desactivadas.
- **Propuesta de corrección:**
  1. En `FullscreenWatcher`, filtrar `idObject == OBJID_WINDOW && idChild == 0` antes de cualquier lógica, añadir un *debounce* ligero (~50–100 ms) para `EVENT_OBJECT_LOCATIONCHANGE` y comparar el `HMONITOR` de la ventana en primer plano con el `HMONITOR` de `IslandWindow`.
  2. Condicionar el inicio de `FullscreenWatcher`, `PrivacyAccessMonitor` y demás servicios en el arranque al estado de sus ajustes en `AppSettings`.

#### `AUD-007` — Suscripción simultánea a `CompositionTarget.Rendering` en `MediaCompactView` y `MediaExpandedView`, y captura WASAPI activa durante supresión por pantalla completa
- **Severidad:** ALTO
- **Categoría:** Rendimiento / Visualizador de Audio (`A2`, `A5`)
- **Ubicación:** `src/OpenDynamic.App/Widgets/Media/Views/MediaCompactView.xaml.cs:61-88`, `src/OpenDynamic.App/Widgets/Media/Views/MediaExpandedView.xaml.cs:85-112`, `src/OpenDynamic.App/Widgets/Media/MediaWidget.cs:754-778`
- **Descripción técnica:**
  1. `MediaCompactView.UpdateRenderingSubscription()` (`línea 72`) y `MediaExpandedView.UpdateRenderingSubscription()` (`línea 96`) solo comprueban `if (_widget.IsVisualizerActive && IsLoaded)` sin verificar `_widget.DisplayMode` ni `IsVisible`. Cuando el usuario expande la vista multimedia una vez, ambas vistas quedan cargadas o cacheadas y no se reevalúan ante cambios de `DisplayMode`.
  2. En `MediaWidget.UpdateVisualizerState()` (`línea 773`), se pasa `IsFullscreenSuppressed: false` de forma fija (`hardcoded`) a `VisualizerActivationContext`. Cuando una aplicación en pantalla completa oculta la isla mientras suena música, `AudioSpectrumService` (`WasapiLoopbackCapture` + FFT) y el bucle `CompositionTarget.Rendering` continúan ejecutándose a 30 FPS en segundo plano.
- **Impacto:** Consumo innecesario de CPU/GPU y captura WASAPI activa mientras la isla está suprimida por pantalla completa.
- **Propuesta de corrección:**
  1. En `MediaCompactView`, requerir `_widget.DisplayMode == WidgetDisplayMode.Compact && IsVisible`; en `MediaExpandedView`, requerir `_widget.DisplayMode == WidgetDisplayMode.Expanded && IsVisible`, y escuchar `nameof(MediaWidget.DisplayMode)` en `OnWidgetPropertyChanged`.
  2. Propagar el estado real `IsFullscreenSuppressed` desde `IslandOrchestrator` hacia `MediaWidget.UpdateVisualizerState()`.

#### `AUD-008` — Inicialización y muestreo de 118 contadores PDH `GPU Engine` en el hilo UI de WPF bloquea el arranque durante `1.5 s` y retiene `+168` handles
- **Severidad:** ALTO
- **Categoría:** Rendimiento / Hilo UI (`A2`, `A5`)
- **Ubicación:** `src/OpenDynamic.App/Services/HardwareService.cs:105-182`, `src/OpenDynamic.App/Widgets/Hardware/HardwareWidget.cs:160-176, 278-293`
- **Descripción técnica:** Medido empíricamente en `Release`: cuando `EnableGpuMonitoring = true`, `HardwareWidget.Initialize()` llama a `SampleNow()` síncronamente en el hilo UI durante `App.OnStartup`. `HardwareService.InitializeGpuCounters()` enumera todas las instancias de `"GPU Engine"` (`engtype_3D`, una por cada proceso del sistema, **118 instancias** en el equipo de prueba) e instancia 118 objetos `PerformanceCounter` en el hilo UI, bloqueando el arranque durante **`1,507 ms`** (`16:22:27.495` → `16:22:29.002`), incrementando los handles del proceso de `863` a `1,031` (`+168` handles) y la memoria privada de `74.09 MB` a `87.27 MB`. Además:
  - Cada 2 segundos, `OnSampleTimerTick` invoca `counter.NextValue()` sobre los 118 contadores síncronamente en el hilo UI de WPF.
  - Cuando el usuario desactiva `EnableGpuMonitoring = false` o `DisableMonitoring()`, los 118 `PerformanceCounter` nunca se liberan (`Dispose()` solo se llama al cerrar la app).
- **Impacto:** Micro-tirones (stuttering) en las animaciones de 60 FPS de la isla cada 2 segundos, bloqueo de 1.5 s al activar GPU y retención permanente de ~170 handles nativos.
- **Propuesta de corrección:** Ejecutar `InitializeGpuCounters()` y el muestreo de `SampleGpuUsageSafe()` fuera del hilo UI (en `Task.Run` con guardia de no solapamiento), y liberar (`Dispose()`) inmediatamente `_gpuCounters` cuando `EnableGpuMonitoring` pase a `false` o el widget de hardware se desactive.

#### `AUD-009` — Ausencia de validación de rangos en `SettingsService.Load()` y omisión de migraciones cuando falta `"SchemaVersion"` en `settings.json`
- **Severidad:** ALTO
- **Categoría:** Configuración y Migración (`A3`)
- **Ubicación:** `src/OpenDynamic.Core/Settings/AppSettings.cs:33, 646, 664, 676`, `src/OpenDynamic.Core/Settings/SettingsService.cs:94-267`
- **Descripción técnica:**
  1. `AppSettings.SchemaVersion` tiene el inicializador `= CurrentSchemaVersion;` (`14` en `AppSettings.cs:33`). Si se deserializa un `settings.json` parcial o artesanal que no incluya la propiedad `"SchemaVersion"`, `System.Text.Json` conserva el valor `14` y `SettingsService.Load()` salta todos los bloques de migración (`if (loaded.SchemaVersion < 14)`).
  2. Cuando `SchemaVersion == 14`, `SettingsService.Load()` no aplica ningún *clamping* ni validación de rangos:
     - Si `BatteryCriticalThresholdPercent >= BatteryLowThresholdPercent` (p. ej. `25 >= 20`), `BatteryThresholdTracker` (`BatteryThresholdTracker.cs:31-34`) lanza `ArgumentException` no controlada al iniciar `PowerService`.
     - Dimensiones negativas, cero, `NaN` o extremas (`CapsuleWidth`, `CapsuleHeight`, `CapsuleCornerRadius`, `ScaleFactor`), duraciones negativas o capacidades `<= 0` se aceptan sin sanitizar.
     - Si `"TimerPresetsMinutes": null` en un archivo v14, `AppSettings.Clone()` (`línea 646`) lanza `ArgumentNullException`.
  3. Si un enum en JSON contiene un texto inválido (p. ej. `"MotionMode": "Invalid"`), `JsonSerializer.Deserialize` falla por completo y sobrescribe todo el `settings.json` del usuario con valores por defecto.
- **Impacto:** Riesgo de excepción en el arranque ante ediciones manuales de `settings.json` o corrupción de propiedades individuales.
- **Propuesta de corrección:** Añadir un método `SanitizeAndClamp()` en `AppSettings` / `SettingsService.Load()` que se ejecute siempre tras la deserialización y migración (validando rangos de geometría, duraciones, umbrales de batería `Critical < Low`, listas no nulas y hotkeys válidas), y proteger `AppSettings.Clone()` con `?? Enumerable.Empty<...>()`.

#### `AUD-010` — Condición de carrera en `DeviceAlertPolicy` al coalescer eventos concurrentes del mismo dispositivo
- **Severidad:** ALTO
- **Categoría:** Concurrencia / Core (`A2`)
- **Ubicación:** `src/OpenDynamic.Core/Devices/DeviceAlertPolicy.cs:19, 195-208, 242-265, 312`
- **Descripción técnica:** En `ProcessDeviceEvent` (`líneas 195-208`), cuando llega un segundo evento para el mismo `DeviceId` justo cuando el timer del primer evento (`OnCoalesceTimerElapsed`) está esperando entrar a `lock (_syncLock)` (`línea 248`), `ProcessDeviceEvent` reemplaza `_pendingCoalesce[key] = (newEvent, newTimer)`. Inmediatamente después, el callback del primer timer entra al `lock`, lee `_pendingCoalesce[deviceId]`, **destruye `newTimer`** y emite prematuramente `newEvent` sin esperar la ventana de coalescencia de `800 ms`. Además, el diccionario `_lastEmittedAlertTimes` (`línea 19`) nunca se poda.
- **Impacto:** Emisión prematura o duplicada de alertas ante ráfagas de eventos PnP/WinRT del mismo dispositivo.
- **Propuesta de corrección:** Pasar una tupla o token con un identificador monotónico de versión `(string DeviceId, long GenerationId)` al callback del timer para que `OnCoalesceTimerElapsed` ignore callbacks obsoletos si la entrada en `_pendingCoalesce` ya fue reemplazada, y acotar el tamaño de `_lastEmittedAlertTimes`.

#### `AUD-011` — Ausencia de archivos obligatorios de gobernanza Open Source (`THIRD-PARTY-NOTICES.md`, `SECURITY.md`, `CONTRIBUTING.md`, plantillas GitHub) y enlace roto en `README.md`
- **Severidad:** ALTO (Bloqueante Go/No-Go Sección 6 y A8)
- **Categoría:** Documentación / Legal / OSS (`A7`, `A8`)
- **Ubicación:** Raíz del repositorio, `README.md:46`, `CHANGELOG.md`
- **Descripción técnica:**
  1. No existe `THIRD-PARTY-NOTICES.md` en la raíz del repositorio (obligatorio en la Sección 6 Go/No-Go y requerido por la licencia Apache-2.0 de `Serilog` y `Serilog.Sinks.File` al redistribuir binarios).
  2. No existen `SECURITY.md`, `CONTRIBUTING.md`, `.github/ISSUE_TEMPLATE/bug_report.yml`, `.github/ISSUE_TEMPLATE/feature_request.yml` ni `.github/PULL_REQUEST_TEMPLATE.md` (requeridos en A8 / B5).
  3. `README.md:46` contiene un enlace relativo roto a `[docs/img/](./docs/)` (el directorio `docs/img/` no existe).
  4. `README.md` carece de una sección consolidada de **Declaración de Privacidad** y de guía ante **Windows SmartScreen**.
  5. `CHANGELOG.md` aún no incluye la entrada consolidada de `[2.0.0]`.
- **Impacto:** Bloqueante directo de la lista Go/No-Go de la Sección 6 y del cumplimiento de atribuciones de licencias de terceros.
- **Propuesta de corrección:** Crear `THIRD-PARTY-NOTICES.md`, `SECURITY.md`, `CONTRIBUTING.md`, plantillas de Issue/PR, actualizar `README.md` y `CHANGELOG.md` en la Pasada B (Sección B5).

#### `AUD-012` — Versionado disperso y desactualizado (`1.0.0`) e incumplimientos en empaquetado y workflows de Release (`setup.iss`, `release.yml`, `ci.yml`)
- **Severidad:** ALTO
- **Categoría:** Empaquetado / CI-CD (`A9`, `B6`)
- **Ubicación:** `Directory.Build.props:1-10`, `src/OpenDynamic.App/app.manifest:3`, `installer/setup.iss:8, 22`, `.github/workflows/release.yml:1-95`, `.github/workflows/ci.yml:1-45`
- **Descripción técnica:**
  1. `Directory.Build.props` no define `Version`, `AssemblyVersion`, `FileVersion` ni `InformationalVersion` (`2.0.0`).
  2. `src/OpenDynamic.App/app.manifest:3` y `installer/setup.iss:8` siguen teniendo `1.0.0.0` / `1.0.0`.
  3. `installer/setup.iss` no declara `LicenseFile=..\LICENSE`.
  4. `publish/` incluye archivos de símbolos de depuración `OpenDynamic.App.pdb` y `OpenDynamic.Core.pdb` (prohibidos en artefactos de release según B6.5).
  5. `.github/workflows/release.yml` no incluye `README.md`, `LICENSE` ni `THIRD-PARTY-NOTICES.md` dentro del `.zip` portable, no genera `SHA256SUMS.txt` y no configura `draft: true` en `softprops/action-gh-release@v2`.
  6. `.github/workflows/ci.yml` no declara permisos mínimos `permissions: contents: read`.
- **Impacto:** Los binarios e instaladores generados muestran versión `1.0.0`, carecen de textos legales y hashes SHA-256 verificables, e incluyen símbolos `.pdb`.
- **Propuesta de corrección:** Centralizar versión `2.0.0` en `Directory.Build.props`, sincronizar `app.manifest` y `setup.iss` (añadiendo `LicenseFile=..\LICENSE`), excluir `.pdb` del paquete final y actualizar `ci.yml` y `release.yml` según B6.

#### `AUD-013` — `IslandWindow.Close()` no se invoca explícitamente en `App.OnExit` y `WorkingSet64` en reposo (`160.5 MB`) supera el umbral nominal de `80 MB`
- **Severidad:** ALTO
- **Categoría:** Ciclo de Vida / Memoria (`A2`, `A5`)
- **Ubicación:** `src/OpenDynamic.App/App.xaml.cs:26, 273-305`, `src/OpenDynamic.App/Windowing/IslandWindow.xaml.cs:1233-1297`
- **Descripción técnica:**
  1. `App.xaml.cs:26` establece `ShutdownMode = ShutdownMode.OnExplicitShutdown`. Como `IslandWindow` no implementa `IDisposable`, `disposableServices.Dispose()` (`App.xaml.cs:292`) no cierra `IslandWindow`, y `App.OnExit` nunca llama a `islandWindow.Close()` (a diferencia de `settingsWindow?.ForceClose()` en la línea 288). En consecuencia, la desregistración nativa ubicada dentro de `IslandWindow.OnClosed` (`UnregisterPowerSettingNotification`, `SystemEvents.PowerModeChanged -= ...`, `_hwndSource.RemoveHook(WndProc)`) no se ejecuta de forma determinista antes de concluir `OnExit`.
  2. En las mediciones de 5 minutos en reposo, `PrivateMemorySize64` es de **`71.04 MB`** (cumpliendo `< 80 MB`), pero `WorkingSet64` es de **`160.51 MB`** debido a las páginas compartidas mapeadas por WPF DirectX/D3D9, `ReadyToRun` y WinRT/COM.
- **Impacto:** Limpieza incompleta de hooks Win32 de `IslandWindow` al salir y exceso de `WorkingSet64` respecto al objetivo nominal de `< 80 MB`.
- **Propuesta de corrección:** Invocar explícitamente `Services?.GetService<Windowing.IslandWindow>()?.Close()` al inicio de `App.OnExit`, y eliminar referencias innecesarias (`NAudio` metapaquete) para reducir la huella de ensamblados mapeados.

---

### 5.3 Hallazgos MEDIOS

#### `AUD-014` — Referencia al metapaquete `NAudio` incluye `NAudio.WinForms.dll` y otros 4 ensamblados innecesarios en `publish/`
- **Severidad:** MEDIO
- **Categoría:** Dependencias / Regla de Oro 3 (`A1`, `A9`)
- **Ubicación:** `src/OpenDynamic.App/OpenDynamic.App.csproj:18`
- **Descripción técnica:** `OpenDynamic.App.csproj` referencia `<PackageReference Include="NAudio" Version="2.3.0" />`. `AudioSpectrumService` únicamente utiliza `NAudio.CoreAudioApi` y `NAudio.Wave` (`WasapiLoopbackCapture`), que residen en `NAudio.Wasapi` y `NAudio.Core`. El metapaquete copia en `publish/` `NAudio.WinForms.dll` (`80 KB`), `NAudio.Asio.dll`, `NAudio.Midi.dll`, `NAudio.WinMM.dll` y `NAudio.dll` (`~765 KB` en total).
- **Impacto:** Contradice la Regla de Oro 3 (cero ensamblados WinForms en la distribución) e infla innecesariamente el paquete publicado.
- **Propuesta de corrección:** Reemplazar la referencia `NAudio` en `OpenDynamic.App.csproj` por `<PackageReference Include="NAudio.Wasapi" Version="2.3.0" />`.

#### `AUD-015` — Fallo de `TrayIconManager.Initialize()` (`ForceCreate`) tras cierre abrupto y fugas de handles nativos (`HICON` y `Process`)
- **Severidad:** MEDIO
- **Categoría:** Robustez / Handles Nativos (`A2`, `A5`)
- **Ubicación:** `src/OpenDynamic.App/Infrastructure/TrayIconManager.cs:63-90, 357-358, 368-398`, `src/OpenDynamic.App/Services/MediaService.cs:228-246`
- **Descripción técnica:**
  1. Verificado empíricamente en los logs de `Release`: si una instancia previa de `OpenDynamic.App.exe` termina abruptamente (sin ejecutar `Shell_NotifyIcon(NIM_DELETE)`), al iniciar una nueva instancia `_taskbarIcon.ForceCreate()` (`TrayIconManager.cs:82`) lanza `InvalidOperationException: TryCreate failed` porque el GUID determinista del icono sigue registrado en el área de notificación del shell, dejando la aplicación sin icono en la bandeja hasta que `explorer.exe` se reinicie.
  2. En `TrayIconManager.CreateFallbackIcon()` (`líneas 357-358`), `bmp.GetHicon()` + `Icon.FromHandle(hIcon)` nunca libera el handle GDI `HICON` mediante `NativeMethods.DestroyIcon`, y `TrayIconManager.Dispose()` no dispone el objeto `System.Drawing.Icon`.
  3. En `MediaService.TryActivateApp()` (`líneas 228-246`), `Process.GetProcessesByName(candidate)` devuelve un arreglo de objetos `Process` con handles nativos abiertos que nunca son liberados con `.Dispose()`.
- **Impacto:** Pérdida del icono de bandeja tras un reinicio forzoso y fuga de handles GDI/OS en rutas de fallback y activación de reproductor.
- **Propuesta de corrección:** En `TrayIconManager.Initialize()`, si `ForceCreate()` lanza `InvalidOperationException`, intentar recrear/reasignar un nuevo `TaskbarIcon` con un `Guid` fresco o invocar `Recreate()`; disponer el `Icon` y liberar `hIcon` con `DestroyIcon`; y disponer todos los elementos de `Process.GetProcessesByName` en `MediaService.cs`.

#### `AUD-016` — `TimerCollection` recrea `_backgroundTimer` `N+1` veces por tick, ignora las duraciones Pomodoro en timers adicionales y `ITimerCollection` no extiende `IDisposable`
- **Severidad:** MEDIO
- **Categoría:** Lógica de Dominio / Timer (`A2`)
- **Ubicación:** `src/OpenDynamic.Core/Timer/ITimerCollection.cs:8`, `src/OpenDynamic.Core/Timer/TimerCollection.cs:101-145, 183, 220-242, 286-339`
- **Descripción técnica:**
  1. En `TimerCollection.UpdateTick()` (`líneas 220-242`), cada `timer.UpdateTick()` dispara `OnTimerIndividualTick` (`línea 286`), que llama a `ScheduleNextCompletion()` (destruyendo y recreando `_backgroundTimer`) y emite `Tick`. Al terminar el bucle, `UpdateTick()` vuelve a llamar a `ScheduleNextCompletion()` y emite `Tick` otra vez (`N+1` recreaciones de timer y eventos por cada tick de UI).
  2. `TimerCollection` recibe `defaultStandardDuration`, `pomodoroWorkDuration` y `pomodoroBreakDuration` en su constructor (`líneas 103-105`), pero solo los pasa al primer timer y no los guarda en campos; llamadas posteriores a `AddTimer()` (`línea 132`) o `CreateDefaultTimer()` (`línea 335`) usan valores fijos (`10m`/`25m`/`5m`), ignorando los ajustes del usuario.
  3. `ITimerCollection` no hereda de `IDisposable`, y `AddTimer`/`RemoveTimer` invocan el evento `TimersChanged` dentro de `lock (_lock)`.
- **Impacto:** Recreación excesiva de timers en cada frame de UI con temporizadores activos y pérdida de la configuración Pomodoro del usuario al añadir un segundo temporizador.
- **Propuesta de corrección:** Almacenar las duraciones por defecto en campos de `TimerCollection`, suprimir la re-ejecución de `OnTimerIndividualTick` durante el bucle de `UpdateTick()`, hacer que `ITimerCollection : IDisposable` e invocar `TimersChanged` fuera del `lock`.

#### `AUD-017` — Defectos en `TimerController` (`AddTime` en pausa/detenido, `RemainingTime` estático) y en `TimerPersistenceService` (borrado prematuro de `timers.json` y escritura no atómica)
- **Severidad:** MEDIO
- **Categoría:** Lógica de Dominio / Persistencia (`A2`)
- **Ubicación:** `src/OpenDynamic.Core/Timer/TimerController.cs:33-36, 74-77, 167-191`, `src/OpenDynamic.Core/Timer/TimerPersistenceService.cs:96, 112, 145-169`
- **Descripción técnica:**
  1. En `TimerController.AddTime()` (`línea 186`), cuando el timer está en `Paused`, sobrescribe `_totalDuration = _remainingTime`, reseteando `ProgressRatio` a `0.0`. Cuando está en `Stopped`, `Start()` (`línea 75`) sobrescribe `_totalDuration = GetDurationForMode(_mode)`, descartando el tiempo añadido.
  2. `TimerPersistenceService.Restore()` (`líneas 166-169`) elimina `timers.json` inmediatamente al iniciar la aplicación; si la aplicación sufre un cierre inesperado durante la sesión, todos los temporizadores restaurados se pierden.
  3. `TimerPersistenceService.Save()` (`línea 112`) usa `File.WriteAllText` directo en lugar de escritura atómica `.tmp` + `File.Move`, no llama a `t.UpdateTick()` antes de leer `RemainingTime` (`línea 96`) y no persiste `TotalDuration` (por lo que al restaurar un timer se pierde el porcentaje de progreso original).
- **Impacto:** Pérdida de progreso visual al añadir `+1 min` en pausa o restaurar sesión, y pérdida de temporizadores si el proceso termina abruptamente.
- **Propuesta de corrección:** Preservar `_totalDuration += additionalTime` en `TimerController.AddTime()`, persistir `TotalDurationSeconds` y usar escritura atómica en `TimerPersistenceService.Save()`.

#### `AUD-018` — `StopwatchController` asigna listas en el heap en cada tick (`_laps.ToList().AsReadOnly()`), no limita `_laps` y realiza doble lectura de reloj
- **Severidad:** MEDIO
- **Categoría:** Rendimiento / Regla de Oro 11 (`A2`)
- **Ubicación:** `src/OpenDynamic.Core/Stopwatch/StopwatchController.cs:12, 20, 178-191`
- **Descripción técnica:** `CreateSnapshot()` (`línea 186`) ejecuta `_laps.ToList().AsReadOnly()` en cada llamada a `UpdateTick()` (invocado a ~30 FPS por `TimingUiCoordinator`) y en cada acceso a `CurrentSnapshot`. Además, lee `ElapsedTime` (`_timeProvider.GetUtcNow()`) dos veces seguidas (`líneas 180-181`), no aplica un límite máximo de vueltas en `_laps` y expone la lista interna mutable en `public IReadOnlyList<StopwatchLap> Laps => _laps;`.
- **Impacto:** Asignaciones innecesarias en el heap a 30 FPS mientras el cronómetro está activo.
- **Propuesta de corrección:** Cachear la instancia `ReadOnlyCollection<StopwatchLap>` y regenerarla únicamente cuando se registre una vuelta (`Lap()`) o se reinicie (`Reset()`), calcular `elapsed` con una sola lectura de `_timeProvider.GetUtcNow()` y limitar la capacidad máxima de vueltas (p. ej. 1000).

#### `AUD-019` — `PowerService` no actualiza los umbrales de `BatteryThresholdTracker` en caliente y `EnergySaverService` usa un cooldown de `500 ms` en lugar de los `5 s` documentados
- **Severidad:** MEDIO
- **Categoría:** Servicios de Energía (`A2`, `A3`)
- **Ubicación:** `src/OpenDynamic.App/Services/PowerService.cs:42-44`, `src/OpenDynamic.App/Services/EnergySaverService.cs:76`
- **Descripción técnica:**
  1. `PowerService` instancia `new BatteryThresholdTracker(settings.BatteryLowThresholdPercent, settings.BatteryCriticalThresholdPercent)` una sola vez en su constructor (`líneas 42-44`). Si el usuario modifica los umbrales de batería baja o crítica en `SettingsWindow`, `PowerService` sigue usando los umbrales antiguos hasta reiniciar la aplicación.
  2. `EnergySaverService.cs:76` inicializa `new EnergySaverAlertPolicy(timeProvider, cooldownDuration: TimeSpan.FromMilliseconds(500))` (`0.5 s`), contradiciendo ADR-029 y `README.md:103` que especifican un *cooldown* antirrebote de **5 segundos** (`TimeSpan.FromSeconds(5)`).
- **Impacto:** Los cambios de umbrales de batería en Configuración no surten efecto sin reiniciar, y ráfagas de WNF separadas por >500 ms pueden emitir alertas repetidas de ahorro de energía.
- **Propuesta de corrección:** Actualizar los umbrales de `BatteryThresholdTracker` dinámicamente (o recrearlo conservando estado) cuando cambien en `AppSettings`, y usar `TimeSpan.FromSeconds(5)` (el valor por defecto de `EnergySaverAlertPolicy`) en `EnergySaverService.cs:76`.

#### `AUD-020` — `VolumeService` re-engancha el dispositivo de audio por defecto ante cambios en *cualquier* dispositivo del sistema y mantiene un `_deviceEnumerator` sin usar
- **Severidad:** MEDIO
- **Categoría:** Servicios de Audio / COM (`A2`)
- **Ubicación:** `src/OpenDynamic.App/Services/VolumeService.cs:62, 118, 190, 357-388, 461`
- **Descripción técnica:**
  1. `VolumeService.Initialize()` instancia `_deviceEnumerator = new MMDeviceEnumerator()` (`línea 118`) que nunca se utiliza porque `HookDefaultDevice_NoLock()` (`línea 190`) crea un nuevo `using var enumerator = new MMDeviceEnumerator()` en cada llamada.
  2. En `IMMNotificationClient.OnDeviceStateChanged` y `OnDeviceRemoved` (`líneas 357-388`), `VolumeService` llama a `RehookDefaultDevice()` sin comprobar si `deviceId` corresponde al dispositivo actualmente enganchado (`_currentDeviceId`), provocando liberaciones y re-enganches COM innecesarios cuando cambia el estado de micrófonos o dispositivos secundarios.
- **Impacto:** Churn innecesario de objetos COM `MMDevice` / `AudioEndpointVolume` ante eventos de dispositivos no relacionados.
- **Propuesta de corrección:** Reutilizar una única instancia de `MMDeviceEnumerator` (o eliminar el campo muerto `_deviceEnumerator`) y filtrar `OnDeviceRemoved`/`OnDeviceStateChanged` cuando `deviceId == _currentDeviceId` o `_currentDevice == null`.

#### `AUD-021` — La revelación inicial de 3 segundos del reloj ambiental en `App.OnStartup` se cancela a los `9 ms` y `TransitionContent` en `IslandView` no limpia `AnimationClock`
- **Severidad:** MEDIO
- **Categoría:** UX / Animaciones (`A2`, `A5`)
- **Ubicación:** `src/OpenDynamic.App/App.xaml.cs:188-205`, `src/OpenDynamic.App/Windowing/IslandWindow.xaml.cs:233, 381-386`, `src/OpenDynamic.App/Views/IslandView.xaml.cs:382-434`
- **Descripción técnica:**
  1. Verificado en los logs reales de `Release` (`16:12:47.509` → `16:12:47.518`): `App.OnStartup` llama a `orchestrator.SetHovering(true)` para mostrar el reloj ambiental durante 3 segundos al iniciar, pero `9 ms` después `IslandWindow` evalúa que el cursor físico no está sobre la cápsula y llama a `orchestrator.SetHovering(false)`, colapsando inmediatamente la revelación de bienvenida.
  2. En `IslandView.TransitionContent` (`líneas 405-434`), las animaciones `fadeIn` y `crossFadeIn` sobre `PrimaryContent.OpacityProperty` y `SecondaryContent.OpacityProperty` nunca llaman a `container.BeginAnimation(OpacityProperty, null)` al completarse, y si ocurren dos cambios rápidos de vista dentro de la ventana de `CrossFadeOutDurationMs`, el `Completed` de la primera transición sobrescribe el contenido con la vista obsoleta.
- **Impacto:** La animación de bienvenida de 3 segundos al arrancar nunca es visible para el usuario y las transiciones rápidas de widgets pueden sufrir parpadeos o solapamiento de callbacks `Completed`.
- **Propuesta de corrección:** Mantener una bandera temporal o actividad transitoria para la revelación inicial de 3 segundos sin depender de `_isHovering` físico, y cancelar/desacoplar explícitamente los `AnimationClock` previos en `IslandView.TransitionContent`.

#### `AUD-022` — `dotnet format openDynamic.sln --verify-no-changes` falla con código de salida 1 por finales de línea `LF` en 9 archivos
- **Severidad:** MEDIO
- **Categoría:** Calidad de Código / Formato (`A6`)
- **Ubicación:**
  - `src/OpenDynamic.App/AssemblyInfo.cs`
  - `src/OpenDynamic.App/Infrastructure/LoggingConfiguration.cs`
  - `src/OpenDynamic.App/Infrastructure/SingleInstanceManager.cs`
  - `src/OpenDynamic.App/Windowing/IslandWindow.xaml.cs`
  - `src/OpenDynamic.Core/CoreInfo.cs`
  - `tests/OpenDynamic.Tests/Hardware/HardwareSettingsTests.cs`
  - `tests/OpenDynamic.Tests/InfrastructureTests.cs`
  - `tests/OpenDynamic.Tests/Power/BatteryThresholdTrackerTests.cs`
  - `tests/OpenDynamic.Tests/Widgets/PriorityResolverPhase15Tests.cs`
- **Descripción técnica:** Al ejecutar `dotnet format openDynamic.sln --verify-no-changes`, la herramienta reporta diagnósticos `ENDOFLINE: Fix end of line marker` en los 9 archivos listados arriba y termina con código de salida `1`.
- **Impacto:** Incumple el criterio de formato limpio en toda la solución.
- **Propuesta de corrección:** Normalizar los finales de línea de los 9 archivos con `dotnet format` en la Pasada B (Sección B2).

#### `AUD-023` — `PrivacyAccessAggregator` carece de supresión de línea base al arrancar y `UpdateIgnoredApps` no reevalúa las aplicaciones activas
- **Severidad:** MEDIO
- **Categoría:** Lógica de Dominio / Privacidad (`A2`)
- **Ubicación:** `src/OpenDynamic.Core/Privacy/PrivacyAccessAggregator.cs:62-77, 123-165`
- **Descripción técnica:**
  1. A diferencia de `NetworkAlertPolicy`, `DeviceAlertPolicy`, `EnergySaverAlertPolicy` y `BatteryThresholdTracker`, `PrivacyAccessAggregator` no tiene bandera `_isInitialized` para suprimir alertas `Started` en la primera lectura al arrancar. Si el usuario ya tiene una videollamada o grabación activa antes de abrir `openDynamic`, la primera lectura emite una alerta transitoria `Started` como si acabara de activarse. (Los puntos persistentes ámbar/verde sí deben mostrarse desde el inicio, pero no el toast transitorio).
  2. `UpdateIgnoredApps` (`líneas 62-77`) actualiza `_ignoredApps` pero no elimina de `_activeMicApps` / `_activeCamApps` una aplicación que ya estuviera activa ni dispara `StateChanged` inmediatamente.
- **Impacto:** Toasts transitorios redundantes al arrancar la aplicación durante una llamada en curso y retraso al ignorar una app activa en Configuración.
- **Propuesta de corrección:** En la primera llamada a `ProcessEntries`, poblar `_activeMicApps` y `_activeCamApps` y emitir `StateChanged` (para encender los puntos indicadores persistentes) sin emitir alertas transitorias `PrivacyAccessEventKind.Started`; y reevaluar el estado activo inmediatamente en `UpdateIgnoredApps`.

#### `AUD-024` — `MediaActivityController` no desuscribe eventos al cerrarse la sesión (`OnSessionClosed`) y `SettingsService.Save` tiene condición de carrera con `Dispose()`
- **Severidad:** MEDIO
- **Categoría:** Ciclo de Vida / Core (`A2`)
- **Ubicación:** `src/OpenDynamic.Core/Media/MediaActivityController.cs:72-75, 144-154`, `src/OpenDynamic.Core/Settings/SettingsService.cs:274, 296-331`
- **Descripción técnica:**
  1. En `MediaActivityController.OnSessionClosed` (`líneas 72-75`), se llama a `HandleSessionTerminated()` pero no se desuscriben `_currentSession.PlaybackInfoChanged -= OnPlaybackInfoChanged` ni `_currentSession.SessionClosed -= OnSessionClosed`, ni se limpia `_currentSession = null`.
  2. En `SettingsService`, si `OnDebounceTimerTick` ya fue encolado en el ThreadPool cuando se invoca `Dispose()`, puede ejecutarse después de dispuesto el servicio.
- **Impacto:** Retención de referencia a `IMediaSession` cerrada y posible escritura concurrente en `SettingsService` al cerrar.
- **Propuesta de corrección:** Llamar a `UnhookCurrentSession()` dentro de `OnSessionClosed` en `MediaActivityController` y sincronizar `OnDebounceTimerTick` con `_isDisposed` bajo un candado en `SettingsService`.

---

### 5.4 Hallazgos BAJOS / INFO

#### `AUD-025` — Cobertura de pruebas unitarias ausente para clases de `OpenDynamic.App` y casos borde de `OpenDynamic.Core`
- **Severidad:** BAJO / INFO
- **Categoría:** Suite de Pruebas (`A6`)
- **Ubicación:** `tests/OpenDynamic.Tests/OpenDynamic.Tests.csproj:19-21`, `tests/OpenDynamic.Tests/Settings/SettingsServiceTests.cs:497`
- **Descripción técnica:** `OpenDynamic.Tests` solo referencia `OpenDynamic.Core.csproj`, por lo que `IslandOrchestrator`, `IslandAnimator`, `WindowPositioner` y los widgets de `OpenDynamic.App` no tienen pruebas unitarias directas. Además, `SettingsServiceTests.cs:497` utiliza `await Task.Delay(250)` real y faltan tests para clamping de valores fuera de rango en `SettingsService.Load()`, `TimerController.AddTime()` en pausa y `DeviceAlertPolicy` bajo concurrencia.
- **Impacto:** Regresiones en la capa `App` (como `AUD-002`) no fueron detectadas por la suite automatizada.
- **Propuesta de corrección:** Añadir pruebas unitarias deterministas para todos los casos borde de `SettingsService.Load()`, `DeviceAlertPolicy`, `TimerCollection`, `TimerController` y `PrivacyAccessAggregator` en la Pasada B (Sección B4).

#### `AUD-026` — Suscripción duplicada a `SystemParameters.StaticPropertyChanged` y pinceles `SolidColorBrush` sin `.Freeze()` en `AccessibilityThemeManager`
- **Severidad:** BAJO
- **Categoría:** Rendimiento WPF / Limpieza (`A2`)
- **Ubicación:** `src/OpenDynamic.App/Infrastructure/AccessibilityThemeManager.cs:22, 60-68`, `src/OpenDynamic.App/Windowing/IslandWindow.xaml.cs:240, 587-599`, `src/OpenDynamic.App/ViewModels/SettingsViewModel.cs:660`
- **Descripción técnica:** Tanto `AccessibilityThemeManager.Initialize()` como `IslandWindow.OnSourceInitialized` se suscriben a `SystemParameters.StaticPropertyChanged` para `HighContrast` e invocan `AccessibilityThemeManager.UpdateTheme()` por duplicado. Además, los 6 `new SolidColorBrush(...)` creados en `AccessibilityThemeManager.ApplyTheme(false)` (`líneas 60-68`) no llaman a `.Freeze()`, y `SettingsViewModel.cs:660` nunca se desuscribe de `SystemParameters.StaticPropertyChanged`.
- **Impacto:** Doble aplicación de recursos de tema ante cambios de alto contraste y pinceles de tema no congelados.
- **Propuesta de corrección:** Congelar (`.Freeze()`) todos los `SolidColorBrush` en `AccessibilityThemeManager.ApplyTheme` y eliminar la suscripción duplicada en `IslandWindow.xaml.cs:240`.

#### `AUD-027` — Hook anónimo `HwndSourceHook` para `TaskbarCreated` en `App.OnStartup` no se almacena ni se remueve al cerrar
- **Severidad:** BAJO
- **Categoría:** Ciclo de Vida Win32 (`A2`)
- **Ubicación:** `src/OpenDynamic.App/App.xaml.cs:144-152`
- **Descripción técnica:** `hwndSource.AddHook((IntPtr hwnd, int msg, ...) => ...)` registra una lambda anónima sin guardar la referencia del delegado ni llamar a `RemoveHook` en `OnExit`.
- **Impacto:** El hook permanece registrado en `HwndSource` hasta la destrucción final del HWND.
- **Propuesta de corrección:** Guardar el `HwndSourceHook` en un campo privado de `App` y removerlo en `OnExit`.

#### `AUD-028` — Argumentos de prueba por línea de comandos (`--trigger-test-exception`, `--exit-after-ms`, `--smoke-test`) expuestos en compilación `Release`
- **Severidad:** BAJO / INFO
- **Categoría:** Seguridad / Superficie de Ataque (`A4`)
- **Ubicación:** `src/OpenDynamic.App/App.xaml.cs:237-271`
- **Descripción técnica:** `ProcessCommandLineArgs` permite forzar una excepción en el Dispatcher (`--trigger-test-exception`) o programar el cierre de la aplicación (`--exit-after-ms`, `--smoke-test`) en compilaciones `Release`.
- **Impacto:** Cualquier proceso local podría invocar `OpenDynamic.App.exe --trigger-test-exception` (si no hay instancia previa corriendo).
- **Propuesta de corrección:** Restringir `--trigger-test-exception` bajo `#if DEBUG` (manteniendo `--smoke-test` o `--exit-after-ms` solo si se usan en CI smoke tests).

#### `AUD-029` — Método muerto `FullscreenDetector.IsNotificationStateFullscreen` y campo sin leer `_lastEmittedTimeUtc` en `NetworkAlertPolicy`
- **Severidad:** BAJO / INFO
- **Categoría:** Limpieza de Código (`A2`)
- **Ubicación:** `src/OpenDynamic.Core/Windowing/FullscreenDetector.cs:21-24`, `src/OpenDynamic.Core/Network/NetworkAlertPolicy.cs:22, 104, 253`
- **Descripción técnica:** `FullscreenDetector.IsNotificationStateFullscreen` no es llamado por `FullscreenWatcher` (que evalúa `QUNS_RUNNING_D3D_FULL_SCREEN` y `QUNS_PRESENTATION_MODE` directamente para no ocultar la isla con `QUNS_BUSY`), y `_lastEmittedTimeUtc` en `NetworkAlertPolicy` se escribe pero nunca se lee.
- **Impacto:** Código muerto menor.
- **Propuesta de corrección:** Alinear `FullscreenDetector.IsNotificationStateFullscreen` con `FullscreenWatcher` (excluyendo `QunsBusy`) y eliminar el campo sin leer `_lastEmittedTimeUtc`.

#### `AUD-030` — Paquetes NuGet desactualizados o marcados como Legacy en `OpenDynamic.Tests` y dependencia transitiva en `OpenDynamic.App`
- **Severidad:** BAJO / INFO
- **Categoría:** Dependencias (`A1`)
- **Ubicación:** `src/OpenDynamic.App/OpenDynamic.App.csproj`, `tests/OpenDynamic.Tests/OpenDynamic.Tests.csproj`
- **Descripción técnica:**
  - `dotnet list package --vulnerable --include-transitive`: **0 vulnerabilidades**.
  - `dotnet list package --deprecated --include-transitive`: `xunit 2.9.3` (y paquetes `xunit.*` en `OpenDynamic.Tests`) están marcados como `Legacy` en favor de `xunit.v3`.
  - `dotnet list package --outdated --include-transitive`: en `OpenDynamic.App`, `System.Numerics.Tensors` transitivo (`9.0.0` → `10.0.12`). En `OpenDynamic.Tests`, `coverlet.collector` (`6.0.4` → `10.1.0`), `Microsoft.NET.Test.Sdk` (`17.14.1` → `18.10.1`), `xunit.runner.visualstudio` (`3.1.4` → `4.0.0`), `Newtonsoft.Json` (`13.0.3` → `13.0.4`).
- **Impacto:** Ninguno en seguridad ni estabilidad del producto (`0` vulnerabilidades).
- **Propuesta de corrección:** Mantener `xunit 2.9.3` estable para `v2.0.0` (evitando cambios disruptivos de framework de pruebas en pre-release) y reemplazar `NAudio` por `NAudio.Wasapi` (`AUD-014`).

---

## 6. Auditoría de Dependencias y Licencias (A1 y A8)

### 6.1 Verificación de Retiro de Antigravity (Fases 17 y 18)
Comando ejecutado:
```bash
git grep -n -i -E "antigravity|AgentApproval|OpenDynamic\.Hook|approval-rules|openDynamic-agent|hooks\.json"
```
- **Resultado en `src/` y `tests/`:** **0 coincidencias** (en `SettingsService.cs:374` la cadena `"approval" + "-rules.json"` está dividida deliberadamente para la limpieza automática del archivo heredado según ADR-027).
- **Coincidencias legítimas fuera de `CHANGELOG.md` y `DECISIONS.md`:**
  1. `.github/workflows/ci.yml:31-36`: Paso de guardia en CI que verifica precisamente la ausencia de restos de Antigravity en `src/` y `tests/`.
  2. `installer/setup.iss:87-89`: Sección `[InstallDelete]` que elimina `{app}\hook` y `{app}\OpenDynamic.Hook.exe` al actualizar desde instalaciones `v1.x`.
  3. `scripts/limpiar-hooks-antigravity.ps1`: Script independiente de desinstalación para usuarios de versiones previas (ADR-027).

### 6.2 Inventario Completo de Paquetes NuGet y Licencias

| Proyecto | Paquete NuGet | Versión Actual | Tipo | Licencia SPDX | Vulnerabilidades | Estado / Acción en Pasada B |
|---|---|---|---|---|---|---|
| `OpenDynamic.Core` | `CommunityToolkit.Mvvm` | `8.4.2` | Directa | `MIT` | `0` | Conservar e incluir en `THIRD-PARTY-NOTICES.md`. |
| `OpenDynamic.App` | `CommunityToolkit.Mvvm` | `8.4.2` | Directa | `MIT` | `0` | Conservar e incluir en `THIRD-PARTY-NOTICES.md`. |
| `OpenDynamic.App` | `H.NotifyIcon.Wpf` | `2.4.1` | Directa | `MIT` | `0` | Conservar e incluir en `THIRD-PARTY-NOTICES.md` (junto con `H.NotifyIcon` `2.4.1` MIT). |
| `OpenDynamic.App` | `Microsoft.Extensions.DependencyInjection` | `10.0.5` | Directa | `MIT` | `0` | Conservar e incluir en `THIRD-PARTY-NOTICES.md`. |
| `OpenDynamic.App` | `NAudio` (`NAudio.Wasapi` / `NAudio.Core`) | `2.3.0` | Directa | `MIT` | `0` | Cambiar de metapaquete `NAudio` a `NAudio.Wasapi 2.3.0` (`AUD-014`) e incluir en `THIRD-PARTY-NOTICES.md`. |
| `OpenDynamic.App` | `Serilog` | `4.3.1` | Directa | `Apache-2.0` | `0` | Conservar e incluir atribución Apache-2.0 en `THIRD-PARTY-NOTICES.md`. |
| `OpenDynamic.App` | `Serilog.Sinks.File` | `7.0.0` | Directa | `Apache-2.0` | `0` | Conservar e incluir atribución Apache-2.0 en `THIRD-PARTY-NOTICES.md`. |
| `OpenDynamic.App` | `System.Diagnostics.PerformanceCounter` | `10.0.5` | Directa | `MIT` | `0` | Conservar e incluir en `THIRD-PARTY-NOTICES.md`. |
| `OpenDynamic.Tests` | `xunit` / `xunit.runner.visualstudio` / `Microsoft.NET.Test.Sdk` / `coverlet.collector` | `2.9.3` / `3.1.4` / `17.14.1` / `6.0.4` | Test (No distribuido) | `Apache-2.0` / `MIT` | `0` | Solo de pruebas; no se redistribuyen en binarios de release. |

---

## 7. Decisiones de Diseño Pendientes (Para Aprobación del Usuario antes de Pasada B)

Antes de iniciar la **Pasada B (Corrección, Endurecimiento y Preparación de Release)**, se solicita tu confirmación sobre las siguientes decisiones técnicas:

1. **Detección de Hover en Reposo (`AUD-001` y `AUD-005`)**:
   - **Propuesta recomendada:** Eliminar por completo el timer de sondeo de 10 Hz (`_restingHoverWatcherTimer`) para cumplir estrictamente la Regla de Oro 1 (0 timers activos en `Hidden`). En su lugar, usar eventos nativos de entrada de ratón (`MouseEnter`/`MouseMove`/`WM_NCHITTEST` de WPF) sobre un `RestingSensorNotch` reducido a **`120x4 DIP`** (o `80x4 DIP` según ADR-007/ADR-017) pegado al borde superior de la pantalla, que solo devuelva `HTCLIENT` cuando `EnableAmbientClock == true` y `!IsFullscreenSuppressed` (y devuelva `HTTRANSPARENT` en cualquier otro caso para no interceptar clics en pestañas del navegador). Una vez que el cursor entra y activa la isla (`Compact`/`Expanded`), el `_hoverLeaveTimer` existente (`60 ms`) vigila la salida del cursor y se apaga en cuanto la isla vuelve a `Hidden`.
2. **Comportamiento al desactivar `Enable*Widget` en Configuración (`AUD-002`)**:
   - **Propuesta recomendada:** Respetar en tiempo real los 5 interruptores (`EnableMediaWidget`, `EnableVolumeWidget`, `EnableBatteryWidget`, `EnableTimerWidget`, `EnableStopwatchWidget`). Cuando un widget esté deshabilitado, no se activará en la isla (`IsActive = false`) y, en el caso de `EnableTimerWidget` / `EnableStopwatchWidget`, se ocultarán o deshabilitarán también sus accesos rápidos si están desactivados.
3. **Monitoreo de GPU (`EnableGpuMonitoring`) y Hilo UI (`AUD-008`)**:
   - **Propuesta recomendada:** Mantener `EnableGpuMonitoring = false` por defecto, mover la inicialización y lectura de los contadores PDH `GPU Engine` a un hilo en segundo plano (`Task.Run` con exclusión mutua) para no bloquear jamás el hilo UI de 60 FPS, y liberar (`Dispose()`) inmediatamente los `PerformanceCounter` cuando `EnableGpuMonitoring` se desactive o cuando el widget de hardware deje de estar activo.
4. **Huella de Memoria en Reposo (`WorkingSet64` vs `PrivateMemorySize64`, `AUD-013` y `AUD-014`)**:
   - En .NET 10 + WPF con aceleración por hardware DirectX/D3D9 y `ReadyToRun`, la memoria privada (`PrivateMemorySize64`) en reposo es de **`71.04 MB`** (dentro del objetivo `< 80 MB`), mientras que el `WorkingSet64` total del proceso reportado por Windows incluye ~90 MB de páginas compartidas de DLLs del sistema/GPU (`~160.5 MB`).
   - **Propuesta recomendada:** Reemplazar `NAudio` por `NAudio.Wasapi` (`AUD-014`), liberar recursos transitorios y documentar explícitamente en el informe final de cierre tanto `PrivateMemorySize64` (`~71 MB`, memoria real propia del proceso) como `WorkingSet64` (que incluye páginas compartidas de D3D/WPF/OS).

---

## 8. Plan de Acción Priorizado para Pasada B

Una vez aprobada la transición a la Pasada B, las correcciones se aplicarán en la rama `audit/v2.0.0` siguiendo commits atómicos, verificables y en este orden estricto:

1. **Commit 1 — `fix(windowing): remove idle 10Hz polling timer and shrink resting sensor notch` (`AUD-001`, `AUD-005`, `AUD-021`)**:
   - Eliminar `_restingHoverWatcherTimer` en `IslandWindow.xaml.cs`.
   - Reducir `RestingSensorNotch` a una franja mínima superior (`120x4 DIP`), devolver `HTTRANSPARENT` en `WM_NCHITTEST` cuando `_orchestrator.IsFullscreenSuppressed` o `!_settings.EnableAmbientClock`, e impedir que el hover salte la supresión de pantalla completa en `IslandOrchestrator.cs:456`.
   - Corregir la revelación inicial de 3 segundos del reloj ambiental en `App.OnStartup` para que no sea cancelada a los 9 ms, y limpiar `AnimationClock` en `IslandView.TransitionContent`.
   - Invocar `IslandWindow.Close()` en `App.OnExit` (`AUD-013`) y remover el hook `TaskbarCreated` (`AUD-027`).
2. **Commit 2 — `fix(widgets): enforce Enable*Widget settings and fix Bluetooth watcher & GPU UI stall` (`AUD-002`, `AUD-003`, `AUD-006`, `AUD-007`, `AUD-008`, `AUD-015`, `AUD-019`, `AUD-020`)**:
   - Hacer cumplir `EnableMediaWidget`, `EnableVolumeWidget`, `EnableBatteryWidget`, `EnableTimerWidget` y `EnableStopwatchWidget` en tiempo real.
   - Corregir las propiedades AEP en `DeviceService.StartBluetoothWatcher()` (`0x8002802B`) y sincronizar `NotifyEnumerationCompleted()` entre Bluetooth y AudioRender para eliminar las 3 alertas falsas de audio al arrancar.
   - Mover la inicialización/lectura de contadores PDH GPU en `HardwareService`/`HardwareWidget` fuera del hilo UI y disponerlos al desactivar GPU.
   - Corregir la suscripción duplicada a `CompositionTarget.Rendering` en `MediaCompactView`/`MediaExpandedView` y pasar el estado real `IsFullscreenSuppressed` en `MediaWidget.UpdateVisualizerState()`.
   - Añadir filtro por monitor y *debounce* a `EVENT_OBJECT_LOCATIONCHANGE` en `FullscreenWatcher`, y condicionar el arranque de servicios deshabilitados (`PrivacyAccessMonitor`, `FullscreenWatcher`).
   - Corregir `TrayIconManager` (`ForceCreate` fallback y `DestroyIcon`), disponer `Process` en `MediaService`, usar cooldown de `5 s` en `EnergySaverService` y actualizar umbrales en caliente en `PowerService`.
3. **Commit 3 — `fix(privacy): redact sensitive data from logs and set Release log level to Information` (`AUD-004`, `AUD-028`)**:
   - Configurar `.MinimumLevel.Information()` en `Release` (`.MinimumLevel.Debug()` solo bajo `#if DEBUG`) en `LoggingConfiguration.cs`.
   - Sanitizar todos los logs en `NetworkWidget`, `PrivacyWidget`, `MediaColorService`, `VolumeService`, `VolumeWidget`, `TimerWidget`, `SettingsViewModel`, `LoggingConfiguration`, `TrayIconManager`, `ServiceCollectionExtensions` y `AutostartService`.
   - Proteger `--trigger-test-exception` bajo `#if DEBUG` en `App.xaml.cs`.
4. **Commit 4 — `fix(core): harden settings validation, timer collection, and device alert concurrency` (`AUD-009`, `AUD-010`, `AUD-016`, `AUD-017`, `AUD-018`, `AUD-023`, `AUD-024`, `AUD-025`, `AUD-026`, `AUD-029`)**:
   - Implementar `SanitizeAndClamp()` en `AppSettings`/`SettingsService.Load()` y proteger `AppSettings.Clone()`.
   - Corregir la condición de carrera en `DeviceAlertPolicy`, el churn `N+1` y duraciones Pomodoro en `TimerCollection`, `AddTime` en `TimerController`, la persistencia atómica y `TotalDuration` en `TimerPersistenceService`, el cacheo de `_laps` en `StopwatchController`, la línea base inicial en `PrivacyAccessAggregator` y la desuscripción en `MediaActivityController`.
   - Añadir tests unitarios en `OpenDynamic.Tests` que cubran cada uno de los bugs corregidos.
5. **Commit 5 — `style: normalize line endings and code formatting across solution` (`AUD-022`)**:
   - Ejecutar `dotnet format openDynamic.sln` y verificar `dotnet format openDynamic.sln --verify-no-changes` con código de salida `0`.
6. **Commit 6 — `docs: add v2.0.0 governance files, third-party notices, and update README/CHANGELOG` (`AUD-011`)**:
   - Crear `THIRD-PARTY-NOTICES.md`, `SECURITY.md`, `CONTRIBUTING.md`, `.github/ISSUE_TEMPLATE/bug_report.yml`, `.github/ISSUE_TEMPLATE/feature_request.yml` y `.github/PULL_REQUEST_TEMPLATE.md`.
   - Actualizar `README.md` (sección de Privacidad, guía SmartScreen, corrección de enlace roto a `docs/img/`) y redactar la entrada `[2.0.0]` en `CHANGELOG.md`.
7. **Commit 7 — `build(release): centralize v2.0.0 version, replace NAudio metapackage, and harden release workflow` (`AUD-012`, `AUD-014`)**:
   - Centralizar versión `2.0.0` en `Directory.Build.props`, sincronizar `app.manifest` y `installer/setup.iss` (añadiendo `LicenseFile=..\LICENSE`).
   - Reemplazar `NAudio` por `NAudio.Wasapi` en `OpenDynamic.App.csproj`.
   - Actualizar `.github/workflows/ci.yml` (`permissions: contents: read`) y `.github/workflows/release.yml` (excluir `.pdb`, incluir `README.md`/`LICENSE`/`THIRD-PARTY-NOTICES.md` en el ZIP, generar `SHA256SUMS.txt` y configurar `draft: true`).
8. **Verificación Post-Corrección (B7)**:
   - Re-ejecutar `scripts/medir-rendimiento-v2.ps1` en `Release` (`ReadyToRun`), verificar la tabla comparativa *Antes vs. Después* en `docs/auditoria-v2.0.0.md` y validar el 100 % de los criterios Go/No-Go antes de cualquier fusión a `main`.
