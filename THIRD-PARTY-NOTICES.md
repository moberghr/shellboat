# Third-party notices

Shellboat is MIT licensed (see `LICENSE`). It includes or links the following third-party code; their
licenses apply to those parts.

| Component | Where | License |
|---|---|---|
| [XtermSharp](https://github.com/migueldeicaza/XtermSharp) (terminal engine, derived from xterm.js) | `vendor/XtermSharp` | MIT — `vendor/XtermSharp/LICENSE` |
| [RDPilot](https://github.com/ErnieBernie10/RDPilot) (FreeRDP wrapper, RDP viewport logic) | `native/freerdp-wrapper`, ported into `src/Shellboat.Rdp` and `src/Shellboat.App/Rdp` | MIT — `native/freerdp-wrapper/LICENSE.RDPilot` |
| [FreeRDP](https://www.freerdp.com/) | linked at runtime (not vendored) | Apache-2.0 |
| [SSH.NET](https://github.com/sshnet/SSH.NET) | NuGet | MIT |
| [NStack.Core](https://github.com/gui-cs/NStack) | NuGet | MIT |
| [YamlDotNet](https://github.com/aaubry/YamlDotNet) (Ásbrú import) | NuGet | MIT |
| [Avalonia](https://avaloniaui.net/) | NuGet | MIT |
| [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) | NuGet | MIT |

Local changes to vendored code are listed in each folder's `NOTICE.md`.
