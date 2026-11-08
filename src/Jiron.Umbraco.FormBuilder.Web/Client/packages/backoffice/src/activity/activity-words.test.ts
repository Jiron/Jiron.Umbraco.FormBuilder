// The activity screens in words (ROADMAP 12.4b, ADR-0219): an action's sentence from its details' facts, where it happened
// and who did it, with the English dictionary; a delivery's state, recipient, kind and story; and German holding the same
// words as English.
import { describe, expect, it } from 'vitest';
import type { AuditEntryResponseModel, DeliveryDetailResponseModel, DeliveryResponseModel } from '../api';
import actionsEn from '../lang/actions.en';
import de from '../lang/activity.de';
import en from '../lang/activity.en';
import sectionEn from '../lang/section.en';
import { factsOf, grantChangeOf, sentenceOf, whereOf, whoOf, type AuditNames } from './audit-words';
import { headlineOf, storyOf, toneOf, toOf, whatOf, whenOfDelivery } from './delivery-words';

type Words = Record<string, unknown>;

const termOf =
  (words: Words) =>
  (key: string, ...args: unknown[]): string => {
    const word = words[key];
    if (typeof word === 'function') return (word as (...values: unknown[]) => string)(...args);
    return typeof word === 'string' ? word : key;
  };

const term = termOf(en.jfbActivity as Words);
const runTerm = termOf(actionsEn.jfbActions as Words);
const section = termOf(sectionEn.jfbSection as Words);
const setting = (sectionAlias: string, field: string) =>
  sectionAlias === 'general' && field === 'theme' ? 'Default theme' : '';

const FORM = '00000000-0000-0000-0000-0000000000f1';
const ENTRY = '00000000-0000-0000-0000-0000000000e1';
const FIELD = '00000000-0000-0000-0000-0000000000d1';
const GROUP = '00000000-0000-0000-0000-0000000000a1';

const names: AuditNames = {
  [FORM]: { kind: 'form', name: 'Job application' },
  [ENTRY]: { kind: 'entry', name: 'Job application', number: 212 },
  [FIELD]: { kind: 'field', name: 'Date of birth' },
  [GROUP]: { kind: 'userGroup', name: 'HR team' },
};

function action(more: Partial<AuditEntryResponseModel>): AuditEntryResponseModel {
  return {
    id: 1,
    occurredUtc: '2026-11-08T12:00:00Z',
    userKey: 'u1',
    userName: 'Ana Novak',
    action: 'form.published',
    entityType: 'form',
    entityKey: FORM,
    details: {},
    ...more,
  };
}

const say = (entry: AuditEntryResponseModel) => sentenceOf(entry, factsOf(entry, names, term, setting), term);

