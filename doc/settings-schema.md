# Settings schema

Per-key settings persisted by the Stream Deck SDK (round-tripped through `PluginSettings` in `MonitorPluginAction.cs`). Written by the Property Inspector controls and by `Connection.SetSettingsAsync`.

## Shape

```json
{
  "monitors": ["MODEL-A", "MODEL-B"],

  "properties": {
    "MODEL-A": ["Input Select", "Brightness"],
    "MODEL-B": ["Input Select"]
  },

  "values": {
    "MODEL-A": { "Input Select": [17], "Brightness": [64] },
    "MODEL-B": { "Input Select": [15] }
  }
}
```

## Fields

| JSON key | C# property | Type | Meaning |
|---|---|---|---|
| `monitors` | `Monitors` | `List<string>` | Selected monitor model names (from capabilities string `model(...)`) |
| `properties` | `Properties` | `Dictionary<string, List<string>>` | Per monitor: selected VCP property **code names** (e.g. `"Input Select"`) |
| `values` | `VCPActionsSettings` | `Dictionary<string, Dictionary<string, int>>` | Per monitor → per property: the single integer value to apply |

Notes:

- Keys are monitor **model strings**, matched against `MonitorConfigurator.Monitors[].model`. Two identical-model monitors are not distinguished.
- Property names are resolved to VCP codes at run time via `VCP_PROPS.IndexByCodeName`.
- `values` is what `KeyPressed` actually executes; `monitors`/`properties` drive the PI's cascading dropdowns.
- `values` uses `DefaultValueHandling.Populate`, so missing keys deserialize to empty dictionaries.

## Example: switch two monitors to DisplayPort

```json
{
  "monitors": ["U2723QE", "P2422H"],
  "properties": { "U2723QE": ["Input Select"], "P2422H": ["Input Select"] },
  "values": { "U2723QE": { "Input Select": [15] }, "P2422H": { "Input Select": [15] } }
}
```

Pressing the key calls `SetVCPFeature(handle, 0x60, 15)` on each monitor. Actual input-select value codes are monitor-specific — the PI offers only the values each monitor advertises in its capabilities string.
