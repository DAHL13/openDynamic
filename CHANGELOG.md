# Bitácora de Cambios (Changelog) - openDynamic

Todas las modificaciones notables en este proyecto serán documentadas en este archivo.
El formato se basa en [Keep a Changelog](https://keepachangelog.com/es-ES/1.1.0/) y cumple con [SemVer](https://semver.org/).

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
