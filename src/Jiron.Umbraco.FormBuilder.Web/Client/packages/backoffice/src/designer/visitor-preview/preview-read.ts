// What the preview reads (ADR-0203, ADR-0204): the saved draft's document as visitors would get it, that the draft's
// rules have errors, which the preview never evaluates, or that there was no answer; a new ticket for the frame; and the
// test entry the frame sent, with what running its actions did.
import type { FormPreviewResponseModel, TestEntryResponseModel } from '../../api';

/** The preview's document, or why there is none. */
export type PreviewRead =
  | ({ readonly kind: 'shown' } & FormPreviewResponseModel)
  | { readonly kind: 'rulesInvalid' }
  | { readonly kind: 'failed' };

/** A new ticket for the frame, which counts for so many seconds from now. */
export interface PreviewTicket {
  readonly ticket: string;
  readonly lifetime: number;
}

/** What the document is asked for besides its language: light or dark, the sample answers, and the test entries' actions. */
export interface PreviewAsk {
  readonly dark: boolean;
  readonly sample: boolean;
  readonly actions: boolean;
}

/** The test entry the frame sent: its number, whether its actions ran, and those Run actions... would run; or none found. */
export type TestEntryRead = ({ readonly kind: 'found' } & TestEntryResponseModel) | { readonly kind: 'missing' };

/** What running a test entry's actions did: queued them, found they had run, or got no answer. */
export type RunAnswer = 'ran' | 'alreadyRan' | 'failed';

/** No answer, or one the preview can't show. */
export const FAILED: PreviewRead = { kind: 'failed' };

/** What the preview endpoint answered: its document, its refusal of a draft whose rules have errors, or nothing. */
export function previewRead(data: FormPreviewResponseModel | undefined, code: string | undefined): PreviewRead {
  if (data) return { kind: 'shown', ...data };
  return code === 'rules_invalid' ? { kind: 'rulesInvalid' } : FAILED;
}

/** What the test entry endpoint answered: the entry, or none for any other answer. */
export function testEntryRead(data: TestEntryResponseModel | undefined): TestEntryRead {
  return data ? { kind: 'found', ...data } : { kind: 'missing' };
}

/** What running the actions answered: queued, or ran before (409 `actions_ran`), or nothing to go by. */
export function runAnswer(ok: boolean, code: string | undefined): RunAnswer {
  if (ok) return 'ran';
  return code === 'actions_ran' ? 'alreadyRan' : 'failed';
}
