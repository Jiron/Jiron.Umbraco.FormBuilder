// "Insert merge tag" (ROADMAP 9.4c, ADR-0173): opening it asks the server for the place's paths in the draft as it is,
// in the language shown, and shows them by source under a search box; a path pressed goes to the box as its tag, and
// Escape gives the focus back to the button. A stand-in server answers from the endpoint's snapshot.
import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { afterEach, beforeAll, describe, expect, it, vi } from 'vitest';
import type { MergeTagPathResponseModel } from '../../api';
import { must, registerEnglish, settle, testHost, text } from '../../testing';

const server = vi.hoisted(() => ({
  asked: [] as unknown[][],
  answer: undefined as MergeTagPathResponseModel[] | undefined,
  pending: undefined as Promise<MergeTagPathResponseModel[]> | undefined,
}));

vi.mock('./merge-tags', async (original) => ({
  ...(await original<typeof import('./merge-tags')>()),
  readMergeTagPaths: async (...args: unknown[]) => {
    server.asked.push(args.slice(1));
    return server.pending ?? server.answer;
  },
}));

const { MergeTagPickerElement } = await import('./merge-tag-picker.element');

const root = join(dirname(fileURLToPath(import.meta.url)), ...Array<string>(8).fill('..'));
const consent = (
  JSON.parse(readFileSync(join(root, 'docs', 'api', 'merge-tag-paths.json'), 'utf8')) as Record<
    string,
    MergeTagPathResponseModel[]
  >
)['consentText'];

beforeAll(registerEnglish);

afterEach(() => {
  document.body.replaceChildren();
  server.asked.length = 0;
});

async function pickerOf() {
  const element = new MergeTagPickerElement();
  const inserted: string[] = [];
  const definition = { pages: [] };
  Object.assign(element, {
    place: 'consentText',
    form: 'form1',
    culture: 'de-DE',
    definition: () => definition,
    insert: (tag: string) => inserted.push(tag),
    owner: 'heading-1',
  });
  testHost().append(element);
  await settle(element);
  const shadow = must(element.shadowRoot, 'its shadow root');
  const button = must(shadow.querySelector<HTMLButtonElement>('.open'), 'the button');
  // Opens the panel, closing it first when it is open, as pressing the button twice does.
  const open = async () => {
    if (shadow.querySelector('.panel')) {
      button.click();
      await settle(element);
    }
    button.click();
    await new Promise((done) => setTimeout(done, 0));
    await settle(element);
  };
  const search = () => must(shadow.querySelector<HTMLInputElement>('input[type="search"]'), 'the search');
  const type = async (value: string) => {
    search().value = value;
    search().dispatchEvent(new Event('input'));
    await settle(element);
  };
  return { element, shadow, inserted, definition, open, button, search, type };
}

describe('the merge tag picker', () => {
  it("asks for the place's paths in the draft as it is, in the language shown, and shows them by source", async () => {
    server.answer = consent;
    const { shadow, open, definition, button } = await pickerOf();
    expect(text(button)).toBe('Insert merge tag');
    expect(button.getAttribute('aria-expanded')).toBe('false');
    expect(shadow.querySelector('.panel')).toBeNull();
    await open();
    expect(server.asked).toEqual([['form1', 'consentText', 'de-DE', definition, 'heading-1']]);
    const panel = must(shadow.querySelector('.panel'), 'the panel');
    expect([button.getAttribute('aria-expanded'), button.getAttribute('aria-controls')]).toEqual(['true', panel.id]);
    expect([panel.getAttribute('role'), panel.getAttribute('aria-label')]).toEqual(['group', 'Insert merge tag']);
    expect([...panel.querySelectorAll('jfb-learn-more')].map((link) => link.getAttribute('topic'))).toEqual([
      'mergeTags',
    ]);
    expect(shadow.activeElement).toBe(shadow.querySelector('input[type="search"]'));
    expect([...shadow.querySelectorAll('.heading')].map((heading) => text(heading))).toEqual([
      'The form',
      'The page',
      'The site',
    ]);
    expect([...shadow.querySelectorAll('.path span')].map((label) => text(label))).toEqual([
      'Name of the form',
      'Alias of the form',
      'Name of the page',
      'Address of the page',
      'Name of the site',
      'Address of the site',
    ]);
    expect(text(shadow.querySelector('.path code'))).toBe('{{ form.name }}');
  });

  it('finds paths by what is typed, says when none matches, and inserts the one pressed', async () => {
    server.answer = consent;
    const { element, shadow, open, inserted, type, search } = await pickerOf();
    await open();
    await type('address');
    expect([...shadow.querySelectorAll('.path span')].map((label) => text(label))).toEqual([
      'Address of the page',
      'Address of the site',
    ]);
    must(shadow.querySelector<HTMLElement>('.path'), 'the first path').click();
    await settle(element);
    expect(inserted).toEqual(['{{ page.url }}']);
    expect(shadow.querySelector('.panel')).toBeNull();
    await open();
    await type('zzz');
    expect(text(shadow.querySelector('.note'))).toBe('No merge tag matches.');
    // Opened again, it starts with an empty search and every path.
    await open();
    expect(search().value).toBe('');
    expect(shadow.querySelectorAll('.path')).toHaveLength(6);
  });

  it('closes when the focus leaves it, and drops an answer to an earlier opening', async () => {
    server.answer = consent;
    const { element, shadow, open, search, button } = await pickerOf();
    await open();
    search().dispatchEvent(
      new FocusEvent('focusout', { bubbles: true, composed: true, relatedTarget: shadow.querySelector('.path') }),
    );
    await settle(element);
    expect(shadow.querySelector('.panel')).not.toBeNull();
    const outside = document.createElement('button');
    testHost().append(outside);
    search().dispatchEvent(new FocusEvent('focusout', { bubbles: true, composed: true, relatedTarget: outside }));
    await settle(element);
    expect([shadow.querySelector('.panel'), button.getAttribute('aria-expanded')]).toEqual([null, 'false']);

    let answer: (paths: MergeTagPathResponseModel[]) => void = () => undefined;
    server.answer = undefined;
    server.pending = new Promise<MergeTagPathResponseModel[]>((done) => (answer = done));
    await open();
    server.pending = undefined;
    server.answer = [];
    await open();
    answer(consent ?? []);
    await new Promise((done) => setTimeout(done, 0));
    await settle(element);
    expect(text(shadow.querySelector('.note'))).toBe('No merge tag matches.');
  });

  it("says when the paths can't be read, and gives the focus back to its button on Escape", async () => {
    server.answer = undefined;
    const { element, shadow, open, search, button } = await pickerOf();
    await open();
    expect(text(shadow.querySelector('.note'))).toBe("The merge tags couldn't be read.");
    const escape = new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, composed: true, cancelable: true });
    search().dispatchEvent(escape);
    await settle(element);
    expect(escape.defaultPrevented).toBe(true);
    expect(shadow.querySelector('.panel')).toBeNull();
    expect(shadow.activeElement).toBe(button);
  });
});
