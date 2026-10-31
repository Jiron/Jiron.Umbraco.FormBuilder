/**
 * The headless element (ADR-0015, ADR-0128): `<jfb-form>` with `api`, `form`, `culture`, `content`, `theme` and
 * `context-<name>` attributes fetches the element endpoint's answer for the next free instance id of the document and
 * writes the built-in views' markup from it in the light DOM, so the themes, the site's CSS and the runtime behave as
 * on a Razor page. It adds the theme's stylesheets once per document and shows the form once they loaded, loads the
 * runtime once per document, hands the runtime its form, and dispatches `jfb:rendered`; an answer it cannot use leaves
 * it empty, says why in the console, and dispatches `jfb:error`.
 */
import type { JfbElementAnswer } from './answer';
import { writeForm } from './views';

/** The version of the element endpoint's answer this element reads (`ElementAnswer.Version`). */
export const ANSWER_CONTRACT = 1;

/** The start of an attribute that gives a context value, as `<jfb-render>`'s do, and of its query name. */
const CONTEXT_PREFIX = 'context-';

/** How long a form waits for its theme's stylesheets before it shows without them, in milliseconds. */
export const STYLES_WAIT = 3000;

/** What the runtime offers the page once its module ran (ROADMAP section 4.9). */
interface Runtime {
  init(root?: ParentNode): void;
}

/** Why a render shows nothing: what the console says and the code `jfb:error` carries. */
interface Failure {
  readonly reason: string;
  readonly code: string;
}

// The instance ids each document's elements took, so two elements rendering at once never share one.
const taken = new WeakMap<Document, Set<number>>();

// The site's own field types each document was told of, so each is said once.
const told = new WeakMap<Document, Set<string>>();

// Each document's stylesheets by address, settled once loaded, failed or waited for, so each is added once.
const sheets = new WeakMap<Document, Map<string, Promise<void>>>();

/** The next instance id no form of the document has, `jfb-<n>` as the server numbers them. */
export function nextInstance(document: Document): string {
  const used = taken.get(document) ?? new Set<number>();
  taken.set(document, used);
  for (const element of document.querySelectorAll('[id^="jfb-"]')) {
    const match = /^jfb-([1-9][0-9]*)$/.exec(element.id);
    if (match) used.add(Number(match[1]));
  }
  let next = 1;
  while (used.has(next)) next++;
  used.add(next);
  return `jfb-${next}`;
}

/** The element endpoint's address for the element's attributes and the instance id. */
export function answerUrl(element: Element, instance: string): string | null {
  const api = element.getAttribute('api')?.replace(/\/+$/, '');
  const form = element.getAttribute('form')?.trim();
  if (!api || !form) return null;
  const query = new URLSearchParams({ instance });
  for (const [attribute, name] of [
    ['culture', 'culture'],
    ['content', 'contentKey'],
    ['theme', 'theme'],
  ] as const) {
    const value = element.getAttribute(attribute);
    if (value !== null && value.length > 0) query.set(name, value);
  }
  for (const attribute of element.getAttributeNames()) {
    if (attribute.startsWith(CONTEXT_PREFIX) && attribute.length > CONTEXT_PREFIX.length)
      query.set(attribute, element.getAttribute(attribute) ?? '');
  }
  return `${api}/forms/${encodeURIComponent(form)}/element?${query}`;
}

/** The element endpoint's answer, or why there is none: its problem's code, or that the API could not be reached. */
async function fetchAnswer(url: string, api: string): Promise<JfbElementAnswer | Failure> {
  let response: Response;
  try {
    response = await fetch(url, { headers: { Accept: 'application/json' } });
  } catch {
    return {
      reason: `the form could not be loaded from ${api}: the API is not reachable, or it does not allow this page's origin`,
      code: 'network_error',
    };
  }
  if (!response.ok) {
    const problem = (await response.json().catch(() => null)) as { code?: unknown } | null;
    return {
      reason: `the form could not be loaded (${response.status})`,
      code: typeof problem?.code === 'string' ? problem.code : 'server_error',
    };
  }
  const answer = (await response.json().catch(() => null)) as JfbElementAnswer | null;
  return answer ?? { reason: 'the form could not be loaded: the answer is not JSON', code: 'server_error' };
}

/** Adds a stylesheet to the document unless it has it, and settles once it loaded or failed, or after a while. */
function stylesheet(document: Document, href: string): Promise<void> {
  const known = sheets.get(document) ?? new Map<string, Promise<void>>();
  sheets.set(document, known);
  let settled = known.get(href);
  if (settled !== undefined) return settled;
  let link = [...document.querySelectorAll<HTMLLinkElement>('link[rel="stylesheet"]')].find(
    (each) => each.href === href,
  );
  if (link === undefined) {
    link = document.createElement('link');
    link.rel = 'stylesheet';
    link.href = href;
    document.head.append(link);
  }
  const target = link;
  settled =
    target.sheet !== null
      ? Promise.resolve()
      : new Promise<void>((resolve) => {
          let timer = 0;
          const done = (): void => {
            clearTimeout(timer);
            resolve();
          };
          timer = setTimeout(done, STYLES_WAIT);
          target.addEventListener('load', done, { once: true });
          target.addEventListener('error', done, { once: true });
        });
  known.set(href, settled);
  return settled;
}

