import { defineConfig } from 'vitest/config';
import { buildVersion } from '../../scripts/version.mjs';
import { sharedBuild, sharedPlugins, websiteAssets } from '../../scripts/vite-shared.mjs';

const version = buildVersion();

// A module entry with a stable name and a content-hashed chunk per feature loaded on demand (ADR-0004, ADR-0026).
export default defineConfig({
  publicDir: false,
  define: { __JFB_VERSION__: JSON.stringify(version) },
  build: {
    ...sharedBuild({ version, outDir: `${websiteAssets}/runtime` }),
    lib: { entry: 'src/jfb-runtime.ts', formats: ['es'], fileName: () => 'jfb-runtime.js' },
  },
  plugins: sharedPlugins({ version, name: 'runtime' }),
  test: {
    name: 'runtime',
    environment: 'jsdom',
  },
});
