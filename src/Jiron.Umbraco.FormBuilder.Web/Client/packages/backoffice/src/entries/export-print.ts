// The printable view printed where it was asked for (ROADMAP 11.4, IA 6.3 and 6.4, ADR-0210): the server's document,
// which holds no script and escapes every text, goes into a frame of its own that runs nothing, and the browser's print
// dialog opens on it. No window opens, so no pop-up blocker stands in the way, and the frame goes once printing is done.

/** How long the frame stays at most, for a browser whose print dialog does not hold the page while it is open. */
export const FRAME_KEPT_MS = 60_000;

/** Prints an HTML document through a hidden frame; false when the browser could not print it. */
export function printDocument(html: string, page: Document = document): Promise<boolean> {
  return new Promise((resolve) => {
    const frame = page.createElement('iframe');
    // Same origin, so the page may ask it to print, and modals, which printing is; no scripts, no forms, no navigation.
    frame.setAttribute('sandbox', 'allow-same-origin allow-modals');
    frame.setAttribute('aria-hidden', 'true');
    frame.tabIndex = -1;
    frame.style.cssText = 'position:fixed;inset-inline-start:0;inset-block-end:0;width:0;height:0;border:0;opacity:0;';
    let removed = false;
    const remove = () => {
      if (removed) return;
      removed = true;
      frame.remove();
    };
    frame.addEventListener(
      'load',
      () => {
        const view = frame.contentWindow;
        if (!view) {
          remove();
          resolve(false);
          return;
        }

        view.addEventListener('afterprint', () => setTimeout(remove, 0), { once: true });
        setTimeout(remove, FRAME_KEPT_MS);
        try {
          view.focus();
          view.print();
          resolve(true);
        } catch {
          remove();
          resolve(false);
        }
      },
      { once: true },
    );
    frame.srcdoc = html;
    page.body.append(frame);
  });
}
