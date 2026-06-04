# Sprinkler Controller Rewrite — C# / .NET 10 / Blazor Server PWA (Raspberry Pi)

## Context

The current OpenSprinkler firmware is embedded multi-platform C++ centered on
microcontrollers (ESP8266/AVR), with the Pi as one of several targets. Its UI is
minified HTML compiled into a C header (`htmls.h`) and its HTTP API uses cryptic
2-char command codes. Every feature wanted — drag-and-drop run order, a modern
responsive UI, bulk run-time editing, a property-map view, and AI/natural-language
control — lives in the application/UI layer, which is exactly where that architecture
fights you hardest.

**Decision:** full standalone rewrite, Pi-only, in **.NET 10 / Blazor Server**,
delivered as an **installable PWA**, with an **MCP server** for the AI control. Hardware
is an **OSPi-clone, 16 zones** driven by two daisy-chained 74HC595 shift registers
(latch/clock/data/output-enable GPIO pins).

**Why a rewrite, not a fork:** the existing firmware's hardware layer is tiny
(~200 lines) and its HTTP API/UI are tightly coupled to embedded constraints. The
*only* genuinely valuable, hard-to-reproduce asset is the **scheduling logic**
(`check_match` + `schedule_all_stations`), with its edge cases (overnight runs,
sunrise/sunset offsets, odd/even days, leap-year/last-day, master sequencing). We
keep the old code as a **behavioral reference and test oracle**, port the scheduler
carefully, and build everything else fresh.

**Intended outcome:** a maintainable single-language app on the Pi that delivers all
five requested features, with a real-time UI and an AI-controllable MCP surface, while
matching the old firmware's scheduling behavior provably.

## Progress

- **Phase 0 — Walking skeleton: ✅ complete.** 5-project clean-architecture solution;
  `IZoneDriver` with `Sim` + `ShiftRegister` drivers; `SprinklerEngine : BackgroundService`
  driven by a `Channel<EngineCommand>`; `IStateHub`/`StatusSnapshot`; Blazor page with 16
  working zone toggles; engine unit tests green.
- **Phase 1 — Persistence + CRUD: ✅ complete.**
  Data layer (landed 2026-06-04): Domain entities + enums (`Zone`, `Program`, owned
  `ProgramStartTime`, `ProgramZoneDuration` with first-class `RunOrder`, `MasterStation`,
  `ControllerSettings`, `RunLogEntry`); EF Core 10 / SQLite `OSPiDbContext` with Fluent configs,
  enum→int conversions, owned start-times, unique indexes, and cascade/set-null FK behavior;
  `InitialCreate` migration seeding 16 zones + 2 masters + settings; repository interfaces
  (Application) over `IDbContextFactory` (Infrastructure), incl. a one-call `SchedulingData`
  read for Phase 2; startup `MigrateAsync`; 9 SQLite in-memory persistence tests.
  CRUD UI (landed 2026-06-04): config screens under a `/config/*` prefix (dashboard stays at
  `/zones`) — settings, zones (edit-only, 16 seeded), master stations (2 seeded), programs list
  + full Program editor covering all four schedule types, fixed/repeating start times,
  sunrise/sunset offsets, the date-range gate, and per-zone durations + `RunOrder`. Validation
  via `EditForm` + `DataAnnotationsValidator` on Web-layer view models (Domain POCOs stay
  annotation-free); shared components `EnumSelect`/`DurationInput`/`TimeOfDayInput`/
  `WeekdayPicker`/`SaveCancelToolbar`/`ConfirmDeleteModal`. The Program editor offers only
  not-yet-added zones and leaves duration `Id = 0` so `ProgramRepository.UpdateAsync`'s
  merge-by-`ZoneId` does not duplicate or drop rows. Verified: `dotnet build`/`dotnet test`
  green (13 tests); every screen exercised in the browser with the Sim driver — create/edit/
  round-trip of a program across schedule types, zone/master/settings edits all persist; no
  console errors. **Deferred to later phases as planned:** drag-and-drop run-order reordering
  and multi-select bulk duration edit (Phase 4).
- **Phases 2–6:** not started. Next: **Phase 2 — Scheduler core** (highest risk).

## Reference files to port (from the OpenSprinkler-Firmware C++ repo; read, do not modify)

- `program.cpp` — `check_match`, `starttime_decode`, `gen_station_runorder`
- `main.cpp` — `do_loop` tick cadence, `schedule_all_stations`, master adjustments, turn on/off, dynamic events
- `OpenSprinkler.cpp` — `apply_all_station_bits` (~line 1404) is the exact spec for the shift-register driver
- `program.h` / `defines.h` — `ProgramStruct`, `RuntimeQueueStruct`, `LogStruct`, group/master/option constants

