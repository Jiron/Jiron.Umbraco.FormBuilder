/** What the runtime offers the page as `window.JironFormBuilder` (ROADMAP section 4.9). */
export interface JironFormBuilderGlobal {
  /** The version of the runtime on the page. */
  readonly version: string;
  /** Enhances the forms in a root that the page added after it loaded (the document by default); a form once only. */
  init(root?: ParentNode): void;
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
): JironFormBuilderGlobal {
  target.JironFormBuilder ??= Object.freeze({ version, init });
  return target.JironFormBuilder;
}
