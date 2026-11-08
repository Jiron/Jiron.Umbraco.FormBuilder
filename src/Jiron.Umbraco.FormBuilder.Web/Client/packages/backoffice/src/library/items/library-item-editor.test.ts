// A library item's workspace header (ROADMAP 12.1, IA 7, ADR-0214, ADR-0215), shown for an option list: the name with
// its alias in Umbraco's box, an alias that breaks the rule made from what was typed; and the forms that use the item, a
// button that opens them as links with a note for a draft and the recycle bin and a count of those the user may not
// open, a quiet line when none does, and nothing for a new item. The back arrow goes to the list screen. A kind that
// counts uses says them, and one whose alias is fixed locks it once saved.
import { UmbObjectState } from '@umbraco-cms/backoffice/observable-api';
import { afterEach, beforeAll, describe, expect, it, vi } from 'vitest';
import type { LibraryUsersResponseModel } from '../../api';
import { OPTION_LIST_KIND } from '../../option-lists/option-list-kind';
import { loadOptionListWords } from '../../option-lists/option-list-words';
import { must, registerEnglish, settle, shadow, testHost, text } from '../../testing';
import { LIBRARY_ITEM_WORKSPACE_CONTEXT, LibraryItemEditorElement } from './library-item-editor.element';
import type { LibraryKind } from './library-items';

function contextOf(isNew: boolean, users: LibraryUsersResponseModel | undefined, kind: LibraryKind) {
  const name = new UmbObjectState('Sizes');
  const alias = new UmbObjectState('sizes');
  return {
    libraryKind: kind,
    getEntityType: () => kind.entityType,
    getHostElement: () => document.body,
    name: name.asObservable(),
    alias: alias.asObservable(),
    isNew: new UmbObjectState(isNew).asObservable(),
    users: new UmbObjectState(users).asObservable(),
    setName: vi.fn((next: string) => name.setValue(next)),
    setAlias: vi.fn((next: string) => alias.setValue(next)),
    refreshUsers: vi.fn(async () => undefined),
  };
}

async function headerOf(isNew: boolean, users?: LibraryUsersResponseModel, kind: LibraryKind = OPTION_LIST_KIND) {
  const context = contextOf(isNew, users, kind);
  const host = testHost();
  host.provideContext(LIBRARY_ITEM_WORKSPACE_CONTEXT, context as never);
  const element = new LibraryItemEditorElement();
  host.append(element);
  await settle(element);
  return { element, root: shadow(element), context };
}

const NONE: LibraryUsersResponseModel = { forms: [], others: 0, otherUses: 0 };

beforeAll(async () => {
  registerEnglish();
  await loadOptionListWords('en');
});

afterEach(() => {
  document.body.replaceChildren();
});

