// What the preview reads (ADR-0203, ADR-0204): a document, a draft whose rules have errors or no answer; a test entry or
// none; and what running its actions did: queued, run before (409 actions_ran), or no answer to go by.
import { describe, expect, it } from 'vitest';
import { FAILED, previewRead, runAnswer, testEntryRead, ticketRead } from './preview-read';

describe('what the preview reads', () => {
  it('shows a document, says a draft whose rules have errors, and fails on anything else', () => {
    const shown = {
      document: '<!doctype html>',
      version: 7,
      culture: 'en-US',
      darkScheme: false,
      lifetimeSeconds: 600,
    };
    expect(previewRead(shown, undefined)).toEqual({ kind: 'shown', ...shown });
    expect(previewRead(undefined, 'rules_invalid')).toEqual({ kind: 'rulesInvalid' });
    expect(previewRead(undefined, 'content_rights')).toEqual({ kind: 'actionsRefused' });
    expect(previewRead(undefined, 'forbidden')).toBe(FAILED);
    expect(previewRead(undefined, 'form_not_found')).toBe(FAILED);
    expect(previewRead(undefined, undefined)).toBe(FAILED);
  });

  it("hands a ticket on, says a content action's missing rights, and has nothing for anything else", () => {
    expect(ticketRead({ ticket: 't', lifetimeSeconds: 600 }, undefined)).toEqual({ ticket: 't', lifetime: 600 });
    expect(ticketRead(undefined, 'content_rights')).toBe('refused');
    expect(ticketRead(undefined, 'forbidden')).toBeUndefined();
    expect(ticketRead(undefined, undefined)).toBeUndefined();
  });

  it('finds the test entry, or none for any other answer', () => {
    const entry = { number: 12, actionsRan: false, awaitsConfirmation: false, actions: ['a1'] };
    expect(testEntryRead(entry)).toEqual({ kind: 'found', ...entry });
    expect(testEntryRead(undefined)).toEqual({ kind: 'missing' });
  });

  it("says whether a test entry's actions were queued, had run before, or got no answer", () => {
    expect(runAnswer(true, undefined)).toBe('ran');
    expect(runAnswer(false, 'actions_ran')).toBe('alreadyRan');
    expect(runAnswer(false, 'content_rights')).toBe('refused');
    expect(runAnswer(false, 'test_entry_not_found')).toBe('failed');
    expect(runAnswer(false, undefined)).toBe('failed');
  });
});
