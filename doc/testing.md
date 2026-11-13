# Testing

Three test tiers, from in-process contracts to full browser-to-plugin end-to-end:

| Tier | Location | What runs |
|---|---|---|
| Integration | `tests/StreamDeckMonSwitchMKI.Tests` | real plugin **process** vs fake Stream Deck WebSocket |
| Component | `frontend/sdpi-components` | PI component over the real wire protocol (jsdom) |
| **E2E** | `tests/e2e` | real **Chromium** → real PI HTML → fake Stream Deck relay → real plugin process |

Run everything with:

```powershell
.\run-tests.ps1            # or: -Suite backend | -Suite frontend | -Suite e2e
```

## Playwright e2e suite — `tests/e2e` (the real end-to-end)

Playwright drives the **unmodified Property Inspector HTML in a real Chromium** against the **real plugin executable**, with a fake Stream Deck relay in between:

```
Chromium (PluginActionPI.html + sdpi-components.js)
    │  WebSocket (Stream Deck SDK protocol)
    ▼
FakeStreamDeck relay (tests/e2e/lib/fake-streamdeck.ts)
    │  WebSocket
    ▼
StreamDeckMonitorSwitch.exe  (real process, real monitor enumeration)
```

Nothing in the backend is mocked or linked into the test process — the plugin is a black-box child process. The relay behaves like the Stream Deck application: registrations, `getGlobalSettings`, PI↔plugin routing (`sendToPlugin` / `sendToPropertyInspector`), settings persistence with `didReceiveSettings` fan-out, and injected action lifecycle events (`willAppear`).

Run via:

```powershell
cd tests\e2e
npm install
npx playwright install chromium
npx playwright test
```

| Test | Scenario |
|---|---|
| plugin renders and pushes a key icon on willAppear | real plugin renders the SVG icon and pushes a base64 `setImage` |
| PI renders the monitors detected by the real plugin | browser shows one checkbox per physically detected monitor |
| selecting a monitor persists settings through the plugin | real click → PI `setSettings` → relay → plugin `didReceiveSettings` |
| PI pre-checks a monitor supplied via initial settings | initial `actionInfo.payload.settings` renders checked in the browser |
| selecting a monitor renders VCP properties, selecting a property renders VCP values | real click cascade: monitor → `getProperties` reply renders properties; property → `getValues` reply renders values |
| PI with saved settings renders properties and values on load without interaction | pre-configured action PI: plugin answers initial `getProperties`/`getValues` without user interaction |

Implementation notes:

- `lib/pi-server.ts` serves the real PI page and injects the `connectElgatoStreamDeckSocket(...)` bootstrap call the Stream Deck app normally performs.
- `lib/plugin-process.ts` launches the plugin with a staged working directory (`img/`) and the standard `-port/-pluginUUID/-registerEvent/-info` arguments.
- Monitor-dependent tests retry `getMonitors` for up to ~100 s: DDC/CI enumeration contends with the running Stream Deck application and the plugin recovers on its 10 s refresh cycle. They `test.skip` only when no monitor can be enumerated at all.
- Safety: no test writes VCP values — the flow stops at settings/inspection.

## Backend integration suite — `tests/StreamDeckMonSwitchMKI.Tests`

NUnit (.NET Framework 4.8.1, SDK-style project). Run via:

```powershell
dotnet test tests\StreamDeckMonSwitchMKI.Tests
```

The project references the **built plugin executable** (`backend/bin/Debug/StreamDeckMonSwitchMKI.sdPlugin/StreamDeckMonitorSwitch.exe`), so build the backend (Debug) first — `run-tests.ps1` does this automatically.

### Process-level integration (`PluginProcessE2ETests`, category `e2e`)

Launches the **real plugin executable** against `FakeStreamDeckServer` (a Fleck WebSocket server speaking the Stream Deck SDK protocol) and drives the full lifecycle:

| Test | Scenario |
|---|---|
| `Plugin_Registers_With_The_StreamDeck` | process start → `registerPlugin` with the plugin UUID |
| `WillAppear_Without_Settings_Persists_Default_Settings_And_Pushes_Icon` | `willAppear` → default `setSettings` + rendered `setImage` (base64 PNG) |
| `SendToPlugin_GetMonitors_Replies_With_Detected_Monitors` | PI request → `sendToPropertyInspector` reply matches physically detected monitors |
| `KeyDown_With_Unknown_Monitor_Model_Fails_Gracefully_And_Updates_Icon` | key press with an impossible monitor model → icon update, no crash |
| `DidReceiveSettings_Triggers_PropertyInspector_Update` | settings push → inspector data refresh |
| `SendToPlugin_GetProperties_Replies_When_Monitor_Is_Selected` | `getProperties` answered with exactly the writable, valued VCPs of the selected monitor |
| `SendToPlugin_GetValues_Replies_When_Monitor_And_Property_Are_Selected` | `getValues` answered with the value list of the selected property |

Safety: the key-press scenario uses a monitor model that cannot exist on the machine, so **no VCP value is ever written to a real monitor**. Tests that depend on attached monitors `Assert.Inconclusive` on monitor-less machines.

Implementation notes:

- The plugin resolves `img/computer-monitor-white.svg` relative to its working directory; the harness stages a temp dir containing `img/`.
- `SDMONSWITCH_UPDATE_PERIOD_MS` stretches the native monitor re-enumeration interval (read by `MonitorConfigurator`) so timers don't fire inside the test host, which also loads the plugin assembly.
- `getGlobalSettings` requests are auto-answered by the fake server.

### Contract tests (`BackendContractTests`)

In-process coverage of the pieces the e2e flow depends on:

- DDC/CI **capabilities string parsing** (`PhysicalMonitor.fromEmumeration`) — top-level fields, `cmds`, VCP codes with/without values, and the trailing-code regression (e.g. `D6` power mode).
- **VCP registry** consistency (`VCP_PROPS` indexes).
- **Settings schema** — the PI shape (`values` as one-element string arrays), lenient value coercion (`15`, `"15"`, `[15]`, `["15"]`), missing keys, round-trip serialization.
- **Inspector DTO** serialization shape consumed by the frontend.
- Icon power-status color completeness.

## Frontend component suite — `frontend/sdpi-components`

Jest + jsdom + jest-websocket-mock. Run via:

```powershell
cd frontend/sdpi-components
npx jest          # or: npm run test-silent
```

### Component tests (`src/components/__tests__/checkbox-list-custom.e2e.test.ts`)

Drives `sdpi-checkbox-list-custom` over the **real Stream Deck wire protocol** (a mocked WebSocket server stands in for the Stream Deck application + plugin backend):

| Test | Scenario |
|---|---|
| data sources flat items | `sendToPlugin getMonitors` → items render as checkboxes |
| grouped items | nested `getProperties` items render with group headers |
| multi-select persistence | checking two properties → `setSettings` matches the backend `Dictionary<string, List<string>>` contract |
| exclusive persistence | `exclusive` mode → `setSettings` values remain single-element arrays (backend coerces `["16"]` → `16`) |
| pre-checks + unchecking | existing settings render checked; unchecking the last item persists `[]` |

The asserted `setSettings` payloads mirror the backend contract tests, pinning the PI ↔ plugin schema from both sides.

The existing upstream sdpi-components suites (13 suites / 165 tests) run unchanged in the same command.

## What the suites do NOT cover

- Actual DDC/CI **writes** to physical monitors (deliberately avoided — see safety note above).
- Real Stream Deck hardware/application (the relay emulates the app's protocol behavior).
- Icon visual appearance (only that a non-empty base64 image is pushed).
