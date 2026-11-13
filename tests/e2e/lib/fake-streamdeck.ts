import { WebSocketServer, WebSocket } from 'ws';

export type Json = Record<string, any>;

export interface LogEntry {
    from: 'plugin' | 'pi' | 'relay';
    data: Json;
    time: number;
}

/**
 * A fake Stream Deck application: one WebSocket server to which both the real
 * plugin process and the browser-hosted Property Inspector connect.
 *
 * It behaves like the Stream Deck app:
 *  - accepts `registerPlugin` / `registerPropertyInspector` registrations
 *  - answers `getGlobalSettings`
 *  - routes PI -> plugin (`sendToPlugin`) and plugin -> PI (`sendToPropertyInspector`)
 *  - persists PI `setSettings`, echoing `didReceiveSettings` to the PI and
 *    notifying the plugin (which is how ReceivedSettings fires in the backend)
 *  - can inject action lifecycle events (`willAppear`, `keyDown`, ...)
 *
 * Everything is logged for assertions.
 */
export class FakeStreamDeck {
    private wss!: WebSocketServer;
    public port = 0;

    public readonly action: string;
    public readonly pluginUUID: string;
    public readonly piUUID: string;
    public readonly actionContext: string;
    public readonly deviceId: string;

    public log: LogEntry[] = [];
    public lastSettings: Json = {};

    private pluginSocket?: WebSocket;
    private piSocket?: WebSocket;

    private resolvePluginRegistered?: () => void;
    public pluginRegistered = new Promise<void>((resolve) => (this.resolvePluginRegistered = resolve));
    private resolvePiRegistered?: () => void;
    public piRegistered = new Promise<void>((resolve) => (this.resolvePiRegistered = resolve));

    constructor(opts: { action: string; pluginUUID: string; piUUID: string; actionContext: string; deviceId: string }) {
        this.action = opts.action;
        this.pluginUUID = opts.pluginUUID;
        this.piUUID = opts.piUUID;
        this.actionContext = opts.actionContext;
        this.deviceId = opts.deviceId;
    }

    public async start(): Promise<void> {
        this.wss = new WebSocketServer({ port: 0, host: '127.0.0.1' });
        await new Promise<void>((resolve) => this.wss.once('listening', resolve));
        this.port = (this.wss.address() as any).port;

        this.wss.on('connection', (socket) => {
            socket.on('message', (raw) => {
                let data: Json;
                try {
                    data = JSON.parse(raw.toString());
                } catch {
                    return;
                }
                this.route(socket, data);
            });
        });
    }

    private record(from: LogEntry['from'], data: Json): void {
        this.log.push({ from, data, time: Date.now() });
    }

    private send(socket: WebSocket | undefined, data: Json): void {
        if (socket && socket.readyState === WebSocket.OPEN) {
            this.record('relay', data);
            socket.send(JSON.stringify(data));
        }
    }

    private route(socket: WebSocket, data: Json): void {
        switch (data.event) {
            case 'registerPlugin':
                this.pluginSocket = socket;
                this.record('plugin', data);
                this.resolvePluginRegistered?.();
                return;

            case 'registerPropertyInspector':
                this.piSocket = socket;
                this.record('pi', data);
                this.resolvePiRegistered?.();
                return;
        }

        if (socket === this.pluginSocket) {
            this.record('plugin', data);
            switch (data.event) {
                case 'getGlobalSettings':
                    this.send(this.pluginSocket, {
                        event: 'didReceiveGlobalSettings',
                        context: this.pluginUUID,
                        payload: { settings: {} }
                    });
                    return;

                case 'sendToPropertyInspector':
                    this.send(this.piSocket, data);
                    return;

                case 'setSettings':
                    // The plugin persists settings (e.g. defaults on first willAppear).
                    this.lastSettings = data.payload ?? {};
                    this.send(this.piSocket, this.didReceiveSettingsForPi(this.lastSettings));
                    return;
            }
            return;
        }

        if (socket === this.piSocket) {
            this.record('pi', data);
            switch (data.event) {
                case 'sendToPlugin':
                    // The real app delivers PI messages to the action instance context.
                    this.send(this.pluginSocket, { ...data, context: this.actionContext });
                    return;

                case 'setSettings':
                    this.lastSettings = data.payload ?? {};
                    // Echo to the PI (hot-reload) and notify the plugin instance.
                    this.send(this.piSocket, this.didReceiveSettingsForPi(this.lastSettings));
                    this.send(this.pluginSocket, {
                        action: this.action,
                        event: 'didReceiveSettings',
                        context: this.actionContext,
                        device: this.deviceId,
                        payload: {
                            settings: this.lastSettings,
                            coordinates: { column: 0, row: 0 },
                            isInMultiAction: false
                        }
                    });
                    return;

                case 'getSettings':
                    this.send(this.piSocket, this.didReceiveSettingsForPi(this.lastSettings));
                    return;

                case 'getGlobalSettings':
                    this.send(this.piSocket, {
                        event: 'didReceiveGlobalSettings',
                        context: this.piUUID,
                        payload: { settings: {} }
                    });
                    return;
            }
        }
    }

    private didReceiveSettingsForPi(settings: Json): Json {
        return {
            action: this.action,
            event: 'didReceiveSettings',
            context: this.piUUID,
            device: this.deviceId,
            payload: {
                settings,
                coordinates: { column: 0, row: 0 },
                isInMultiAction: false
            }
        };
    }

    /** Sends an action lifecycle event to the plugin (as the Stream Deck app would). */
    public sendToPluginEvent(event: string, payload: Json): void {
        this.send(this.pluginSocket, {
            action: this.action,
            event,
            context: this.actionContext,
            device: this.deviceId,
            payload
        });
    }

    public sendWillAppear(settings: Json): void {
        this.sendToPluginEvent('willAppear', {
            settings,
            coordinates: { column: 0, row: 0 },
            state: 0,
            isInMultiAction: false,
            controller: 'Keypad'
        });
    }

    /** All `setSettings` payloads sent by the PI, in order. */
    public get piSetSettings(): Json[] {
        return this.log.filter((e) => e.from === 'pi' && e.data.event === 'setSettings').map((e) => e.data.payload);
    }

    /** All `sendToPropertyInspector` payloads the plugin pushed, in order. */
    public get pluginToPi(): Json[] {
        return this.log.filter((e) => e.from === 'plugin' && e.data.event === 'sendToPropertyInspector').map((e) => e.data.payload);
    }

    /** Waits until the predicate matches a logged entry (polls; checks future messages too). */
    public async waitFor(predicate: (entry: LogEntry) => boolean, timeoutMs = 20_000, description = '', fromIndex = 0): Promise<LogEntry> {
        const deadline = Date.now() + timeoutMs;
        let seen = Math.max(0, fromIndex);
        while (Date.now() < deadline) {
            for (; seen < this.log.length; seen++) {
                if (predicate(this.log[seen])) {
                    return this.log[seen];
                }
            }
            await new Promise((r) => setTimeout(r, 100));
        }
        throw new Error(`Timed out after ${timeoutMs}ms waiting for: ${description}`);
    }

    /** Current log length (use as `fromIndex` to only match entries logged after this point). */
    public get logLength(): number {
        return this.log.length;
    }

    public async stop(): Promise<void> {
        for (const client of this.wss.clients) {
            client.terminate();
        }
        await new Promise<void>((resolve) => this.wss.close(() => resolve()));
    }
}
