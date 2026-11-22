// The AI add-on's backoffice without a site (ADR-0018): its words in both languages for every key its code and
// manifests use; refusals said by their codes; the translation's preview and what Apply writes; and its manifests, each
// named as the add-on's, placed where the information architecture puts them, for who may use them.
import { readdirSync, readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { UmbApiError } from '@umbraco-cms/backoffice/resources';
import { describe, expect, it } from 'vitest';
import { manifests } from './bundle.manifests';
import de from './lang/de';
import en from './lang/en';
import { KNOWN_PROBLEMS, problemKey, problemOf, refusalOf, refusalText } from './problems';
import { changesOf, plainOf, rowsOf, withRow } from './translate/translate.model';

const folder = dirname(fileURLToPath(import.meta.url));

/**
 * The add-on's sources, its tests and generated client left out. A folder whose name starts with a dot is another test's
 * scratch (the API check's), which may go while this one reads.
 */
function sources(dir = folder): string[] {
  return readdirSync(dir, { withFileTypes: true }).flatMap((entry) => {
    if (entry.name.startsWith('.')) return [];
    const path = join(dir, entry.name);
    if (entry.isDirectory()) return entry.name === 'api' || entry.name === 'lang' ? [] : sources(path);
    return entry.name.endsWith('.ts') && !entry.name.endsWith('.test.ts') ? [path] : [];
  });
}

describe('the words', () => {
  it('are the same keys in English and German, and the functions take the same arguments', () => {
    expect(Object.keys(de.jfbAI).sort()).toEqual(Object.keys(en.jfbAI).sort());
    for (const [key, text] of Object.entries(en.jfbAI)) {
      const german = (de.jfbAI as Record<string, unknown>)[key];
      expect(typeof german, key).toBe(typeof text);
      if (typeof text === 'function') expect((german as (...args: never[]) => unknown).length, key).toBe(text.length);
    }
  });

  it('hold every key the code and the manifests name, and no key nothing names', () => {
    const code = sources()
      .map((file) => readFileSync(file, 'utf8'))
      .join('\n');
    const named = new Set([...code.matchAll(/jfbAI_([A-Za-z_]+)/g)].map((match) => match[1] ?? ''));
    const built = ['problem_', 'suggestSetting_'];
    expect([...named].filter((key) => !built.some((prefix) => key === prefix) && !(key in en.jfbAI))).toEqual([]);
    expect(
      Object.keys(en.jfbAI).filter((key) => !named.has(key) && !built.some((prefix) => key.startsWith(prefix))),
    ).toEqual([]);
    for (const code of KNOWN_PROBLEMS) expect(en.jfbAI, code).toHaveProperty(`problem_${code}`);
    for (const setting of ['label', 'help', 'placeholder'])
      expect(en.jfbAI).toHaveProperty(`suggestSetting_${setting}`);
  });
});

describe('a refusal', () => {
  it('is said by its code, and anything else as a failure', () => {
    const refusal = (code: string) =>
      new UmbApiError('refused', 409, undefined, { code, title: 'x', status: 409, type: 'about:blank' } as never);
    expect(problemOf(refusal('ai_no_profile'))).toBe('ai_no_profile');
    expect(problemOf(refusal('something_else'))).toBe('failed');
    expect(problemOf(new Error('network'))).toBe('failed');
    expect(problemKey('ai_provider_failed')).toBe('jfbAI_problem_ai_provider_failed');
    expect(problemKey('unknown')).toBe('jfbAI_problem_failed');
  });

  it("names a refused value by its own code, and a site's stop with the site's words", () => {
    const answer = (details: object) =>
      new UmbApiError('refused', 400, undefined, { title: 'x', status: 400, type: 'about:blank', ...details } as never);
    const words = (key: string) => `<${key}>`;

    expect(problemOf(answer({ code: 'invalid_request', errors: [{ code: 'text_invalid', path: '$.text' }] }))).toBe(
      'text_invalid',
    );
    expect(problemOf(answer({ code: 'invalid_request', errors: [{ code: 'other' }] }))).toBe('failed');
    const stopped = refusalOf(answer({ code: 'cancelled', messages: ['Closed for the holidays.', 'Try in January.'] }));
    expect(stopped).toEqual({ code: 'cancelled', said: 'Closed for the holidays. Try in January.' });
    expect(refusalText(words, stopped)).toBe('<jfbAI_problem_cancelled> Closed for the holidays. Try in January.');
    expect(refusalText(words, refusalOf(answer({ code: 'folder_missing' })))).toBe('<jfbAI_problem_folder_missing>');
  });
});

describe("a translation's preview", () => {
  const texts = [
    { path: '$.pages[0].title', source: 'About you', text: '\u00dcber Sie', html: false },
    {
      path: '$.pages[0].groups[0].fields[0].settings.html',
      source: '<p>Hi <b>there</b></p>',
      text: '<p>Hallo <b>dort</b></p>',
      html: true,
    },
    { path: '$.pages[0].groups[0].fields[1].label', source: 'Name', text: 'Name', html: false },
  ];

  it('uses every text until the editor changes one or leaves it out, and Apply writes what is used and not empty', () => {
    let rows = rowsOf(texts);
    expect(rows.every((row) => row.use)).toBe(true);
    rows = withRow(rows, '$.pages[0].title', { text: '  Zu Ihnen ' });
    rows = withRow(rows, '$.pages[0].groups[0].fields[1].label', { use: false });

    expect(changesOf(rows, 'de-DE')).toEqual([
      { path: '$.pages[0].title', culture: 'de-DE', text: 'Zu Ihnen' },
      { path: '$.pages[0].groups[0].fields[0].settings.html', culture: 'de-DE', text: '<p>Hallo <b>dort</b></p>' },
    ]);
    expect(changesOf(withRow(rows, '$.pages[0].title', { text: '   ' }), 'de-DE')).toHaveLength(1);
  });

  it('shows rich text as it reads', () => {
    expect(plainOf('<p>Hi <b>there</b></p>\n<p>again</p>')).toBe('Hi there again');
  });
});

describe('the manifests', () => {
  const byAlias = new Map(
    manifests.map((manifest) => [manifest.alias, manifest as unknown as Record<string, unknown>]),
  );

  it("are each named as the add-on's", () => {
    expect(
      manifests.map((manifest) => manifest.alias).every((alias) => alias.startsWith('Jiron.FormBuilder.AI.')),
    ).toBe(true);
    expect(new Set(manifests.map((manifest) => manifest.alias)).size).toBe(manifests.length);
  });

  it('put each feature where the information architecture says, for who may use it', () => {
    const draft = byAlias.get('Jiron.FormBuilder.AI.EntityCreateOptionAction.Draft');
    expect(draft?.['forEntityTypes']).toEqual(['jfb-library-root', 'jfb-folder']);
    expect(draft?.['weight'], "after the core's options, the last of which weighs 100").toBeLessThan(100);
    expect(byAlias.get('Jiron.FormBuilder.AI.WorkspaceFooterApp.Drafted')?.['conditions']).toEqual([
      { alias: 'Umb.Condition.WorkspaceAlias', match: 'Jiron.FormBuilder.Workspace.Form' },
    ]);
    expect(byAlias.get('Jiron.FormBuilder.AI.DesignerAction.Translate')?.['type']).toBe('jfbDesignerAction');
    expect(byAlias.get('Jiron.FormBuilder.AI.FieldTextAction.Suggest')?.['meta']).toEqual({ texts: ['help'] });
    const menu = byAlias.get('Jiron.FormBuilder.AI.MenuItem.Settings');
    expect((menu?.['meta'] as { menus?: unknown })?.menus).toEqual(['Jiron.FormBuilder.Menu.Settings']);
    expect(menu?.['conditions']).toEqual([
      { alias: 'Jiron.FormBuilder.Condition.Permission', needs: [{ anyOf: ['manageSettings'] }] },
    ]);
    expect(
      (byAlias.get('Jiron.FormBuilder.AI.Workspace.Settings')?.['meta'] as { entityType?: unknown })?.entityType,
    ).toBe((menu?.['meta'] as { entityType?: unknown })?.entityType);
  });
});