/** Loads the runtime's module, the API's origin its base (a module runs once per document), and hands back the runtime. */
async function runtimeOf(document: Document, script: URL): Promise<Runtime | undefined> {
  await import(/* @vite-ignore */ script.href);
  return (document.defaultView as (Window & { JironFormBuilder?: Runtime }) | null)?.JironFormBuilder;
}

/**
 * `<jfb-form>`: a published form written from the element endpoint's answer. It renders when it first joins a
 * document and again when `api`, `form`, `culture`, `content` or `theme` changes; after a change of a `context-`
 * attribute the page calls `render()`.
 */
export class JfbFormElement extends HTMLElement {
  static readonly observedAttributes = ['api', 'form', 'culture', 'content', 'theme'];

  // The render that is the element's latest; an older one's outcome is dropped.
  #render = 0;

  // The instance id the element took on its first render and keeps, so its ids stay as they are on each render.
  #instance: string | undefined;

  constructor() {
    super();
    // The form has no address to post to, which only a post without JavaScript uses (ADR-0128): it never posts as a
    // page would, the visitor's answers to the page's own address. It is sent through the runtime (6.4i).
    this.addEventListener('submit', (event) => event.preventDefault());
  }

  /** Renders the form when the element first joins a document; moved to another place, it keeps its form. */
  connectedCallback(): void {
    if (this.#render === 0) void this.render();
  }

  /** Renders the form again when one of its attributes changes once it rendered. */
  attributeChangedCallback(_name: string, before: string | null, after: string | null): void {
    if (before !== after && this.isConnected && this.#render > 0) void this.render();
  }

  /** Fetches the answer and writes the form; resolves once it is shown, or once the element stays empty. */
  async render(): Promise<void> {
    const render = ++this.#render;
    const document = this.ownerDocument;
    this.#instance ??= nextInstance(document);
    const url = answerUrl(this, this.#instance);
    const base = this.getAttribute('api')?.replace(/\/+$/, '');
    if (url === null || base === undefined)
      return this.#fail({ reason: 'the attributes api and form are both needed', code: 'invalid_request' });
    const answer = await fetchAnswer(url, base);
    if (render !== this.#render) return;
    if ('code' in answer) return this.#fail(answer);
    if (answer.contract !== ANSWER_CONTRACT) {
      return this.#fail({
        reason: `the answer's contract is ${answer.contract}, this element reads ${ANSWER_CONTRACT}; load the element from the same site`,
        code: 'contract_mismatch',
      });
    }

    const origin = new URL(base, document.baseURI).origin;
    const runtime = runtimeOf(document, new URL(answer.assets.script, origin)).catch(() => {
      console.warn('jfb-form: the website runtime could not be loaded; the form shows without it.');
      return undefined;
    });
    await Promise.all(answer.assets.styles.map((style) => stylesheet(document, new URL(style, origin).href)));
    if (render !== this.#render) return;
    this.replaceChildren(
      writeForm(
        {
          document,
          api: base,
          onCustom: (type) => {
            const said = told.get(document) ?? new Set<string>();
            told.set(document, said);
            if (said.has(type)) return;
            said.add(type);
            console.warn(
              `jfb-form: a field of type '${type}' has no control in the element; the site registers one (ADR-0128).`,
            );
          },
        },
        answer.form,
      ),
    );
    try {
      (await runtime)?.init(this);
    } catch (error) {
      console.error('jfb-form: the website runtime failed on the form.', error);
    }
    if (render !== this.#render) return;
    this.dispatchEvent(
      new CustomEvent('jfb:rendered', { bubbles: true, detail: { form: answer.form.key, instance: answer.form.id } }),
    );
  }

  // Leaves the element empty, says why in the console, and tells the page.
  #fail(failure: Failure): void {
    this.replaceChildren();
    console.error(`jfb-form: ${failure.reason}.`);
    this.dispatchEvent(new CustomEvent('jfb:error', { bubbles: true, detail: { code: failure.code } }));
  }
}

/** Registers `<jfb-form>` unless the page has it already (a second copy of the script changes nothing). */
export function defineElement(registry: CustomElementRegistry = customElements): void {
  if (!registry.get('jfb-form')) registry.define('jfb-form', JfbFormElement);
}

defineElement();
