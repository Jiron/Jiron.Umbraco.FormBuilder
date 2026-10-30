import { defineConfig } from 'vite';
import { buildVersion } from '../../scripts/version.mjs';
import { sharedPlugins, websiteAssets } from '../../scripts/vite-shared.mjs';

const version = buildVersion();

// One stylesheet per entry, each under its own stable name that the page links with a ?v= fingerprint (ADR-0015,
// ADR-0105), minified for the browsers the theme is written for, so its logical properties stay as written.
export default defineConfig({
  publicDir: false,
  build: {
    outDir: `${websiteAssets}/themes`,
    emptyOutDir: true,
    cssCodeSplit: true,
    cssMinify: 'lightningcss',
    cssTarget: ['chrome123', 'edge123', 'firefox129', 'safari17.5'],
    rolldownOptions: {
      input: { base: 'src/base.css', default: 'src/default.css' },
      output: { assetFileNames: '[name][extname]' },
    },
  },
  plugins: sharedPlugins({ version, name: 'themes' }),
});
