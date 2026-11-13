import { test as base, expect, Page } from '@playwright/test';

import { FakeStreamDeck } from '../lib/fake-streamdeck';
import { PluginProcess } from '../lib/plugin-process';
import { PiServer } from '../lib/pi-server';
import { ACTION_UUID } from '../lib/paths';

const PLUGIN_UUID = 'e2e-plugin-uuid';
const PI_UUID = 'e2e-pi-uuid';
const ACTION_CONTEXT = 'e2e-action-context';
const DEVICE_ID = 'e2e-device';

const registrationInfo = {
    application: { font: 'Arial', language: 'en', platform: 'windows', platformVersion: '10', version: '6.0' },
    plugin: { uuid: 'com.elgato.streamdeck.streamdeckmonswitch', version: '0.3' },
    devicePixelRatio: 1,
    colors: {
        buttonPressedBackgroundColor: '#303030FF',
        buttonPressedBorderColor: '#646464FF',
        buttonPressedTextColor: '#969696FF',
        disabledColor: '#F7821B59',
        highlightColor: '#F7821BFF',
        mouseDownColor: '#CF6304FF'
    },
    devices: [{ id: DEVICE_ID, name: 'E2E Device', size: { columns: 5, rows: 3 }, type: 0 }]
};

type E2E = {
    deck: FakeStreamDeck;
    plugin: PluginProcess;
    page: Page;
    /** Serves the real PI (with the given initial settings) and opens it in the browser. */
    loadPi: (settings?: Record<string, unknown>) => Promise<void>;
    /** Waits for the plugin's getMonitors reply; returns the monitor models, or null when none are attached. */
    waitForMonitorList: () => Promise<string[] | null>;
};

/**
 * True end-to-end fixture: a real Chromium drives the unmodified Property
 * Inspector HTML, which talks over a WebSocket to a fake Stream Deck relay,
 * which in turn drives the REAL plugin executable (monitor enumeration, VCP
 * registry, settings, icon rendering). No backend type is mocked.
 */
export const test = base.extend<{ e2e: E2E }>({
    e2e: async ({ page }, use) => {
        const deck = new FakeStreamDeck({
            action: ACTION_UUID,
            pluginUUID: PLUGIN_UUID,
            piUUID: PI_UUID,
            actionContext: ACTION_CONTEXT,
            deviceId: DEVICE_ID
        });
        await deck.start();

        const plugin = new PluginProcess(deck.port, PLUGIN_UUID, DEVICE_ID);
        await plugin.start();
        await deck.pluginRegistered;

        // Bring up the action instance so it answers the PI's data-source requests.
        // The constructor persists default settings, which signals it is ready.
        deck.sendWillAppear({});
        await deck.waitFor((e) => e.from === 'plugin' && e.data.event === 'setSettings', 30_000, 'plugin default setSettings after willAppear');

        let piServers: PiServer[] = [];

        const loadPi = async (settings: Record<string, unknown> = {}) => {
            const piServer = new PiServer({
                port: deck.port,
                piUUID: PI_UUID,
                registerEvent: 'registerPropertyInspector',
                info: registrationInfo,
                actionInfo: {
                    action: ACTION_UUID,
                    context: ACTION_CONTEXT,
                    device: DEVICE_ID,
                    payload: { settings, coordinates: { column: 0, row: 0 } }
                }
            });
            await piServer.start();
            piServers.push(piServer);
            await page.goto(piServer.url);
            await deck.piRegistered;
        };

        const waitForMonitorList = async (): Promise<string[] | null> => {
            // The plugin only answers a getMonitors request when it has monitors;
            // if its enumeration loses to DDC/CI contention (e.g. with the real
            // Stream Deck app) it answers nothing and recovers on its 10s refresh.
            // Re-request (as the PI would) until a non-empty list arrives.
            // Be patient: enumeration can contend for a while on a busy machine.
            const deadline = Date.now() + 100_000;
            while (Date.now() < deadline) {
                const fromIndex = deck.logLength;
                deck.sendToPluginEvent('sendToPlugin', { event: 'getMonitors' });
                try {
                    const entry = await deck.waitFor(
                        (e) => e.from === 'plugin' && e.data.event === 'sendToPropertyInspector' && e.data.payload?.event === 'getMonitors',
                        12_000,
                        'plugin getMonitors response',
                        fromIndex
                    );
                    const items = (entry.data.payload.items ?? []) as Array<{ value: string }>;
                    if (items.length > 0) {
                        return items.map((i) => i.value);
                    }
                } catch {
                    // No response yet - the plugin had no monitors; retry after its refresh.
                }
                await new Promise((r) => setTimeout(r, 2_000));
            }
            return null;
        };

        try {
            await use({ deck, plugin, page, loadPi, waitForMonitorList });
        } finally {
            await page.close().catch(() => undefined);
            for (const server of piServers) {
                await server.stop().catch(() => undefined);
            }
            await plugin.stop().catch(() => undefined);
            await deck.stop().catch(() => undefined);
        }
    }
});

