# openDynamic

> **Dynamic Island para Windows (WPF, .NET 10)**  
> Una cápsula flotante, contextual e interactiva (música, volumen, batería, hardware, temporizador) con animaciones de resorte fluidas y consumo ultra bajo de recursos.

Repositorio oficial: [https://github.com/DAHL13/openDynamic](https://github.com/DAHL13/openDynamic)

---

## Estado del Proyecto

| Fase | Descripción | Estado |
|---|---|:---:|
| **Fase 0** | **Andamiaje del proyecto, CI, DI, Logging y Pruebas iniciales** | **Completada** |
| **Fase 1** | **Ventana de la isla, posicionamiento y renderizado básico (M1)** | **Completada** |
| **Fase 2** | **Motor de física de animación de resorte (*Spring physics*) y máquina de estados (M2)** | **Completada** |
| **Fase 3** | **Arquitectura de widgets, resolución de prioridades y Orchestrator (M4 Base)** | **Completada** |
| **Fase 4** | **Widget multimedia GSMTC (Windows.Media.Control, 0% CPU, Freeze thumbnails, 10s Grace)** | **Completada** |
| **Fase 5** | **Volumen (NAudio/CoreAudio), batería sin polling (WM_POWERBROADCAST) y pantalla completa (M3)** | **Completada** |
| **Fase 6** | **Hardware (GetSystemTimes/GlobalMemoryStatusEx), Temporizador/Pomodoro y Modo Split con intercambio (M4)** | **Completada** |
| **Fase 7** | **Bandeja del sistema (H.NotifyIcon), Ajustes (MVVM), Atajos Win32 e Inicio automático (M5)** | **Completada** |
| **Fase 8** | **Optimización, robustez, eventos del sistema y pruebas de rendimiento (Previo a M6)** | **Completada** |


---

## Requisitos

- **Sistema Operativo:** Windows 10 (versión 2004 / compilación 19041 o superior) o Windows 11.
- **SDK:** [.NET 10 SDK](https://dotnet.microsoft.com/download) instalado.

---

## Stack Tecnológico

- **Lenguaje / Runtime:** C# 13+, .NET 10
- **Interfaz de Usuario:** WPF (`net10.0-windows10.0.19041.0`) con soporte `PerMonitorV2` DPI
- **Patrón Arquitectónico:** MVVM mediante `CommunityToolkit.Mvvm`
- **Inyección de Dependencias:** `Microsoft.Extensions.DependencyInjection`
- **Audio:** `NAudio` (`MMDeviceEnumerator`, `AudioEndpointVolume`) con detección en caliente (`IMMNotificationClient`)
- **Bandeja del Sistema (Tray):** `H.NotifyIcon.Wpf` (cero WinForms)
- **Atajos Globales:** Win32 `RegisterHotKey` / `UnregisterHotKey` mediante WndProc
- **Configuración y Persistencia:** `System.Text.Json` en `%AppData%\openDynamic\settings.json` (versión de esquema y debounce 500ms)
- **Registro de Eventos (Logging):** `Serilog` y `Serilog.Sinks.File` en `%LocalAppData%\openDynamic\logs`
- **Pruebas Unitarias:** `xUnit`

Para más detalles sobre las directrices y restricciones de arquitectura, consulta [`DECISIONS.md`](./DECISIONS.md).

---

## Estructura de la Solución

```
openDynamic/
├─ openDynamic.sln
├─ Directory.Build.props            # Nullable, LangVersion latest, ImplicitUsings
├─ .editorconfig
├─ .gitignore
├─ DECISIONS.md                    # Registro de Decisiones de Arquitectura (ADR)
├─ CHANGELOG.md                    # Historial de cambios por versión
├─ README.md                       # Documentación principal
├─ docs/
│  └─ pruebas.md                   # Matriz de validación y pruebas de sistema
├─ .github/
│  └─ workflows/
│      └─ ci.yml                   # CI en windows-latest (restore, build, test)
├─ src/
│  ├─ OpenDynamic.Core/             # net10.0 — Lógica pura sin dependencias de Windows ni WPF
│  │   ├─ CoreInfo.cs
│  │   ├─ Animation/               # Física de resortes con sub-stepping y conservación de inercia
│  │   │   └─ Spring.cs
│  │   ├─ State/                   # Máquina de estados finita y layout de dimensiones de cápsula
│  │   │   ├─ IslandState.cs
│  │   │   ├─ IslandStateMachine.cs
│  │   │   ├─ IslandStateChangedEventArgs.cs
│  │   │   └─ IslandLayout.cs
│  │   ├─ Widgets/                 # Modelos de actividad y resolución pura de prioridades
│  │   │   ├─ IActivitySource.cs
│  │   │   ├─ IslandActivity.cs
│  │   │   ├─ ActivityPriority.cs
│  │   │   ├─ WidgetDisplayMode.cs
│  │   │   ├─ PriorityResult.cs
│  │   │   └─ PriorityResolver.cs
│  │   ├─ Media/                   # Modelos inmutables de reproducción, control y extrapolación pura
│  │   │   ├─ IMediaSession.cs
│  │   │   ├─ IMediaService.cs
│  │   │   ├─ MediaPlaybackStatus.cs
│  │   │   ├─ MediaPlaybackCapabilities.cs
│  │   │   ├─ MediaPlaybackInfo.cs
│  │   │   ├─ MediaTimelineInfo.cs
│  │   │   ├─ MediaPropertiesInfo.cs
│  │   │   ├─ MediaProgressCalculator.cs
│  │   │   └─ MediaActivityController.cs
│  │   ├─ Audio/                   # Control de audio puro, pasos por rueda de ratón y tipos de icono
│  │   │   ├─ IVolumeController.cs
│  │   │   ├─ VolumeChangedEventArgs.cs
│  │   │   ├─ VolumeIconType.cs
│  │   │   └─ VolumeCalculator.cs
│  │   ├─ Power/                   # Monitorización de batería, snapshots y tracker de umbrales puro
│  │   │   ├─ IBatteryMonitor.cs
│  │   │   ├─ BatterySnapshot.cs
│  │   │   ├─ BatteryAlertKind.cs
│  │   │   ├─ BatteryAlertEventArgs.cs
│  │   │   └─ BatteryThresholdTracker.cs
│  │   ├─ Hardware/                # Rendimiento de hardware puro, deltas Win32 y snapshots
│  │   │   ├─ IHardwareMonitor.cs
│  │   │   ├─ HardwareSnapshot.cs
│  │   │   └─ HardwareCalculator.cs
│  │   ├─ Timer/                   # Temporizador por marca de tiempo objetivo y Pomodoro sin deriva
│  │   │   ├─ ITimerController.cs
│  │   │   ├─ TimerController.cs
│  │   │   ├─ TimerSnapshot.cs
│  │   │   ├─ TimerMode.cs
│  │   │   └─ TimerState.cs
│  │   ├─ Windowing/               # Detección geométrica y de estado de pantalla completa pura
│  │   │   └─ FullscreenDetector.cs
│  │   ├─ Settings/                # Configuración persistente, versionado de esquema y debounce (500ms)
│  │   │   ├─ AppSettings.cs
│  │   │   ├─ ISettingsService.cs
│  │   │   └─ SettingsService.cs
│  │   ├─ Hotkeys/                 # Definición y análisis puro de atajos de teclado sin hooks
│  │   │   ├─ HotkeyDefinition.cs
│  │   │   └─ HotkeyParser.cs
│  │   ├─ Autostart/               # Abstracción y lógica de arranque con Windows (HKCU Run)
│  │   │   ├─ IRegistryAccessor.cs
│  │   │   ├─ IAutostartService.cs
│  │   │   └─ AutostartServiceCore.cs
│  │   └─ Positioning/             # Cálculo puro de posicionamiento geométrico y DPI
│  │       ├─ IslandPositionCalculator.cs
│  │       ├─ MonitorArea.cs
│  │       ├─ DisplayDpi.cs
│  │       ├─ WindowDimensions.cs
│  │       └─ CalculatedWindowPlacement.cs
│  └─ OpenDynamic.App/              # WPF, net10.0-windows10.0.19041.0
│      ├─ app.manifest              # PerMonitorV2 DPI awareness
│      ├─ App.xaml / App.xaml.cs    # Ciclo de vida, DI, bandeja, atajos y arranque
│      ├─ Native/                   # P/Invoke a Win32 (RegisterHotKey, monitores, hardware, energía, estilos)
│      │   └─ NativeMethods.cs
│      ├─ Animation/                # Coordinador de animación y suscripción a CompositionTarget.Rendering
│      │   └─ IslandAnimator.cs
│      ├─ Orchestration/            # Autoridad exclusiva de transiciones, suspensión y entrega de vistas
│      │   └─ IslandOrchestrator.cs
│      ├─ Windowing/                # Ventana overlay, posicionamiento, detección en primer plano y pantalla completa
│      │   ├─ IslandWindow.xaml / IslandWindow.xaml.cs
│      │   ├─ WindowPositioner.cs
│      │   ├─ ForegroundWatcher.cs
│      │   └─ FullscreenWatcher.cs
│      ├─ Services/                 # Servicios nativos: GSMTC, NAudio, Energía, Hardware, Hotkeys y Autostart
│      │   ├─ MediaService.cs
│      │   ├─ WinRtMediaSession.cs
│      │   ├─ VolumeService.cs
│      │   ├─ PowerService.cs
│      │   ├─ HardwareService.cs
│      │   ├─ IHotkeyService.cs / HotkeyService.cs
│      │   └─ AutostartService.cs
│      ├─ ViewModels/               # Arquitectura MVVM para ventana de configuración
│      │   ├─ SettingsViewModel.cs
│      │   └─ SettingsConverters.cs
│      ├─ Views/                    # Renderizado elástico y ventana independiente de ajustes
│      │   ├─ IslandView.xaml / IslandView.xaml.cs
│      │   └─ SettingsWindow.xaml / SettingsWindow.xaml.cs
│      ├─ Widgets/                  # Contrato base, mensajería, widgets multimedia, volumen, batería, hardware y temporizador
│      │   ├─ IIslandWidget.cs
│      │   ├─ IslandWidgetBase.cs
│      │   ├─ Messages/ActivityMessages.cs
│      │   ├─ Media/                # Widget GSMTC: vistas Compact, Expanded y Split satélite
│      │   ├─ Volume/               # Widget Volumen: vistas Compact, Expanded y Split satélite
│      │   ├─ Battery/              # Widget Batería: vistas Compact, Expanded y Split satélite
│      │   ├─ Hardware/             # Widget Hardware: vistas Compact, Expanded y Split satélite (0% CPU reposo)
│      │   ├─ Timer/                # Widget Temporizador/Pomodoro: vistas Compact, Expanded y Split satélite
│      │   └─ Demo/DemoWidgets.cs   # Condicionado a #if DEBUG
│      ├─ Resources/                # Icono de cápsula integrado
│      │   └─ app.ico
│      └─ Infrastructure/           # DI, SingleInstance, Logging, TrayIconManager (H.NotifyIcon)
│          ├─ ServiceCollectionExtensions.cs
│          ├─ SingleInstanceManager.cs
│          ├─ LoggingConfiguration.cs
│          └─ TrayIconManager.cs
└─ tests/
   └─ OpenDynamic.Tests/            # xUnit probando Core (resortes, FSM, prioridades, settings, hotkeys, autostart)
       ├─ InfrastructureTests.cs
       ├─ Animation/
       │   └─ SpringTests.cs
       ├─ State/
       │   ├─ IslandStateMachineTests.cs
       │   └─ IslandLayoutTests.cs
       ├─ Widgets/
       │   ├─ PriorityResolverTests.cs
       │   ├─ PriorityResolverPhase5Tests.cs
       │   └─ PriorityResolverPhase6Tests.cs
       ├─ Hardware/
       │   └─ HardwareCalculatorTests.cs
       ├─ Timer/
       │   ├─ FakeTimeProvider.cs
       │   └─ TimerControllerTests.cs
       ├─ Settings/
       │   └─ SettingsServiceTests.cs
       ├─ Hotkeys/
       │   └─ HotkeyParserTests.cs
       ├─ Autostart/
       │   └─ AutostartServiceTests.cs
       └─ Positioning/
           └─ IslandPositionCalculatorTests.cs
```

---

## Compilación y Pruebas

### Restaurar dependencias:
```powershell
dotnet restore openDynamic.sln
```

### Compilar la solución en Release:
```powershell
dotnet build openDynamic.sln -c Release
```

### Ejecutar las pruebas unitarias:
```powershell
dotnet test openDynamic.sln -c Release
```

### Ejecutar la aplicación en modo de verificación (humo):
```powershell
dotnet run --project src/OpenDynamic.App/OpenDynamic.App.csproj -- --smoke-test
```

### Publicar con optimización ReadyToRun (R2R):
```powershell
dotnet publish src/OpenDynamic.App/OpenDynamic.App.csproj -c Release -r win-x64 -p:PublishReadyToRun=true --self-contained false
```

---

## Rendimiento y Mediciones Reales

En estricto cumplimiento de la **Regla de Oro 1** (0% CPU en reposo y consumo mínimo de memoria), se auditaron y registraron las siguientes métricas de rendimiento reales en el entorno de desarrollo:

| Parámetro / Métrica | Meta Técnica | Medición Real Obtenida | Estado |
|---|---|---|:---:|
| **Consumo de RAM en Reposo (Working Set)** | < 100 MB | **27.9 MB** | ✔ **Superada ampliamente** |
| **Memoria Privada Comprometida** | < 25 MB | **5.2 MB** | ✔ **Excelente** |
| **Consumo de CPU en Reposo (sin actividad visible)** | < 0.5% (ideal 0.0%) | **0.00%** | ✔ **Cumplida estrictamente** |
| **Consumo de CPU durante Animación de Resorte** | < 5.0% | **< 1.0%** (pico transitorio) | ✔ **Fluido a 60-144 FPS** |
| **Suscripción al Bucle de Composición WPF** | Desuscrito en reposo | **0 suscripciones** a `CompositionTarget.Rendering` al asentarse | ✔ **Cero bucles ocultos** |
| **Compilación Estricta** | 0 advertencias, 0 errores | **0 advertencias, 0 errores** (`TreatWarningsAsErrors=true`) | ✔ **Código limpio** |

> [!NOTE]
> Para consultar la matriz exhaustiva de casos de prueba de sistema (1080p/1440p/4K, DPI mixto, desconexión de pantallas, suspensión/reanudación y reinicio de la Shell de Windows), consulta [`docs/pruebas.md`](./docs/pruebas.md).
