# openDynamic v2.0.0 — Notas de Lanzamiento Oficiales (Release Notes)

**Fecha de lanzamiento:** Octubre de 2026  
**Plataforma:** Windows 10 (2004 / Build 19041+) y Windows 11 (x64)  
**Runtime:** Microsoft .NET 10 Desktop Runtime (x64)  
**Licencia:** MIT

---

## 🌟 Resumen Ejecutivo

**openDynamic v2.0.0** representa la consolidación arquitectónica, funcional y de rendimiento más importante del proyecto desde el lanzamiento de `v1.0.0`. Esta versión mayor incorpora **10 nuevas fases de ingeniería** (Fases 10 a 16 y Fases 19 a 21), un ecosistema de **12 widgets reactivos** integrados en la **Upper Notch UI** (muesca rectangular superior anclada al bisel de la pantalla), y una **auditoría integral de fin a fin** que certifica un consumo de **0.00% de CPU en reposo** con cero temporizadores activos cuando la muesca está oculta.

---

## 🚀 Nuevas Funcionalidades (de v1.0.0 a v2.0.0)

1. **Accesibilidad Integral y Perfiles de Movimiento (Fase 10):**
   - Perfiles de animación `Auto` (sincronizado reactivamente con `WM_SETTINGCHANGE`), `Full` (física de resortes subamortiguados) y `Reduced` (amortiguamiento crítico $\zeta = 1.0$, cero sobreimpulso y transiciones $\le 150\text{ ms}$).
   - Soporte reactivo de Alto Contraste de Windows (`SystemParameters.HighContrast`) y propiedades completas de UI Automation / Narrador de Windows.
2. **Conectividad de Red y Periféricos USB / Bluetooth Reactivos (Fase 11):**
   - Alertas contextuales de cambios de red Wi-Fi / Ethernet con calidad de señal y detección de portal cautivo (`NetworkWidget`, prioridad 65).
   - Notificaciones consolidadas de conexión y desconexión de periféricos USB y Bluetooth con clasificación automática de categoría y porcentaje de batería (`DeviceWidget`, prioridad 60).
3. **Cronómetro de Precisión y Múltiples Temporizadores Simultáneos (Fase 12):**
   - Hasta **5 temporizadores concurrentes** con selección automática del próximo a expirar, preajustes rápidos, persistencia atómica y cola secuencial de alertas (`TimerWidget`, prioridad 50 / alerta 95).
   - Cronómetro de alta precisión basado en marcas de tiempo con registro de vueltas (*laps*), vuelta más rápida/lenta y coexistencia en Modo Split (`StopwatchWidget`, prioridad 45).
4. **Color Dinámico de Carátula y Gestos Táctiles / Ratón en Multimedia (Fase 13):**
   - Extracción cuantizada del color dominante de la carátula con ajuste automático de luminancia para garantizar un contraste mínimo **WCAG AA ($\ge 4.5:1$)**.
   - Gestos de arrastre horizontal con retorno elástico y soporte de rueda horizontal (`WM_MOUSEHWHEEL`) para cambiar de pista.
5. **Portapapeles Reciente y Seguro en Memoria RAM (Fase 14 — Opt-In):**
   - Historial volátil en memoria RAM (desactivado por defecto) mediante `AddClipboardFormatListener` (`WM_CLIPBOARDUPDATE`).
   - Exclusión automática de gestores de contraseñas (`ClipboardHistoryIgnore`, `CanIncludeInClipboardHistory`, `ExcludeClipboardContentFromMonitorProcessing`) y purga automática al bloquear sesión o suspender el equipo (`ClipboardWidget`, prioridad 55).
6. **Indicadores de Privacidad de Micrófono y Cámara (Fase 15):**
   - Vigilancia 100% reactiva mediante `RegNotifyChangeKeyValue` sobre `CapabilityAccessManager\ConsentStore` (cero polling).
   - Indicador persistente ambarino/verde mientras cualquier aplicación utiliza micrófono o cámara, con lista de exclusión configurable (`PrivacyWidget`, prioridad 92 / alerta 94).
7. **Visualizador de Espectro de Audio en Tiempo Real (Fase 16):**
   - Motor **FFT Cooley-Tukey Radix-2** propio en `OpenDynamic.Core` con ventana de Hann, solapamiento del 50% y **cero asignaciones en el heap por cuadro (`0 B/frame`)**.
   - Captura loopback WASAPI (`NAudio.Wasapi`) de 12 bandas (compacto) y 24 bandas (expandido) que se detiene y libera instantáneamente al pausar la música o entrar en pantalla completa.
