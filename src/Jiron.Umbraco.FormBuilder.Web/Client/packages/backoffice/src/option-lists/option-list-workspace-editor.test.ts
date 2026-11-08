// An option list's workspace header (ROADMAP 12.1, IA 7, ADR-0214): the name with its alias in Umbraco's box, an alias
// that breaks the rule made from what was typed; and the forms that use the list, a button that opens them as links with
// a note for a draft and the recycle bin and a count of those the user may not open, a quiet line when none does, and
// nothing for a new list. The back arrow goes to the list screen.
import { UmbObjectState } from '@umbraco-cms/backoffice/observable-api';
import { afterEach, beforeAll, describe, expect, it, vi } from 'vitest';
import type { LibraryUsersResponseModel } from '../api';
import { must, registerEnglish, settle, shadow, testHost, text } from '../testing';
import { OPTION_LIST_WORKSPACE_CONTEXT } from './constants';
import { OptionListWorkspaceEditorElement } from './option-list-workspace-editor.element';
import { loadOptionListWords } from './option-list-words';

function contextOf(isNew: boolean, users: LibraryUsersResponseModel | undefined) {
  const name = new UmbObjectState('Sizes');
  const alias = new UmbObjectState('sizes');
  return {
    getEntityType: () => 'jfb-option-list',
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

async function headerOf(isNew: boolean, users?: LibraryUsersResponseModel) {
  const context = contextOf(isNew, users);
  const host = testHost();
  host.provideContext(OPTION_LIST_WORKSPACE_CONTEXT, context as never);
  const element = new OptionListWorkspaceEditorElement();
  host.append(element);
  await settle(element);
  return { element, root: shadow(element), context };
}

beforeAll(async () => {
  registerEnglish();
  await loadOptionListWords('en');
});

afterEach(() => {
  document.body.replaceChildren();
});

describe("an option list's workspace header", () => {
  it("names the list in Umbraco's name-and-alias box, its back arrow to the list screen", async () => {
    const { root } = await headerOf(false, { forms: [], others: 0 });
    const box = must(root.querySelector<HTMLElement & { value: string; alias: string }>('umb-input-with-alias'), 'box');
    expect([box.getAttribute('label'), box.getAttribute('placeholder'), box.value, box.alias]).toEqual([
      'Name of the option list',
      'Name the list',
      'Sizes',
      'sizes',
    ]);
    expect(box.getAttribute('alias-pattern')).toBe('^[a-z][A-Za-z0-9]{0,63}$');
    expect(box.hasAttribute('auto-generate-alias')).toBe(false);
    const frame = must(
      root.querySelector<HTMLElement & { backPath: string }>('umb-entity-detail-workspace-editor'),
      'frame',
    );
    expect(frame.backPath).toBe('section/jfb-forms/workspace/jfb-option-lists');
  });

  it('makes the alias from the name while the list is new, and from what was typed when it breaks the rule', async () => {
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
    // A new list shows no forms that use it.
    expect(root.querySelector('.used')).toBeNull();
  });

  it('opens the forms that use the list as links, with their notes and those the user may not open', async () => {
    const { root, context } = await headerOf(false, {
      forms: [
        { key: 'f1', name: 'Order', inDraft: true, inLive: true, isTrashed: false, uses: 2 },
        { key: 'f2', name: 'Returns', inDraft: true, inLive: false, isTrashed: false, uses: 1 },
        { key: 'f3', name: 'Old order', inDraft: true, inLive: false, isTrashed: true, uses: 1 },
      ],
      others: 2,
    });
    const button = must(root.querySelector<HTMLElement>('uui-button.used'), 'Used by');
    expect([button.getAttribute('label'), button.getAttribute('popovertarget')]).toEqual([
      'Used by 5 forms',
      'option-list-users',
    ]);
    // Dispatched: the test DOM drops a synthetic click() on Umbraco's button here.
    button.dispatchEvent(new Event('click'));
    expect(context.refreshUsers).toHaveBeenCalledOnce();
    const popover = must(root.querySelector('uui-popover-container#option-list-users'), 'the popover');
    expect(text(popover.querySelector('h2'))).toBe('Forms that use this list');
    expect([...popover.querySelectorAll('li')].map(text)).toEqual([
      'Order',
      'Returns in its draft, not yet live',
      'Old order in its draft, not yet live, in the recycle bin',
    ]);
    expect(popover.querySelector('a')?.getAttribute('href')).toBe('section/jfb-forms/workspace/jfb-form/edit/f1');
    expect(text(popover.querySelector('.others'))).toBe("2 more forms that you can't open");
  });

  it('says quietly that no form uses the list', async () => {
    const { root } = await headerOf(false, { forms: [], others: 0 });
    expect(text(root.querySelector('span.used'))).toBe('Not used by any form');
    expect(root.querySelector('uui-button.used')).toBeNull();
  });
});