describe('the audit log in words', () => {
  it('says each action from its facts, naming entries by number and fields by label', () => {
    expect(say(action({ details: { version: 3 } }))).toBe('Published version 3');
    expect(
      say(
        action({
          action: 'entry.sensitiveRevealed',
          entityType: 'entry',
          entityKey: ENTRY,
          details: { form: FORM, field: FIELD },
        }),
      ),
    ).toBe('Revealed Date of birth in entry 212');
    expect(
      say(action({ action: 'entry.stateChanged', details: { entry: ENTRY, from: 'submitted', to: 'approved' } })),
    ).toBe('Moved entry 212 from Submitted to Approved');
    expect(say(action({ action: 'entries.exported', details: { entries: 214, format: 'csv' } }))).toBe(
      'Exported 214 entries',
    );
    expect(say(action({ action: 'entry.deleted', details: { entries: 1 } }))).toBe('Deleted 1 entry');
    expect(say(action({ action: 'form.draftsPruned', details: { versions: [2, 3, 4] } }))).toBe(
      'Cleared 3 old draft saves',
    );
    expect(
      say(action({ action: 'access.grantChanged', entityType: 'access', entityKey: null, details: { group: GROUP } })),
    ).toBe('Changed what HR team may do');
    expect(say(action({ action: 'form.versionRestored', details: { version: 5, from: 2 } }))).toBe(
      'Restored version 2 as version 5',
    );
  });

  it('names one changed setting, counts several, and keeps no value', () => {
    const one = action({
      action: 'settings.saved',
      entityType: 'settings',
      entityKey: null,
      details: { section: 'general', fields: ['theme'], fieldCount: 1 },
    });
    const two = action({
      ...one,
      details: { section: 'general', fields: ['theme', 'history.draftDays'], fieldCount: 2 },
    });
    expect(say(one)).toBe('Changed Default theme');
    expect(say(two)).toBe('Changed 2 settings');
  });

  it('says where: the item by its name now, a deleted one as such, an entry with its form, a section where it sits', () => {
    expect(whereOf(action({}), names, term, section)).toBe('Job application');
    expect(whereOf(action({ entityKey: 'gone' }), names, term, section)).toBe('A deleted form');
    expect(whereOf(action({ entityType: 'entry', entityKey: ENTRY }), names, term, section)).toBe(
      'Entry 212 of Job application',
    );
    const saved = action({
      action: 'settings.saved',
      entityType: 'settings',
      entityKey: null,
      details: { section: 'privacy' },
    });
    expect(whereOf(saved, names, term, section)).toBe('Privacy \u203a Data collection');
    expect(whereOf({ ...saved, details: { section: 'email' } }, names, term, section)).toBe('Settings \u203a Email');
    expect(whereOf(action({ entityType: 'access', entityKey: null }), names, term, section)).toBe(
      'Settings \u203a Access',
    );
    expect(whereOf(action({ entityType: 'audit', entityKey: null }), names, term, section)).toBe(
      'Activity \u203a Audit log',
    );
  });

  it('says who: the user, the product for its own work, or a deleted user', () => {
    expect(whoOf(action({}), term)).toBe('Ana Novak');
    expect(whoOf(action({ userKey: null, userName: null }), term)).toBe('Jiron Form Builder');
    expect(whoOf(action({ userName: null }), term)).toBe('A deleted user');
  });

  it("lists a grant's permissions before and after, each once", () => {
    const changed = action({
      action: 'access.grantChanged',
      details: { before: ['viewEntries'], after: ['viewEntries', 'export'] },
    });
    expect(grantChangeOf(changed)).toEqual([
      { name: 'viewEntries', before: true, after: true },
      { name: 'export', before: false, after: true },
    ]);
  });

  it('says the code of an action it has no words for, never a key', () => {
    expect(say(action({ action: 'widget.spun' }))).toBe('widget.spun');
  });
});

function delivery(more: Partial<DeliveryResponseModel>): DeliveryResponseModel {
  return {
    kind: 'email',
    id: 1,
    state: 'failed',
    emailKind: 'action',
    recipients: ['team@example.com'],
    actionType: 'email',
    actionName: null,
    typeName: '#jfbActionTypes_email',
    target: null,
    formKey: FORM,
    formName: 'Contact us',
    entryKey: ENTRY,
    entryNumber: 7,
    attempts: 5,
    nextAttemptUtc: null,
    errorCode: 'exception',
    errorMessage: 'System.Net.Mail.SmtpException',
    createdUtc: '2026-11-08T12:00:00Z',
    firstAttemptUtc: null,
    lastAttemptUtc: null,
    finishedUtc: null,
    ...more,
  };
}

const typeName = (name: string) => (name === '#jfbActionTypes_webhook' ? 'Webhook' : name);

