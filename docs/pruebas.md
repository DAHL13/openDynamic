# Matriz de Pruebas y Validación de Calidad - openDynamic

> **Proyecto:** openDynamic (https://github.com/DAHL13/openDynamic)  
> **Versión:** 2.0.0 (Lanzamiento Mayor v2.0.0 — Auditoría Integral de Fin a Fin)  
> **Entorno de Pruebas:** Windows 11 Pro 24H2 (OS Build 26100), AMD Ryzen 7 5700G, .NET 10, PerMonitorV2 DPI  
> **Fecha de Validación:** 2026-10-05  

---

## 1. Resumen Ejecutivo y Metodología

La estrategia de aseguramiento de calidad de **openDynamic v2.0.0** se sustenta en tres pilares:

1. **Pruebas Automatizadas Deterministas:** **619 pruebas unitarias** sin `Thread.Sleep` ni `Task.Delay` de reloj real (`FakeTimeProvider`) en `OpenDynamic.Tests`, ejecutadas con compilación estricta (`TreatWarningsAsErrors=true`) y 100% de aprobación en 3 ejecuciones consecutivas en `Release`.
2. **Auditoría de Recursos y Rendimiento Real (`Release`):** Verificación empírica de las Reglas de Oro (CPU **0.00%** y **0 timers activos** en reposo durante 5 minutos, desuscripción estricta de `CompositionTarget.Rendering`, `PrivateMemorySize64` **~41.8 MB < 80 MB** y cero fugas en eventos COM/WinRT/Win32/WNF).
3. **Matriz de Pruebas de Sistema y Entorno [MANUAL]:** Protocolo exhaustivo para validar la robustez ante eventos del sistema operativo (suspensión, DPI mixto, multimonitor, `F11` pantalla completa, reinicio de `explorer.exe`, temas, privacidad, espectro FFT, reloj ambiental, ahorro de energía y capturas de pantalla).

---

## 2. Pruebas Automatizadas (Unitarias y de Dominio — 619 Pruebas)

| ID | Componente / Subsistema | Caso de Prueba | Resultado Esperado | Estado |
|---|---|---|---|:---:|
| **UT-01** | `OpenDynamic.Core.Animation` | Sub-stepping y conservación de inercia en `Spring` | Las velocidades iniciales se conservan entre cambios de objetivo; tiempo de convergencia < 600 ms sin oscilaciones infinitas. | **PASA** |
| **UT-02** | `OpenDynamic.Core.State` | Restricción de transiciones en `IslandStateMachine` | Transiciones inválidas (`Hidden -> Expanded`, `Hidden -> Split`) son rechazadas pacíficamente sin excepciones no controladas. | **PASA** |
| **UT-03** | `OpenDynamic.Core.Widgets` | Algoritmo determinista de `PriorityResolver` y `OnHover` | Actividad de mayor prioridad domina la cápsula; resolución en modo Split ante dos continuas; actividades `OnHover` solo se activan en reposo. | **PASA** |
| **UT-04** | `OpenDynamic.Core.Hardware` | Cálculo de deltas Win32 en `HardwareCalculator` | Lecturas de `GetSystemTimes` y `GlobalMemoryStatusEx` generan porcentajes exactos (0-100%) sin divisiones por cero. | **PASA** |
| **UT-05** | `OpenDynamic.Core.Timer` | Múltiples temporizadores (`TimerCollection` / `TimerController`) | Hasta 5 temporizadores simultáneos, cero deriva temporal, `RestoreRunning`/`RestorePaused`, `AddTime` en pausa y persistencia atómica `.tmp`. | **PASA** |
| **UT-06** | `OpenDynamic.Core.Settings` | Respaldo, migración `v1 -> v14` y `SanitizeAndClamp` | JSON malformado genera `.bak`; valores fuera de rango o enums inválidos se recortan limpiamente; `SaveDebounced` verificado con `FakeTimeProvider`. | **PASA** |
| **UT-07** | `OpenDynamic.Core.Stopwatch` | Cronómetro de precisión (`StopwatchController`) | Cálculo de vueltas (*laps*), vuelta más rápida/lenta, caché `ReadOnlyCollection` y límite seguro de 999 vueltas. | **PASA** |
| **UT-08** | `OpenDynamic.Core.Hotkeys` | Parser y normalizador (`HotkeyParser`) | Reconoce combinaciones válidas (`Win+Ctrl+I`), flags modificadores y rechaza combinaciones sin teclas no modificadoras. | **PASA** |
| **UT-09** | `OpenDynamic.Core.Autostart` | Servicio de arranque (`AutostartServiceCore`) | Lectura y validación de claves en registro `HKCU\Run` sin requerir elevación de privilegios UAC. | **PASA** |
| **UT-10** | `OpenDynamic.Core.Positioning` | Geometría en 1080p, 1440p, 4K y DPI escalas 100-200% | Posicionamiento centrado horizontalmente y coordenadas físicas exactas calculadas con `IslandPositionCalculator`. | **PASA** |
| **UT-11** | `OpenDynamic.Core.Audio.Spectrum` | Procesador FFT Cooley-Tukey Radix-2 (`FftProcessor`) | Descomposición armónica de 12 y 24 bandas logarítmicas con ventana de Hann y 0 asignaciones en heap por cuadro. | **PASA** |
| **UT-12** | `OpenDynamic.Core.Media` | Color dominante WCAG AA y gestos (`MediaColorExtractor`) | Ajuste de luminancia con contraste mínimo 4.5:1 sobre texto blanco y máquina de gestos horizontales. | **PASA** |
| **UT-13** | `OpenDynamic.Core.Clipboard` | Historial seguro en memoria (`ClipboardHistory`) | Deduplicación SHA-256, expiración temporal, truncamiento de vistas previas y exclusión de gestores de contraseñas. | **PASA** |
| **UT-14** | `OpenDynamic.Core.Privacy` | Agregador de micrófono y cámara (`PrivacyAccessAggregator`) | Evaluación `FILETIME`, supresión de alertas espurias en el escaneo inicial y filtrado inmediato de apps ignoradas. | **PASA** |
| **UT-15** | `OpenDynamic.Core.Network` / `Devices` | Políticas de alertas de red y periféricos | Supresión en arranque y post-suspensión (10 s), coalescencia con generación de timer y diccionarios acotados. | **PASA** |
| **UT-16** | `OpenDynamic.Core.Clock` | Formateador cultural y `ClockTickScheduler` | Alineación exacta al segundo `:00.000` del minuto, formato 12h/24h y cálculo de semana ISO 8601. | **PASA** |
| **UT-17** | `OpenDynamic.Core.EnergySaver` | Mapeador WNF/Win32 y `ResourceProfilePolicy` | Resolución determinista `Standard` vs `Efficient` respetando configuraciones explícitas del usuario. | **PASA** |
| **UT-18** | `OpenDynamic.Core.Screenshots` | Filtro canónico, `FileStabilityPolicy` e historial | Validación contra *reparse points* / *symlinks* fuera de carpetas vigiladas, estabilidad de escritura de 300 ms y retención en RAM. | **PASA** |

---

## 3. Pruebas de Rendimiento, Memoria y Estrés (`Release v2.0.0`)

| ID | Prueba / Métrica | Procedimiento | Meta Técnica | Medición Real | Estado |
|---|---|---|---|---|:---:|
| **PRF-01** | Memoria Privada Comprometida (`PrivateMemorySize64`) | Medición con proceso en estado inactivo (`Hidden`). | < 80.0 MB | **~41.8 MB** | **PASA** |
| **PRF-02** | Working Set Compartido (`WorkingSet64`) | Muestreo incluyendo páginas compartidas de D3D11/WPF/OS. | Documentado | **~158.0 MB** | **PASA** |
| **PRF-03** | Consumo de CPU en Reposo (5 min) | Muestreo de 5 minutos sin actividad visible (`Hidden`). | < 0.5% (ideal 0.0%) | **0.00%** (0 timers activos) | **PASA** |
| **PRF-04** | Consumo de CPU durante Animación de Resorte | Muestreo activo durante transición elástica con resortes en movimiento. | < 5.0% | **< 1.0%** (pico transitorio) | **PASA** |
| **PRF-05** | Suscripción a `CompositionTarget.Rendering` | Inspección de `IslandAnimator.IsSubscribed` tras completar animación. | `false` (Desuscrito) | `false` (Desuscripción inmediata al estabilizar) | **PASA** |
| **PRF-06** | Estabilidad de Memoria en Sesión Continua | Sesión continua con múltiples transiciones y retorno a reposo. | Deriva < 5% | **+0.0% a +2.1%** (estable tras GC) | **PASA** |
| **PRF-07** | Desacoplamiento Asíncrono de GPU PDH | Muestreo de `GPU Engine` con `EnableGpuMonitoring = true`. | 0 ms de bloqueo en hilo UI | Ejecución en `Task.Run` no solapado y `Dispose()` al desactivar | **PASA** |

---

## 4. Matriz de Pruebas de Sistema y Entorno Windows [MANUAL]

*Esta matriz permite verificar en hardware real el comportamiento visual e interactivo de las 21 fases en el binario de lanzamiento.*

| ID | Categoría | Caso de Prueba / Escenario | Procedimiento de Validación | Pasa / Falla | Notas del Tester |
|---|---|---|---|:---:|---|
| **MAN-01** | Pantallas | **Resolución 1080p / 1440p / 4K y Escalado DPI** | Alternar escala en Ajustes de Windows (100%, 125%, 150%, 200%). Confirmar que la muesca se redimensiona y centra vía `WM_DPICHANGED`. | [ ] | |
| **MAN-02** | Multi-monitor | **DPI Mixto y Desconexión de Monitor** | Conectar 2 monitores con distinta escala, cambiar monitor destino en Ajustes y desconectar el secundario. Confirmar fallback pacífico al principal. | [ ] | |
| **MAN-03** | Energía | **Suspensión y Reanudación (`PBT_APMSUSPEND`)** | Suspender y despertar el equipo. Confirmar que se suprimen alertas espurias durante 10 s y el Z-order topmost se restaura. | [ ] | |
| **MAN-04** | Shell | **Reinicio del Explorador (`TaskbarCreated`)** | Reiniciar `explorer.exe` desde el Administrador de Tareas. Verificar que el icono de bandeja se recrea y la muesca recupera su posición topmost. | [ ] | |
| **MAN-05** | Pantalla Completa | **F11 en Navegador y Juegos (`EVENT_OBJECT_LOCATIONCHANGE`)** | Con un navegador ya enfocado, pulsar `F11` o entrar a pantalla completa en YouTube. Verificar que la muesca se oculta en ~150 ms y reaparece al salir. | [ ] | |
| **MAN-06** | Reposo / Hit-Test | **Franja Sensora `120x4 DIP` y Clics en Pestañas (`AUD-001`, `AUD-005`)** | En estado `Hidden`, hacer clic en pestañas del navegador situadas a `Y = 10..36 px` bajo el centro superior: el clic debe pasar a la ventana inferior. Acercar el cursor al borde superior (`Y = 0..3 px`) por 250 ms: debe desplegarse el Reloj Ambiental. | [ ] | |
| **MAN-07** | Ajustes en Vivo | **Interruptores `Enable*Widget` en Caliente (`AUD-002`)** | Con música, temporizador o cronómetro activos en la muesca, desmarcar su casilla en Ajustes. Confirmar que el widget desaparece de inmediato y su servicio se detiene. | [ ] | |
| **MAN-08** | Accesibilidad | **Perfiles de Movimiento y Alto Contraste** | Conmutar `MotionMode` (`Auto`, `Reduced`, `Full`) y activar Alto Contraste de Windows (`Alt+Shift+ImprPant`). Verificar amortiguamiento crítico y bordes de alto contraste. | [ ] | |
| **MAN-09** | Red y Periféricos | **Wi-Fi / Ethernet y USB / Bluetooth** | Conectar/desconectar un dispositivo USB o auriculares Bluetooth. Verificar alerta consolidada única tras la ventana de coalescencia de 800 ms. | [ ] | |
| **MAN-10** | Tiempo | **5 Temporizadores Concurrentes y Cronómetro Split** | Iniciar 2 temporizadores y el cronómetro simultáneamente. Verificar modo `Split`, intercambio al clic en la burbuja satélite y persistencia tras reiniciar la app. | [ ] | |
| **MAN-11** | Multimedia | **Color Dinámico WCAG AA, Gestos y Espectro FFT** | Reproducir música, verificar tinte de carátula legible, cambiar pista arrastrando horizontalmente la cabecera y comprobar las 12/24 barras del espectro WASAPI. | [ ] | |
| **MAN-12** | Portapapeles | **Historial Opt-In en RAM y Exclusión de Contraseñas** | Activar Portapapeles en Ajustes, copiar texto/imagen y verificar vista previa. Bloquear sesión (`Win+L`) y confirmar que el historial en RAM se vacía. | [ ] | |
| **MAN-13** | Privacidad | **Indicador de Cámara y Micrófono (`ConsentStore`)** | Abrir la app Cámara o Grabadora de voz. Confirmar que al iniciar la app con micrófono ya abierto se muestra el indicador sin disparar alerta transitoria falsa, y que añadir la app a ignorados oculta el indicador al instante. | [ ] | |
| **MAN-14** | Ahorro de Energía | **Conmutación WNF en Windows 11 y Perfil Eficiente** | Activar el mosaico "Ahorro de energía" en Configuración Rápida de Windows. Verificar aviso esmeralda de 3 s y conmutación automática del visualizador a modo `Simulated`. | [ ] | |
| **MAN-15** | Capturas | **Vista Previa Instantánea (`Win+ImprPant`), Drag & Drop y Papelera** | Tomar captura con `Win+ImprPant`, arrastrar la miniatura hacia otra app (verificar copia), borrar el archivo desde el Explorador (verificar que no está bloqueado) o usar el botón Papelera con doble confirmación. | [ ] | |

