/**
 * The runtime's frame (ROADMAP 6.4b, ADR-0110): it finds the forms of the markup contract (ADR-0015), reads each one's
 * model data and enhances it once, and loads the chunk with the logic on a form's first interaction, so a page whose
 * visitor never touches a form loads the frame alone; a form with a default value for the page to fill in loads it at
 * once, so the default is there before the visitor touches the form (ADR-0118). A form of another contract version,
 * with model data this runtime cannot read, or whose chunk cannot load, is left to the post without JavaScript.
 */
import { parseModel, type FormModel } from './model';

/** The contract version this runtime knows (ADR-0015). */
export const CONTRACT = '1';

/** The interactions that load a form's logic: a field reached, pressed or changed, autofill included. */
const TOUCHES = ['focusin', 'pointerdown', 'input', 'change'] as const;

/** What the chunk offers the frame. */
export interface Chunk {
  attach(form: HTMLFormElement, model: FormModel): unknown;
}

/** What `jfb:ready` tells a site: which form, never an answer. */
export interface ReadyDetail {
  readonly key: string;
  readonly alias: string;
}

export interface Frame {
  /** Enhances the forms in the root (the document by default), the root itself when it is one; a form once only. */
  init(root?: ParentNode): void;
}

/** A frame that loads its chunk through `load`, once for every form of the page. */
export function createFrame(load: () => Promise<Chunk>): Frame {
  const enhanced = new WeakSet<HTMLFormElement>();
  let chunk: Promise<Chunk | null> | undefined;

  function enhance(form: HTMLFormElement): void {
    if (enhanced.has(form) || form.dataset['jfbContract'] !== CONTRACT) return;
    const data = form.querySelector('script[type="application/json"][data-jfb-model]');
    const model = data === null ? null : parseModel(data.textContent ?? '');
    if (model === null) return;
    enhanced.add(form);
    const touched = (): void => {
      for (const type of TOUCHES) form.removeEventListener(type, touched);
      chunk ??= load().catch(() => null);
      void chunk.then((loaded) => loaded?.attach(form, model));
    };
    for (const type of TOUCHES) form.addEventListener(type, touched);
    if (form.querySelector('[data-jfb-default]') !== null) touched();
    form.dispatchEvent(
      new CustomEvent<ReadyDetail>('jfb:ready', {
        bubbles: true,
        detail: { key: form.dataset['jfbForm'] ?? '', alias: model.alias },
      }),
    );
  }

  return {
    init(root: ParentNode = document): void {
      if (root instanceof HTMLFormElement && root.matches('[data-jfb-form]')) enhance(root);
      for (const form of root.querySelectorAll<HTMLFormElement>('form[data-jfb-form]')) enhance(form);
    },
  };
}
