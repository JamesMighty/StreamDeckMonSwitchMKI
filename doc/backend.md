# Backend

C# / .NET Framework 4.8.1 WinExe. Runs as a subprocess of the Stream Deck application. Built on [`StreamDeck-Tools` 6.3.1](https://github.com/BarRaider/streamdeck-tools) (BarRaider), which wraps the Stream Deck SDK protocol.

- Project: [`backend/StreamDeckMonitorSwitch.csproj`](../backend/StreamDeckMonitorSwitch.csproj)
- Assembly: `StreamDeckMonitorSwitch.exe` (root namespace `StreamDeckMonitorSwitch`)
- Entry point: `Program.cs` → `SDWrapper.Run(args)`
- NuGet packages are vendored in [`packages/`](../packages) (see `packages.config`).

## Build configurations

| Configuration | Output path | Notes |
|---|---|---|
| `Debug` | `bin/Debug/StreamDeckMonSwitchMKI.sdPlugin/` | `DEBUG;TRACE`, x64, full symbols |
| `Release` | `bin/Release/StreamDeckMonSwitchMKI.sdPlugin/` | optimized |
| `ReleaseAndRun` | `bin/ReleaseAndRun/` | optimized + full debug info; used by CI |

## Source layout

```
backend/
├── Program.cs                          # entry point
├── manifest.json                       # Stream Deck plugin manifest
└── src/
    ├── Actions/MonitorPluginAction.cs  # the keypad action
    ├── DTOs/                           # Property Inspector payloads
    ├── IconsManager/                   # icon rendering + status polling
    └── MonitorConfigurator/            # DDC/CI monitor access + VCP defs
```

## MonitorPluginAction (`src/Actions/MonitorPluginAction.cs`)

Registered with `[PluginActionId("com.elgato.streamdeck.streamdeckmonswitch.monitorpluginaction")]`, extends `KeypadBase`.

### Key members

| Member | Purpose |
|---|---|
| `Settings` (`PluginSettings`) | Deserialized per-key settings (see [Settings schema](./settings-schema.md)) |
| `svgIcon` | The live `SvgDocument` rendered onto the key |
| `IsChangingVCP` | Re-entrancy guard while applying values |
| `PushIcon()` | Renders the SVG at 128×128 and calls `SetImageAsync` |

### Lifecycle

- **Constructor** — loads or initializes settings, registers with `IconManager`, subscribes to `OnSendToPlugin` (PI requests).
- **`KeyPressed`** — applies all configured VCP values (see below).
- **`KeyReleased`** — clears `IsChangingVCP`.
- **`ReceivedSettings`** — re-deserializes settings; rebuilds the icon if the monitor count changed; pushes fresh data to the PI via `UpdateInspector()`.
- **`Dispose`** — unregisters from `IconManager`.

### Applying values (`KeyPressed`)

For each `monitor model → { vcpCodeName → value }` in `Settings.VCPActionsSettings`:

1. Find the `PhysicalMonitor` whose `model` matches.
2. Resolve the VCP code via `VCP_PROPS.IndexByCodeName[codeName].code`.
3. Call `MonitorConfigurator.SetVCPFeature(hPhysicalMonitor, code, value)`.
4. Tint the icon outline: yellow on success, red on failure (orange while running).

### Property Inspector communication

The PI sends `sendToPlugin` events; the backend replies with `sendToPropertyInspector`:

| PI event | Backend response | Payload |
|---|---|---|
| `getMonitors` | `UpdateInspectorMonitors()` | flat list of monitor models |
| `getProperties` | `UpdateInspectorProperties()` | per-monitor group of writable, known, valued VCP names |
| `getValues` | `UpdateInspectorValues()` | per-monitor → per-VCP group of allowed values |

Responses use `InspectorDataSourceDTO` (`{ event, items[] }`) with `InspectorDataSourceItemDTO` (leaf) and `InspectorDataSourceItemGroupDTO` (group with `children`). Filters applied: VCP must be known (`!= Unknown`), writable (`VCPPropertyPerms.WRITE`), and advertise at least one value.

