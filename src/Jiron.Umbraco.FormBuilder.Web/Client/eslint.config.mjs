import js from '@eslint/js';
import { defineConfig } from 'eslint/config';
import prettier from 'eslint-config-prettier';
import globals from 'globals';
import tseslint from 'typescript-eslint';
import formMembers from './scripts/form-members.mjs';
import jfb from './scripts/no-hard-coded-text.mjs';

const noHtmlStrings =
  'Build the DOM with createElement and textContent: website code sets no HTML from strings (ROADMAP section 4.10). The reviewed exceptions parse in an inert <template>: packages/runtime/src/html.ts for the rich texts that recall answers, whose nodes never enter the page (ADR-0115), the message of an outcome (ADR-0120), and the form after form_changed (ADR-0004); and the rich texts the server sanitised that the headless element writes as the Razor views do (ADR-0128).';
const noUnsafeDirectives =
  'No unsafe directives in the backoffice: Lit templates escape everything (ROADMAP section 4.10).';

export default defineConfig(
  {
    ignores: [
      '**/node_modules/**',
      '.build/**',
      'packages/backoffice/src/api/**',
      'packages/backoffice/src/.api-check-*/**',
      'packages/ai/src/api/**',
      'packages/ai/src/.api-check-*/**',
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
        { property: 'createContextualFragment', message: noHtmlStrings },
        { property: 'setHTMLUnsafe', message: noHtmlStrings },
        { property: 'parseHTMLUnsafe', message: noHtmlStrings },
      ],
      'no-restricted-syntax': [
        'error',
        {
          selector: "CallExpression[callee.object.name='document'][callee.property.name=/^write(ln)?$/]",
          message: noHtmlStrings,
        },
        { selector: "NewExpression[callee.name='DOMParser']", message: noHtmlStrings },
      ],
    },
  },
  {
    // A form answers a read with its control of that name, and a control is named by its field's alias: the website
    // code reads a form's members through proto(form) (@jfb/logic/proto). The rule reads the types, so these files
    // are linted with their project.
    files: ['packages/runtime/src/**/*.ts', 'packages/element/src/**/*.ts'],
    ignores: ['**/*.test.ts', '**/testing*.ts'],
    languageOptions: { parserOptions: { projectService: true, tsconfigRootDir: import.meta.dirname } },
    plugins: { jfb: formMembers },
    rules: { 'jfb/form-members': 'error' },
  },
  {
    // The AI add-on's backoffice too (16.1's review): its words come from a model, which Lit's templates escape as any text.
    files: ['packages/backoffice/src/**/*.ts', 'packages/ai/src/**/*.ts'],
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
  {
    // Every word the backoffice shows comes from its dictionaries in English and German (ROADMAP 14.1, ADR-0057).
    files: ['packages/backoffice/src/**/*.ts', 'packages/ai/src/**/*.ts'],
    ignores: [
      '**/*.test.ts',
      'packages/backoffice/src/lang/**',
      'packages/backoffice/src/testing.ts',
      'packages/ai/src/lang/**',
    ],
    plugins: { jfb },
    rules: { 'jfb/no-hard-coded-text': 'error' },
  },
  prettier,
);
