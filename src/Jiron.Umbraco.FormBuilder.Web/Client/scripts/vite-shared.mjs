// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
// What every package build shares (ADR-0026): the licence banner on every JS and CSS file, content-hashed chunk
// names, source maps only on request, and a report of the chunks for scripts/size-check.mjs; and the terser pass the
// website runtime takes (ADR-0119).
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { isAbsolute, join, relative, resolve, sep } from 'node:path';
import { fileURLToPath } from 'node:url';
import { minify } from 'terser';
import { licenceBanner } from './banner.mjs';

export const clientRoot = join(fileURLToPath(new URL('.', import.meta.url)), '..');
/** The Web project, which the client builds into. */
export const webProject = join(clientRoot, '..');
/** The backoffice files: static web assets under /App_Plugins/JironFormBuilder/. */
export const backofficeFolder = join(webProject, 'wwwroot', 'App_Plugins', 'JironFormBuilder');
/** The AI add-on's backoffice files: static web assets of its own project, under /App_Plugins/JironFormBuilderAI/ (ADR-0018). */
export const addOnFolder = join(
  webProject,
  '..',
  'Jiron.Umbraco.FormBuilder.AI',
  'wwwroot',
  'App_Plugins',
  'JironFormBuilderAI',
);
/** The website assets: embedded in the Web assembly and served under /jfb/assets/ (ADR-0026). */
export const websiteAssets = join(webProject, 'WebsiteAssets');
/** Build records that are not assets: the chunk reports and the stamp. Git-ignored, never packed. */
export const buildFolder = join(clientRoot, '.build');

/**
 * Adds the banner to every CSS file, which the bundler's own banner option does not reach.
 * @param {string} banner
 * @returns {import('vite').Plugin}
 */
export function cssLicenceBanner(banner) {
  return {
    name: 'jfb-css-licence-banner',
    apply: 'build',
    enforce: 'post',
    generateBundle(_options, bundle) {
      for (const file of Object.values(bundle)) {
        if (file.type !== 'asset' || !file.fileName.endsWith('.css')) continue;
        const source = typeof file.source === 'string' ? file.source : new TextDecoder().decode(file.source);
        if (!source.startsWith(banner)) file.source = `${banner}\n${source}`;
      }
    },
  };
}

/**
 * Writes `.build/<name>.chunks.json`: each emitted file with its static and dynamic imports and the modules it holds,
 * so the size check can tell the code a page loads at once from the features it loads on demand, and a language's
 * words from a feature. The folder and the modules are relative to the Web project.
 * @param {string} name
 * @param {{ buildDir?: string, base?: string }} [options] where the report goes, and what its folder is relative to
 * @returns {import('vite').Plugin}
 */
export function chunkReport(name, { buildDir = buildFolder, base = webProject } = {}) {
  return {
    name: 'jfb-chunk-report',
    apply: 'build',
    writeBundle(options, bundle) {
      const dir = resolve(options.dir ?? '.');
      const folder = relative(base, dir).split(sep).join('/');
      const files = Object.values(bundle)
        .filter((file) => file.type === 'chunk' || file.fileName.endsWith('.css'))
        .map((file) =>
          file.type === 'chunk'
            ? {
                file: file.fileName,
                entry: file.isEntry,
                imports: file.imports,
                dynamicImports: file.dynamicImports,
                modules: file.moduleIds
                  .filter((id) => isAbsolute(id))
                  .map((id) => relative(base, id).split(sep).join('/'))
                  .sort(),
              }
            : { file: file.fileName, entry: false, imports: [], dynamicImports: [] },
        )
        .sort((a, b) => a.file.localeCompare(b.file));
      mkdirSync(buildDir, { recursive: true });
      writeFileSync(join(buildDir, `${name}.chunks.json`), `${JSON.stringify({ folder, files }, null, 2)}\n`);
    },
  };
}

/**
 * Stamps the build version into umbraco-package.json, whose `version` drives the backoffice's cache busting
 * (ROADMAP section 3.4).
 * @param {string} outDir
 * @param {string} version
 * @returns {import('vite').Plugin}
 */
export function stampPackageVersion(outDir, version) {
  return {
    name: 'jfb-stamp-package-version',
    apply: 'build',
    writeBundle() {
      const file = join(outDir, 'umbraco-package.json');
      const manifest = JSON.parse(readFileSync(file, 'utf8'));
      manifest.version = version;
      writeFileSync(file, `${JSON.stringify(manifest, null, 2)}\n`);
    },
  };
}

/**
 * The plugins every package build shares: the banner on CSS files and the chunk report.
 * @param {{ version: string, name: string, buildDir?: string, base?: string }} options
 * @returns {import('vite').Plugin[]}
 */
export function sharedPlugins({ version, name, ...report }) {
  return [cssLicenceBanner(licenceBanner(version)), chunkReport(name, report)];
}

/** What the terser pass does: an ES module of the build's target, compressed in three passes, legal comments kept. */
export const TERSER_OPTIONS = Object.freeze({
  module: true,
  ecma: 2022,
  compress: { passes: 3 },
  mangle: true,
  format: { comments: /^!/ },
});

/**
 * A pass of terser over each chunk before the bundler's own minifier, for the website runtime, whose size section 4.12
 * budgets: the two together write a few percent less than either alone (ADR-0119). It runs while the chunk is
 * rendered, so the chunk's content hash covers what it writes. A debugging build with source maps
 * (`JFB_SOURCEMAP=1`) goes without it, as terser would cut the maps' thread.
 * @param {Record<string, string | undefined>} [env]
 * @returns {import('vite').Plugin}
 */
export function terserPass(env = process.env) {
  return {
    name: 'jfb-terser-pass',
    apply: () => env.JFB_SOURCEMAP !== '1',
    async renderChunk(code) {
      const out = await minify(code, TERSER_OPTIONS);
      if (out.code === undefined) throw new Error('terser wrote nothing');
      return { code: out.code, map: null };
    },
  };
}

/**
 * The build options every package shares. Source maps embed the original TypeScript, so builds have none unless
 * JFB_SOURCEMAP=1 asks for a local debugging build, which `dotnet pack` then refuses (scripts/stamp.mjs).
 * @param {{ version: string, outDir: string, external?: RegExp[], env?: Record<string, string | undefined> }} options
 */
export function sharedBuild({ version, outDir, external = [], env = process.env }) {
  return {
    outDir,
    emptyOutDir: true,
    target: 'es2022',
    sourcemap: env.JFB_SOURCEMAP === '1',
    rolldownOptions: {
      external,
      output: {
        // The entry keeps a stable name that the page links with a ?v= fingerprint; chunks carry a content hash,
        // because that fingerprint does not reach import() (ADR-0004).
        chunkFileNames: 'chunks/[name]-[hash].js',
        assetFileNames: 'assets/[name]-[hash][extname]',
        // Browsers load these files as they are, so they are minified in full, which Vite's library mode leaves out
        // for ES output (it keeps the whitespace and comments for other bundlers). Legal comments stay.
        minify: true,
        comments: { legal: true, annotation: false, jsdoc: false },
        // Added after minification, so no minifier can drop it.
        postBanner: licenceBanner(version),
      },
    },
  };
}
