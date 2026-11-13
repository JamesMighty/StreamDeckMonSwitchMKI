import type { Config } from '@jest/types';

/*
 * For a detailed explanation regarding each configuration property and type check, visit:
 * https://jestjs.io/docs/configuration
 */
const config: Config.InitialOptions = {
    coverageReporters: ['json-summary', 'text'],
    maxWorkers: 1,
    testEnvironment: 'jsdom',
    transform: {
        '^.+\\.ts?$': [
            'ts-jest',
            {
                isolatedModules: true
            }
        ],
        // The lit family is ESM-only; transpile it to CJS for jsdom tests.
        '^.+\\.(js|mjs)$': [
            'ts-jest',
            {
                isolatedModules: true,
                tsconfig: {
                    allowJs: true
                }
            }
        ]
    },
    transformIgnorePatterns: ['node_modules/(?!(lit|lit-html|lit-element|@lit|@lit-labs)/)']
};

export default config;
