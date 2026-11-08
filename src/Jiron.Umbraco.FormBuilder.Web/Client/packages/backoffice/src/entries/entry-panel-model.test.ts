// What the entry panel says of an entry (ROADMAP 11.2, IA 6.3, ADR-0208): the bar of each state as each user may use
// it, the details line, a file's size, the timeline in order with its runs and mail, an edit's problems by their answer,
// and what a move back asks.
import { describe, expect, it } from 'vitest';
import entriesEn from '../lang/entries.en';
import { asksLines, detailsLine, panelBar, problemAnswer, sizeText, timelineLines } from './entry-panel-model';

const words = entriesEn.jfbEntries as Record<string, unknown>;
const term = (key: string, ...args: unknown[]): string => {
  const word = words[key.replace(/^jfbEntries_/, '')];
  return typeof word === 'function'
    ? String((word as (...values: unknown[]) => unknown)(...args))
    : String(word ?? key);
};

const ADMIN = ['viewEntries', 'editEntries', 'deleteEntries', 'manageActions', 'viewSensitive'];
const HANDLER = ['viewEntries', 'editEntries', 'export'];
const PRIVACY = ['viewEntries', 'viewSensitive', 'deleteEntries', 'privacyTools'];

describe("the entry panel's bar", () => {
  it('waits for approval with Approve first, then the first three the user may use', () => {
    expect(panelBar('submitted', true, ADMIN)).toEqual({
      primary: 'approve',
      visible: ['reject', 'markSpam', 'forward'],
      more: ['delete', 'rerun'],
    });
    expect(panelBar('submitted', true, HANDLER)).toEqual({
      primary: 'approve',
      visible: ['reject', 'markSpam', 'forward'],
      more: ['print'],
    });
  });

  it('keeps Print in the "..." in every state, for a user who may export (IA 6.3)', () => {
    const all = [...ADMIN, 'export'];
    expect(panelBar('submitted', true, all).more).toEqual(['delete', 'print', 'rerun']);
    expect(panelBar('spam', true, all)).toEqual({ primary: 'notSpam', visible: ['delete'], more: ['print'] });
    expect(panelBar('approved', true, all).more).toEqual(['backToWaiting', 'print', 'rerun']);
    expect(panelBar('unverified', false, ['viewEntries', 'export'])).toEqual({ visible: [], more: ['print'] });
    expect(panelBar('approved', false, ADMIN).more, 'none without export').toEqual(['rerun']);
  });

  it('gives a privacy officer Delete... alone, and Spam its Not spam', () => {
    expect(panelBar('submitted', true, PRIVACY)).toEqual({ visible: ['delete'], more: [] });
    expect(panelBar('spam', true, ADMIN)).toEqual({ primary: 'notSpam', visible: ['delete'], more: [] });
  });

  it('offers the way back to waiting only where a person approves', () => {
    expect(panelBar('approved', true, ADMIN)).toEqual({
      visible: ['markSpam', 'forward', 'delete'],
      more: ['backToWaiting', 'rerun'],
    });
    expect(panelBar('approved', false, ADMIN).more).toEqual(['rerun']);
    expect(panelBar('submitted', false, ADMIN).primary).toBeUndefined();
  });
});

