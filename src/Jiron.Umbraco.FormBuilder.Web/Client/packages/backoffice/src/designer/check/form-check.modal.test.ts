// The form check's sidebar (ROADMAP 9.5f, IA 5.1, ADR-0181): errors first, which block publishing, then warnings, then
// suggestions, each with its element, its sentence and Fix it where the designer leads somewhere; a form without
// problems says so; it follows the check while open, and offers Try again after a check that gave no answer.
import { UmbObjectState } from '@umbraco-cms/backoffice/observable-api';
import { afterEach, beforeAll, describe, expect, it, vi } from 'vitest';
import { all, must, registerEnglish, settle, shadow, text } from '../../testing';
import type { DesignDefinition } from '../definition';
import { builtInTypes } from '../preview/fixtures';
import type { CheckIssue } from './form-check';
import { UNCHECKED, type CheckState } from './form-checker';
import { FormCheckModalElement } from './form-check.modal.element';
import type { FormCheckModalData, FormCheckModalValue } from './form-check.modal.token';

const DEFINITION: DesignDefinition = {
  pages: [
    {
      key: 'p1',
      groups: [
        {
          key: 'g1',
          fields: [
            {
              key: 'email',
              type: 'email',
              alias: 'email',
              label: { '': 'Email address' },
              rules: [{ key: 'r', effect: 'require', conditions: [] }],
            },
          ],
        },
      ],
    },
  ],
};

const issue = (severity: CheckIssue['severity'], code: string, path: string): CheckIssue => ({
  severity,
  code,
  path,
  message: `The server's ${code}.`,
});

const FOUND: readonly CheckIssue[] = [
  issue('error', 'unknown_path', "$.pages[0].groups[0].fields[0].label['']"),
  issue('error', 'cycle', '$'),
  issue('warning', 'required_can_hide', '$.pages[0].groups[0].fields[0].rules[0]'),
  issue('hint', 'consent_missing', '$.pages[0].groups[0].fields[0]'),
];

function modalContext() {
  const state = new UmbObjectState<Partial<FormCheckModalValue>>({});
  return {
    value: state.asObservable(),
    setValue: (next: FormCheckModalValue) => state.setValue(next),
    updateValue: (partial: Partial<FormCheckModalValue>) => state.update(partial as never),
    getValue: () => state.getValue(),
    submit: vi.fn(),
    reject: vi.fn(),
  };
}

async function sidebarOf(check: CheckState, extra: Partial<FormCheckModalData> = {}) {
  const modal = modalContext();
  const state = new UmbObjectState<CheckState>(check);
  const element = new FormCheckModalElement();
  element.modalContext = modal as never;
  element.data = { check: state.asObservable(), retry: vi.fn(), types: builtInTypes, culture: 'en-US', ...extra };
  document.body.append(element);
  await settle(element);
  return { element, modal, state, root: shadow(element) };
}

beforeAll(registerEnglish);

afterEach(() => {
  document.body.replaceChildren();
});

describe('the form check', () => {
  it('lists errors first, which block publishing, then warnings, then suggestions, each with its element', async () => {
    const opened = vi.fn();
    const closed = vi.fn();
    const { element, root } = await sidebarOf(
      { status: 'checked', issues: FOUND, definition: DEFINITION },
      { opened, closed },
    );
    expect(opened).toHaveBeenCalledOnce();
    expect(root.querySelector('umb-body-layout')?.getAttribute('headline')).toBe('Form check');
    const boxes = all<HTMLElement>(element, 'uui-box');
    expect(boxes.map((box) => box.getAttribute('headline'))).toEqual(['2 errors', '1 warning', '1 suggestion']);
    expect(text(boxes[0]?.querySelector('[slot="header-actions"]'))).toBe('Blocks publishing');
    expect(boxes[1]?.querySelector('[slot="header-actions"]')).toBeNull();
    const rows = boxes.map((box) =>
      [...box.querySelectorAll('li')].map((row) => [
        text(row.querySelector('.name')),
        text(row.querySelector('.said')),
      ]),
    );
    expect(rows).toEqual([
      [
        ['Email address', 'A merge tag in "Label" names nothing, so it shows nothing.'],
        ['The form', 'Its rules or calculations depend on each other in a circle.'],
      ],
      [['Email address', expect.stringContaining('This rule requires the field')]],
      [['Email address', expect.stringContaining('has no consent field')]],
    ]);
    element.remove();
    expect(closed).toHaveBeenCalledOnce();
  });

  it('offers Fix it where the designer leads somewhere, named for its element, and answers with where', async () => {
    const { element, modal } = await sidebarOf({ status: 'checked', issues: FOUND, definition: DEFINITION });
    const rows = all<HTMLElement>(element, 'li');
    const fixes = rows.map((row) => row.querySelector('uui-button')?.getAttribute('label') ?? null);
    expect(fixes).toEqual(['Fix it: Email address', null, 'Fix it: Email address', 'Fix it: Email address']);
    rows[2]?.querySelector<HTMLElement>('uui-button')?.click();
    await settle(element);
    expect(modal.getValue()).toEqual({ fix: { kind: 'rule', key: 'email', rule: 'r' } });
    expect(modal.submit).toHaveBeenCalledOnce();
  });

  it('says a form without problems can be published', async () => {
    const { element, root } = await sidebarOf({ status: 'checked', issues: [], definition: DEFINITION });
    expect(all(element, 'uui-box')).toEqual([]);
    expect(text(root.querySelector('.clear'))).toBe('No problems. The form can be published.');
  });

  it('follows the check while it is open: waiting for its first answer, then the answer, then the next', async () => {
    const { element, root, state } = await sidebarOf({ ...UNCHECKED, status: 'checking' });
    expect(text(root.querySelector('[role="status"]'))).toBe('Checking the form\u2026');
    state.setValue({ status: 'checked', issues: [], definition: DEFINITION });
    await settle(element);
    expect(root.querySelector('.clear')).not.toBeNull();
    // The line for a check of the latest changes sits beside Close, there and empty while none runs, so nothing moves.
    const again = must(root.querySelector('[slot="actions"] [role="status"]'), 'the line beside Close');
    expect(text(again)).toBe('');
    state.setValue({ status: 'checking', issues: [], definition: DEFINITION });
    await settle(element);
    expect(text(again)).toBe('Checking the latest changes\u2026');
    expect(root.querySelector('.clear')).not.toBeNull();
  });

  it('says when the check gave no answer, and checks again on Try again', async () => {
    const retry = vi.fn();
    const { element, root } = await sidebarOf({ status: 'failed', issues: [], definition: null }, { retry });
    const failed = must(root.querySelector<HTMLElement>('[role="alert"]'), 'the failure');
    expect(text(failed.querySelector('p'))).toBe("The form check didn't answer. Try again in a moment.");
    failed.querySelector<HTMLElement>('uui-button')?.click();
    await settle(element);
    expect(retry).toHaveBeenCalledOnce();
  });

  it('closes on Close', async () => {
    const { element, root, modal } = await sidebarOf({ status: 'checked', issues: [], definition: DEFINITION });
    root.querySelector<HTMLElement>('[slot="actions"] uui-button')?.click();
    await settle(element);
    expect(modal.reject).toHaveBeenCalledOnce();
  });
});
