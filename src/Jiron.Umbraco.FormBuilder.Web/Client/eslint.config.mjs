import js from '@eslint/js';
import { defineConfig } from 'eslint/config';
import prettier from 'eslint-config-prettier';
import globals from 'globals';
import tseslint from 'typescript-eslint';

const noHtmlStrings =
  'Build the DOM with createElement and textContent: website code sets no HTML from strings (ROADMAP section 4.10). The reviewed exceptions parse in an inert <template>: the rich texts that recall answers, whose nodes never enter the page (ADR-0115), the rich texts the server sanitised that the headless element writes as the Razor views do (ADR-0128), and form_changed (ADR-0004), which comes with 6.4j.';
const noUnsafeDirectives =
  'No unsafe directives in the backoffice: Lit templates escape everything (ROADMAP section 4.10).';

export default defineConfig(
  {
    ignores: [
      '**/node_modules/**',
      '.build/**',
      'packages/backoffice/src/api/**',
      'packages/backoffice/src/.api-check-*/**',
      // Made by scripts/generate-public-types.mjs and held to its schema there (ADR-0128).
      'packages/element/src/answer.d.ts',
    ],
  },
  js.configs.recommended,
  tseslint.configs.strict,
  tseslint.configs.stylistic,
  {
    languageOptions: { globals: { ...globals.browser } },
  },
  {
    files: ['scripts/**/*.mjs', 'packages/*/scripts/**/*.mjs', '**/*.config.ts', 'eslint.config.mjs'],
    languageOptions: { globals: { ...globals.node } },
  },
  {
    // The shared logic has no DOM and no dependencies (ROADMAP section 4.2): only relative imports, and no browser globals.
    files: ['packages/logic/src/**/*.ts'],
    ignores: ['**/*.test.ts'],
    languageOptions: { globals: {} },
    rules: {
      'no-restricted-imports': [
        'error',
        {
          patterns: [{ regex: '^[^.]', message: 'The shared logic imports nothing but itself (ROADMAP section 4.2).' }],
        },
      ],
    },
  },
  {
    // The website code ships no runtime dependencies (ROADMAP 3.2) and sets no HTML from strings (ROADMAP section 4.10).
    files: ['packages/runtime/src/**/*.ts', 'packages/element/src/**/*.ts'],
    ignores: ['**/*.test.ts'],
    rules: {
      'no-restricted-imports': [
        'error',
        {
          patterns: [
            {
              regex: '^(?![.]|@jfb/logic(?:$|/))',
              message: 'Website code has no runtime dependencies: import only its own files and @jfb/logic.',
            },
          ],
        },
      ],
      'no-restricted-properties': [
        'error',
        { property: 'innerHTML', message: noHtmlStrings },
        { property: 'outerHTML', message: noHtmlStrings },
        { property: 'insertAdjacentHTML', message: noHtmlStrings },
      ],
      'no-restricted-syntax': [
        'error',
        {
          selector: "CallExpression[callee.object.name='document'][callee.property.name=/^write(ln)?$/]",
          message: noHtmlStrings,
        },
      ],
    },
  },
  {
    files: ['packages/backoffice/src/**/*.ts'],
    rules: {
      'no-restricted-imports': [
        'error',
        {
          paths: [
            {
              name: '@umbraco-cms/backoffice/external/lit',
              importNames: ['unsafeHTML', 'unsafeSVG'],
              message: noUnsafeDirectives,
            },
          ],
          patterns: [
            {
              // A regex, not a group: gitignore patterns would match the lit at the end of the backoffice's own path.
              regex: '^lit(-html)?(/|$)',
              message: 'Import Lit from @umbraco-cms/backoffice/external/lit, which the backoffice provides.',
            },
          ],
        },
      ],
    },
  },
  prettier,
);
