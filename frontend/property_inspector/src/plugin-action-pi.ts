/**
 * Property Inspector logic for the StreamDeckMonSwitch monitor action.
 *
 * This file is compiled to `dist/plugin-action-pi.js` and loaded in the PI
 * HTML <head> BEFORE `sdpi-components.js`, so the WebSocket patch below
 * captures the single connection the component bundle creates.
 *
 * The DOM wiring that used to live at the end of <body> now waits for
 * DOMContentLoaded, which fires after the whole document (including the
 * sdpi custom elements) has been parsed.
 */

// ---------------------------------------------------------------------------
// Globals
// ---------------------------------------------------------------------------

/** The PI's WebSocket captured from the patched constructor. */
let piSocket: WebSocket | null = null;

/** The PI context UUID supplied by the Stream Deck bootstrap. */
let piUUID: string | null = null;

/** VCP property type info pushed by the plugin (getVcpInfo). */
let vcpInfo: VcpInfoItem[] = [];

/** True when any selected property is a continuous (value-range) control. */
let isContinuous = false;

/** Max value of the (first) continuous property, used to bound the step slider. */
let maxValue = 100;

interface VcpInfoItem {
    monitor: string;
    property: string;
    type: 'discrete' | 'continuous';
    maxValue?: number;
}

// ---------------------------------------------------------------------------
// WebSocket interception
// ---------------------------------------------------------------------------

const OrigWebSocket = window.WebSocket;

// Replace the global WebSocket constructor so we can grab the PI's connection
// the moment sdpi-components opens it. Only the first socket is captured.
(window as any).WebSocket = function (this: WebSocket, url: string, protocols?: string | string[]) {
    const ws: WebSocket = protocols !== undefined
        ? new OrigWebSocket(url, protocols)
        : new OrigWebSocket(url);

    if (!piSocket) {
        piSocket = ws;
        ws.addEventListener('message', (ev: MessageEvent) => {
            try {
                const msg = JSON.parse(ev.data);
                if (
                    msg.event === 'sendToPropertyInspector' &&
                    msg.payload &&
                    msg.payload.event === 'getVcpInfo'
                ) {
                    handleVcpInfo(msg.payload.items);
                }
            } catch (e) {
                // Ignore malformed frames.
            }
        });
    }
    return ws;
} as any;

// Preserve the static surface of the original constructor.
(window as any).WebSocket.prototype = OrigWebSocket.prototype;
(window as any).WebSocket.CONNECTING = OrigWebSocket.CONNECTING;
(window as any).WebSocket.OPEN = OrigWebSocket.OPEN;
(window as any).WebSocket.CLOSING = OrigWebSocket.CLOSING;
(window as any).WebSocket.CLOSED = OrigWebSocket.CLOSED;

// ---------------------------------------------------------------------------
// Plugin messaging
// ---------------------------------------------------------------------------

function sendToPlugin(payload: Record<string, unknown>): void {
    if (piSocket && piSocket.readyState === OrigWebSocket.OPEN) {
        piSocket.send(JSON.stringify({
            event: 'sendToPlugin',
            action: 'com.elgato.streamdeck.streamdeckmonswitch.monitorpluginaction',
            context: piUUID,
            payload: payload
        }));
    }
}

// Capture the PI's UUID when the Stream Deck app invokes the bootstrap
// (the bundle calls any pre-existing handler first).
(window as any).connectElgatoStreamDeckSocket = function (
    port: number,
    uuid: string,
    registerEvent: string,
    info: unknown,
    actionInfo: unknown
): void {
    piUUID = uuid;
};

// ---------------------------------------------------------------------------
// VCP info handling & mode UI
// ---------------------------------------------------------------------------

function handleVcpInfo(items: VcpInfoItem[] | null): void {
    vcpInfo = items || [];
    updateModeUI();
}

