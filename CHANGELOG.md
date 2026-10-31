# Changelog

## [v1.3.0] : 2026-10-31
- Memory table: each app also shows its % of total RAM.
- Unified Remote phone remote: live RAM + top 10 apps.

## [v1.2.0] : 2026-10-31
- Expanded CPU load / Memory graph: top 10 apps table for the hovered second (24 h, `apps.bin`).
- Zoomed-out hover snaps to the column peak.

## [v1.1.1] : 2026-10-29
- Added CLAUDE.md dev notes.

## [v1.1.0] : 2026-10-29
- New layout: 8 equal tiles on one screen, no scrolling.
- Click a graph to enlarge it: drag to move, wheel to zoom, Now button.
- Keeps 24 h of history on disk (saved every 5 min + on exit).
- Drives and fans in a slim bottom bar.
- Tray shows the app icon; hover for CPU/GPU °C.

## [v1.0.0] : 2026-10-29
- First release: WPF hardware monitor (temps, CPU, GPU, RAM, VRAM, disk, network, drives, fans).
- Live strip charts, 10 min history, hover + expand view.
- Tray icon with hottest CPU/GPU temp; close to tray; start with Windows.
- Dev flags: `--selftest`, `--snap`, `--tray`.
