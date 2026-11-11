import { defineConfig } from 'vitest/config';
import { lessTemplateText } from '../../scripts/templates.mjs';
import { buildVersion } from '../../scripts/version.mjs';
import { addOnFolder, sharedBuild, sharedPlugins, stampPackageVersion } from '../../scripts/vite-shared.mjs';

const version = buildVersion();
const outDir = addOnFolder;
const shared = sharedBuild({ version, outDir, external: [/^@umbraco-cms[/]/] });

/** The package's own modules: its sources, not the words and not the generated client, which keep chunks of their own. */
const OWN = /[\\/]ai[\\/]src[\\/](?!lang[\\/]|api[\\/])/;

/**
 * What every backoffice session loads, the manifests, in one small start chunk; each feature's screens load on demand
 * in a chunk of their folder (ADR-0216, ADR-0018).
 */
const GROUPS = [
  {
    // The generated client, which several screens share, in a chunk of its own.
    name: 'ai-api',
    test: (id: string) => /[\\/]ai[\\/]src[\\/](api[\\/]|hey-api\.ts$)/.test(id),
    priority: 3,
  },
  {
    name: 'ai-start',
    tags: ['$initial' as const],
    test: (id: string) => OWN.test(id) && !/bundle\.manifests\.ts$/.test(id),
    priority: 2,
  },
  {
    name: (id: string) => `ai-${/[\\/]ai[\\/]src[\\/]([^\\/]+)[\\/]/.exec(id)?.[1] ?? 'misc'}`,
    test: (id: string) => OWN.test(id),
    priority: 1,
  },
];

// The add-on's one bundle, which its own umbraco-package.json names, built into the add-on's project (ADR-0018).
// Umbraco's backoffice provides @umbraco-cms/* through its import map, so those imports stay external.
export default defineConfig({
  build: {
    ...shared,
    rolldownOptions: {
      ...shared.rolldownOptions,
      output: { ...shared.rolldownOptions.output, codeSplitting: { groups: GROUPS }, strictExecutionOrder: true },
    },
    lib: { entry: 'src/bundle.manifests.ts', formats: ['es'], fileName: () => 'jfb-ai.js' },
  },
  plugins: [lessTemplateText(), ...sharedPlugins({ version, name: 'ai' }), stampPackageVersion(outDir, version)],
  test: {
    name: 'ai',
    environment: 'jsdom',
    // Umbraco's packages hold directory imports that Node's resolver refuses, so Vite transforms them (ADR-0134).
    server: { deps: { inline: [/@umbraco-cms[/]/, /@umbraco-ui[/]/] } },
    setupFiles: ['src/test-setup.ts'],
  },
});
