// The lines under an action editor's box (ADR-0189): why what was typed in it can't be kept, then what the form check
// found at it, with the ids that describe the box and whether it is in error.
import { html, nothing } from '@umbraco-cms/backoffice/external/lit';
import type { EditorContext } from '../inspector/setting-editor';

/** The line under a box that says why what was typed in it can't be kept. */
export function typedLine(editor: EditorContext, id: string) {
  const text = editor.problems.get(id);
  return text ? html`<p class="why" id=${`${id}-why`}>${text}</p>` : nothing;
}

/** The problems at a box: what was typed that can't be kept, then the check's, said under it. */
export function foundAt(editor: EditorContext, id: string) {
  const typed = editor.problems.get(id);
  const found = editor.found?.(id);
  return {
    ids: [...(typed ? [`${id}-why`] : []), ...(found?.ids ?? [])],
    invalid: typed !== undefined || (found?.error ?? false),
    lines: html`${typed ? html`<p class="why" id=${`${id}-why`}>${typed}</p>` : nothing}${found?.lines ?? nothing}`,
  };
}
