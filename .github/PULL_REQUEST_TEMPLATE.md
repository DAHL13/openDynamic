## Resumen de Cambios

<!-- Describe brevemente qué problema resuelve o qué funcionalidad introduce este Pull Request -->

- **Issue relacionado:** Closes #

---

## Lista de Verificación Técnica (Checklist)

Por favor confirma que tu Pull Request cumple con los siguientes requisitos antes de solicitar revisión:

- [ ] **Separación de Capas:** `OpenDynamic.Core` se mantiene 100% puro y libre de referencias a WPF, WinForms, WinRT o P/Invoke.
- [ ] **0.0% CPU en Reposo:** No se introdujeron bucles de sondeo (*polling*) en estado `Hidden` ni suscripciones permanentes a `CompositionTarget.Rendering`.
- [ ] **Hilo de UI Despejado:** No se realizan operaciones de E/S de disco, inicialización de contadores PDH ni bloqueos síncronos en el hilo de despacho de WPF.
- [ ] **Privacidad y Seguridad:** No se registran rutas de usuario, nombres de archivos, títulos multimedia, SSIDs ni contenido del portapapeles en `Serilog`.
- [ ] **Compilación Limpia:** `dotnet build openDynamic.sln -c Release` finaliza con **0 advertencias** y **0 errores** (`TreatWarningsAsErrors=true`).
- [ ] **Formato:** `dotnet format openDynamic.sln --verify-no-changes` finaliza con código `0`.
- [ ] **Pruebas Automatizadas:** `dotnet test openDynamic.sln -c Release` pasa al **100%** (se añadieron pruebas unitarias con `FakeTimeProvider` para la lógica nueva).
- [ ] **Documentación:** Se actualizaron `CHANGELOG.md`, `README.md` y `DECISIONS.md` (si aplica).
