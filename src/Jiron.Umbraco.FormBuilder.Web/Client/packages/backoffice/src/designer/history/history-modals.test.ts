// History's sidebar and dialog (ROADMAP 10.3a, ADR-0196): two versions compared in their parts, each under its
// heading, sentences then a table of both values with each version's name in its column and on each value for a narrow
// sidebar; no differences, a version that can't be read, and a read that fails with Try again; Close. A comment's
// dialog starts with the comment, keeps what is written without its spaces, and Cancel keeps nothing.
import { UmbObjectState } from '@umbraco-cms/backoffice/observable-api';
import { afterEach, beforeAll, describe, expect, it, vi } from 'vitest';
import { all, must, registerEnglish, settle, shadow, text } from '../../testing';
import { CommentModalElement } from './comment.modal.element';
import { ComparisonModalElement } from './comparison.modal.element';
import type { ComparisonModalData, ComparisonRead } from './comparison.modal.token';
import { loadHistoryWords } from './history-words';

/** Umbraco's modal context as far as the modals use it: the value they keep, and whether they submit or reject. */
function modalContext() {
  const state = new UmbObjectState<object>({});
  return {
    value: state.asObservable(),
    getValue: () => state.getValue(),
    setValue: (next: object) => state.setValue(next),
    updateValue: (partial: object) => state.update(partial as never),
    submit: vi.fn(),
    reject: vi.fn(),
  };
}

async function comparisonOf(read: () => Promise<ComparisonRead>, more: Partial<ComparisonModalData> = {}) {
  const element = new ComparisonModalElement();
  const context = modalContext();
  element.modalContext = context as never;
  element.data = {
    headline: 'Version 12 and version 14',
    olderName: 'Version 12',
    newerName: 'Version 14',
    read,
    ...more,
  };
  document.body.append(element);
  await settle(element);
  return { element, root: shadow(element), context };
}

beforeAll(async () => {
  registerEnglish();
  await loadHistoryWords('en');
});

afterEach(() => {
  document.body.replaceChildren();
});

describe('a version compared with the current draft (ROADMAP 10.3b, ADR-0198)', () => {
  // The footer's buttons, in one box that wraps them where they don't fit a row.
  const buttons = (element: HTMLElement) =>
    all<HTMLElement>(element, '[slot="actions"].actions uui-button').map((button) => [
      button.getAttribute('label'),
      button.getAttribute('look'),
    ]);

  it('offers both restores beside Close once the comparison is read, and answers the one chosen', async () => {
    let answer: (read: ComparisonRead) => void = () => undefined;
    const { element, context } = await comparisonOf(() => new Promise((done) => (answer = done)), {
      restorable: true,
      publishable: true,
    });
    // A restore acts on what the comparison shows, so it waits for it.
    expect(buttons(element)).toEqual([['Close', 'default']]);

    answer({ kind: 'compared', parts: [] });
    await settle(element);
    expect(buttons(element)).toEqual([
      ['Close', 'default'],
      ['Restore and publish' + String.fromCharCode(0x2026), 'secondary'],
      ['Restore as a new draft', 'primary'],
    ]);
    all<HTMLElement>(element, '.actions uui-button')[1]?.click();
    await settle(element);
    expect(context.submit).toHaveBeenCalled();
    expect(context.getValue()).toEqual({ restore: 'publish' });
  });

  it('offers no Restore and publish... for the live version, which visitors get already', async () => {
    const { element } = await comparisonOf(async () => ({ kind: 'compared', parts: [] }), { restorable: true });
    expect(buttons(element)).toEqual([
      ['Close', 'default'],
      ['Restore as a new draft', 'primary'],
    ]);
  });

  it("offers no restore of a version that can't be read, nor where two versions are compared", async () => {
    const unreadable = await comparisonOf(async () => ({ kind: 'unreadable', name: 'Version 12' }), {
      restorable: true,
    });
    expect(buttons(unreadable.element)).toEqual([['Close', 'default']]);
    document.body.replaceChildren();
    const two = await comparisonOf(async () => ({ kind: 'compared', parts: [] }));
    expect(buttons(two.element)).toEqual([['Close', 'default']]);
  });

  it('says that changes not saved yet are left out, which a restore saves first', async () => {
    const unsaved = await comparisonOf(async () => ({ kind: 'compared', parts: [] }), {
      restorable: true,
      unsaved: true,
    });
    expect(text(unsaved.root.querySelector('.unsaved'))).toBe(
      "Changes not saved yet aren't in this comparison. A restore saves them first, so they stay in History.",
    );
    document.body.replaceChildren();
    // Two ticked versions restore nothing, so nothing is saved first either.
    const two = await comparisonOf(async () => ({ kind: 'compared', parts: [] }), { unsaved: true });
    expect(two.root.querySelector('.unsaved')).toBeNull();
  });
});

