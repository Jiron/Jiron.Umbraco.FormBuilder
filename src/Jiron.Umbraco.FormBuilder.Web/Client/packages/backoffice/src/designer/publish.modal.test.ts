// The publish dialog with the form check's result (ROADMAP 9.5f, IA 5.7, ADR-0181): no problems said once; errors that
// block Publish, each with Fix it; warnings that don't; Publish waiting for the check's answer, and left to the
// server's own check when the check gives none; the comment the version keeps.
import { UmbObjectState } from '@umbraco-cms/backoffice/observable-api';
import { afterEach, beforeAll, describe, expect, it, vi } from 'vitest';
import { all, must, registerEnglish, settle, shadow, text } from '../testing';
import type { CheckIssue } from './check/form-check';
import type { CheckState } from './check/form-checker';
import type { DesignDefinition } from './definition';
import { builtInTypes } from './preview/fixtures';
import { MOST_LISTED, PublishModalElement } from './publish.modal.element';
import type { PublishModalValue } from './publish.modal.token';

const DEFINITION: DesignDefinition = {
  pages: [
    {
      key: 'p1',
      groups: [
        { key: 'g1', fields: [{ key: 'email', type: 'email', alias: 'email', label: { '': 'Email address' } }] },
      ],
    },
  ],
};

const LABEL = "$.pages[0].groups[0].fields[0].label['']";

const issue = (severity: CheckIssue['severity'], code: string, path = LABEL): CheckIssue => ({
  severity,
  code,
  path,
  message: code,
});

function modalContext() {
  const state = new UmbObjectState<Partial<PublishModalValue>>({});
  return {
    value: state.asObservable(),
    setValue: (next: PublishModalValue) => state.setValue(next),
    updateValue: (partial: Partial<PublishModalValue>) => state.update(partial as never),
    getValue: () => state.getValue(),
    submit: vi.fn(),
    reject: vi.fn(),
  };
}

async function dialogOf(check: CheckState) {
  const modal = modalContext();
  const state = new UmbObjectState<CheckState>(check);
  const element = new PublishModalElement();
  const closed = vi.fn();
  element.modalContext = modal as never;
  element.data = { name: 'Contact us', check: state.asObservable(), types: builtInTypes, culture: '', closed };
  document.body.append(element);
  await settle(element);
  const root = shadow(element);
  const publish = must(
    all<HTMLElement>(element, 'uui-button[slot="actions"]').find(
      (button) => button.getAttribute('label') === 'Publish',
    ),
    'Publish',
  );
  return { element, modal, state, root, publish, closed };
}

const checked = (issues: readonly CheckIssue[]): CheckState => ({ status: 'checked', issues, definition: DEFINITION });

beforeAll(registerEnglish);

afterEach(() => {
  document.body.replaceChildren();
});

describe('the publish dialog', () => {
  it('says once that the form check found no problems, and publishes with the comment', async () => {
    const { element, root, modal, publish } = await dialogOf(checked([issue('hint', 'consent_missing', '$')]));
    expect(root.querySelector('uui-dialog-layout')?.getAttribute('headline')).toBe('Publish Contact us?');
    expect(all(element, '.result .note').map((note) => text(note))).toEqual(['The form check found no problems.']);
    expect(publish.hasAttribute('disabled')).toBe(false);
    const comment = must(root.querySelector<HTMLTextAreaElement>('uui-textarea'), 'the comment');
    comment.value = 'Typo fixed';
    comment.dispatchEvent(new Event('input'));
    publish.click();
    await settle(element);
    expect(modal.getValue()).toEqual({ comment: 'Typo fixed' });
    expect(modal.submit).toHaveBeenCalledOnce();
  });

  it("blocks Publish while an error needs fixing, each with Fix it, and lists the warnings that don't block", async () => {
    const { element, modal, publish, closed } = await dialogOf(
      checked([
        issue('error', 'label_missing'),
        issue('warning', 'card_number_asked'),
        issue('hint', 'consent_missing'),
      ]),
    );
    expect(publish.hasAttribute('disabled')).toBe(true);
    // Nothing of a version that can't be published yet: no sentence about visitors, no comment for it.
    expect([
      shadow(element).querySelector('uui-textarea'),
      text(shadow(element).querySelector('uui-dialog-layout')),
    ]).toEqual([null, expect.not.stringContaining('Visitors get this version')]);
    const [errors, warnings] = all<HTMLElement>(element, '.result section');
    expect(errors?.getAttribute('role')).toBe('alert');
    expect(text(errors?.querySelector('.lead'))).toBe("This form can't be published yet: 1 error needs fixing.");
    expect(text(errors?.querySelector('li span'))).toBe(
      'Email address: This field has no label, which screen readers and the entries name it by.',
    );
    expect(text(warnings?.querySelector('.lead'))).toBe("It also has 1 warning, which doesn't block publishing.");
    expect(warnings?.querySelectorAll('li')).toHaveLength(1);
    errors?.querySelector<HTMLElement>('uui-button')?.click();
    await settle(element);
    expect(modal.getValue()).toEqual({
      fix: { kind: 'element', key: 'email', item: { kind: 'text', property: 'label' }, culture: '' },
    });
    expect(modal.submit).toHaveBeenCalledOnce();
    // The action makes the Fix it once the dialog is gone, which gives the focus back to Publish... as it closes.
    expect(closed).not.toHaveBeenCalled();
    element.remove();
    expect(closed).toHaveBeenCalledOnce();
  });

  it('publishes a form with warnings only, saying they do not block it', async () => {
    const { element, publish } = await dialogOf(checked([issue('warning', 'a'), issue('warning', 'b')]));
    expect(publish.hasAttribute('disabled')).toBe(false);
    expect(text(element.shadowRoot?.querySelector('.lead'))).toBe(
      "The form check found 2 warnings. They don't block publishing.",
    );
  });

  it('lists five problems of each weight, and says how many more the form check lists', async () => {
    const { element } = await dialogOf(checked(Array.from({ length: MOST_LISTED + 2 }, () => issue('error', 'cycle'))));
    expect(all(element, 'li')).toHaveLength(MOST_LISTED);
    expect(text(element.shadowRoot?.querySelector('.more'))).toBe('And 2 more, which the form check lists.');
  });

  it("waits for the check's answer, and leaves publishing to the server's check when it gives none", async () => {
    const { element, root, state, publish } = await dialogOf({ status: 'checking', issues: [], definition: null });
    // One region says the wait and then the answer, so a screen reader reads the answer out as it comes.
    const region = root.querySelector('[role="status"]');
    expect(text(region)).toBe('Checking the form\u2026');
    expect(publish.hasAttribute('disabled')).toBe(true);
    state.setValue({ status: 'failed', issues: [], definition: null });
    await settle(element);
    expect(publish.hasAttribute('disabled')).toBe(false);
    expect(root.querySelector('[role="status"]')).toBe(region);
    expect(text(region)).toBe("The form check didn't answer. Publishing checks the form again.");
    state.setValue({ status: 'checked', issues: [], definition: DEFINITION });
    await settle(element);
    expect(root.querySelector('[role="status"]')).toBe(region);
    expect(text(region)).toBe('The form check found no problems.');
  });
});
