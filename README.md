# EZMonitor

Windows hardware monitor (WPF, .NET 10). Shows temps, CPU, GPU, RAM, VRAM, disk, network, drives and fans as live strip charts.

## Needs
- Windows 10/11 x64. Runs as admin (asks on start).
- **PawnIO driver** for CPU temp/clock/power and board fans: https://pawnio.eu. Without it those show `–`.

## Use
- Graphs keep the last 10 min. Hover a graph for value + time. Click (or Enter) to expand; Esc closes.
- Settings: start with Windows (logon task, opens in tray), close to tray.
- Tray icon shows the hottest of CPU/GPU in °C. Left click opens, right click → Exit.
- Settings file: `%APPDATA%\EZMonitor\settings.json`.

## Build
```
dotnet build
dotnet publish -c Release -r win-x64 --self-contained   # → bin\Release\net10.0-windows\win-x64\publish\EZMonitor.exe
```

## Dev flags
- `--selftest` → exit code 0 = pass.
- `--snap out.png` → renders the window after 12 s, then exits.
- `--tray` → start hidden in tray.

Sensors: [LibreHardwareMonitorLib](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) 0.9.6 (MPL-2.0).
