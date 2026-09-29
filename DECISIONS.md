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
  - **Aislamiento de depuración (#if DEBUG):** La ventana `IslandDebugWindow` y sus puntos de entrada se condicionan estrictamente a `#if DEBUG`, garantizando cero código ni dependencias visuales de depuración en compilaciones Release.


