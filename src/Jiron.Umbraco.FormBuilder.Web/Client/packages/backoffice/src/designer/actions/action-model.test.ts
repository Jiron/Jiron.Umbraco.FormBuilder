// The actions as the Actions view edits them (ROADMAP 10.2a, ADR-0188): by trigger, moved within one, to another,
// copied; whether a trigger runs on the form; what each does in one line; the choices of what a new one does; and an
// action edited and written back as the server keeps it.
import { describe, expect, it } from 'vitest';
import type { ActionTypeResponseModel } from '../../api';
import type { DesignDefinition, DesignRule } from '../definition';
import {
  actionOf,
  actionsOf,
  approvesEachEntry,
  choicesOf,
  duplicated,
  editOf,
  inTrigger,
  isOn,
  keptIn,
  movedWithin,
  newEdit,
  placeOf,
  runRuleOf,
  settingAt,
  settingAtPath,
  summaryOf,
  triggerNeed,
  triggerOf,
  withAction,
  withActions,
  withChoice,
  withSettingAt,
  withTrigger,
  type DesignAction,
} from './action-model';

const rule: DesignRule = {
  key: '40000000-0000-4000-8000-000000000008',
  effect: 'run',
  enabled: true,
  match: 'all',
  conditions: [{ field: 'topic', op: 'equals', value: 'sales' }],
};

const action = (key: string, trigger?: string, more: Partial<DesignAction> = {}): DesignAction => ({
  key,
  type: 'email',
  name: `Action ${key}`,
  enabled: true,
  ...(trigger ? { trigger } : {}),
  stopOnFailure: false,
  ...more,
});

const keys = (actions: readonly DesignAction[]) => actions.map((one) => one.key);

const definitionWith = (settings: Record<string, unknown>): DesignDefinition => ({ pages: [], settings });

