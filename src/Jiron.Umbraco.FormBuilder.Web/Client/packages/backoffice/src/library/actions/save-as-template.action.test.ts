// Save as template's line on changes not saved yet (ADR-0217): only in the form's own workspace while it has some; from
// the tree, where no workspace is around the action, and in a workspace with nothing unsaved, the dialog leaves it out.
import { describe, expect, it } from 'vitest';
import { holdsUnsaved } from './save-as-template.action';

const workspace = (unique: string | null, unsaved: boolean) => ({
  getUnique: () => unique,
  getHasUnpersistedChanges: () => unsaved,
});

describe('the line on changes not saved yet', () => {
  it("shows in the form's own workspace while it has changes not saved yet", () => {
    expect(holdsUnsaved(workspace('f1', true), 'f1')).toBe(true);
  });

  it('stays out from the tree, with nothing unsaved, and for another form', () => {
    expect(holdsUnsaved(undefined, 'f1')).toBe(false);
    expect(holdsUnsaved(workspace('f1', false), 'f1')).toBe(false);
    expect(holdsUnsaved(workspace('f2', true), 'f1')).toBe(false);
  });
});
