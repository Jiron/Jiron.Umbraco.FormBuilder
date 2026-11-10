import { describe, expect, it, vi } from 'vitest';

const asked = vi.hoisted(() => ({ calls: [] as { key: string[]; culture: string }[], fails: false }));

vi.mock('@umbraco-cms/backoffice/resources', () => ({
  tryExecute: async (_host: unknown, request: Promise<unknown>) => ({ data: await request }),
}));

vi.mock('../api', async (original) => ({
  ...(await original<typeof import('../api')>()),
  dictionaryTextGet: async ({ query }: { query: { key: string[]; culture: string } }) => {
    asked.calls.push(query);
    if (asked.fails) return undefined;
    const known: Record<string, string> = { 'forms.email': 'E-Mail-Adresse', 'forms.empty': '' };
    return {
      culture: query.culture,
      texts: query.key.map((key) => ({
        key,
        found: Object.hasOwn(known, key.toLowerCase()),
        text: known[key.toLowerCase()] ?? '',
      })),
    };
  },
}));

const {
  DictionaryReader,
  dictionaryKeyOf,
  dictionaryKeysIn,
  dictionaryLineOf,
  isLocalizedText,
  shownText,
  withDictionary,
} = await import('./dictionary-texts');

const lookups = new Map([
  ['forms.email', { found: true, text: 'E-Mail-Adresse' }],
  ['forms.empty', { found: true, text: '' }],
  ['forms.gone', { found: false, text: '' }],
]);

describe('a text that names a dictionary item', () => {
  it('names one as the server reads it: # and letters, digits, dots, dashes and underscores, nothing around it', () => {
    expect(dictionaryKeyOf('#Forms.Email')).toBe('Forms.Email');
    expect(dictionaryKeyOf('#a_b-c.9')).toBe('a_b-c.9');
    for (const text of ['Forms.Email', '# Forms', '#Forms Email', ' #Forms', '#', '#<b>', '', null, undefined])
      expect(dictionaryKeyOf(text)).toBeNull();
  });

  it("tells an editor's text from other records by its default and its texts", () => {
    expect(isLocalizedText({ '': 'Email', de: 'E-Mail' })).toBe(true);
    expect(isLocalizedText({ de: 'E-Mail' })).toBe(false);
    expect(isLocalizedText({ '': 'Email', count: 3 })).toBe(false);
    expect(isLocalizedText(['x'])).toBe(false);
  });

  it('finds the keys the texts name in the language shown, anywhere in the draft, in lower case', () => {
    const draft = {
      pages: [
        {
          key: 'p',
          title: { '': '#Forms.Page' },
          groups: [
            {
              key: 'g',
              fields: [
                {
                  key: 'f',
                  label: { '': 'Email', 'de-DE': '#Forms.Email' },
                  settings: { prompt: { '': '#Forms.Prompt' } },
                },
              ],
            },
          ],
        },
      ],
    };
    expect([...dictionaryKeysIn(draft, 'de-DE')].sort()).toEqual(['forms.email', 'forms.page', 'forms.prompt']);
    expect([...dictionaryKeysIn(draft, 'en-US')].sort()).toEqual(['forms.page', 'forms.prompt']);
  });

  it("shows the item's text where it has one, and the text as written where it hasn't or there is none", () => {
    expect(shownText({ '': '#Forms.Email' }, 'de-DE', lookups)).toBe('E-Mail-Adresse');
    expect(shownText({ '': '#forms.email' }, 'de-DE', lookups)).toBe('E-Mail-Adresse');
    expect(shownText({ '': '#Forms.Empty' }, 'de-DE', lookups)).toBe('#Forms.Empty');
    expect(shownText({ '': '#Forms.Gone' }, 'de-DE', lookups)).toBe('#Forms.Gone');
    expect(shownText({ '': 'Email' }, 'de-DE', lookups)).toBe('Email');
    expect(shownText(null, 'de-DE', lookups)).toBe('');
  });

  it("draws the draft with the items' texts, keeping every part that changes not as the same object", () => {
    const plain = { key: 'a', label: { '': 'Name' } };
    const named = { key: 'b', label: { '': 'Email', 'de-DE': '#Forms.Email' } };
    const group = { key: 'g', fields: [plain, named] };
    const other = { key: 'h', fields: [plain] };
    const draft = { pages: [{ key: 'p', groups: [group, other] }] };
    const drawn = withDictionary(draft, 'de-DE', lookups);
    expect(drawn).not.toBe(draft);
    expect(drawn.pages[0]?.groups[1]).toBe(other);
    expect(drawn.pages[0]?.groups[0]?.fields[0]).toBe(plain);
    expect(drawn.pages[0]?.groups[0]?.fields[1]?.label).toEqual({ '': 'Email', 'de-DE': 'E-Mail-Adresse' });
    // In English the text names no item, and nothing changes.
    expect(withDictionary(draft, 'en-US', lookups)).toBe(draft);
    expect(withDictionary(draft, 'de-DE', new Map())).toBe(draft);
  });

  it("says what visitors see: the item's text, or why the text shows as written", () => {
    const term = (key: string, ...args: unknown[]) => [key, ...args].join(' | ');
    expect(dictionaryLineOf('#Forms.Email', lookups, term)).toBe('dictionaryShows | E-Mail-Adresse');
    expect(dictionaryLineOf('#Forms.Empty', lookups, term)).toBe('dictionaryEmpty | #Forms.Empty');
    expect(dictionaryLineOf('#Forms.Gone', lookups, term)).toBe('dictionaryMissing | Forms.Gone | #Forms.Gone');
    // Not read yet, or no key: nothing to say.
    expect(dictionaryLineOf('#Forms.Unread', lookups, term)).toBeNull();
    expect(dictionaryLineOf('Email', lookups, term)).toBeNull();
  });

  it('reads each language and key from the server once, a hundred at a time, and again once forgotten', async () => {
    asked.calls = [];
    asked.fails = false;
    const reader = new DictionaryReader();
    const host = {} as never;
    const first = await reader.read(host, ['Forms.Email', 'forms.email', 'Forms.Gone'], 'de-DE');
    expect(first).toEqual(
      new Map([
        ['forms.email', { found: true, text: 'E-Mail-Adresse' }],
        ['forms.gone', { found: false, text: '' }],
      ]),
    );
    await reader.read(host, ['forms.email'], 'de-DE');
    await reader.read(host, ['forms.email'], 'en-US');
    expect(asked.calls.map((call) => [call.culture, call.key])).toEqual([
      ['de-DE', ['forms.email', 'forms.gone']],
      ['en-US', ['forms.email']],
    ]);
    asked.calls = [];
    const many = Array.from({ length: 150 }, (_, at) => `k${at}`);
    await reader.read(host, many, 'de-DE');
    expect(asked.calls.map((call) => call.key.length)).toEqual([100, 50]);
    reader.forget();
    asked.calls = [];
    await reader.read(host, ['forms.email'], 'de-DE');
    expect(asked.calls).toHaveLength(1);
  });

  it('gives no lookup for what the server could not answer, and asks again the next time', async () => {
    asked.calls = [];
    asked.fails = true;
    const reader = new DictionaryReader();
    expect(await reader.read({} as never, ['forms.email'], 'de-DE')).toEqual(new Map());
    asked.fails = false;
    expect((await reader.read({} as never, ['forms.email'], 'de-DE')).get('forms.email')?.text).toBe('E-Mail-Adresse');
    expect(asked.calls).toHaveLength(2);
  });
});
