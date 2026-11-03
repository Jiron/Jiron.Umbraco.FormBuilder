// The designer's undo and redo (ROADMAP 9.3a and 4.13, ADR-0016): every change to the definition is a step back, with
// the selection it had, so Ctrl+Z puts back what changed and where the editor was. Selecting alone changes nothing to
// undo. A new change after an undo drops what could have been redone, and the oldest steps go past a limit. Changes
// that go on with the one before, as typing in one box of the inspector does (9.4a), are one step.
import type { Edit, EditorState } from './editor-model';

/** What one change did, as the announcement says it: "Moved Phone". */
export interface Change {
  readonly state: EditorState;
  readonly said: string;
}

export class EditorHistory {
  #present: EditorState;
  #past: Change[] = [];
  #future: Change[] = [];
  /** What the last change went on with, so the next change of the same thing joins its step. */
  #merging: string | null = null;

  constructor(
    initial: EditorState,
    /** How many changes back undo reaches. */
    readonly limit = 100,
  ) {
    this.#present = initial;
  }

  /** The definition and selection the editor shows. */
  get state(): EditorState {
    return this.#present;
  }

  get canUndo(): boolean {
    return this.#past.length > 0;
  }

  get canRedo(): boolean {
    return this.#future.length > 0;
  }

  /** What undo would take back, as said when it was done; undefined with nothing to undo. */
  get undoes(): string | undefined {
    return this.#past.at(-1)?.said;
  }

  /** What redo would do again; undefined with nothing to redo. */
  get redoes(): string | undefined {
    return this.#future.at(-1)?.said;
  }

  /**
   * Takes an edit's result: a change to the definition is a step to undo, one to the selection alone is not, and a
   * refused edit changes nothing. A change with the same `merge` as the change just before joins its step, so typing in
   * one box is undone at once; anything else in between ends that. Returns the edit, so a caller can say why it was
   * refused.
   */
  apply(edit: Edit, said: string, merge?: string): Edit {
    if (!edit.ok) return edit;
    if (edit.state.definition !== this.#present.definition) {
      const joins = merge !== undefined && merge === this.#merging && this.#past.length > 0;
      if (!joins) {
        this.#past.push({ state: this.#present, said });
        if (this.#past.length > this.limit) this.#past.shift();
      }
      this.#future = [];
      this.#merging = merge ?? null;
    } else {
      this.#merging = null;
    }
    this.#present = edit.state;
    return edit;
  }

  /** Takes the last change back, with the selection it had; what it said, or undefined with nothing to undo. */
  undo(): string | undefined {
    this.#merging = null;
    const change = this.#past.pop();
    if (!change) return undefined;
    this.#future.push({ state: this.#present, said: change.said });
    this.#present = change.state;
    return change.said;
  }

  /** Does the last undone change again; what it said, or undefined with nothing to redo. */
  redo(): string | undefined {
    this.#merging = null;
    const change = this.#future.pop();
    if (!change) return undefined;
    this.#past.push({ state: this.#present, said: change.said });
    this.#present = change.state;
    return change.said;
  }

  /**
   * Puts a definition in the present one's place without a step, as a save does: the server's copy of what was saved
   * holds the same, in its own order and with its defaults, so what is now unsaved compares against it.
   */
  rebase(definition: EditorState['definition']): void {
    this.#merging = null;
    this.#present = { ...this.#present, definition };
  }

  /** Starts over from a state, as a load or a reload does: nothing to undo or redo. */
  reset(state: EditorState): void {
    this.#merging = null;
    this.#present = state;
    this.#past = [];
    this.#future = [];
  }
}
