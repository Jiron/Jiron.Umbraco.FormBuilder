import { defineConfig } from 'vitest/config';
import { buildVersion } from '../../scripts/version.mjs';
import { sharedBuild, sharedPlugins, websiteAssets } from '../../scripts/vite-shared.mjs';

const version = buildVersion();

// A module entry with a stable name, like the runtime's (ADR-0026).
export default defineConfig({
  publicDir: false,
  build: {
    ...sharedBuild({ version, outDir: `${websiteAssets}/element` }),
    lib: { entry: 'src/jfb-element.ts', formats: ['es'], fileName: () => 'jfb-element.js' },
  },
  plugins: sharedPlugins({ version, name: 'element' }),
  test: {
    name: 'element',
    environment: 'jsdom',
  },
});
