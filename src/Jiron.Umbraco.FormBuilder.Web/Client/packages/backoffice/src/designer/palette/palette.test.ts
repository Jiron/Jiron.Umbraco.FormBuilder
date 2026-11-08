// The palette and the Paste questions dialog (ROADMAP 9.3b): Recently used as this browser keeps it, Layout, the types in
// their groups in the backoffice's words, search that narrows them, what each pick asks for, an unavailable type's
// reason, the palette's ways of showing, a type pressed to drag it onto the page (9.3c); and the dialog's fields to add
// with their guessed types and its count.
import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { afterEach, beforeAll, beforeEach, describe, expect, it } from 'vitest';
import type { FieldTypesResponseModel } from '../../api';
import { all, must, registerEnglish, settle, shadow, text } from '../../testing';
import { builtInTypes } from '../preview/fixtures';
import {
  folded,
  keepRecent,
  MOST_RECENT,
  PALETTE_PICK,
  PaletteElement,
  readRecent,
  type PalettePick,
} from './palette.element';
import { PasteQuestionsModalElement } from './paste-questions.modal.element';

const served = JSON.parse(
  readFileSync(
    join(dirname(fileURLToPath(import.meta.url)), ...Array<string>(8).fill('..'), 'docs', 'api', 'field-types.json'),
    'utf8',
  ),
) as FieldTypesResponseModel;

async function paletteOf(extra: Partial<PaletteElement> = {}) {
  const element = new PaletteElement();
  Object.assign(element, { types: served.types, groups: served.paletteGroups, ...extra });
  const picks: PalettePick[] = [];
  element.addEventListener(PALETTE_PICK, (event) => picks.push((event as CustomEvent<PalettePick>).detail));
  document.body.append(element);
  await settle(element);
  return { element, picks, root: shadow(element) };
}

const headings = (element: Element) => all(element, 'section h3').map((heading) => text(heading));
const items = (element: Element) => all(element, '.item').map((item) => text(item));

beforeAll(registerEnglish);

beforeEach(() => localStorage.clear());

afterEach(() => {
  document.body.replaceChildren();
});

describe('the recently used types', () => {
  it('are none at first, then the types picked last, most recent first, as many as the palette keeps', () => {
    expect(readRecent()).toEqual([]);
    keepRecent('email');
    keepRecent('text');
    keepRecent('email');
    expect(readRecent()).toEqual(['email', 'text']);
    for (const type of ['phone', 'url', 'date']) keepRecent(type);
    expect(readRecent()).toEqual(['date', 'url', 'phone'].slice(0, MOST_RECENT));
  });

  it('are none when the browser keeps something else under their name', () => {
    localStorage.setItem('jfb.designer.recent', '{"not":"a list"}');
    expect(readRecent()).toEqual([]);
    localStorage.setItem('jfb.designer.recent', 'not json');
    expect(readRecent()).toEqual([]);
  });
});

