# Windows dependencies and redistribution

Pinned Windows x64 build: .NET SDK 10.0.112; self-contained runtime 10.0.12. The bundled CPU library is unmodified LibreHardwareMonitor 0.9.6; the optional installer is unmodified PawnIO 2.2.0.

| Download | SHA-256 | Build path |
|---|---|---|
| [LibreHardwareMonitor.NET.10.zip](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/releases/download/v0.9.6/LibreHardwareMonitor.NET.10.zip) | `29739c4959b01b348fddad87664066634bcfd4f46e9bf41e4e916c318bcfdb99` | Extract to `upstream/lhm-0.9.6/` at repository root |
| [PawnIO_setup.exe](https://github.com/namazso/PawnIO.Setup/releases/download/2.2.0/PawnIO_setup.exe) | `1f519a22e47187f70a1379a48ca604981c4fcf694f4e65b734aaa74a9fba3032` | `upstream/PawnIO_setup-2.2.0.exe` |
| [LibreHardwareMonitor source](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/archive/refs/tags/v0.9.6.zip) | `28100b87fcd7a9a213c58fabe1174e4f3064975b5ab11799224766993ca1fbfc` | `upstream/LibreHardwareMonitor-0.9.6-source.zip` |
| [PawnIO.Modules source](https://github.com/namazso/PawnIO.Modules/archive/refs/tags/0.1.6.zip) | `665b2f7a028f8a317bc35cd6fbf64986115909239019ff2d30d19260d7dd0982` | `upstream/PawnIO.Modules-0.1.6.zip` |

The prepare-dependencies PowerShell script downloads and verifies these files and corresponding source archives. It does not run an installer, change execution policy, or modify security settings. Build dependencies, executables, archives, logs and local settings are excluded from Git.

Distribution includes the project's MIT license; usage kun's MIT reference notice; .NET and WindowsDesktop MIT licenses and runtime third-party notices; LibreHardwareMonitor MPL-2.0 and third-party notices; and PawnIO.Modules LGPL-2.1 notice. Bundled library source is supplied in sources/LibreHardwareMonitor-0.9.6-source.zip. PawnIO.Modules 0.1.6 source is supplied because the upstream project references those modules. The signed PawnIO installer permits unmodified redistribution; its notice and exact hash are included.

The root THIRD_PARTY_NOTICES.md describes the separate macOS implementation. The Windows-specific redistribution notices are under Windows/third-party-notices and the built ZIP's licenses/ directory.

Official origins: [LibreHardwareMonitor v0.9.6](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/tree/v0.9.6), [PawnIO 2.2.0](https://github.com/namazso/PawnIO.Setup/releases/tag/2.2.0), [PawnIO.Modules 0.1.6](https://github.com/namazso/PawnIO.Modules/tree/0.1.6), [.NET release metadata](https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json).
