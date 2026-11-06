// Review both versions (ROADMAP 10.3c, IA 5.7, ADR-0200): each difference under what it is about, both sides' values
// under the versions' names, and a choice that starts on the side that made it; one both made is marked and waits, as
// does one whose maker isn't known, unmarked. Save says once how many still wait, marks each in words and goes to the
// first; Save answers the differences taken from the user's version.
import { UmbObjectState } from '@umbraco-cms/backoffice/observable-api';
import { afterEach, beforeAll, describe, expect, it, vi } from 'vitest';
import { all, must, registerEnglish, settle, shadow, text } from '../../testing';
import { loadHistoryWords } from '../history/history-words';
import { ReviewModalElement } from './review.modal.element';
import type { ReviewModalData } from './review.modal.token';

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

const DATA: ReviewModalData = {
  theirs: "Ana Novak's version",
  differences: [
    {
      id: 'field:f1',
      title: 'Field "Email address"',
      rows: [{ what: 'Label', older: 'E-mail', newer: 'Email address' }],
      start: 'yours',
    },
    {
      id: 'field:f8',
      title: 'Field "Company"',
      rows: [{ what: 'In the form', older: 'Yes', newer: 'No' }],
      start: 'theirs',
    },
    {
      id: 'settings:appearance',
      title: 'Settings: Appearance',
      rows: [{ what: 'Theme', older: 'Calm', newer: 'Bold' }],
      both: true,
    },
  ],
};

async function reviewOf(data: ReviewModalData = DATA) {
  const element = new ReviewModalElement();
  const context = modalContext();
  element.modalContext = context as never;
  element.data = data;
  document.body.append(element);
  await settle(element);
  const root = shadow(element);
  const sections = () => all<HTMLElement>(element, 'section.difference');
  const groups = () => all<HTMLInputElement>(element, 'uui-radio-group');
  const button = (label: string) => must(root.querySelector<HTMLElement>(`uui-button[label="${label}"]`), label);
  return { element, root, context, sections, groups, button };
}

beforeAll(async () => {
  registerEnglish();
  await loadHistoryWords('en');
});

afterEach(() => {
  document.body.replaceChildren();
});

