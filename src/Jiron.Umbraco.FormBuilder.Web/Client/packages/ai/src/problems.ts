// What a refusal of the add-on's endpoints says to the editor (ADR-0018): its code, as the server names it, picks the
// sentence; a refusal of the request's values names its first value's code; a change a site's own code stopped says
// the site's words after the sentence; a code the add-on does not know, or a failure without one, says that something
// went wrong.
import { UmbApiError } from '@umbraco-cms/backoffice/resources';

/** The codes the add-on has a sentence for. */
export const KNOWN_PROBLEMS = [
  'ai_no_profile',
  'ai_profile_missing',
  'ai_profile_cannot_chat',
  'ai_provider_failed',
  'ai_reply_unusable',
  'ai_draft_invalid',
  'ai_language_invalid',
  'ai_field_missing',
  'text_invalid',
  'profile_invalid',
  'folder_missing',
  'folder_not_found',
  'form_not_found',
  'cancelled',
  'forbidden',
] as const;

export type ProblemCode = (typeof KNOWN_PROBLEMS)[number] | 'failed';

/** A refusal as a screen keeps it: its code, and the words a site's own code gave when it stopped the change. */
export interface Refusal {
  readonly code: ProblemCode;
  readonly said: string;
}

/** The server's code of the request values' refusal (`invalid_request`): the first value's. */
const INVALID_REQUEST = 'invalid_request';

const detailsOf = (error: unknown): Record<string, unknown> | null =>
  UmbApiError.isUmbApiError(error) && typeof error.problemDetails === 'object' && error.problemDetails !== null
    ? (error.problemDetails as unknown as Record<string, unknown>)
    : null;

/** The code of a refusal, or `failed`. */
export function problemOf(error: unknown): ProblemCode {
  const details = detailsOf(error);
  let code = details?.['code'];
  if (code === INVALID_REQUEST && Array.isArray(details?.['errors'])) {
    code = (details['errors'][0] as { code?: unknown } | undefined)?.code;
  }

  return (KNOWN_PROBLEMS as readonly unknown[]).includes(code) ? (code as ProblemCode) : 'failed';
}

/** A refusal: its code, and for a change a site's own code stopped, the words it gave. */
export function refusalOf(error: unknown): Refusal {
  const code = problemOf(error);
  const messages = code === 'cancelled' ? detailsOf(error)?.['messages'] : undefined;
  const said = Array.isArray(messages)
    ? messages.filter((message): message is string => typeof message === 'string').join(' ')
    : '';
  return { code, said };
}

/** A refusal of a code alone, as a screen's own checks give it. */
export const refusal = (code: ProblemCode): Refusal => ({ code, said: '' });

/** The words' key of a code. */
export const problemKey = (code: ProblemCode | string): string =>
  (KNOWN_PROBLEMS as readonly string[]).includes(code) ? `jfbAI_problem_${code}` : 'jfbAI_problem_failed';

/** What a refusal says: the sentence of its code, then a site's own words. */
export const refusalText = (term: (key: string) => string, refused: Refusal): string =>
  refused.said ? `${term(problemKey(refused.code))} ${refused.said}` : term(problemKey(refused.code));
