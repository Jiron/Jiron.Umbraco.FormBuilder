// The outcomes as the Settings view edits them (ROADMAP 10.1b, ADR-0185): their kinds, which visitors can get, an
// outcome edited and written back as the server keeps it, and where a new one goes.
import { describe, expect, it } from 'vitest';
import type { DesignDefinition, DesignRule } from '../definition';
import {
  DEFAULT_DELAY,
  drawnSetting,
  editOf,
  excerptOf,
  holdsAlways,
  kindOf,
  leavesDefault,
  messageLine,
  outcomeOf,
  outcomesOf,
  placeOfNew,
  reachOf,
  settingAtPath,
  targetOf,
  withOutcome,
  withOutcomes,
  type DesignOutcome,
} from './outcome-model';

const KEY = '60000000-0000-4000-8000-000000000001';

const rule = (
  conditions: readonly unknown[] = [{ field: 'topic', op: 'eq', value: 'press' }],
  more = {},
): DesignRule => ({
  key: '40000000-0000-4000-8000-000000000008',
  effect: 'use',
  enabled: true,
  match: 'all',
  conditions,
  ...more,
});

const message: DesignOutcome = {
  key: KEY,
  kind: 'message',
  message: { '': '<p>Thanks, {{ field.name }}!</p>', de: '<p>Danke, {{ field.name }}!</p>' },
  showAnswers: true,
};

const redirect: DesignOutcome = {
  key: '60000000-0000-4000-8000-000000000002',
  kind: 'redirect',
  showAnswers: false,
  redirect: { content: '73000000-0000-4000-8000-000000000001', delaySeconds: 0 },
  rules: [rule()],
};

const messageThenRedirect: DesignOutcome = {
  key: '60000000-0000-4000-8000-000000000003',
  kind: 'message',
  message: { '': '<p>Thanks!</p>' },
  showAnswers: false,
  redirect: { url: '/events/thanks?ref={{ query.ref | url }}', delaySeconds: 3 },
};

describe('the outcomes', () => {
  it('are read in their order, and left out where none is left', () => {
    const definition: DesignDefinition = { pages: [], outcomes: [message, 'not one', redirect] };
    expect(outcomesOf(definition).map((one) => one.key)).toEqual([message.key, redirect.key]);
    expect(outcomesOf({ pages: [] })).toEqual([]);
    expect(withOutcomes(definition, [])).toEqual({ pages: [] });
    expect(withOutcomes({ pages: [] }, [redirect])).toEqual({ pages: [], outcomes: [redirect] });
  });

  it('give a message, a redirect, or a message and then a redirect', () => {
    expect([kindOf(message), kindOf(redirect), kindOf(messageThenRedirect)]).toEqual([
      'message',
      'redirect',
      'messageThenRedirect',
    ]);
    // A message outcome with a redirect that leads nowhere yet is still one that redirects, which the check then says.
    expect(kindOf({ key: KEY, redirect: { delaySeconds: 5 } })).toBe('messageThenRedirect');
    expect([targetOf(redirect), targetOf(messageThenRedirect), targetOf(message)]).toEqual([
      { kind: 'page', key: '73000000-0000-4000-8000-000000000001' },
      { kind: 'address', url: '/events/thanks?ref={{ query.ref | url }}' },
      { kind: 'none' },
    ]);
  });

  it("say a message's text in a language as one line, nothing where visitors get the default", () => {
    expect([messageLine(message, 'de-DE'), messageLine(message, 'en-US')]).toEqual([
      'Danke, {{ field.name }}!',
      'Thanks, {{ field.name }}!',
    ]);
    expect(messageLine({ key: KEY }, '')).toBe('');
  });
});

describe('which outcome visitors get', () => {
  it('is the first whose rule matches, the first without one for every other visitor, and none after that', () => {
    const always = { ...message, key: 'a' };
    expect(reachOf([redirect, always, { ...message, key: 'b' }, { ...redirect, key: 'c' }])).toEqual([
      'ruled',
      'otherwise',
      'never',
      'never',
    ]);
    expect(reachOf([always, redirect])).toEqual(['always', 'never']);
    expect([leavesDefault([redirect]), leavesDefault([]), leavesDefault([redirect, always])]).toEqual([
      true,
      true,
      false,
    ]);
  });

  it('counts a rule switched off, or one of "all of" nothing, as none, as the runtime does', () => {
    expect(holdsAlways(rule([], { match: 'all' }))).toBe(true);
    expect(holdsAlways(rule(undefined, { enabled: false }))).toBe(true);
    expect(holdsAlways(rule([], { match: 'any' }))).toBe(false);
    expect(holdsAlways(rule())).toBe(false);
    expect(reachOf([{ ...redirect, rules: [rule(undefined, { enabled: false })] }, message])).toEqual([
      'always',
      'never',
    ]);
  });
});