describe("the form's actions", () => {
  it("are the definition's, in their order, without what is no action, and left out where there are none", () => {
    const definition: DesignDefinition = {
      pages: [],
      actions: [action('a'), { type: 'email' }, 'odd', action('b')],
    };
    expect(keys(actionsOf(definition))).toEqual(['a', 'b']);
    expect(actionsOf({ pages: [] })).toEqual([]);
    expect(withActions(definition, [])).toEqual({ pages: [] });
    expect(withActions({ pages: [] }, [action('c')])['actions']).toEqual([action('c')]);
  });

  it('start on Submitted where they name no trigger the view knows, and run unless switched off', () => {
    expect(triggerOf(action('a'))).toBe('submitted');
    expect(triggerOf(action('a', 'approved'))).toBe('approved');
    expect(triggerOf(action('a', 'later'))).toBe('submitted');
    expect(isOn(action('a'))).toBe(true);
    expect(isOn({ ...action('a'), enabled: null })).toBe(true);
    expect(isOn({ ...action('a'), enabled: false })).toBe(false);
  });

  it("move within their trigger, the other triggers' keeping their places in the one list", () => {
    const actions = [action('s1'), action('a1', 'approved'), action('s2'), action('a2', 'approved'), action('s3')];
    expect(keys(inTrigger(actions, 'submitted'))).toEqual(['s1', 's2', 's3']);
    // The last of Submitted to the first place: the Approved actions stay where they were.
    expect(keys(movedWithin(actions, 'submitted', 2, 0))).toEqual(['s3', 'a1', 's1', 'a2', 's2']);
    expect(keys(movedWithin(actions, 'approved', 0, 1))).toEqual(['s1', 'a2', 's2', 'a1', 's3']);
    expect(keys(movedWithin(actions, 'rejected', 0, 1))).toEqual(keys(actions));
  });

  it('take a new action at the end of its trigger, and an action of the same key in its place', () => {
    const actions = [action('s1'), action('a1', 'approved'), action('s2')];
    expect(keys(withAction(actions, action('a2', 'approved')))).toEqual(['s1', 'a1', 'a2', 's2']);
    expect(keys(withAction(actions, action('r1', 'rejected')))).toEqual(['s1', 'a1', 's2', 'r1']);
    const renamed = withAction(actions, { ...action('a1', 'approved'), name: 'Renamed' });
    expect(renamed[1]?.name).toBe('Renamed');
  });

  it('move to another trigger at the end of its actions, and stay where they are for their own', () => {
    const actions = [action('s1'), action('a1', 'approved'), action('s2'), action('a2', 'approved')];
    const moved = withTrigger(actions, 's1', 'approved');
    expect(keys(moved)).toEqual(['a1', 's2', 'a2', 's1']);
    expect(moved.at(-1)?.trigger).toBe('approved');
    expect(withTrigger(actions, 's2', 'submitted')).toEqual(actions);
    expect(withTrigger(actions, 'gone', 'approved')).toEqual(actions);
  });

  it('are kept by Add and Update in their place, at the end of a new trigger, or at the place chosen', () => {
    const actions = [action('s1'), action('a1', 'approved'), action('s2'), action('a2', 'approved')];
    // Changed in its place; new at the end of its trigger.
    expect(keys(keptIn(actions, { ...action('s1'), name: 'Renamed' }))).toEqual(['s1', 'a1', 's2', 'a2']);
    expect(keys(keptIn(actions, action('s3')))).toEqual(['s1', 'a1', 's2', 's3', 'a2']);
    // Another trigger: at the end of its actions, carrying its new trigger.
    const moved = keptIn(actions, action('s1', 'approved'));
    expect(keys(moved)).toEqual(['a1', 's2', 'a2', 's1']);
    expect(moved.at(-1)?.trigger).toBe('approved');
    // A place chosen among its trigger's; the other triggers' keep theirs.
    expect(keys(keptIn(actions, action('s2'), 0))).toEqual(['s2', 'a1', 's1', 'a2']);
    expect(keys(keptIn(actions, action('s3'), 0))).toEqual(['s3', 'a1', 's1', 's2', 'a2']);
    expect(keys(keptIn(actions, action('s1', 'approved'), 1))).toEqual(['a1', 's2', 's1', 'a2']);
    expect(keys(keptIn(actions, action('s1'), 0))).toEqual(keys(actions));
  });

  it("say an action's place among a trigger's: where it is, or after the others", () => {
    const actions = [action('s1'), action('a1', 'approved'), action('s2')];
    expect(placeOf(actions, 's2', 'submitted')).toEqual({ at: 1, count: 2 });
    expect(placeOf(actions, 'new', 'submitted')).toEqual({ at: 2, count: 3 });
    // An action going to another trigger comes after that trigger's.
    expect(placeOf(actions, 's1', 'approved')).toEqual({ at: 1, count: 2 });
  });

  it('copy one right after it, with keys of its own and its name as a copy', () => {
    let next = 0;
    const newKey = () => `new-${++next}`;
    const actions = [action('a', undefined, { rules: [rule] }), action('b')];
    const copied = duplicated(actions, 'a', newKey, (name) => `${name} (copy)`);
    expect(keys(copied)).toEqual(['a', 'new-2', 'b']);
    expect(copied[1]?.name).toBe('Action a (copy)');
    expect(copied[1]?.rules?.[0]).toEqual({ ...rule, key: 'new-1' });
    // An action without a name stays without one; one that is gone copies nothing.
    expect(duplicated([action('c', undefined, { name: null })], 'c', newKey, (name) => name)[1]?.name).toBeNull();
    expect(duplicated(actions, 'gone', newKey, (name) => name)).toEqual(actions);
  });

  it('find their run rule', () => {
    expect(runRuleOf(action('a', undefined, { rules: [rule] }))).toEqual(rule);
    expect(runRuleOf(action('a'))).toBeNull();
  });
});

describe('the triggers on a form', () => {
  it('run Rejected only with manual approval and Verified only with email verification', () => {
    const plain = definitionWith({});
    expect(triggerNeed(plain, 'submitted')).toBeNull();
    expect(triggerNeed(plain, 'approved')).toBeNull();
    expect(triggerNeed(plain, 'rejected')).toBe('approval');
    expect(triggerNeed(plain, 'verified')).toBe('verification');
    const manual = definitionWith({ submission: { approval: 'manual', verification: { emailField: 'f' } } });
    expect(triggerNeed(manual, 'rejected')).toBeNull();
    expect(triggerNeed(manual, 'verified')).toBeNull();
    expect(triggerNeed({ pages: [] }, 'rejected')).toBe('approval');
  });

  it('approve each entry as it comes in without manual approval', () => {
    expect(approvesEachEntry(definitionWith({}))).toBe(true);
    expect(approvesEachEntry(definitionWith({ submission: { approval: 'manual' } }))).toBe(false);
  });
});

