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