describe('the palette', () => {
  it('shows Layout, then the types in their groups and in their order, and Paste questions at the end', async () => {
    const { element, root } = await paletteOf();
    expect(headings(element)).toEqual([
      'Layout',
      'Text',
      'Numbers and dates',
      'Choices',
      'People',
      'Files',
      'Structure',
      'Content',
      'Protection',
    ]);
    expect(items(element).slice(0, 4)).toEqual(['Group', 'New page', 'Short text', 'Long text']);
    expect(items(element)).toHaveLength(2 + Object.keys(builtInTypes).length);
    expect(root.querySelector('uui-button.paste')?.getAttribute('label')).toBe('Paste questions\u2026');
    // Beside the canvas, its heading is for screen readers only.
    expect(root.querySelector('h2')?.classList.contains('visually-hidden')).toBe(true);
  });

  it('starts with the recently used types', async () => {
    const { element } = await paletteOf({ recent: ['email', 'gone', 'dropdown'] });
    expect(headings(element)[0]).toBe('Recently used');
    expect(items(element).slice(0, 2)).toEqual(['Email', 'Dropdown']);
  });

  it('narrows to what the search finds by name or alias, any letter case and accent, and says when nothing is', async () => {
    const { element, root } = await paletteOf({ recent: ['email'] });
    const search = must(root.querySelector('uui-input'), 'the search');
    (search as unknown as { value: string }).value = 'MAIL';
    search.dispatchEvent(new Event('input'));
    await settle(element);
    expect(headings(element)).toEqual(['Text']);
    expect(items(element)).toEqual(['Email']);

    (search as unknown as { value: string }).value = 'p\u00e1ge';
    search.dispatchEvent(new Event('input'));
    await settle(element);
    expect(items(element)).toEqual(['New page']);

    (search as unknown as { value: string }).value = 'xyz';
    search.dispatchEvent(new Event('input'));
    await settle(element);
    expect(items(element)).toEqual([]);
    expect(text(root.querySelector('.none'))).toBe('No field type matches "xyz".');
    expect(folded('Gro\u00dfe \u00c4pfel!')).toBe('gro\u00dfe apfel');
  });

  it("lists the site's saved groups after the types, finds them by name, and asks for one's copy", async () => {
    const address = { key: 't1', name: 'Address block', group: { key: 'g1', fields: [] }, checks: [] };
    const consent = { key: 't2', name: 'Standard consent', group: { key: 'g2', fields: [] }, checks: [] };
    const { element, picks } = await paletteOf({ saved: [address, consent] });
    expect(headings(element).at(-1)).toBe('Saved groups');
    expect(items(element).slice(-2)).toEqual(['Address block', 'Standard consent']);
    must(
      all<HTMLButtonElement>(element, '.item').find((item) => text(item) === 'Address block'),
      'Address block',
    ).click();
    expect(picks).toEqual([{ kind: 'savedGroup', saved: address }]);
    const search = must(shadow(element).querySelector<HTMLInputElement>('uui-input'), 'the search');
    search.value = 'standard';
    search.dispatchEvent(new Event('input'));
    await settle(element);
    expect(headings(element)).toEqual(['Saved groups']);
    expect(items(element)).toEqual(['Standard consent']);
  });

  it('shows no Saved groups while the site has none', async () => {
    const { element } = await paletteOf();
    expect(headings(element)).not.toContain('Saved groups');
  });

  it('asks for a field of a type, a group, a page and a paste; an unavailable type says why', async () => {
    const captchaless = served.types.map((type) =>
      type.alias === 'captcha'
        ? { ...type, availability: { isAvailable: false, reason: 'No captcha provider has its keys.' } }
        : type,
    );
    const { element, picks, root } = await paletteOf({ types: captchaless });
    const button = (name: string) =>
      must(
        all<HTMLButtonElement>(element, '.item').find((item) => text(item).startsWith(name)),
        name,
      );
    button('Email').click();
    button('Group').click();
    button('New page').click();
    root.querySelector('uui-button.paste')?.dispatchEvent(new Event('click'));
    const captcha = button('Captcha');
    expect(captcha.getAttribute('aria-disabled')).toBe('true');
    captcha.click();
    expect(picks).toEqual([
      { kind: 'field', type: 'email' },
      { kind: 'group' },
      { kind: 'page' },
      { kind: 'paste' },
      { kind: 'unavailable', reason: 'No captcha provider has its keys.' },
    ]);
  });

  it('passes on a press on an available type beside the canvas to drag it; none over the canvas or for others', async () => {
    const captchaless = served.types.map((type) =>
      type.alias === 'captcha' ? { ...type, availability: { isAvailable: false, reason: 'No keys.' } } : type,
    );
    const press = async (mode: 'docked' | 'overlay', name: string) => {
      const { element, picks } = await paletteOf({ types: captchaless, mode });
      const item = must(
        all<HTMLButtonElement>(element, '.item').find((each) => text(each).startsWith(name)),
        name,
      );
      const event = new MouseEvent('pointerdown', { bubbles: true }) as PointerEvent;
      item.dispatchEvent(event);
      return { picks, item, event };
    };
    const email = await press('docked', 'Email');
    expect(email.picks).toEqual([
      { kind: 'drag', type: 'email', label: 'Email', event: email.event, handle: email.item },
    ]);
    expect(email.item.hasAttribute('data-drags')).toBe(true);
    for (const [mode, name] of [
      ['overlay', 'Email'],
      ['docked', 'Captcha'],
      ['docked', 'Group'],
    ] as const) {
      const other = await press(mode, name);
      expect(other.picks, `${name} ${mode}`).toEqual([]);
      expect(other.item.hasAttribute('data-drags')).toBe(false);
    }
  });

  it('folds to a rail and opens again, and over the canvas has a heading and Close', async () => {
    const docked = await paletteOf();
    docked.root.querySelector('.head uui-button')?.dispatchEvent(new Event('click'));
    const rail = await paletteOf({ mode: 'rail' });
    expect(all(rail.element, '.item')).toEqual([]);
    rail.root.querySelector('uui-button')?.dispatchEvent(new Event('click'));
    const overlay = await paletteOf({ mode: 'overlay' });
    expect(overlay.root.querySelector('h2')?.classList.contains('visually-hidden')).toBe(false);
    expect(text(overlay.root.querySelector('h2'))).toBe('Add a field');
    overlay.root.querySelector('.head uui-button')?.dispatchEvent(new Event('click'));
    expect([...docked.picks, ...rail.picks, ...overlay.picks]).toEqual([
      { kind: 'fold' },
      { kind: 'unfold' },
      { kind: 'close' },
    ]);
  });
});

