# thermal kun for Windows 0.1.4-temperature-preview

Verified: 2026-10-11. [日本語](START-HERE.ja.md)

## Download and run

1. Download the [x64 ZIP](https://github.com/Kohei-SAWADA/thermal_kun/releases/download/windows-v0.1.4-temperature-preview/thermal-kun-windows-0.1.4-temperature-preview-x64.zip) and [SHA-256 file](https://github.com/Kohei-SAWADA/thermal_kun/releases/download/windows-v0.1.4-temperature-preview/thermal-kun-windows-0.1.4-temperature-preview-x64.zip.sha256). Current ZIP: `thermal-kun-windows-0.1.4-temperature-preview-x64.zip` (93,206,008 bytes).
2. Check SHA-256 using `Get-FileHash .\thermal-kun-windows-0.1.4-temperature-preview-x64.zip -Algorithm SHA256`. Expected: `0c7dd14b3a5ea2d24bac6b2e3b57a8ca46d49632e3609d677f268e56c38d07b6`.
3. Extract the entire ZIP and run `ThermalKun/ThermalKun.exe`. The .NET runtime is included. This is an unsigned Windows x64 preview.
4. For CPU temperature, choose the enable/reconnect option in Settings. The ordinary-privilege GUI uses a separate administrator-approved reader with LibreHardwareMonitor 0.9.6 and PawnIO 2.2.0. Initial driver installation requires explicit confirmation; an existing compatible driver is reused. Cancelling leaves CPU/GPU load and memory available. No separate HTTP server is needed.

Update by quitting from Settings or tray Quit, then extracting the full ZIP to the same location. Settings are kept in `%LOCALAPPDATA%\ThermalKun\temperature-settings.json`. Startup registration is off by default. Quit before moving the folder; disable startup before moving and re-enable only if desired afterward. Removing the app folder does not uninstall PawnIO.

## Controls and current behavior

Compact/detail: Ctrl+C. Settings: Ctrl+comma. Hide: Escape. Show: tray Show, tray double-click, or run the same executable again. Quit: Settings or tray Quit. Dark/Light/System and 1/2/5/10-second sampling are supported. The memory GiB value and usage meter remain visible; Windows 0.1.4 omits the Unsupported memory-pressure label and thermal-status section. Missing or stale temperature is Unavailable, rather than an estimated value.

## Matching source and checks

Source: [`thermal-kun-windows-0.1.4-source.zip`](https://github.com/Kohei-SAWADA/thermal_kun/releases/download/windows-v0.1.4-temperature-preview/thermal-kun-windows-0.1.4-source.zip) or [the root Windows tree](../Windows/). This standalone ZIP is byte-identical to `ThermalKun/sources/SOURCE-Windows.zip` inside the current distribution. All 52 build-snapshot files exactly match the original local source and match source commit `5627dccdb48f5e658317ad14c27a12593b4cb81f` after Git line-ending normalization. The 508 distribution manifest entries, DLL/PDB identity and 16 source checksums passed; 194 core, 12 temperature-boundary and live Windows pipe checks were rerun successfully. A rebuild was unnecessary.

See [verification](VERIFICATION.md), [latest metadata](../LATEST_WINDOWS_BUILD.json) and [build instructions](../Windows/README.md). Use the .NET 10 SDK and pinned dependencies documented in the source. Other PCs, ARM64, Windows 10, other DPI settings, multiple monitors, physical sleep/resume, temperature calibration and a login cycle remain unverified. This audit did not relaunch the GUI, install a driver or change user settings. Prior 0.1.4 GUI checks are preserved separately in `Windows/docs/VERIFICATION.md`.

Repository download documentation was updated for GitHub publication; the release source archive remains the original build snapshot and compiled source is unchanged.
