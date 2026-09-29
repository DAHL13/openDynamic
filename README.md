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
| Fase 6 | Configuración, persistencia y bandeja del sistema (*System Tray*) | Pendiente |
| Fase 7 | Optimización de rendimiento (CPU ~0% en reposo, consumo de RAM) | Pendiente |
| Fase 8 | Empaquetado y distribución (Inno Setup, publicación Release) | Pendiente |

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
│  │   ├─ Windowing/               # Detección geométrica y de estado de pantalla completa pura
│  │   │   └─ FullscreenDetector.cs
│  │   ├─ Settings/                # Configuración de aplicación (Grace periods, umbrales y prioridades)
│  │   │   └─ AppSettings.cs
│  │   └─ Positioning/             # Cálculo puro de posicionamiento geométrico y DPI
│  │       ├─ IslandPositionCalculator.cs
│  │       ├─ MonitorArea.cs
│  │       ├─ DisplayDpi.cs
│  │       ├─ WindowDimensions.cs
│  │       └─ CalculatedWindowPlacement.cs
│  └─ OpenDynamic.App/              # WPF, net10.0-windows10.0.19041.0
│      ├─ app.manifest              # PerMonitorV2 DPI awareness
│      ├─ App.xaml / App.xaml.cs    # Ciclo de vida, DI, manejadores de excepción globales
│      ├─ Native/                   # P/Invoke a Win32 (estilos, DPI, energía, pantalla completa)
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
│      ├─ Services/                 # Servicios nativos: GSMTC, NAudio CoreAudio y Windows Power
│      │   ├─ MediaService.cs
│      │   ├─ WinRtMediaSession.cs
│      │   ├─ VolumeService.cs
│      │   └─ PowerService.cs
│      ├─ Views/                    # Renderizado elástico y soporte visual Split (satélite circular)
│      │   ├─ IslandView.xaml
│      │   └─ IslandView.xaml.cs
│      ├─ Widgets/                  # Contrato base, mensajería, widgets multimedia, volumen y batería
│      │   ├─ IIslandWidget.cs
│      │   ├─ IslandWidgetBase.cs
│      │   ├─ Messages/ActivityMessages.cs
│      │   ├─ Media/                # Widget GSMTC: vistas Compact, Expanded y Split satélite
│      │   │   ├─ MediaWidget.cs
│      │   │   └─ Views/
│      │   ├─ Volume/               # Widget Volumen: vistas Compact, Expanded y Split satélite
│      │   │   ├─ VolumeWidget.cs
│      │   │   └─ Views/
│      │   ├─ Battery/              # Widget Batería: vistas Compact, Expanded y Split satélite
│      │   │   ├─ BatteryWidget.cs
│      │   │   └─ Views/
│      │   └─ Demo/DemoWidgets.cs   # Condicionado a #if DEBUG
│      └─ Infrastructure/           # DI, SingleInstance, Logging
└─ tests/
   └─ OpenDynamic.Tests/            # xUnit probando Core (resortes, FSM, prioridades, posicionamiento)
       ├─ InfrastructureTests.cs
       ├─ Animation/
       │   └─ SpringTests.cs
       ├─ State/
       │   ├─ IslandStateMachineTests.cs
       │   └─ IslandLayoutTests.cs
       ├─ Widgets/
       │   └─ PriorityResolverTests.cs
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
