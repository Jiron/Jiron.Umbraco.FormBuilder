// An outcome's live preview (ROADMAP 10.1c, ADR-0187): the server renders the outcome of the draft as the sidebar would
// make it, for the sample entry, with the site's own view, asked as the draft's previewer asks. What the runtime adds to
// an outcome after a send, the countdown's seconds and the print button, the preview adds the same way.
import { countdownFrom } from '@jfb/runtime/countdown';
import type { OutcomePreviewResponseModel } from '../../api';
import {
  DraftPreviewer,
  NO_PREVIEW,
  type PreviewAsk,
  type PreviewState as DraftPreviewState,
} from '../preview/draft-previewer';

/** What the server answers for an outcome: its markup, or for a redirect where visitors go. */
export type OutcomePreview = OutcomePreviewResponseModel;

/** Asks the server for the preview of an outcome of a definition; undefined when there is no answer. */
export type OutcomePreviewAsk = PreviewAsk<OutcomePreview>;

/** The preview as the outcome's sidebar shows it. */
export type PreviewState = DraftPreviewState<OutcomePreview>;

export { NO_PREVIEW };

/** The outcome's previewer: the draft's, for an outcome. */
export class OutcomePreviewer extends DraftPreviewer<OutcomePreview> {}

// What a site's own view could write that has no place in a preview: nothing in it runs or is sent, and no frame,
// object or stylesheet loads. An image of the site's, which a site's own view may show, stays.
const ACTIVE = 'script, style, link, meta, base, iframe, frame, object, embed, form, template, noscript';

/** Whether an attribute's value is a script address, as a browser reads it: spaces and control characters dropped. */
function isScriptAddress(value: string): boolean {
  const read = [...value].filter((character) => character.charCodeAt(0) > 32).join('');
  return read.toLowerCase().startsWith('javascript:');
}

/**
 * The outcome's markup as the preview shows it: parsed apart from the page, without anything that could run or send;
 * the countdown says the seconds it starts from, as the runtime's does, and the print button shows, as after a send. Its
 * links and buttons stay what they are, so a screen reader reads the page as visitors get it, but none is a stop of
 * Tab, and the outcome itself takes no focus.
 */
export function previewMarkup(document: Document, html: string, delaySeconds: number): DocumentFragment {
  const parsed = new DOMParser().parseFromString(html, 'text/html');
  for (const node of parsed.body.querySelectorAll(ACTIVE)) node.remove();
  for (const element of parsed.body.querySelectorAll('*')) {
    for (const attribute of [...element.attributes]) {
      if (attribute.name.toLowerCase().startsWith('on') || isScriptAddress(attribute.value))
        element.removeAttribute(attribute.name);
    }
  }
  const count = parsed.body.querySelector<HTMLElement>('[data-jfb-countdown]');
  const from = countdownFrom(delaySeconds);
  if (count && from !== null) {
    count.textContent = (count.dataset[from === 1 ? 'jfbOne' : 'jfbOther'] ?? '').replace('{0}', String(from));
  }
  for (const print of parsed.body.querySelectorAll<HTMLElement>('[data-jfb-print]')) print.hidden = false;
  parsed.body.querySelector('[data-jfb-outcome]')?.removeAttribute('tabindex');
  for (const stop of parsed.body.querySelectorAll('a[href], button, input, select, textarea, [tabindex]'))
    stop.setAttribute('tabindex', '-1');
  const fragment = document.createDocumentFragment();
  for (const node of [...parsed.body.childNodes]) fragment.append(document.importNode(node, true));
  return fragment;
}
