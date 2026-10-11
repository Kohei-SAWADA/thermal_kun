# Windows design

The native Windows implementation is isolated under Windows/. The existing Swift/macOS implementation is independent. Windows 0.1.4 removes the memory-pressure label beside MEMORY, its tooltip text and its Settings source row while retaining measured memory usage and its meter. Windows 0.1.3 removed the THERMAL STATUS card and its Settings source row; the freed vertical space expands the temperature history. The internal measurement model retains unsupported thermal-state and memory-pressure fields for diagnostic compatibility. No state is inferred from temperature or memory percentage.

ThermalKun.Core contains finite nullable readings, explicit missing states, CPU/GPU counter arithmetic, a capped 180-point session history, and atomic preferences. ThermalKun.App uses WPF and a WinForms tray. A serial background STA sampler produces immutable snapshots; the dispatcher renders them. Suspend/resume handling discards counter baselines and marks gaps, but physical sleep/resume is not yet verified.

CPU load comes from GetSystemTimes, GPU load from the busiest PDH GPU engine, and physical memory usage from GlobalMemoryStatusEx. CPU temperature comes from a separate elevated CPU-only LibreHardwareMonitor reader through a parent-bound named pipe. Frames carry acquisition times and are rejected when stale. The GUI remains asInvoker. The app does not change fan, clock, thermal, firewall or Windows security settings.

PawnIO installation and CPU-reader elevation require the user's explicit confirmations. The installer is the pinned, signed, unmodified official 2.2.0 distribution. Existing compatible installations are reused. GUI shutdown closes the pipe; only expected Windows pipe-disconnection errors end the reader normally. Other I/O errors remain errors. Secondary launches signal the existing GUI and query executable paths with limited-information access.

Detailed and compact panels retain square resizing and persisted frames. Detailed view contains CPU temperature, CPU/GPU load, memory, history and peak; compact omits GPU, history and peak. Dark/Light/System and Windows animation preferences are supported. Unsupported memory pressure is not displayed.

Preferences and startup logs are local. There is no cloud sync or analytics in the app. Startup is opt-in through the current user's Run entry; disabled Windows startup decisions are preserved. Diagnostics do not change preferences or startup registration. Automated tests use synthetic values only inside test processes; those values never enter the app UI.
