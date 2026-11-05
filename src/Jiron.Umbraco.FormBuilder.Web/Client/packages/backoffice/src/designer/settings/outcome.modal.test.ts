// An outcome's sidebar (ROADMAP 10.1b, ADR-0185): what visitors get and each kind's settings, written back as the server
// keeps them; a value it can't keep refused at its box, until the box is gone; the form check's problems in the outcome
// under their setting, at once for those it had, others once left, another language's said by its name, Update kept
// back by an error here but not by a check that gives no answer, and kept once for two presses; the place in the list;
// the rule in the rules builder, and whether any visitor gets the outcome; and Delete outcome.
import { UmbObjectState } from '@umbraco-cms/backoffice/observable-api';
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest';
import { must, registerEnglish, shadow, text } from '../../testing';
import type { CheckIssue } from '../check/form-check';
import { CHECK_PAUSE } from '../check/form-checker';
import type { DesignDefinition, DesignRule } from '../definition';
import { builtInTypes } from '../preview/fixtures';
import type { DesignOutcome } from './outcome-model';
import type { OutcomeModalData, OutcomeModalValue } from './outcome.modal.token';

// The rules builder's answer, and what it was opened with.
const builder = vi.hoisted(() => ({ answer: undefined as unknown, data: [] as unknown[] }));

vi.mock('@umbraco-cms/backoffice/modal', async (original) => ({
  ...(await original<typeof import('@umbraco-cms/backoffice/modal')>()),
  umbOpenModal: async (_host: unknown, _token: unknown, args?: { data?: { closed?: () => void } }) => {
    builder.data.push(args?.data);
    args?.data?.closed?.();
    return builder.answer;
  },
}));

const { OutcomeModalElement } = await import('./outcome.modal.element');

/** Lets the sidebar's renders and asks settle, the clock moved on by nothing: its timers are the test's. */
async function settle(element?: { updateComplete: Promise<unknown> }) {
  for (let round = 0; round < 5; round++) {
    await vi.advanceTimersByTimeAsync(0);
    await element?.updateComplete;
  }
}

const DEFINITION: DesignDefinition = {
  pages: [
    {
      key: 'p1',
      groups: [{ key: 'g1', fields: [{ key: 'name', type: 'text', alias: 'name', label: { '': 'Name' } }] }],
    },
  ],
  outcomes: [{ key: 'o1', kind: 'message', showAnswers: false }],
};

const redirect: DesignOutcome = {
  key: 'o2',
  kind: 'redirect',
  showAnswers: false,
  redirect: { url: 'javascript:alert(1)', delaySeconds: 0 },
};

const RULE: DesignRule = {
  key: 'r1',
  effect: 'use',
  enabled: true,
  match: 'all',
  conditions: [{ field: 'name', op: 'equals', value: 'Mira' }],
};

/** What the check answers: each ask's definition, and the problems it finds in it; undefined for no answer. */
const checking = {
  asked: [] as DesignDefinition[],
  answer: (() => []) as (definition: DesignDefinition) => CheckIssue[] | undefined,
};

function modalContext() {
  const state = new UmbObjectState<Partial<OutcomeModalValue>>({});
  return {
    value: state.asObservable(),
    setValue: (next: OutcomeModalValue) => state.setValue(next),
    updateValue: (partial: Partial<OutcomeModalValue>) => state.update(partial),
    getValue: () => state.getValue(),
    submit: vi.fn(),
    reject: vi.fn(),
  };
}

async function sidebarOf(extra: Partial<OutcomeModalData> = {}) {
  const outcomes = (extra.definition ?? DEFINITION)['outcomes'] as DesignOutcome[];
  const data: OutcomeModalData = {
    outcome: outcomes[0] ?? null,
    number: 1,
    count: outcomes.length,
    definition: DEFINITION,
    form: 'form1',
    culture: 'en-US',
    languages: [{ culture: 'en-US', name: 'English', isDefault: true }],
    types: builtInTypes,
    defaultMessage: 'Thank you. We have received your message.',
    htmlLength: 100,
    check: async (definition) => {
      checking.asked.push(definition);
      return checking.answer(definition);
    },
    ...extra,
  };
  const modal = modalContext();
  const element = new OutcomeModalElement();
  element.modalContext = modal as never;
  element.data = data;
  document.body.append(element);
  await settle(element);
  // The first ask goes at once.
  await vi.advanceTimersByTimeAsync(0);
  await settle(element);
  const root = shadow(element);
  const box = <E extends HTMLElement = HTMLInputElement>(id: string) =>
    must(root.getElementById(id) as E | null, `#${id}`);
  const choose = async (name: string, value: string) => {
    const radio = must(root.querySelector<HTMLInputElement>(`input[name="${name}"][value="${value}"]`), value);
    radio.checked = true;
    radio.dispatchEvent(new Event('change'));
    await settle(element);
  };
  const type = async (id: string, value: string, leave = true) => {
    const input = box(id);
    input.value = value;
    input.dispatchEvent(new Event('input'));
    if (leave) input.dispatchEvent(new Event('change'));
    await settle(element);
  };
  const pause = async () => {
    await vi.advanceTimersByTimeAsync(CHECK_PAUSE);
    await settle(element);
  };
  const keep = async () => {
    must(root.querySelector<HTMLElement>('uui-button[look="primary"]'), 'Update').click();
    await vi.advanceTimersByTimeAsync(0);
    await settle(element);
  };
  const why = (id: string) => [...root.querySelectorAll(`[id^="${id}-why-"]`)].map((one) => text(one));
  return { element, modal, root, box, choose, type, pause, keep, why };
}

