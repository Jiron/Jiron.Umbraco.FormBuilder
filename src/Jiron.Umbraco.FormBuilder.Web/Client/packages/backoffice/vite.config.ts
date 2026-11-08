import { defineConfig } from 'vitest/config';
import { lessTemplateText } from '../../scripts/templates.mjs';
import { buildVersion } from '../../scripts/version.mjs';
import { backofficeFolder, sharedBuild, sharedPlugins, stampPackageVersion } from '../../scripts/vite-shared.mjs';

const version = buildVersion();
const outDir = backofficeFolder;
const shared = sharedBuild({ version, outDir, external: [/^@umbraco-cms[/]/] });

/** The package's own modules: its sources, not the words and not the generated client, which keep chunks of their own. */
const OWN = /[\\/]backoffice[\\/]src[\\/](?!lang[\\/]|api[\\/])/;

/**
 * Fewer, larger chunks, so a screen loads fewer files and the bundle carries fewer copies of the licence banner
 * (ADR-0216): what the start imports in one chunk, and each folder's modules that load on demand in chunks of their
 * folder, none larger than the budget of one file allows.
 */
const GROUPS = [
  {
    name: 'start',
    tags: ['$initial' as const],
    test: (id: string) => OWN.test(id) && !/bundle\.manifests\.ts$/.test(id),
    priority: 2,
  },
  {
    name: (id: string) => /[\\/]backoffice[\\/]src[\\/]([^\\/]+)[\\/]/.exec(id)?.[1] ?? null,
    test: (id: string) => OWN.test(id),
    priority: 1,
    maxSize: 160_000,
  },
];

// One bundle that registers every extension (ADR-0025). Umbraco's backoffice provides @umbraco-cms/* through its
// import map, so those imports stay external.
export default defineConfig({
  build: {
    ...shared,
    rolldownOptions: {
      ...shared.rolldownOptions,
      output: { ...shared.rolldownOptions.output, codeSplitting: { groups: GROUPS }, strictExecutionOrder: true },
    },
    lib: { entry: 'src/bundle.manifests.ts', formats: ['es'], fileName: () => 'jfb-backoffice.js' },
  },
  // The templates' comments and line breaks go before the bundler compiles them (ADR-0216).
  plugins: [
    lessTemplateText(),
    ...sharedPlugins({ version, name: 'backoffice' }),
    stampPackageVersion(outDir, version),
  ],
  test: {
    name: 'backoffice',
    environment: 'jsdom',
    // Umbraco's packages hold directory imports that Node's resolver refuses, so Vite transforms them (ADR-0134).
    server: { deps: { inline: [/@umbraco-cms[/]/, /@umbraco-ui[/]/] } },
    setupFiles: ['src/test-setup.ts'],
  },
});
