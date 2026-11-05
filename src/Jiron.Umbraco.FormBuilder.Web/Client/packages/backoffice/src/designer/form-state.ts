// What the form workspace's header says of a form (docs/ux/information-architecture.md, section 5), and where the
// workspace opens: pure, so the tests read them without a backoffice.
import type { FormResponseModel } from '../api';

/** The state chip: never published, published as the draft is, published with a newer draft, or taken offline. */
export type FormState = 'draft' | 'published' | 'pending' | 'unpublished';

/**
 * A form's state. "Pending changes" is said only to who may design, who can act on it; anyone else is told what
 * visitors get, "Published".
 */
export function formState(
  form: Pick<FormResponseModel, 'isPublished' | 'publicRevision' | 'permissions'>,
  hasUnpublishedChanges: boolean,
): FormState {
  if (form.isPublished) return hasUnpublishedChanges && form.permissions.includes('design') ? 'pending' : 'published';
  return form.publicRevision > 0 ? 'unpublished' : 'draft';
}

/** The field a link asks the designer to select, `?field=` (ADR-0166), or none. */
export const fieldOfQuery = (search: string): string | null => keyOfQuery(search, 'field');

/** The action a link asks the Actions view to show, `?action=` (ADR-0166, ADR-0188), or none. */
export const actionOfQuery = (search: string): string | null => keyOfQuery(search, 'action');

function keyOfQuery(search: string, name: string): string | null {
  const key = new URLSearchParams(search).get(name);
  return key && /^[0-9a-f-]{36}$/i.test(key) ? key.toLowerCase() : null;
}
