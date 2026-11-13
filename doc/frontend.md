# Frontend

The Property Inspector (PI) — the settings panel shown in the Stream Deck application when a key using this action is selected. It is pure HTML/JS: it renders controls, collects selections into the settings object, and fetches dynamic option lists from the backend.

## Layout

```
frontend/
├── property_inspector/
│   ├── PluginActionPI.html              # the PI page (referenced by manifest.json)
│   └── code.jquery.com_jquery-3.7.1.min.js
└── sdpi-components/                     # custom fork of GeekyEggo/sdpi-components v2.6.2
    ├── src/                             # Lit-based web components (TypeScript)
    ├── example/pi/                      # build output copied into the release (sdpi-components.js)
    └── rollup.config.mjs                # bundler config
```

## PluginActionPI.html

Three stacked `sdpi-checkbox-list-custom` controls, each bound to one part of the settings object:

| Control | `setting=` | `datasource=` | Meaning |
|---|---|---|---|
| Monitors | `monitors` | `getMonitors` | which physical monitors the key targets |
| VCP Properties | `properties` | `getProperties` | which VCP properties to change (grouped per monitor) |
| VCP Values | `values` | `getValues` | which value to set for each chosen property (grouped per monitor → property) |

## Data sourcing

The `datasource` attribute makes the component send a `sendToPlugin` event with `{ "event": "<name>" }`. The backend replies via `sendToPropertyInspector` with:

```json
{
  "event": "getMonitors",
  "items": [
    { "value": "MODEL-A", "label": "MODEL-A" },
    { "label": "MODEL-B", "children": [ { "value": "Brightness", "label": "Brightness" } ] }
  ]
}
```

Items are either leaves (`value`/`label`) or groups (`label` + `children`). Groups are rendered with a header; nested groups produce nested settings paths (see below). The `monitors_selection` list additionally uses `hot-reload`, re-requesting data when settings change.

## Custom component: `sdpi-checkbox-list-custom`

Defined in `sdpi-components/src/components/checkbox-list-custom.ts` — the fork's main addition over upstream sdpi-components. It is a checkbox list whose checked state and writes are **path-based**, enabling grouped/nested settings:

- Each rendered child accumulates a `groups` string from its parent group labels (dot-joined, e.g. `MODEL-A.Brightness`).
- **Checked state** — looks up `value[<group path>]` and checks whether the item's value is present.
- **On check** — `setByPath(value, "<group path>", itemValue)` creates/overwrites the array at that path with the value.
- **On uncheck** — `removeByPath(...)` removes the value; empty arrays are deleted.

This is exactly what produces the nested [settings schema](./settings-schema.md):

- `monitors` → flat array of model names,
- `properties` → `{ model: [propertyNames...] }`,
- `values` → `{ model: { propertyName: [value] } }`.

> Note: `setByPath` resets the target array before pushing, so within one group path only a single value is kept — intentional for `values` (one target value per property), while `properties` items live under distinct paths.

## Building sdpi-components

The fork builds with Rollup (see `rollup.config.mjs`):

```sh
cd frontend/sdpi-components
npm install --force
npm run build_only     # emits example/pi/sdpi-components.js (+ .map)
```

The CI pipeline runs this inside the `frontend-builder` Docker container and copies `example/pi/sdpi-components.js` into the release (see [CI/CD](./cicd.md)).

## Debugging the Property Inspector

1. Enable HTML remote debugging: add DWORD `html_remote_debugging_enabled = 1` under
   `HKEY_CURRENT_USER\Software\Elgato Systems GmbH\StreamDeck`.
2. Open DevTools at `http://localhost:23654/`.

See [Elgato SDK docs — debugging](https://docs.elgato.com/sdk/plugins/getting-started#id-5.-debugging).
