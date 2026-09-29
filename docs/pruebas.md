# Matriz de Pruebas y Validación de Calidad - openDynamic

> **Proyecto:** openDynamic (https://github.com/DAHL13/openDynamic)  
> **Versión:** 0.8.0+ (Fase 8: Optimización y Pruebas de Rendimiento)  
> **Entorno de Pruebas:** Windows 10/11 (x64), .NET 10 LTS, PerMonitorV2 DPI  
> **Fecha de Validación:** 2026-09-29  

---

## 1. Resumen Ejecutivo y Metodología

La estrategia de aseguramiento de calidad de **openDynamic** se sustenta en tres pilares:

1. **Pruebas Automatizadas Deterministas:** 197 pruebas unitarias sin `Thread.Sleep` ni dependencias gráficas directas en `OpenDynamic.Tests`, ejecutadas con compilación estricta (`TreatWarningsAsErrors=true`).
2. **Auditoría de Recursos y Rendimiento Real:** Verificación empírica de las Reglas de Oro (CPU ~0% en reposo, desuscripción estricta de `CompositionTarget.Rendering`, Working Set < 100 MB y cero fugas en eventos COM/WinRT/Win32).
3. **Matriz de Pruebas de Sistema y Entorno [MANUAL]:** Protocolo exhaustivo para validar la robustez ante eventos del sistema operativo (suspensión, DPI mixto, cambio de monitores, reinicio de shell y temas).

---

## 2. Pruebas Automatizadas (Unitarias y de Integración)

| ID | Componente / Subsistema | Caso de Prueba | Resultado Esperado | Estado |
|---|---|---|---|:---:|
| **UT-01** | `OpenDynamic.Core.Animation` | Sub-stepping y conservación de inercia en `Spring` | Las velocidades iniciales se conservan entre cambios de objetivo; tiempo de convergencia < 600 ms sin oscilaciones infinitas. | **PASA** |
| **UT-02** | `OpenDynamic.Core.State` | Restricción de transiciones en `IslandStateMachine` | Transiciones inválidas (`Hidden -> Expanded`, `Hidden -> Split`) son rechazadas pacíficamente sin excepciones no controladas. | **PASA** |
| **UT-03** | `OpenDynamic.Core.Widgets` | Algoritmo determinista de `PriorityResolver` | Actividad de mayor prioridad domina la cápsula; resolución en modo Split ante dos continuas; alertas transitorias toman control exclusivo. | **PASA** |
| **UT-04** | `OpenDynamic.Core.Hardware` | Cálculo de deltas Win32 en `HardwareCalculator` | Lecturas de `GetSystemTimes` y `GlobalMemoryStatusEx` generan porcentajes exactos (0-100%) sin divisiones por cero. | **PASA** |
| **UT-05** | `OpenDynamic.Core.Timer` | Temporizador por marca de tiempo (`TimerController`) | Cero deriva temporal; cálculo exacto de `TargetEndTimeUtc`; pausa y reanudación sin retraso acumulativo con `TimeProvider`. | **PASA** |
| **UT-06** | `OpenDynamic.Core.Settings` | Respaldo y tolerancia a corrupción (`SettingsService`) | JSON malformado genera `.bak` automático, registra advertencia y restaura configuración predeterminada sin colapso. | **PASA** |
| **UT-07** | `OpenDynamic.Core.Settings` | Escritura debounced (500 ms) | Múltiples cambios consecutivos de propiedades consolidan una única operación de I/O en disco. | **PASA** |
| **UT-08** | `OpenDynamic.Core.Hotkeys` | Parser y normalizador (`HotkeyParser`) | Reconoce combinaciones válidas (`Win+Ctrl+I`), flags modificadores y rechaza combinaciones sin teclas no modificadoras. | **PASA** |
| **UT-09** | `OpenDynamic.Core.Autostart` | Servicio de arranque (`AutostartServiceCore`) | Lectura y validación de claves en registro `HKCU\Run` sin requerir elevación de privilegios UAC. | **PASA** |
| **UT-10** | `OpenDynamic.Core.Positioning` | Geometría en 1080p, 1440p, 4K y DPI escalas 100-200% | Posicionamiento centrado horizontalmente y coordenadas físicas exactas calculadas con `IslandPositionCalculator`. | **PASA** |
| **UT-11** | `OpenDynamic.Core.Positioning` | Multi-monitor con DPI mixto y monitores a la izquierda | Soporta coordenadas virtuales negativas y factores de escala heterogéneos sin recortes ni desalineación. | **PASA** |
| **UT-12** | `OpenDynamic.Core.Positioning` | Fallback seguro ante monitor desconectado | Cuando el monitor destino ya no existe, calcula coordenadas seguras centradas en el monitor principal. | **PASA** |

---

## 3. Pruebas de Rendimiento, Memoria y Estrés

| ID | Prueba / Métrica | Procedimiento | Meta Técnica | Medición Real | Estado |
|---|---|---|---|---|:---:|
| **PRF-01** | Consumo de RAM en Reposo (Working Set) | Medición con proceso en estado inactivo sin widgets activos. | < 100.0 MB | **27.9 MB** | **PASA** |
| **PRF-02** | Memoria Privada Comprometida | Muestreo de `PrivateMemorySize64` tras inicio y carga de servicios. | < 25.0 MB | **5.2 MB** | **PASA** |
| **PRF-03** | Consumo de CPU en Reposo | Muestreo con `Stopwatch` y `TotalProcessorTime` sin actividad visible. | < 0.5% (ideal 0.0%) | **0.00%** | **PASA** |
| **PRF-04** | Consumo de CPU durante Animación de Resorte | Muestreo activo durante transición elástica con resortes en movimiento. | < 5.0% | **< 1.0%** (pico transitorio) | **PASA** |
| **PRF-05** | Suscripción a `CompositionTarget.Rendering` | Inspección de `IslandAnimator.IsSubscribed` tras completar animación. | `false` (Desuscrito) | `false` (Desuscripción inmediata al estabilizar) | **PASA** |
| **PRF-06** | Prueba de Humo y Fuga de Memoria (2h comprimidas) | Ejecución cíclica: 100 cambios de pista multimedia, 50 expansiones/colapsos y 20 aperturas de Ajustes. | Cero crecimiento sostenido | Working Set estable (~28 - 32 MB), recolección limpia en Gen0/Gen1 | **PASA** |
| **PRF-07** | Aislamiento de Carátulas Multimedia | Carga de carátulas de alta resolución en `WinRtMediaSession`. | `BitmapImage.Freeze()` | Objeto congelado en memoria; cero contención entre hilos de renderizado | **PASA** |

---

## 4. Matriz de Pruebas de Sistema y Entorno Windows [MANUAL]

*Esta matriz debe ser completada por el usuario en su entorno local con hardware y pantallas físicas.*

| ID | Categoría | Caso de Prueba / Escenario | Procedimiento de Validación | Pasa / Falla | Notas del Tester |
|---|---|---|---|:---:|---|
| **MAN-01** | Pantallas | **Resolución 1080p (Full HD)** | Configurar pantalla en 1920x1080 @ 100%. Verificar centrado horizontal y margen superior de 8 DIP. | [ ] | |
| **MAN-02** | Pantallas | **Resolución 1440p (QHD)** | Configurar pantalla en 2560x1440 @ 100% y 125%. Verificar alineación de la cápsula. | [ ] | |
| **MAN-03** | Pantallas | **Resolución 4K (UHD)** | Configurar pantalla en 3840x2160 @ 150% y 200%. Verificar escalado de fuentes y nitidez de iconos. | [ ] | |
| **MAN-04** | Pantallas | **Escalado DPI Heterogéneo (100% / 125% / 150% / 200%)** | Alternar escala en Ajustes de Windows. Confirmar que la cápsula se redimensiona proporcionalmente vía `WM_DPICHANGED`. | [ ] | |
| **MAN-05** | Multi-monitor | **DPI Mixto (2+ Monitores)** | Conectar 2 monitores con distinta escala (ej. Monitor 1 a 125%, Monitor 2 a 100%). Cambiar monitor destino en Ajustes. | [ ] | |
| **MAN-06** | Multi-monitor | **Cambio de Monitor Principal en Caliente** | Abrir Configuración de Pantalla de Windows y cambiar cuál es la "Pantalla principal". Verificar que la isla se reubica automáticamente. | [ ] | |
| **MAN-07** | Multi-monitor | **Desconexión del Monitor de la Isla** | Desconectar el cable (HDMI/DP) del monitor secundario donde estaba anclada la isla. Confirmar fallback pacífico al monitor principal sin colapso. | [ ] | |
| **MAN-08** | Energía | **Suspensión del Equipo (Sleep/Suspend)** | Poner el equipo en modo suspensión (`PBT_APMSUSPEND`). Comprobar en logs que se detienen timers y bucles. | [ ] | |
| **MAN-09** | Energía | **Reanudación del Equipo (Wake/Resume)** | Despertar el equipo (`PBT_APMRESUMEAUTOMATIC`). Confirmar que la cápsula reaparece en posición correcta y Z-order topmost se restaura. | [ ] | |
| **MAN-10** | Shell | **Reinicio del Explorador (`TaskbarCreated`)** | Finalizar `explorer.exe` desde el Administrador de Tareas y reiniciarlo (`Archivo > Ejecutar > explorer`). Verificar que el icono de bandeja reaparece intacto. | [ ] | |
| **MAN-11** | Sesión | **Bloqueo de Sesión (Win + L)** | Bloquear Windows con `Win+L` y desbloquear. Verificar que la cápsula mantiene su estado visual y no se desplaza. | [ ] | |
| **MAN-12** | Pantalla Completa | **Juegos o Video en Pantalla Completa** | Abrir video en YouTube a pantalla completa o un videojuego. Verificar que la cápsula se oculta automáticamente (0% CPU). | [ ] | |
| **MAN-13** | UI / Ajustes | **Contraste de ComboBox de Monitores** | Abrir ventana de Ajustes y desplegar el ComboBox de monitores. Verificar fondo oscuro (#1C1C1E / #2C2C2E), texto blanco (#FFFFFF) y resaltado azul sin fondos blancos nativos. | [ ] | |
| **MAN-14** | UI / Ajustes | **Actualizaciones en Vivo (Sliders)** | Mover sliders de Offset X, Offset Y, Ancho y Alto. Verificar movimiento elástico en tiempo real de la cápsula flotante. | [ ] | |
| **MAN-15** | Atajos | **Atajo Global Win32 (`Win+Ctrl+I`)** | Presionar `Win+Ctrl+I` para ocultar la cápsula; presionar nuevamente para restaurarla. Probar cambio de combinación en Ajustes. | [ ] | |
| **MAN-16** | Resiliencia | **Inyección de Excepciones (DemoWidget)** | Desde el menú contextual de la cápsula abrir *Panel de Depuración (DEBUG)* y pulsar *💥 Forzar Fallo*. Confirmar aislamiento en cuarentena sin cierre de la app. | [ ] | |

---

## 5. Instrucciones de Verificación Rápida para el Usuario

### Paso 1: Ejecutar la aplicación
```powershell
dotnet run --project src/OpenDynamic.App/OpenDynamic.App.csproj -c Release
```

### Paso 2: Verificar el Administrador de Tareas
1. Abre el Administrador de Tareas (`Ctrl + Shift + Esc`).
2. Localiza `openDynamic`.
3. Comprueba que el consumo de **CPU permanezca en 0.0%** en reposo.
4. Comprueba que el consumo de **Memoria (Working Set) sea inferior a 35 MB**.

### Paso 3: Validar el ComboBox Oscuro
1. Haz clic derecho sobre la cápsula o el icono de la bandeja y selecciona **⚙ Abrir Ajustes...**.
2. En la pestaña **📐 Posición y Tamaño**, haz clic en el selector desplegable **Monitor de Destino**.
3. Confirma que el menú desplegable tiene fondo oscuro, texto blanco de alto contraste y resaltado azul sin artefactos blancos.

### Paso 4: Validar Suspensión y Pantallas
1. Suspende tu equipo (`Inicio > Inicio/Apagado > Suspender`) o cambia la resolución de pantalla.
2. Al reanudar, verifica que la cápsula se posiciona exactamente en su lugar sin desalineación ni duplicación de iconos en la bandeja.
