# StreamDeckMonSwitchMKI — Documentation

An Elgato Stream Deck plugin that lets you view and change monitor **VCP** (Virtual Control Panel) values over DDC/CI — most commonly the monitor's **input source**, but also brightness, contrast, power mode, and more.

Pressing a Stream Deck key applies a configured set of VCP values to one or more monitors. The key icon shows live monitor state (power, whether the configured input is currently selected).

## Contents

| Document | Description |
|---|---|
| [Architecture](./architecture.md) | High-level design, components, and data flow |
| [Backend](./backend.md) | The .NET plugin process: actions, monitor access, icons |
| [Frontend](./frontend.md) | The Property Inspector UI and its data sources |
| [Settings schema](./settings-schema.md) | JSON structure persisted per key |
| [VCP reference](./vcp-reference.md) | Known VCP codes supported by the plugin |
| [CI/CD](./cicd.md) | Ansible + Docker build/deploy pipeline |
| [Testing](./testing.md) | Test tiers: Playwright browser e2e, plugin-process integration, component tests |

## Quick facts

- **Platform:** Windows 10+ (Stream Deck software ≥ 4.9, SDK 3)
- **Backend:** C# / .NET Framework 4.8.1, built on [BarRaider/streamdeck-tools](https://github.com/BarRaider/streamdeck-tools) (`StreamDeck-Tools` 6.3.1)
- **Monitor access:** Windows `dxva2.dll` DDC/CI APIs via P/Invoke
- **Frontend:** HTML Property Inspector using a custom fork of [sdpi-components](https://github.com/GeekyEggo/sdpi-components)
- **Plugin UUID (action):** `com.elgato.streamdeck.streamdeckmonswitch.monitorpluginaction`
- **Manifest:** [`backend/manifest.json`](../backend/manifest.json)

## Repository layout

```
backend/     C# plugin executable (Stream Deck subprocess)
frontend/    Property Inspector HTML + sdpi-components fork
ci_cd/       Ansible playbooks, Docker containers, compose pipeline
packages/    Restored NuGet packages (offline)
release/     Composed .sdPlugin build output
doc/         This documentation
```

## How it works in one paragraph

The Stream Deck application launches the plugin executable (`backend/StreamDeckMonitorSwitch.exe`) as a subprocess. The backend enumerates physical monitors via `EnumDisplayMonitors` + `dxva2.dll`, parses each monitor's DDC/CI capabilities string to discover which VCP codes it supports, and exposes that data to the Property Inspector so the user can pick monitors → VCP properties → values. On key press, the backend calls `SetVCPFeature` for each configured monitor/value pair. A background timer polls each monitor's input select (`0x60`) and power mode (`0xD6`) codes and tints the key icon accordingly.
