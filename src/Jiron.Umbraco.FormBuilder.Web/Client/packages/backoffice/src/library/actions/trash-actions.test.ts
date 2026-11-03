// What a form's delete for good asks (docs/ux/copy.md, Confirmations; ADR-0166): what goes with it, with numbers, and the
// pages that still pick it; without the numbers, which only who may view or delete its entries gets, it asks nothing.
import type { UmbLocalizationController } from '@umbraco-cms/backoffice/localization-api';
import { describe, expect, it } from 'vitest';
import { deleteFormQuestion } from './trash-actions';

const localize = {
  term: (key: string, ...args: unknown[]) => (args.length ? `${key}(${args.join(',')})` : key),
} as unknown as UmbLocalizationController;
const nowhere = { count: 0, names: [], more: 0 };

describe("a form's delete for good", () => {
  it('names its entries and files, and the pages that still pick it', () => {
    const question = deleteFormQuestion(
      localize,
      { folders: 0, forms: 1, entries: 3, files: 1, left: 0 },
      { count: 6, names: ['Home', 'Contact'], more: 4 },
    );

    expect(question).toEqual({
      content: 'jfbLibrary_deleteFormText(3,1) jfbLibrary_deleteFormInUse(6,Home, Contact,4) jfbLibrary_cannotUndo',
      confirmLabel: 'jfbLibrary_deleteFormConfirm(3)',
    });
  });

  it('leaves out the pages when none picks it', () => {
    const question = deleteFormQuestion(localize, { folders: 0, forms: 1, entries: 0, files: 0, left: 0 }, nowhere);

    expect(question).toEqual({
      content: 'jfbLibrary_deleteFormText(0,0) jfbLibrary_cannotUndo',
      confirmLabel: 'jfbLibrary_deleteFormConfirm(0)',
    });
  });

  it('asks nothing of who gets no numbers, and says the permission it needs', () => {
    const question = deleteFormQuestion(
      localize,
      { folders: 0, forms: 1, entries: null, files: null, left: 0 },
      nowhere,
    );

    expect(question).toEqual({
      refusal: 'jfbLibrary_missingPermission(jfbLibrary_permission_deleteEntries)',
    });
  });
});
