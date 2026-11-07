/**
 * The runtime's frame (ROADMAP 6.4b, ADR-0110): it finds the forms of the markup contract (ADR-0015), reads each one's
 * model data and enhances it once, and loads the chunk with the logic on a form's first interaction, so a page whose
 * visitor never touches a form loads the frame alone; a form with a default value for the page to fill in loads it at
 * once, so the default is there before the visitor touches the form (ADR-0118), as does a form whose view says it asks
 * at once, for what only the challenge's answer can tell a cached page (ADR-0156). A form of another contract version,
 * with model data this runtime cannot read, or whose chunk cannot load, is left to the post without JavaScript. Below
 * an outcome, it reveals the print button of the copy of the answers and binds it to the browser's print (ADR-0123). It
 * fits every form root to its page (6.4l), and gives a site's code a form to work with (`get`, section 4.9). Where the
 * form's markup names where to count (`data-jfb-insights`), it counts what visitors do with the form (ROADMAP 11.5,
 * ADR-0211).
 */
import { checkGround, followSteps } from './fit';
import type { JironFormBuilderForm } from './global';
import { parseModel, type FormModel } from './model';

/** Marks a form that came in another's place with its logic already (6.4j): one the frame leaves alone. */
const WOKEN = 'data-jfb-woken';

/** The contract version this runtime knows (ADR-0015). */
export const CONTRACT = '1';

/** The interactions that load a form's logic: a field reached, pressed or changed, autofill included. */
const TOUCHES = ['focusin', 'pointerdown', 'input', 'change'] as const;

/** What the chunk offers the frame. */
export interface Chunk {
  attach(form: HTMLFormElement, model: FormModel): unknown;
  /** The logic of a form that came in another's place, which the chunk gave it itself (6.4j). */
  sessionOf?(form: HTMLFormElement): unknown;
}

/**
 * Counts what visitors do with a form, where its markup names where to count (`data-jfb-insights`, ADR-0211): a view as
 * it is enhanced, a start at the visitor's first touch, each page reached, and each field and kind of error that stopped
 * them, each once in a page's life (a beacon the browser could not queue goes again the next time), as a beacon that
 * holds no answer and no person.
 */
function count(form: HTMLFormElement): void {
  const to = form.dataset['jfbInsights'];
  if (!to) return;
  const sent: Record<string, boolean> = {};
  const send = (event: Record<string, string>): void => {
    const body = JSON.stringify(event);
    sent[body] ||= navigator.sendBeacon(to, body);
  };
  send({ type: 'view' });
  for (const type of TOUCHES) form.addEventListener(type, (event) => event.isTrusted && send({ type: 'start' }));
  form.addEventListener('jfb:page', (event) =>
    send({ type: 'page', page: (event as CustomEvent<{ page: string }>).detail.page }),
  );
  form.addEventListener('jfb:invalid', (event) => {
    for (const { field, kind } of (event as CustomEvent<{ errors: { field: string | null; kind: string }[] }>).detail
      .errors)
      if (field) send({ type: 'invalid', field, kind });
  });
}

/** What a form's logic offers a site's code. */
interface Session {
  validate(scope: 'send'): Promise<boolean>;
  submit(): boolean;
  press(nav: 'next' | 'back'): boolean;
  values(): Readonly<Record<string, unknown>>;
}

/** What `jfb:ready` tells a site: which form, never an answer. */
export interface ReadyDetail {
  readonly key: string;
  readonly alias: string;
}

export interface Frame {
  /**
   * Enhances the forms in the root (the document by default), the root itself when it is one, and reveals the print
   * buttons of the copies of the answers in it; each once only.
   */
  init(root?: ParentNode): void;
  /** A form of the markup contract to work with, enhanced if it was not; null for any other element. */
  get(form: Element): JironFormBuilderForm | null;
}