describe('what an action does, in one line', () => {
  const field = (key: string) => (key === 'f-email' ? 'Email address' : undefined);
  const of = (type: string, settings: Record<string, unknown>) => summaryOf({ key: 'k', type, settings }, field);

  it("says an email's recipients, the field it goes to, the form's creator, a choice that routes it, or no one yet", () => {
    expect(of('email', { to: ['sales@example.com', ' support@example.com'] })).toEqual({
      term: 'summary_emailTo',
      args: [['sales@example.com', 'support@example.com']],
    });
    expect(of('email', { toField: 'f-email' })).toEqual({ term: 'summary_emailToField', args: ['Email address'] });
    expect(of('email', { toField: 'f-gone', toCreator: true })).toEqual({ term: 'summary_emailToCreator', args: [] });
    expect(of('email', { toPrivacyTeam: true })).toEqual({ term: 'summary_emailToPrivacyTeam', args: [] });
    expect(of('email', { recipientMap: { field: 'f-topic', recipients: {} } })).toEqual({
      term: 'summary_emailRouted',
      args: [],
    });
    expect(of('email', {})).toEqual({ term: 'summary_emailNoOne', args: [] });
  });

  it("says where a webhook goes: Slack, Teams, an address's host, one the site's configuration names, or nowhere yet", () => {
    expect(of('webhook', { target: 'slack' })?.term).toBe('summary_slack');
    expect(of('webhook', { target: 'teams' })?.term).toBe('summary_teams');
    expect(of('webhook', { url: 'https://crm.example.com/hooks/forms' })).toEqual({
      term: 'summary_webhook',
      args: ['crm.example.com'],
    });
    // An address made of a merge tag is said as it is.
    expect(of('webhook', { url: '{{ query.hook }}' })?.args).toEqual(['{{ query.hook }}']);
    expect(of('webhook', { url: ' ' })?.term).toBe('summary_webhookNowhere');
    // A reference is named by its name, never read as a host (ADR-0191).
    expect(of('webhook', { url: '$JironFormBuilder:Secrets:CrmHook' })).toEqual({
      term: 'summary_webhookNamed',
      args: ['CrmHook'],
    });
  });

  it('says what a change of state and content do, and nothing of a type the view does not know', () => {
    expect(of('setState', {})?.term).toBe('summary_approve');
    expect(of('setState', { state: 'rejected' })?.term).toBe('summary_reject');
    expect(of('setState', { state: 'spam' })?.term).toBe('summary_spam');
    expect(of('crm', { anything: 1 })).toBeNull();
  });

  it('says what content is saved or published as, by the name the site gives its document type (ADR-0192)', () => {
    const named = (settings: Record<string, unknown>) =>
      summaryOf({ key: 'k', type: 'content', settings }, field, (alias) => (alias === 'story' ? 'Story' : undefined));
    expect(named({})).toEqual({ term: 'summary_contentNoType', args: [] });
    expect(named({ documentType: 'story' })).toEqual({ term: 'summary_contentSaves', args: ['Story'] });
    expect(named({ documentType: 'story', publish: true })).toEqual({
      term: 'summary_contentPublishes',
      args: ['Story'],
    });
    // A type the site no longer has is said by its alias.
    expect(named({ documentType: 'gone' })?.args).toEqual(['gone']);
    expect(of('content', { documentType: 'story' })?.args).toEqual(['story']);
  });
});

describe('what a new action does', () => {
  const type = (alias: string, presets: { name: string; settings: string }[]): ActionTypeResponseModel => ({
    alias,
    name: `#jfbActionTypes_${alias}`,
    icon: `icon-${alias}`,
    test: 'dryRun',
    settings: [],
    presets,
  });

  it("is every preset of every type, and a type without presets as itself, in the registry's order", () => {
    const choices = choicesOf([
      type('email', [
        { name: '#team', settings: '{"subject":{"":"New"}}' },
        { name: '#visitor', settings: '{}' },
      ]),
      type('setState', []),
      type('odd', [{ name: '#broken', settings: 'not json' }]),
    ]);
    expect(choices.map((one) => [one.id, one.type, one.name])).toEqual([
      ['email:0', 'email', '#team'],
      ['email:1', 'email', '#visitor'],
      ['setState', 'setState', '#jfbActionTypes_setState'],
      ['odd:0', 'odd', '#broken'],
    ]);
    expect(choices[0]?.settings).toEqual({ subject: { '': 'New' } });
    expect(choices[0]?.icon).toBe('icon-email');
    expect(choices[3]?.settings).toEqual({});
  });
});

