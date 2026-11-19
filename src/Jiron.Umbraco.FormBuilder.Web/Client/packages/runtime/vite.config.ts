import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import type { Plugin } from 'vite';
import { defineConfig } from 'vitest/config';
import { buildVersion } from '../../scripts/version.mjs';
import { sharedBuild, sharedPlugins, terserPass, websiteAssets } from '../../scripts/vite-shared.mjs';

const version = buildVersion();
const shared = sharedBuild({ version, outDir: `${websiteAssets}/runtime` });

/** The entry's file, which a page loads as it is. */
const ENTRY = 'jfb-runtime.js';
/** The entry's own modules that read a form through `@jfb/logic/proto`. */
const ENTRY_MODULES = /[\\/]packages[\\/]runtime[\\/]src[\\/](frame|fit)\.ts$/;
const PROTO = fileURLToPath(new URL('../logic/src/proto.ts', import.meta.url));
/** The entry's copy of it: an id beside the source that no file has. */
const PROTO_IN_ENTRY = PROTO.replace(/proto\.ts$/, 'proto.entry.ts');

/**
 * The entry runs alone: it hears a visitor's first answers while the form chunk is still on its way, so nothing may load
 * before it, and a chunk cannot import from it, as a page loads it under an address of its own (`?v=`). So the entry's
 * modules get a copy of `@jfb/logic/proto` of their own, built from the one source, and the chunks share theirs through
 * the form chunk; the build fails when the entry imports anything statically.
 */
function entryRunsAlone(): Plugin {
  return {
    name: 'jfb-entry-runs-alone',
    apply: 'build',
    enforce: 'pre',
    resolveId(source, importer) {
      if (source === '@jfb/logic/proto' && importer !== undefined && ENTRY_MODULES.test(importer))
        return PROTO_IN_ENTRY;
      return null;
    },
    load(id) {
      return id === PROTO_IN_ENTRY ? readFileSync(PROTO, 'utf8') : null;
    },
    generateBundle(_, bundle) {
      const entry = bundle[ENTRY];
      if (entry?.type === 'chunk' && entry.imports.length > 0)
        this.error(
          `${ENTRY} imports ${entry.imports.join(', ')} statically, so a page would wait for it before the runtime hears a ` +
            "visitor's first answers. Give the entry's module that brings it in a copy of its own (ENTRY_MODULES in " +
            'packages/runtime/vite.config.ts), or import it dynamically.',
        );
    },
  };
}

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
    lib: { entry: 'src/jfb-runtime.ts', formats: ['es'], fileName: () => ENTRY },
  },
  // Terser as well as the bundler's minifier, as section 4.12 budgets the runtime's size (ADR-0119).
  plugins: [...sharedPlugins({ version, name: 'runtime' }), terserPass(), entryRunsAlone()],
  test: {
    name: 'runtime',
    environment: 'jsdom',
    // Forms covered as a browser covers them, so a plain read of a form's member fails a suite (testing-cover.ts).
    setupFiles: ['src/testing-setup.ts'],
  },
});
