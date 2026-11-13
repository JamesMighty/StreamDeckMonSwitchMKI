import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));

/** Repository root (tests/e2e/lib -> repo root). */
export const repoRoot = path.resolve(here, '..', '..', '..');

/** Built plugin executable (Debug). */
export const pluginExe = path.join(repoRoot, 'backend', 'bin', 'Debug', 'StreamDeckMonSwitchMKI.sdPlugin', 'StreamDeckMonitorSwitch.exe');

/** The SVG the plugin loads relative to its working directory. */
export const monitorSvg = path.join(repoRoot, 'backend', 'img', 'computer-monitor-white.svg');

/** Property Inspector sources. */
export const piHtml = path.join(repoRoot, 'frontend', 'property_inspector', 'PluginActionPI.html');
export const piJquery = path.join(repoRoot, 'frontend', 'property_inspector', 'code.jquery.com_jquery-3.7.1.min.js');

/** Built sdpi-components bundle (produced by `npm run build_only`). */
export const sdpiBundle = path.join(repoRoot, 'frontend', 'sdpi-components', 'example', 'pi', 'sdpi-components.js');
export const sdpiPackageDir = path.join(repoRoot, 'frontend', 'sdpi-components');

/** The action UUID registered by the plugin and referenced by the PI. */
export const ACTION_UUID = 'com.elgato.streamdeck.streamdeckmonswitch.monitorpluginaction';
