// The focus a form's workspace gives its name box as it opens (ROADMAP 10.2a, ADR-0188): Umbraco's name box takes it,
// then looks 100 ms later whether it holds it and takes it again where it doesn't, which its first try, still on its
// way, can miss. A focus a link asks for elsewhere waits until the box has held the focus past that look. A press of a
// key or a pointer before then is the user's own move, which the link's focus does not take over.

/** The longest wait, for a name box that never takes the focus. */
export const NAME_FOCUS_WAIT = 5000;

/** How long the name box holds the focus before the wait is over: past its next look, 100 ms on. */
export const NAME_FOCUS_HOLD = 250;

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

/**
 * Resolves once the workspace's name box has held the focus for `NAME_FOCUS_HOLD` ms, or after `wait` ms: true then,
 * and false where the user pressed a key or a pointer first.
 */
export function afterNameFocus(root: Document = document, wait = NAME_FOCUS_WAIT): Promise<boolean> {
  return new Promise((done) => {
    let held: ReturnType<typeof setTimeout> | undefined;
    const finish = (free: boolean) => {
      root.removeEventListener('focusin', heard, true);
      root.removeEventListener('keydown', pressed, true);
      root.removeEventListener('pointerdown', pressed, true);
      clearTimeout(timer);
      clearTimeout(held);
      done(free);
    };
    const hold = () => {
      clearTimeout(held);
      held = setTimeout(() => finish(true), NAME_FOCUS_HOLD);
    };
    const heard = (event: Event) => {
      if (event.composedPath().some((one) => one instanceof Element && one.localName === NAME_BOX)) hold();
    };
    const pressed = () => finish(false);
    const timer = setTimeout(() => finish(true), wait);
    root.addEventListener('focusin', heard, true);
    root.addEventListener('keydown', pressed, true);
    root.addEventListener('pointerdown', pressed, true);
    if (inNameBox(root)) hold();
  });
}