const problem = (path: string, code: string, message: string, severity: CheckIssue['severity'] = 'error') => ({
  severity,
  code,
  path,
  message,
});

beforeAll(registerEnglish);

beforeEach(() => {
  vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });
  checking.asked = [];
  checking.answer = () => [];
  builder.answer = undefined;
  builder.data = [];
});

afterEach(() => {
  document.body.replaceChildren();
  vi.useRealTimers();
});

describe("an outcome's sidebar", () => {
  it('opens a new outcome as a message that gives the default one, and adds it', async () => {
    const { root, modal, keep } = await sidebarOf({ outcome: null, number: 2 });
    expect(root.querySelector('umb-body-layout')?.getAttribute('headline')).toBe('New outcome');
    expect(root.querySelector<HTMLInputElement>('input[name="kind"]:checked')?.value).toBe('message');
    expect((root.getElementById('message') as unknown as { placeholder: string }).placeholder).toBe(
      'Thank you. We have received your message.',
    );
    expect(text(root.querySelector('#rule-words'))).toBe('Used when no earlier outcome applies.');
    // A new outcome has nothing to delete.
    expect(root.querySelector('uui-button[slot="footer-info"]')).toBeNull();
    expect(root.querySelector('uui-button[look="primary"]')?.getAttribute('label')).toBe('Add');
    await keep();
    const value = modal.getValue() as { outcome: DesignOutcome };
    expect(value.outcome).toEqual({ key: value.outcome.key, kind: 'message', showAnswers: false });
    expect(modal.submit).toHaveBeenCalled();
  });

  it("shows each kind's settings, and writes back only what the chosen kind uses", async () => {
    const { root, modal, choose, type, keep, box } = await sidebarOf();
    must(root.querySelector<HTMLInputElement>('#show-answers'), 'the switch').click();
    await choose('kind', 'redirect');
    expect([root.getElementById('message'), root.getElementById('show-answers')]).toEqual([null, null]);
    expect(root.querySelector<HTMLInputElement>('input[name="leads-to"]:checked')?.value).toBe('page');
    expect(root.getElementById('page')?.localName).toBe('umb-input-document');
    await choose('leads-to', 'address');
    await type('address', '  /press/thanks  ');
    await keep();
    expect((modal.getValue() as { outcome: DesignOutcome }).outcome).toEqual({
      key: 'o1',
      kind: 'redirect',
      redirect: { url: '/press/thanks', delaySeconds: 0 },
    });
    // A message and then a redirect keeps the message's settings and waits before it leaves.
    await choose('kind', 'messageThenRedirect');
    expect(box('delay').value).toBe('5');
    await type('delay', '12');
    await keep();
    expect((modal.getValue() as { outcome: DesignOutcome }).outcome).toEqual({
      key: 'o1',
      kind: 'message',
      showAnswers: true,
      redirect: { url: '/press/thanks', delaySeconds: 12 },
    });
  });

  it("refuses at its box what the server can't keep, once the box is left", async () => {
    const { element, root, choose, type, keep, why, box, modal } = await sidebarOf();
    await choose('kind', 'messageThenRedirect');
    await type('delay', '601', false);
    expect(why('delay')).toEqual([]);
    box('delay').dispatchEvent(new Event('change'));
    await settle(element);
    expect(why('delay')).toEqual(['Enter a whole number from 0 to 600.']);
    await choose('leads-to', 'address');
    await type('address', `/${'x'.repeat(2000)}`);
    expect(why('address')).toEqual(['Use 2000 characters at most.']);
    await keep();
    // Update is kept back, the focus on the first box that says why.
    expect([modal.submit.mock.calls.length, root.activeElement?.id]).toEqual([0, 'address']);
  });

  it("shows the check's problems it had at once, others once their box is left, and keeps back Update for an error", async () => {
    checking.answer = (definition) => {
      const outcomes = definition['outcomes'] as DesignOutcome[];
      const at = outcomes.findIndex((one) => one.key === 'o2');
      const url = outcomes[at]?.redirect?.url ?? '';
      return url.startsWith('javascript')
        ? [problem(`$.outcomes[${at}].redirect.url`, 'invalid_redirect', 'The address is no web address.')]
        : url === '/x'
          ? [problem(`$.outcomes[${at}].redirect.url`, 'invalid_redirect', 'Nothing there.', 'warning')]
          : [];
    };
    const definition = { ...DEFINITION, outcomes: [...(DEFINITION['outcomes'] as DesignOutcome[]), redirect] };
    const { root, modal, type, pause, keep, why } = await sidebarOf({
      definition,
      outcome: redirect,
      number: 2,
      focus: 'address',
    });
    await new Promise((done) => requestAnimationFrame(() => done(null)));
    expect(root.activeElement?.id).toBe('address');
    expect(why('address')).toEqual(['The address is no web address.']);
    expect(root.getElementById('address')?.getAttribute('aria-invalid')).toBe('true');
    // Asked about the draft as Update would make it: the outcome in its own place.
    expect((checking.asked[0]?.['outcomes'] as DesignOutcome[]).map((one) => one.key)).toEqual(['o1', 'o2']);
    await keep();
    expect(modal.submit).not.toHaveBeenCalled();
    // A warning doesn't keep it back.
    await type('address', '/x');
    await pause();
    expect(why('address')).toEqual(['Warning: Nothing there.']);
    await keep();
    expect((modal.getValue() as { outcome: DesignOutcome }).outcome.redirect).toEqual({ url: '/x', delaySeconds: 0 });
  });

  it('keeps an outcome when the check gives no answer', async () => {
    checking.answer = () => undefined;
    const { modal, keep } = await sidebarOf();
    await keep();
    expect(modal.submit).toHaveBeenCalled();
  });

  it('builds its rule in the rules builder, which says when visitors get it', async () => {
    builder.answer = { target: 'o1', rule: RULE };
    const { root, modal, keep } = await sidebarOf();
    const add = must(root.querySelector<HTMLElement>('#rule'), 'Add a rule');
    expect(text(add)).toBe('Add a rule');
    // The button's name is its words; what the rule says describes it.
    expect(add.getAttribute('aria-describedby')).toBe('rule-words');
    add.click();
    await vi.advanceTimersByTimeAsync(0);
    await settle();
    expect(builder.data[0]).toMatchObject({
      origin: 'o1',
      outcome: { number: 1 },
      effect: 'use',
      rule: null,
      form: 'form1',
    });
    expect(text(root.querySelector('#rule-words'))).toBe('Used when Name is "Mira"');
    expect(text(root.querySelector('#rule'))).toBe('Change rule');
    await keep();
    expect((modal.getValue() as { outcome: DesignOutcome }).outcome.rules).toEqual([RULE]);
    // The builder's Delete takes the rule away.
    builder.answer = { target: 'o1', rule: null };
    must(root.querySelector<HTMLElement>('#rule'), 'Change rule').click();
    await vi.advanceTimersByTimeAsync(0);
    await settle();
    expect(text(root.querySelector('#rule-words'))).toBe('Used when no earlier outcome applies.');
  });

  it('keeps an outcome whose refused box the kind took away', async () => {
    const { choose, type, keep, why, modal } = await sidebarOf();
    await choose('kind', 'messageThenRedirect');
    await type('delay', '601');
    expect(why('delay')).toEqual(['Enter a whole number from 0 to 600.']);
    await choose('kind', 'message');
    await keep();
    expect((modal.getValue() as { outcome: DesignOutcome }).outcome).toEqual({
      key: 'o1',
      kind: 'message',
      showAnswers: false,
    });
  });

  it('shows no problem of a box the last change took away while the check catches up', async () => {
    checking.answer = (definition) =>
      (definition['outcomes'] as DesignOutcome[])[0]?.kind === 'redirect'
        ? [problem('$.outcomes[0].redirect.url', 'invalid_redirect', 'The address is no web address.')]
        : [];
    const { choose, why } = await sidebarOf({
      definition: { ...DEFINITION, outcomes: [redirect] },
      outcome: redirect,
    });
    expect(why('address')).toEqual(['The address is no web address.']);
    // Asked again only after the pause: the answer is about the redirect, whose problem has no box now.
    await choose('kind', 'message');
    expect(why('kind')).toEqual([]);
  });

  it("names the language of a problem in another language's message, which doesn't keep Update back", async () => {
    checking.answer = () => [problem("$.outcomes[0].message['de-DE']", 'invalid_message', 'Too long.')];
    const { why, keep, modal } = await sidebarOf({
      languages: [
        { culture: 'en-US', name: 'English', isDefault: true },
        { culture: 'de-DE', name: 'German', isDefault: false },
      ],
    });
    expect(why('message')).toEqual(['German: Too long.']);
    await keep();
    expect(modal.submit).toHaveBeenCalled();
  });

  it('keeps the outcome once for two presses of Update', async () => {
    const { element, root, modal } = await sidebarOf();
    const update = must(root.querySelector<HTMLElement>('uui-button[look="primary"]'), 'Update');
    update.click();
    update.click();
    await vi.advanceTimersByTimeAsync(0);
    await settle(element);
    expect(modal.submit).toHaveBeenCalledTimes(1);
  });

  it('keeps nothing when the sidebar closes while Update waits for the check', async () => {
    let answer: (issues: CheckIssue[]) => void = () => undefined;
    const { element, root, modal } = await sidebarOf({ check: () => new Promise((done) => (answer = done)) });
    must(root.querySelector<HTMLElement>('uui-button[look="primary"]'), 'Update').click();
    await vi.advanceTimersByTimeAsync(0);
    await settle(element);
    element.remove();
    answer([]);
    await vi.advanceTimersByTimeAsync(0);
    await settle();
    expect(modal.submit).not.toHaveBeenCalled();
  });

  it('opens one rules builder for two presses of its button', async () => {
    const { element, root } = await sidebarOf();
    const rule = must(root.querySelector<HTMLButtonElement>('#rule'), 'Add a rule');
    rule.click();
    rule.click();
    await vi.advanceTimersByTimeAsync(0);
    await settle(element);
    expect(builder.data).toHaveLength(1);
  });

  it("puts the focus in the message's text for a Fix it there", async () => {
    const RichText = must(customElements.get('jfb-rich-text'), 'the rich text');
    // The editor's own focus needs a real browser's selection; the sidebar asks for it.
    const focus = vi.spyOn(RichText.prototype as HTMLElement, 'focus').mockImplementation(() => undefined);
    await sidebarOf({ focus: 'message' });
    await new Promise((done) => requestAnimationFrame(() => done(null)));
    expect(focus).toHaveBeenCalled();
    focus.mockRestore();
  });

  it('chooses its place in the list without dragging, and says where no visitor gets it', async () => {
    // The first outcome is for every visitor: none gets the second.
    const definition = { ...DEFINITION, outcomes: [...(DEFINITION['outcomes'] as DesignOutcome[]), redirect] };
    const { element, root, modal, keep } = await sidebarOf({ definition, outcome: redirect, number: 2, count: 2 });
    const place = must(root.querySelector<HTMLSelectElement>('#place'), 'the place');
    expect([[...place.options].map((one) => text(one)), place.value]).toEqual([['1 of 2', '2 of 2'], '1']);
    expect(text(root.querySelector('#rule-never'))).toBe('Never used: an outcome above is used for every visitor');
    expect(root.querySelector('#rule')?.getAttribute('aria-describedby')).toBe('rule-words rule-never');
    place.value = '0';
    place.dispatchEvent(new Event('change'));
    await settle(element);
    expect(root.querySelector('#rule-never')).toBeNull();
    await keep();
    expect((modal.getValue() as { at: number }).at).toBe(0);
    // An outcome alone in the list has no other place.
    document.body.replaceChildren();
    const alone = await sidebarOf();
    expect(alone.root.querySelector('#place')).toBeNull();
  });

  it('places a new outcome where it goes by its rule until a place is chosen', async () => {
    builder.answer = { target: 'new', rule: RULE };
    const { element, root, modal, keep } = await sidebarOf({ outcome: null, number: 2, count: 1 });
    const place = () => must(root.querySelector<HTMLSelectElement>('#place'), 'the place');
    // Without a rule it goes last, after the one for every visitor.
    expect(place().value).toBe('1');
    must(root.querySelector<HTMLElement>('#rule'), 'Add a rule').click();
    await vi.advanceTimersByTimeAsync(0);
    await settle(element);
    // The builder names it the new outcome; with a rule it goes before the one for every visitor.
    expect(builder.data[0]).toMatchObject({ outcome: { number: null } });
    expect(place().value).toBe('0');
    await keep();
    expect((modal.getValue() as { at: number }).at).toBe(0);
  });

  it('deletes the outcome', async () => {
    const { element, root, modal } = await sidebarOf();
    must(root.querySelector<HTMLElement>('uui-button[slot="footer-info"]'), 'Delete outcome').click();
    await settle(element);
    expect(modal.getValue()).toEqual({ deleted: true });
    expect(modal.submit).toHaveBeenCalled();
  });
});
