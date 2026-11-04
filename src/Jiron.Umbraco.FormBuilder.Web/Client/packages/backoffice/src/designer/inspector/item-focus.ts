// Where a problem's Fix it puts the focus in the inspector (ROADMAP 9.5f, ADR-0181): the item the problem's path names,
// found among the items the inspector draws for the element, and its first box. Pure, but for the search of a box's
// shadow roots.
import type { InspectorItem } from '../check/form-check';
import type { Item } from './inspector-model';
import { boxId } from './setting-editor';

/** Whether an item the inspector draws is the one a problem names. */
export function isItem(item: Item, wanted: InspectorItem): boolean {
  switch (wanted.kind) {
    case 'text':
    case 'switch':
    case 'plain':
      return item.kind === wanted.kind && item.property === wanted.property;
    case 'message':
      return item.kind === 'message' && item.message === wanted.message;
    case 'setting':
      return item.kind === 'setting' && item.setting.name === wanted.name;
    default:
      return item.kind === wanted.kind;
  }
}

/** The ids of an item's boxes, in the order the focus tries them: a nested setting's own, then its parent's. */
export function boxesOf(wanted: InspectorItem): string[] {
  switch (wanted.kind) {
    case 'text':
    case 'switch':
    case 'plain':
      return [`prop-${wanted.property}`];
    case 'message':
      return [`prop-message-${wanted.message}`];
    case 'setting':
      return wanted.inner ? [boxId(`${wanted.name}.${wanted.inner}`), boxId(wanted.name)] : [boxId(wanted.name)];
    case 'prefill':
      return ['prop-prefill-query'];
    default:
      return [`prop-${wanted.kind}`];
  }
}

const FOCUSABLE =
  'input:not([disabled]):not([type="hidden"]), select:not([disabled]), textarea:not([disabled]), ' +
  'button:not([disabled]), [tabindex]:not([tabindex="-1"]), [contenteditable="true"]';

/** The element itself where it takes the focus, else the first element in it that does, its shadow roots looked into. */
export function focusableIn(element: Element): HTMLElement | null {
  if (element instanceof HTMLElement && element.matches(FOCUSABLE)) return element;
  for (const root of [element.shadowRoot, element]) {
    if (!root) continue;
    for (const child of Array.from(root.children)) {
      const found = focusableIn(child);
      if (found) return found;
    }
  }
  return null;
}
