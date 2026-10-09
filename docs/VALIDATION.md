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

## 0.1.2: monitored process roster

The UI now lists the root and all tracked descendants in a separate process tab, including processes that have not produced any activity. The collector publishes an initial roster before reporting readiness, updates it when process membership or state changes, and saves a final roster on normal completion. Exited processes remain in the roster; PID plus creation time separates reused identities. Available full paths are shown, and paths that could not be resolved before a short-lived process exited are explicitly labeled.

- Release build: zero warnings and zero errors.
- Core/self-test: **41 checks passed**, including idle descendant seeding, immutable snapshots, root exit with surviving descendants, related/unrelated PID reuse, root-only mode and process roster protocol serialization.
- Bound WPF UI: **17 checks passed**, including the prior 12,000-event regression and new roster checks for idle processes, sorted updates, preserved selection, exit filtering, path search, PID reuse and session reset.
- Real ordinary UI client → elevated collector → journal: **11 checks passed**, capturing **3,144 events** with zero reported ETW/queue loss. The initial snapshot includes the suspended root; the final snapshot retains the exited root, cmd.exe and two conhost.exe descendants. Initial and final snapshots were verified in an exported journal copy.
- The process tab preview was rendered and visually inspected at 1440 × 960, with explicit sample data.

The collector plumbing probe now uses an IPv4-only fixture and verifies the fixture's exit code. This machine currently rejects IPv6 loopback ConnectAsync with SocketException 10013; the original full ETW `--integration` test retains its IPv6 checks and was not rerun successfully for 0.1.2. This validation does not claim renewed IPv6 coverage.

Evidence: `artifacts/process-roster-packaged-core-results.txt`, `artifacts/process-roster-packaged-ui-results.txt`, `artifacts/process-roster-collector-final-results.txt`, and `artifacts/process-roster-preview.png`. The 0.1.2 distribution was published to a separate directory while the previous UI remained open.

## 0.1.3: concurrent monitoring sessions

One WPF window now supports up to four root programs concurrently, each with an independent ordinary UI client, elevated collector, process roster, counters and journal. Switching the selected session restores its retained history while all other clients continue receiving and persisting events. Each session retains its own newest 5,000 UI events. Exited or stopped sessions remain available until explicitly removed from the view; removal preserves their disk journals.

- Release build and self-contained publish: zero warnings and zero errors.
- Core/self-test: **41 checks passed**.
- Bound WPF regression: **21 checks passed**, retaining the prior event/roster tests and adding separate session history, independent retention, switching back and stopped-session removal.
- Real ordinary WPF UI with four concurrent elevated collectors: **27 checks passed** in the final self-contained package. Verified duplicate-root reuse, concurrency limit, selective stop, startup-failure isolation, continued events in another session, separate bound process rosters, separate complete journals, stop-all and unchanged suspended test targets.
- Both executing IPv4 fixtures generated file writes, network bytes, registry value changes and child-process starts in their own journals. Neither journal included the other independent root or its test directory. All four collectors reported zero ETW or queue loss and no receive errors.
- Closing the window with two collectors active while a selective stop was pending completed normally: closing waits for the existing stop/removal operation before disposing clients, preventing concurrent cleanup.
- The two-program process-tab preview was rendered and visually inspected at 1440 × 960. It is explicitly marked as sample data.

Evidence: `artifacts/multiple-core-results.txt`, `artifacts/multiple-packaged-ui-results.txt`, `artifacts/multiple-packaged-final-results.txt`, and `artifacts/multiple-preview.png`. The portable app and ZIP are in `Releases/ProcessSentinel-0.1.3-win-x64/` and `Releases/ProcessSentinel-0.1.3-win-x64.zip`.

The live tests use only temporary IPv4 fixtures created by the diagnostics, including suspended roots for deterministic startup and stop checks. They do not establish new IPv6 coverage, sustained multi-program load capacity, or compatibility beyond this machine. Overlapping descendant scopes can intentionally produce the same event in different journals. No combined all-program event or total-count view is implemented.

