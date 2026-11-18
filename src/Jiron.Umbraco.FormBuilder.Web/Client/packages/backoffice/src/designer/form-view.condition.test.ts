// The form's buttons where they apply (ROADMAP 22.3, ADR-0258): Preview, Save draft and Publish... on Design, Settings
// and Actions by a condition that follows the address, and no footer drawn on Entries, Insights and History.
import { UmbObjectState } from '@umbraco-cms/backoffice/observable-api';
import { afterEach, beforeAll, describe, expect, it } from 'vitest';
import { must, registerEnglish, settle, shadow, testHost } from '../testing';
import { FORM_VIEW_CONDITION, FORM_WORKSPACE_CONTEXT } from './constants';
import { FormViewCondition } from './form-view.condition';
import { FormWorkspaceEditorElement } from './form-workspace-editor.element';
import { changesForm } from './form-views';

const FORM = '/umbraco/section/jfb-forms/workspace/jfb-form/edit/3f2504e0-4f89-41d3-9a0c-0305e82c3301';

/** Moves to an address as Umbraco's router does, which says so when it is done. */
const go = (path: string) => {
  history.replaceState(null, '', path);
  window.dispatchEvent(new Event('navigationend'));
};

beforeAll(registerEnglish);

afterEach(() => {
  history.replaceState(null, '', '/');
  document.body.replaceChildren();
});

describe('the views that change the form', () => {
  it('are Design, Settings and Actions', () => {
    expect(['design', 'settings', 'actions', 'entries', 'insights', 'history'].map(changesForm)).toEqual([
      true,
      true,
      true,
      false,
      false,
      false,
    ]);
  });
});

describe('the condition of the footer actions', () => {
  it('permits them on a view that changes the form, follows each move, and stops listening once gone', () => {
    go(FORM);
    const heard: boolean[] = [];
    const condition = new FormViewCondition(testHost(), {
      host: testHost(),
      manifest: { alias: 'x', type: 'workspaceAction', name: 'x' } as never,
      config: { alias: FORM_VIEW_CONDITION },
      onChange: (permitted: boolean) => heard.push(permitted),
    } as never);
    expect(condition.permitted).toBe(true);
    go(`${FORM}/view/entries`);
    expect(condition.permitted).toBe(false);
    go(`${FORM}/view/settings`);
    expect(condition.permitted).toBe(true);
    condition.destroy();
    go(`${FORM}/view/history`);
    expect(heard.at(-1)).toBe(true);
  });
});

describe("the form's workspace editor", () => {
  /** The editor at an address, over a form as the workspace holds it: saved and open to the user unless said. */
  async function editorAt(path: string, held: { forbidden?: boolean; missing?: boolean; isNew?: boolean } = {}) {
    go(path);
    const host = testHost();
    const flag = (on: boolean) => ({ isOn: new UmbObjectState(on).asObservable() });
    host.provideContext(FORM_WORKSPACE_CONTEXT, {
      state: new UmbObjectState(undefined).asObservable(),
      form: new UmbObjectState(undefined).asObservable(),
      loading: flag(false),
      forbidden: flag(held.forbidden ?? false),
      data: new UmbObjectState(held.missing ? undefined : { form: {} }).asObservable(),
      isNew: new UmbObjectState(held.isNew ?? false).asObservable(),
      getHostElement: () => host,
      getEntityType: () => 'jfb-form',
      getUnique: () => '3f2504e0-4f89-41d3-9a0c-0305e82c3301',
      unique: new UmbObjectState('3f2504e0-4f89-41d3-9a0c-0305e82c3301').asObservable(),
    } as never);
    const editor = new FormWorkspaceEditorElement();
    host.append(editor);
    await settle(editor);
    const workspace = () =>
      must(
        shadow(editor).querySelector<HTMLElement & { enforceNoFooter: boolean }>('umb-workspace-editor'),
        'the editor',
      );
    return { editor, workspace };
  }

  it('draws the footer on Design, and none on Entries, Insights and History, as the address moves', async () => {
    const { editor, workspace } = await editorAt(FORM);
    expect(workspace().enforceNoFooter).toBe(false);
    for (const view of ['entries', 'insights', 'history']) {
      go(`${FORM}/view/${view}`);
      await settle(editor);
      expect(workspace().enforceNoFooter, view).toBe(true);
    }
    go(`${FORM}/view/actions`);
    await settle(editor);
    expect(workspace().enforceNoFooter).toBe(false);
  });

  it("keeps the form's menu where the form is saved, and leaves it out of a form not saved yet", async () => {
    const { editor } = await editorAt(`${FORM}/view/entries`);
    expect(shadow(editor).querySelector('umb-workspace-entity-action-menu')?.getAttribute('slot')).toBe('action-menu');
    const unsaved = await editorAt(FORM, { isNew: true });
    expect(shadow(unsaved.editor).querySelector('umb-workspace-entity-action-menu')).toBeNull();
  });

  it("says in Umbraco's words that a form can't be opened, or isn't there, and keeps the editor in place", async () => {
    const open = await editorAt(FORM);
    const said = (editor: FormWorkspaceEditorElement) =>
      ['umb-entity-detail-forbidden', 'umb-entity-detail-not-found'].map(
        (name) => shadow(editor).querySelector(name)?.getAttribute('entity-type') ?? null,
      );
    expect(said(open.editor)).toEqual([null, null]);
    expect(open.workspace().classList.contains('hide')).toBe(false);

    const forbidden = await editorAt(FORM, { forbidden: true });
    expect(said(forbidden.editor)).toEqual(['jfb-form', null]);

    const missing = await editorAt(FORM, { missing: true });
    expect(said(missing.editor)).toEqual([null, 'jfb-form']);
    // In the DOM still, out of sight, as Umbraco's own keeps it for an action that is still running.
    expect(missing.workspace().classList.contains('hide')).toBe(true);
  });
});
