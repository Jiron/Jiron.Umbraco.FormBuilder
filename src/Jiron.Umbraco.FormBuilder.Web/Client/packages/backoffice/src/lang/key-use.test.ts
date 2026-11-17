// Every key is used (ROADMAP 14.1, carried in from 9.6b's wiring sweep): each area of the backoffice's dictionaries
// holds exactly the keys its code uses, read from the sources by `key-use.ts`, the keys the server serves read from the
// snapshots its integration tests hold it to, and each family the code builds a key of from the list that names it;
// and every area is held so by one check, here or in the check of its screens.
import { existsSync, readdirSync, readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';
import { CONTEXT_TERMS } from '../designer/rules/rule-words';
import { SPANS } from '../privacy/retention-screen.element';
import { LEADS } from '../settings/settings-screen.element';
import { inFolders, sources, usedKeys } from './key-use';

const here = dirname(fileURLToPath(import.meta.url));

/** The repository root: the nearest folder above this file that holds the solution. */
function repositoryRoot(): string {
  let folder = here;
  while (!existsSync(join(folder, 'Jiron.Umbraco.FormBuilder.slnx'))) {
    const parent = dirname(folder);
    if (parent === folder) throw new Error('No folder above the dictionaries holds the solution');
    folder = parent;
  }
  return folder;
}

const root = repositoryRoot();
const read = (...parts: string[]) => readFileSync(join(root, ...parts), 'utf8');
const core = (...parts: string[]) => read('src', 'Jiron.Umbraco.FormBuilder.Core', ...parts);

/** Every dictionary of the backoffice, in English: each file's areas, as Umbraco loads them. */
interface Dictionary {
  readonly default: Record<string, Record<string, unknown>>;
}
const modules: Record<string, Dictionary> = {};
for (const file of readdirSync(here).filter((name) => name.endsWith('.en.ts')))
  modules[`./${file}`] = (await import(`./${file}`)) as Dictionary;

/** Each area's keys, from every file but the two that merge others. */
const areas = new Map<string, Set<string>>();
for (const [file, module] of Object.entries(modules)) {
  if (/\/(en|workspace\.en)\.ts$/.test(file)) continue;
  for (const [area, texts] of Object.entries(module.default)) {
    const keys = areas.get(area) ?? new Set<string>();
    for (const key of Object.keys(texts)) keys.add(key);
    areas.set(area, keys);
  }
}

const keysOf = (area: string): Set<string> => areas.get(area) ?? new Set<string>();

/** Every string of a JSON value that is a `#` key of this area, without its area. */
function servedIn(value: unknown, area: string, found = new Set<string>()): Set<string> {
  if (typeof value === 'string' && value.startsWith(`#${area}_`)) found.add(value.slice(area.length + 2));
  else if (Array.isArray(value)) value.forEach((item) => servedIn(item, area, found));
  else if (value !== null && typeof value === 'object')
    Object.values(value).forEach((item) => servedIn(item, area, found));
  return found;
}

const snapshot = (name: string): unknown => JSON.parse(read('docs', 'api', name));

/** An enum's members in a C# file, as the server writes them (camelCase). */
function enumMembers(text: string, name: string): string[] {
  const body = new RegExp(`enum ${name}\\s*\\{([^}]*)\\}`).exec(text)?.[1] ?? '';
  return [...body.matchAll(/^\s*([A-Z][A-Za-z0-9]*),?\s*$/gm)].map(
    (match) => (match[1] ?? '').charAt(0).toLowerCase() + (match[1] ?? '').slice(1),
  );
}

/** The values of a C# file's string constants. */
const constantsIn = (text: string): string[] =>
  [...text.matchAll(/public const string \w+ = "([A-Za-z_]+)";/g)].map((match) => match[1] ?? '');

/** Each part's key of a family, `family_part`, the way `usedKeys` takes them. */
const family = (parts: Iterable<string>) => [...parts];

/** What a check of an area expects: the keys its code uses equal its keys, so neither lacks one. */
function expectHeld(area: string, used: Set<string>) {
  const keys = keysOf(area);
  expect(keys.size, `${area} has keys`).toBeGreaterThan(0);
  expect(
    [...keys].filter((key) => !used.has(key)),
    `the keys of ${area} no code uses`,
  ).toEqual([]);
}

describe('the key-use reading', () => {
  it("takes a key named whole, a prefix and its rest in one file, an owner's literal and a family's part, and no other", () => {
    const area = 'jfbInspector';
    const keys = keysOf(area);
    // A key named whole somewhere, and one the inspector builds of its prefix: both are its.
    expect(usedKeys(area, keys).has('editingLanguage')).toBe(true);
    // A key no source holds stays unused, whatever the owners.
    expect(usedKeys(area, [...keys, 'zzNoCodeUsesThis'], {}, inFolders('designer')).has('zzNoCodeUsesThis')).toBe(
      false,
    );
    // A family's part makes its key used, and only for a key the area holds.
    expect(usedKeys(area, ['probe_one'], { probe: family(['one', 'two']) }).has('probe_one')).toBe(true);
    expect(usedKeys(area, ['probe_one'], { probe: family(['one', 'two']) }).has('probe_two')).toBe(false);
  });

  it('reads every source of the backoffice but the tests, the dictionaries and the generated client', () => {
    const files = sources().map((source) => source.file);
    expect(files).toContain('designer/form-design.view.ts');
    expect(files.some((file) => file.endsWith('.test.ts') || file.startsWith('lang/') || file.startsWith('api/'))).toBe(
      false,
    );
  });
});

describe('every key is used', () => {
  it("in the inspector's area: by its code, its parts' lists among it, and the settings' problems", () => {
    expectHeld('jfbInspector', usedKeys('jfbInspector', keysOf('jfbInspector'), {}, inFolders('designer')));
  });

  it("in the rules' area: by its code, the operators', effects' and elements' lists, and each part of the visit said in a sentence", () => {
    const inSentence = [...Object.values(CONTEXT_TERMS), 'jfbRules_subjectQuery'].map(
      (term) => `${term.slice('jfbRules_'.length)}InSentence`,
    );
    const used = usedKeys('jfbRules', keysOf('jfbRules'), {}, inFolders('designer'));
    for (const key of inSentence) used.add(key);
    expect(inSentence.filter((key) => !keysOf('jfbRules').has(key))).toEqual([]);
    expectHeld('jfbRules', used);
  });

  it("in the merge tags' area: by the tag picker, each source the server has, and each built-in path it serves", () => {
    const served = snapshot('merge-tag-paths.json') as Record<
      string,
      { path: string; source: string; isSiteSource: boolean }[]
    >;
    const builtIn = Object.values(served)
      .flat()
      .filter((each) => !each.isSiteSource);
    const used = usedKeys('jfbMergeTags', keysOf('jfbMergeTags'), {}, inFolders('designer/inspector'));
    for (const each of builtIn) if (each.source !== 'field') used.add(`path_${each.path.replaceAll('.', '_')}`);
    // Each source's name, as its group in the picker says it, from the sources the server has.
    const folder = join(root, 'src', 'Jiron.Umbraco.FormBuilder.Core', 'MergeTags');
    for (const file of readdirSync(folder).filter((name) => name.endsWith('.cs')))
      for (const match of readFileSync(join(folder, file), 'utf8').matchAll(/public string Name => "([a-z]+)";/g))
        used.add(`source_${match[1] ?? ''}`);
    expectHeld('jfbMergeTags', used);
  });

  it("in the Settings view's area: by its code, the captcha providers' settings the server serves and the visitor texts' groups", () => {
    const used = usedKeys('jfbSettings', keysOf('jfbSettings'), {}, inFolders('designer'));
    for (const key of servedIn(snapshot('captcha-providers.json'), 'jfbSettings')) used.add(key);
    // The visitor texts' groups, as Core lists them (ADR-0230): `textsGroup_${group.alias}`.
    const groups =
      /Groups \{ get; \} = \[([^\]]+)\]/.exec(
        read('src', 'Jiron.Umbraco.FormBuilder.Core', 'Logic', 'VisitorTextKeys.cs'),
      )?.[1] ?? '';
    const aliases = [...groups.matchAll(/"([a-z]+)"/g)].map((match) => match[1] ?? '');
    expect(aliases).toContain('errors');
    for (const alias of aliases) used.add(`textsGroup_${alias}`);
    expectHeld('jfbSettings', used);
  });

  it("in the outcomes' area: by their code, on the Design view and in the History view", () => {
    expectHeld('jfbOutcomes', usedKeys('jfbOutcomes', keysOf('jfbOutcomes'), {}, inFolders('designer/outcomes')));
  });

  it("in the History view's area: by its code and its changes' kinds", () => {
    expectHeld('jfbHistory', usedKeys('jfbHistory', keysOf('jfbHistory'), {}, inFolders('designer')));
  });

  it("in the Actions view's area: by its code, the deliveries that say a run, and each row's words of the webhook's tables", () => {
    const used = usedKeys('jfbActions', keysOf('jfbActions'), {}, inFolders('designer', 'activity'));
    // The webhook's tables say a row of each kind in that kind's words: `pairName`, `header_name_empty`.
    const editors = sources().find((source) => source.file === 'designer/actions/webhook-editors.ts')?.text ?? '';
    const kinds = [...editors.matchAll(/words: '([a-z]+)'/g)].map((match) => match[1] ?? '');
    const said = [...editors.matchAll(/(?:'|`)([A-Za-z_]+)(?:'|`)/g)].map((match) => match[1] ?? '');
    expect(kinds).toEqual(['pair', 'header']);
    for (const kind of kinds)
      for (const each of said) {
        used.add(`${kind}${each}`);
        used.add(`${kind}_${each}`);
      }
    // And each problem of a row, by its part and kind (`header_name_empty`), as the webhook's model names them.
    const model = sources().find((source) => source.file === 'designer/actions/webhook-model.ts')?.text ?? '';
    for (const [, at, union] of model.matchAll(/at: '([a-z]+)'; readonly kind: ([^}]+)}/g))
      for (const [, problem] of (union ?? '').matchAll(/'([a-z]+)'/g))
        for (const kind of kinds) used.add(`${kind}_${at}_${problem}`);
    expectHeld('jfbActions', used);
  });

  it("in the global settings' area: by the screens, each key the sections serve, and each box's lead", () => {
    const used = usedKeys(
      'jfbGlobalSettings',
      keysOf('jfbGlobalSettings'),
      {},
      inFolders('settings', 'privacy', 'activity'),
    );
    // The snapshot holds the line under a choice (`${choice.label}Line`) only where the field's editor draws one.
    for (const key of snapshot('settings-keys.json') as string[]) used.add(key.slice('jfbGlobalSettings_'.length));
    for (const [section, boxes] of Object.entries(LEADS)) for (const box of boxes) used.add(`${section}Box_${box}Lead`);
    for (const days of SPANS) used.add(`retentionSpan_${days}`);
    expectHeld('jfbGlobalSettings', used);
  });

  it("in the entries' area: by its screens, and each state, test and spam reason the server sends", () => {
    const used = usedKeys(
      'jfbEntries',
      keysOf('jfbEntries'),
      {
        run: family(enumMembers(core('Persistence', 'Repositories', 'IActionRunRepository.cs'), 'ActionRunState')),
        mail: family(enumMembers(core('Persistence', 'Repositories', 'IEmailRepository.cs'), 'EmailState')),
        test: family(enumMembers(core('Entries', 'EntryListFilter.cs'), 'EntryFieldTest')),
        chip: family(enumMembers(core('Entries', 'EntryListFilter.cs'), 'EntryFieldTest')),
        spam: family(constantsIn(core('Submissions', 'SubmissionProtection.cs'))),
      },
      inFolders('entries', 'designer/entries', 'designer/insights'),
    );
    expectHeld('jfbEntries', used);
  });

  it("in the insights' area: by its screens and the form's Insights view", () => {
    expectHeld(
      'jfbInsights',
      usedKeys('jfbInsights', keysOf('jfbInsights'), {}, inFolders('insights', 'designer/insights')),
    );
  });

  it("in the export formats' area: each name the built-in formats serve, and no other", () => {
    const named = new Set(
      [...core('Exports', 'BuiltInExportFormats.cs').matchAll(/"#jfbExport_([A-Za-z]+)"/g)].map(
        (match) => match[1] ?? '',
      ),
    );
    expect(named.size).toBe(5);
    expectHeld('jfbExport', named);
    expect([...named].filter((key) => !keysOf('jfbExport').has(key))).toEqual([]);
  });

  it("in the layouts' names the Actions view loads beside its own words: each built-in layout's name the server serves", () => {
    const served = new Set(
      [
        ...read(
          'src',
          'Jiron.Umbraco.FormBuilder.Web',
          'ManagementApi',
          'Designer',
          'FormSettingsController.cs',
        ).matchAll(/"#jfbEmailLayouts_([A-Za-z]+)"/g),
      ].map((match) => match[1] ?? ''),
    );
    const loaded = Object.keys(modules['./actions.en.ts']?.default['jfbEmailLayouts'] ?? {});
    expect(served.size).toBe(2);
    expect(loaded.sort()).toEqual([...served].sort());
  });

  it("in the action types' areas: each key the built-in types, their presets, choices and settings are served with", () => {
    const types = snapshot('action-types.json');
    for (const area of ['jfbActionTypes', 'jfbActionPresets', 'jfbActionChoices', 'jfbActionSettings'])
      expectHeld(area, servedIn(types, area));
  });
});

describe('every area', () => {
  /**
   * The areas other checks hold to the keys their code uses: the dictionaries' check (`lang.test.ts`) and each screen's
   * own words check, by the file that holds it.
   */
  const ELSEWHERE: Readonly<Record<string, string>> = {
    jfbSection: 'lang/lang.test.ts',
    jfbOverview: 'lang/lang.test.ts',
    jfbLibrary: 'lang/lang.test.ts',
    jfbDesigner: 'lang/lang.test.ts',
    jfbCheck: 'lang/lang.test.ts',
    jfbPreview: 'lang/lang.test.ts',
    jfbFormPicker: 'lang/lang.test.ts',
    jfbPicker: 'lang/lang.test.ts',
    jfbFieldTypes: 'lang/lang.test.ts',
    jfbFieldPalette: 'lang/lang.test.ts',
    jfbFieldSettingGroups: 'lang/lang.test.ts',
    jfbFieldSettings: 'lang/lang.test.ts',
    jfbFieldChoices: 'lang/lang.test.ts',
    jfbFieldTypeAvailability: 'lang/lang.test.ts',
    jfbOptionListSettings: 'lang/lang.test.ts',
    jfbOptionListChoices: 'lang/lang.test.ts',
    jfbOptionSources: 'lang/lang.test.ts',
    jfbAccess: 'access/access-words.test.ts',
    jfbActivity: 'activity/activity-words.test.ts',
    jfbOptionLists: 'option-lists/option-list-words.test.ts',
    jfbEmailLayouts: 'email-layouts/email-layout-words.test.ts',
    jfbThemes: 'themes/theme-words.test.ts',
    jfbLearnMore: 'docs/learn-more.test.ts',
  };
  const HERE = [
    'jfbInspector',
    'jfbRules',
    'jfbMergeTags',
    'jfbSettings',
    'jfbOutcomes',
    'jfbHistory',
    'jfbActions',
    'jfbGlobalSettings',
    'jfbEntries',
    'jfbInsights',
    'jfbExport',
    'jfbActionTypes',
    'jfbActionPresets',
    'jfbActionChoices',
    'jfbActionSettings',
  ];

  it('is held to the keys its code uses by one check, here or where its screens are', () => {
    const src = join(here, '..');
    for (const [area, file] of Object.entries(ELSEWHERE))
      expect(readFileSync(join(src, file), 'utf8'), `${file} holds ${area}`).toContain(area);
    expect([...areas.keys()].filter((area) => !HERE.includes(area) && !Object.hasOwn(ELSEWHERE, area))).toEqual([]);
    expect(HERE.filter((area) => Object.hasOwn(ELSEWHERE, area))).toEqual([]);
  });
});
