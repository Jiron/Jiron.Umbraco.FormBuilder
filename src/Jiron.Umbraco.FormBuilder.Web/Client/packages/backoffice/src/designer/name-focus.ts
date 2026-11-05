// The focus a form's workspace gives its name box as it opens (ROADMAP 10.2a, ADR-0188): Umbraco's name box takes it,
// and takes it back every 100 ms until it holds it, so a focus a link asks for elsewhere waits until the box has it.

/** The longest wait: a form the user may not rename shows a heading, not the box, and nothing takes the focus. */
export const NAME_FOCUS_WAIT = 5000;

const NAME_BOX = 'umb-workspace-header-name-editable';

/** Whether the focus is in the workspace's name box, through the shadow roots it is in. */
function inNameBox(root: Document): boolean {
  let active: Element | null = root.activeElement;
  while (active) {
    if (active.localName === NAME_BOX) return true;
    active = active.shadowRoot?.activeElement ?? null;
  }
  return false;
}

/** Resolves once the workspace's name box holds the focus, at once where it does, or after `wait` ms. */
export function afterNameFocus(root: Document = document, wait = NAME_FOCUS_WAIT): Promise<void> {
  if (inNameBox(root)) return Promise.resolve();
  return new Promise((done) => {
    const finish = () => {
      root.removeEventListener('focusin', heard, true);
      clearTimeout(timer);
      done();
    };
    const heard = (event: Event) => {
      if (event.composedPath().some((one) => one instanceof Element && one.localName === NAME_BOX)) finish();
    };
    const timer = setTimeout(finish, wait);
    root.addEventListener('focusin', heard, true);
  });
}
