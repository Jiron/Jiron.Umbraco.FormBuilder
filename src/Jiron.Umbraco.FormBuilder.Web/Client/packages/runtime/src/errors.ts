/**
 * When the page shows errors (ROADMAP 6.4e, ADR-0113): a field's as the visitor leaves it, once they changed it or
 * while it shows one; gone on the input that fixes it; and after a refused send or Next every error of its scope, with
 * the error summary, which takes the focus, and `jfb:invalid`. A passing check lets the post go on as without
 * JavaScript, or the sender sends it (6.4i, ADR-0120), whose refusal the server's errors show as the page's own. The
 * server's errors stay while their field is unchanged, the ones only the server can make among them.
 */
import { fieldsOf } from './page';
import {
  clearFieldError,
  errorIn,
  focusTarget,
  removeSummary,
  showFieldError,
  showSummary,
  showsErrors,
} from './display';
import type { MessageWords } from '@jfb/logic';
import type { PageEngine } from './engine';
import { elementOf, wordsFor, type ShownError } from './messages';
import type { FormModel } from './model';
import type { Deliver } from './send';
import { checkFields, checkPage, serverErrors, type CheckScope, type ServerProblem } from './validation';

export type { ServerProblem } from './validation';

/** What `jfb:invalid` tells: the form, the check's scope, and each error's field and kind; never an answer. */
export interface InvalidDetail {
  readonly key: string;
  readonly alias: string;
  readonly scope: CheckScope;
  /** Each error's field by the path the page names it by (`data-jfb-alias`), null for the empty submission. */
  readonly errors: readonly { readonly field: string | null; readonly kind: string }[];
}

/** A form's errors on the page. */
export interface PageErrors {
  /**
   * Checks the answers as a send or Next does and shows what stops them; true when nothing the page shows does,
   * which leaves the post, and what only the server or another page can show, to the server.
   */
  validate(scope: CheckScope): Promise<boolean>;
  /**
   * Shows the server's errors of a refused send as a refused send shows the page's, kept while their field is
   * unchanged; false when one is at a field of another page, or none is at a field, which leaves them to the post.
   */
  server(problems: readonly ServerProblem[]): Promise<boolean>;
}

const FIELD = '[data-jfb-field]';
const NAV = 'jfb-nav';
/** The kind `jfb:invalid` gives an error the server showed after a post without JavaScript. */
const SERVER = 'server';
/** How long the live region stays empty before it says an error, so the same words are said again. */
const SPEAK_AFTER = 100;

/** An error the server showed: the control it names and its summary link's text. */
interface Kept {
  readonly target: string;
  readonly link: string;
}

