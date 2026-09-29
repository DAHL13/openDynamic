# openDynamic

> **Dynamic Island para Windows (WPF, .NET 10)**  
> Una cápsula flotante, contextual e interactiva (música, volumen, batería, hardware, temporizador) con animaciones de resorte fluidas y consumo ultra bajo de recursos.

Repositorio oficial: [https://github.com/DAHL13/openDynamic](https://github.com/DAHL13/openDynamic)

---

## Estado del Proyecto

| Fase | Descripción | Estado |
|---|---|:---:|
| **Fase 0** | **Andamiaje del proyecto, CI, DI, Logging y Pruebas iniciales** | **Completada** |
| Fase 1 | Ventana de la isla, posicionamiento y renderizado básico | Pendiente |
| Fase 2 | Motor de física de animación de resorte (*Spring physics*) | Pendiente |
| Fase 3 | Máquina de estados de la isla y resolución de prioridades | Pendiente |
| Fase 4 | Integración de widgets del sistema (Audio, Medios GSMTC, Hardware, Batería, Temporizador) | Pendiente |
| Fase 5 | Interacciones avanzadas y expansión de cápsula (Hover, Gestos, Menú contextual) | Pendiente |
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
│  │   └─ CoreInfo.cs
│  └─ OpenDynamic.App/              # WPF, net10.0-windows10.0.19041.0
│      ├─ app.manifest              # PerMonitorV2 DPI awareness
│      ├─ App.xaml / App.xaml.cs    # Ciclo de vida, DI, manejadores de excepción globales
│      └─ Infrastructure/           # DI, SingleInstance, Logging
└─ tests/
   └─ OpenDynamic.Tests/            # xUnit probando la infraestructura y aislamiento de Core
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