describe("a library item's workspace header", () => {
  it("names the item in Umbraco's name-and-alias box, its back arrow to the list screen", async () => {
    const { root } = await headerOf(false, NONE);
    const box = must(
      root.querySelector<HTMLElement & { value: string; alias: string; aliasReadonly: boolean }>(
        'umb-input-with-alias',
      ),
      'box',
    );
    expect([box.getAttribute('label'), box.getAttribute('placeholder'), box.value, box.alias]).toEqual([
      'Name of the option list',
      'Name the list',
      'Sizes',
      'sizes',
    ]);
    expect(box.getAttribute('alias-pattern')).toBe('^[a-z][A-Za-z0-9]{0,63}$');
    expect(box.hasAttribute('auto-generate-alias')).toBe(false);
    expect(box.aliasReadonly).toBe(false);
    const frame = must(
      root.querySelector<HTMLElement & { backPath: string }>('umb-entity-detail-workspace-editor'),
      'frame',
    );
    expect(frame.backPath).toBe('section/jfb-forms/workspace/jfb-option-lists');
  });

  it('makes the alias from the name while the item is new, and from what was typed when it breaks the rule', async () => {
    const { root, context } = await headerOf(true);
    const box = must(root.querySelector<HTMLElement & { value: string; alias: string }>('umb-input-with-alias'), 'box');
    expect(box.hasAttribute('auto-generate-alias')).toBe(true);
    box.value = 'Shoe sizes';
    box.alias = 'Shoe-Sizes!';
    box.dispatchEvent(new Event('change'));
    expect(context.setName).toHaveBeenCalledWith('Shoe sizes');
    expect(context.setAlias).toHaveBeenCalledWith('shoeSizes');
    expect(box.alias).toBe('shoeSizes');
    box.alias = 'shoeSizesEu';
    box.dispatchEvent(new Event('change'));
    expect(context.setAlias).toHaveBeenLastCalledWith('shoeSizesEu');
    // A new item shows no forms that use it.
    expect(root.querySelector('.used')).toBeNull();
  });

  it('opens the forms that use the item as links, with their notes and those the user may not open', async () => {
    const { root, context } = await headerOf(false, {
      forms: [
        { key: 'f1', name: 'Order', inDraft: true, inLive: true, isTrashed: false, uses: 2 },
        { key: 'f2', name: 'Returns', inDraft: true, inLive: false, isTrashed: false, uses: 1 },
        { key: 'f3', name: 'Old order', inDraft: true, inLive: false, isTrashed: true, uses: 1 },
      ],
      others: 2,
      otherUses: 3,
    });
    const button = must(root.querySelector<HTMLElement>('uui-button.used'), 'Used by');
    expect([button.getAttribute('label'), button.getAttribute('popovertarget')]).toEqual([
      'Used by 5 forms',
      'library-item-users',
    ]);
    // Dispatched: the test DOM drops a synthetic click() on Umbraco's button here.
    button.dispatchEvent(new Event('click'));
    expect(context.refreshUsers).toHaveBeenCalledOnce();
    const popover = must(root.querySelector('uui-popover-container#library-item-users'), 'the popover');
    expect(text(popover.querySelector('h2'))).toBe('Forms that use this list');
    expect([...popover.querySelectorAll('li')].map(text)).toEqual([
      'Order',
      'Returns in its draft, not yet live',
      'Old order in its draft, not yet live, in the recycle bin',
    ]);
    expect(popover.querySelector('a')?.getAttribute('href')).toBe('section/jfb-forms/workspace/jfb-form/edit/f1');
    expect(text(popover.querySelector('.others'))).toBe("2 more forms that you can't open");
  });

  it('says quietly that no form uses the item', async () => {
    const { root } = await headerOf(false, NONE);
    expect(text(root.querySelector('span.used'))).toBe('Not used by any form');
    expect(root.querySelector('uui-button.used')).toBeNull();
  });

  it("counts a kind's uses, its forms' and the hidden ones', and locks an alias the kind keeps once saved", async () => {
    const words = { ...OPTION_LIST_KIND, counts: 'uses' as const, aliasFixed: true };
    const users = {
      forms: [{ key: 'f1', name: 'Order', inDraft: true, inLive: true, isTrashed: false, uses: 2 }],
      others: 1,
      otherUses: 3,
    };
    const saved = await headerOf(false, users, words);
    expect(must(saved.root.querySelector<HTMLElement>('uui-button.used'), 'Used by').getAttribute('label')).toBe(
      'Used by 5 forms',
    );
    expect(text(saved.root.querySelector('li'))).toContain('Order');
    expect(
      must(saved.root.querySelector<HTMLElement & { aliasReadonly: boolean }>('umb-input-with-alias'), 'box')
        .aliasReadonly,
    ).toBe(true);
    document.body.replaceChildren();
    const fresh = await headerOf(true, undefined, words);
    expect(
      must(fresh.root.querySelector<HTMLElement & { aliasReadonly: boolean }>('umb-input-with-alias'), 'box')
        .aliasReadonly,
    ).toBe(false);
  });
});