describe('Review both versions', () => {
  it("shows each difference under what it is about, both sides' values under the versions' names", async () => {
    const { element, root, sections } = await reviewOf();
    expect(root.querySelector('umb-body-layout')?.getAttribute('headline')).toBe('Review both versions');
    expect(sections().map((section) => text(section.querySelector('h4')))).toEqual([
      'Field "Email address"',
      'Field "Company"',
      'Settings: Appearance',
    ]);
    const first = must(sections()[0], 'first');
    expect([...first.querySelectorAll('thead th')].map((cell) => text(cell))).toEqual([
      'Changed',
      "Ana Novak's version",
      'Your version',
    ]);
    expect([...(first.querySelector('tbody tr')?.children ?? [])].map((cell) => text(cell))).toEqual([
      'Label',
      'E-mail',
      'Email address',
    ]);
    // Each choice is named by what it is about, its options by the versions.
    const group = must(first.querySelector('uui-radio-group'), 'choice');
    expect(group.getAttribute('aria-labelledby')).toBe(must(first.querySelector('h4'), 'title').id);
    expect(
      all(element, 'uui-radio')
        .slice(0, 2)
        .map((radio) => radio.getAttribute('label')),
    ).toEqual(["Ana Novak's version", 'Your version']);
  });

  it('starts each choice on the side that made it, and marks one both made, which waits', async () => {
    const { root, sections, groups } = await reviewOf();
    expect(groups().map((group) => group.value)).toEqual(['yours', 'theirs', '']);
    expect(sections().map((section) => text(section.querySelector('uui-tag')))).toEqual(['', '', 'Changed in both']);
    expect(text(root.querySelector('p'))).toBe(
      'For each difference, choose the version to keep, then save. Where only one of you changed something, that version is chosen already.',
    );
  });

  it("says nothing of who changed a difference whose maker isn't known, and nothing is chosen for it", async () => {
    const { root, sections, groups } = await reviewOf({
      theirs: 'The version saved meanwhile',
      differences: [
        { id: 'field:f1', title: 'Field "Email"', rows: [{ what: 'Label', older: 'E-mail', newer: 'Email' }] },
      ],
    });
    expect(groups().map((group) => group.value)).toEqual(['']);
    expect(sections()[0]?.querySelector('uui-tag')).toBeNull();
    expect(text(root.querySelector('p'))).toBe('For each difference, choose the version to keep, then save.');
  });

  it('says how many choices wait, goes to the first, and saves nothing until each is chosen', async () => {
    const { root, context, groups, button } = await reviewOf();
    button('Save').click();
    await vi.waitFor(() =>
      expect(text(root.querySelector('[role="alert"]'))).toBe('One difference still needs a choice.'),
    );
    // Each that waits says so in words, which its choice is described by, and has the focus first.
    const missing = must(root.querySelector<HTMLElement>('section.missing'), 'missing');
    expect(text(missing.querySelector('h4'))).toBe('Settings: Appearance');
    const wait = must(missing.querySelector('.wait'), 'wait');
    expect(text(wait)).toBe('Choose a version');
    const waiting = must(groups()[2], 'third');
    expect(waiting.getAttribute('aria-describedby')).toBe(wait.id);
    expect(root.activeElement === waiting || waiting.matches(':focus-within')).toBe(true);
    expect(context.submit).not.toHaveBeenCalled();

    waiting.value = 'theirs';
    waiting.dispatchEvent(new Event('change'));
    await settle();
    // A choice ends the alert, so it isn't said again, and the words go with the wait.
    expect(root.querySelector('[role="alert"]')).toBeNull();
    expect(root.querySelector('.wait')).toBeNull();
    expect(waiting.hasAttribute('aria-describedby')).toBe(false);
    const changed = must(groups()[1], 'second');
    changed.value = 'yours';
    changed.dispatchEvent(new Event('change'));
    await settle();
    button('Save').click();
    await settle();
    expect(context.submit).toHaveBeenCalledOnce();
    expect(context.getValue()).toEqual({ fromYours: ['field:f1', 'field:f8'] });
  });

  it('says once how many wait: a choice ends the alert, and each that still waits keeps its words', async () => {
    const waiting = (id: string, title: string) => ({
      id,
      title,
      rows: [{ what: 'Theme', older: 'Calm', newer: 'Bold' }],
      both: true,
    });
    const { root, groups, button } = await reviewOf({
      theirs: "Ana Novak's version",
      differences: [
        waiting('settings:appearance', 'Settings: Appearance'),
        waiting('settings:privacy', 'Settings: Privacy'),
      ],
    });
    button('Save').click();
    await vi.waitFor(() =>
      expect(text(root.querySelector('[role="alert"]'))).toBe('2 differences still need a choice.'),
    );
    const first = must(groups()[0], 'first');
    first.value = 'yours';
    first.dispatchEvent(new Event('change'));
    await settle();
    expect(root.querySelector('[role="alert"]')).toBeNull();
    expect([...root.querySelectorAll('.wait')].map((one) => text(one))).toEqual(['Choose a version']);
  });

  it('says when the two versions do not differ, and Save keeps the draft as it is', async () => {
    const { root, context, button } = await reviewOf({ theirs: "Ana Novak's version", differences: [] });
    expect(text(root.querySelector('p'))).toBe("The two versions don't differ. Saving keeps your draft as it is.");
    button('Save').click();
    await settle();
    expect(context.getValue()).toEqual({ fromYours: [] });
  });

  it('closes with Cancel, keeping nothing', async () => {
    const { element, context, button } = await reviewOf();
    button('Cancel').click();
    await settle(element);
    expect(context.reject).toHaveBeenCalled();
    expect(context.submit).not.toHaveBeenCalled();
  });
});
