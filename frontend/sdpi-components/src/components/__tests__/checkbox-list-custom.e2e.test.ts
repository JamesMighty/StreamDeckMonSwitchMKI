import { WS as WebSocketServer } from 'jest-websocket-mock';

import '../checkbox-list-custom';
import streamDeckClient from '../../stream-deck/stream-deck-client';
import { actionInfo, info } from '../../stream-deck/__mocks__/registration';

/**
 * End-to-end tests for the plugin's custom checkbox list, driven over the real
 * Stream Deck wire protocol (a mocked WebSocket server standing in for the
 * Stream Deck application + plugin backend):
 *
 *   PI element -> sendToPlugin { event: getMonitors|getProperties|getValues }
 *             <- sendToPropertyInspector { event, items }
 *   user checks boxes -> setSettings { monitors|properties|values }
 *
 * The asserted `setSettings` payloads match the backend's PluginSettings
 * contract (see doc/settings-schema.md and the backend contract tests).
 *
 * A single connection is shared across the suite (as in the real Property
 * Inspector: one WebSocket, three lists). The Stream Deck client and the
 * custom element are module-scoped singletons, so modules are NOT reset and
 * jest.mock is NOT used (the manual mock creates its own server that collides).
 */
describe('sdpi-checkbox-list-custom (e2e)', () => {
    const port = '13';
    const propertyInspectorUUID = 'ABC123';
    const registerEvent = 'registerPropertyInspector';

    let server: WebSocketServer;

    /** Routes `sendToPlugin` requests to per-datasource responders set by each test. */
    const responders: Record<string, () => unknown[]> = {};

    const flush = (ms = 0) => new Promise((resolve) => setTimeout(resolve, ms));

    type El = HTMLElement & { updateComplete: Promise<unknown>; shadowRoot: ShadowRoot };

    beforeAll(async () => {
        server = new WebSocketServer(`ws://localhost:${port}`, { jsonProtocol: true });

        // Intercept client->server messages and route datasource requests to the
        // per-test responders (same pattern as stream-deck-client.test.ts).
        server.on('connection', (socket) => {
            socket.on('message', (raw: unknown) => {
                const data = typeof raw === 'string' ? JSON.parse(raw) : raw;
                if (data.event === 'sendToPlugin') {
                    const datasourceEvent = data.payload?.event;
                    const respond = responders[datasourceEvent];
                    if (respond) {
                        server.send({
                            event: 'sendToPropertyInspector',
                            action: data.action,
                            context: data.context,
                            payload: { event: datasourceEvent, items: respond() }
                        });
                    }
                }
            });
        });

        streamDeckClient.connect(port, propertyInspectorUUID, registerEvent, info, actionInfo);
        await server.connected;
        await server.nextMessage; // registration message
    });

    afterAll(() => {
        WebSocketServer.clean();
    });

    beforeEach(() => {
        document.body.innerHTML = '';
        for (const key of Object.keys(responders)) {
            delete responders[key];
        }
    });

    /** Waits until the (mock) Stream Deck receives a message matching the predicate. */
    async function waitForSent(predicate: (msg: any) => boolean, timeoutMs = 5000): Promise<any> {
        const deadline = Date.now() + timeoutMs;
        while (Date.now() < deadline) {
            // eslint-disable-next-line no-await-in-loop
            const msg: any = await Promise.race([
                server.nextMessage,
                new Promise((resolve) => setTimeout(() => resolve('__timeout__'), Math.max(1, deadline - Date.now())))
            ]);
            if (msg === '__timeout__') {
                break;
            }
            if (predicate(msg)) {
                return msg;
            }
        }
        throw new Error('Timed out waiting for a matching message from the property inspector');
    }

    /** Creates the component and waits for it to render its data source. */
    async function mount(attrs: Record<string, string>): Promise<El> {
        const el = document.createElement('sdpi-checkbox-list-custom') as El;
        Object.entries(attrs).forEach(([name, value]) => el.setAttribute(name, value));
        document.body.appendChild(el);
        await el.updateComplete;
        await flush();
        await el.updateComplete;
        return el;
    }

    /** Waits until the component has rendered at least one checkbox (task resolved + re-rendered). */
    async function waitForCheckboxes(el: El, timeoutMs = 3000): Promise<void> {
        const deadline = Date.now() + timeoutMs;
        while (Date.now() < deadline) {
            if (el.shadowRoot.querySelectorAll('input[type="checkbox"]').length > 0) {
                return;
            }
            // eslint-disable-next-line no-await-in-loop
            await el.updateComplete;
            // eslint-disable-next-line no-await-in-loop
            await flush(10);
        }
        throw new Error('Timed out waiting for checkboxes to render');
    }

    /** Simulates a user (un)checking a rendered checkbox by its value. */
    function toggle(el: El, value: string, checked: boolean): void {
        const input = Array.from(el.shadowRoot.querySelectorAll('input[type="checkbox"]')).find((i) => (i as HTMLInputElement).value === value) as HTMLInputElement | undefined;
        expect(input).toBeDefined();
        input.checked = checked;
        input.dispatchEvent(new Event('change', { bubbles: true }));
    }

    const checkboxValues = (el: El) => Array.from(el.shadowRoot.querySelectorAll('input[type="checkbox"]')).map((i) => (i as HTMLInputElement).value);
    const checkedValues = (el: El) => Array.from(el.shadowRoot.querySelectorAll('input[type="checkbox"]:checked')).map((i) => (i as HTMLInputElement).value);

    it('data sources flat items via sendToPlugin and renders a checkbox per item', async () => {
        responders['getMonitors'] = () => [
            { value: 'MODEL-A', label: 'MODEL-A' },
            { value: 'MODEL-B', label: 'MODEL-B' }
        ];

        const el = await mount({ setting: 'monitors', datasource: 'getMonitors' });

        // The PI asked the plugin for monitors ...
        const request = await waitForSent((msg) => msg.event === 'sendToPlugin' && msg.payload?.event === 'getMonitors');
        expect(request.payload).toEqual({ event: 'getMonitors' });

        // ... and rendered one checkbox per monitor, none pre-selected.
        await waitForCheckboxes(el);
        expect(checkboxValues(el)).toEqual(['MODEL-A', 'MODEL-B']);
        expect(checkedValues(el)).toEqual([]);
    });

    it('renders grouped items with a group header and one checkbox per child', async () => {
        responders['getProperties'] = () => [
            {
                label: 'MODEL-A',
                children: [
                    { value: 'Input Select', label: 'Input Select' },
                    { value: 'Brightness', label: 'Brightness' }
                ]
            }
        ];

        const el = await mount({ setting: 'properties', datasource: 'getProperties' });
        await waitForSent((msg) => msg.event === 'sendToPlugin' && msg.payload?.event === 'getProperties');
        await waitForCheckboxes(el);

        expect(checkboxValues(el)).toEqual(['Input Select', 'Brightness']);
        expect(el.shadowRoot.querySelectorAll('.group').length).toBe(1);
        expect(el.shadowRoot.querySelector('.group').textContent).toContain('MODEL-A');
    });

    it('persists multi-select settings grouped by monitor (backend properties contract)', async () => {
        responders['getProperties'] = () => [
            {
                label: 'MODEL-A',
                children: [
                    { value: 'Input Select', label: 'Input Select' },
                    { value: 'Brightness', label: 'Brightness' }
                ]
            }
        ];

        const el = await mount({ setting: 'properties', datasource: 'getProperties' });
        await waitForSent((msg) => msg.event === 'sendToPlugin' && msg.payload?.event === 'getProperties');
        await waitForCheckboxes(el);

        // when: the user selects both properties.
        toggle(el, 'Input Select', true);
        toggle(el, 'Brightness', true);

        // then: the persisted settings match Dictionary<string, List<string>>.
        const saved = await waitForSent((msg) => msg.event === 'setSettings' && msg.payload?.properties?.['MODEL-A']?.length === 2);
        expect(saved.payload.properties).toEqual({
            'MODEL-A': ['Input Select', 'Brightness']
        });
    });

    it('persists exclusive selections as single-element arrays (backend values contract)', async () => {
        responders['getValues'] = () => [
            {
                label: 'MODEL-A',
                children: [
                    {
                        label: 'Input Select',
                        children: [
                            { value: '15', label: '15' },
                            { value: '16', label: '16' }
                        ]
                    }
                ]
            }
        ];

        const el = await mount({ setting: 'values', datasource: 'getValues', exclusive: '' });
        await waitForSent((msg) => msg.event === 'sendToPlugin' && msg.payload?.event === 'getValues');
        await waitForCheckboxes(el);

        // when: the user picks a value, changes their mind, and picks another.
        toggle(el, '15', true);
        await flush();
        toggle(el, '15', false);
        await flush();
        toggle(el, '16', true);

        // then: exactly one value remains - the backend coerces ["16"] to 16.
        // (saves are not debounced, so skip intermediate states and match the final one)
        const saved = await waitForSent((msg) => msg.event === 'setSettings' && msg.payload?.values?.['MODEL-A']?.['Input Select']?.[0] === '16');
        expect(saved.payload.values).toEqual({
            'MODEL-A': {
                'Input Select': ['16']
            }
        });

        // and the rendered check state follows the exclusive selection.
        await el.updateComplete;
        expect(checkedValues(el)).toEqual(['16']);
    });

    it('pre-checks boxes from loaded settings and persists unchecking', async () => {
        responders['getMonitors'] = () => [
            { value: 'MODEL-A', label: 'MODEL-A' },
            { value: 'MODEL-B', label: 'MODEL-B' }
        ];

        const el = await mount({ setting: 'monitors', datasource: 'getMonitors' });
        await waitForSent((msg) => msg.event === 'sendToPlugin' && msg.payload?.event === 'getMonitors');
        await waitForCheckboxes(el);

        // given: settings arrive already containing a selection (as loaded from the action).
        (el as any).value = ['MODEL-A'];
        await el.updateComplete;
        await flush();
        await el.updateComplete;

        // then: the existing selection is rendered as checked.
        expect(checkedValues(el)).toEqual(['MODEL-A']);

        // when: the user unchecks it.
        toggle(el, 'MODEL-A', false);

        // then: the setting is emptied.
        const saved = await waitForSent((msg) => msg.event === 'setSettings' && Array.isArray(msg.payload?.monitors) && msg.payload.monitors.length === 0);
        expect(saved.payload.monitors).toEqual([]);
    });
});
