/**
 * The chunk the frame loads on a form's first interaction (ROADMAP 6.4b, ADR-0110): the shared logic and what the
 * runtime does with it. 6.4b reads the page, 6.4c applies the rules to what it reads (ADR-0111), 6.4d checks the
 * answers (ADR-0112), 6.4e shows their errors (ADR-0113), 6.4f works the calculations out, with the calculation
 * language loaded first as a chunk of its own for a form that has one, and keeps the sliders' values (ADR-0114), and
 * 6.4g renders the visitor texts that recall answers as the visitor types, with the merge tags loaded first as a
 * chunk of their own for a form that has such a text (ADR-0115), and 6.4h fills in the default values that hold merge
 * tags before the rules first decide, from a chunk of its own that a form loads only when the server marked such a
 * field (ADR-0118); 6.4i sends a send that passes to the entries endpoint and shows its outcome in the form's place
 * (ADR-0120), 6.4j puts a form published again meanwhile in the old one's place, which it wakes again here, and 6.4m
 * takes a form's files on the page, from a chunk of its own that a form with a file field loads (ADR-0152).
 */
import type { Context } from '@jfb/logic';
import { loadEngine, type PageEngine } from './engine';
import { pageErrorsOf } from './errors';
import type { FormModel } from './model';
import { followSliders } from './outputs';
import { readPage, type PageReading } from './page';
import { challengesOf, senderOf, valuesOf, type Carried } from './send';
import { allOf, takesUploads, type PageUploads, type UploadOptions } from './uploads';
import type { ShownError } from './messages';
import { contextOf, rulesOf, type PageRules } from './rules';
import { checkPage, type CheckScope } from './validation';

/** An enhanced form with its logic. */
export interface FormSession {
  readonly form: HTMLFormElement;
  readonly model: FormModel;
  /** The form engine it decides by, with the calculation language when the form has a calculation. */
  readonly engine: PageEngine;
  /** The form's answers as its controls hold them now. */
  read(): PageReading;
  /** The form's rules and calculations, shown on the page as the visitor answers; null for a form with neither. */
  readonly rules: PageRules | null;
  /** The errors the page shows for its answers now (ADR-0112): a send's, or the page's that Next checks. */
  check(scope: CheckScope): Promise<ShownError[]>;
  /** Checks as a send or Next does and shows what stops it on the page (ADR-0113); true when nothing does. */
  validate(scope: CheckScope): Promise<boolean>;
  /** Presses the form's send button; false when the form shows none now. */
  submit(): boolean;
  /** The answers as the page would send them. */
  values(): Readonly<Record<string, unknown>>;
}

/** The logic of each form, for a site's code to reach, the form that came in another's place among them. */
const sessions = new WeakMap<HTMLFormElement, Promise<FormSession | null>>();

/** The logic given to a form, if any. */
export function sessionOf(form: HTMLFormElement): Promise<FormSession | null> | undefined {
  return sessions.get(form);
}

/** Fills in the default values the server marked, from a chunk of its own. */
export type FillDefaults = (form: HTMLFormElement, model: FormModel, context: Context) => void;

let filler: Promise<FillDefaults> | undefined;

/**
 * The filler of the default values that hold merge tags, imported once for the page from the merge tags' chunk, which
 * answer recall is in too: one way into the chunk, so the bundler adds no helper chunk for two (ADR-0118).
 */
export function loadDefaults(): Promise<FillDefaults> {
  return (filler ??= import('./recall').then((module) => module.fillDefaults));
}

/**
 * Gives a form its logic; the frame calls it once per form. A form whose calculation language or defaults' filler
 * cannot load is left to the post without JavaScript (null), as one whose chunk cannot.
 */
export function attach(
  form: HTMLFormElement,
  model: FormModel,
  defaults: () => Promise<FillDefaults> = loadDefaults,
  carried?: Carried,
): Promise<FormSession | null> {
  const session = wake(form, model, defaults, carried);
  sessions.set(form, session);
  return session;
}

async function wake(
  form: HTMLFormElement,
  model: FormModel,
  defaults: () => Promise<FillDefaults>,
  carried?: Carried,
): Promise<FormSession | null> {
  const loaded = await Promise.all([
    loadEngine(model),
    form.querySelector('[data-jfb-default]') === null ? null : defaults(),
  ]).catch(() => null);
  if (loaded === null) return null;
  const [engine, fill] = loaded;
  fill?.(form, model, contextOf(model, readPage(form, model), Date.now(), form.ownerDocument.URL));
  const rules = rulesOf(form, model, engine);
  followSliders(form, model);
  const api = form.dataset['jfbApi'];
  const challenges =
    api !== undefined && takesUploads(form, model) ? challengesOf(form, api, carried?.challenge) : undefined;
  const uploading: UploadOptions | undefined =
    api === undefined || challenges === undefined
      ? undefined
      : {
          api,
          challenge: challenges.current,
          renew: challenges.renew,
          ...(carried?.files === undefined ? {} : { restored: carried.files }),
        };
  const files: PageUploads | undefined =
    uploading === undefined
      ? undefined
      : await Promise.all([
          form.querySelector('.jfb-upload') === null
            ? []
            : import('./uploads-chunk').then((module) => [module.enhanceUploads(form, model, uploading)]),
          form.querySelector('.jfb-signature') === null
            ? []
            : import('./signature-chunk').then((module) => [module.enhanceSignatures(form, model, uploading)]),
        ])
          .then((parts) => allOf(parts.flat()))
          .catch(() => undefined);
  const errors = pageErrorsOf(
    form,
    model,
    engine,
    Date.now,
    senderOf(form, model, {
      ...(carried === undefined ? {} : { id: carried.id, challenge: carried.challenge }),
      ...(files === undefined || challenges === undefined ? {} : { files, challenges }),
      rewake: (next, nextModel, carry) => void attach(next, nextModel, defaults, carry),
    }),
  );
  return {
    form,
    model,
    engine,
    read: () => readPage(form, model),
    rules,
    check: (scope) => checkPage(form, model, engine, scope),
    validate: (scope) => errors.validate(scope),
    submit: () => {
      const button = form.querySelector<HTMLButtonElement>('button[data-jfb-nav="submit"]');
      if (button === null) return false;
      form.requestSubmit(button);
      return true;
    },
    values: () => valuesOf(readPage(form, model).post),
  };
}