describe('what the panel says', () => {
  it('says where, in which language and how long, each where it is known, and a test entry first', () => {
    const none = { member: null, memberName: null, ipAddress: null, spamReasons: [] };
    expect(
      detailsLine(
        { ...none, page: 'https://example.org/careers/apply?x=1', culture: 'de-CH', duration: 365 },
        'en',
        term,
      ),
    ).toEqual(['Sent from /careers/apply?x=1', 'Swiss High German', '6 min to complete']);
    expect(detailsLine({ ...none, page: null, culture: 'en', duration: 20 }, 'en', term)).toEqual([
      'English',
      'Under a minute to complete',
    ]);
    expect(detailsLine({ ...none, page: null, culture: 'en', duration: null }, 'en', term, true)).toEqual([
      'Test entry, sent from the preview',
      'English',
    ]);
  });

  it('sizes a file as people read it', () => {
    expect([sizeText(512, 'en'), sizeText(389_120, 'en'), sizeText(1_258_291, 'en')]).toEqual([
      '512 B',
      '380 KB',
      '1.2 MB',
    ]);
  });

  it('lists the timeline newest first, a run with Retry where it failed, and a mail of its own', () => {
    const lines = timelineLines(
      {
        events: [
          { id: 1, when: '2026-11-07T08:00:00Z', kind: 'submitted', changes: [] },
          { id: 2, when: '2026-11-07T09:00:00Z', kind: 'opened', user: 'Ana', changes: [] },
          {
            id: 3,
            when: '2026-11-07T11:00:00Z',
            kind: 'answersEdited',
            user: 'Ana',
            changes: [
              { label: 'Age', before: ['36'], after: ['37'], masked: false },
              { label: 'Salary', before: [], after: [], masked: true },
            ],
          },
        ],
        runs: [
          {
            id: 7,
            when: '2026-11-07T10:00:00Z',
            action: 'a',
            name: 'Email the team',
            type: 'email',
            state: 'failed',
            attempts: 3,
            error: 'The server answered 503.',
          },
        ],
        mail: [
          {
            id: 8,
            when: '2026-11-07T10:00:01Z',
            kind: 'action',
            to: 'hr@example.org',
            subject: 's',
            state: 'failed',
            actionRun: 7,
          },
          { id: 9, when: '2026-11-07T12:00:00Z', kind: 'forward', to: 'ana@example.org', subject: 's', state: 'sent' },
        ],
      } as never,
      'en-GB',
      new Date('2026-11-07T13:00:00Z'),
      term,
    );

    expect(lines.map((line) => line.text)).toEqual([
      'Forward to ana@example.org: sent',
      'Answers edited by Ana',
      'Email the team: failed',
      'Opened by Ana',
      'Received',
    ]);
    expect(lines[1]?.changes).toEqual(['Age: 36 \u2192 37', 'Salary: changed']);
    expect(lines[2]?.retry).toEqual({ run: 7, uncertain: false, name: 'Email the team' });
    expect(lines[2]?.icon).toBe('icon-alert');
    expect(lines[2]?.changes).toEqual(['The server answered 503.']);
  });

  it('says to whom and by whom an entry was forwarded, and how many actions who queued again', () => {
    const lines = timelineLines(
      {
        events: [
          { id: 1, when: '2026-11-07T08:00:00Z', kind: 'submitted', changes: [] },
          {
            id: 2,
            when: '2026-11-07T09:00:00Z',
            kind: 'forwarded',
            user: 'Ana',
            recipient: 'hr@example.org',
            changes: [],
          },
          { id: 3, when: '2026-11-07T10:00:00Z', kind: 'actionsRerun', user: 'Ben', count: 2, changes: [] },
          { id: 4, when: '2026-11-07T11:00:00Z', kind: 'forwarded', recipient: 'Cy', changes: [] },
        ],
        runs: [],
        mail: [],
      } as never,
      'en-GB',
      new Date('2026-11-07T13:00:00Z'),
      term,
    );

    expect(lines.map((line) => [line.text, line.icon])).toEqual([
      ['Forwarded to Cy by someone', 'icon-share'],
      ['2 actions queued again by Ben', expect.any(String)],
      ['Forwarded to hr@example.org by Ana', 'icon-share'],
      ['Received', expect.any(String)],
    ]);
  });

  it("finds an edit problem's answer, and says what a move back asks", () => {
    expect([problemAnswer('answers[2].values'), problemAnswer('answers')]).toEqual([2, undefined]);
    expect(
      asksLines(
        {
          full: [{ field: 'f', fieldLabel: 'Seat', option: 'front', optionLabel: 'Front', places: 1, taken: 1 }],
          held: [{ field: 'c', fieldLabel: 'Code' }],
        },
        term,
      ),
    ).toEqual([
      'Front (Seat) is full by now: all 1 place is taken.',
      'Another entry has the same Code, which must be unique.',
    ]);
  });
});