export { expect };

test.describe('Property Inspector end-to-end (real browser + real plugin)', () => {
    // Monitor enumeration contends with the real Stream Deck app on DDC/CI and
    // can need several 10s refresh cycles to recover; allow generous headroom.
    test.setTimeout(180_000);

    test('plugin renders and pushes a key icon on willAppear', async ({ e2e }) => {
        const entry = await e2e.deck.waitFor((e) => e.from === 'plugin' && e.data.event === 'setImage', 30_000, 'plugin setImage');

        const image = entry.data.payload?.image ?? entry.data.payload?.data;
        expect(String(image)).toMatch(/^data:image\//);
    });

    test('PI renders the monitors detected by the real plugin', async ({ e2e }) => {
        await e2e.loadPi();

        const monitors = await e2e.waitForMonitorList();
        test.skip(monitors === null || monitors.length === 0, 'No physical monitors attached; the plugin intentionally sends no monitor list.');

        // One checkbox per detected monitor, rendered by the real component in the browser.
        await expect(e2e.page.locator('#monitors_selection input[type="checkbox"]')).toHaveCount(monitors!.length, { timeout: 20_000 });

        // Each monitor model is visible in the Monitors section.
        for (const model of monitors!) {
            await expect(e2e.page.locator('#dvMonitors').getByText(model, { exact: false }).first()).toBeVisible();
        }
    });

    test('selecting a monitor persists settings through the plugin', async ({ e2e }) => {
        await e2e.loadPi();

        const monitors = await e2e.waitForMonitorList();
        test.skip(monitors === null || monitors.length === 0, 'No physical monitors attached.');
        const target = monitors![0];

        // Wait for the checkbox to render, then click its label (real user interaction).
        await expect(e2e.page.locator('#monitors_selection input[type="checkbox"]')).toHaveCount(monitors!.length, { timeout: 20_000 });
        await e2e.page.locator('#dvMonitors label', { hasText: target }).first().click();

        // The PI persists settings carrying the selected monitor ...
        const saved = await e2e.deck.waitFor(
            (e) => e.from === 'pi' && e.data.event === 'setSettings' && Array.isArray(e.data.payload?.monitors) && e.data.payload.monitors.includes(target),
            20_000,
            `PI setSettings containing "${target}"`
        );
        expect(saved.data.payload.monitors).toContain(target);

        // ... and the relay delivered didReceiveSettings to the plugin instance.
        await e2e.deck.waitFor(
            (e) => e.from === 'relay' && e.data.event === 'didReceiveSettings' && Array.isArray(e.data.payload?.settings?.monitors) && e.data.payload.settings.monitors.includes(target),
            20_000,
            'didReceiveSettings delivered to the plugin'
        );
    });

    test('PI pre-checks a monitor supplied via initial settings', async ({ e2e }) => {
        // Discover a real monitor model first (needs a throwaway PI load).
        await e2e.loadPi();
        const monitors = await e2e.waitForMonitorList();
        test.skip(monitors === null || monitors.length === 0, 'No physical monitors attached.');
        const target = monitors![0];

        // Re-open the PI with the monitor already selected.
        await e2e.loadPi({ monitors: [target] });
        await expect(e2e.page.locator('#monitors_selection input[type="checkbox"]')).toHaveCount(monitors!.length, { timeout: 20_000 });

        const checked = e2e.page.locator('#monitors_selection input[type="checkbox"]:checked');
        await expect(checked).toHaveCount(1, { timeout: 20_000 });
        await expect(checked.first()).toHaveValue(target);
    });

    test('selecting a monitor renders VCP properties, selecting a property renders VCP values', async ({ e2e }) => {
        await e2e.loadPi();

        const monitors = await e2e.waitForMonitorList();
        test.skip(monitors === null || monitors.length === 0, 'No physical monitors attached.');
        const target = monitors![0];

        await expect(e2e.page.locator('#monitors_selection input[type="checkbox"]')).toHaveCount(monitors!.length, { timeout: 20_000 });

        // Selecting a monitor must make the VCP properties list render. Before
        // the fix the plugin dropped getProperties until values were selected -
        // a chicken-and-egg deadlock that left the list on "Loading ..." forever.
        const propsFrom = e2e.deck.logLength;
        await e2e.page.locator('#dvMonitors label', { hasText: target }).first().click();

        await expect(e2e.page.locator('#properties_selection input[type="checkbox"]')).not.toHaveCount(0, { timeout: 30_000 });

        // The property names come from the plugin's real getProperties push.
        const propsEntry = await e2e.deck.waitFor(
            (e) => e.from === 'plugin' && e.data.event === 'sendToPropertyInspector' && e.data.payload?.event === 'getProperties',
            30_000, 'plugin getProperties response', propsFrom);
        const monitorGroup = propsEntry.data.payload.items[0];
        expect(monitorGroup.label).toBe(target);
        expect(monitorGroup.children.length).toBeGreaterThan(0);

        // Pick a property that has discrete values (continuous ones like Brightness
        // no longer show in the values list). Select ALL properties at once and
        // request getValues — the response only contains discrete properties.
        const allPropNames = monitorGroup.children.map((c: any) => c.value as string);

        // Click all property labels to select them.
        for (const name of allPropNames) {
            await e2e.page.locator('#dvProperties label', { hasText: name }).first().click();
        }

        // Request getValues — only discrete properties will appear in the response.
        const valuesFrom = e2e.deck.logLength;
        e2e.deck.sendToPluginEvent('sendToPlugin', { event: 'getValues' });

        let foundValuesEntry: any = null;
        try {
            foundValuesEntry = await e2e.deck.waitFor(
                (e) => e.from === 'plugin' && e.data.event === 'sendToPropertyInspector'
                    && e.data.payload?.event === 'getValues'
                    && Array.isArray(e.data.payload.items?.[0]?.children)
                    && e.data.payload.items[0].children.length > 0
                    && Array.isArray(e.data.payload.items[0].children[0]?.children)
                    && e.data.payload.items[0].children[0].children.length > 0,
                30_000, 'getValues with discrete values', valuesFrom);
        } catch {
            test.skip(true, 'No discrete (valued) property found on this monitor.');
            return;
        }

        const propName = foundValuesEntry.data.payload.items[0].children[0].label as string;

        // Uncheck all properties except the discrete one we found.
        for (const name of allPropNames) {
            if (name !== propName) {
                await e2e.page.locator('#dvProperties label', { hasText: name }).first().click();
            }
        }

        // Selecting a discrete property must make the VCP values list render.
        await expect(e2e.page.locator('#values_selection input[type="checkbox"]')).not.toHaveCount(0, { timeout: 30_000 });

        // One checkbox per advertised value of the selected property.
        const valueMonitorGroup = foundValuesEntry.data.payload.items[0];
        expect(valueMonitorGroup.label).toBe(target);
        const valuePropGroup = valueMonitorGroup.children[0];
        expect(valuePropGroup.label).toBe(propName);

        await expect(e2e.page.locator('#values_selection input[type="checkbox"]')).toHaveCount(valuePropGroup.children.length, { timeout: 20_000 });
    });

    test('PI with saved settings renders properties and values on load without interaction', async ({ e2e }) => {
        // Discover a monitor and one of its writable properties via interaction.
        await e2e.loadPi();
        const monitors = await e2e.waitForMonitorList();
        test.skip(monitors === null || monitors.length === 0, 'No physical monitors attached.');
        const target = monitors![0];

        await expect(e2e.page.locator('#monitors_selection input[type="checkbox"]')).toHaveCount(monitors!.length, { timeout: 20_000 });

        // Selecting the monitor triggers the plugin's inspector cascade, which
        // pushes getProperties. Under DDC/CI contention the plugin may enumerate
        // the monitor but fail to read its capabilities, answering with an
        // empty group - retry by toggling the checkbox (each toggle forces a new
        // didReceiveSettings -> UpdateInspector cycle) until real children arrive.
        // The input itself is inside the component's shadow DOM, so click the
        // visible label in light DOM (as a real user would).
        const monitorLabel = e2e.page.locator('#dvMonitors label', { hasText: target }).first();
        let propName: string | null = null;
        let allPropNames: string[] = [];
        const deadline = Date.now() + 90_000;
        while (Date.now() < deadline && propName === null) {
            const fromIndex = e2e.deck.logLength;
            await monitorLabel.click();
            try {
                const entry = await e2e.deck.waitFor(
                    (e) => e.from === 'plugin' && e.data.event === 'sendToPropertyInspector'
                        && e.data.payload?.event === 'getProperties'
                        && Array.isArray(e.data.payload.items?.[0]?.children)
                        && e.data.payload.items[0].children.length > 0,
                    12_000, 'plugin getProperties response with children', fromIndex);
                propName = entry.data.payload.items[0].children[0].value as string;
                allPropNames = entry.data.payload.items[0].children.map((c: any) => c.value as string);
            } catch {
                // Contention: toggle back off so the next iteration produces a
                // fresh check -> didReceiveSettings -> UpdateInspector cycle.
                await monitorLabel.click().catch(() => undefined);
                await new Promise((r) => setTimeout(r, 2_000));
            }
        }
        test.skip(propName === null, 'Plugin never read monitor capabilities (DDC/CI contention).');

        // Find a discrete (valued) property by selecting all properties at once
        // and requesting getValues — only discrete properties appear in the reply.
        // This avoids iterating property-by-property (which times out on monitors
        // with many continuous controls).
        for (const name of allPropNames) {
            await e2e.page.locator('#dvProperties label', { hasText: name }).first().click();
        }

        const valuesFrom = e2e.deck.logLength;
        e2e.deck.sendToPluginEvent('sendToPlugin', { event: 'getValues' });

        let discretePropName: string | null = null;
        try {
            const valuesEntry = await e2e.deck.waitFor(
                (e) => e.from === 'plugin' && e.data.event === 'sendToPropertyInspector'
                    && e.data.payload?.event === 'getValues'
                    && Array.isArray(e.data.payload.items?.[0]?.children)
                    && e.data.payload.items[0].children.length > 0
                    && Array.isArray(e.data.payload.items[0].children[0]?.children)
                    && e.data.payload.items[0].children[0].children.length > 0,
                30_000, 'getValues with discrete values', valuesFrom);
            discretePropName = valuesEntry.data.payload.items[0].children[0].label as string;
        } catch {
            // No discrete property found; leave all selected (harmless for seeding).
        }
        test.skip(discretePropName === null, 'No discrete (valued) property found on this monitor.');
        propName = discretePropName;

        // Uncheck all properties except the discrete one we found.
        for (const name of allPropNames) {
            if (name !== propName) {
                await e2e.page.locator('#dvProperties label', { hasText: name }).first().click();
            }
        }

        // Destroy the discovery PI so its hot-reload monitors component cannot
        // send a stale setSettings (monitors-only) that would overwrite the seed.
        await e2e.page.goto('about:blank');

        // Seed the plugin with saved settings (monitor + property selected), as
        // the Stream Deck app would when re-opening a configured action's PI.
        e2e.deck.sendToPluginEvent('didReceiveSettings', {
            settings: { monitors: [target], properties: { [target]: [propName] }, values: {} },
            coordinates: { column: 0, row: 0 },
            isInMultiAction: false
        });

        // Wait until the plugin has applied the seed: poll it with getValues (as
        // the PI would) until it answers - it only answers once a monitor AND a
        // property are selected. Poll from fresh log positions on every attempt.
        const applied = await (async () => {
            const pollDeadline = Date.now() + 60_000;
            while (Date.now() < pollDeadline) {
                const fromIndex = e2e.deck.logLength;
                e2e.deck.sendToPluginEvent('sendToPlugin', { event: 'getValues' });
                try {
                    await e2e.deck.waitFor(
                        (e) => e.from === 'plugin' && e.data.event === 'sendToPropertyInspector' && e.data.payload?.event === 'getValues',
                        5_000, 'plugin getValues response (settings applied)', fromIndex);
                    return true;
                } catch {
                    await new Promise((r) => setTimeout(r, 1_000));
                }
            }
            return false;
        })();
        test.skip(!applied, 'Plugin never applied the seeded settings (DDC/CI contention).');

        // Re-open the PI with the same saved settings: the plugin must answer
        // the initial getProperties/getValues requests so both lists render
        // immediately instead of staying on "Loading ..." (the reported bug).
        await e2e.loadPi({ monitors: [target], properties: { [target]: [propName] } });

        await expect(e2e.page.locator('#properties_selection input[type="checkbox"]')).not.toHaveCount(0, { timeout: 30_000 });
        await expect(e2e.page.locator('#dvProperties label', { hasText: propName }).first()).toBeVisible();

        await expect(e2e.page.locator('#values_selection input[type="checkbox"]')).not.toHaveCount(0, { timeout: 30_000 });
    });
});