## 0.1.4: selecting a member monitors its program tree

Existing PID selection now defaults to walking its current parent chain up to a shared launcher/system-host boundary, then monitoring the resulting root and all descendants. This includes intermediate parents and sibling branches. Existing members of a monitored program reuse that session. Explicit executable launches continue to root their new process tree at the newly created process. Clearing the default option monitors only the selected PID.

The UI and journal retain both the effective root and original selection. Process rosters distinguish the selected process, intermediate ancestors and program root. Parent creation times prevent following reused PIDs; absent/inaccessible parents or cyclic ancestry stop expansion at the last confirmed process. The elevated collector independently revalidates the selected identity and effective root before collecting. Shared launcher names are explicit heuristics, not proof of application ownership or a mechanism for discovering detached service activity.

- Release solution build and self-contained publish: zero warnings and zero errors.
- Core/self-test: **60 checks passed**, including different-path helpers, parents/siblings/descendants, shared launchers, missing/inaccessible/reused parents, cycles, selected PID reuse and old-request compatibility.
- Final self-contained bound WPF UI: **23 checks passed**, including original event/retention tests, expanded scope and role details, and clearing another program's roster when switching sessions.
- Development build, ordinary WPF UI → elevated collector: **16 family checks passed**, capturing **5,088 real events**. A live grandchild selection resolved to its application grandparent, including the intermediate parent and sibling. Real file writes from all four original PIDs, IPv4 traffic, registry changes and later cmd.exe descendants were persisted in one journal, with zero reported ETW/queue loss. Stop left the original family processes running.
- Four concurrent program-tree sessions: **26 checks passed**, including session reuse, limit handling, startup-resolution failure isolation, selective/all stop, separate logs and closing during a pending stop.
- Final process-tab preview visually inspected at 1440 × 960; selected child, parent and main process rows are all visible with clearly labeled sample data.

The self-contained live family recheck was attempted twice, but administrator elevation was canceled before the collector started. The second attempt records Windows error 1223 as `已取消管理员授权。`; only a session header was created, so these attempts do not establish packaged ETW capture. Startup errors now remain visible in the session state rather than being replaced by a generic stopped status. The successful development capture and final packaged UI checks are reported separately.

Evidence: `artifacts/family-core-results.txt`, `artifacts/family-live-results.txt`, `artifacts/family-multiple-results.txt`, `artifacts/family-packaged-ui-results.txt`, `artifacts/family-packaged-results.txt`, `artifacts/family-packaged-final-results.txt`, and `artifacts/family-preview.png`. The 0.1.4 portable directory and ZIP preserve the existing versions and remain excluded from Git. IPv6 and sustained-load limitations from prior validation still apply.

## Development and release build workflow

The root `VERSION` file is the version source for MSBuild and the build script. Normal builds update the fixed `artifacts/dev/ProcessSentinel-win-x64/` directory; only explicit `-Package` builds create a versioned release directory and ZIP. The version remains 0.1.4 for this workflow change.

- `build.ps1 -NoRestore` completed successfully: zero build warnings/errors, **60 core checks** and **23 bound WPF checks** passed, and all three self-contained executables were published to the fixed development directory.
- The development executable reports product version `0.1.4` (with Git metadata), matching the copied `VERSION` file and the evaluated MSBuild `Version` property.
- Compared all existing release files by path, size and modification time before and after the daily build: no changes. The development directory contains no ZIP.
- Default and `-Package -WhatIf` invocations resolve the respective fixed development and versioned release paths without building or generating artifacts. No formal package was created for this change.

Evidence: `artifacts/build-workflow-results.txt` and `artifacts/build-workflow-releases-before.txt`. This validation covers build output and version propagation; no new elevated ETW test was run for the build-script changes.

## Top toolbar, settings and about windows

The top toolbar now contains Settings, About and the existing usage help. Settings stores the default program-tree scope and risk-only filter in the current user's local application data. Applying settings updates the current filter and the scope used by new sessions; existing session requests remain unchanged. Preview and regression modes use default options without writing user preferences.