describe('the Paste questions dialog', () => {
  async function dialogOf(questions: string) {
    const element = new PasteQuestionsModalElement();
    element.data = { types: { text: 'Short text', email: 'Email', yesno: 'Yes or no', textarea: 'Long text' } };
    document.body.append(element);
    await settle(element);
    const area = must(shadow(element).querySelector<HTMLTextAreaElement>('textarea#questions'), 'the questions');
    area.value = questions;
    area.dispatchEvent(new Event('input'));
    await settle(element);
    return { element, root: shadow(element) };
  }

  it('labels its box and gives it its help, then lists the fields it will add with their types and how many', async () => {
    const { root } = await dialogOf(
      'Your name\nEmail address\n\nDo you need step-free access?\nAnything else we should know?',
    );
    expect(
      [...root.querySelectorAll('tbody tr')].map((row) => [...row.querySelectorAll('td')].map((cell) => text(cell))),
    ).toEqual([
      ['Your name', 'Short text'],
      ['Email address', 'Email'],
      ['Do you need step-free access?', 'Yes or no'],
      ['Anything else we should know?', 'Long text'],
    ]);
    const area = must(root.querySelector('textarea'), 'the questions');
    expect([text(root.querySelector(`label[for="${area.id}"]`)), area.getAttribute('aria-describedby')]).toEqual([
      'Your questions',
      'questions-help',
    ]);
    expect(text(root.querySelector('#questions-help'))).toMatch(/^One question per line/);
    const add = must(root.querySelector('uui-button[look="primary"]'), 'Add');
    expect([add.getAttribute('label'), add.hasAttribute('disabled')]).toEqual(['Add 4 fields', false]);
  });

  it('adds nothing without a question, and a guess the site does not offer is a short text', async () => {
    const empty = await dialogOf('  \n ');
    expect(empty.root.querySelector('table')).toBeNull();
    expect(empty.root.querySelector('uui-button[look="primary"]')?.hasAttribute('disabled')).toBe(true);
    const phone = await dialogOf('Phone number');
    expect(text(phone.root.querySelector('tbody td:last-child'))).toBe('Short text');
  });
});
