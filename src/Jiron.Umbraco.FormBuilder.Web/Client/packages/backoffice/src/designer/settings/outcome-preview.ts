// An outcome's live preview (ROADMAP 10.1c, ADR-0187): the server renders the outcome of the draft as the sidebar would
// make it, for the sample entry, with the site's own view; the sidebar asks once the changes pause, as the form check
// does, the newest answer winning, and keeps the last answer shown while the next is awaited. What the runtime adds to
// an outcome after a send, the countdown's seconds and the print button, the preview adds the same way.
import { countdownFrom } from '@jfb/runtime/countdown';
import type { OutcomePreviewResponseModel } from '../../api';
import { CHECK_PAUSE } from '../check/form-checker';
import type { DesignDefinition } from '../definition';

/** What the server answers for an outcome: its markup, or for a redirect where visitors go. */
export type OutcomePreview = OutcomePreviewResponseModel;

/** Asks the server for the preview of an outcome of a definition; undefined when there is no answer. */
export type OutcomePreviewAsk = (definition: DesignDefinition) => Promise<OutcomePreview | undefined>;

/** The preview as the sidebar shows it. */
export interface PreviewState {
  /**
   * Idle before the first ask; asking while an answer is awaited, with the last answer until it comes; shown with an
   * answer; failed when the server gave none.
   */
  readonly status: 'idle' | 'asking' | 'shown' | 'failed';
  readonly preview: OutcomePreview | null;
}

export const NO_PREVIEW: PreviewState = { status: 'idle', preview: null };

export class OutcomePreviewer {
  readonly #ask: OutcomePreviewAsk;
  readonly #show: (state: PreviewState) => void;
  readonly #pause: number;
  #state: PreviewState = NO_PREVIEW;
  #waiting: ReturnType<typeof setTimeout> | undefined;
  /** Counts the asks, so only the last one's answer is shown. */
  #asked = 0;
  /** The definition last asked about, as text: a change that leaves it as it was asks nothing. */
  #last: string | null = null;
  /** The definition last asked about, which Try again asks about again. */
  #definition: DesignDefinition | null = null;

  constructor(ask: OutcomePreviewAsk, show: (state: PreviewState) => void, pause = CHECK_PAUSE) {
    this.#ask = ask;
    this.#show = show;
    this.#pause = pause;
  }

  get state(): PreviewState {
    return this.#state;
  }

  /** The outcome changed, or the sidebar opened: the preview is asked for once the changes pause, the first time at once. */
  changed(definition: DesignDefinition) {
    clearTimeout(this.#waiting);
    const first = this.#state.status === 'idle' && this.#last === null;
    this.#waiting = setTimeout(() => this.#now(definition), first ? 0 : this.#pause);
  }

  #now(definition: DesignDefinition) {
    this.#waiting = undefined;
    const text = JSON.stringify(definition);
    if (text === this.#last && this.#state.status !== 'failed') return;
    this.#last = text;
    this.#definition = definition;
    const asked = ++this.#asked;
    this.#set({ status: 'asking', preview: this.#state.preview });
    void this.#ask(definition).then(
      (preview) => this.#answered(asked, preview),
      () => this.#answered(asked, undefined),
    );
  }

  #answered(asked: number, preview: OutcomePreview | undefined) {
    // A later ask's answer is the one to show.
    if (asked !== this.#asked) return;
    this.#set(preview ? { status: 'shown', preview } : { status: 'failed', preview: null });
  }

  /** Asks again about the definition last asked about, after an ask that got no answer. */
  retry() {
    if (this.#state.status === 'failed' && this.#definition) this.#now(this.#definition);
  }

  /** Stops waiting and drops an answer still awaited, as the sidebar closes. */
  stop() {
    clearTimeout(this.#waiting);
    this.#waiting = undefined;
    this.#asked++;
  }

  #set(state: PreviewState) {
    this.#state = state;
    this.#show(state);
  }
}

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