describe('an action as its sidebar edits it', () => {
  const kept: DesignAction = {
    key: 'k',
    type: 'webhook',
    name: 'Send to the CRM',
    enabled: false,
    trigger: 'approved',
    stopOnFailure: true,
    rules: [rule],
    settings: { url: 'https://crm.example.com' },
    later: 'kept',
  };

  it('reads every value and writes it back as it came', () => {
    const edit = editOf(kept);
    expect(edit).toMatchObject({
      name: 'Send to the CRM',
      named: true,
      choice: null,
      trigger: 'approved',
      enabled: false,
      stopOnFailure: true,
      rule,
      rest: { later: 'kept' },
    });
    expect(actionOf(edit)).toEqual(kept);
  });

  it('writes what the server writes for a new one, the name only where there is one, and no rule or setting it lacks', () => {
    const choice = { id: 'setState', type: 'setState', name: '#x', icon: 'icon-x', settings: {} };
    const edit = { ...newEdit('n', choice, ' '), settings: { state: undefined } };
    expect(actionOf(edit)).toEqual({
      key: 'n',
      type: 'setState',
      enabled: true,
      trigger: 'submitted',
      stopOnFailure: false,
    });
    expect(newEdit('n', choice, 'Approve')).toMatchObject({
      trigger: 'submitted',
      named: false,
      choice: 'setState',
    });
  });

  it('follows another choice with its settings, and its name until one was typed', () => {
    const team = { id: 'email:0', type: 'email', name: '#team', icon: 'icon-m', settings: { subject: { '': 'New' } } };
    const hook = { id: 'webhook:0', type: 'webhook', name: '#hook', icon: 'icon-l', settings: {} };
    const started = newEdit('n', team, 'Email the team');
    expect(withChoice(started, hook, 'Webhook')).toMatchObject({
      type: 'webhook',
      choice: 'webhook:0',
      name: 'Webhook',
      settings: {},
    });
    const typed = { ...started, name: 'Tell sales', named: true };
    expect(withChoice(typed, hook, 'Webhook').name).toBe('Tell sales');
  });

  it('sets a setting at its path, nested ones too, and takes one away with what it leaves empty', () => {
    const edit = editOf(kept);
    const set = withSettingAt(edit, 'format.decimals', 2);
    expect(set.settings).toEqual({ url: 'https://crm.example.com', format: { decimals: 2 } });
    expect(settingAt(set, 'format.decimals')).toBe(2);
    expect(settingAt(set, 'format.gone')).toBeUndefined();
    expect(withSettingAt(set, 'format.decimals', undefined).settings).toEqual({ url: 'https://crm.example.com' });
    expect(withSettingAt(set, 'url', undefined).settings).toEqual({ format: { decimals: 2 } });
  });

  it("leads a problem's path to its box, a setting of the type, or the action", () => {
    expect(settingAtPath(['name'])).toEqual({ kind: 'name' });
    expect(settingAtPath(['trigger'])).toEqual({ kind: 'trigger' });
    expect(settingAtPath(['stopOnFailure'])).toEqual({ kind: 'stopOnFailure' });
    expect(settingAtPath(['rules', 0, 'conditions', 1])).toEqual({ kind: 'rule' });
    expect(settingAtPath(['settings', 'to'])).toEqual({ kind: 'setting', path: 'to' });
    // An item of a list, and its part, for an editor that draws each item's boxes (ADR-0192).
    expect(settingAtPath(['settings', 'headers', 2, 'name'])).toEqual({
      kind: 'setting',
      path: 'headers',
      row: { index: 2, part: 'name' },
    });
    expect(settingAtPath(['settings', 'to', 1])).toEqual({
      kind: 'setting',
      path: 'to',
      row: { index: 1, part: undefined },
    });
    expect(settingAtPath(['settings', 'format', 'decimals'])).toEqual({ kind: 'setting', path: 'format.decimals' });
    expect(settingAtPath(['settings'])).toEqual({ kind: 'action' });
    expect(settingAtPath(['type'])).toEqual({ kind: 'action' });
    expect(settingAtPath([])).toEqual({ kind: 'action' });
  });
});
