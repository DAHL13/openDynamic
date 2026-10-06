# Política de Seguridad y Privacidad (Security Policy)

## Versiones Soportadas

Actualmente se brinda soporte activo de actualizaciones de seguridad para las siguientes versiones de **openDynamic**:

| Versión | Soporte de Seguridad |
|---|:---:|
| `2.0.x` | ✅ Soportada |
| `< 2.0.0` | ❌ No soportada (actualizar a `v2.0.0+`) |

---

## Reporte de Vulnerabilidades

Si descubres una vulnerabilidad de seguridad o una filtración de datos sensibles en **openDynamic**, por favor **no abras un Issue público** de inmediato.

1. Utiliza la función de **[Reportar una vulnerabilidad de forma privada (GitHub Security Advisories)](https://github.com/DAHL13/openDynamic/security/advisories/new)** del repositorio oficial.
2. Incluye los pasos detallados para reproducir el problema, la versión exacta de Windows y de openDynamic, y el impacto potencial.
3. Recibirás acuse de recibo en un plazo máximo de **72 horas** y trabajaremos contigo para validar, corregir y publicar el parche correspondiente antes de su divulgación pública.

---

## Garantías Arquitectónicas de Seguridad y Privacidad

**openDynamic** está diseñado bajo principios de privacidad por diseño (*Privacy by Design*) y mínimo privilegio:

1. **Cero Privilegios de Administrador (`asInvoker`):**
   - La aplicación se ejecuta e instala exclusivamente con privilegios de usuario estándar (`requestedExecutionLevel level="asInvoker"` en `app.manifest` y `PrivilegesRequired=lowest` en Inno Setup). Jamás solicita elevación UAC.
2. **Cero Telemetría y Cero Conexiones de Red Salientes:**
   - openDynamic **no incluye** telemetría, analíticas, rastreadores ni realiza peticiones HTTP/TCP/UDP hacia Internet. La vigilancia del estado de red (`NetworkService`) consulta únicamente el estado local de las interfaces del sistema operativo.
3. **Cero Hooks Globales Invasivos:**
   - Los atajos de teclado se registran exclusivamente mediante la API estándar `RegisterHotKey` / `UnregisterHotKey` de Win32. No se instalan hooks globales de teclado ni ratón (`WH_KEYBOARD_LL`, `WH_MOUSE_LL`).
4. **Aislamiento Estricto en Memoria RAM y Redacción de Logs:**
   - **Portapapeles (`ClipboardService`):** Desactivado por defecto (*opt-in*). Cuando el usuario lo activa, los elementos viven exclusivamente en memoria RAM volátil, excluyen gestores de contraseñas conocidos (`ClipboardExclusionFormat`), se purgan automáticamente al bloquear sesión o suspender el equipo y jamás se escriben en disco.
   - **Espectro de Audio (`AudioSpectrumService`):** Las muestras PCM de loopback WASAPI se procesan en búferes preasignados en RAM para el cálculo FFT y se descartan en cada cuadro sin tocar el disco.
   - **Capturas de Pantalla (`ScreenshotWatcherService`):** Solo opera sobre carpetas autorizadas (`FOLDERID_Screenshots` y carpeta opcional del usuario), validando rutas canónicas y rechazando enlaces simbólicos (*reparse points*) que apunten fuera de las carpetas vigiladas. El envío a la Papelera requiere doble confirmación explícita y utiliza `SHFileOperationW` con `FOF_ALLOWUNDO`.
   - **Registros de Diagnóstico (`Serilog`):** En compilaciones `Release`, el nivel mínimo es `Information` y se redactan sistemáticamente rutas de usuario, nombres de archivos, títulos multimedia, SSIDs de redes Wi-Fi, nombres de periféricos, etiquetas de temporizadores y cualquier contenido del portapapeles.
