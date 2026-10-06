# Guía de Contribución (Contributing to openDynamic)

¡Gracias por tu interés en contribuir a **openDynamic**! Este documento describe los requisitos del entorno, la arquitectura del proyecto, las Reglas de Oro de ingeniería y el flujo de trabajo para enviar mejoras o correcciones.

---

## 1. Requisitos del Entorno de Desarrollo

- **Sistema Operativo:** Windows 10 (compilación 19041 / 2004 o superior) o Windows 11 (x64).
- **SDK:** [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (`10.0.100` o superior).
- **Editor / IDE recomendado:** Visual Studio 2022/2026 (con carga de trabajo de escritorio .NET) o VS Code con C# Dev Kit.
- **Opcional (para compilar el instalador):** [Inno Setup 6](https://jrsoftware.org/isinfo.php).

---

## 2. Estructura de la Solución y Separación de Capas

La solución `openDynamic.sln` se divide en tres proyectos con responsabilidades estrictamente delimitadas:

- **`src/OpenDynamic.Core` (`net10.0`):**
  - Dominio puro y multiplataforma (física de resortes `SpringSolver`, máquina de estados `IslandStateMachine`, resolución de prioridades `PriorityResolver`, FFT Cooley-Tukey `FftProcessor`, políticas de alertas, temporizadores, cronómetro y persistencia de configuración).
  - **Restricción absoluta:** `OpenDynamic.Core` **no puede** referenciar WPF (`System.Windows.*`), WinForms, WinRT ni realizar llamadas `P/Invoke` a DLLs nativas de Windows.
- **`src/OpenDynamic.App` (`net10.0-windows10.0.19041.0`):**
  - Capa de presentación WPF (Upper Notch UI), orquestación visual (`IslandWindow`), vistas XAML y adaptadores de servicios del sistema operativo (GSMTC, WASAPI, Win32, WNF).
- **`tests/OpenDynamic.Tests` (`net10.0`):**
  - Suite de pruebas unitarias deterministas en `xUnit`. Todas las dependencias temporales utilizan `System.TimeProvider` inyectado (`FakeTimeProvider`) sin `Thread.Sleep` ni `Task.Delay` arbitrarios.

---

## 3. Reglas de Oro Innegociables

Cualquier Pull Request debe respetar las siguientes reglas técnicas:

1. **0.0% de CPU en Reposo:** Queda prohibido cualquier bucle de sondeo (*polling*) continuo cuando la isla está en estado `Hidden`. Los temporizadores de interfaz (`DispatcherTimer`) y el bucle `CompositionTarget.Rendering` deben detenerse y desuscribirse en cuanto la isla se oculta o la animación converge.
2. **Cero Bloqueos en el Hilo de UI:** Ninguna llamada costosa (E/S de disco, inicialización/lectura de contadores PDH, consultas COM/WinRT o decodificación de imágenes) puede ejecutarse de forma síncrona en el hilo de despacho de WPF.
3. **Cero Privilegios de Administrador:** Todas las integraciones deben funcionar bajo `asInvoker` y `HKEY_CURRENT_USER`.
4. **Privacidad Estricta en Logs:** Jamás registres en `Serilog` contenido del portapapeles, rutas completas con nombre de usuario, nombres de archivos de capturas, SSIDs de Wi-Fi, títulos de canciones o nombres de periféricos.
5. **Cero Advertencias de Compilación:** La solución tiene `TreatWarningsAsErrors=true` en `Directory.Build.props`. Todo código nuevo debe compilar con **0 advertencias**.

---

## 4. Comandos de Verificación Local

Antes de abrir un Pull Request, ejecuta los siguientes comandos desde la raíz del repositorio y asegúrate de que todos finalicen con código `0`:

```powershell
# 1. Restaurar paquetes
dotnet restore openDynamic.sln

# 2. Verificar formato y estilo (.editorconfig)
dotnet format openDynamic.sln --verify-no-changes

# 3. Compilar en modo Release (0 advertencias, 0 errores)
dotnet build openDynamic.sln -c Release --no-restore

# 4. Ejecutar la suite completa de pruebas unitarias
dotnet test openDynamic.sln -c Release --no-build

# 5. Ejecutar prueba de humo de arranque y apagado limpio
dotnet run --project src/OpenDynamic.App/OpenDynamic.App.csproj -c Release -- --smoke-test
```

---

## 5. Convenciones de Commits y Pull Requests

Utilizamos **Conventional Commits** en inglés para mantener un historial claro:

- `feat(scope): ...` — Nueva funcionalidad o widget.
- `fix(scope): ...` — Corrección de errores, fugas de recursos o condiciones de carrera.
- `perf(scope): ...` — Optimizaciones de CPU, memoria o renderizado.
- `refactor(scope): ...` — Reestructuración interna sin cambio de comportamiento.
- `test(scope): ...` — Adición o mejora de pruebas unitarias.
- `docs(scope): ...` — Cambios en documentación o ADRs (`DECISIONS.md`).
- `chore(scope): ...` — Cambios en CI/CD, empaquetado o dependencias.

Si tu cambio introduce una decisión arquitectónica relevante, documéntala como un nuevo ADR en [`DECISIONS.md`](./DECISIONS.md) y actualiza [`CHANGELOG.md`](./CHANGELOG.md).
