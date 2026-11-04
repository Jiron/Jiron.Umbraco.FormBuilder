// When the designer asks the server to check the draft (ROADMAP 9.5f, ADR-0181): once the form is read, and after each
// change once the changes pause, so typing in a box asks once; a change while an answer is awaited asks again, and the
// answer to the earlier ask is dropped. The last answer stays shown while the next is awaited. Publish asks at once.
import type { DesignDefinition } from '../definition';
import type { CheckIssue } from './form-check';

/** How long the check waits after a change before it asks: typing changes the form with each key. */
export const CHECK_PAUSE = 600;

/** What the check found, as the badge, its sidebar and the publish dialog show it. */
export interface CheckState {
  /**
   * Idle before the first ask; checking while an answer is awaited, with the last answer's problems until it comes;
   * failed when the check gave no answer.
   */
  readonly status: 'idle' | 'checking' | 'checked' | 'failed';
  readonly issues: readonly CheckIssue[];
  /** The definition the problems are of, whose places their paths name; null before the first answer. */
  readonly definition: DesignDefinition | null;
}

export const UNCHECKED: CheckState = { status: 'idle', issues: [], definition: null };

/** Asks the server about a definition: its problems, or undefined when there is no answer. */
export type CheckAsk = (definition: DesignDefinition) => Promise<readonly CheckIssue[] | undefined>;

export class FormChecker {
  readonly #ask: CheckAsk;
  readonly #show: (state: CheckState) => void;
  readonly #pause: number;
  #state: CheckState = UNCHECKED;
  #waiting: ReturnType<typeof setTimeout> | undefined;
  /** The definition the pause waits to ask about. */
  #pending: DesignDefinition | null = null;
  /** Counts the asks, so only the last one's answer is shown. */
  #asked = 0;
  /** The definition the last ask was of, and its answer while it is awaited. */
  #last: { readonly definition: DesignDefinition; readonly text: string; readonly answer: Promise<void> } | null = null;

  constructor(ask: CheckAsk, show: (state: CheckState) => void, pause = CHECK_PAUSE) {
    this.#ask = ask;
    this.#show = show;
    this.#pause = pause;
  }

  get state(): CheckState {
    return this.#state;
  }

  /**
   * The form changed, or was read: the check asks once the changes pause, the first time at once. The form it already
   * waits for, as a change of the selection gives, does not start the pause again; any other form ends the wait for the
   * one before, so an undo is never asked about the change it took back.
   */
  changed(definition: DesignDefinition) {
    if (definition === this.#pending) return;
    this.#stopWaiting();
    this.#pending = definition;
    this.#waiting = setTimeout(() => void this.now(definition), this.#state.status === 'idle' ? 0 : this.#pause);
  }

  // Ends the pause, if any: nothing waits to be asked.
  #stopWaiting() {
    clearTimeout(this.#waiting);
    this.#waiting = undefined;
    this.#pending = null;
  }

  /**
   * Asks about the definition at once, unless the last ask was about the same form, whose answer it waits for; it
   * settles once the state shows an answer about it.
   */
  now(definition: DesignDefinition): Promise<void> {
    this.#stopWaiting();
    const text = JSON.stringify(definition);
    // The same form again, as a save gives it back: the answer about it holds.
    if (this.#last?.text === text && this.#state.status !== 'failed') return this.#last.answer;
    const asked = ++this.#asked;
    this.#set({ ...this.#state, status: 'checking' });
    const answer = this.#ask(definition).then(
      (issues) => this.#answered(asked, definition, issues),
      () => this.#answered(asked, definition, undefined),
    );
    this.#last = { definition, text, answer };
    return answer;
  }

  #answered(asked: number, definition: DesignDefinition, issues: readonly CheckIssue[] | undefined) {
    // A later ask's answer is the one to show.
    if (asked !== this.#asked) return;
    this.#set(issues ? { status: 'checked', issues, definition } : { status: 'failed', issues: [], definition: null });
  }

  /** Forgets every answer, as another form is read: the next change asks at once. */
  reset() {
    this.#stopWaiting();
    this.#asked++;
    this.#last = null;
    this.#set(UNCHECKED);
  }

  /** Stops waiting to ask; an answer still awaited is dropped. */
  stop() {
    this.#stopWaiting();
    this.#asked++;
  }

  #set(state: CheckState) {
    this.#state = state;
    this.#show(state);
  }
}
