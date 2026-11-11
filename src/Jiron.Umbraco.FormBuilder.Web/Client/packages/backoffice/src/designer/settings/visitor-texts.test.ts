// A site's or a form's own wording of the visitor texts (ROADMAP 14.2, ADR-0230): the editor lists the texts reworded and
// those just chosen in the package's order, named by the package's words in the language shown; it writes the culture
// it edits and never another's, an emptied wording leaves the package's words (and the text goes once it has none), a
// wording on two lines, too long or naming a value its text has not says so, and "Reword a text" offers each text by
// group that is not listed yet, once chosen focusing its box.
import { afterEach, beforeAll, describe, expect, it, vi } from 'vitest';
import { must, registerEnglish, settle, shadow, testHost, text } from '../../testing';
import type { VisitorTextsResponseModel } from '../../api';

const asked = vi.hoisted(() => ({ cultures: [] as string[] }));

const catalogueOf = (culture: string): VisitorTextsResponseModel => ({
  culture,
  groups: [
    {
      alias: 'files',
      texts: [
        {
          key: 'form.chooseFile',
          text: culture.startsWith('de') ? 'Datei ausw\u00e4hlen' : 'Choose a file',
          values: [],
        },
      ],
    },
    {
      alias: 'errors',
      texts: [
        { key: 'message.email', text: 'Enter an email address', values: [] },
        { key: 'message.minLength.other', text: 'Enter at least {min} characters', values: ['{min}'] },
      ],
    },
  ],
});

vi.mock('@umbraco-cms/backoffice/resources', () => ({
  tryExecute: async (_host: unknown, request: Promise<unknown>) => ({ data: await request }),
}));

vi.mock('../../api', async (original) => ({
  ...(await original<typeof import('../../api')>()),
  visitorTextsGet: async ({ query }: { query: { culture: string } }) => {
    asked.cultures.push(query.culture);
    return catalogueOf(query.culture);
  },
}));

const { VisitorTextsElement } = await import('./visitor-texts.element');
const { isVisitorTexts, rowsOf, withWording, withoutWording, wordingIn, wordingProblem } =
  await import('./visitor-texts');

beforeAll(registerEnglish);

afterEach(() => {
  document.body.innerHTML = '';
});

describe('the wording', () => {
  const texts = { 'message.email': { 'de-DE': 'Ihre E-Mail', 'en-US': 'Your email' } };

  it('is a map of texts to their wording per culture', () => {
    expect(isVisitorTexts(texts)).toBe(true);
    expect(isVisitorTexts({ 'message.email': 'x' })).toBe(false);
    expect(isVisitorTexts([texts])).toBe(false);
    expect(isVisitorTexts(null)).toBe(false);
  });

  it("is the culture's own, in any letter case, and never another culture's", () => {
    expect(wordingIn(texts, 'message.email', 'DE-de')).toBe('Ihre E-Mail');
    expect(wordingIn(texts, 'message.email', 'fr-FR')).toBe('');
    expect(wordingIn(undefined, 'message.email', 'de-DE')).toBe('');
  });

  it("is written in the culture the editor edits, the others' kept, and an emptied one leaves the package's", () => {
    const changed = withWording(texts, 'message.email', 'de-de', 'Ihre Adresse');
    expect(changed).toEqual({ 'message.email': { 'en-US': 'Your email', 'de-de': 'Ihre Adresse' } });
    expect(withoutWording(changed, 'message.email', 'en-US')).toEqual({ 'message.email': { 'de-de': 'Ihre Adresse' } });
    expect(withoutWording(withoutWording(changed, 'message.email', 'en-US'), 'message.email', 'de-DE')).toEqual({});
    expect(withWording(undefined, 'form.chooseFile', 'de-DE', '  ')).toEqual({});
    expect(texts['message.email']['de-DE']).toBe('Ihre E-Mail');
  });

  it('keeps each text where it was when its wording changes, and adds a new one last', () => {
    const two = { 'message.email': { '': 'Your email' }, 'form.chooseFile': { '': 'Pick a file' } };
    expect(Object.keys(withWording(two, 'message.email', 'de-DE', 'Ihre E-Mail'))).toEqual([
      'message.email',
      'form.chooseFile',
    ]);
    expect(Object.keys(withWording(two, 'answer.yes', '', 'Sure'))).toEqual([
      'message.email',
      'form.chooseFile',
      'answer.yes',
    ]);
  });

  it("says a wording on two lines, too long, or naming a value or a brace its text hasn't", () => {
    expect(wordingProblem('At least {min} letters', ['{min}'])).toBeNull();
    expect(wordingProblem('Two\nlines', [])).toEqual({ kind: 'line' });
    expect(wordingProblem('a'.repeat(501), [])).toEqual({ kind: 'long', most: 500 });
    expect(wordingProblem('a'.repeat(500), [])).toBeNull();
    expect(wordingProblem('At most {max}', ['{min}'])).toEqual({ kind: 'values', values: ['{min}'] });
    expect(wordingProblem('Your { email', [])).toEqual({ kind: 'values', values: [] });
  });

  it("lists the texts reworded and those chosen, in the package's order, with their wording in the culture", () => {
    const rows = rowsOf(
      { 'message.minLength.other': { 'en-US': 'At least {min}' }, 'message.gone': { '': 'x' } },
      'en-US',
      catalogueOf('en-US'),
      new Set(['form.chooseFile']),
    );
    expect(rows.map((row) => [row.key, row.group, row.wording])).toEqual([
      ['form.chooseFile', 'files', ''],
      ['message.minLength.other', 'errors', 'At least {min}'],
    ]);
  });
});