describe('two versions compared', () => {
  it('say each part under its heading: what was added or removed, then what changed with both values', async () => {
    const { element, root } = await comparisonOf(async () => ({
      kind: 'compared',
      parts: [
        {
          name: 'fields',
          lines: ['Field added: Order number'],
          rows: [{ what: 'Email address, Required', older: 'No', newer: 'Yes' }],
        },
        { name: 'rules', lines: ['Rule added to Order number: Required when Topic is Sales'], rows: [] },
      ],
    }));
    await vi.waitFor(() => expect(root.querySelector('.part')).not.toBeNull());
    expect(root.querySelector('umb-body-layout')?.getAttribute('headline')).toBe('Version 12 and version 14');
    expect(all(element, '.part h4').map((heading) => text(heading))).toEqual(['Fields', 'Rules']);
    expect(all(element, '.part li').map((line) => text(line))).toEqual([
      'Field added: Order number',
      'Rule added to Order number: Required when Topic is Sales',
    ]);
    expect(all(element, 'thead th').map((cell) => text(cell))).toEqual(['Changed', 'Version 12', 'Version 14']);
    expect(all(element, 'tbody tr').map((row) => [...row.children].map((cell) => text(cell)))).toEqual([
      ['Email address, Required', 'No', 'Yes'],
    ]);
    // Where the sidebar is narrow each value says its version.
    expect(all(element, 'tbody td').map((cell) => cell.getAttribute('data-side'))).toEqual([
      'Version 12',
      'Version 14',
    ]);
    // Stacked, the rows keep their table's roles, so a cell is still read with its version's header.
    expect(
      all(element, 'tbody tr').map((row) => [
        row.getAttribute('role'),
        ...[...row.children].map((cell) => cell.getAttribute('role')),
      ]),
    ).toEqual([['row', 'rowheader', 'cell', 'cell']]);
    // No landmarks of its own in a sidebar read top to bottom.
    expect(all(element, 'section')).toHaveLength(0);
  });

  it('say when and by whom for one version compared with the draft, and when nothing differs', async () => {
    const { root } = await comparisonOf(async () => ({ kind: 'compared', parts: [] }), {
      made: 'Today, 09:12 \u00b7 Ana Novak',
    });
    await vi.waitFor(() => expect(text(root.querySelector('[role="status"]'))).toBe('No differences.'));
    expect(text(root.querySelector('.made'))).toBe('Today, 09:12 \u00b7 Ana Novak');
  });

  it("say a version that can't be read, and a read that fails, which Try again reads again", async () => {
    const reads: ComparisonRead[] = [{ kind: 'failed' }, { kind: 'unreadable', name: 'Version 12' }];
    const read = vi.fn(async () => must(reads.shift(), 'a read'));
    const { root, element, context } = await comparisonOf(read);
    await vi.waitFor(() =>
      expect(text(root.querySelector('[role="alert"]'))).toBe("The versions couldn't be compared."),
    );
    must(root.querySelector<HTMLElement>('[role="alert"] uui-button'), 'Try again').click();
    await vi.waitFor(() =>
      expect(text(root.querySelector('[role="status"]'))).toBe("Version 12 can't be read, so it can't be compared."),
    );
    expect(read).toHaveBeenCalledTimes(2);
    must(root.querySelector<HTMLElement>('.actions uui-button'), 'Close').click();
    await settle(element);
    expect(context.reject).toHaveBeenCalled();
  });
});

describe("a version's comment", () => {
  it('starts with the comment, keeps what is written without its spaces, and Cancel keeps nothing', async () => {
    const element = new CommentModalElement();
    const context = modalContext();
    element.modalContext = context as never;
    element.data = { name: 'Version 12', comment: 'Autumn' };
    document.body.append(element);
    await settle(element);
    const root = shadow(element);
    expect(root.querySelector('uui-dialog-layout')?.getAttribute('headline')).toBe('Comment on Version 12');
    const box = must(root.querySelector<HTMLTextAreaElement>('uui-textarea'), 'the comment');
    expect([box.value, box.getAttribute('maxlength'), text(root.querySelector('.help'))]).toEqual([
      'Autumn',
      '500',
      'At most 500 characters. Saving it empty removes the comment.',
    ]);
    // The box's own name holds the help, which a description from outside its shadow root couldn't give it.
    expect(box.getAttribute('label')).toBe('Comment. At most 500 characters. Saving it empty removes the comment.');
    expect(box.hasAttribute('aria-describedby')).toBe(false);
    box.value = '  Shorter for the autumn  ';
    box.dispatchEvent(new Event('input'));
    const [cancel, save] = all<HTMLElement>(element, 'uui-button[slot="actions"]');
    expect(save?.getAttribute('label')).toBe('Save');
    save?.click();
    await settle(element);
    expect(context.submit).toHaveBeenCalled();
    expect(context.getValue()).toEqual({ comment: 'Shorter for the autumn' });
    cancel?.dispatchEvent(new Event('click'));
    expect(context.reject).toHaveBeenCalled();
  });
});
