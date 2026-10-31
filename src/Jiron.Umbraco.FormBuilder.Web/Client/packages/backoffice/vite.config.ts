import { defineConfig } from 'vitest/config';
import { buildVersion } from '../../scripts/version.mjs';
import { backofficeFolder, sharedBuild, sharedPlugins, stampPackageVersion } from '../../scripts/vite-shared.mjs';

const version = buildVersion();
const outDir = backofficeFolder;

// One bundle that registers every extension (ADR-0025). Umbraco's backoffice provides @umbraco-cms/* through its
// import map, so those imports stay external.
export default defineConfig({
  build: {
    ...sharedBuild({ version, outDir, external: [/^@umbraco-cms[/]/] }),
    lib: { entry: 'src/bundle.manifests.ts', formats: ['es'], fileName: () => 'jfb-backoffice.js' },
  },
  plugins: [...sharedPlugins({ version, name: 'backoffice' }), stampPackageVersion(outDir, version)],
  test: {
    name: 'backoffice',
    environment: 'jsdom',
    // Umbraco's packages hold directory imports that Node's resolver refuses, so Vite transforms them (ADR-0134).
    server: { deps: { inline: [/@umbraco-cms[/]/, /@umbraco-ui[/]/] } },
    setupFiles: ['src/test-setup.ts'],
  },
});
