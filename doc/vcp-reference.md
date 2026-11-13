# VCP reference

VCP (Virtual Control Panel) codes are part of the MCCS (Monitor Control Command Set) standard, accessed over DDC/CI. This plugin knows the following codes (defined in `backend/src/MonitorConfigurator/VCP.cs` as `VCP_PROPS`).

Whether a code is usable on a given monitor depends on its capabilities string — the Property Inspector only offers codes the monitor advertises, that are writable, and that list at least one value.

| VCP code | Name (`codeName`) | Permissions | Notes |
|---|---|---|---|
| `0x02` | New Control Value | READ | |
| `0x04` | Restore Factory Defaults | WRITE | |
| `0x05` | Restore Factory Luminance/ Contrast | WRITE | |
| `0x08` | Restore Factory Color Defaults | WRITE | |
| `0x0B` | Color Temperature Increment | READ | |
| `0x0C` | Color Temperature Request | READ+WRITE | |
| `0x10` | Brightness | READ+WRITE | value range typically 0–100 |
| `0x12` | Contrast | READ+WRITE | |
| `0x14` | Select Color Preset | READ+WRITE | |
| `0x16` | Video Gain (Drive): Red | READ+WRITE | |
| `0x18` | Video Gain (Drive): Green | READ+WRITE | |
| `0x1A` | Video Gain (Drive): Blue | READ+WRITE | |
| `0x52` | Active Control | READ | |
| `0x60` | Input Select | READ+WRITE | **primary use case** — values are monitor-specific |
| `0xAC` | Horizontal Frequency | READ | |
| `0xAE` | Vertical Frequency | READ | |
| `0xB2` | Flat Panel Sub-Pixel Layout | READ | |
| `0xB6` | Display Technology Type | READ | |
| `0xC0` | Display Usage Time | READ | |
| `0xC6` | Application Enable Key | READ | |
| `0xC8` | Display Controller ID | READ+WRITE | |
| `0xC9` | Display Firmware Level | READ | |
| `0xCA` | OSD | READ+WRITE | |
| `0xCC` | OSD Language | READ+WRITE | |
| `0xD6` | Power mode | READ+WRITE | used for status polling |

Any code reported by a monitor but not in this table is exposed internally as an `"Unknown"` `VCPProperty` and is filtered out of the Property Inspector.

## Common Input Select (`0x60`) values

Values vary by vendor; frequently seen MCCS definitions:

| Value | Input |
|---|---|
| `0x01` (1) | VGA / Analog |
| `0x03` (3) | DVI |
| `0x04` (4) | HDMI |
| `0x05` (5) | HDMI 2 |
| `0x0F` (15) | DisplayPort 1 |
| `0x10` (16) | DisplayPort 2 |
| `0x11` (17) | USB-C |

Always trust the per-monitor value list shown in the Property Inspector over this table.

## Power mode (`0xD6`) values

Mapped by `MonitorStatus.PowerStatus` for icon coloring:

| Value | Status | Icon color |
|---|---|---|
| `0x01` | ON | LimeGreen |
| `0x04` | SLEEP | Yellow |
| `0x05` | OFF | Red |
| *(unreadable)* | UNKNOWN | Gray |