/** A frame that loads its chunk through `load`, once for every form of the page. */
export function createFrame(load: () => Promise<Chunk>): Frame {
  const enhanced = new WeakSet<Element>();
  const wakes = new WeakMap<HTMLFormElement, () => void>();
  const sessions = new WeakMap<HTMLFormElement, Promise<unknown>>();
  const fitted = new WeakSet<Element>();
  let chunk: Promise<Chunk | null> | undefined;

  /** Fits a form root to the page once: an outcome or a notice stands on the same ground as a form. */
  function fit(root: Element): void {
    if (fitted.has(root)) return;
    fitted.add(root);
    checkGround(root);
    const follows = root.ownerDocument.defaultView?.matchMedia?.('(prefers-color-scheme: dark)');
    follows?.addEventListener('change', () => checkGround(root));
    const steps = root.querySelector(':scope > .jfb-progress-steps');
    if (steps !== null) followSteps(steps);
  }

  function enhance(form: HTMLFormElement): void {
    if (enhanced.has(form) || form.hasAttribute(WOKEN) || form.dataset['jfbContract'] !== CONTRACT) return;
    const data = form.querySelector('script[type="application/json"][data-jfb-model]');
    const model = data === null ? null : parseModel(data.textContent ?? '');
    if (model === null) return;
    enhanced.add(form);
    const touched = (): void => {
      if (sessions.has(form)) return;
      for (const type of TOUCHES) form.removeEventListener(type, touched);
      chunk ??= load().catch(() => null);
      sessions.set(
        form,
        chunk.then((loaded) => loaded?.attach(form, model)),
      );
    };
    wakes.set(form, touched);
    count(form);
    for (const type of TOUCHES) form.addEventListener(type, touched);
    if (form.querySelector('[data-jfb-default]') !== null || form.hasAttribute('data-jfb-asks')) touched();
    form.dispatchEvent(
      new CustomEvent<ReadyDetail>('jfb:ready', {
        bubbles: true,
        detail: { key: form.dataset['jfbForm'] ?? '', alias: model.alias },
      }),
    );
  }

  /** The logic of a form, loaded now if the visitor has not touched it; null where it cannot load. */
  async function sessionOf(form: HTMLFormElement): Promise<Session | null> {
    if (form.hasAttribute(WOKEN)) {
      const loaded = await chunk;
      return ((await loaded?.sessionOf?.(form)) as Session | null | undefined) ?? null;
    }
    wakes.get(form)?.();
    return ((await sessions.get(form)) as Session | null | undefined) ?? null;
  }

  return {
    get(form: Element): JironFormBuilderForm | null {
      if (!(form instanceof HTMLFormElement) || !form.matches('[data-jfb-form]')) return null;
      enhance(form);
      if (!form.hasAttribute(WOKEN) && !wakes.has(form)) return null;
      return Object.freeze({
        validate: async () => (await (await sessionOf(form))?.validate('send')) ?? true,
        submit: async () => (await sessionOf(form))?.submit() ?? false,
        next: async () => (await sessionOf(form))?.press('next') ?? false,
        back: async () => (await sessionOf(form))?.press('back') ?? false,
        values: async () => (await sessionOf(form))?.values() ?? {},
      });
    },
    init(root: ParentNode = document): void {
      const roots = root.querySelectorAll(`.jfb-form[data-jfb-contract="${CONTRACT}"]`);
      if (root instanceof Element && root.matches(`.jfb-form[data-jfb-contract="${CONTRACT}"]`)) fit(root);
      for (const each of roots) fit(each);
      if (root instanceof HTMLFormElement && root.matches('[data-jfb-form]')) enhance(root);
      for (const form of root.querySelectorAll<HTMLFormElement>('form[data-jfb-form]')) enhance(form);
      for (const button of root.querySelectorAll<HTMLButtonElement>(
        `[data-jfb-contract="${CONTRACT}"] [data-jfb-print]`,
      )) {
        if (enhanced.has(button)) continue;
        enhanced.add(button);
        button.addEventListener('click', () => window.print());
        button.hidden = false;
      }
    },
  };
}