## MonitorConfigurator (`src/MonitorConfigurator/MonitorConfigurator.cs`)

Static class owning all monitor enumeration and DDC/CI access. Namespace `StreamDeckMonitorSwitch.ddcmon`.

### Behavior

- Static constructor enumerates monitors immediately and starts a `System.Threading.Timer` refreshing every `UPDATE_PERIOD_MS` (10 s), firing `OnUpdateMonitors` after each refresh.
- `Monitors` — `List<PhysicalMonitor>` of currently detected monitors (replaced atomically per refresh).

### Win32 APIs (P/Invoke)

| API | DLL | Used for |
|---|---|---|
| `EnumDisplayMonitors`, `GetMonitorInfo` | user32 | enumerate display handles + geometry |
| `GetNumberOfPhysicalMonitorsFromHMONITOR`, `GetPhysicalMonitorsFromHMONITOR` | dxva2 | map display → physical monitor handle |
| `GetCapabilitiesStringLength`, `CapabilitiesRequestAndCapabilitiesReply` | dxva2 | fetch DDC/CI capabilities string |
| `SetVCPFeature`, `GetVCPFeatureAndVCPFeatureReply` | dxva2 | write/read VCP values |
| `DDCCIGetVCPFeature` | gdi32 | alternate VCP read |
| `GetDevicePowerState` | kernel32 | fallback power detection |

### Capabilities string parsing

`PhysicalMonitor.fromEmumeration(...)` parses the DDC/CI capabilities string, e.g.:

```
(prot(monitor)type(lcd)model(ABC123)cmds(01 02 ...)vcp(02 04 10(00 64) 60(0F 10 11) D6(01 04 05)))
```

- Top-level `key(value)` pairs become monitor fields (`prot`, `type`, `model`, `mswhql`, ...) via reflection.
- `cmds(...)` → hex list of supported commands.
- `vcp(...)` → for each VCP code, the nested parentheses list its allowed values; each becomes a `possibleVCPValues[VCPProperty] = List<int>` entry. Unknown codes get a `VCPProperty` named `"Unknown"`.

## VCP definitions (`src/MonitorConfigurator/VCP.cs`)

- `VCPProperty` — `{ code, codeName, perms }` where `perms` is `VCPPropertyPerms` (`READ`, `WRITE`, `READ_WRITE`).
- `VCP_PROPS` — static registry of known VCP codes with lookup dictionaries `IndexByHexCode` and `IndexByCodeName`. See [VCP reference](./vcp-reference.md).

## IconManager (`src/IconsManager/IconManager.cs`)

Static manager that renders and refreshes key icons.

- **Registration** — each action registers on construction (`Register`), unregisters on `Dispose`.
- **Icon construction** (`MakeIcon`) — opens `img/computer-monitor-white.svg`:
  - Multi-monitor actions: clones the monitor layer as a semi-transparent offset background.
  - Single-monitor actions: scales the monitor shape to match the physical monitor's aspect ratio.
- **Status polling** — a timer (due 5 s, period 2 s, guarded by `TimerState`) reads per monitor:
  - `INPUT_SELECT (0x60)` current value,
  - `POWER_MODE (0xD6)`; falls back to `GetDevicePowerState` when unreadable.
- **Icon tinting**:
  - Outline: **lime green** if every configured input select already matches, else **white**.
  - Power button color by power status:

| PowerStatus | Value | Color |
|---|---|---|
| `ON` | 0x01 | LimeGreen |
| `SLEEP` | 0x04 | Yellow |
| `OFF` | 0x05 | Red |
| `UNKNOWN` | 0x10 | Gray |

Supporting types: `MonitorStatus.cs`, `TimerState.cs`, `TimerStates.cs`.

## Logging

Uses `BarRaider.SdTools.Logger` (`TracingLevel.DEBUG/INFO/WARN`). Logs land in the Stream Deck plugin log location (see StreamDeck-Tools docs). Debug-wait loop in `Program.cs` is commented out — uncomment to attach a debugger.
