# openDynamic

[![CI](https://github.com/DAHL13/openDynamic/actions/workflows/ci.yml/badge.svg)](https://github.com/DAHL13/openDynamic/actions/workflows/ci.yml)
[![Release](https://github.com/DAHL13/openDynamic/actions/workflows/release.yml/badge.svg)](https://github.com/DAHL13/openDynamic/actions/workflows/release.yml)
[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/download)
[![Windows](https://img.shields.io/badge/Windows-10%20%7C%2011-0078D6?logo=windows)](https://www.microsoft.com/windows)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](./LICENSE)
[![Platform](https://img.shields.io/badge/Platform-win--x64-lightgrey)]()
[![RAM](https://img.shields.io/badge/RAM-%3C%2030%20MB-brightgreen)]()
[![CPU](https://img.shields.io/badge/CPU%20Idle-0.0%25-brightgreen)]()

> **Dynamic Island / Upper Notch para Windows (WPF, .NET 10)**  
> Una muesca rectangular superior interactiva y contextual (música, volumen, batería, hardware, temporizador, reloj ambiental) anclada al marco superior de la pantalla, con animaciones de física de resortes elásticos y consumo ultra bajo de recursos.

Repositorio oficial: [https://github.com/DAHL13/openDynamic](https://github.com/DAHL13/openDynamic)

---

## Rediseño Visual: Upper Notch UI

openDynamic adopta una estética de **muesca rectangular superior (Notch)** pegada directamente al marco superior de la pantalla (`OffsetY = 0.0 DIP`), con esquinas superiores ortogonales a 0 DIP y esquinas inferiores suavemente curvadas (14–16 DIP):

```
┌────────────────────────────────────────────────────────────────────────┐  ◄── Bisel Superior de la Pantalla
│                        ┌────────────────────┐                          │
│                        │  🎵 Bohemian Rhap  │                          │  ◄── Modo Compacto (200x36 DIP)
│                        └─┬────────────────┬─┘                          │      Esquinas inferiores: 14 DIP
│                          └────────────────┘                            │
└────────────────────────────────────────────────────────────────────────┘

┌────────────────────────────────────────────────────────────────────────┐  ◄── Bisel Superior
│                   ┌──────────────────────┐  ┌──────┐                   │
│                   │  ⏱️ 24:59 (Pomodoro)  │  │  🎵  │                   │  ◄── Modo Split Multitasking (280 DIP)
│                   └─┬──────────────────┬─┘  └─┬──┬─┘                   │      Intercambio interactivo al clic
└─────────────────────┴──────────────────┴──────┴──┴─────────────────────┘

┌────────────────────────────────────────────────────────────────────────┐  ◄── Bisel Superior
│                   ┌────────────────────────────────┐                   │
│                   │ 🎵 Queen - A Night at the Opera │                   │
│                   │ ━━━━━━━━━━━●━━━━━━━━━━━━━━━━━━ │                   │  ◄── Modo Expandido (400x160 DIP)
│                   │      ⏮️      ⏸️      ⏭️         │                   │      Esquinas inferiores: 16 DIP
│                   └─┬────────────────────────────┬─┘                   │
└─────────────────────┴────────────────────────────┴─────────────────────┘
```

> 📷 *Capturas de interfaz y demostraciones visuales disponibles en [`docs/img/`](./docs/).*

---

## Estado del Proyecto (Hito M6 - Release Beta v1.0.0)

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
| **Notch UI** | **Rediseño geométrico a muesca rectangular superior con esquinas asimétricas (ADR-017)** | **Completada** |
| **Fase 9** | **Empaquetado Inno Setup, decisión de runtime (ADR-016), CI/CD y documentación técnica (M6)** | **Completada** |
| **Fase 10** | **Accesibilidad integral, perfiles de movimiento, alto contraste y UI Automation (v1.1)** | **Completada** |
| **Fase 11** | **Red (Wi-Fi/Ethernet) y dispositivos periféricos USB/Bluetooth reactivos (v1.1)** | **Completada** |
| **Fase 12** | **Cronómetro de precisión y múltiples temporizadores con preajustes y cola de alertas (v1.1)** | **Completada** |
| **Fase 13** | **Color de carátula dinámico y gestos táctiles/ratón en el widget multimedia (v1.1)** | **Completada** |
| **Fase 14** | **Portapapeles reciente y seguro en memoria RAM (Opt-in, privacidad, 0% leak) (v1.1)** | **Completada** |
| **Fase 15** | **Indicador de micrófono y cámara en uso (ConsentStore pasivo, cero polling, notch UI) (v1.1)** | **Completada** |
| **Fase 16** | **Visualizador de audio real (espectro FFT propia, WASAPI loopback, 0 heap alloc, <2% CPU) (v1.1)** | **Completada** |
| **Fase 19** | **Reloj ambiental en reposo (OnHover, 0% CPU idle, WM_TIMECHANGE, alineación al minuto) (v1.4)** | **Completada** |

---

## Accesibilidad y Preferencias de Movimiento

openDynamic está diseñado desde sus cimientos cumpliendo con las directrices de accesibilidad **WCAG 2.1** (Criterios 2.2.2 y 2.3.3) y las guías de diseño de Windows 11:

- **Perfiles de Movimiento Adaptativos (`MotionMode`):**
  - **Automático (`Auto` - Por defecto):** Sigue la preferencia de accesibilidad de Windows (*Configuración > Accesibilidad > Efectos visuales > Efectos de animación*). Detectado 100% de forma reactiva mediante `WM_SETTINGCHANGE` (sin polling, 0% CPU).
  - **Reducidas (`Reduced`):** Diseñado para usuarios con sensibilidad al movimiento o trastornos vestibulares. Amortiguamiento crítico ($\zeta = 1.0$) sin sobreimpulso (overshoot 0.0%), sin rebotes elásticos, cross-fades directos de 120 ms ($\le 150\text{ ms}$) y supresión automática de animaciones decorativas (como las barras oscilantes del ecualizador).
  - **Completas (`Full`):** Física elástica de resortes subamortiguados con rebote suave natural de ~4%.
- **Soporte de Alto Contraste del Sistema:** Detección instantánea de `SystemParameters.HighContrast`. Aplica dinámicamente pinceles de alto contraste (`SystemColors.*Key`), fuerza un borde sólido visible de 1 DIP alrededor de la muesca y fondo negro opaco para garantizar legibilidad infinita sobre cualquier ventana o fondo de pantalla.
- **Lectores de Pantalla y UI Automation:** Soporte nativo para el Narrador de Windows mediante propiedades estructuradas (`AutomationProperties.Name`, `AutomationProperties.HelpText`) y notificaciones dinámicas (`AutomationProperties.LiveSetting="Polite"`) en la cápsula y todos los widgets interactivos.
- **Interacción sin Robo de Foco:** La ventana utiliza `WS_EX_NOACTIVATE` y `WM_MOUSEACTIVATE`, permitiendo interactuar con los widgets sin quitar el foco del teclado de la aplicación activa.

---

## Requisitos del Sistema

- **Sistema Operativo:** Windows 10 (versión 2004 / compilación 19041 o superior) o Windows 11.
- **Arquitectura:** x64 (64 bits).
- **Runtime:** [Microsoft .NET 10 Desktop Runtime (x64)](https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x64.exe) instalado en el sistema.
- **Para Compilar desde el Código Fuente:** [.NET 10 SDK](https://dotnet.microsoft.com/download).

---

## Guía de Instalación

### Opción 1: Instalador Oficial de Windows (Recomendado)
1. Descarga el instalador oficial `openDynamic-setup.exe` desde [Releases](https://github.com/DAHL13/openDynamic/releases).
2. Ejecuta el archivo.
   - **Cero permisos de Administrador (UAC lowest):** Se instala de forma segura y aislada en `%LocalAppData%\Programs\openDynamic`.
   - Si no cuentas con .NET 10 Desktop Runtime, el instalador lo detecta automáticamente y te ofrece descargarlo en un clic.
   - Puedes activar accesos directos e inicio automático con Windows.
3. Al desinstalar, el desinstalador purga las claves de registro de autostart y te consulta si deseas conservar o eliminar tu archivo de configuración (`%AppData%\openDynamic\settings.json`).

### Opción 2: Paquete Portátil (.ZIP)
1. Descarga `openDynamic-portable-win-x64.zip` desde [Releases](https://github.com/DAHL13/openDynamic/releases).
2. Descomprime el archivo en la carpeta que desees.
3. Ejecuta `OpenDynamic.App.exe`. No requiere instalación previa.

---

## Atajos de Teclado e Interacción

| Acción | Control / Atajo | Descripción |
|---|---|---|
| **Mostrar / Ocultar Notch** | `Win + Ctrl + I` | Atajo global configurable mediante la API nativa `RegisterHotKey` (sin hooks globales). |
| **Control Rápido de Volumen** | **Rueda del ratón** sobre el Notch | Gira la rueda hacia arriba/abajo sobre la muesca para ajustar el volumen maestro. |
| **Pista Anterior / Siguiente (Rueda)** | **Rueda inclinable / Touchpad horizontal** (`WM_MOUSEHWHEEL`) | Desplazamiento horizontal sobre la cápsula de música con cooldown de 400ms para evitar doble salto. |
| **Pista Anterior / Siguiente (Arrastre)** | **Arrastre horizontal** sobre cabecera en modo expandido | Desliza con el ratón o táctil a izquierda/derecha con amortiguación y retorno de resorte visual. |
| **Expandir Actividad** | **Clic izquierdo** o **Hover** (>250ms) | Despliega los controles completos e información extendida (400x160 DIP). |
| **Colapsar Actividad** | **Clic fuera** o **Hover leave** (>350ms) | Regresa fluidamente al tamaño compacto o split mediante animación de resortes. |
| **Intercambiar en Modo Split** | **Clic en la burbuja satélite** | Intercambia inmediatamente la actividad principal y la secundaria. |
| **Menú de la Bandeja** | **Clic derecho en icono del Tray** | Acceso a Ajustes, monitor de hardware, reinicio de posición y salida limpia. |

---

## Rendimiento y Mediciones Reales

En estricto cumplimiento de la **Regla de Oro 1** (0% CPU en reposo y consumo mínimo de memoria), se auditaron y registraron las siguientes métricas de rendimiento reales en el entorno de desarrollo:

| Parámetro / Métrica | Meta Técnica | Medición Real Obtenida | Estado |
|---|---|---|:---:|
| **Consumo de RAM en Reposo (Working Set)** | < 100 MB | **27.9 MB** | ✔ **Superada ampliamente** |
| **Memoria Privada Comprometida** | < 25 MB | **5.2 MB** | ✔ **Excelente** |
| **Consumo de CPU en Reposo (sin actividad visible)** | < 0.5% (ideal 0.0%) | **0.00%** | ✔ **Cumplida estrictamente** |
| **Consumo de CPU durante Animación de Resorte** | < 5.0% | **< 1.0%** (pico transitorio) | ✔ **Fluido a 60-144 FPS** |
| **Visualizador de Espectro FFT (Carga de CPU activa)** | < 2.0% | **~1.0%** (50 ms CPU por cada 5 s de audio 48 kHz) | ✔ **Presupuesto cumplido** |
| **Visualizador de Espectro en Pausa / Reposo** | 0.0% CPU | **0.00%** (captura y bucle de render desuscritos) | ✔ **0% en reposo absoluto** |
| **Asignaciones de Memoria en Cuadro de Audio** | 0 B / frame | **0 B** (búferes fijos preasignados y MemoryMarshal) | ✔ **Cero presión de GC** |
| **Suscripción al Bucle de Composición WPF** | Desuscrito en reposo | **0 suscripciones** a `CompositionTarget.Rendering` al asentarse | ✔ **Cero bucles ocultos** |
| **Compilación Estricta** | 0 advertencias, 0 errores | **0 advertencias, 0 errores** (`TreatWarningsAsErrors=true`) | ✔ **Código limpio** |

---

## Visualizador de Audio Reactivo y Privacidad

El widget multimedia incorpora un analizador de espectro reactivo en tiempo real integrado directamente en la muesca Notch:

- **Modos Configurables (`AudioVisualizerMode`):**
  - **Deshabilitado (`Disabled`):** Muestra el icono estático de ecualizador sin procesamiento de audio ni animación.
  - **Simulado (`Simulated`):** Ondas sinusoidales matemáticas generadas localmente a ~30 FPS para equipos con audio de baja latencia o sin permisos WASAPI.
  - **Reactivo Real (`Real` - Predeterminado cuando reactivo está activo):** Captura en búfer circular de bucle invertido (`WasapiLoopbackCapture`) con descomposición armónica en tiempo real.
- **FFT Pura de Cero Dependencias y Cero Asignaciones:**
  - Implementación matemática propia de **Cooley-Tukey Radix-2** iterativa en `OpenDynamic.Core` con ventana de Hann y solapamiento del 50%. Cero librerías externas de procesamiento digital de señales.
  - Cero asignaciones en memoria dinámica (*0 per-frame heap allocations*): todos los búferes (`float[]`, `Complex[]`) son preasignados y reutilizados de forma estricta.
  - Descomposición en **12 bandas logarítmicas** (modo Compacto) y **24 bandas logarítmicas** (modo Expandido) con suavizado temporal asimétrico (*fast attack* 0.65 / *slow decay* 0.85).
- **Política de Activación Estricta (`VisualizerActivationPolicy`):**
  - La captura WASAPI y el bucle de dibujo a ~30 FPS se activan **única y exclusivamente** si:
    1. Hay reproducción multimedia activa confirmada por GSMTC (`IsMediaPlaying == true`).
    2. El widget multimedia está visible en la isla.
    3. La isla no está oculta (`IslandState != Hidden`).
    4. No hay una aplicación en pantalla completa exclusiva activa.
  - En cuanto la música se pausa, se cambia de pista o se oculta la isla, la captura se detiene de inmediato, desuscribiéndose del compositor WPF y volviendo instantáneamente a **0% de CPU**.
- **Privacidad y Seguridad Absoluta (Regla de Oro 10):**
  - El búfer de audio PCM se procesa **exclusiva y transitoriamente en memoria RAM**.
  - Queda prohibida y anulada cualquier persistencia en disco, generación de archivos temporales o registro de amplitudes y frecuencias en registros de Serilog.
- **Resiliencia de Hardware de Audio:**
  - Tolerancia a errores COM WASAPI con degradación elegante automática al modo Simulado.
  - Reconexión en caliente inmediata ante cambios en el dispositivo de salida predeterminado mediante notificaciones de `IMMNotificationClient`.

---

## Reloj Ambiental en Reposo (Upper Notch UI)

openDynamic incorpora un widget de reloj ambiental diseñado para consultar la hora y fecha de manera no intrusiva al interactuar con la Dynamic Island cuando no hay actividades prioritarias en curso:

- **Activación por Sobrevuelo (`ActivationMode.OnHover`):** Al pasar el cursor sobre la muesca cuando la isla se encuentra en reposo (`Hidden`), se despliega suavemente mostrando la hora local, día de la semana y fecha completa.
- **Presupuesto Estricto de Rendimiento (0% CPU en Reposo):** El temporizador de actualización interno (`DispatcherTimer`) solo se reserva y ejecuta mientras la isla se mantiene visible en pantalla; se detiene y destruye de forma instantánea al ocultarse la muesca.
- **Alineación de Precisión al Minuto:** Sincronizado para disparar su primer tick exactamente en el segundo `:00.000` del minuto entrante, evitando ciclos de reloj innecesarios por segundo cuando los segundos no están habilitados.
- **Sincronización Reactiva Win32 (`WM_TIMECHANGE`):** Escucha mensajes nativos del sistema y `SystemEvents.TimeChanged` para actualizarse de inmediato ante cambios manuales de hora o ajustes de zona horaria sin incurrir en bucles de sondeo continuo (cero polling).
- **Formatos y Localización Cultural:** Respeta la configuración regional de Windows (`CultureInfo.CurrentCulture`) con soporte configurable para 12 horas (AM/PM), 24 horas, visualización opcional de segundos y número de semana según el estándar ISO 8601.

---

## Documentación Técnica

- **[Arquitectura y Guía para Desarrolladores (`docs/arquitectura.md`)](./docs/arquitectura.md):** Diagramas conceptuales de capas (Core vs. App), flujo del `IslandOrchestrator`, ciclo de vida de la FSM y la **Guía de 10 pasos** para crear e integrar nuevos widgets desde cero.
- **[Registro de Decisiones de Arquitectura (`DECISIONS.md`)](./DECISIONS.md):** Registro histórico y justificación de las 28 decisiones técnicas (ADR-001 a ADR-028).
- **[Matriz de Validación y Pruebas (`docs/pruebas.md`)](./docs/pruebas.md):** 426 pruebas unitarias automatizadas y casos de prueba manual de sistema (DPI, multimonitor, suspensión, pantalla completa, accesibilidad, portapapeles, privacidad de cámara/micrófono, espectro de audio, reloj ambiental).

---

## Stack Tecnológico

- **Lenguaje / Runtime:** C# 13+, .NET 10
- **Interfaz de Usuario:** WPF (`net10.0-windows10.0.19041.0`) con soporte `PerMonitorV2` DPI y UI Automation
- **Patrón Arquitectónico:** MVVM mediante `CommunityToolkit.Mvvm`
- **Inyección de Dependencias:** `Microsoft.Extensions.DependencyInjection`
- **Audio:** `NAudio` (`MMDeviceEnumerator`, `AudioEndpointVolume`, `WasapiLoopbackCapture`), FFT Cooley-Tukey Radix-2 pura en Core (cero dependencias externas)
- **Multimedia:** WinRT `Windows.Media.Control` (GSMTC) con extrapolación continua y miniaturas congeladas
- **Bandeja del Sistema (Tray):** `H.NotifyIcon.Wpf` (cero WinForms)
- **Atajos Globales:** Win32 `RegisterHotKey` / `UnregisterHotKey` mediante WndProc
- **Configuración y Persistencia:** `System.Text.Json` en `%AppData%\openDynamic\settings.json` (esquema v9 con migración automática y debounce de 500ms)
- **Registro de Eventos (Logging):** `Serilog` y `Serilog.Sinks.File` en `%LocalAppData%\openDynamic\logs`
- **Pruebas Unitarias:** `xUnit`
- **Instalador:** Inno Setup 6 (distribución ReadyToRun)

---

## Compilación y Pruebas para Desarrolladores

### Restaurar dependencias:
```powershell
dotnet restore openDynamic.sln
```

### Compilar la solución en Release (TreatWarningsAsErrors):
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
dotnet publish src/OpenDynamic.App/OpenDynamic.App.csproj -c Release -r win-x64 -p:PublishReadyToRun=true --self-contained false -o publish
```

---

## Autoría y Créditos

- **Desarrollador Principal y Autor del Proyecto:** (https://github.com/DAHL13)
- **Asistencia de Inteligencia Artificial:** El desarrollo de openDynamic contó con la asistencia de Inteligencia Artificial como copiloto técnico para el diseño arquitectónico, implementación de código y control de calidad (QA).

---

## Licencia

Este proyecto se distribuye bajo los términos de la **[Licencia MIT](./LICENSE)**. Para más detalles sobre permisos, condiciones y limitaciones de responsabilidad, consulta el archivo oficial [`LICENSE`](./LICENSE).

