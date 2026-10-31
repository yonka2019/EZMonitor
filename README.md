# EZMonitor

Windows hardware monitor (WPF, .NET 10). Shows temps, CPU, GPU, RAM, VRAM, disk, network, drives and fans as live strip charts.

## Needs
- Windows 10/11 x64. Runs as admin (asks on start).
- **PawnIO driver** for CPU temp/clock/power and board fans: https://pawnio.eu. Without it those show `–`.

## Use
- One screen, no scrolling. Tiles show the last 10 min. Hover a graph for value + time. Click (or Enter) to expand; Esc closes.
- Expanded graph: drag to move back in time, wheel to zoom, End or **Now** jumps back.
- Expanded CPU load / Memory: top 10 apps at the hovered second (else the right edge). Same-name processes summed. Memory = private (like Task Manager), with % of total RAM.
- Keeps 24 h, saved every 5 min and on exit (`%LOCALAPPDATA%\EZMonitor\history.bin` ≈3.5 MB, `apps.bin` ≈10 MB). Closed time shows as a gap.
- Reads everything once per second.
- Settings: start with Windows (logon task, opens in tray), close to tray.
- Tray: app icon; hover shows CPU/GPU °C. Left click opens, right click → Exit.
- Settings file: `%APPDATA%\EZMonitor\settings.json`.

## Phone remote (Unified Remote)
- Shows RAM use + top 10 apps (size · % of RAM), refreshes every 2 s. Needs EZMonitor running.
- Install: copy `UnifiedRemote/EZMonitor RAM` to `C:\ProgramData\Unified Remote\Remotes\Custom\`, then Server Manager → Remotes → Reload.
- Data: EZMonitor rewrites `C:\ProgramData\EZMonitor\live.txt` each second.

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
