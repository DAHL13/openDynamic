# openDynamic

[![CI](https://github.com/DAHL13/openDynamic/actions/workflows/ci.yml/badge.svg)](https://github.com/DAHL13/openDynamic/actions/workflows/ci.yml)
[![Release](https://github.com/DAHL13/openDynamic/actions/workflows/release.yml/badge.svg)](https://github.com/DAHL13/openDynamic/actions/workflows/release.yml)
[![Version](https://img.shields.io/badge/Version-v2.0.0-blue.svg)](./docs/release-notes-2.0.0.md)
[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/download)
[![Windows](https://img.shields.io/badge/Windows-10%20%7C%2011-0078D6?logo=windows)](https://www.microsoft.com/windows)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](./LICENSE)
[![Platform](https://img.shields.io/badge/Platform-win--x64-lightgrey)]()
[![Private Memory](https://img.shields.io/badge/Private%20RAM-~42%20MB%20(%3C80%20MB)-brightgreen)]()
[![CPU](https://img.shields.io/badge/CPU%20Idle-0.00%25-brightgreen)]()

> **Dynamic Island / Upper Notch para Windows (WPF, .NET 10 — v2.0.0)**  
> Una muesca rectangular superior interactiva y contextual (música con color dinámico y espectro FFT, volumen, batería, ahorro de energía, red, periféricos USB/Bluetooth, portapapeles opt-in, privacidad de cámara/micrófono, capturas de pantalla, hardware, hasta 5 temporizadores, cronómetro y reloj ambiental) anclada al marco superior de la pantalla, con animaciones de física de resortes elásticos y consumo ultra bajo de recursos (**0.00% CPU en reposo**).

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

## Estado del Proyecto (Lanzamiento Mayor v2.0.0)

| Fase | Descripción | Estado |
|---|---|:---:|
| **Fase 0** | **Andamiaje del proyecto, CI, DI, Logging y Pruebas iniciales** | **Completada** |
| **Fase 1** | **Ventana de la isla, posicionamiento y renderizado básico (M1)** | **Completada** |
| **Fase 2** | **Motor de física de animación de resorte (*Spring physics*) y máquina de estados (M2)** | **Completada** |
| **Fase 3** | **Arquitectura de widgets, resolución de prioridades y Orchestrator (M4 Base)** | **Completada** |
| **Fase 4** | **Widget multimedia GSMTC (Windows.Media.Control, 0% CPU, Freeze thumbnails, 10s Grace)** | **Completada** |
| **Fase 5** | **Volumen (NAudio.Wasapi/CoreAudio), batería sin polling (WM_POWERBROADCAST) y pantalla completa (M3)** | **Completada** |
| **Fase 6** | **Hardware (GetSystemTimes/GlobalMemoryStatusEx), Temporizador/Pomodoro y Modo Split con intercambio (M4)** | **Completada** |
| **Fase 7** | **Bandeja del sistema (H.NotifyIcon), Ajustes (MVVM), Atajos Win32 e Inicio automático (M5)** | **Completada** |
| **Fase 8** | **Optimización, robustez, eventos del sistema y pruebas de rendimiento (Previo a M6)** | **Completada** |
| **Notch UI** | **Rediseño geométrico a muesca rectangular superior con esquinas asimétricas (ADR-017)** | **Completada** |
| **Fase 9** | **Empaquetado Inno Setup, decisión de runtime (ADR-016), CI/CD y documentación técnica (v1.0.0)** | **Completada** |
| **Fase 10** | **Accesibilidad integral, perfiles de movimiento, alto contraste y UI Automation** | **Completada** |
| **Fase 11** | **Red (Wi-Fi/Ethernet) y dispositivos periféricos USB/Bluetooth reactivos** | **Completada** |
| **Fase 12** | **Cronómetro de precisión y múltiples temporizadores con preajustes y cola de alertas** | **Completada** |
| **Fase 13** | **Color de carátula dinámico y gestos táctiles/ratón en el widget multimedia** | **Completada** |
| **Fase 14** | **Portapapeles reciente y seguro en memoria RAM (Opt-in, privacidad, 0% leak)** | **Completada** |
| **Fase 15** | **Indicador de micrófono y cámara en uso (ConsentStore pasivo, cero polling, notch UI)** | **Completada** |
| **Fase 16** | **Visualizador de audio real (espectro FFT propia, WASAPI loopback, 0 heap alloc, <2% CPU)** | **Completada** |
| **Fase 19** | **Reloj ambiental en reposo (OnHover 120x4 DIP, 0% CPU idle, WM_TIMECHANGE, alineación al minuto)** | **Completada** |
| **Fase 20** | **Ahorro de energía reactivo (WNF + Win32, ResourceProfile en Core, 0% polling)** | **Completada** |
| **Fase 21** | **Vista previa de capturas de pantalla (FileSystemWatcher reactivo, Drag & Drop Copy, 0% bloqueo)** | **Completada** |
| **Auditoría v2.0.0** | **Auditoría integral de fin a fin (30 hallazgos resueltos, 0 timers en reposo, 619 pruebas, ADR-031)** | **Completada** |

---

## Vista Previa de Capturas de Pantalla

openDynamic detecta de forma instantánea las nuevas capturas de pantalla guardadas en el sistema mediante vigilancia reactiva por eventos de sistema de archivos (`FileSystemWatcher`), con cero sondeo periódico (0% CPU en reposo):

- **Vigilancia Reactiva de Carpeta KnownFolder (`ScreenshotWatcherService`):**
  - Resuelve dinámicamente la carpeta oficial del sistema (`FOLDERID_Screenshots` vía `SHGetKnownFolderPath`) y admite una carpeta adicional opcional configurada por el usuario.
  - Cuando el widget se desactiva en Ajustes o el directorio no existe, el `FileSystemWatcher` se detiene y destruye (`Dispose()`) por completo.
  - Nota sobre atajos de Windows: `Win + PrtScn` guarda directamente en disco y dispara la vista previa al instante; `Win + Shift + S` copia por defecto al portapapeles (cubierto por la Fase 14) y solo genera archivo si la Herramienta Recortes está configurada para guardar capturas automáticamente.
- **Cero Bloqueo de Archivo y Estabilidad de Escritura (`FileStabilityPolicy`):**
  - Espera de forma asíncrona a que el archivo termine de escribirse en disco (tamaño estable durante 300 ms y apertura compartida disponible) sin bloquear el hilo de interfaz.
  - Decodifica la miniatura (`DecodePixelWidth = 320`, `BitmapCacheOption.OnLoad`, `Freeze()`) desde un flujo en memoria y cierra el archivo inmediatamente para que el usuario pueda moverlo, renombrarlo o borrarlo desde el Explorador en cualquier momento. Al cerrarse el aviso, la referencia a la miniatura se libera de RAM.
- **Acciones Rápidas, Drag & Drop Seguro y Papelera:**
  - **Arrastrar y Soltar (`DataFormats.FileDrop`):** Permite arrastrar la miniatura hacia cualquier aplicación (navegador, correo, chat, editor) utilizando estrictamente `DragDropEffects.Copy` (nunca `Move`) para evitar pérdida accidental del archivo original.
  - **Acciones en Modo Expandido:** Copiar imagen al portapapeles (con supresión de eco hacia el historial de portapapeles de la Fase 14), copiar ruta, abrir archivo, mostrar en carpeta (`explorer.exe /select`) y enviar a la Papelera de reciclaje mediante `SHFileOperationW` (`FOF_ALLOWUNDO`) con **doble confirmación** visual.
  - **Privacidad (Regla de Oro 10):** El historial reciente (máximo 5 elementos) vive únicamente en RAM y los registros de diagnóstico (`Serilog`) jamás incluyen rutas, nombres de archivo, nombres de usuario ni contenido de imagen.

---

## Ahorro de Energía y Perfil de Recursos Inteligente

openDynamic monitorea el estado de **Ahorro de Batería de Windows** de forma estrictamente reactiva mediante la API nativa de WinRT (`Windows.System.Power.PowerManager.EnergySaverStatusChanged`), con cero bucles de sondeo (0% CPU adicional en reposo):

- **Aviso Discreto en la Muesca (`EnergySaverWidget`):**
  - Notificación transitoria de 3 segundos en modo compacto con acentos color esmeralda cuando Windows activa o desactiva el ahorro de energía.
  - Supresión automática en el arranque del sistema y ventana de gracia de 10 segundos tras reanudar de suspensión o hibernación, evitando falsos avisos por lecturas transitorias de ACPI.
  - Cooldown de 5 segundos para prevenir saturación de avisos ante cambios rápidos.
- **Perfil de Recursos Adaptativo (`ResourceProfile`):**
  - **Física de Resortes:** Conmuta automáticamente los resortes elásticos a amortiguamiento directo (`Reduced`) si el modo de movimiento está en `Auto`, reduciendo cuadros innecesarios. Respeta de forma prioritaria la configuración explícita del usuario (`Full`).
  - **Espectro de Audio:** Suspende la captura de audio en bucle loopback y el procesamiento FFT pesado conmutando el visualizador a modo `Simulated`, liberando ciclos de CPU.
  - **Telemetría de Hardware:** Espacia automáticamente el intervalo de muestreo de CPU, GPU y RAM de 2.0 s a 5.0 s (o el valor personalizado por el usuario).
- **Compatibilidad Total:** Detecta entornos de escritorio sin batería (`NotSupported`) de forma silenciosa sin arrojar excepciones ni degradar el funcionamiento de la aplicación.

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

## Rendimiento y Mediciones Reales (Release v2.0.0)

En estricto cumplimiento de la **Regla de Oro 1** (0% CPU en reposo y consumo mínimo de memoria), se auditaron y registraron las siguientes métricas reales sobre el binario compilado en `Release` (Windows 11 Pro 24H2, Ryzen 7 5700G, .NET 10):

| Parámetro / Métrica | Meta Técnica | Medición Real Obtenida | Estado |
|---|---|---|:---:|
| **Memoria Privada Comprometida (`PrivateMemorySize64`)** | < 80 MB | **~41.8 MB** (Working Set compartido D3D/WPF: ~158 MB) | ✔ **Superada ampliamente** |
| **Consumo de CPU en Reposo (5 min, estado `Hidden`)** | < 0.5% (ideal 0.0%) | **0.00%** (0 timers activos en reposo) | ✔ **Cumplida estrictamente** |
| **Consumo de CPU durante Animación de Resorte** | < 5.0% | **< 1.0%** (pico transitorio) | ✔ **Fluido a 60-144 FPS** |
| **Visualizador de Espectro FFT (Carga de CPU activa)** | < 2.0% | **~0.09% - 1.0%** (búferes fijos preasignados) | ✔ **Presupuesto cumplido** |
| **Visualizador de Espectro en Pausa / Reposo** | 0.0% CPU | **0.00%** (captura y bucle de render desuscritos) | ✔ **0% en reposo absoluto** |
| **Asignaciones de Memoria en Cuadro de Audio** | 0 B / frame | **0 B** (búferes fijos preasignados y MemoryMarshal) | ✔ **Cero presión de GC** |
| **Suscripción al Bucle de Composición WPF** | Desuscrito en reposo | **0 suscripciones** a `CompositionTarget.Rendering` al asentarse | ✔ **Cero bucles ocultos** |
| **Compilación Estricta y Suite de Pruebas** | 0 advertencias, 100% tests | **0 advertencias, 619/619 pruebas en verde** | ✔ **Código auditado** |

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

- **Activación por Sobrevuelo (`ActivationMode.OnHover`):** Al acercar el cursor a la franja sensora superior (`120x4 DIP` centrada en el borde superior) cuando la isla se encuentra en reposo (`Hidden`), se despliega suavemente mostrando la hora local, día de la semana y fecha completa.
- **Presupuesto Estricto de Rendimiento (0% CPU y 0 Timers en Reposo):** No existe ningún temporizador corriendo en estado `Hidden`. El `DispatcherTimer` del reloj solo se reserva y ejecuta mientras el reloj está visible en pantalla; se detiene y destruye de forma instantánea al ocultarse la muesca.
- **Alineación de Precisión al Minuto:** Sincronizado para disparar su primer tick exactamente en el segundo `:00.000` del minuto entrante, evitando ciclos de reloj innecesarios por segundo cuando los segundos no están habilitados.
- **Sincronización Reactiva Win32 (`WM_TIMECHANGE`):** Escucha mensajes nativos del sistema y `SystemEvents.TimeChanged` para actualizarse de inmediato ante cambios manuales de hora o ajustes de zona horaria sin incurrir en bucles de sondeo continuo (cero polling).
- **Formatos y Localización Cultural:** Respeta la configuración regional de Windows (`CultureInfo.CurrentCulture`) con soporte configurable para 12 horas (AM/PM), 24 horas, visualización opcional de segundos y número de semana según el estándar ISO 8601.

---

## Documentación Técnica y Gobernanza

- **[Notas de Lanzamiento v2.0.0 (`docs/release-notes-2.0.0.md`)](./docs/release-notes-2.0.0.md):** Resumen ejecutivo de las 10 nuevas fases, mejoras de rendimiento y guía de actualización desde `v1.0.0`.
- **[Informe de Auditoría Integral v2.0.0 (`docs/auditoria-v2.0.0.md`)](./docs/auditoria-v2.0.0.md):** Auditoría de extremo a extremo (Pasadas A y B), resolución de los 30 hallazgos (`AUD-001` a `AUD-030`) y mediciones reales Antes vs. Después.
- **[Arquitectura y Guía para Desarrolladores (`docs/arquitectura.md`)](./docs/arquitectura.md):** Diagramas conceptuales de capas (Core vs. App), los 12 widgets del sistema, flujo del `IslandOrchestrator`, ciclo de vida de la FSM y la **Guía de 10 pasos** para crear e integrar nuevos widgets desde cero.
- **[Registro de Decisiones de Arquitectura (`DECISIONS.md`)](./DECISIONS.md):** Registro histórico y justificación de las 31 decisiones técnicas (ADR-001 a ADR-031).
- **[Matriz de Validación y Pruebas (`docs/pruebas.md`)](./docs/pruebas.md):** 619 pruebas unitarias automatizadas y protocolo de pruebas manuales de sistema (`[MANUAL]`).
- **[Gobernanza y Seguridad](./SECURITY.md):** [`SECURITY.md`](./SECURITY.md), [`CONTRIBUTING.md`](./CONTRIBUTING.md) y [`THIRD-PARTY-NOTICES.md`](./THIRD-PARTY-NOTICES.md).

---

## Stack Tecnológico

- **Lenguaje / Runtime:** C# 13+, .NET 10
- **Interfaz de Usuario:** WPF (`net10.0-windows10.0.19041.0`) con soporte `PerMonitorV2` DPI y UI Automation
- **Patrón Arquitectónico:** MVVM mediante `CommunityToolkit.Mvvm` (`8.4.2`)
- **Inyección de Dependencias:** `Microsoft.Extensions.DependencyInjection` (`10.0.12`)
- **Audio:** `NAudio.Wasapi` (`2.3.0` — `MMDeviceEnumerator`, `AudioEndpointVolume`, `WasapiLoopbackCapture`), FFT Cooley-Tukey Radix-2 pura en Core (cero dependencias externas)
- **Multimedia:** WinRT `Windows.Media.Control` (GSMTC) con extrapolación continua y miniaturas congeladas
- **Bandeja del Sistema (Tray):** `H.NotifyIcon.Wpf` (`2.4.1`, cero WinForms)
- **Atajos Globales:** Win32 `RegisterHotKey` / `UnregisterHotKey` mediante WndProc
- **Configuración y Persistencia:** `System.Text.Json` en `%AppData%\openDynamic\settings.json` (esquema v14 con migración automática, `SanitizeAndClamp` y debounce de 500ms)
- **Registro de Eventos (Logging):** `Serilog` (`4.4.0`) y `Serilog.Sinks.File` (`7.0.0`) en `%LocalAppData%\openDynamic\logs` (nivel `Information` en Release con redacción de datos personales)
- **Pruebas Unitarias:** `xUnit` (`2.9.2`, 619 pruebas)
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

