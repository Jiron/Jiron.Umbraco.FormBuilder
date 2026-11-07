import { defineConfig } from 'vite';
import { buildVersion } from '../../scripts/version.mjs';
import { sharedBuild, sharedPlugins, terserPass, websiteAssets } from '../../scripts/vite-shared.mjs';

const version = buildVersion();
const shared = sharedBuild({ version, outDir: `${websiteAssets}/preview` });

// The preview script (ADR-0203): one module of its own, which only the preview's document loads, so nothing of it
// reaches a visitor's page or the runtime's budget.
export default defineConfig({
  publicDir: false,
  build: {
    ...shared,
    lib: { entry: 'src/preview.ts', formats: ['es'], fileName: () => 'jfb-preview.js' },
  },
  plugins: [...sharedPlugins({ version, name: 'preview' }), terserPass()],
});
