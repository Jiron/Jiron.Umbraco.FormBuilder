// The backoffice's dictionaries (ADR-0057): English and German hold the same keys, each a text that is not blank (or
// a function of the values that gives one); they hold every `#` key the management API serves for the built-in field
// types, action types, option sources and captcha providers, as docs/api/field-types.json, action-types.json,
// option-sources.json and captcha-providers.json (the snapshots integration tests hold the endpoints to) have them
// (ADR-0172, ADR-0184); they hold
// every `#jfbPicker_` key the form picker's checks send (ADR-0129), and its area no other; the form picker's screens
// (ADR-0134, ADR-0135) hold the keys their elements and manifests use, and no other; the field types' areas hold no
// key nothing uses; and the form workspace's own areas load with it, not at the start, and nothing outside it asks for
// them (ADR-0201). The start's words and the workspace's are checked together.
import { existsSync, readdirSync, readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';
import { CANVAS_WIDTHS } from '../designer/canvas/widths';
import { OWN_WORDS, SERVER_WORDS } from '../designer/check/check-words';
import { SIDEBAR } from '../section/sidebar';
import actionsDe from './actions.de';
import actionsEn from './actions.en';
import startDe from './de';
import startEn from './en';
import workspaceDe from './workspace.de';
import workspaceEn from './workspace.en';

type Dictionary = Record<string, Record<string, unknown>>;

const en: Dictionary = { ...startEn, ...workspaceEn };
const de: Dictionary = { ...startDe, ...workspaceDe };

/** The repository root: the nearest folder above this file that holds the solution. */
function repositoryRoot(): string {
  let folder = dirname(fileURLToPath(import.meta.url));
  while (!existsSync(join(folder, 'Jiron.Umbraco.FormBuilder.slnx'))) {
    const parent = dirname(folder);
    if (parent === folder) throw new Error('No folder above the dictionaries holds the solution');
    folder = parent;
  }
  return folder;
}

/** Every key of a dictionary as a term names it, `<area>_<key>`, sorted. */
const termsOf = (dictionary: Dictionary): string[] =>
  Object.entries(dictionary)
    .flatMap(([area, texts]) => Object.keys(texts).map((key) => `${area}_${key}`))
    .sort();

/** Every string of a JSON value that is a `#` key, without its `#`. */
function keysIn(value: unknown, found = new Set<string>()): Set<string> {
  if (typeof value === 'string' && value.startsWith('#')) found.add(value.slice(1));
  else if (Array.isArray(value)) value.forEach((item) => keysIn(item, found));
  else if (value !== null && typeof value === 'object') Object.values(value).forEach((item) => keysIn(item, found));
  return found;
}

const served = keysIn(JSON.parse(readFileSync(join(repositoryRoot(), 'docs', 'api', 'field-types.json'), 'utf8')));
keysIn(JSON.parse(readFileSync(join(repositoryRoot(), 'docs', 'api', 'action-types.json'), 'utf8')), served);
keysIn(JSON.parse(readFileSync(join(repositoryRoot(), 'docs', 'api', 'captcha-providers.json'), 'utf8')), served);
const optionKeys = keysIn(
  JSON.parse(readFileSync(join(repositoryRoot(), 'docs', 'api', 'option-sources.json'), 'utf8')),
);
optionKeys.forEach((key) => served.add(key));

/** The `#jfbPicker_` keys the form picker's checks send, read from their source. */
const pickerKeys = new Set(
  [
    ...readFileSync(
      join(repositoryRoot(), 'src', 'Jiron.Umbraco.FormBuilder.Web', 'Picker', 'FormPickerEditors.cs'),
      'utf8',
    ).matchAll(/"#(jfbPicker_[A-Za-z]+)"/g),
  ].map((match) => match[1]),
);

/**
 * The `jfbFormPicker_` keys the form picker's elements use, read from their sources: as a term (`'key'`), and in the
 * manifests as a label (`'#key'`) or in a description's markdown (`'{#key}'`).
 */
const pickerFolder = join(dirname(fileURLToPath(import.meta.url)), '..', 'picker');
const screenKeys = new Set(
  readdirSync(pickerFolder)
    .filter((file) => file.endsWith('.ts') && !file.endsWith('.test.ts'))
    .flatMap((file) => [
      ...readFileSync(join(pickerFolder, file), 'utf8').matchAll(/'(?:\{?#)?(jfbFormPicker_[A-Za-z]+)\}?'/g),
    ])
    .map((match) => match[1]),
);

/** A dictionary's text as a person reads it: a function is given sample values (a name, a number). */
const shown = (text: unknown): unknown => (typeof text === 'function' ? text('Contact us', 2) : text);

/** The areas of the field types' texts. */
const fieldTypeAreas = [
  'jfbFieldTypes',
  'jfbFieldPalette',
  'jfbFieldSettingGroups',
  'jfbFieldSettings',
  'jfbFieldChoices',
  'jfbFieldTypeAvailability',
  'jfbOptionListSettings',
  'jfbOptionListChoices',
  'jfbOptionSources',
];

/** Keys the server sends that the built-in types' snapshot cannot show: FieldTypeAvailabilityService.CheckFailed. */
const servedOtherwise = ['jfbFieldTypeAvailability_checkFailed'];

describe('the dictionaries', () => {
  it('hold the same keys in English and German, each a text that is not blank', () => {
    expect(termsOf(de)).toEqual(termsOf(en));
    for (const dictionary of [en, de] as Dictionary[]) {
      for (const [area, texts] of Object.entries(dictionary)) {
        for (const [key, text] of Object.entries(texts)) {
          const value = shown(text);
          expect(typeof value === 'string' && value.trim().length > 0, `${area}_${key}`).toBe(true);
        }
      }
    }
  });

  it('hold every key the built-in field types are served with', () => {
    expect(served.size).toBeGreaterThan(150);
    expect([...served].filter((key) => !termsOf(en).includes(key))).toEqual([]);
  });

  it('hold every key the option sources are served with, in English and German alike (ADR-0172)', () => {
    expect(optionKeys.size).toBeGreaterThanOrEqual(30);
    for (const dictionary of [en, de])
      expect([...optionKeys].filter((key) => !termsOf(dictionary).includes(key))).toEqual([]);
  });

  it('name every built-in merge tag path and source the tag picker is served, in English and German alike (ADR-0173)', () => {
    const served = JSON.parse(
      readFileSync(join(repositoryRoot(), 'docs', 'api', 'merge-tag-paths.json'), 'utf8'),
    ) as Record<string, { path: string; source: string; isSiteSource: boolean }[]>;
    const builtIn = Object.values(served)
      .flat()
      .filter((each) => !each.isSiteSource);
    const wanted = new Set([
      ...builtIn.map((each) => `jfbMergeTags_source_${each.source}`),
      ...builtIn
        .filter((each) => each.source !== 'field')
        .map((each) => `jfbMergeTags_path_${each.path.replaceAll('.', '_')}`),
    ]);
    expect(wanted.size).toBeGreaterThanOrEqual(10);
    for (const dictionary of [en, de])
      expect([...wanted].filter((key) => !termsOf(dictionary).includes(key))).toEqual([]);
  });

  it("hold every key the form picker's checks send, and no other in its area", () => {
    expect(pickerKeys.size).toBeGreaterThanOrEqual(5);
    expect(termsOf(en).filter((term) => term.startsWith('jfbPicker_'))).toEqual([...pickerKeys].sort());
  });

  it("hold the section's and the Overview's keys that their code uses, and no other in those areas", () => {
    // Forms at a glance is the Overview's too, with its own words beside (insights, ADR-0211); the default theme's name
    // is the section's, as every screen that names a theme says it (themes/theme-label.ts).
    const folders = ['section', 'overview', 'tree', 'insights', 'themes', 'members'].map((folder) =>
      join(dirname(fileURLToPath(import.meta.url)), '..', folder),
    );
    const sources = folders.flatMap((folder) =>
      readdirSync(folder)
        .filter((file) => file.endsWith('.ts') && !file.endsWith('.test.ts'))
        .map((file) => readFileSync(join(folder, file), 'utf8')),
    );
    const used = new Set(
      sources.flatMap((source) =>
        [...source.matchAll(/(?:'|#)(jfb(?:Section|Overview)_[A-Za-z_]+)'/g)].map((match) => match[1]),
      ),
    );
    // The keys code builds: an item's and a group's label, and a step's texts and a finding's link.
    for (const group of SIDEBAR) {
      used.add(`jfbSection_${group.id}`);
      for (const item of group.items) used.add(`jfbSection_${item.id}`);
    }

    for (const step of ['email', 'form', 'testEntry', 'formBlock', 'access']) {
      for (const part of ['step', 'help', 'action']) used.add(`jfbOverview_${part}_${step}`);
    }

    for (const link of [
      'settingsEmail',
      'settingsGeneral',
      'deliveries',
      'privacyDataCollection',
      'privacyRetention',
      'settingsProtection',
    ])
      used.add(`jfbOverview_open_${link}`);
    expect(termsOf(en).filter((term) => term.startsWith('jfbSection_') || term.startsWith('jfbOverview_'))).toEqual(
      [...used].sort(),
    );
  });

  it("hold the library's keys that its code uses, and no other in that area", () => {
    const folders = [
      'access',
      'library',
      'library/actions',
      'library/modals',
      'tree',
      'ui',
      'overview',
      'section',
      'templates',
    ].map((folder) => join(dirname(fileURLToPath(import.meta.url)), '..', folder));
    const sources = folders.flatMap((folder) =>
      readdirSync(folder)
        .filter((file) => file.endsWith('.ts') && !file.endsWith('.test.ts'))
        .map((file) => readFileSync(join(folder, file), 'utf8')),
    );
    const used = new Set(
      sources.flatMap((source) => [...source.matchAll(/(?:'|#)(jfbLibrary_[A-Za-z_]+)'/g)].map((match) => match[1])),
    );
    // The keys code builds: each "..." action's and create option's label, a refusal's permission, a built-in template's texts.
    for (const id of [
      'Reload',
      'RenameFolder',
      'Move',
      'DeleteFolder',
      'Duplicate',
      'CopyActions',
      'SaveAsTemplate',
      'Export',
      'Replace',
    ]) {
      used.add(`jfbLibrary_action${id}`);
    }
    for (const id of ['Trash', 'EmptyBin', 'Restore', 'DeleteForGood']) used.add(`jfbLibrary_action${id}`);
    for (const part of ['builtIn', 'forms', 'groups']) used.add(`jfbLibrary_templatesPart_${part}`);
    for (const id of ['Form', 'FromTemplate', 'Import', 'Folder']) {
      used.add(`jfbLibrary_option${id}`);
      used.add(`jfbLibrary_option${id}Description`);
    }
    for (const permission of [
      'design',
      'manageActions',
      'viewEntries',
      'viewSensitive',
      'editEntries',
      'deleteEntries',
      'export',
      'manageLibrary',
      'manageSettings',
      'managePermissions',
      'privacyTools',
    ]) {
      used.add(`jfbLibrary_permission_${permission}`);
    }
    // Every built-in template, by the alias its file in Core has (ADR-0217).
    const templates = readdirSync(join(repositoryRoot(), 'src', 'Jiron.Umbraco.FormBuilder.Core', 'Forms', 'Templates'))
      .filter((file) => file.endsWith('.json'))
      .map((file) => file.slice(0, -'.json'.length));
    expect(templates).toContain('privacyRequest');
    for (const template of templates) {
      used.add(`jfbLibrary_template_${template}`);
      used.add(`jfbLibrary_template_${template}Description`);
    }
    expect(termsOf(en).filter((term) => term.startsWith('jfbLibrary_'))).toEqual([...used].sort());
  });

  it("hold the form workspace's keys that its code uses, and no other in that area", () => {
    const folder = join(dirname(fileURLToPath(import.meta.url)), '..', 'designer');
    const sources = readdirSync(folder, { recursive: true, encoding: 'utf8' })
      .filter((file) => file.endsWith('.ts') && !file.endsWith('.test.ts'))
      .map((file) => readFileSync(join(folder, file), 'utf8'));
    const used = new Set(
      sources.flatMap((source) => [...source.matchAll(/(?:'|#)(jfbDesigner_[A-Za-z_]+)'/g)].map((match) => match[1])),
    );
    // The keys code builds: each view's label, each state's chip, each canvas width, what a delete's and a move's toast
    // says of each kind of element, and why the editor model refused an edit.
    for (const view of ['design', 'settings', 'actions', 'entries', 'insights', 'history']) {
      used.add(`jfbDesigner_view_${view}`);
    }
    for (const state of ['draft', 'published', 'pending', 'unpublished']) used.add(`jfbDesigner_state_${state}`);
    for (const width of Object.keys(CANVAS_WIDTHS)) used.add(`jfbDesigner_width_${width}`);
    for (const kind of ['page', 'group', 'field']) {
      used.add(`jfbDesigner_deleted_${kind}`);
      used.add(`jfbDesigner_moved_${kind}`);
    }
    const refusals =
      readFileSync(join(folder, 'editor-model.ts'), 'utf8').match(/export type Refusal =([^;]+);/)?.[1] ?? '';
    for (const [, refusal] of refusals.matchAll(/'([a-zA-Z]+)'/g))
      if (refusal !== 'unchanged') used.add(`jfbDesigner_refusal_${refusal}`);
    expect(termsOf(en).filter((term) => term.startsWith('jfbDesigner_'))).toEqual([...used].sort());
  });

  it("hold a sentence for every code the server's form check sends, the form check's other keys, and no other in that area (ADR-0181)", () => {
    const core = join(repositoryRoot(), 'src', 'Jiron.Umbraco.FormBuilder.Core');
    const sources = [
      ['Definitions', 'DefinitionProblem.cs'],
      ['MergeTags', 'MergeTagValues.cs'],
      ['Logic', 'LogicException.cs'],
      ['Actions', 'ActionCheckCodes.cs'],
      ...[
        'AnonymousFormCheck',
        'CollectedDataFormChecks',
        'FieldFormChecks',
        'FormCheck',
        'LogicFormChecks',
        'MergeTagFormCheck',
        'SiteFormChecks',
      ].map((file) => ['Forms', `${file}.cs`]),
    ];
    const codes = new Set(
      sources.flatMap((parts) =>
        [...readFileSync(join(core, ...parts), 'utf8').matchAll(/public const string \w+ = "([a-z_]+)";/g)].map(
          (match) => match[1] ?? '',
        ),
      ),
    );
    // The codes only reading a stored draft gives, and the logic engine's while it runs a form for its answers.
    const neverChecked = [
      'invalid_json',
      'unknown_property',
      'missing_property',
      'duplicate_property',
      'invalid_type',
      'unsupported_schema_version',
      'unsupported_format',
      'invalid_rule',
      'invalid_form',
      'invalid_context',
      'unknown_time_zone',
      'not_a_number',
      'too_many_digits',
      'division_by_zero',
      'tag_in_escape',
    ];
    expect(codes.size).toBeGreaterThan(60);
    expect(
      [...codes].filter((code) => !OWN_WORDS.has(code) && !SERVER_WORDS.has(code) && !neverChecked.includes(code)),
    ).toEqual([]);
    expect([...OWN_WORDS].filter((code) => SERVER_WORDS.has(code) || !codes.has(code))).toEqual([]);

    const folder = join(dirname(fileURLToPath(import.meta.url)), '..', 'designer');
    const used = new Set(
      readdirSync(folder, { recursive: true, encoding: 'utf8' })
        .filter((file) => file.endsWith('.ts') && !file.endsWith('.test.ts'))
        .flatMap((file) => [...readFileSync(join(folder, file), 'utf8').matchAll(/(?:'|#)(jfbCheck_[A-Za-z_]+)'/g)])
        .map((match) => match[1]),
    );
    // The keys code builds: each own code's sentence.
    for (const code of OWN_WORDS) used.add(`jfbCheck_code_${code}`);
    expect(termsOf(en).filter((term) => term.startsWith('jfbCheck_'))).toEqual([...used].sort());
  });

  it("hold the preview's keys that its code uses, and no other in that area (ADR-0203)", () => {
    const folder = join(dirname(fileURLToPath(import.meta.url)), '..', 'designer', 'visitor-preview');
    const used = new Set(
      readdirSync(folder, { encoding: 'utf8' })
        .filter((file) => file.endsWith('.ts') && !file.endsWith('.test.ts'))
        .flatMap((file) => [...readFileSync(join(folder, file), 'utf8').matchAll(/#term\('([A-Za-z_]+)'/g)])
        .map((match) => `jfbPreview_${match[1]}`),
    );
    expect(used.size).toBeGreaterThan(5);
    expect(termsOf(en).filter((term) => term.startsWith('jfbPreview_'))).toEqual([...used].sort());
  });

  it("hold the form picker screens' keys that their elements use, and no other in that area", () => {
    expect(screenKeys.size).toBeGreaterThanOrEqual(20);
    expect(termsOf(en).filter((term) => term.startsWith('jfbFormPicker_'))).toEqual([...screenKeys].sort());
  });

  it('hold no key of the field types that nothing serves', () => {
    const unused = termsOf(en).filter(
      (term) =>
        fieldTypeAreas.includes(term.slice(0, term.indexOf('_'))) &&
        !served.has(term) &&
        !servedOtherwise.includes(term),
    );
    expect(unused).toEqual([]);
    expect(termsOf(en)).toEqual(expect.arrayContaining(servedOtherwise));
  });

  it("hold the form workspace's own areas apart from the start, asked for only inside the workspace (ADR-0201)", () => {
    const areas = Object.keys(workspaceEn).sort();
    expect(areas).toEqual(['jfbCheck', 'jfbInspector', 'jfbPreview', 'jfbRules', 'jfbSettings']);
    expect(Object.keys(workspaceDe).sort()).toEqual(areas);
    expect([...Object.keys(startEn), ...Object.keys(startDe)].filter((area) => areas.includes(area))).toEqual([]);
    const source = join(dirname(fileURLToPath(import.meta.url)), '..');
    const asking = new RegExp(`(${areas.join('|')})_`);
    const outside = readdirSync(source, { recursive: true, encoding: 'utf8' })
      .filter((file) => file.endsWith('.ts') && !file.endsWith('.test.ts'))
      .filter((file) => !/^(designer|lang)[\\/]/.test(file))
      .filter((file) => asking.test(readFileSync(join(source, file), 'utf8')));
    expect(outside).toEqual([]);
  });

  it('name a button with a name that starts with the words it shows, in both languages (WCAG 2.5.3)', () => {
    // A voice user says what the button shows; its name for a screen reader says more, after those words.
    const shownWords = (text: string) => text.replace(/\u2026$/, '');
    for (const [preview, actions] of [
      [workspaceEn.jfbPreview, actionsEn.jfbActions],
      [workspaceDe.jfbPreview, actionsDe.jfbActions],
    ] as const) {
      expect(preview.runActionsFor(12)).toMatch(new RegExp(`^${shownWords(preview.runActions)}`));
      expect(actions.runDetailsOf('Email the team')).toMatch(new RegExp(`^${actions.runDetails}`));
    }
  });

  it('give each culture a different text where the words differ', () => {
    // A copied English text would pass the key checks; most labels differ between the languages.
    const same = termsOf(en).filter((term) => {
      const [area, key] = [term.slice(0, term.indexOf('_')), term.slice(term.indexOf('_') + 1)];
      return shown((en as Dictionary)[area]?.[key]) === shown((de as Dictionary)[area]?.[key]);
    });
    expect(same.length).toBeLessThan(termsOf(en).length / 10);
  });
});
