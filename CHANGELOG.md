# Bitácora de Cambios (Changelog) - openDynamic

Todas las modificaciones notables en este proyecto serán documentadas en este archivo.
El formato se basa en [Keep a Changelog](https://keepachangelog.com/es-ES/1.1.0/) y cumple con [SemVer](https://semver.org/).

## [1.3.0-dev] - 2026-10-01 (Fase 18: Reglas "Siempre Permitir", Cola FIFO y Estado del Agente)

### Añadido
- **Despliegue Completo de 5 Opciones en el Notch (`ApprovalWidget`):**
  - Despliegue interactivo con estructura visual idéntica a Antigravity: `[1] Permitir esta vez`, `[2] Permitir siempre en esta conversación`, `[3] Permitir siempre en este proyecto`, `[4] Permitir siempre globalmente` y `[5] No: Denegar` con menú de motivos.
  - Opción destacada/resaltada configurable en Ajustes (por defecto opción 1), activable mediante `Ctrl+Alt+Enter` tras vencer la guarda anti-clic.
  - Atajos dinámicos de teclado `Ctrl+Alt+1` a `Ctrl+Alt+5` y `Ctrl+Alt+A` (Decidir en Antigravity) vinculados de forma no invasiva al HWND de la isla.
  - Restricción de Seguridad Automática (Regla de Oro 12): Ocultamiento inmediato de las opciones 2, 3 y 4 cuando el comando clasifica como riesgo `High` o cuando se trata de herramientas de edición de archivos (`write_to_file`, `replace_file_content`).
- **Motor de Reglas y Persistencia Atómica (`OpenDynamic.Core.AgentApprovals`):**
  - `ApprovalRule`: Entidad inmutable con ámbito (`Conversation`, `Project`, `Global`), patrón de comando, claves de workspace y contadores de uso.
  - `ApprovalRuleMatcher`: Evaluador puro de coincidencia exacta carácter por carácter (espacios internos y casing estrictos, normalización `\r\n` a `\n`). Cero auto-aprobaciones para riesgo `High` o herramientas de archivo.
  - `ApprovalRuleStore`: Almacén con persistencia en memoria RAM para conversaciones, guardado atómico con archivos `.tmp` y recuperación automática desde `.bak` para proyectos (`.antigravity/approval-rules.json`) y global (`%USERPROFILE%/.antigravity/approval-rules.json`). Límite de 200 reglas por ámbito con política de poda LRU.
  - `SafePrefixMatcher`: Módulo opcional (opt-in, Tarea 6b) para reglas de prefijo en proyectos (`git status`, `git diff`, `git log`, `dotnet test`, etc.) con límites de token y argumentos benignos. Exclusión total de shells, intérpretes y descargadores.
  - `ApprovalHistoryTracker`: Búfer circular en memoria RAM de las últimas 50 decisiones sanitizadas (resúmenes de 60 caracteres y nombres de carpetas relativos, sin rutas absolutas ni credenciales).
- **Cola FIFO Multisesión de Solicitudes:**
  - Gestión secuencial de solicitudes concurrentes de múltiples agentes o proyectos con indicador `"1 / N"`.
  - Temporizadores de guarda (600 ms) y expiración (90 s) totalmente independientes por solicitud.
  - Descarte seguro y limpio de solicitudes si el cliente se desconecta anticipadamente.
- **Hook `Stop` y Notificación de Estado del Agente (`AgentStatusWidget`):**
  - Intercepción del ciclo de vida del agente mediante el subcomando `--event stop` en `OpenDynamic.Hook` registrado bajo la clave `openDynamic-status`.
  - Notificación visual sutil de Prioridad 70 durante 4 segundos en el Notch ("Antigravity terminó en <proyecto>") con sonido opcional, emitida únicamente cuando `fullyIdle == true`.
- **Ajustes y Migración de Esquema v11 (`AppSettings`):**
  - `CurrentSchemaVersion = 11`.
  - Nueva tarjeta "Reglas de Aprobación" con listado filtrable por ámbito, botón de borrado, reglas sugeridas con 1 clic (`git status`, `git diff --stat`, `git log --oneline`), historial reciente de 50 decisiones y controles para notificaciones de estado y prefijos seguros.
- **Pruebas Automatizadas:**
  - 551 pruebas en verde, incluyendo cobertura integral de coincidencia exacta, prefijo seguro, cola FIFO, recuperación de copias de seguridad `.bak`, poda LRU y privacidad estricta en logs.

## [1.2.0-dev] - 2026-09-30 (Fase 17: Aprobaciones de Agente en el Notch - Núcleo)

### Añadido
- **Protocolo de Aprobaciones y Modelos de Dominio en Core (`OpenDynamic.Core.AgentApprovals`):**
  - `ApprovalRequest`: Modelo inmutable de solicitud PreToolUse que extrae y tipifica campos de herramientas de Antigravity (`toolCall.name`, `toolCall.args`, `conversationId`, `stepIdx`, `workspacePaths`, etc.).
  - `ApprovalResponse`: Factorías seguras para decisiones `allow`, `deny` (con motivo predefinido) y `ask` (delegación segura con motivo).
  - `PipeMessage`: Envoltorio versionado (`Version = 1`) para IPC seguro con límite estricto de tamaño a 256 KB.
  - `RiskLevel` y `CommandRiskClassifier`: Clasificador determinista de riesgo (Low, Medium, High) con garantía de cero falsos bajos para comandos destructivos (`rm -rf`, `Remove-Item -Recurse`, `git push --force`, `git reset --hard`, `iex`, `reg delete`, `format`, etc.), comandos encadenados (`&&`, `;`, `|`) y redirecciones.
  - `ApprovalPresentation`: Formateador legible para humanos con truncado seguro a 200 caracteres y marcado obligatorio de revisión expandida (`RequiresExpandedReview`) para comandos truncados o de riesgo High.
  - `ApprovalSessionPolicy`: Política pura de sesión con guarda anti-clic accidental de 600 ms, timeout de 90 segundos con expiración a `AskNative`, y restricción estricta de atajos de teclado para riesgo High.
  - `AntigravityHookInstaller`: Instalador y desinstalador seguro de ganchos en `%USERPROFILE%\.gemini\antigravity\hooks.json` con copia de seguridad `.bak`, fusión limpia que solo toca `openDynamic-approvals`, y previsualización de configuración.
- **Servidor IPC Named Pipe y Widget Notch en App (`OpenDynamic.App`):**
  - `AgentApprovalPipeServer`: Servidor Named Pipe local `openDynamic-agent-v1` protegido exclusivamente para el usuario actual (`PipeOptions.CurrentUserOnly`). Cero logs de comandos o rutas en Serilog (solo metadatos agregados auditables).
  - `ApprovalWidget`: Widget con prioridad 95 (`ActivityPriority.AgentApproval = 95`) que se auto-expande sin robar el foco (`WS_EX_NOACTIVATE`) ni interrumpir la terminal.
  - Vistas XAML `ApprovalCompactView` y `ApprovalExpandedView` con opciones numeradas ([1] permitir, [5] denegar con motivos predefinidos, [Esc]/botón "Decidir en Antigravity").
  - Enlace dinámico de atajos HWND en `IslandWindow` mientras el widget de aprobación esté activo en pantalla.
  - Integración en `SettingsViewModel` y `SettingsWindow`: pestaña "Antigravity" con interruptor desactivado por defecto (SchemaVersion = 10), instalador/desinstalador del hook, y botón "Enviar solicitud de prueba".
- **Cliente Hook Ligero (`OpenDynamic.Hook`):**
  - CLI ligero independiente compilado con ReadyToRun (`< 150 ms` arranque presupuestado, `~65 ms` medido).
  - Falla segura obligatoria (Golden Rule 12): ante cualquier excepción, timeout o servidor ausente, emite `{"decision":"ask","reason":"..."}` por `stdout` con código de salida 0.
- **Pruebas Automatizadas y Seguridad:**
  - `ProtocolTests`: Serialización y deserialización de modelos y mensajes de pipe.
  - `RiskClassifierTests`: 45 casos de prueba cubriendo comandos destructivos, encadenados, PowerShell y lecturas seguras.
  - `PresentationTests`: Verificación de truncado y requisitos de revisión expandida.
  - `SessionPolicyTests`: Guarda anti-clic de 600 ms, timeouts y autorizaciones de atajos.
  - `HookInstallerTests`: Instalación, respaldo `.bak`, fusión y desinstalación limpia.
  - `HookClientIntegrationTests`: 7 pruebas completas de integración de proceso verificando respuestas `allow`, `deny`, `ask`, timeouts (`--wait-ms`) y desconexiones tempranas en < 1.5 s.
  - `ZeroLogsSecurityTests`: Verificación de que comandos, rutas y secretos nunca son registrados en Serilog ni en el código fuente.

## [1.1.0-dev] - 2026-09-30 (Fase 16: Visualizador de Audio Real - Espectro)

### Añadido
- **Analizador de Espectro Puro y Sin Dependencias en Core (`OpenDynamic.Core.Audio.Spectrum`):**
  - `SpectrumAnalyzer`: FFT propia iterativa Cooley-Tukey Radix-2 optimizada ($N = 1024$) con ventana de Hann precalculada, 50% de solape (512 muestras), precomputación de raíces de la unidad y permutación bit-reversal sin dependencias externas (Regla de Oro 5).
  - Agrupación en bandas logarítmicas de audio (12 bandas para modo `Compact` y 24 bandas para modo `Expanded`) cubriendo el rango de 45 Hz a 16.5 kHz.
  - Compensación perceptual de inclinación espectral (Pink noise tilt ~ -3dB/octava) y mapeo dinámico en decibelios (dBFS) a rango [0.0, 1.0].
  - Suavizado temporal con ataque rápido (0.65f) para transitorios y caída lenta (0.85f).
  - Cero asignaciones por cuadro (0 per-frame heap allocations - Regla de Oro 11) con búferes internos reutilizables preasignados.
  - `VisualizerActivationPolicy`: Política pura que determina la activación de captura si y solo si se cumplen simultáneamente: modo `Real`, reproducción GSMTC activa (`IsMediaPlaying`), widget visible en la isla, isla no oculta y sin pantalla completa exclusiva suprimida.
- **Servicio Resiliente de Captura WASAPI Loopback en App (`OpenDynamic.App.Services.AudioSpectrumService`):**
  - Captura en vivo del audio del sistema mediante `WasapiLoopbackCapture` de NAudio sobre el dispositivo predeterminado.
  - Conversión estéreo a mono sin asignaciones (`MemoryMarshal.Cast<byte, float>`).
  - Doble búfer sin bloqueos (Lock-free double buffering con `Interlocked.Exchange`) para transferencia inmediata entre el hilo de captura y el hilo de renderización de WPF.
  - Resiliencia y degradación pacífica (Regla de Oro 4): reenganche automático ante cambios de dispositivo en caliente vía `VolumeService.DefaultDeviceChanged`; degradación transparente a modo `Simulado` si WASAPI falla o arroja excepciones COM.
  - Manejo de silencios: decaimiento automático suave a cero cuando no se reciben paquetes WASAPI.
  - Privacidad estricta (Regla de Oro 10): el audio se procesa 100% en memoria volátil; cero muestras en disco o logs de Serilog.
- **Renderizado Reactivo Notch a ~30 FPS en Vistas (`MediaCompactView` y `MediaExpandedView`):**
  - 12 barras logarítmicas en la vista Compacta y 24 barras en la vista Expandida, coloreadas dinámicamente con el acento de la carátula (`AccentBrush`).
  - Bucle de renderizado limitado a ~30 FPS (33 ms) mediante `CompositionTarget.Rendering` activo **únicamente** mientras la captura/música está encendida (Regla de Oro 1: 0% CPU en reposo).
  - Soporte para modo de accesibilidad `MotionMode.Reduced`: en modo reducido no se animan las barras dinámicas.
- **Ajustes y Migración de Esquema v9 (`AppSettings`):**
  - Incremento a `CurrentSchemaVersion = 9` en `AppSettings.cs`.
  - Migración automática retrocompatible en `SettingsService.Load()` manteniendo `AudioVisualizerMode.Real` como valor predeterminado.
  - Controles accesibles en `SettingsWindow.xaml`: selector de modo (Desactivado / Simulado / Real) e interruptor rápido para activar/desactivar visualizador reactivo.
- **Pruebas Automatizadas y Presupuesto de Rendimiento:**
  - Nuevas suites de pruebas: `SpectrumAnalyzerTests` (tono senoidal de 1000 Hz, silencio, suavizado, cero asignaciones), `VisualizerActivationPolicyTests` (tabla de combinaciones) y `MediaVisualizerTests` (ciclo de vida de activación).
  - Medición de rendimiento comprobada: 50 ms de CPU para procesar 5 segundos continuos de audio a 48 kHz (< 1.0% de un núcleo, < 0.15% de CPU total del sistema), cumpliendo holgadamente el presupuesto de < 2% de CPU adicional. 389 pruebas en verde.

## [1.1.0-dev] - 2026-09-30 (Fase 15: Indicador de Micrófono y Cámara en Uso)

### Añadido
- **Modelos y Agregador Puro en Core (`OpenDynamic.Core.Privacy`):**
  - Modelos inmutables `PrivacyResourceType` (`Microphone`, `Camera`), `PrivacyAccessEntry`, `PrivacyAccessState` y `PrivacyAccessChange` con evaluación estricta de marcas FILETIME de 64 bits (`LastUsedTimeStart > 0 && (LastUsedTimeStop == 0 || LastUsedTimeStart > LastUsedTimeStop)`), 100% desacoplados de Windows y de la interfaz (Regla de Oro 5).
  - `PrivacyAccessAggregator`: Agregador determinista con inyección de `TimeProvider` para pruebas unitarias sin pausas reales, cálculo de transiciones (`Started`, `Stopped`) y soporte para lista de exclusión configurable (`IgnoredPrivacyApps`). Exclusión automática de la propia app (`openDynamic`).
  - `PrivacyConsentStoreParser`: Decodificación de rutas ejecutables `#` a `\`, extracción limpia de nombres de procesos y formateo amigable de identificadores de paquetes conocidos ("Cámara de Windows", "Grabadora de voz", etc.).
- **Monitor Reactivo Nativo en App (`OpenDynamic.App.Services.PrivacyAccessMonitor`):**
  - Lectura pasiva y segura del registro de Windows (`HKCU\Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\microphone` y `...\webcam`).
  - **Cero Captura Física (Reglas de Oro 10 y 11):** Prohibición estricta de abrir o inicializar dispositivos de audio/video (sin MediaCapture, DirectShow, WASAPI de captura). openDynamic opera 100% como lector pasivo del registro.
  - **Cero Polling (Regla de Oro 1):** Hilo de fondo dedicado (`PrivacyConsentStoreWatcher`) con `RegNotifyChangeKeyValue` (`bWatchSubtree = true`, `ChangeName | ChangeLastSet`) y eventos de kernel (`AutoResetEvent` / `ManualResetEvent`). El hilo permanece dormido en `WaitHandle.WaitAny` y no consume ciclos de CPU en reposo.
  - Liberación limpia y garantizada de hilos y handles al apagar la aplicación o desactivar la monitorización.
  - Degradación elegante: si las claves no existen o fallan, la función se desactiva pacíficamente registrando una advertencia estructurada.
- **Insignias Persistentes en la Muesca (Notch UI):**
  - Insignia sutil integrada en la esquina superior del notch (verde `#34C759` para cámara, ámbar/naranja `#FFFF9500` para micrófono).
  - Convivencia no destructiva: los indicadores flotan por encima del notch sin desplazar ni achicar el contenido del widget primario activo (Música, Temporizador, etc.).
  - Respeto a la visibilidad: si la isla está oculta por el usuario o en pantalla completa, las insignias no fuerzan la aparición de la muesca.
- **Aviso Transitorio (`OpenDynamic.App.Widgets.Privacy`):**
  - `PrivacyWidget`: Widget transitorio asignado a prioridad 85 (`ActivityPriority.Privacy = 85`), superior a volumen (80) e inferior a batería (90), con duración de 3.0 s.
  - Vistas `PrivacyCompactView`, `PrivacyExpandedView` y `PrivacySplitView`.
- **Ajustes y Migración de Esquema v8 (`AppSettings`):**
  - Incremento a `CurrentSchemaVersion = 8` en `AppSettings.cs` con migración retrocompatible en `SettingsService.Load()`.
  - Interruptores independientes en Ajustes para el indicador de micrófono, el de cámara y avisos transitorios, con deslizadores de prioridad y duración.
  - Gestión interactiva de lista de aplicaciones ignoradas (`IgnoredPrivacyApps`).
- **Pruebas Automatizadas:**
  - Nuevas suites de pruebas: `PrivacyAccessAggregatorTests`, `PrivacyConsentStoreParserTests` y prueba de migración v8 en `SettingsServiceTests`. 355 pruebas en verde.

## [1.1.0-dev] - 2026-09-30 (Fase 14: Portapapeles Reciente y Seguro)

### Añadido
- **Historial de Portapapeles Seguro y Puro en Core (`OpenDynamic.Core.Clipboard`):**
  - Modelos inmutables y seguros `ClipboardItemKind` (`Text`, `Url`, `Image`, `Files`) y `ClipboardItem` sin dependencias de Windows ni WPF (Regla de Oro 5).
  - `ClipboardFormatter`: Sanitización de texto a una sola línea, truncado a 80 caracteres con elipsis, clasificación automática de URLs y generación de etiquetas opacas para imágenes y conteo de archivos sin exponer datos sensibles ni rutas de disco (Regla de Oro 10).
  - `ClipboardHistoryManager`: Almacenamiento estrictamente volátil en memoria RAM, capacidad acotada (1 a 10 elementos, default 5), expiración temporal con `TimeProvider` inyectable (default 10 min), supresión de duplicados consecutivos y vaciado instantáneo (`Clear()`).
- **Servicio Nativo de Portapapeles Reactivo en App (`OpenDynamic.App.Services.ClipboardService`):**
  - Registro reactivo de eventos Win32 con `AddClipboardFormatListener` y `WM_CLIPBOARDUPDATE` sobre `HwndSource`. Cero polling permanente (Regla de Oro 1).
  - Filtrado y exclusión estricta de formatos de administradores de contraseñas (`ExcludeClipboardContentFromMonitorProcessing`, `Clipboard Viewer Ignore`, `CanIncludeInClipboardHistory`, `CanUploadToCloudClipboard`).
  - Resiliencia y tolerancia ante contención COM (`CLIPBRD_E_CANT_OPEN` / `0x800401D0`) con hasta 3 reintentos asíncronos de 50 ms protegidos con try/catch (Regla de Oro 4).
  - Vaciado forzado e inmediato de RAM ante bloqueo de sesión (`SessionSwitchReason.SessionLock`), suspensión de energía (`WM_POWERBROADCAST` / `PowerModes.Suspend`), desactivación del interruptor o cierre de la aplicación.
  - Anonimización y privacidad absoluta en logs de Serilog: se prohíbe registrar texto o rutas, reportando únicamente tipo y longitud (`Kind`, `Length`, `Count`).
- **Widget y Vistas en la Muesca (`OpenDynamic.App.Widgets.Clipboard`):**
  - `ClipboardWidget`: Asignado a `ActivityPriority.Clipboard = 55` y auto-expiración transitoria de 2.0 s.
  - `ClipboardCompactView`: Aviso de copiado adaptado al notch ("Copiado: <vista previa>", "Enlace copiado", "Imagen copiada", "Archivos copiados") y badge identificador "RAM".
  - `ClipboardExpandedView`: Lista de historial reciente en memoria con affordance de re-copiado sin robar foco (`WS_EX_NOACTIVATE`), mensaje de retroalimentación ("Copiado de nuevo") y botón de borrado inmediato.
  - `ClipboardSplitView`: Burbuja satélite circular de 36x36 con icono de portapapeles y acento violeta.
- **Ajustes y Migración de Esquema v7 (`AppSettings`):**
  - Incremento a `CurrentSchemaVersion = 7` en `AppSettings.cs`.
  - Migración retrocompatible en `SettingsService.Load()` manteniendo la función estrictamente opt-in (`EnableClipboardWidget = false` por defecto).
  - Tarjeta de ajustes en `SettingsWindow.xaml` con interruptor de función, selector de vista previa, deslizadores de capacidad y expiración, y botón para borrar historial.
  - Submenú contextual en el icono de la bandeja del sistema (`TrayIconManager`) para pausar/reanudar monitoreo y vaciar el búfer de memoria.
- **Pruebas Automatizadas:**
  - Nuevas suites de pruebas unitarias: `ClipboardHistoryTests` y `PriorityResolverPhase14Tests`, y migración v7 en `SettingsServiceTests`. 327 pruebas en verde.

## [1.1.0-dev] - 2026-09-29 (Fase 13: Color de Carátula y Gestos en la Música)

### Añadido
- **Extracción Algorítmica Pura de Color en Core (`OpenDynamic.Core.Media.Color`):**
  - Estructura pura e inmutable `RgbColor` con conversión bidireccional HSL y cálculo de matiz, saturación y luminosidad sin dependencias de Windows ni WPF (Regla de Oro 5).
  - `DominantColorExtractor`: Muestreo en cuadrícula de baja resolución (32x32 = 1024 píxeles), agrupación en 16 cubetas angulares de Hue (22.5°), ponderación por saturación cuadrática y eliminación estricta de casi negros, casi blancos y grises neutros.
  - `AccentColorAdjuster`: Ajusta el color candidato forzando saturación mínima ($S \ge 0.50$) y acotando luminosidad ($0.45 \le L \le 0.80$) para asegurar máxima legibilidad y contraste contra el fondo negro de la muesca. Retorna `#FFFFFF` (blanco estándar) como fallback si la portada carece de color dominante válido o es monocromática.
- **Detector de Gestos Horizontales en Core (`OpenDynamic.Core.Media.Gestures`):**
  - `SwipeGestureDetector`: Máquina de estados pura con `TimeProvider` inyectable para pruebas deterministas.
  - Acumula deltas horizontales y absorbe la inercia del touchpad con una ventana de enfriamiento (*cooldown*) de 400 ms para eliminar saltos accidentales dobles de canción.
  - Métodos semánticos `ProcessWheelDelta` y `ProcessDragDelta` con umbral configurable y restablecimiento determinista.
- **Servicio de Color de Miniatura Asíncrono en App (`OpenDynamic.App.Services.MediaColorService`):**
  - Ejecución en segundo plano (`Task.Run`) a partir de la miniatura congelada (`BitmapImage.Freeze()`).
  - Caché en memoria por pista (`ConcurrentDictionary<string, RgbColor>` con clave `"Título|Artista"`) sin retener referencias a bitmaps antiguos para prevenir fugas de memoria.
  - Generación de `SolidColorBrush` congelados (`brush.Freeze()`) para consumo seguro y eficiente en subprocesos de interfaz.
- **Integración Visual del Acento en la Muesca (`OpenDynamic.App`):**
  - Aplicación del color de acento de carátula en:
    * Barras del ecualizador oscilante en `MediaCompactView`.
    * Barra interactiva de progreso y botón de reproducción en `MediaExpandedView`.
    * Insignia indicadora de reproducción del satélite en `MediaSplitView`.
    * Tinte sutil en el borde perimetral (`BorderBrush`) de la muesca en `IslandView` animado con `ColorAnimation` corta (~300 ms) al cambiar de pista.
  - Prohibición estricta de efectos `DropShadowEffect` de gran radio, protegiendo el rasterizado por GPU y manteniendo ~0% CPU en reposo.
  - Respeto total al modo de accesibilidad `MotionMode.Reduced`: supresión de animaciones lentas y cambio instantáneo del color.
- **Gestos Horizontales de Salto de Pista:**
  - Soporte de rueda horizontal y deslizamiento con dos dedos (`WM_MOUSEHWHEEL`, `0x020E`) en el `WndProc` de `IslandWindow`, activo únicamente cuando el cursor está sobre la cápsula multimedia y el reproductor soporta `CanSkipNext / CanSkipPrevious`.
  - Arrastre táctil y con botón izquierdo sobre la cabecera (carátula y títulos) en `MediaExpandedView` con umbral mínimo ~40 DIPs, aislado completamente de la barra de progreso (seek bar).
  - Retroalimentación táctil y visual con desplazamiento elástico de la carátula (`ElasticEase`), omitido limpiamente en modo reducido.
- **Ajustes y Migración de Configuración (Schema v6):**
  - Incremento a `CurrentSchemaVersion = 6` en `AppSettings.cs` con propiedades `EnableDynamicMediaColor`, `EnableMediaGestures` y `MediaGestureSensitivity`.
  - Migración retrocompatible en `SettingsService.Load()` para esquemas `< 6`.
  - Nuevas tarjetas de configuración en `SettingsWindow` con interruptores y control de sensibilidad accesibles vía UI Automation.
- **Pruebas Automatizadas:**
  - Nuevas suites: `DominantColorExtractorTests`, `SwipeGestureDetectorTests`, y migración Schema v6 en `SettingsServiceTests`. 305 pruebas unitarias en verde.


## [1.1.0-dev] - 2026-09-29 (Fase 12: Cronómetro y Varios Temporizadores)

### Añadido
- **Cronómetro de Alta Precisión en Core (`OpenDynamic.Core.Stopwatch`):**
  - Modelos puros `StopwatchState`, `StopwatchLap`, `StopwatchSnapshot` y controlador `StopwatchController` sin dependencias de plataforma (Regla de Oro 5).
  - Cálculo estricto basado en marcas de tiempo UTC provistas por `TimeProvider` (`ElapsedTime = accumulated + (now - sessionStartUtc)`), eliminando completamente la acumulación iterativa de ticks y la deriva temporal.
  - Soporte de vueltas (*laps*) con cálculo independiente del tiempo de vuelta y el tiempo acumulado (*split time*), y formateo dual `mm:ss.cc` y `h:mm:ss`.
- **Colección de Múltiples Temporizadores en Core (`OpenDynamic.Core.Timer`):**
  - `TimerCollection`: Administración de hasta 5 temporizadores simultáneos (`TimerController`) con identificador único y etiqueta configurable.
  - Regla de selección del temporizador principal: La cápsula compacta de la Dynamic Island muestra siempre el temporizador que termina antes entre los que están corriendo.
  - Encolado secuencial de alertas: Cuando expiran temporizadores concurrentemente, las alertas transitorias (prioridad 100, 5 segundos cada una) se encolan y presentan secuencialmente con su etiqueta y sonido del sistema.
  - Despertar de bajo consumo: Programación de temporizadores de precisión mediante `TimeProvider.CreateTimer` para expirar reactivamente sin sondeo continuo cuando la isla está oculta o inactiva.
  - Compatibilidad total: El temporizador y Pomodoro previos pasan a ser el primer elemento (`"primary"`), manteniendo en verde y sin modificaciones las pruebas unitarias de Fase 6.
- **Persistencia Segura de Temporizadores (`OpenDynamic.Core.Timer.TimerPersistenceService`):**
  - Serialización de temporizadores activos en `%AppData%\openDynamic\timers.json`.
  - Restauración automática de temporizadores vigentes al iniciar y reporte único de temporizadores que vencieron con la aplicación cerrada.
- **Presupuesto de Rendimiento y Coordinador de UI (`OpenDynamic.App.Services.TimingUiCoordinator`):**
  - Un único `DispatcherTimer` compartido para toda la aplicación (Reglas de Oro 1 y 11), activo únicamente si el cronómetro está corriendo o si hay al menos un temporizador activo visible. Detención absoluta (0% CPU) en reposo o pausa.
- **Widgets y Vistas en la Muesca (`OpenDynamic.App.Widgets`):**
  - `StopwatchWidget`: Prioridad 45 (`ActivityPriority.Stopwatch`), vistas `StopwatchCompactView`, `StopwatchExpandedView` (con lista de vueltas cronológica invertida y botones de vuelta/iniciar/reiniciar) y `StopwatchSplitView`.
  - `TimerWidget`: Vista expandida renovada `TimerExpandedView` con preajustes rápidos (1, 5, 10, 15 min), sumadores (+1m/+5m) y lista interactiva de temporizadores en marcha.
- **Aislamiento de Entrada de Texto en Ajustes (`SettingsWindow`):**
  - Creación de temporizadores y edición de nombres restringida exclusivamente a `SettingsWindow` para preservar la arquitectura `WS_EX_NOACTIVATE` sin robo de foco en la Dynamic Island.
  - Tarjeta dedicada de configuración del cronómetro con interruptor y control de prioridad.
- **Migración de Esquema de Configuración (Schema v5):**
  - Incremento a `CurrentSchemaVersion = 5` en `AppSettings.cs` con propiedades `EnableStopwatchWidget`, `DefaultStopwatchPriority` y `TimerPresetsMinutes`.
  - Migración transparente en `SettingsService.Load()` para versiones `< 5`.
- **Pruebas Automatizadas:**
  - Nuevas suites de pruebas unitarias: `StopwatchControllerTests`, `TimerCollectionTests`, `TimerPersistenceServiceTests`, `PriorityResolverPhase12Tests` y actualización de `SettingsServiceTests` (migración v5).
  - Suite completa de 286 pruebas unitarias en verde.

## [1.1.0-dev] - 2026-09-29 (Fase 11: Red y Dispositivos Periféricos)

### Añadido
- **Alertas de Conectividad de Red en Core (`OpenDynamic.Core.Network`):**
  - Modelos puros `NetworkState`, `NetworkType` y `NetworkSnapshot` sin dependencias de Windows ni WPF (Regla de Oro 5).
  - Política pura `NetworkAlertPolicy` con `TimeProvider` inyectable:
    * Debounce de 1.0 s ante cambios rápidos e inestabilidad transitoria de interfaces de red.
    * Cooldown de 5.0 s entre alertas consecutivas del mismo estado/red para evitar spam de reconexión.
    * Supresión total del estado inicial como línea base en el arranque del sistema.
    * Ventana de supresión de 10.0 s tras reanudación de suspensión/hibernación (`OnPowerResumed`).
- **Alertas de Dispositivos USB y Bluetooth en Core (`OpenDynamic.Core.Devices`):**
  - Modelos puros `DeviceEventType`, `DeviceCategory`, `DeviceEvent` y clasificador heurístico `DeviceCategoryClassifier`.
  - Política pura `DeviceAlertPolicy` con `TimeProvider` inyectable:
    * Coalescencia / agrupación de 800 ms para inserciones multifunción o ráfagas de periféricos compuestos.
    * Cooldown de 3.0 s entre eventos similares.
    * Supresión de toda la enumeración previa a `EnumerationCompleted` para evitar ruido al arrancar.
    * Supresión de 10.0 s tras reanudación de energía.
    * Filtrado dinámico insensible a mayúsculas/minúsculas mediante lista de dispositivos ignorados.
- **Servicios de Plataforma Reactivos sin Polling (`OpenDynamic.App.Services`):**
  - `NetworkService`: Suscripción al evento WinRT `NetworkInformation.NetworkStatusChanged`. Detección de nombre de perfil (`ProfileName`) sin requerir permisos invasivos de ubicación de Windows.
  - `DeviceService`: Notificaciones Win32 `WM_DEVICECHANGE` (`RegisterDeviceNotification`) para almacenamiento USB y WinRT `DeviceWatcher` (`AssociationEndpoint`) para periféricos Bluetooth en tiempo real.
  - Lectura de nivel de batería Bluetooth a través de `System.Devices.BatteryLevel` (0-100) cuando el controlador lo soporta.
  - Captura y reenvío de eventos `WM_POWERBROADCAST` (`PBT_APMSUSPEND`, `PBT_APMRESUME*`) en `IslandWindow.WndProc`.
  - **Regla de Oro 11 & Budget:** Desactivación y liberación completa de watchers y suscripciones nativas (`DeviceWatcher.Stop()`, `UnregisterDeviceNotification`, desuscripción de eventos) al desmarcar toggles en ajustes o al apagar la aplicación.
  - **Privacidad Estricta:** Registro exclusivo de categorías sanitizadas, tipos de evento y conteos en Serilog, sin nombres legibles de dispositivos.
- **Widgets Notificadores en Muesca Superior (`OpenDynamic.App.Widgets`):**
  - `NetworkWidget`: Prioridad 65 (`ActivityPriority.Network`), duración transitoria 3.0 s configurable. Vistas XAML `NetworkCompactView`, `NetworkExpandedView` y `NetworkSplitView`.
  - `DeviceWidget`: Prioridad 60 (`ActivityPriority.Device`), duración transitoria 3.0 s configurable. Vistas XAML `DeviceCompactView`, `DeviceExpandedView` y `DeviceSplitView` con iconos contextuales y porcentaje de batería.
- **Jerarquía Global de Prioridades y Resolución Determinista:**
  - `TimerAlert (100) > Battery (90) > Volume (80) > Network (65) > Device (60) > Timer (50) > Media (30) > Hardware (10)`.
- **Configuración, UI y Migración de Esquema (Schema v4):**
  - Incremento a `CurrentSchemaVersion = 4` en `AppSettings.cs` con propiedades `EnableNetworkAlerts`, `DefaultNetworkPriority`, `NetworkTransientDurationSeconds`, `EnableDeviceAlerts`, `DefaultDevicePriority`, `DeviceTransientDurationSeconds` y `IgnoredDeviceNames`.
  - Migración transparente en `SettingsService.Load()` para versiones `< 4`.
  - Tarjetas dedicadas de configuración en la pestaña "Widgets y Prioridades" de `SettingsWindow.xaml`, incluyendo gestión de dispositivos ignorados y atributos de accesibilidad `AutomationProperties.Name`.
- **Pruebas Automatizadas:**
  - Nuevas suites de pruebas unitarias completas: `NetworkAlertPolicyTests`, `DeviceAlertPolicyTests`, `SettingsServiceTests` (migración v4) y `PriorityResolverPhase11Tests`.
  - Suite completa de 248 pruebas unitarias en verde.

## [1.1.0-dev] - 2026-09-29 (Fase 10: Accesibilidad y Preferencias de Animación)

### Añadido
- **Perfiles de Movimiento en Core (`OpenDynamic.Core.Animation`):**
  - Enum `MotionMode` con opciones `Auto` (sigue la configuración de Windows), `Reduced` (sin rebote ni efectos decorativos) y `Full` (física elástica de resortes completa).
  - Clase inmutable `MotionProfile` con rigidez ($k$), amortiguamiento ($c$), masa ($m$), duración de transiciones de contenido y bandera booleana `AllowDecorative`.
  - Resolvedor determinista `MotionProfileResolver` que mapea el modo y la configuración del sistema operativo:
    * En `Reduced` o `Auto` sin animaciones de Windows: amortiguamiento crítico exacto ($k=400, c=40, m=1 \rightarrow \zeta = 1.0$) garantizando 0% de sobreimpulso (overshoot), transiciones de contenido de 120 ms ($\le 150\text{ ms}$) y desactivación de efectos decorativos.
    * En `Full` o `Auto` con animaciones de Windows activas: física de resorte natural ($k=280, c=24, m=1 \rightarrow \zeta \approx 0.717$) con rebote elástico suave de ~4% y transiciones de 250 ms.
  - **Regla de Oro 5 cumplida:** Cero dependencias de namespaces de Windows, Win32 o WPF en `OpenDynamic.Core`.
- **Detección Reactiva de Preferencias de Windows (`OpenDynamic.App`):**
  - Detección 100% reactiva en `IslandWindow.WndProc` interceptando el mensaje nativo `WM_SETTINGCHANGE (0x001A)` con `SPI_GETCLIENTAREAANIMATION` y mediante `SystemParameters.StaticPropertyChanged`.
  - **Reglas de Oro 1 y 11 cumplidas:** Prohibición absoluta de polling o timers periódicos para consultar la configuración del sistema operativo.
  - Preservación de inercia y velocidad: `ApplyProfile()` en `IslandAnimator` actualiza rigidez y amortiguamiento de los resortes en vuelo sin reiniciar ni alterar `Velocity` o `Value`, evitando congelamientos visuales o discontinuidades.
- **Supresión de Animaciones Decorativas en Widgets:**
  - Desacoplamiento mediante el mensaje `MotionProfileChangedMessage` transmitido a través de `WeakReferenceMessenger`.
  - `MediaWidget`: propiedad reactiva `IsDecorativeAllowed` y `EqualizerVisibility` que colapsa y oculta las barras simuladas del ecualizador cuando el movimiento está reducido.
  - `TimerWidget`: desactiva animaciones decorativas parpadeantes al expirar la cuenta regresiva, manteniendo resaltado estático carmesí y sonido.
- **Modo de Alto Contraste y UI Automation para Lectores de Pantalla:**
  - `AccessibilityThemeManager` en infraestructura para aplicar de forma reactiva temas de Alto Contraste ante cambios en `SystemParameters.HighContrast`.
  - Recursos dinámicos en `App.xaml` vinculados a `SystemColors.*Key`, garantizando bordes de 1 DIP visibles y fondo negro sólido en modo de alto contraste para máxima legibilidad.
  - Enlaces de accesibilidad UI en todas las vistas XAML (`AutomationProperties.Name`, `AutomationProperties.HelpText`, y `AutomationProperties.LiveSetting="Polite"`) en la muesca y en widgets de música, volumen, batería, hardware y temporizador.
- **Persistencia, Configuración y Migración de Esquema (Schema v3):**
  - Propiedad `MotionMode` persistida en `AppSettings.cs` con valor predeterminado `Auto`.
  - Incremento a `CurrentSchemaVersion = 3` con migración transparente e inmediata en `SettingsService.Load()` para esquemas v1 y v2 sin pérdida de datos.
  - Selector accesible en `SettingsWindow.xaml` con información y diagnóstico en tiempo real del estado de efectos de animación de Windows.
- **Pruebas Automatizadas:**
  - 15 nuevas pruebas unitarias en `MotionProfileTests` y `SettingsServiceTests` verificando la resolución de perfiles, física sin sobreimpulso en trayectorias crecientes y decrecientes, y migración segura de esquemas v1/v2 a v3.
  - Suite completa de 213 pruebas unitarias en verde.

## [1.0.0] - 2026-09-29 (Fase 9: Empaquetado, Documentación y Release Beta - Hito M6)

### Añadido
- **Instalador de Windows de Alta Fidelidad (Inno Setup 6):**
  - Script oficial `installer/setup.iss` para compilar el instalador `openDynamic-setup.exe`.
  - **Regla de Oro 3 cumplida (Cero privilegios UAC):** Instalación por usuario (`PrivilegesRequired=lowest`), instalando de forma predeterminada en `{localappdata}\Programs\openDynamic` (`{autopf}\openDynamic`) sin requerir permisos de administrador.
  - **Desinstalador limpio y respetuoso:**
    - Purga incondicional de la entrada de autostart en el registro de Windows (`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`).
    - Eliminación completa de binarios y accesos directos.
    - Cuadro de diálogo interactivo que consulta al usuario si desea conservar o eliminar sus datos de configuración (`%AppData%\openDynamic\settings.json`) y registros (`%LocalAppData%\openDynamic\logs`).
  - **Detección inteligente del runtime:** Verificación en el registro de la presencia de Microsoft .NET 10 Desktop Runtime (x64) con enlace de descarga oficial en un solo clic si no se encuentra instalado.
- **Flujo de Trabajo Automatizado de CI/CD para GitHub Releases (`.github/workflows/release.yml`):**
  - Disparo automático en GitHub Actions al empujar tags con patrón `v*` (ej. `v1.0.0`).
  - Pipeline en `windows-latest`: Checkout, Setup .NET 10, compilación en Release con `TreatWarningsAsErrors=true`, ejecución completa de la suite de 198 pruebas unitarias en verde, publicación ReadyToRun (R2R), empaquetado de archivo portátil (.zip), compilación del instalador con Inno Setup y publicación de ambos artefactos adjuntos a la GitHub Release oficial mediante `softprops/action-gh-release@v2`.
- **Documentación Técnica Profunda (`docs/arquitectura.md`):**
  - Diagrama conceptual de capas (Core vs. App) en Mermaid detallando el desacoplamiento total de `OpenDynamic.Core` (net10.0, cero dependencias de Windows/WPF) y `OpenDynamic.App` (WPF, Win32 P/Invoke, WinRT).
  - Diagrama de flujo y orquestación del `IslandOrchestrator`, arbitraje determinista de prioridades y gestión de actividades transitorias y continuas.
  - Diagrama de estados FSM (`Hidden`, `Compact`, `Expanded`, `Split`) y geometría adaptativa del Notch.
  - **Guía de 10 pasos para desarrolladores externos:** Manual exhaustivo con código de ejemplo de extremo a extremo para implementar un widget nuevo (`WeatherWidget`) con su servicio, modelo, interfaz `IWidget`, vistas XAML, registro DI y pruebas unitarias.
- **Registro de Decisiones de Arquitectura:**
  - `ADR-016`: Justificación formal y análisis comparativo entre distribución dependiente de framework (.NET 10 Desktop Runtime) vs. auto-contenido (Self-Contained), analizando tamaño final (~11 MB vs ~75 MB), consumo de memoria, seguridad y portabilidad.
  - `ADR-017`: Rediseño estético y geométrico de píldora flotante a muesca rectangular superior (Notch) anclada al bisel de la pantalla a 0 DIP.
- **Rediseño Visual a Muesca Rectangular Superior (Upper Notch UI):**
  - Anclaje exacto al marco superior absoluto de la pantalla (`OffsetY = 0.0 DIP`).
  - Esquinas asimétricas: esquinas superiores ortogonales a 0 DIP pegadas al bisel, esquinas inferiores redondeadas (14 DIP en compacto/split, 16 DIP en expandido).
  - Recorte geométrico exacto (`CreateNotchClipGeometry`) con `PathGeometry` congelada para eliminar desbordamientos visuales.
  - Migración automática a Schema v2 en `SettingsService`.
- **Cierre Consolidado de Hitos:**
  - Hito M1: Overlay transparente, Z-order topmost reactivo, click-through por píxel y soporte PerMonitorV2 DPI.
  - Hito M2: Motor de física de resortes elásticos (Spring physics), máquina de estados FSM y bucle de render reactivo (0.0% CPU en reposo).
  - Hito M3: Integración multimedia WinRT (GSMTC), control de volumen en tiempo real (NAudio CoreAudio), alertas de batería sin polling (WM_POWERBROADCAST) y detección reactiva de pantalla completa.
  - Hito M4: Monitorización nativa de hardware (Win32 GetSystemTimes/GlobalMemoryStatusEx), temporizador por marca de tiempo objetivo (Pomodoro) y modo Split multitasking con intercambio de satélite.
  - Hito M5: Persistencia settings.json con debounce, bandeja del sistema (H.NotifyIcon), ventana de Ajustes MVVM con tema oscuro y atajos de teclado globales Win32 (RegisterHotKey).
  - Hito M6: Empaquetado oficial con Inno Setup, CI/CD de releases y documentación de arquitectura v1.0.0.

## [0.9.0] - 2026-09-29 (Fase 8: Optimización y Pruebas de Rendimiento - Hito Previo a M6)

### Añadido
- Compilación estricta sin advertencias:
  - Activación de `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` de forma centralizada en `Directory.Build.props`.
  - Compilación en Debug y Release con exactamente 0 advertencias y 0 errores.
- Rediseño y estilización oscura de alta fidelidad para `ComboBox` en `SettingsWindow.xaml`:
  - Plantilla de control completa `DarkComboBox` y `DarkComboBoxItem` con fondo `#1C1C1E` / `#2C2C2E`, texto blanco `#FFFFFF`, bordes `#3B4252` y selector Popup oscuro con resaltado de selección azul (`#2563EB` / `#1D4ED8`), erradicando texto blanco sobre fondo blanco nativo.
- Robustez ante eventos del sistema operativo:
  - Suspensión y reanudación de energía: Intercepción de `WM_POWERBROADCAST` (`PBT_APMSUSPEND`, `PBT_APMRESUMEAUTOMATIC`, `PBT_APMRESUMESUSPEND`) y `SystemEvents.PowerModeChanged` en `IslandWindow.xaml.cs` para suspender temporizadores/animaciones (`SuspendForPower()`) y restaurar geometría, Z-order topmost y lecturas de batería limpiamente al despertar (`ResumeFromPower()`).
  - Cambio de resolución y pantallas: Intercepción de `WM_DISPLAYCHANGE` y `WM_DPICHANGED` para recalcular dinámicamente y reubicar la cápsula sin reiniciar la aplicación.
  - Tolerancia ante desconexión de pantallas: Fallback seguro y automático al monitor principal en `WindowPositioner.PositionWindow` si el monitor de destino fue desconectado.
  - Reintentos en inicialización de GSMTC: `MediaService.InitializeAsync` incorpora reintentos con retardo progresivo ante arranques tardíos del servicio multimedia de Windows.
  - Confirmación del reinicio de Shell: Verificación del mensaje registrado `TaskbarCreated` para mantener viva y recrear la bandeja tras reinicios de `explorer.exe`.
- Auditoría de recursos y diagnóstico en tiempo real:
  - Telemetría en vivo en `IslandDebugWindow`: Contadores de Working Set en MB, memoria en Heap de GC, estado de suscripción a `CompositionTarget.Rendering` y temporizadores activos con refresco periódico.
  - Mediciones reales registradas en `README.md`: Working Set en reposo de 27.9 MB (meta < 100 MB), Private Memory de 5.2 MB, CPU en reposo de 0.00% (meta < 0.5%) y CPU animando < 1.0%.
  - Desuscripción de eventos y hooks nativos (`SystemEvents.PowerModeChanged`, timers `Tick`, `HwndSource` hooks) auditada en `Dispose` y `OnClosed`.
- Matriz exhaustiva de pruebas documentada en `docs/pruebas.md`:
  - Cobertura de 197 pruebas unitarias automatizadas en verde.
  - Protocolo de 16 casos de prueba manuales de entorno y sistema Windows (DPI mixto, suspensión, pantalla completa, resoluciones 1080p/1440p/4K, etc.).
- Optimización de publicación:
  - Configuración de ReadyToRun (R2R) en `OpenDynamic.App.csproj` (`<PublishReadyToRun>true</PublishReadyToRun>`).
  - Verificación de publicación `win-x64` con IL precompilado a código nativo Ahead-of-Time para arranque instantáneo sin trimming destructivo.

## [0.8.0] - 2026-09-29 (Fase 7: Bandeja, Ajustes, Atajos e Inicio Automático - Hito M5)

### Añadido
- Persistencia robusta y versionada de configuración en `OpenDynamic.Core.Settings`:
  - `AppSettings`: Versionado de esquema (`SchemaVersion = 1`), propiedades para monitor, desplazamientos X/Y, dimensiones de cápsula (ancho/alto/radio), factores de escala, umbrales, atajos y arranque.
  - `ISettingsService` y `SettingsService`: Carga y guardado en `%AppData%\openDynamic\settings.json`.
  - Tolerancia a fallos: Respaldo automático `settings.json.bak` si el archivo está corrupto o ilegible, registro en Serilog y regeneración limpia de valores predeterminados sin colapsar la app.
  - Escritura con debounce de 500 ms mediante temporizadores para evitar el desgaste innecesario de unidades SSD durante el arrastre de sliders.
  - Métodos `SaveImmediate()` y `Dispose()` para vaciado instantáneo antes del cierre.
- Integración de bandeja del sistema en `OpenDynamic.App.Infrastructure.TrayIconManager`:
  - Uso exclusivo de `H.NotifyIcon.Wpf` cumpliendo estrictamente con la Regla de Oro 3 (cero WinForms).
  - Clic izquierdo para alternar la visibilidad de la isla entre visible y oculta (`Hidden`).
  - Menú contextual completo con tema oscuro: *Abrir Ajustes*, *Conmutar Monitor de Hardware*, *Reiniciar Posición* y *Salir de openDynamic*.
  - Limpieza garantizada: Liberación explícita con `Dispose()` sin dejar iconos fantasma al pasar el puntero.
- Panel de configuración independiente en `OpenDynamic.App.Views.SettingsWindow`:
  - Arquitectura MVVM completa con `SettingsViewModel` (`CommunityToolkit.Mvvm`).
  - Regla de oro de entrada: Entradas de texto y controles de configuración aislados estrictamente a esta ventana; la cápsula flotante jamás roba foco ni aloja `TextBox`.
  - Aplicación de cambios en tiempo real (*Live Updates*): Ajustar sliders (offsets, ancho, alto, radio), switches de hardware/GPU o temporizador se refleja en vivo en la isla sin necesidad de reiniciar la app.
  - Ocultamiento reactivo al cerrar la ventana (`Hide()`) para preservar el estado y ciclo de vida de la aplicación.
- Atajos de teclado globales en `OpenDynamic.App.Services.HotkeyService`:
  - API nativa Win32 `RegisterHotKey` / `UnregisterHotKey` vinculada al procedimiento de ventana (`WndProc`) vía `HwndSource`. Prohibidos hooks globales de bajo nivel (Regla de oro 2).
  - Atajo predeterminado `Win+Ctrl+I` para alternar la visibilidad de la isla.
  - Parser y normalizador de acordes `HotkeyParser` en `OpenDynamic.Core.Hotkeys`.
  - Detección pacífica de colisiones (error Win32 1409) con aviso visual en la UI de Ajustes sin lanzar excepciones.
- Servicio de inicio con Windows en `OpenDynamic.App.Services.AutostartService`:
  - Modificación del registro `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` sin elevación de privilegios UAC (Regla de oro 3).
  - Verificación y corrección automática de la ruta del ejecutable ante cambios de ubicación de la app.
- Batería de 30 nuevas pruebas unitarias en `OpenDynamic.Tests` (total 193 en verde):
  - `SettingsServiceTests`: Creación por defecto, carga válida, respaldo `.bak` ante JSON corrupto, migración de esquema, debounce de 500ms, vaciado inmediato y clonación.
  - `HotkeyParserTests`: Parser de teclas, validación de combinaciones con modificadores, flag `NoRepeat` y normalización.
  - `AutostartServiceTests`: Verificación de clave de registro, adición con comillas, eliminación y sincronización ante cambio de ruta ejecutable.
  - `IslandPositionCalculatorTests`: Cálculo geométrico con `offsetXDip` y DPI scaling.

## [0.7.0] - 2026-09-28 (Fase 6: Hardware, Temporizador y Modo Split Multitasking - Hito M4)

### Añadido
- Modelos de dominio y controlador de temporizador por marca de tiempo en `OpenDynamic.Core.Timer` (Regla de oro 5):
  - `TimerMode`: Modos operativo estándar, Pomodoro trabajo (`PomodoroWork`, 25 min) y descanso (`PomodoroBreak`, 5 min).
  - `TimerState`: Ciclo de vida determinista (`Stopped`, `Running`, `Paused`, `Completed`).
  - `TimerSnapshot`: Snapshot inmutable con `RemainingTime`, `TotalDuration`, `TargetEndTimeUtc`, `FormattedTime` (`mm:ss` / `hh:mm:ss`), `ProgressRatio` y `RemainingRatio`.
  - `ITimerController` y `TimerController`: Controlador sin deriva temporal sustentado en marca de tiempo objetivo (`TargetEndTimeUtc`), con soporte de reloj inyectable `System.TimeProvider` para pruebas unitarias deterministas sin esperas reales (`Thread.Sleep`).
- Modelos y cálculo puro de rendimiento de hardware en `OpenDynamic.Core.Hardware` (Regla de oro 5):
  - `HardwareSnapshot`: Snapshot inmutable de utilización de CPU, RAM (porcentaje y GB usados/totales) y GPU opcional.
  - `IHardwareMonitor`: Contrato para muestreo de recursos de hardware.
  - `HardwareCalculator`: Lógica pura de cálculo de deltas de Win32 `GetSystemTimes` ($\text{CPU\%} = (1.0 - \frac{\text{Idle}}{\text{Total}}) \times 100$), porcentaje de memoria y conversión a GB.
- P/Invoke nativo a Win32 en `NativeMethods` (Regla de oro 4):
  - `GetSystemTimes`: Lectura directa de tiempos de CPU (Idle, Kernel, User) de `kernel32.dll` sin librerías pesadas externas.
  - `GlobalMemoryStatusEx`: Lectura de estado de memoria física mediante estructura `MEMORYSTATUSEX`.
- Configuración extendida en `AppSettings`:
  - `DefaultHardwarePriority = 10`, `HardwareSamplingIntervalSeconds = 2.0`, `EnableHardwareMonitoring = true`.
  - `EnableGpuMonitoring = false` (estrictamente desactivado por defecto).
  - `DefaultTimerPriority = 50`, `DefaultTimerAlertPriority = 100`, `TimerAlertTransientDurationSeconds = 5.0`.
  - `PomodoroWorkDurationMinutes = 25`, `PomodoroBreakDurationMinutes = 5`.
- Servicio nativo de hardware `HardwareService` en `OpenDynamic.App.Services`:
  - Muestreo periódico aislado con `try/catch` y log en Serilog.
  - Método `ResetCpuBaseline()` para eliminar picos de delta tras períodos inactivos.
- Widget de monitorización de hardware `HardwareWidget` en `OpenDynamic.App.Widgets.Hardware`:
  - Condición estricta de visibilidad (Regla de oro 1: 0% CPU en reposo): el timer de 2.0s se activa ÚNICAMENTE si el widget está activamente visible en pantalla (`DisplayMode == Compact || Expanded || Split` e `IsVisibleOnIsland == true`). Al colapsar a `Hidden` o no estar visible, se detiene por completo.
  - Vistas `Compact` (CPU/RAM con colores dinámicos), `Expanded` (tarjetas métricas con barras de progreso) y `Split` (satélite circular 36x36).
- Widget de temporizador `TimerWidget` en `OpenDynamic.App.Widgets.Timer`:
  - Funciona con marca de tiempo continua aun si la isla está oculta (`Hidden`).
  - Prioridad 50 en ejecución continua; al finalizar, emite una actividad transitoria con Prioridad 100 durante 5.0 segundos con aviso visual crítico y sonido del sistema (`SystemSounds.Asterisk`).
  - Controles interactivos: Play/Pausa, Reinicio, +1m, +5m, alternancia rápida entre Temporizador, Pomodoro (25m) y Descanso (5m).
  - Vistas `Compact` (reloj y badge de Pomodoro), `Expanded` (display gigante, selectores segmentados, controles de transporte) y `Split` (satélite circular 36x36 con tiempo compacto).
- Modo Split Multitasking con Intercambio Interactivo en `IslandOrchestrator` e `IslandWindow` (Hito M4):
  - Convivencia de actividades continuas (ej. Temporizador P=50 en cápsula principal y Música P=30 en satélite circular).
  - Clic en burbuja satélite: Invoca `SwapSplitActivities()`, intercambiando de inmediato la actividad primaria y secundaria con cross-fade suave (80ms).
  - Clic en cápsula principal en Split: Expande la actividad primaria actualmente seleccionada (`RequestExpand()`).
  - Pre-emption de alertas transitorias (ej. Temporizador finalizado P=100, Batería P=90, Volumen P=80) que toman la cápsula y al expirar restauran el modo Split original.
- Batería de 27 nuevas pruebas unitarias en `OpenDynamic.Tests` (total 160 en verde):
  - `TimerControllerTests`: Expiración exacta, pausa/reanudación sin deriva, Pomodoro Trabajo/Descanso, adición de tiempo y formateo `mm:ss` / `hh:mm:ss`.
  - `HardwareCalculatorTests`: Deltas de CPU normales, carga completa, desbordamiento de inactividad, porcentaje de RAM y conversión a GB.
  - `PriorityResolverPhase6Tests`: Convivencia Timer + Media en Split, Media + Hardware en Split, Hardware solo en Compact, pre-emption de Timer Alert (100) sobre Batería (90) y Volumen (80), y restauración automática tras expirar alerta transitoria.

## [0.6.0] - 2026-09-28 (Fase 5: Volumen, Batería y Pantalla Completa)


### Añadido
- Modelos de dominio y contratos puros de audio en `OpenDynamic.Core.Audio` (Regla de oro 5):
  - `IVolumeController`: Contrato puro para lectura, modificación relativa/absoluta, mute y eventos de volumen.
  - `VolumeChangedEventArgs`: Argumentos inmutables de cambio de volumen y estado de mute.
  - `VolumeIconType`: Categorización de icono de altavoz (`Muted`, `Low`, `Medium`, `High`).
  - `VolumeCalculator`: Lógica pura de cálculo y normalización `[0.0, 1.0]`, cálculo de pasos por rueda del ratón (`CalculateLevelStep`), porcentaje entero `[0, 100]` y selección de icono.
- Modelos de dominio y tracker de alertas de energía en `OpenDynamic.Core.Power` (Regla de oro 5):
  - `IBatteryMonitor`: Contrato para monitoreo reactivo de energía y eventos de alerta sin polling.
  - `BatterySnapshot`: Snapshot inmutable de estado de batería (`Percent`, `IsCharging`, `HasBattery`).
  - `BatteryAlertKind`: Categorías de alerta (`None`, `ChargerConnected`, `ChargerDisconnected`, `LowBattery`, `CriticalBattery`).
  - `BatteryThresholdTracker`: Tracker puro de cruce de umbrales con histéresis (2%) que garantiza una sola notificación por cruce al descender del 20% y del 10% sin re-notificar en fluctuaciones de voltaje, con reinicio al conectar el cargador o subir por encima del umbral.
- Detección geométrica y de estado en `OpenDynamic.Core.Windowing`:
  - `FullscreenDetector`: Lógica pura para evaluar estados `QUNS_*` de Windows y cobertura completa de ventana sobre monitor (juegos y reproductores sin bordes).
- Configuración extendida en `AppSettings`:
  - `DefaultVolumePriority = 80`, `VolumeTransientDurationSeconds = 2.0`.
  - `DefaultBatteryPriority = 90`, `BatteryChargerTransientDurationSeconds = 3.0`, `BatteryWarningTransientDurationSeconds = 3.0`.
  - `BatteryLowThresholdPercent = 20`, `BatteryCriticalThresholdPercent = 10`.
  - `HideOnFullscreen = true`.
- Servicio nativo de audio `VolumeService` en `OpenDynamic.App.Services`:
  - Implementación con NAudio (`MMDeviceEnumerator`, `AudioEndpointVolume`).
  - Soporte de cambio de dispositivo en caliente mediante registro COM nativo de `IMMNotificationClient`.
  - Todas las operaciones COM encapsuladas en `try/catch` con registro estructurado en Serilog (Regla de oro 4).
- Widget de volumen transitorio `VolumeWidget` y vistas en `OpenDynamic.App.Widgets.Volume`:
  - Actividad transitoria (Prioridad 80, duración 2.0 s).
  - Vistas `Compact`, `Expanded` y `Split` con iconos reactivos y barra de volumen elegante.
  - Interacción directa con la rueda del ratón sobre la cápsula para ajustar volumen y reiniciar inmediatamente el temporizador de gracia de 2 segundos.
- Servicio reactivo de energía `PowerService` en `OpenDynamic.App.Services`:
  - Escucha de mensajes `WM_POWERBROADCAST` en el `WndProc` de `IslandWindow`.
  - Registro de notificaciones Win32 `RegisterPowerSettingNotification` (`GUID_ACDC_POWER_SOURCE` y `GUID_BATTERY_PERCENTAGE_REMAINING`).
  - Cero polling (Regla de oro 1: CPU ~0% en reposo, ningún timer periódico).
- Widget de batería transitorio `BatteryWidget` y vistas en `OpenDynamic.App.Widgets.Battery`:
  - Actividad transitoria (Prioridad 90, duración 3.0 s).
  - Alertas visuales inmediatas al conectar o desconectar el cargador y al cruzar los umbrales de 20% y 10%.
- Detector de pantalla completa `FullscreenWatcher` en `OpenDynamic.App.Windowing`:
  - Detección combinada de `SHQueryUserNotificationState` y `EVENT_SYSTEM_FOREGROUND` con `SetWinEventHook`.
  - Detección de juegos exclusivos DirectX/Vulkan y aplicaciones maximizadas sin bordes (YouTube en navegador, VLC).
  - Ocultamiento inmediato de la isla (`SuspendForFullscreen`), cancelación de timers activos y congelación del animador (`SnapTo(Hidden)`). Restauración suave al salir de pantalla completa (`ResumeFromFullscreen`).
- Suite de 43 nuevas pruebas unitarias en `OpenDynamic.Tests` (131 pruebas en verde en total):
  - `VolumeCalculatorTests`: Normalización, pasos con rueda del ratón, porcentaje y categorías de icono.
  - `BatteryThresholdTrackerTests`: Alertas de cargador, cruce único de umbrales 20% y 10%, histéresis ante fluctuaciones, reseteo al conectar cargador e ignorar PCs sin batería.
  - `PriorityResolverPhase5Tests`: Precedencia de Volumen (80) sobre Media (30), Batería (90) sobre Volumen (80), y auto-expiración / reinicio de tiempo de vida.
  - `FullscreenDetectorTests`: Detección de estados `QUNS_*` y evaluación de geometría de ventana vs monitor.

## [0.5.0] - 2026-09-28 (Fase 4: Widget Multimedia - GSMTC)

### Añadido
- Modelos de dominio y contratos multimedia puros en `OpenDynamic.Core.Media` (Regla de oro 5):
  - `MediaPlaybackStatus`: Estados de reproducción (`Closed`, `Opened`, `Changing`, `Stopped`, `Playing`, `Paused`).
  - `MediaPlaybackCapabilities`: Capacidades interactivas de la sesión (`CanPlay`, `CanPause`, `CanTogglePlayPause`, `CanSkipNext`, `CanSkipPrevious`, `CanSeek`).
  - `MediaPlaybackInfo`, `MediaTimelineInfo` y `MediaPropertiesInfo`: Snapshots inmutables de reproducción, límites de tiempo y metadatos libres de tipos de UI.
  - `IMediaSession` e `IMediaService`: Contratos de abstracción para sesiones y servicios de transporte multimedia de Windows.
  - `MediaProgressCalculator`: Lógica pura de cálculo de progreso extrapolado localmente, cálculo de ratio normalizado `[0.0, 1.0]` y formateo de tiempo (`mm:ss`, `h:mm:ss`).
  - `MediaActivityController`: Coordinador de ciclo de vida de sesión que gestiona la regla de tiempo de gracia de 10 segundos tras pausa, reanudación y terminación inmediata al cerrar el reproductor.
  - `AppSettings` en `OpenDynamic.Core.Settings`: Configuración con `MediaPauseGracePeriodSeconds = 10` y `DefaultMediaPriority = 30`.
- Servicio de transporte multimedia nativo `MediaService` y sesión `WinRtMediaSession` en `OpenDynamic.App.Services`:
  - Conexión a `GlobalSystemMediaTransportControlsSessionManager` (WinRT `Windows.Media.Control`).
  - Todas las invocaciones a WinRT/COM encapsuladas en `try/catch` con registro estructurado en Serilog (Regla de oro 4).
  - Desuscripción meticulosa (`-=`) de eventos al cambiar de sesión o cerrar reproductores para evitar fugas de memoria y callbacks sobre sesiones muertas.
  - Extracción de miniatura (`Thumbnail`) con lectura asíncrona a memoria local y ejecución de `BitmapImage.Freeze()` antes de su entrega al hilo de UI.
  - Activación de aplicación emisora (`TryActivateApp`) mediante `SourceAppUserModelId` o nombre de ejecutable (`SetForegroundWindow`, `ShowWindow`).
- Widget multimedia `MediaWidget` en `OpenDynamic.App.Widgets.Media`:
  - Vista `Compact`: carátula miniatura (24x24) con esquinas redondeadas y escalado de alta calidad, título y artista con recorte elíptico, y mini barras de onda de audio.
  - Vista `Expanded`: carátula grande (52x52), título, artista, álbum, barra de progreso interactiva (seek en vivo por clic o arrastre), tiempos `mm:ss / mm:ss` y controles de transporte (Anterior, Play/Pausa central destacado y Siguiente).
  - Vista `Split`: carátula circular (26x26) adaptada al satélite multitasking de 36x36 con insignia verde activa en reproducción.
  - Regla de oro 1 (CPU ~0% en reposo): Prohibición total de sondeo (polling). El temporizador `DispatcherTimer` de 1s de extrapolación de progreso se activa ÚNICAMENTE cuando la cápsula está en estado `Expanded` Y la sesión está en reproducción (`Playing`). En `Compact`, `Split`, `Hidden` o en Pausa, el timer se detiene de inmediato.
  - Tiempo de gracia de 10 segundos tras pausa: al pausar, la actividad se mantiene visible 10s antes de pasar a `IsActive = false`. Si se reanuda antes de los 10s, se cancela el temporizador y continúa activa; si el reproductor se cierra, la actividad se retira de inmediato.
- Batería de 26 nuevas pruebas unitarias en `OpenDynamic.Tests` (total 88 en verde):
  - Extrapolación de posición en reproducción, pausa, desbordamiento de límites y formato de tiempo en `MediaProgressCalculatorTests`.
  - Simulación de transiciones Play -> Pause con timeout de 10s, reanudación y cierre inmediato en `MediaActivityControllerTests`.
  - Resolución de prioridades con el nuevo widget Media en `PriorityResolverMediaTests`.

## [0.4.0] - 2026-09-28 (Fase 3: Arquitectura de Widgets y Orchestrator - Base M4)

### Añadido
- Modelos de dominio y contratos de actividad puros en `OpenDynamic.Core.Widgets` (Regla de oro 5):
  - `IActivitySource`: Contrato para emisores de actividades (`Id`, `Priority`, `IsActive`, `IsTransient`, `LastActivatedUtc`, `TransientDuration`, `Changed`).
  - `ActivityPriority`: Niveles estándar de prioridad (`Idle = 0`, `Low = 10`, `Normal = 50`, `High = 100`, `TransientNotice = 200`, `Critical = 500`).
  - `IslandActivity`: Modelo inmutable de snapshot de actividad.
  - `WidgetDisplayMode`: Enumeración de modos de renderizado (`Compact`, `Split`, `Expanded`).
  - `PriorityResult`: Resultado inmutable de resolución con fuente primaria, secundaria, estado sugerido y próximo tiempo de expiración.
- Servicio de resolución de prioridades determinista `PriorityResolver` en `OpenDynamic.Core.Widgets`:
  - Evaluación por prioridad numérica descendente (mayor valor gana).
  - Desempate determinista por recencia de activación (`LastActivatedUtc`) y por identificador (`Id`).
  - Pre-emption de actividades transitorias con tiempo de vida o expiración: toman el control como fuente primaria exclusiva en `Compact`, y al expirar devuelven automáticamente el control a las actividades en segundo plano.
  - Soporte de lista vacía o fuentes inactivas retornando estado inactivo/reposo (`Hidden`).
- Interfaz `IIslandWidget` y clase base `IslandWidgetBase` en `OpenDynamic.App.Widgets`:
  - Integración de `IActivitySource` con `IDisposable` y fábricas de vistas WPF (`CreateCompactView()`, `CreateExpandedView()`, `CreateSplitView()`).
  - Métodos de ciclo de vida: `Initialize()`, `OnExpand()`, `OnCollapse()`.
  - Integración MVVM con `CommunityToolkit.Mvvm` (`ObservableObject`).
- Bus de eventos desacoplado con `WeakReferenceMessenger` de CommunityToolkit.Mvvm:
  - Mensajes de actividad (`ActivityChangedMessage`, `WidgetRegisteredMessage`, `WidgetUnregisteredMessage`, `ExpandRequestedMessage`, `CollapseRequestedMessage`, `IslandStateChangedMessage`) que evitan fugas de memoria por referencias fuertes.
- Orquestador central `IslandOrchestrator` en `OpenDynamic.App.Orchestration`:
  - Autoridad EXCLUSIVA sobre transiciones en `IslandStateMachine`.
  - Coordinación de prioridades de widgets, entrega de vistas a `IslandView` y gestión de temporizadores de expiración de transitorios de un solo disparo (CPU ~0% en reposo).
  - Aislamiento estricto de fallos (Regla de oro 4): toda inicialización y creación de vistas de widgets está protegida en bloques `try/catch` con registro en Serilog y puesta en cuarentena de widgets con excepciones sin desestabilizar la cápsula ni la aplicación.
- Componente de presentación `IslandView.xaml` en `OpenDynamic.App.Views`:
  - Cápsula principal con animación y recorte elástico.
  - Soporte visual nativo para modo multitasking `Split`: cápsula principal más burbuja satélite circular (`36x36` con radio 18) adyacente con espacio transparente para clics intermedios.
  - Transiciones de opacidad suaves (cross-fade) al conmutar contenido entre widgets.
- Widgets de demostración `DemoWidgetA` y `DemoWidgetB` condicionados estrictamente a compilación `#if DEBUG`:
  - Simulación de actividad continua (Música y Temporizador) y avisos transitorios de 3 segundos con auto-expiración.
  - Controles interactivos y telemetría en tiempo real en `IslandDebugWindow`.
  - Cero código de demostración en compilaciones Release.
- Suite de 15 pruebas unitarias exhaustivas en `OpenDynamic.Tests.Widgets.PriorityResolverTests`, elevando el total de pruebas a 62 en verde.

## [0.3.0] - 2026-09-28 (Fase 2: Animación y Estados - M2)

### Añadido
- Motor de física de oscilador armónico amortiguado `Spring` en `OpenDynamic.Core.Animation` con integración semi-implícita de Euler y sub-pasos temporales (*sub-stepping* a $h = 1/240\text{ s}$), garantizando estabilidad numérica incondicional ante picos de $\Delta t$ o tirones de fotogramas sin divergencia.
- Conservación de inercia y velocidad continua (`Velocity`) al reorientar el destino dinámicamente (`Target`), posibilitando transiciones fluidas en vuelo sin tirones visuales.
- Máquina de estados finita `IslandStateMachine` en `OpenDynamic.Core.State` con estados formales `Hidden`, `Compact`, `Split` y `Expanded`, y reglas estrictas de transición (prohibición taxativa de `Hidden -> Expanded` directo, requiriendo paso por `Compact`).
- Definición y resolución de geometrías de cápsula con `IslandLayout` (Compact 160x36 R:18, Expanded 400x160 R:24, Split 260x36 R:18, Hidden 80x4 R:2 Op:0.01).
- Eliminación de `Visibility.Collapsed` en la cápsula para mantener activa la capacidad de hit-testing en WPF, permitiendo que la micro-muesca de reposo en `Hidden` (80x4 DIPs a 1% de opacidad) detecte `MouseWheel` (scroll down), `MouseEnter` o clic izquierdo para restaurar la cápsula a `Compact` de forma inmediata.
- Coordinador de animación `IslandAnimator` en `OpenDynamic.App.Animation` con gestión estricta del ciclo de vida de `CompositionTarget.Rendering`: suscripción exclusiva durante el vuelo de resortes y desuscripción inmediata al reposo (`IsSettled`), garantizando 0% de uso de CPU en reposo (Regla de oro 1). Acotación de $\Delta t$ a 50 ms.
- Eliminación del margen superior estático en `IslandWindow.xaml` (`Margin="0"` en `CapsuleBorder`), delegando toda la distancia al cálculo exacto de 8 DIPs de `IslandPositionCalculator`.
- Recorte de contenido interno mediante `ClipToBounds="True"` y `RectangleGeometry` adaptativa en `CapsuleBorder` para prevenir artefactos o fugas gráficas durante los rebotes elásticos.
- Interacciones directas de ratón sobre la cápsula:
  - Detección de hover sin sobrecoste de CPU: temporizador reactivo de 150 ms para expandir y 400 ms de margen al retirar el puntero antes de contraer a `Compact`.
  - Detección de `MouseEnter` en la micro-muesca de `Hidden` para despertar automáticamente la isla a `Compact`.
  - Clic primario para alternar de forma bidireccional entre `Compact` y `Expanded` (o restaurar desde `Hidden`).
  - Rueda de ratón (`MouseWheel`) hacia arriba para contraer/ocultar (`Expanded -> Compact -> Hidden`) y hacia abajo para revelar/expandir (`Hidden -> Compact -> Expanded`).
- Panel visual interactivo `IslandDebugWindow` condicionado estrictamente a compilación `#if DEBUG` con telemetría en vivo del render loop, conmutadores manuales de estado y controles deslizantes de rigidez, amortiguamiento y perfiles.
- Batería de 33 nuevas pruebas unitarias en `OpenDynamic.Tests` (convergencia, inercia, estabilidad ante $\Delta t$ extremos, sub-stepping, transiciones FSM válidas e inválidas, y layout), elevando la cobertura a 47 pruebas automáticas.

## [0.2.0] - 2026-09-28 (Fase 1: Ventana Overlay Flotante - M1)


### Añadido
- Ventana overlay `IslandWindow` en `OpenDynamic.App.Windowing` sin bordes (`WindowStyle="None"`, `AllowsTransparency="True"`, `Background="Transparent"`) con dimensiones fijas de 640x240 DIP y cápsula negra centrada (160x36 DIP).
- Configuración de estilos extendidos Win32 mediante `GetWindowLongPtr` y `SetWindowLongPtr`:
  - `WS_EX_TOOLWINDOW`: exclusión de la barra de tareas y del selector Alt+Tab.
  - `WS_EX_NOACTIVATE`: prevención de activación de ventana.
  - `WS_EX_TOPMOST`: elevación de la ventana sobre las aplicaciones estándar.
  - Prohibición explícita de `WS_EX_TRANSPARENT`, delegando el click-through al canal alfa nativo de WPF por píxel en capas.
- Intercepción de `WM_MOUSEACTIVATE` en el procedimiento de ventana (`WndProc`), retornando `MA_NOACTIVATE (3)` para garantizar que interactuar con la cápsula no robe el foco de teclado de la ventana activa.
- Sistema reactivo de z-order con `ForegroundWatcher` utilizando `SetWinEventHook` (`EVENT_SYSTEM_FOREGROUND`) para reafirmar `HWND_TOPMOST` sin sondeo ni temporizadores periódicos (CPU 0% en reposo).
- Servicio `WindowPositioner` para consulta de monitores, resolución y DPI mediante P/Invoke a Win32 (`MonitorFromWindow`, `MonitorFromPoint`, `GetMonitorInfo`, `GetDpiForMonitor`, `GetDpiForWindow`) sin dependencias de `System.Windows.Forms`.
- Reposicionamiento automático ante eventos del sistema `WM_DISPLAYCHANGE` y `WM_DPICHANGED`.
- Menú contextual temporal en la cápsula para cierre controlado de la aplicación durante desarrollo y pruebas.
- Modelos puros y motor de cálculo en `OpenDynamic.Core.Positioning`:
  - `IslandPositionCalculator`: algoritmo puro de cálculo de coordenadas físicas (X, Y, Ancho, Alto) y margen superior.
  - `MonitorArea`, `DisplayDpi`, `WindowDimensions` y `CalculatedWindowPlacement`.
- Batería de pruebas unitarias en `OpenDynamic.Tests.Positioning.IslandPositionCalculatorTests` validando escenarios a 100% (96 DPI), 125% (120 DPI), 150% (144 DPI) y 200% (192 DPI), configuraciones multimonitor y límites de entrada.

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