function updateModeUI(): void {
    // Determine if any selected property is continuous.
    isContinuous = vcpInfo.some(item => item.type === 'continuous');
    if (isContinuous) {
        const continuousItem = vcpInfo.find(item => item.type === 'continuous');
        maxValue = (continuousItem && continuousItem.maxValue) || 100;
    }

    const modeRadio = document.getElementById('mode_selection') as HTMLSelectElement | null;
    const valuesSection = document.getElementById('dvValues');

    if (!modeRadio) return;

    if (isContinuous) {
        // Continuous: show set (target), scrollUp, scrollDown. No VCP values list.
        modeRadio.innerHTML =
            '<option value="set">Set</option>' +
            '<option value="scrollUp">Scroll Up</option>' +
            '<option value="scrollDown">Scroll Down</option>';
        if (valuesSection) valuesSection.style.display = 'none';
        // Update step size slider max.
        const stepSlider = document.getElementById('step_size_slider') as HTMLInputElement | null;
        if (stepSlider) stepSlider.max = String(maxValue);
    } else {
        // Discrete: show set, toggle, loop. Values list applies.
        modeRadio.innerHTML =
            '<option value="set">Set</option>' +
            '<option value="toggle">Toggle</option>' +
            '<option value="loop">Loop</option>';
        if (valuesSection) valuesSection.style.display = '';
    }

    // If the currently saved mode is not valid for this property type,
    // fall back to 'set' so the radio always shows a checked option.
    const validModes = isContinuous ? ['set', 'scrollUp', 'scrollDown'] : ['set', 'toggle', 'loop'];
    const currentMode = modeRadio.value || 'set';
    if (validModes.indexOf(currentMode) === -1) {
        modeRadio.value = 'set';
    }

    applyModeVisibility();
}

// Show/hide step-size, wrap-around and target-value sections based on the
// currently selected mode and whether the property is continuous.
function applyModeVisibility(): void {
    const modeRadio = document.getElementById('mode_selection') as HTMLSelectElement | null;
    const stepSection = document.getElementById('dvStepSize');
    const wrapSection = document.getElementById('dvWrapAround');
    const targetSection = document.getElementById('dvTargetValue');
    if (!modeRadio) return;

    const mode = modeRadio.value || 'set';

    if (isContinuous) {
        if (mode === 'set') {
            // Continuous set: only the target value input is relevant.
            if (stepSection) stepSection.style.display = 'none';
            if (wrapSection) wrapSection.style.display = 'none';
            if (targetSection) targetSection.style.display = '';
        } else {
            // scrollUp / scrollDown: step size + wrap around, no target input.
            if (stepSection) stepSection.style.display = '';
            if (wrapSection) wrapSection.style.display = '';
            if (targetSection) targetSection.style.display = 'none';
        }
    } else {
        // Discrete modes never use step size, wrap around or target value.
        if (stepSection) stepSection.style.display = 'none';
        if (wrapSection) wrapSection.style.display = 'none';
        if (targetSection) targetSection.style.display = 'none';
    }
}

// ---------------------------------------------------------------------------
// DOM wiring (replaces the old end-of-body script)
// ---------------------------------------------------------------------------

function wireUp(): void {
    const propertiesList = document.getElementById('properties_selection');
    const modeRadio = document.getElementById('mode_selection');
    const valuesList = document.getElementById('values_selection');

    // When properties change, request VCP info from the plugin.
    if (propertiesList) {
        propertiesList.addEventListener('valuechange', () => {
            setTimeout(() => { sendToPlugin({ event: 'getVcpInfo' }); }, 500);
        });
    }

    // Sync exclusive attribute on values list based on mode, and update
    // section visibility whenever the mode changes.
    function syncExclusivity(): void {
        const mode = (modeRadio as HTMLSelectElement | null)?.value || 'set';
        if (!valuesList) return;
        if (mode === 'set' && !isContinuous) {
            valuesList.setAttribute('exclusive', '');
        } else {
            valuesList.removeAttribute('exclusive');
        }
        applyModeVisibility();
    }

    if (modeRadio) {
        modeRadio.addEventListener('valuechange', syncExclusivity);
    }

    // Initial sync after components load.
    customElements.whenDefined('sdpi-radio').then(() => {
        setTimeout(() => {
            syncExclusivity();
            sendToPlugin({ event: 'getVcpInfo' });
        }, 200);
    });
}

document.addEventListener('DOMContentLoaded', wireUp);