describe('a delivery in words', () => {
  it('says whom it went to: the first of several recipients, a host, a reference, a chat', () => {
    expect(toOf(delivery({ recipients: ['a@example.com', 'b@example.com', 'c@example.com'] }), term)).toBe(
      'a@example.com and 2 more',
    );
    const run = delivery({ kind: 'run', recipients: [], actionType: 'webhook' });
    expect(toOf({ ...run, target: { kind: 'host', text: 'crm.example.com' } }, term)).toBe('crm.example.com');
    expect(toOf({ ...run, target: { kind: 'reference', text: 'JironFormBuilder:Secrets:Crm' } }, term)).toBe(
      'The address in JironFormBuilder:Secrets:Crm',
    );
    expect(toOf({ ...run, target: { kind: 'slack', text: null } }, term)).toBe('Slack');
    expect(toOf({ ...run, actionType: 'setState' }, term)).toBe('');
  });

  it("says what it is: the action's name, its type's, or the kind of mail", () => {
    expect(whatOf(delivery({ actionName: 'Email the team' }), term, typeName)).toBe('Email the team');
    expect(whatOf(delivery({ emailKind: 'test', actionType: null }), term, typeName)).toBe('A test email');
    expect(whatOf(delivery({ emailKind: 'action' }), term, typeName)).toBe('An email action');
    expect(whatOf(delivery({ kind: 'run', typeName: '#jfbActionTypes_webhook' }), term, typeName)).toBe('Webhook');
  });

  it('gives a problem its tone and a delivery the positive one', () => {
    expect([
      toneOf('failed'),
      toneOf('tryingAgain'),
      toneOf('uncertain'),
      toneOf('limited'),
      toneOf('delivered'),
      toneOf('waiting'),
    ]).toEqual(['danger', 'danger', 'warning', 'warning', 'positive', 'default']);
  });

  it('says when it waits for its next try', () => {
    const now = new Date('2026-11-08T15:00:00Z');
    const trying = delivery({
      state: 'tryingAgain',
      createdUtc: '2026-11-08T14:02:00Z',
      nextAttemptUtc: '2026-11-08T14:30:00Z',
    });
    expect(whenOfDelivery(trying, now, 'en-GB', term)).toMatch(/^\d{2}:\d{2}, next try \d{2}:\d{2}$/);
  });

  it('heads its details with whom it went to', () => {
    expect(headlineOf(delivery({}), term, typeName)).toBe('Email to team@example.com');
    expect(
      headlineOf(
        delivery({
          kind: 'run',
          recipients: [],
          actionType: 'webhook',
          target: { kind: 'host', text: 'crm.example.com' },
        }),
        term,
        typeName,
      ),
    ).toBe('Webhook to crm.example.com');
  });

  it("tells a limited mail's limit and why it is never retried, an uncertain one that it may have arrived, and a skipped run why", () => {
    const now = new Date('2026-11-08T15:00:00Z');
    const detail = (
      more: Partial<DeliveryResponseModel>,
      limit: DeliveryDetailResponseModel['limit'] = null,
    ): DeliveryDetailResponseModel => ({
      delivery: delivery(more),
      mayRetry: true,
      retryable: true,
      limit,
    });
    const limited = storyOf(
      detail(
        { state: 'limited', recipients: ['anna@example.org'] },
        { kind: 'recipient', count: 3, key: 'JironFormBuilder:Limits:Mail:PerRecipientPerDay' },
      ),
      now,
      'en-GB',
      term,
      runTerm,
    );
    expect(limited.lines[0]).toContain('3 emails to anna@example.org that day');
    expect(limited.lines[1]).toContain("so it can't be retried");
    expect(limited.tries).toBe('');
    const uncertain = storyOf(
      detail({ state: 'uncertain', errorCode: 'lease_expired', attempts: 1 }),
      now,
      'en-GB',
      term,
      runTerm,
    );
    expect(uncertain.lines[0]).toBe('Sending stopped before the mail server answered, so it may have arrived.');
    expect(uncertain.tries).toBe('Tried once.');
    const skipped = storyOf(
      detail({ kind: 'run', state: 'skipped', errorCode: 'stopped' }),
      now,
      'en-GB',
      term,
      runTerm,
    );
    expect(skipped.lines).toEqual(['An earlier action of the entry failed and stopped the rest.']);
  });
});

describe("the activity screens' words", () => {
  it('are the same keys in German as in English', () => {
    expect(Object.keys(de.jfbActivity).sort()).toEqual(Object.keys(en.jfbActivity).sort());
  });
});
