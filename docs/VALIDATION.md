# Validation record

Validated on 2026-10-09 on Windows 11 x64 (build 26200), .NET SDK 10.0.401.

- Release solution build: zero warnings and zero errors.
- Core/self-test: 34 checks for risk boundaries, registry binary layouts, process identity/lifecycle, real process enumeration, live evidence reading, JSON/CSV, and suspended launch/cancellation.
- Elevated ETW integration: 9 additional checks; real file reads/writes, TCP IPv4 send/receive, UDP send, TCP IPv6 send, registry value paths, child process attribution and reported event loss. Combined run: **43 checks passed**.
- Ordinary UI → elevated collector → pipe → journal: 8 checks passed, including ordinary UI permissions, process descendants, graceful stop and zero reported ETW/queue loss. Test captured 3,177 real events.
- Final self-contained portable distribution: the same 8 collector checks passed, capturing 3,339 real events after the final collector/stop-handling changes.
- WPF preview rendered and visually inspected at 1440 × 960. Preview contains explicitly labeled sample data.

Machine-generated outputs are in `artifacts/integration-results.txt`, `artifacts/collector-results.txt`, `artifacts/packaged-collector-results.txt`, and `artifacts/ui-preview.png`. Ordinary-user collector tests also save real event evidence to the application's normal local session directory.

These probes demonstrate working event capture and data flow on this machine. They are not an exhaustive coverage, stress, malware detection, or operating-system compatibility evaluation.

## 0.1.1: live DataGrid crash fix

The user's two sessions targeting YoudaoDict.exe (PID 12664) ended in an unhandled `InvalidOperationException`. Windows Application log event 1026 identifies `MainWindow.Tick` → `ObservableCollection.OnCollectionChanged` → `ListCollectionView.CurrentPosition` → `VerifyRefreshNotDeferred` as the crash path. The live view incorrectly wrapped source collection additions in `DeferRefresh`.

The new UI regression replays both interrupted journals through the same append path used by Tick, with an actual bound WPF DataGrid and dispatcher. Before the fix it reproduces the identical exception; after removing the deferred-refresh wrapper, all 11 checks pass, including normal idle-window closing:

- Read and replay 45 recoverable events from the user's two journals.
- Append incoming events with an existing selected/current row.
- Append while sorting, filtering by category/risk and searching keywords.
- Append 12,000 additional events while retaining the latest 5,000 and safely removing selected old rows.
- Display full evidence and clear/restart the bound list.

Evidence: `artifacts/ui-replay-before-fix.txt`, `artifacts/ui-replay-after-fix.txt`. The build now runs this UI regression in addition to the core self-test. No target program is relaunched or modified by the replay.

The final portable 0.1.1 was also tested against the still-running PID 12664 with the actual MainWindow timer, elevated collector, category/search filters, sort and selection. It captured and displayed **353 events**, stopped the collector and closed the window with exit code **0**. The original YoudaoDict.exe creation time remained unchanged. Evidence: `artifacts/pid12664-live-ui-results.txt`.

That live test also exposed a separate idle/stopped-window close bug: synchronously completed cleanup could call Close again before the original Closing event returned. The fix allows an idle window to close directly and explicitly yields to the dispatcher before repeating Close after collector cleanup. Both idle closing and stopped-live-monitor closing are now verified.
