/**
 * Forms covered as a browser covers them, for the runtime's and the element's tests. A browser answers a read on a form
 * with the form's control of that name in front of every built-in member, and a field's control is named by its alias:
 * on a form with a field called `dataset`, `form.dataset` is that field's control. The test DOM does not do this, so
 * every form a test makes is covered here: for each name on the form's prototype chain that an alias could be (a
 * lowercase letter, then letters and digits, 64 at most), the form gets a member of its own that answers a read from the
 * website code with a control of that name, and refuses a write as the browser does. The website code reads a form's
 * members through `proto(form)`, which never meets these; a plain read meets them and fails as it would on the page.
 * A test's own reads still get the form's members, so the suites keep reading their forms as they always did.
 */
import { proto } from '@jfb/logic/proto';

/** The names an alias can take, as Core checks them. */
const ALIAS = /^[a-z][A-Za-z0-9]{0,63}$/;

/** The website code's own files, which the cover answers; not the tests, their support, or the test tools. */
const WEBSITE =
  /[\\/]packages[\\/](?:runtime|element|logic)[\\/]src[\\/](?![^\\/:]*(?:\.test|testing[^\\/:]*)\.ts)[^\\/:]+\.ts/;

const covered = new WeakSet<HTMLFormElement>();

/**
 * The frames between a read or a write and the cover that do neither themselves: the cover's own, the engine's own
 * functions (`Reflect.get`, which have no file), and the proxy the test DOM makes of a form with the helper that sets
 * through it, which hand it on. Any other frame is the reader: the website code, a test, or a tool such as the test
 * DOM's selector engine, which a browser runs natively and which gets the form's own members. testing-cover.test.ts
 * fails when one of these stops being passed over.
 */
const PASSING = /^$|testing-cover\.ts$|[\\/]jsdom[\\/]lib[\\/]generated[\\/]idl[\\/](?:HTMLFormElement|utils)\.js$/;

/** The part of the engine's call site the cover reads: the file of a frame, or the name its source gives itself. */
interface CallSite {
  getScriptNameOrSourceURL(): string | null | undefined;
}

/**
 * Whether the read or write that reached the cover came from the website code: the first frame below it that reads.
 * The frames are the engine's call sites, not the stack's text, which the test runner maps to the sources at a cost
 * every read of a test would pay.
 */
function fromWebsite(): boolean {
  const engine = Error as unknown as { prepareStackTrace?: ((error: Error, sites: CallSite[]) => unknown) | undefined };
  const prepare = engine.prepareStackTrace;
  engine.prepareStackTrace = (_, sites) => sites;
  try {
    const sites = (new Error().stack as unknown as CallSite[] | undefined) ?? [];
    const reader = sites.map((site) => site.getScriptNameOrSourceURL() ?? '').find((file) => !PASSING.test(file));
    return WEBSITE.test(reader ?? '');
  } finally {
    engine.prepareStackTrace = prepare;
  }
}

/** Covers one form, once. */
function coverForm(form: HTMLFormElement): void {
  if (covered.has(form)) return;
  covered.add(form);
  const controls = new Map<string, HTMLInputElement>();
  const names = new Set<string>();
  for (
    let link: object | null = Object.getPrototypeOf(form) as object;
    link !== null;
    link = Object.getPrototypeOf(link) as object | null
  )
    for (const name of Object.getOwnPropertyNames(link)) if (ALIAS.test(name)) names.add(name);
  for (const name of names) {
    Object.defineProperty(form, name, {
      configurable: true,
      get(this: HTMLFormElement): unknown {
        if (!fromWebsite()) return Reflect.get(Object.getPrototypeOf(this) as object, name, this);
        let control = controls.get(name);
        if (control === undefined) {
          control = proto(form).ownerDocument.createElement('input');
          control.name = name;
          controls.set(name, control);
        }
        return control;
      },
      set(this: HTMLFormElement, value: unknown): void {
        if (fromWebsite()) throw new TypeError(`Cannot assign to read only property '${name}' of a form`);
        // A test's own write: a method is stubbed through stubMembers, as the website code reads past the form itself.
        if (!Reflect.set(Object.getPrototypeOf(this) as object, name, value, this))
          throw new TypeError(`'${name}' of a covered form: stub it with stubMembers`);
      },
    });
  }
}

/** Covers the forms in a tree, the tree itself and its templates' contents included. */
function coverIn(root: Node | null | undefined): void {
  if (root === null || root === undefined) return;
  if (root instanceof HTMLFormElement) coverForm(root);
  if (!(root instanceof Element || root instanceof Document || root instanceof DocumentFragment)) return;
  if (root instanceof HTMLTemplateElement) coverIn(root.content);
  for (const each of proto(root).querySelectorAll('form, template'))
    if (each instanceof HTMLTemplateElement) coverIn(each.content);
    else if (each instanceof HTMLFormElement) coverForm(each);
}

/** Runs a method or setter as it was, then covers the forms it made. */
function after<T extends object>(
  owner: T,
  name: string,
  where: (self: Node, result: unknown) => (Node | null | undefined)[],
): void {
  const descriptor = Object.getOwnPropertyDescriptor(owner, name);
  if (descriptor === undefined) return;
  if (typeof descriptor.value === 'function') {
    const method = descriptor.value as (this: unknown, ...args: unknown[]) => unknown;
    Object.defineProperty(owner, name, {
      ...descriptor,
      value(this: Node, ...args: unknown[]): unknown {
        const result = method.apply(this, args);
        for (const each of where(this, result)) coverIn(each);
        return result;
      },
    });
  } else if (descriptor.set !== undefined) {
    const set = descriptor.set;
    Object.defineProperty(owner, name, {
      ...descriptor,
      set(this: Node, value: unknown): void {
        const parent = this.parentNode;
        set.call(this, value);
        for (const each of where(this, parent)) coverIn(each);
      },
    });
  }
}

/**
 * Covers every form the window's document makes from now on: written as markup, made, cloned or taken in. Each test
 * file of the runtime and the element runs with it (testing-setup.ts).
 */
export function coverForms(window: Window & typeof globalThis): void {
  const made = (_: Node, result: unknown): Node[] => [result as Node];
  const self = (node: Node): Node[] => [node];
  after(window.Element.prototype, 'innerHTML', self);
  after(window.Element.prototype, 'outerHTML', (_, parent) => [parent as Node | null]);
  after(window.Element.prototype, 'insertAdjacentHTML', (node) => [node.parentNode, node]);
  after(window.Element.prototype, 'setHTMLUnsafe', self);
  after(window.ShadowRoot.prototype, 'innerHTML', self);
  after(window.Document.prototype, 'createElement', made);
  after(window.Document.prototype, 'createElementNS', made);
  after(window.Document.prototype, 'importNode', made);
  after(window.Node.prototype, 'cloneNode', made);
  after(window.Range.prototype, 'createContextualFragment', made);
  after(window.DOMParser.prototype, 'parseFromString', made);
}

/**
 * Gives one object members of its own where the website code reads them, as a link of its prototype chain of its own:
 * a stub set on a form itself is what a control of that name looks like, which `proto(form)` reads past. Returns what
 * takes the stubs away again.
 */
export function stubMembers<T extends object>(target: T, members: { readonly [K in keyof T]?: unknown }): () => void {
  const was = Object.getPrototypeOf(target) as object;
  const link = Object.create(was) as object;
  for (const [name, value] of Object.entries(members))
    Object.defineProperty(link, name, { configurable: true, writable: true, value });
  Object.setPrototypeOf(target, link);
  return () => {
    if (Object.getPrototypeOf(target) === link) Object.setPrototypeOf(target, was);
  };
}
