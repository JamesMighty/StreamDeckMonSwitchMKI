import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';

import { piHtml, piJquery, sdpiBundle } from './paths';

export interface PiBootstrapOptions {
    /** WebSocket port of the fake Stream Deck server. */
    port: number;
    piUUID: string;
    registerEvent: string;
    /** Registration info (application/plugin metadata). */
    info: Record<string, unknown>;
    /** Action info, including payload.settings with the initial per-key settings. */
    actionInfo: Record<string, unknown>;
}

/**
 * Serves the real Property Inspector page (`PluginActionPI.html`) together with
 * its script dependencies, injecting the `connectElgatoStreamDeckSocket`
 * bootstrap call that the Stream Deck application normally performs when it
 * loads a Property Inspector. This lets a real browser drive the unmodified PI.
 */
export class PiServer {
    private server?: http.Server;
    public port = 0;

    constructor(private readonly opts: PiBootstrapOptions) {}

    public async start(): Promise<void> {
        for (const file of [piHtml, piJquery, sdpiBundle]) {
            if (!fs.existsSync(file)) {
                throw new Error(`Missing PI asset: ${file}` + (file === sdpiBundle ? ' - run `npm run build_only` in frontend/sdpi-components first.' : ''));
            }
        }

        this.server = http.createServer((req, res) => this.handle(req, res));
        await new Promise<void>((resolve) => this.server!.listen(0, '127.0.0.1', resolve));
        this.port = (this.server.address() as any).port;
    }

    private handle(req: http.IncomingMessage, res: http.ServerResponse): void {
        const url = (req.url ?? '/').split('?')[0];

        if (url === '/' || url === '/PluginActionPI.html') {
            const html = fs.readFileSync(piHtml, 'utf8');
            res.writeHead(200, { 'Content-Type': 'text/html; charset=utf-8' });
            res.end(this.injectBootstrap(html));
            return;
        }

        if (url.endsWith('jquery-3.7.1.min.js') || url === '/code.jquery.com_jquery-3.7.1.min.js') {
            res.writeHead(200, { 'Content-Type': 'application/javascript' });
            res.end(fs.readFileSync(piJquery));
            return;
        }

        if (url === '/sdpi-components.js') {
            res.writeHead(200, { 'Content-Type': 'application/javascript' });
            res.end(fs.readFileSync(sdpiBundle));
            return;
        }

        res.writeHead(404);
        res.end('not found');
    }

    /** Injects the Stream Deck bootstrap call before `</body>`. */
    private injectBootstrap(html: string): string {
        const bootstrap = `
<script>
  window.addEventListener('DOMContentLoaded', function () {
    window.connectElgatoStreamDeckSocket(
      ${JSON.stringify(String(this.opts.port))},
      ${JSON.stringify(this.opts.piUUID)},
      ${JSON.stringify(this.opts.registerEvent)},
      ${JSON.stringify(JSON.stringify(this.opts.info))},
      ${JSON.stringify(JSON.stringify(this.opts.actionInfo))}
    );
  });
</script>
`;
        return html.replace('</body>', bootstrap + '</body>');
    }

    public get url(): string {
        return `http://127.0.0.1:${this.port}/PluginActionPI.html`;
    }

    public async stop(): Promise<void> {
        await new Promise<void>((resolve) => {
            if (!this.server) return resolve();
            this.server.close(() => resolve());
        });
    }
}