## Target architecture

Five projects, one solution, dependencies pointing inward only:

```
OSPi.Domain         entities + PURE scheduling functions (no I/O, EF, or GPIO)
OSPi.Application    services, IZoneDriver, repo + IStateHub interfaces, engine logic
OSPi.Infrastructure EF Core SQLite, ShiftRegister + Sim drivers, weather client, image store
OSPi.Web            Blazor Server PWA: UI, SignalR hub impl, hosts the SprinklerEngine
OSPi.Mcp / MCP host MCP server tools over the same application services
```

**Hardware abstraction.** One interface — the engine computes the full desired state
of all 16 zones each tick and hands it down:

```csharp
public interface IZoneDriver { int ZoneCount { get; } void Apply(ReadOnlySpan<bool> zoneStates); }
```

- `ShiftRegisterZoneDriver` — `System.Device.Gpio` (`GpioController`), latch-low →
  MSB-first 8-bit loop → latch-high, two bytes high-board-first; OE driven low at startup.
- `SimZoneDriver` — in-memory `bool[16]`, logs transitions, drives a dev "virtual board"
  panel. Selected by config (`Hardware:Driver=Sim`) so the whole app runs on a Mac/PC.

**Scheduling engine.** `SprinklerEngine : BackgroundService` hosted only in `OSPi.Web`
is the single owner of mutable runtime state and the only writer to hardware. Loop:
once-per-minute program matching/enqueue (`CheckMatch`) + once-per-second timekeeping
(apply queue → `bool[16]`, apply master adjustments, `driver.Apply`, publish snapshot).
All external mutations (manual run, pause, edits affecting the live queue) post commands
into a `System.Threading.Channels.Channel<EngineCommand>` the loop drains each tick —
this removes the global-state locking issues of the C++ design and keeps the engine
deterministic/testable.

**Persistence — SQLite via EF Core** (chosen over flat JSON): the data is relational
(Program → ordered ProgramZoneDuration → Zone; Master → bound zones; Map → markers),
run-history/bulk-edit want queryable storage, migrations give clean schema evolution,
and SQLite is zero-config and first-class on ARM. Domain entities stay
persistence-ignorant POCOs (Fluent API mapping in Infrastructure); the runtime queue
stays in memory (ephemeral, like the C++ `queue[]`); only config + run log are persisted.

**Real-time to UI.** Engine builds an immutable `StatusSnapshot` each second →
`IStateHub` (singleton, lock-free latest-snapshot) → broadcaster pushes via
`IHubContext<SprinklerHub>` (SignalR) → Blazor zone cards + map highlights are pure
projections. `IStateHub` is an Application interface so the engine doesn't depend on
SignalR. Manual commands trigger an immediate out-of-band publish for snappy feedback.

**PWA / installable.** The Web app ships a `manifest.webmanifest` (name, icons, theme
color, `display: standalone`) and a service worker that caches the static app shell
(HTML/CSS/JS/icons), making it installable to phones/tablets/desktop with an app icon and
full-screen chrome. Because Blazor Server depends on its live SignalR circuit, the app is
**installable but not offline-functional** — acceptable here, since controlling hardware
requires a connection to the Pi on the LAN anyway. The service worker caches only the shell
(not the interactive runtime); the manifest + responsive layout deliver the native-app feel.
If true offline UI is wanted later, .NET 10 Blazor render modes (WASM/Auto) are the upgrade path.

**MCP.** Host the MCP endpoint **in the Web process** (HTTP/SSE transport via the
official `ModelContextProtocol` SDK) so there is exactly one engine/driver/DbContext;
tools call `IManualRunService`/`IStatusService` directly. (A separate MCP process talking
to a localhost API is a later option if MCP must run while the UI host is down.) Tools are
stateless wrappers: `list_zones`, `get_status`, `run_zone`, `stop_zone`, `stop_all`,
`run_program`, `set_rain_delay`, `list_programs`.

## Feature modeling decisions

- **Custom run order** — replace the old name-annotation hack with first-class data on
  the `ProgramZoneDuration` join entity: `(ProgramId, ZoneId, DurationSeconds, RunOrder)`.
  Drag-and-drop reorders and persists `RunOrder`; the engine enqueues sorted by `RunOrder`.
  Run order governs only enqueue order *within* a sequential group, orthogonal to grouping.
  Optionally keep an `AlternateReverse` flag to preserve the old "flip order each run"
  behavior (low-stakes; can defer).
- **Bulk edit** — pure UI/data op: set the same `DurationSeconds` across selected
  `ProgramZoneDuration` rows. No engine involvement.
