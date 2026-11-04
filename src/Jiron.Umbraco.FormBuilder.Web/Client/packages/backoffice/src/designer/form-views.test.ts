// The moves between the form's views (ROADMAP 9.3a, 9.5f): the address of a view, the view an address shows, Design at
// the form's own address, and no move to the view shown, which Umbraco's router would build anew.
import { afterEach, describe, expect, it, vi } from 'vitest';
import { FIRST_VIEW, goToView, shownView, viewPath } from './form-views';

const FORM = '/umbraco/section/jfb-forms/workspace/jfb-form/edit/3f2504e0-4f89-41d3-9a0c-0305e82c3301';

afterEach(() => {
  history.replaceState(null, '', '/');
  vi.restoreAllMocks();
});

describe("the form's views", () => {
  it('put a view in place of the one an address names, or after the form', () => {
    expect(viewPath(FORM, 'settings')).toBe(`${FORM}/view/settings`);
    expect(viewPath(`${FORM}/`, 'settings')).toBe(`${FORM}/view/settings`);
    expect(viewPath(`${FORM}/view/design`, 'actions')).toBe(`${FORM}/view/actions`);
  });

  it("read the view an address shows, Design at the form's own address", () => {
    expect(shownView(`${FORM}/view/settings`)).toBe('settings');
    expect(shownView(`${FORM}/view/settings/`)).toBe('settings');
    expect(shownView(FORM)).toBe(FIRST_VIEW);
    expect(FIRST_VIEW).toBe('design');
  });

  it('move to another view, and not to the one shown', () => {
    const moves: string[] = [];
    vi.spyOn(window, 'dispatchEvent').mockImplementation((event) => {
      if (event.type === 'popstate') moves.push(window.location.pathname);
      return true;
    });
    history.replaceState(null, '', FORM);
    goToView('design');
    expect(moves).toEqual([]);
    goToView('settings');
    expect(moves).toEqual([`${FORM}/view/settings`]);
    goToView('settings');
    goToView('design');
    expect(moves).toEqual([`${FORM}/view/settings`, `${FORM}/view/design`]);
  });
});
