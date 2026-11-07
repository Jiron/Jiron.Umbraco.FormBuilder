// What the preview reads (ADR-0203): the saved draft's document as visitors would get it, that the draft's rules have
// errors, which the preview never evaluates, or that there was no answer; and a new ticket for the frame.
import type { FormPreviewResponseModel } from '../../api';

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

/** No answer, or one the preview can't show. */
export const FAILED: PreviewRead = { kind: 'failed' };

/** What the preview endpoint answered: its document, its refusal of a draft whose rules have errors, or nothing. */
export function previewRead(data: FormPreviewResponseModel | undefined, code: string | undefined): PreviewRead {
  if (data) return { kind: 'shown', ...data };
  return code === 'rules_invalid' ? { kind: 'rulesInvalid' } : FAILED;
}
