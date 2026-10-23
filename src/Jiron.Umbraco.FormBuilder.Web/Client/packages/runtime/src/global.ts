/** What the runtime offers the page as `window.JironFormBuilder`. */
export interface JironFormBuilderGlobal {
  /** The version of the runtime on the page. */
  readonly version: string;
}

declare global {
  interface Window {
    JironFormBuilder?: JironFormBuilderGlobal;
  }
}

/**
 * Sets `JironFormBuilder` on the target unless it is there already, so a second copy of the runtime on the same page
 * (a site that includes it twice) changes nothing. Returns the global in use.
 */
export function installGlobal(
  target: { JironFormBuilder?: JironFormBuilderGlobal | undefined },
  version: string,
): JironFormBuilderGlobal {
  target.JironFormBuilder ??= Object.freeze({ version });
  return target.JironFormBuilder;
}
