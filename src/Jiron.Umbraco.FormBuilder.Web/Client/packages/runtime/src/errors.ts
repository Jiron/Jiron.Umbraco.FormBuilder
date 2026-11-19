/**
 * When the page shows errors (ROADMAP 6.4e, ADR-0113): a field's as the visitor leaves it, once they changed it or
 * while it shows one, and when they leave it with a press in the form, after the press's click, so the error never moves
 * what was pressed, such as Next, from under the pointer; gone on the input that fixes it; and after a refused send or Next every error of its scope, with
 * the error summary, which takes the focus, and `jfb:invalid`. A passing check lets the post go on as without
 * JavaScript, or the sender sends it (6.4i, ADR-0120), whose refusal the server's errors show as the page's own. The
 * server's errors after a post without JavaScript stay while their field is unchanged, the ones only the server can
 * make among them, and join a send the page's own checks refuse; a send they alone would stop goes on, and the server
 * checks it again. A field the page leaves out, as one a member's own value stands in for (ADR-0124), is the server's
 * to check (6.8b).
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
import { proto } from '@jfb/logic/proto';
import type { PageEngine } from './engine';
import { elementOf, formWords, type ShownError } from './messages';
import type { FormModel } from './model';
import type { Paging } from './pages-chunk';
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

/** `Node.DOCUMENT_POSITION_FOLLOWING`, which the build cannot shorten. */
const FOLLOWING = 4;

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
  pages?: Paging,
): PageErrors {
  const changed = new WeakSet<HTMLElement>();
  const kept = keptOf(form);
  let checking = false;
  let passing = false;
  let queued = false;
  let speaking: ReturnType<typeof setTimeout> | undefined;

  const fieldOf = (node: EventTarget | null): HTMLElement | null =>
    node instanceof Element ? proto(node).closest<HTMLElement>(FIELD) : null;
  const ruledOut = (field: HTMLElement): boolean => field.closest('[data-jfb-hidden]') !== null;
  const offPage = (field: HTMLElement | null): boolean => field?.closest('[data-jfb-page][hidden]') != null;

  /** The field an error shows at: the one that holds the control it names, else the field by its key. */
  const fieldAt = (error: ShownError): HTMLElement | null => {
    if (error.field === '') return null;
    const target = proto(form).ownerDocument.getElementById(error.target);
    if (target !== null && proto(form).contains(target)) return fieldOf(target);
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

  const liveRegion = (): HTMLElement | null => proto(form).querySelector<HTMLElement>(':scope > [data-jfb-live]');
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
    for (const paragraph of [...proto(form).querySelectorAll<HTMLElement>('[data-jfb-error]')]) {
      const field = fieldOf(paragraph);
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
      // A row removed in place takes its errors with it, and those after it move up with their ids.
      if (!field.isConnected) {
        kept.delete(field);
        continue;
      }
      const paragraph = errorIn(field);
      if (errored.has(field) || paragraph === null || ruledOut(field) || offPage(field)) continue;
      const error: ShownError = {
        field: field.dataset['jfbField'] ?? '',
        row: null,
        code: SERVER,
        target: paragraph.id.endsWith('-error') ? paragraph.id.slice(0, -'-error'.length) : keep.target,
        message: (paragraph.textContent ?? '').trim(),
        link: keep.link,
      };
      const at = merged.findIndex((other) => {
        const next = fieldAt(other);
        return next !== null && (field.compareDocumentPosition(next) & FOLLOWING) !== 0;
      });
      if (at < 0) merged.push(error);
      else merged.splice(at, 0, error);
    }
    return merged;
  };

  // A form that posts between its pages leaves it to the server to show the first page that holds an error.
  const elsewhere = (errors: readonly ShownError[]): boolean =>
    errors.some((error) => {
      if (error.field === '') return false;
      const field = fieldAt(error);
      return field === null || offPage(field);
    });

  /** The errors of the first page that holds one, which a form that moves on the page shows now; null to leave them to the post. */
  const ofOnePage = (errors: readonly ShownError[]): readonly ShownError[] | null => {
    if (!elsewhere(errors)) return errors;
    const fields = errors.filter((error) => error.field !== '').map(fieldAt);
    if (pages === undefined || fields.includes(null)) return null;
    const first = (fields as HTMLElement[]).reduce((one, other) =>
      (one.compareDocumentPosition(other) & FOLLOWING) !== 0 ? one : other,
    );
    pages.reveal(first);
    return errors.filter((error) => error.field === '' || !offPage(fieldAt(error)));
  };

  async function validate(scope: CheckScope): Promise<boolean> {
    const found = await safely(() => checkPage(form, model, engine, scope, clock));
    const words = found === null ? null : await safely(() => formWords(model));
    if (found === null || words === null) return true;
    const here = found.filter((error) => error.field === '' || fieldAt(error) !== null);
    const shown = here.length === 0 ? null : ofOnePage(here);
    if (shown === null) return true;
    present(withKept(shown), scope, words);
    return false;
  }

  async function server(problems: readonly ServerProblem[]): Promise<boolean> {
    const found = await safely(() => serverErrors(form, model, engine, problems, clock));
    const words = found === null ? null : await safely(() => formWords(model));
    const shown = found === null || found.length === 0 ? null : ofOnePage(found);
    if (shown === null || words === null) return false;
    present(shown, 'send', words);
    for (const error of shown) {
      const field = fieldAt(error);
      if (field !== null) kept.set(field, { target: error.target, link: error.link });
    }
    return true;
  }

  /** The errors at their fields and in the summary, which takes the focus, and `jfb:invalid`. */
  function present(errors: readonly ShownError[], scope: CheckScope, words: MessageWords): void {
    hush();
    const shown = byField(errors);
    for (const paragraph of [...proto(form).querySelectorAll<HTMLElement>('[data-jfb-error]')]) {
      const field = fieldOf(paragraph);
      if (field !== null && !shown.has(field)) clearFieldError(field);
    }
    // The server's own stay as it marked them, as only it knows a grid's row.
    for (const [field, error] of shown) if (error.code !== SERVER) showFieldError(form, field, error);
    const listed =
      model.form.settings?.validationSummary === false ? errors.filter((error) => error.field === '') : errors;
    if (listed.length > 0) showSummary(form, listed, words).focus();
    else {
      removeSummary(form);
      const first = errors.find((error) => error.field !== '');
      if (first !== undefined) focusTarget(form, first.target);
    }
    proto(form).dispatchEvent(
      new CustomEvent<InvalidDetail>('jfb:invalid', {
        bubbles: true,
        detail: {
          key: proto(form).dataset['jfbForm'] ?? '',
          alias: model.alias,
          scope,
          errors: errors.map((error) => ({
            field: error.field === '' ? null : (fieldAt(error)?.dataset['jfbAlias'] ?? null),
            kind: error.code,
          })),
        },
      }),
    );
  }

  /** The post goes on as without JavaScript, with the button the visitor pressed. */
  const send = (button: HTMLButtonElement): void => {
    passing = true;
    proto(form).requestSubmit(button);
    passing = false;
  };

  proto(form).addEventListener('submit', (event) => {
    // A browser that cannot send on with the button pressed posts as without JavaScript, and the server checks it.
    if (passing || !('requestSubmit' in proto(form))) return;
    // No other button leaves the page while a check or a send is on its way: its entry may be stored by then.
    if (checking) {
      event.preventDefault();
      return;
    }
    const button = event.submitter;
    if (!(button instanceof HTMLButtonElement) || button.name !== NAV) return;
    if (button.value === 'back' && pages?.back() === true) event.preventDefault();
    if (button.value !== 'submit' && button.value !== 'next') return;
    event.preventDefault();
    checking = true;
    void validate(button.value === 'next' ? 'page' : 'send')
      .then((passes) => {
        if (!passes) return true;
        if (button.value === 'next') return pages?.next(button, errors) ?? false;
        return deliver !== undefined && deliver(button, errors);
      })
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
    if (queued || (!showsErrors(form) && proto(form).querySelector(':scope > [data-jfb-summary]') === null)) return;
    queued = true;
    queueMicrotask(() => {
      queued = false;
      void recheck();
    });
  };
  proto(form).addEventListener('input', answered);
  proto(form).addEventListener('change', answered);

  proto(form).addEventListener('focusout', (event) => {
    const field = fieldOf(event.target);
    if (field === null) return;
    const next = event.relatedTarget;
    if (next instanceof Node && field.contains(next)) return;
    if (!changed.has(field) && errorIn(field) === null) return;
    // The browser marks what a press holds down before the focus moves; its click comes once the press ends, and a
    // press on Next or Send checks the page itself.
    if (proto(form).querySelector(':active'))
      proto(form).addEventListener('click', () => void leave(field), { once: true });
    else void leave(field);
  });

  proto(form).addEventListener('click', (event) => {
    const link = event.target instanceof Element ? proto(event.target).closest('a[href^="#"]') : null;
    if (link === null || link.closest('[data-jfb-summary]')?.parentElement !== form) return;
    if (focusTarget(form, (link.getAttribute('href') ?? '').slice(1))) event.preventDefault();
  });

  // An answer given before this woke changed nothing it heard: its error goes now if the answer fixed it.
  if (showsErrors(form)) void recheck();
  const errors: PageErrors = { validate, server };
  return errors;
}

/** Whether the visitor changed a field's answer since the page came: before the runtime woke, as it hears later ones. */
function changedSinceServed(field: HTMLElement): boolean {
  return (
    [...field.querySelectorAll<HTMLInputElement>('input,textarea')].some(
      (each) => each.checked !== each.defaultChecked || each.value !== each.defaultValue,
    ) || [...field.querySelectorAll('option')].some((each) => each.index > 0 && each.selected !== each.defaultSelected)
  );
}

/** The errors the server showed at fields still as it saw them after a post without JavaScript, with their summary links' texts. */
function keptOf(form: HTMLFormElement): Map<HTMLElement, Kept> {
  const links = new Map<string, string>();
  for (const link of proto(form).querySelectorAll(':scope > [data-jfb-summary] a[href^="#"]'))
    links.set((link.getAttribute('href') ?? '').slice(1), (link.textContent ?? '').trim());
  const kept = new Map<HTMLElement, Kept>();
  for (const paragraph of proto(form).querySelectorAll<HTMLElement>('[data-jfb-error]')) {
    const field = paragraph.closest<HTMLElement>(FIELD);
    const target = paragraph.id.endsWith('-error') ? paragraph.id.slice(0, -'-error'.length) : '';
    if (field !== null && target.length > 0 && !changedSinceServed(field))
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