- **Property map** — `PropertyMap(ImagePath, w, h, hash)` + `Marker(MapId, ZoneId, X, Y …)`
  with **normalized 0..1 coordinates** (survive responsive resizing). Image stored as a
  **file** (path+hash in DB), re-encoded/bounded on upload with **SixLabors.ImageSharp**
  (managed, ARM-safe). Live highlight is a projection of the same `StatusSnapshot`;
  click-to-run calls the same `IManualRunService.RunZone`.

## Phased build order

- **Phase 0 — Walking skeleton (low risk).** 5-project solution; `IZoneDriver` + Sim +
  ShiftRegister impls; minimal engine applying a manual `bool[16]`; Blazor page with 16
  toggles → service → channel → engine → SimZoneDriver. Goal: flip a zone from the browser
  on a laptop. Separately bench-test `ShiftRegisterZoneDriver` on the Pi (LED/multimeter).
- **Phase 1 — Persistence + CRUD.** EF Core SQLite, DbContext, migrations, repositories;
  CRUD UI for zones (name, board/bit, group, master bindings) and programs. No execution yet.
- **Phase 2 — Scheduler core (HIGHEST RISK).** Port `CheckMatch` and `Scheduler.Plan` as
  pure functions in Domain; wire per-minute match + per-second timekeeping; water-level +
  weather scaling, station delay, sequential/parallel groups, master adjustments, overnight
  runs, pause/resume. Build the differential test harness (below) **in this phase as its exit gate.**
- **Phase 3 — Real-time UI + PWA shell.** SignalR hub, `IStateHub`, broadcaster, live
  dashboard with countdowns; manual run/stop/run-program/rain-delay; run-log view. Add the
  web manifest, app icons, and service-worker app shell so the app is installable; verify
  install + standalone launch on a phone and desktop.
- **Phase 4 — Custom run order + bulk edit.** `RunOrder`, drag-and-drop reorder, multi-select
  bulk duration edit; engine enqueues by `RunOrder`.
- **Phase 5 — Property map.** Image upload + ImageSharp re-encode + file store; marker
  placement editor; live-highlight projection; click-to-run.
- **Phase 6 — MCP server.** Stateless tools over the application services (in-process host);
  validate natural-language status + run flows end to end.

**Highest-risk phase is Phase 2** — it concentrates all the easy-to-get-wrong behavior, and
a bug either waters at the wrong time or leaves a valve on (water bill / flooding). Mitigate
by keeping the scheduler as pure functions gated behind the test harness before it drives hardware.

## Key packages

`System.Device.Gpio` (v4.x) · `Microsoft.EntityFrameworkCore.Sqlite` + `.Design` ·
`ModelContextProtocol` (official MCP C# SDK) + `Microsoft.Extensions.Hosting` ·
`SixLabors.ImageSharp` · Blazor Server + built-in SignalR (.NET 10 web SDK) · web manifest +
service worker for PWA install (no extra package needed) · `System.Threading.Channels` ·
`NodaTime` (recommended for DST-correct time + sunrise/sunset math) ·
a Blazor SortableJS wrapper for drag-and-drop · `xUnit` + `FluentAssertions` for tests.

## Verification

- **Off-device dev:** run the entire app on Mac/PC with `Hardware:Driver=Sim`; inject an
  `ISystemClock` (`FakeClock`) so a 24-hour schedule fast-forwards in milliseconds.
- **Scheduler correctness (the critical gate):** compile the old firmware in **DEMO mode**
  (`#if defined(DEMO)`, runs without hardware) and use it as an oracle. Extract golden
  vectors (per-minute `check_match` results + `to_delete`; `schedule_all_stations` start/dur/
  dequeue + `last_seq_stop_times`) to JSON; assert the C# pure functions match. Cover the
  known traps: weekly weekday boundaries, monthly last-day in leap/non-leap, interval
  boundaries, single-run auto-delete, fixed vs repeating starts, the overnight `t-86400`
  branch, sunrise/sunset offset clamping, odd/even 31st/Feb-29 skips, year-wrapping date
  ranges, 4 sequential + parallel groups with station delay, master on/off adjustment,
  and the <20%/<10s scaling skip.
- **Full-day integration sim:** with `FakeClock` + `SimZoneDriver`, replay a known program
  set for a simulated day and diff the on/off transition log against the DEMO firmware.
- **Hardware bring-up:** bench-verify `ShiftRegisterZoneDriver` on the Pi (each of 16 zones
  toggles correctly, latch atomicity, OE behavior) before connecting solenoids.

## Open items to decide later (non-blocking)

1. MCP in-process (recommended) vs. separate process — only matters at Phase 6.
2. Keep `AlternateReverse` ("flip order each run") or drop it now that order is explicit.
3. Installable PWA on Blazor Server is not offline-capable; revisit Blazor WASM/Auto render
   modes only if true offline UI becomes a requirement.
