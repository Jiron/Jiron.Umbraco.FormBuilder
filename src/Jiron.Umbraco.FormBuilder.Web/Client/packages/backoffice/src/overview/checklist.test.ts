// The Overview's logic (docs/ux/information-architecture.md, section 2): which step carries the button, what can't work yet, who can
// do what, what Health leaves out, and how a time reads.
import { describe, expect, it } from 'vitest';
import type { ChecklistItemResponseModel, HealthFindingResponseModel } from '../api';
import { doneCount, findingsToShow, receivedText, stepViews } from './checklist';

const steps = ['email', 'form', 'testEntry', 'formBlock', 'access'];

/** The steps with these done and those the user can't do. */
const items = (done: string[] = [], cannot: string[] = []): ChecklistItemResponseModel[] =>
  steps.map((step) => ({ step, done: done.includes(step), canDo: !cannot.includes(step) }));

const states = (list: ChecklistItemResponseModel[]) => stepViews(list).map((view) => view.state);

describe('the steps', () => {
  it('on a fresh install: the first open step carries the action, the preview waits for a form, later ones link', () => {
    expect(states(items(['email']))).toEqual(['done', 'next', 'waiting', 'open', 'open']);
  });

  it('with a form, the preview is the next step', () => {
    expect(states(items(['email', 'form']))).toEqual(['done', 'done', 'next', 'open', 'open']);
  });

  it('a step can be done out of order', () => {
    expect(states(items(['access', 'formBlock']))).toEqual(['next', 'open', 'waiting', 'done', 'done']);
  });

  it('a step the user cannot do says so, and never carries the action', () => {
    expect(states(items(['email', 'form', 'testEntry'], ['formBlock', 'access']))).toEqual([
      'done',
      'done',
      'done',
      'needsSomeone',
      'needsSomeone',
    ]);
    expect(states(items([], ['email']))).toEqual(['needsSomeone', 'next', 'waiting', 'open', 'open']);
  });

  it('a preview someone cannot open says so once there is a form, and waits for one before', () => {
    expect(states(items(['email', 'form'], ['testEntry']))).toEqual(['done', 'done', 'needsSomeone', 'next', 'open']);
    expect(states(items(['email'], ['testEntry']))).toEqual(['done', 'next', 'waiting', 'open', 'open']);
  });

  it('counts what is done', () => {
    expect(doneCount(items(['email', 'access']))).toBe(2);
  });
});

describe('Health', () => {
  const mail: HealthFindingResponseModel = { code: 'emailNoFrom', message: 'No From address', coversEmail: true };
  const jobs: HealthFindingResponseModel = { code: 'jobsStopped', message: 'Jobs stopped', coversEmail: false };

  it('leaves out what the open email step already says', () => {
    expect(findingsToShow([mail, jobs], true, items())).toEqual([jobs]);
  });

  it('says it once the step is done, or the checklist is not shown', () => {
    expect(findingsToShow([mail, jobs], true, items(['email']))).toEqual([mail, jobs]);
    expect(findingsToShow([mail, jobs], false, items())).toEqual([mail, jobs]);
  });
});

describe('the time an entry arrived', () => {
  const words = {
    justNow: 'just now',
    minutes: (count: number) => `${count} min ago`,
    hours: (count: number) => `${count} h ago`,
    today: (time: string) => `Today, ${time}`,
    yesterday: (time: string) => `Yesterday, ${time}`,
  };
  const now = new Date(2026, 10, 3, 18, 30);
  const at = (received: Date) => receivedText(received, now, 'en-GB', words);

  it('reads in minutes, then hours, then the day', () => {
    expect(at(new Date(2026, 10, 3, 18, 29, 30))).toBe('just now');
    expect(at(new Date(2026, 10, 3, 18, 18))).toBe('12 min ago');
    expect(at(new Date(2026, 10, 3, 17, 31))).toBe('59 min ago');
    expect(at(new Date(2026, 10, 3, 15, 30))).toBe('3 h ago');
    expect(at(new Date(2026, 10, 3, 8, 14))).toBe('Today, 08:14');
    expect(at(new Date(2026, 10, 2, 17, 52))).toBe('Yesterday, 17:52');
    expect(at(new Date(2026, 9, 30, 9, 0))).toBe('30 Oct 2026');
  });
});
