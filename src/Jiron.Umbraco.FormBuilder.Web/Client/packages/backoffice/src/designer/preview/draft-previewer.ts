// A live preview of a draft (ADR-0187, ADR-0189): the server renders what the draft as a sidebar would make it gives,
// for the sample entry; the sidebar asks once the changes pause, as the form check does, the newest answer winning, and
// keeps the last answer shown while the next is awaited. An outcome's sidebar and an email's share it.
import { CHECK_PAUSE } from '../check/form-checker';
import type { DesignDefinition } from '../definition';

/** Asks the server for the preview of a definition; undefined when there is no answer. */
export type PreviewAsk<T> = (definition: DesignDefinition) => Promise<T | undefined>;

/** The preview as the sidebar shows it. */
export interface PreviewState<T> {
  /**
   * Idle before the first ask; asking while an answer is awaited, with the last answer until it comes; shown with an
   * answer; failed when the server gave none.
   */
  readonly status: 'idle' | 'asking' | 'shown' | 'failed';
  readonly preview: T | null;
}

export const NO_PREVIEW: PreviewState<never> = { status: 'idle', preview: null };

export class DraftPreviewer<T> {
  readonly #ask: PreviewAsk<T>;
  readonly #show: (state: PreviewState<T>) => void;
  readonly #pause: number;
  #state: PreviewState<T> = NO_PREVIEW;
  #waiting: ReturnType<typeof setTimeout> | undefined;
  /** Counts the asks, so only the last one's answer is shown. */
  #asked = 0;
  /** The definition last asked about, as text: a change that leaves it as it was asks nothing. */
  #last: string | null = null;
  /** The definition last asked about, which Try again asks about again. */
  #definition: DesignDefinition | null = null;

  constructor(ask: PreviewAsk<T>, show: (state: PreviewState<T>) => void, pause = CHECK_PAUSE) {
    this.#ask = ask;
    this.#show = show;
    this.#pause = pause;
  }

  get state(): PreviewState<T> {
    return this.#state;
  }

  /** The draft changed, or the sidebar opened: the preview is asked for once the changes pause, the first time at once. */
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

  #answered(asked: number, preview: T | undefined) {
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

  #set(state: PreviewState<T>) {
    this.#state = state;
    this.#show(state);
  }
}
