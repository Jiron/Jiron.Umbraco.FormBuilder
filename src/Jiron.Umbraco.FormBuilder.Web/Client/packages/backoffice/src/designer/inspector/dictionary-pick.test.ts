import { UMB_CURRENT_USER_CONTEXT } from '@umbraco-cms/backoffice/current-user';
import { BehaviorSubject } from '@umbraco-cms/backoffice/external/rxjs';
import { afterEach, beforeAll, describe, expect, it, vi } from 'vitest';
import { must, registerEnglish, settle, shadow, testHost } from '../../testing';

// What Umbraco's dictionary picker answers, and what was asked of it.
const picker = vi.hoisted(() => ({
  opened: [] as unknown[],
  chosen: null as string | null,
  names: { 'item-1': 'Forms.Email', 'item-2': 'Not a key!' } as Record<string, string>,
}));

vi.mock('@umbraco-cms/backoffice/modal', async (original) => ({
  ...(await original<typeof import('@umbraco-cms/backoffice/modal')>()),
  umbOpenModal: async (_host: unknown, token: unknown, args?: unknown) => {
    picker.opened.push([String(token), args]);
    if (picker.chosen === null) throw new Error('closed');
    return { selection: [picker.chosen] };
  },
}));

vi.mock('@umbraco-cms/backoffice/dictionary', async (original) => ({
  ...(await original<typeof import('@umbraco-cms/backoffice/dictionary')>()),
  UmbDictionaryItemRepository: class {
    async requestItems(uniques: string[]) {
      return { data: uniques.map((unique) => ({ unique, name: picker.names[unique] ?? '' })) };
    }
  },
}));

const { DictionaryPickElement } = await import('./dictionary-pick.element');

beforeAll(() => registerEnglish());

afterEach(() => {
  document.body.replaceChildren();
  picker.opened = [];
  picker.chosen = null;
});

async function pickOf(sections: string[]) {
  const host = testHost();
  const allowed = new BehaviorSubject<string[] | undefined>(sections);
  host.provideContext(UMB_CURRENT_USER_CONTEXT, {
    allowedSections: allowed.asObservable(),
    getHostElement: () => host,
  } as never);
  const picked: string[] = [];
  const element = new DictionaryPickElement();
  element.label = 'Label';
  element.pick = (text) => picked.push(text);
  host.append(element);
  await settle(element);
  return { element, picked, allowed, button: () => shadow(element).querySelector('button') };
}

describe('Use a dictionary item', () => {
  it('shows only to a user whose groups open the Translation section, named for its box', async () => {
    const { element, allowed, button } = await pickOf(['Umb.Section.Content']);
    expect(button()).toBeNull();
    allowed.next(['Umb.Section.Content', 'Umb.Section.Translation']);
    await settle(element);
    expect(must(button(), 'the button').getAttribute('aria-label')).toBe('Use a dictionary item for Label');
    expect(must(button(), 'the button').title).toBe('Use a dictionary item');
  });

  it("writes the item chosen in Umbraco's picker as # and its key, and nothing when the picker closes", async () => {
    const { element, picked, button } = await pickOf(['Umb.Section.Translation']);
    must(button(), 'the button').click();
    await settle(element);
    expect(picked).toEqual([]);
    expect(picker.opened).toHaveLength(1);
    expect((picker.opened[0] as [string, { data: { multiple: boolean } }])[1].data.multiple).toBe(false);
    picker.chosen = 'item-1';
    must(button(), 'the button').click();
    await settle(element);
    expect(picked).toEqual(['#Forms.Email']);
  });

  it('writes nothing for an item whose key no text can name', async () => {
    const { element, picked, button } = await pickOf(['Umb.Section.Translation']);
    picker.chosen = 'item-2';
    must(button(), 'the button').click();
    await settle(element);
    expect(picked).toEqual([]);
  });
});