8. **Reloj Ambiental en Reposo (Fase 19):**
   - Despliegue por sobrevuelo intencional (`ActivationMode.OnHover`, prioridad 5) sobre la franja sensora superior (`120x4 DIP`), alineación exacta al segundo `:00.000` del minuto y sincronización vía `WM_TIMECHANGE`.
9. **Ahorro de Energía Reactivo y Perfil de Recursos Adaptativo (Fase 20):**
   - Suscripción reactiva multicapa a Windows Notification Facility (`WNF_PO_ENERGY_SAVER_OVERRIDE` / `WNF_PO_ENERGY_SAVER_STATE`) y `WM_POWERBROADCAST`.
   - Política pura `ResourceProfilePolicy` que adapta automáticamente las animaciones, conmuta el visualizador de audio a modo simulado y espacia el muestreo de hardware cuando el ahorro de energía está activo (`EnergySaverWidget`, prioridad 88).
10. **Vista Previa Instantánea de Capturas de Pantalla (Fase 21):**
    - Vigilancia reactiva de `FOLDERID_Screenshots` con verificación asíncrona de estabilidad de escritura (`FileStabilityPolicy`) y decodificación de miniaturas en memoria (`DecodePixelWidth = 320`, `Freeze()`) con **cero bloqueo del archivo en disco**.
    - Acciones rápidas: copiar al portapapeles sin auto-disparo, abrir, mostrar en carpeta, arrastrar y soltar seguro (`DragDropEffects.Copy`) y envío a la Papelera (`SHFileOperationW` con `FOF_ALLOWUNDO`) con **doble confirmación**.

---

## 🛡️ Auditoría Integral v2.0.0: Rendimiento, Robustez y Privacidad

- **Cero Timers y 0.00% CPU en Reposo (`AUD-001`, `AUD-005`):** Eliminado por completo el sondeo de cursor en estado `Hidden`. La detección del Reloj Ambiental opera de forma 100% reactiva por eventos de ventana (`WM_NCHITTEST` sobre una franja mínima superior de `120x4 DIP` centrada en el borde superior).
- **Aplicación en Vivo de Ajustes (`AUD-002`):** Todos los interruptores `Enable*Widget` detienen y liberan inmediatamente sus servicios subyacentes y ocultan la actividad en tiempo real al desmarcarse en Ajustes.
- **Muestreo GPU PDH Fuera del Hilo de UI (`AUD-008`):** La enumeración y lectura de contadores `GPU Engine` de Windows se ejecuta de forma asíncrona en segundo plano con protección contra solapamientos y liberación determinista (`Dispose()`), manteniendo el hilo de interfaz a 60–144 FPS constantes.
- **Detección Reactiva de Pantalla Completa por Teclado (`F11`) (`AUD-006`):** `FullscreenWatcher` detecta cambios de geometría de la ventana activa (`EVENT_OBJECT_LOCATIONCHANGE` filtrado y con antirrebote) además de cambios de ventana en primer plano.
- **Privacidad Reforzada en Registros (`AUD-004`):** Nivel de registro en `Release` establecido en `Information`, con redacción completa de rutas locales, SSIDs, títulos multimedia, nombres de dispositivos y etiquetas de temporizadores.
- **Reducción de Huella de Distribución (`AUD-012`, `AUD-014`):** Sustitución del metapaquete `NAudio` por `NAudio.Wasapi`, exclusión de símbolos `.pdb` de los artefactos de lanzamiento y publicación de sumas de verificación `SHA256SUMS.txt`.

---

## 🔄 Compatibilidad y Migración desde v1.0.0

- **Migración Automática Transparente (`SchemaVersion 1 -> 14`):** Al actualizar desde `v1.0.0` o cualquier versión previa, `SettingsService` migra automáticamente `%AppData%\openDynamic\settings.json` al esquema `v14`, preservando todas las preferencias personalizadas del usuario y aplicando saneamiento de rangos (`SanitizeAndClamp`).

---

## 📦 Artefactos de Descarga y Verificación SHA-256

1. **`openDynamic-setup.exe`** — Instalador oficial por usuario (Inno Setup 6, cero UAC, detección automática de .NET 10 Desktop Runtime).
2. **`openDynamic-portable-win-x64.zip`** — Paquete portátil ReadyToRun (`win-x64`) con `README.md`, `LICENSE` y `THIRD-PARTY-NOTICES.md`.
3. **`SHA256SUMS.txt`** — Sumas de verificación criptográficas SHA-256 generadas durante el flujo de lanzamiento.

Para verificar la integridad de tu descarga en PowerShell:

```powershell
Get-FileHash .\openDynamic-setup.exe -Algorithm SHA256
Get-FileHash .\openDynamic-portable-win-x64.zip -Algorithm SHA256
```
