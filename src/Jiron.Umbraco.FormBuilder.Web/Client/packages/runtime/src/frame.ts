/**
 * The runtime's frame (ROADMAP 6.4b, ADR-0110): it finds the forms of the markup contract (ADR-0015), reads each one's
 * model data and enhances it once, and loads the chunk with the logic on a form's first interaction, so a page whose
 * visitor never touches a form loads the frame alone, and what the visitor answers and leaves until the logic woke is
 * handed to it (ADR-0113); a form with a default value for the page to fill in loads it at
 * once, so the default is there before the visitor touches the form (ADR-0118), as does a form whose view says it asks
 * at once, for what only the challenge's answer can tell a cached page (ADR-0156). A form of another contract version,
 * with model data this runtime cannot read, or whose chunk cannot load, is left to the post without JavaScript. Below
 * an outcome, it reveals the print button of the copy of the answers and binds it to the browser's print (ADR-0123). It
 * fits every form root to its page (6.4l), and gives a site's code a form to work with (`get`, section 4.9). Where the
 * form's markup names where to count (`data-jfb-insights`), it counts what visitors do with the form (ROADMAP 11.5,
 * ADR-0211). What one form throws is reported as an uncaught error is, and the page's other forms go on (16.4, ADR-0237).
 */
import { proto } from '@jfb/logic/proto';
import { checkGround, followSteps } from './fit';
import type { JironFormBuilderForm } from './global';
import { parseModel, type FormModel } from './model';

/** Marks a form that came in another's place with its logic already (6.4j): one the frame leaves alone. */
const WOKEN = 'data-jfb-woken';

/** The contract version this runtime knows (ADR-0015). */
export const CONTRACT = '1';

/** The interactions that load a form's logic: a field reached, pressed or changed, autofill included. */
const TOUCHES = ['focusin', 'pointerdown', 'input', 'change'] as const;

/** What the logic hears of a visitor once it woke, which the frame keeps for it until then: an answer and a leaving. */
const HEARD = ['input', 'change', 'focusout'] as const;

/** What the chunk offers the frame. */
export interface Chunk {
  /** `heard` is what the visitor answered and left before the logic woke, in order, which it holds until it wakes. */
  attach(form: HTMLFormElement, model: FormModel, heard: readonly Event[]): unknown;
  /** The logic of a form that came in another's place, which the chunk gave it itself (6.4j). */
  sessionOf?(form: HTMLFormElement): unknown;
}

/**
 * Counts what visitors do with a form, where its markup names where to count (`data-jfb-insights`, ADR-0211): a view as
 * it is enhanced, a start at the visitor's first press or answer, each page reached, and each field that stopped them
 * with the kind of error that did first, each once in a page's life (a beacon the browser could not queue goes again the
 * next time), as a beacon that holds no answer and no person. A browser that sends no beacons counts nothing.
 */
function count(form: HTMLFormElement): void {
  const to = proto(form).dataset['jfbInsights'];
  if (!to) return;
  // Without a prototype: a field's alias names a beacon, and `constructor` or `toString` would find one sent already.
  const sent: Record<string, boolean | undefined> = Object.create(null) as Record<string, boolean | undefined>;
  const send = (event: Record<string, string>, once = JSON.stringify(event)): void => {
    sent[once] ||= navigator.sendBeacon?.(to, JSON.stringify(event));
  };
  send({ type: 'view' });
  // Not a focus: the page's own script or the preview may move it, which the browser calls trusted too.
  for (const type of TOUCHES.slice(1))
    proto(form).addEventListener(type, (event) => event.isTrusted && send({ type: 'start' }));
  proto(form).addEventListener('jfb:page', (event) =>
    send({ type: 'page', page: (event as CustomEvent<{ page: string }>).detail.page }),
  );
  proto(form).addEventListener('jfb:invalid', (event) => {
    for (const { field, kind } of (event as CustomEvent<{ errors: { field: string | null; kind: string }[] }>).detail
      .errors)
      if (field) send({ type: 'invalid', field, kind }, field);
  });
}

/**
 * Runs one form's work, so that what it throws stops no other form: it is reported as an uncaught error would be, for the
 * console and a site's error handler, and that form is left to the post without JavaScript (16.4, ADR-0237).
 */
