import { spawn, ChildProcess } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';

import { pluginExe, monitorSvg } from './paths';

export interface PluginInfo {
    application: Record<string, unknown>;
    plugin: Record<string, unknown>;
    devicePixelRatio: number;
    devices: Record<string, unknown>[];
}

/**
 * Launches the REAL plugin executable (StreamDeckMonitorSwitch.exe) pointed at
 * the fake Stream Deck WebSocket server, staging the working directory the
 * plugin expects (img/computer-monitor-white.svg is resolved relative to CWD).
 */
export class PluginProcess {
    private child?: ChildProcess;
    public stdout = '';
    public stderr = '';
    public workDir = '';

    constructor(
        private readonly port: number,
        private readonly pluginUUID: string,
        private readonly deviceId: string
    ) {}

    public get running(): boolean {
        return !!this.child && this.child.exitCode === null;
    }

    public async start(): Promise<void> {
        if (!fs.existsSync(pluginExe)) {
            throw new Error(`Plugin executable not found at ${pluginExe} - build the backend (Debug) first.`);
        }

        // Stage a working directory containing img/ (the plugin opens the SVG
        // relative to its CWD).
        this.workDir = fs.mkdtempSync(path.join(os.tmpdir(), 'sdmonswitch-e2e-'));
        fs.mkdirSync(path.join(this.workDir, 'img'));
        fs.copyFileSync(monitorSvg, path.join(this.workDir, 'img', 'computer-monitor-white.svg'));

        const info: PluginInfo = {
            application: {
                font: 'Arial',
                language: 'en',
                platform: 'windows',
                platformVersion: '10',
                version: '6.0'
            },
            plugin: {
                uuid: 'com.elgato.streamdeck.streamdeckmonswitch',
                version: '0.3'
            },
            devicePixelRatio: 1,
            devices: [
                {
                    id: this.deviceId,
                    name: 'E2E Device',
                    size: { columns: 5, rows: 3 },
                    type: 0
                }
            ]
        };

        const args = [
            '-port', String(this.port),
            '-pluginUUID', this.pluginUUID,
            '-registerEvent', 'registerPlugin',
            // CommandLineParser expects --option; SDWrapper normalizes - to --.
            '-info', JSON.stringify(info)
        ];

        this.child = spawn(pluginExe, args, {
            cwd: this.workDir
            // Note: the monitor refresh period is intentionally left at its default
            // (10s) so the plugin self-heals when its initial DDC/CI enumeration
            // loses to contention (e.g. right after a previous plugin process ended).
        });

        this.child.stdout?.on('data', (d) => (this.stdout += d.toString()));
        this.child.stderr?.on('data', (d) => (this.stderr += d.toString()));
    }

    public async stop(): Promise<void> {
        if (this.child && this.child.exitCode === null) {
            this.child.kill();
            await new Promise<void>((resolve) => {
                const timer = setTimeout(() => {
                    this.child?.kill('SIGKILL');
                    resolve();
                }, 4000);
                this.child?.once('exit', () => {
                    clearTimeout(timer);
                    resolve();
                });
            });
        }
        if (this.workDir) {
            fs.rmSync(this.workDir, { recursive: true, force: true });
        }
    }
}