- Final daily build and self-contained publish: zero warnings and errors; **60 core checks** and **23 bound WPF checks** passed.
- Rendered and visually inspected the 1440 × 960 main process-tab preview and both new dialog previews. The toolbar, full process column headings, all three sample process roles, settings controls and dialog buttons are visible without clipping.
- VERSION remains 0.1.4; output updated only in the fixed development directory. No formal package or new release directory was generated.

Evidence: `artifacts/toolbar-build-results.txt`, `artifacts/toolbar-preview.png`, `artifacts/settings-preview.png` and `artifacts/about-preview.png`. These checks cover the UI build, existing regression behavior and visual layout; elevated capture was not rerun for these UI changes.

## Menu bar

The toolbar buttons were replaced with a native WPF menu above the application banner: Tools → Settings, and Help → Usage / About. Existing dialog handlers and the settings availability guard remain connected. The menu provides Alt+T and Alt+H access keys.

- Daily build and publish: zero warnings/errors, **60 core checks** and **23 bound WPF checks** passed.
- The 1440 × 960 process-tab preview was rendered and visually inspected; the menu and all three sample process roles are visible without clipping.
- VERSION remains 0.1.4; the fixed development output was updated without formal release packaging.

Evidence: `artifacts/menu-build-results.txt` and `artifacts/menu-preview.png`.

## Process-list context menu

Right-clicking a process row selects it and opens an Add Monitor action. The action resolves the target from the context menu's owning row and uses the existing session startup flow. Its availability follows the same identity, busy-state and concurrency rules as the main Add Monitor button. Empty list space has no process menu.

- Final daily build: zero warnings/errors, **60 core checks** and **23 bound WPF checks** passed.
- An isolated WPF probe passed **4 context-menu checks**: a routed right click selects the clicked row; the menu opens with its action enabled; clicking uses the owning row even if selection changes and reuses that row's existing session; self-monitoring is disabled. The probe used sample sessions and did not start a collector or request elevation.
- The program running from the default development directory was preserved. Updated self-contained executables were published to the fixed alternate development directory `artifacts/dev-alternate/ProcessSentinel-win-x64/`.
- VERSION remains 0.1.4, and no formal release package was generated.

Evidence: `artifacts/context-menu-build-results.txt` and `artifacts/context-menu-probe-results.txt`. No new ETW capture validation was performed for this UI entry point.

## Configurable session log directory

Settings now provides a log directory field, folder picker and reset-to-default action. Saving normalizes an absolute path and verifies ordinary-user write access using a temporary file that deletes on close. New sessions capture the configured directory in their client; existing sessions keep their original journal path. Open Logs follows the selected session's actual journal directory, falling back to the configured directory when there is no session journal. Journal creation errors occur before collector elevation and do not silently switch directories.

- Final daily build and fixed development publish: zero warnings/errors, **60 core checks** and **23 bound WPF checks** passed.
- **8 isolated log-directory checks passed**: old settings retain the original default; a custom directory survives settings JSON reload; changed settings affect new clients without changing existing clients; Unicode/space paths are writable without leftover probe files; actual journal initialization creates the session header in the configured directory; live journals remain readable/exportable; a file used as a directory rejects initialization without a false journal path or fallback; relative directories are rejected.
- The updated settings window was rendered and visually inspected; directory field, browse/reset actions, explanatory text and save/cancel buttons are visible.
- VERSION remains 0.1.4. Updated executables are in `artifacts/dev/ProcessSentinel-win-x64/`; the running alternate development app was preserved. No formal release package was created.

Evidence: `artifacts/log-directory-build-results.txt`, `artifacts/log-directory-probe-results.txt` and `artifacts/log-directory-settings-preview.png`. The isolated probe writes only its own diagnostic files, does not change user settings and does not start an elevated collector. This validates journal storage and UI layout without renewed ETW capture coverage.
