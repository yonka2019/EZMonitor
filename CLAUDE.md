# EZMonitor — notes for Claude

Windows hardware monitor. WPF, .NET 10 (`net10.0-windows`), C#, single project, no tests project. Desktop app → no `app.url`, no web server.

## Build / run / check
- `dotnet build` → `bin\Debug\net10.0-windows\EZMonitor.exe`
- `dotnet publish -c Release -r win-x64 --self-contained` → `bin\Release\net10.0-windows\win-x64\publish\EZMonitor.exe` (single file, compressed)
- Manifest = `requireAdministrator` → launching triggers UAC; run elevated.
- SDK may be per-user only: `%LOCALAPPDATA%\Microsoft\dotnet\dotnet.exe` (not on PATH).
- Unelevated run (selftest without UAC): `$env:__COMPAT_LAYER="RunAsInvoker"`.
- `EZMonitor.exe --selftest` → exit 0 = pass; on fail writes `selftest.log` next to exe. Add a `Check(...)` in `SelfTest.cs` for new logic.
- `EZMonitor.exe --snap out.png` → renders window after 12 s, exits. Use to verify layout visually.
- `--tray` → start hidden (used by autostart task).
- Single instance: named event `EZMonitor.Show`; 2nd launch signals 1st to `Open()` and quits. Kill running instance before rebuilding (exe locked).

## Files
| File | Role |
|---|---|
| `App.xaml(.cs)` | Global brushes/styles (Paper/Strip/Ink/InkSoft, Bahnschrift, Chip, Icon). Startup args, single-instance, `--snap`. `ShutdownMode=OnExplicitShutdown`. |
| `MainWindow.xaml(.cs)` | 8 tiles (2×4 `UniformGrid`) + bottom drives/fans bar + Settings popup + notice banner + full-window `Overlay` with expanded graph. Poll loop, history save, formatters (`Rate`, `TempText`, `Gb`, `Percent`, `Share`, `Bytes`); `ShowApps` table. |
| `Sensors.cs` | `Sensors.Read()` → `Snapshot` record (NaN = unavailable). LibreHardwareMonitorLib + `NetworkInterface` + `DriveInfo`. Not thread-safe; used only on poll thread. |
| `Trace.cs` | `Series` ring buffer (float, NaN = gap, `AddGap`) + `Thermal` colour ramp (40° blue → 65° ochre → 85° red). |
| `TraceView.cs` | Custom `FrameworkElement` strip chart, drawn in `OnRender`. Hover readout; tile = click/Enter → `Expand`; `Interactive` = drag pan, wheel zoom, arrows/+/-/End. One point per pixel column (column peak); hover snaps to that peak. `FocusAge` (hover or right edge) + `Changed` drive the apps table. |
| `Apps.cs` | `AppSampler` (one `NtQuerySystemInformation` call/s, x64 struct offsets, CPU % from time delta keyed (pid, create time), private working set, summed by name, `.exe` stripped) + `AppHistory` (ring of top-10 CPU + top-10 RAM per second, `ushort` name ids; `apps.bin`, compacts names on save). |
| `UnifiedRemote/EZMonitor RAM/` | Unified Remote custom remote (`meta.prop`, `layout.xml`, `remote.lua`). Reads `live.txt` every 2 s while focused (`libs.timer`, `libs.fs`); stale > 5 s → "EZMonitor not running". Installed copy: `C:\ProgramData\Unified Remote\Remotes\Custom\EZMonitor RAM` (re-copy after edits, Reload remotes). |
| `History.cs` | Binary save/load of all series to `%LOCALAPPDATA%\EZMonitor\history.bin` (temp file + move). Pads closed time with NaN. |
| `Settings.cs` | `Settings` (JSON at `%APPDATA%\EZMonitor\settings.json`, only `CloseToTray`) + `Autostart` (schtasks logon task `EZMonitor`, XML, `HighestAvailable`, `--tray`). |
| `Tray.cs` | WinForms `NotifyIcon`; tooltip CPU/GPU °C (≤127 chars); left click open, menu Open/Exit. |
| `SelfTest.cs` | `--selftest` assertions. |
| `Assets/ezmonitor.ico` | App + window + tray icon. |

## Key invariants
- 1 sample/s; every series holds `24*3600` samples. **All series same length** → index i = same second in all. Any skipped time (sleep, stall, app closed) must be padded with `AddGap` on *every* series.
- `Render()` computes missed seconds from `_lastSample` and pads; `Expanded.Follow(n)` keeps a panned view on the same moment.
- History saved every 300 samples in background (`_saving` task, snapshot copied on UI thread) and synchronously on real exit. ≈3.5 MB. Format `Version = 1` — bump if layout changes (old file then ignored).
- History dictionary keys (`cpuTemp`, `gpuTemp`, `cpuLoad`, `gpuLoad`, `ram`, `vram`, `diskRead`, `diskWrite`, `netDown`, `netUp`) are the on-disk names — renaming loses that series' history.
- Close button: hides to tray if `CloseToTray`; real exit only via tray Exit (`_exiting`).
- Poll errors skip one sample, never crash.
- Expanded overlay copies the source tile's settings every tick (`CopyFrom`) since RAM/VRAM `Max` change.
- `WriteLive` (poll thread, each second): `%ProgramData%\EZMonitor\live.txt`, tab lines `time`/`ram`/`app`, pre-formatted text (remote shows as-is). Temp + move; IO errors skipped. Format change → update `remote.lua` too.
- Top apps table: only for `CpuLoadTrace`/`RamTrace` expanded. `AppHistory` is aligned by **age from newest** (not index) — gets the same `AddGap`/`Add` each tick; `Load` pads its own closed time. `ShowApps` skips redraw when `(ram, Added - age)` unchanged.

## Sensors notes
- Needs admin + **PawnIO driver** (https://pawnio.eu) for CPU temp/clock/power and board fans; missing → `–` and a notice banner (`ShowNotice`). Detection: `LibreHardwareMonitor.PawnIo.PawnIo.IsInstalled`.
- GPU pick: discrete (NVIDIA/AMD) before Intel.
- Sensor names tried in preference order via `Get(hw, type, names...)`. Power `0` → NaN (`Positive`).
- Network = sum over Up interfaces with a gateway (skips loopback/tunnel).
- `LibreHardwareMonitorLib` 0.9.6 (MPL-2.0) — only NuGet dependency.

## Conventions
- WinForms only for tray: `System.Windows.Forms`/`System.Drawing` removed from implicit usings; alias `Forms = System.Windows.Forms`.
- Style: terse C#, file-scoped namespace, records, tuple assignments, short `///` summaries, few comments. Match it.
- Visual style: "recorder paper" — pale grey ground, dark ink, heat is the only colour. Font Bahnschrift; icons Segoe Fluent Icons → Segoe MDL2 fallback. No animations.
- UI text short. Missing value shows `–`.
- Windows 10/11 x64, PerMonitorV2 DPI.

## Release flow
- Bump version in **both** `EZMonitor.csproj` `<Version>` and `app.manifest` `assemblyIdentity version` (x.y.z.0).
- Add entry to `CHANGELOG.md` (`## [vX.Y.Z] : YYYY-MM-DD`, short bullets). Update `README.md`.
- Commit `Release vX.Y.Z: ...`, tag `vX.Y.Z`. Remote: `github.com/yonka2019/EZMonitor` (branch `main`). No GitHub Releases created so far.
- Current: v1.3.0 (2026-10-31).