describe('an outcome edited', () => {
  it('opens with every setting it has, a new one as a message that says nothing yet', () => {
    expect(editOf(messageThenRedirect)).toEqual({
      kind: 'messageThenRedirect',
      message: { '': '<p>Thanks!</p>' },
      showAnswers: false,
      leadsTo: 'address',
      content: null,
      url: '/events/thanks?ref={{ query.ref | url }}',
      delay: 3,
      rule: null,
    });
    expect(editOf(redirect)).toMatchObject({ kind: 'redirect', leadsTo: 'page', delay: DEFAULT_DELAY, rule: rule() });
    expect(editOf(null)).toEqual({
      kind: 'message',
      message: null,
      showAnswers: false,
      leadsTo: 'page',
      content: null,
      url: '',
      delay: DEFAULT_DELAY,
      rule: null,
    });
  });

  it('is written back with what its kind uses, as the server keeps it, and the rest as it came', () => {
    const odd = { ...messageThenRedirect, note: 'kept', redirect: { url: '/x', delaySeconds: 3, other: 1 } };
    const edit = editOf(odd);
    expect(outcomeOf({ ...edit, kind: 'redirect' }, odd, odd.key)).toEqual({
      key: odd.key,
      note: 'kept',
      kind: 'redirect',
      redirect: { other: 1, url: '/x', delaySeconds: 0 },
    });
    expect(outcomeOf({ ...edit, kind: 'message' }, odd, odd.key)).toEqual({
      key: odd.key,
      note: 'kept',
      kind: 'message',
      message: { '': '<p>Thanks!</p>' },
      showAnswers: false,
    });
    // A page chosen in place of the address leads there, and only there.
    expect(outcomeOf({ ...edit, leadsTo: 'page', content: 'page-key', delay: 10 }, odd, odd.key).redirect).toEqual({
      other: 1,
      content: 'page-key',
      delaySeconds: 10,
    });
    // A message emptied gives visitors the default one.
    expect(outcomeOf({ ...editOf(message), message: {} }, message, message.key)).toEqual({
      key: message.key,
      kind: 'message',
      showAnswers: true,
    });
  });

  it('keeps its rule in the place of the one it had, other rules as they were', () => {
    const run: DesignRule = { key: 'r', effect: 'run', conditions: [] };
    const base = { ...message, rules: [run, rule()] };
    const changed = rule([{ field: 'topic', op: 'eq', value: 'sales' }]);
    expect(outcomeOf({ ...editOf(base), rule: changed }, base, base.key).rules).toEqual([run, changed]);
    expect(outcomeOf({ ...editOf(base), rule: null }, base, base.key).rules).toEqual([run]);
    expect(outcomeOf({ ...editOf(message), rule: changed }, message, message.key).rules).toEqual([changed]);
    expect('rules' in outcomeOf(editOf(message), message, message.key)).toBe(false);
  });
});

describe('a new outcome', () => {
  it('goes at the end, but one with a rule before the first outcome for every visitor', () => {
    expect(placeOfNew([redirect, message], rule())).toBe(1);
    // After the first for every visitor it would never be used, whatever follows.
    expect(placeOfNew([message, redirect], rule())).toBe(0);
    expect(placeOfNew([redirect, message], null)).toBe(2);
    expect(placeOfNew([redirect], rule())).toBe(1);
    expect(placeOfNew([], rule())).toBe(0);
    // A rule that holds for everyone is as none.
    expect(placeOfNew([redirect, message], rule([]))).toBe(2);
  });

  it('takes the place of the outcome with its key, or comes in where it goes', () => {
    const changed = { ...message, showAnswers: false };
    expect(withOutcome([redirect, message], changed)).toEqual([redirect, changed]);
    expect(withOutcome([redirect, message], messageThenRedirect, 1)).toEqual([redirect, messageThenRedirect, message]);
  });
});

describe("an outcome's settings", () => {
  it('are named by the paths within the outcome, the outcome itself by what it gives', () => {
    const cases: [readonly (string | number)[], string][] = [
      [[], 'kind'],
      [['kind'], 'kind'],
      [['message', 'de-DE'], 'message'],
      [['showAnswers'], 'showAnswers'],
      [['redirect'], 'leadsTo'],
      [['redirect', 'content'], 'page'],
      [['redirect', 'url'], 'address'],
      [['redirect', 'delaySeconds'], 'delay'],
      [['rules', 0, 'conditions', 1], 'rule'],
    ];
    for (const [steps, setting] of cases) expect(settingAtPath(steps), steps.join('.')).toBe(setting);
  });

  it('show at the choice that hides them where their box is not drawn', () => {
    const redirectTo = (leadsTo: 'page' | 'address') => ({ ...editOf(null), kind: 'redirect' as const, leadsTo });
    expect(drawnSetting('message', redirectTo('page'))).toBe('kind');
    expect(drawnSetting('showAnswers', editOf(null))).toBe('showAnswers');
    expect(drawnSetting('address', editOf(null))).toBe('kind');
    expect(drawnSetting('address', redirectTo('page'))).toBe('leadsTo');
    expect(drawnSetting('address', redirectTo('address'))).toBe('address');
    expect(drawnSetting('page', redirectTo('address'))).toBe('leadsTo');
    expect(drawnSetting('page', redirectTo('page'))).toBe('page');
    expect(drawnSetting('leadsTo', redirectTo('address'))).toBe('leadsTo');
    expect(drawnSetting('delay', redirectTo('page'))).toBe('kind');
    expect(drawnSetting('delay', { ...editOf(null), kind: 'messageThenRedirect' })).toBe('delay');
    expect(drawnSetting('rule', redirectTo('page'))).toBe('rule');
  });
});

describe("an outcome's name", () => {
  it('says a short text whole and cuts a long one at a word, with an ellipsis', () => {
    const ellipsis = String.fromCharCode(0x2026);
    expect(excerptOf('  Thanks!  ')).toBe('Thanks!');
    expect(excerptOf('one two three four', 10)).toBe(`one two${ellipsis}`);
    // A word longer than half of it is cut where it ends.
    expect(excerptOf('abcdefghijklmnop', 10)).toBe(`abcdefghij${ellipsis}`);
    expect(excerptOf('x'.repeat(60))).toBe('x'.repeat(60));
  });
});
