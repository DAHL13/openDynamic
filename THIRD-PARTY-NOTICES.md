# Avisos de Software de Terceros (Third-Party Notices)

**openDynamic** incorpora o referencia componentes de software de código abierto de terceros. Agradecemos a los autores y mantenedores de estos proyectos su trabajo y contribución al ecosistema .NET.

---

## Dependencias de Producción (`OpenDynamic.App` y `OpenDynamic.Core`)

| Paquete NuGet | Versión | Licencia | Autor / Titular | Repositorio Oficial |
|---|:---:|:---:|---|---|
| **CommunityToolkit.Mvvm** | `8.4.2` | MIT | .NET Foundation and Contributors | [https://github.com/CommunityToolkit/dotnet](https://github.com/CommunityToolkit/dotnet) |
| **H.NotifyIcon.Wpf** | `2.4.1` | MIT | HavenDV | [https://github.com/HavenDV/H.NotifyIcon](https://github.com/HavenDV/H.NotifyIcon) |
| **H.NotifyIcon** *(transitiva)* | `2.4.1` | MIT | HavenDV | [https://github.com/HavenDV/H.NotifyIcon](https://github.com/HavenDV/H.NotifyIcon) |
| **H.GeneratedIcons.System.Drawing** *(transitiva)* | `2.4.1` | MIT | HavenDV | [https://github.com/HavenDV/H.NotifyIcon](https://github.com/HavenDV/H.NotifyIcon) |
| **Microsoft.Extensions.DependencyInjection** | `10.0.12` | MIT | Microsoft Corporation | [https://github.com/dotnet/runtime](https://github.com/dotnet/runtime) |
| **Microsoft.Extensions.DependencyInjection.Abstractions** *(transitiva)* | `10.0.12` | MIT | Microsoft Corporation | [https://github.com/dotnet/runtime](https://github.com/dotnet/runtime) |
| **NAudio.Wasapi** | `2.3.0` | MIT | Mark Heath & Contributors | [https://github.com/naudio/NAudio](https://github.com/naudio/NAudio) |
| **NAudio.Core** *(transitiva)* | `2.3.0` | MIT | Mark Heath & Contributors | [https://github.com/naudio/NAudio](https://github.com/naudio/NAudio) |
| **Serilog** | `4.4.0` | Apache-2.0 | Serilog Contributors | [https://github.com/serilog/serilog](https://github.com/serilog/serilog) |
| **Serilog.Sinks.File** | `7.0.0` | Apache-2.0 | Serilog Contributors | [https://github.com/serilog/serilog-sinks-file](https://github.com/serilog/serilog-sinks-file) |

---

## Dependencias de Pruebas (`OpenDynamic.Tests` — No distribuidas en el binario final)

| Paquete NuGet | Versión | Licencia | Autor / Titular | Repositorio Oficial |
|---|:---:|:---:|---|---|
| **xunit** | `2.9.2` | Apache-2.0 | .NET Foundation and Contributors | [https://github.com/xunit/xunit](https://github.com/xunit/xunit) |
| **xunit.runner.visualstudio** | `2.8.2` | Apache-2.0 | .NET Foundation and Contributors | [https://github.com/xunit/visualstudio.xunit](https://github.com/xunit/visualstudio.xunit) |
| **Microsoft.NET.Test.Sdk** | `17.12.0` | MIT | Microsoft Corporation | [https://github.com/microsoft/vstest](https://github.com/microsoft/vstest) |
| **coverlet.collector** | `6.0.2` | MIT | Tonerdo & Contributors | [https://github.com/coverlet-coverage/coverlet](https://github.com/coverlet-coverage/coverlet) |

---

## Textos de Licencias

### The MIT License (MIT)

Applies to: `CommunityToolkit.Mvvm`, `H.NotifyIcon.Wpf`, `Microsoft.Extensions.DependencyInjection`, `NAudio.Wasapi`, `NAudio.Core`.

```text
Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

---

### Apache License, Version 2.0

Applies to: `Serilog`, `Serilog.Sinks.File`, `xunit`.

```text
Licensed under the Apache License, Version 2.0 (the "License");
you may not use this file except in compliance with the License.
You may obtain a copy of the License at

    http://www.apache.org/licenses/LICENSE-2.0

Unless required by applicable law or agreed to in writing, software
distributed under the License is distributed on an "AS IS" BASIS,
WITHOUT WARRANTIES OR CONDITIONS OF NATIONS OR ANY KIND, either express or implied.
See the License for the specific language governing permissions and
limitations under the License.
```
