// The text still being typed, taken in before a save (ROADMAP 21.1). A box that tells of its text at `change` has not
// told of it while the cursor is still in it, as when Ctrl+S saves the workspace: the save would send the text as it
// was before, and say it saved.

/** The element that holds the focus, through the shadow roots between the document and it. */
function activeIn(root: Document | ShadowRoot): Element | null {
  let active = root.activeElement;
  while (active?.shadowRoot?.activeElement) active = active.shadowRoot.activeElement;
  return active;
}

/**
 * Has the text box that holds the focus tell of its text now, as leaving it would, and leaves the focus where it is.
 * A box that told at `input` already hears nothing new; a tick box, a radio button and a file say theirs at once.
 */
export function commitTyping(root: Document | ShadowRoot = document): void {
  const active = activeIn(root);
  const typed =
    active instanceof HTMLTextAreaElement ||
    (active instanceof HTMLInputElement && !['checkbox', 'radio', 'file', 'button', 'submit'].includes(active.type));
  if (typed) active.dispatchEvent(new Event('change', { bubbles: true }));
}