function alone(work: () => void): void {
  try {
    work();
  } catch (error) {
    if (typeof reportError === 'function') reportError(error);
    else
      setTimeout(() => {
        throw error;
      });
  }
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

/** Whether the address or this tab names a saved form to take up (ADR-0163). */
function comesBack(form: HTMLFormElement): boolean {
  if (proto(form).ownerDocument.location.hash.includes('jfb-resume=')) return true;
  try {
    return sessionStorage.getItem(`jfb-resume:${proto(form).dataset['jfbForm'] ?? ''}`) !== null;
  } catch {
    return false;
  }
}

/** What following a saved form's link needs of the window. */
export interface ResumeLinkTarget {
  addEventListener(type: 'hashchange', listener: () => void): void;
  readonly location: { readonly hash: string; reload(): void };
}

/**
 * A saved form's link opened in a tab that already shows its page changes only the address's fragment, which loads
 * nothing: the page loads again, so its form takes the link up as it does from the mail (ADR-0163).
 */
export function followResumeLinks(target: ResumeLinkTarget): void {
  target.addEventListener('hashchange', () => {
    if (target.location.hash.includes('jfb-resume=')) target.location.reload();
  });
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
    const follows = proto(root).ownerDocument.defaultView?.matchMedia?.('(prefers-color-scheme: dark)');
    follows?.addEventListener('change', () => checkGround(root));
    const steps = proto(root).querySelector(':scope > .jfb-progress-steps');
    if (steps !== null) followSteps(steps);
  }

  function enhance(form: HTMLFormElement): void {
    if (enhanced.has(form) || proto(form).hasAttribute(WOKEN) || proto(form).dataset['jfbContract'] !== CONTRACT)
      return;
    const data = proto(form).querySelector('script[type="application/json"][data-jfb-model]');
    const model = data === null ? null : parseModel(data.textContent ?? '');
    if (model === null) return;
    enhanced.add(form);
    // What the visitor answers and leaves until the logic woke, which hears it from then on (ADR-0113): handed to it, so
    // a field they changed and left meanwhile is checked as it wakes. Only the visitor's: a script's event is no answer.
    const heard: Event[] = [];
    const hear = (event: Event): void => {
      if (event.isTrusted) heard.push(event);
    };
    for (const type of HEARD) proto(form).addEventListener(type, hear);
    const touched = (): void => {
      if (sessions.has(form)) return;
      for (const type of TOUCHES) proto(form).removeEventListener(type, touched);
      chunk ??= load().catch(() => null);
      const session = chunk.then((loaded) => loaded?.attach(form, model, heard));
      sessions.set(form, session);
      // A form with no address to post to (the element's) has nothing to answer a press once its logic cannot load:
      // the next press says so in the form's own words, tells the site, and loads the logic again, which goes on hearing.
      const settle = (woken?: unknown): void => {
        if (woken != null || proto(form).hasAttribute('action')) {
          for (const type of HEARD) proto(form).removeEventListener(type, hear);
          return;
        }
        chunk = undefined;
        sessions.delete(form);
        proto(form).addEventListener(
          'submit',
          (event) => {
            event.preventDefault();
            const notice = proto(form)
              .querySelector<HTMLTemplateElement>(':scope > template[data-jfb-templates]')
              ?.content.querySelector('[data-jfb-notice="server_error"]')
              ?.cloneNode(true);
            if (notice instanceof HTMLElement) {
              proto(form).querySelector(':scope > [data-jfb-notice]')?.remove();
              (proto(form).querySelector(':scope > .jfb-navigation') ?? proto(form).lastElementChild)?.before(notice);
              notice.focus();
            }
            proto(form).dispatchEvent(
              new CustomEvent('jfb:error', {
                bubbles: true,
                detail: { key: proto(form).dataset['jfbForm'] ?? '', alias: model.alias, code: 'server_error' },
              }),
            );
            touched();
          },
          { once: true },
        );
      };
      void session.then(settle, () => settle());
    };
    wakes.set(form, touched);
    // Counting is the site's, not the visitor's: a form that cannot count works all the same.
    alone(() => count(form));
    for (const type of TOUCHES) proto(form).addEventListener(type, touched);
    if (
      proto(form).querySelector('[data-jfb-default]') !== null ||
      proto(form).hasAttribute('data-jfb-asks') ||
      (model.form.settings?.resume === true && comesBack(form))
    )
      touched();
    proto(form).dispatchEvent(
      new CustomEvent<ReadyDetail>('jfb:ready', {
        bubbles: true,
        detail: { key: proto(form).dataset['jfbForm'] ?? '', alias: model.alias },
      }),
    );
  }

  /** The logic of a form, loaded now if the visitor has not touched it; null where it cannot load. */
  async function sessionOf(form: HTMLFormElement): Promise<Session | null> {
    if (proto(form).hasAttribute(WOKEN)) {
      const loaded = await chunk;
      return ((await loaded?.sessionOf?.(form)) as Session | null | undefined) ?? null;
    }
    wakes.get(form)?.();
    return ((await sessions.get(form)) as Session | null | undefined) ?? null;
  }

  return {
    get(form: Element): JironFormBuilderForm | null {
      if (!(form instanceof HTMLFormElement) || !proto(form).matches('[data-jfb-form]')) return null;
      alone(() => enhance(form));
      if (!proto(form).hasAttribute(WOKEN) && !wakes.has(form)) return null;
      return Object.freeze({
        validate: async () => (await (await sessionOf(form))?.validate('send')) ?? true,
        submit: async () => (await sessionOf(form))?.submit() ?? false,
        next: async () => (await sessionOf(form))?.press('next') ?? false,
        back: async () => (await sessionOf(form))?.press('back') ?? false,
        values: async () => (await sessionOf(form))?.values() ?? {},
      });
    },
    init(root: ParentNode = document): void {
      const roots = proto(root).querySelectorAll(`.jfb-form[data-jfb-contract="${CONTRACT}"]`);
      if (root instanceof Element && proto(root).matches(`.jfb-form[data-jfb-contract="${CONTRACT}"]`))
        alone(() => fit(root));
      for (const each of roots) alone(() => fit(each));
      if (root instanceof HTMLFormElement && proto(root).matches('[data-jfb-form]')) alone(() => enhance(root));
      for (const form of proto(root).querySelectorAll<HTMLFormElement>('form[data-jfb-form]'))
        alone(() => enhance(form));
      for (const button of proto(root).querySelectorAll<HTMLButtonElement>(
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
