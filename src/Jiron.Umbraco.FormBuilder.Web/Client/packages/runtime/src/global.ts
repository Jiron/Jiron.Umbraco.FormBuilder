/** A form as a site's own code works with it, which loads the form's logic when it is first asked (ROADMAP section 4.9). */
export interface JironFormBuilderForm {
  /**
   * Checks the form as a send does and shows what stops it on the page. True when nothing does, and when the logic
   * could not load, as the server checks every send.
   */
  validate(): Promise<boolean>;
  /**
   * Presses the form's send button, with what a visitor's press does: the checks, the send and its events. False when the
   * form shows no such button now, as on a page before its last.
   */
  submit(): Promise<boolean>;
  /** The answers as the page would send them: each one's texts by its alias, a part as `alias.part`, a group's rows as objects. */
  values(): Promise<Readonly<Record<string, unknown>>>;
}

/** What the runtime offers the page as `window.JironFormBuilder` (ROADMAP section 4.9). */
export interface JironFormBuilderGlobal {
  /** The version of the runtime on the page. */
  readonly version: string;
  /** Enhances the forms in a root that the page added after it loaded (the document by default); a form once only. */
  init(root?: ParentNode): void;
  /** A form of the markup contract to work with; null for any other element. */
  get(form: Element): JironFormBuilderForm | null;
}

declare global {
  interface Window {
    JironFormBuilder?: JironFormBuilderGlobal;
  }
}

/**
 * Sets `JironFormBuilder` on the target unless it is there already, so a second copy of the runtime on the same page
 * (a site that includes it twice) changes nothing and enhances through the first. Returns the global in use.
 */
export function installGlobal(
  target: { JironFormBuilder?: JironFormBuilderGlobal | undefined },
  version: string,
  init: (root?: ParentNode) => void,
  get: (form: Element) => JironFormBuilderForm | null,
): JironFormBuilderGlobal {
  target.JironFormBuilder ??= Object.freeze({ version, init, get });
  return target.JironFormBuilder;
}
