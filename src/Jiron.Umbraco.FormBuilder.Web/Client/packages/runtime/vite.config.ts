import { defineConfig } from 'vitest/config';
import { buildVersion } from '../../scripts/version.mjs';
import { sharedBuild, sharedPlugins, terserPass, websiteAssets } from '../../scripts/vite-shared.mjs';

const version = buildVersion();
const shared = sharedBuild({ version, outDir: `${websiteAssets}/runtime` });

// A module entry with a stable name and a content-hashed chunk per feature loaded on demand (ADR-0004, ADR-0026).
export default defineConfig({
  publicDir: false,
  define: { __JFB_VERSION__: JSON.stringify(version) },
  build: {
    ...shared,
    rolldownOptions: {
      ...shared.rolldownOptions,
      output: {
        ...shared.rolldownOptions.output,
        // The logic's base modules stay in the form chunk, which every feature chunk loads after: as a chunk of their
        // own, which the bundler makes once two features share them, they cost a file and its banner (ADR-0115).
        codeSplitting: {
          groups: [
            {
              name: 'form',
              test: /[\\/]packages[\\/](logic[\\/]src[\\/](errors|text|decimal|dates|culture|display)|runtime[\\/]src[\\/]form)\.ts$/,
            },
          ],
        },
      },
    },
    lib: { entry: 'src/jfb-runtime.ts', formats: ['es'], fileName: () => 'jfb-runtime.js' },
  },
  // Terser as well as the bundler's minifier, as section 4.12 budgets the runtime's size (ADR-0119).
  plugins: [...sharedPlugins({ version, name: 'runtime' }), terserPass()],
  test: {
    name: 'runtime',
    environment: 'jsdom',
  },
});