/** Shows a form's errors as its visitor answers, leaves and sends; a send that passes goes to `deliver` first. */
export function pageErrorsOf(
  form: HTMLFormElement,
  model: FormModel,
  engine: PageEngine,
  clock: () => number = Date.now,
  deliver?: Deliver,
): PageErrors {
  const changed = new WeakSet<HTMLElement>();
  const kept = keptOf(form);
  let checking = false;
  let passing = false;
  let queued = false;
  let speaking: ReturnType<typeof setTimeout> | undefined;

  const fieldOf = (node: EventTarget | null): HTMLElement | null =>
    node instanceof Element ? node.closest<HTMLElement>(FIELD) : null;
  const ruledOut = (field: HTMLElement): boolean => field.closest('[data-jfb-hidden]') !== null;
  const offPage = (field: HTMLElement): boolean => field.closest('[data-jfb-page][hidden]') !== null;

  /** The field an error shows at: the one that holds the control it names, else the field by its key. */
  const fieldAt = (error: ShownError): HTMLElement | null => {
    if (error.code === 'empty') return null;
    const target = form.ownerDocument.getElementById(error.target);
    if (target !== null && form.contains(target)) return target.closest<HTMLElement>(FIELD);
    const group =
      error.row === null
        ? null
        : (fieldsOf(model).find((each) => each.fields?.some((child) => child.key === error.field))?.key ?? null);
    return elementOf(form, error.field, error.row, group);
  };

  /** Each field's first error. */
  const byField = (errors: readonly ShownError[]): Map<HTMLElement, ShownError> => {
    const found = new Map<HTMLElement, ShownError>();
    for (const error of errors) {
      const field = fieldAt(error);
      if (field !== null && !found.has(field)) found.set(field, error);
    }
    return found;
  };

  const liveRegion = (): HTMLElement | null => form.querySelector<HTMLElement>(':scope > [data-jfb-live]');
  const hush = (): void => {
    clearTimeout(speaking);
    const live = liveRegion();
    if (live !== null) live.textContent = '';
  };
  const say = (text: string): void => {
    hush();
    const live = liveRegion();
    if (live !== null) speaking = setTimeout(() => (live.textContent = text), SPEAK_AFTER);
  };

  /** The summary goes once no error shows. */
  const tidy = (): void => {
    if (!showsErrors(form)) removeSummary(form);
  };

  /** Each error the form shows that is gone now, or at a field the rules hide, is taken away. */
  async function recheck(): Promise<void> {
    const errors = await safely(() => checkFields(form, model, engine, clock));
    if (errors === null) return;
    const now = byField(errors);
    for (const paragraph of [...form.querySelectorAll<HTMLElement>('[data-jfb-error]')]) {
      const field = paragraph.closest<HTMLElement>(FIELD);
      if (field === null) continue;
      if (ruledOut(field) || (!now.has(field) && !kept.has(field))) {
        clearFieldError(field);
        kept.delete(field);
      }
    }
    tidy();
  }

  /** A field the visitor leaves shows its error now, said once in the live region, or none. */
  async function leave(field: HTMLElement): Promise<void> {
    const errors = await safely(() => checkFields(form, model, engine, clock));
    if (errors === null) return;
    const error = byField(errors).get(field);
    if (error !== undefined && !ruledOut(field)) {
      const fresh = errorIn(field) === null;
      showFieldError(form, field, error);
      kept.delete(field);
      if (fresh) say(error.link);
    } else if (!kept.has(field) || ruledOut(field)) {
      clearFieldError(field);
      kept.delete(field);
    }
    tidy();
  }

  /** The server's errors that still stand at fields without one of the runtime's, in the page's order. */
  const withKept = (errors: readonly ShownError[]): ShownError[] => {
    const merged = [...errors];
    const errored = new Set(errors.map(fieldAt));
    for (const [field, keep] of kept) {
      const paragraph = errorIn(field);
      if (errored.has(field) || paragraph === null || ruledOut(field) || offPage(field)) continue;
      const error: ShownError = {
        field: field.dataset['jfbField'] ?? '',
        row: null,
        code: SERVER,
        target: keep.target,
        message: (paragraph.textContent ?? '').trim(),
        link: keep.link,
      };
      const at = merged.findIndex((other) => {
        const next = fieldAt(other);
        return next !== null && (field.compareDocumentPosition(next) & Node.DOCUMENT_POSITION_FOLLOWING) !== 0;
      });
      if (at < 0) merged.push(error);
      else merged.splice(at, 0, error);
    }
    return merged;
  };

  // Until 6.5 pages in the browser, the server shows the first page that holds an error.
  const elsewhere = (errors: readonly ShownError[]): boolean =>
    errors.some((error) => {
      if (error.code === 'empty') return false;
      const field = fieldAt(error);
      return field === null || offPage(field);
    });

  async function validate(scope: CheckScope): Promise<boolean> {
    const found = await safely(() => checkPage(form, model, engine, scope, clock));
    const words = found === null ? null : await safely(() => wordsFor(model.culture));
    if (found === null || words === null || elsewhere(found)) return true;
    const errors = withKept(found);
    if (errors.length === 0) return true;
    present(errors, scope, words);
    return false;
  }

  async function server(problems: readonly ServerProblem[]): Promise<boolean> {
    const found = await safely(() => serverErrors(form, model, engine, problems, clock));
    const words = found === null ? null : await safely(() => wordsFor(model.culture));
    if (found === null || words === null || found.length === 0 || elsewhere(found)) return false;
    present(found, 'send', words);
    for (const error of found) {
      const field = fieldAt(error);
      if (field !== null) kept.set(field, { target: error.target, link: error.link });
    }
    return true;
  }

  /** The errors at their fields and in the summary, which takes the focus, and `jfb:invalid`. */
  function present(errors: readonly ShownError[], scope: CheckScope, words: MessageWords): void {
    hush();
    const shown = byField(errors);
    for (const paragraph of [...form.querySelectorAll<HTMLElement>('[data-jfb-error]')]) {
      const field = paragraph.closest<HTMLElement>(FIELD);
      if (field !== null && !shown.has(field)) clearFieldError(field);
    }
    for (const [field, error] of shown) showFieldError(form, field, error);
    const listed =
      model.form.settings?.validationSummary === false ? errors.filter((error) => error.code === 'empty') : errors;
    if (listed.length > 0) showSummary(form, listed, words).focus();
    else {
      removeSummary(form);
      const first = errors.find((error) => error.code !== 'empty');
      if (first !== undefined) focusTarget(form, first.target);
    }
    form.dispatchEvent(
      new CustomEvent<InvalidDetail>('jfb:invalid', {
        bubbles: true,
        detail: {
          key: form.dataset['jfbForm'] ?? '',
          alias: model.alias,
          scope,
          errors: errors.map((error) => ({
            field: error.code === 'empty' ? null : (fieldAt(error)?.dataset['jfbAlias'] ?? null),
            kind: error.code,
          })),
        },
      }),
    );
  }

  /** The post goes on as without JavaScript, with the button the visitor pressed. */
  const send = (button: HTMLButtonElement): void => {
    passing = true;
    try {
      if (typeof form.requestSubmit === 'function') form.requestSubmit(button);
      else {
        const nav = form.ownerDocument.createElement('input');
        nav.type = 'hidden';
        nav.name = NAV;
        nav.value = button.value;
        form.append(nav);
        form.submit();
      }
    } finally {
      passing = false;
    }
  };

  form.addEventListener('submit', (event) => {
    if (passing) return;
    const button = event.submitter;
    if (!(button instanceof HTMLButtonElement) || button.name !== NAV) return;
    if (button.value !== 'submit' && button.value !== 'next') return;
    event.preventDefault();
    if (checking) return;
    checking = true;
    void validate(button.value === 'next' ? 'page' : 'send')
      .then((passes) => !passes || (button.value === 'submit' && deliver !== undefined && deliver(button, errors)))
      .then(
        (answered) => {
          if (answered) {
            checking = false;
            return;
          }
          // The press's own submit event may still be running: the browser runs its listeners' microtasks before it
          // ends, and a form ignores a submission asked for while it fires one. The post goes on in a task of its own.
          setTimeout(() => {
            checking = false;
            send(button);
          });
        },
        () => (checking = false),
      );
  });

  const answered = (event: Event): void => {
    const field = fieldOf(event.target);
    if (field !== null) {
      changed.add(field);
      kept.delete(field);
    }
    if (queued || (!showsErrors(form) && form.querySelector(':scope > [data-jfb-summary]') === null)) return;
    queued = true;
    queueMicrotask(() => {
      queued = false;
      void recheck();
    });
  };
  form.addEventListener('input', answered);
  form.addEventListener('change', answered);

  form.addEventListener('focusout', (event) => {
    const field = fieldOf(event.target);
    if (field === null) return;
    const next = event.relatedTarget;
    if (next instanceof Node && field.contains(next)) return;
    if (changed.has(field) || errorIn(field) !== null) void leave(field);
  });

  form.addEventListener('click', (event) => {
    const link = event.target instanceof Element ? event.target.closest('a[href^="#"]') : null;
    if (link === null || link.closest('[data-jfb-summary]')?.parentElement !== form) return;
    if (focusTarget(form, (link.getAttribute('href') ?? '').slice(1))) event.preventDefault();
  });

  const errors: PageErrors = { validate, server };
  return errors;
}

/** The errors the server showed at fields after a post without JavaScript, with their summary links' texts. */
function keptOf(form: HTMLFormElement): Map<HTMLElement, Kept> {
  const links = new Map<string, string>();
  for (const link of form.querySelectorAll(':scope > [data-jfb-summary] a[href^="#"]'))
    links.set((link.getAttribute('href') ?? '').slice(1), (link.textContent ?? '').trim());
  const kept = new Map<HTMLElement, Kept>();
  for (const paragraph of form.querySelectorAll<HTMLElement>('[data-jfb-error]')) {
    const field = paragraph.closest<HTMLElement>(FIELD);
    const target = paragraph.id.endsWith('-error') ? paragraph.id.slice(0, -'-error'.length) : '';
    if (field !== null && target.length > 0)
      kept.set(field, { target, link: links.get(target) ?? (paragraph.textContent ?? '').trim() });
  }
  return kept;
}

/** What a promise gives, or null when it fails: a check that cannot run leaves the answers to the server. */
async function safely<T>(run: () => Promise<T>): Promise<T | null> {
  try {
    return await run();
  } catch {
    return null;
  }
}