describe('the editor', () => {
  async function editorOf(texts: Record<string, Record<string, string>> | undefined, culture = 'de-DE') {
    const host = testHost();
    const editor = new VisitorTextsElement();
    const changes: unknown[] = [];
    editor.texts = texts;
    editor.culture = culture;
    editor.addEventListener('change', (event) => {
      const next = (event as CustomEvent<{ texts: Record<string, Record<string, string>> }>).detail.texts;
      changes.push(next);
      editor.texts = next;
    });
    host.append(editor);
    await settle(editor);
    await new Promise((resolve) => setTimeout(resolve, 0));
    await settle(editor);
    return { editor, root: shadow(editor), changes };
  }

  it("names each text reworded by the package's words in the language shown, with no placeholder", async () => {
    const { root } = await editorOf({ 'form.chooseFile': { 'de-DE': 'Datei hochladen' } });
    const box = must(root.querySelector<HTMLInputElement>('#text-form-chooseFile'), 'the box');
    expect(text(root.querySelector('label[for="text-form-chooseFile"]'))).toBe('Datei ausw\u00e4hlen');
    expect(box.value).toBe('Datei hochladen');
    expect(box.lang).toBe('de-DE');
    expect(box.placeholder).toBe('');
    expect(asked.cultures).toContain('de-DE');
  });

  it('offers each text not listed yet by group, and a text chosen gets its box, focused', async () => {
    const { root } = await editorOf({ 'form.chooseFile': { 'de-DE': 'Datei hochladen' } });
    const select = must(root.querySelector<HTMLSelectElement>('#choose'), 'the list');
    expect([...select.querySelectorAll('optgroup')].map((group) => group.getAttribute('label'))).toEqual([
      'Files',
      'Error messages',
    ]);
    expect([...select.querySelectorAll('option')].map((option) => option.value)).toEqual([
      '',
      'message.email',
      'message.minLength.other',
    ]);
    select.value = 'message.minLength.other';
    select.dispatchEvent(new Event('change'));
    await settle();
    await new Promise((resolve) => setTimeout(resolve, 0));
    const box = must(root.querySelector<HTMLInputElement>('#text-message-minLength-other'), 'the new box');
    expect(root.activeElement).toBe(box);
    expect(select.value).toBe('');
  });

  it('writes the wording in its culture, says what a wording cannot name, and takes a text away', async () => {
    const { editor, root, changes } = await editorOf({ 'message.minLength.other': { 'en-US': 'At least {min}' } });
    const box = must(root.querySelector<HTMLInputElement>('#text-message-minLength-other'), 'the box');
    box.value = 'Mindestens {max} Zeichen';
    box.dispatchEvent(new Event('input'));
    await settle(editor);
    expect(changes.at(-1)).toEqual({
      'message.minLength.other': { 'en-US': 'At least {min}', 'de-DE': 'Mindestens {max} Zeichen' },
    });
    expect(box.getAttribute('aria-invalid')).toBe('true');
    expect(text(root.querySelector('#text-message-minLength-other-why'))).toBe(
      'This text can name only {min} in braces.',
    );
    must(root.querySelector<HTMLButtonElement>('.remove'), 'the remove button').click();
    await settle(editor);
    expect(changes.at(-1)).toEqual({ 'message.minLength.other': { 'en-US': 'At least {min}' } });
  });

  it('keeps a box emptied while it is typed in, so a wording can be typed again', async () => {
    const { editor, root, changes } = await editorOf(undefined);
    const select = must(root.querySelector<HTMLSelectElement>('#choose'), 'the list');
    select.value = 'message.email';
    select.dispatchEvent(new Event('change'));
    await settle(editor);
    const box = must(root.querySelector<HTMLInputElement>('#text-message-email'), 'the box');
    box.value = '';
    box.dispatchEvent(new Event('input'));
    await settle(editor);
    expect(changes.at(-1)).toEqual({});
    expect(root.querySelector('#text-message-email')).not.toBeNull();
  });

  it("asks for a culture's texts once, whichever editor asks", async () => {
    const before = asked.cultures.filter((culture) => culture === 'fr-FR').length;
    await editorOf(undefined, 'fr-FR');
    await editorOf(undefined, 'fr-FR');
    expect(asked.cultures.filter((culture) => culture === 'fr-FR').length - before).toBe(1);
  });
});
