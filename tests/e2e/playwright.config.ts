import { defineConfig } from '@playwright/test';

export default defineConfig({
    testDir: './specs',
    timeout: 90_000,
    expect: { timeout: 15_000 },
    fullyParallel: false,
    workers: 1,
    retries: 0,
    reporter: [['list']],
    use: {
        headless: true,
        viewport: { width: 400, height: 800 },
        actionTimeout: 15_000
    },
    projects: [
        {
            name: 'chromium',
            use: { browserName: 'chromium' }
        }
    ]
});
