/**
 * The website runtime's entry (ADR-0004, ADR-0110): one module script per page, however many forms the page shows. It
 * holds the frame alone; the logic loads as its own chunk on a form's first interaction, and the features that only
 * some forms use (uploads, signature, repeater, calculations, captchas) as theirs, so the entry stays within the core
 * budget of ROADMAP section 4.12.
 */
import { createFrame } from './frame';
import { installGlobal } from './global';

declare global {
  /** The package version, set at build time (vite.config.ts). */
  const __JFB_VERSION__: string;
}

const frame = createFrame(() => import('./form'));
const runtime = installGlobal(window, __JFB_VERSION__, (root) => frame.init(root));

// A module runs once the document is parsed; one added while it still loads waits for the rest of the forms.
if (document.readyState === 'loading')
  document.addEventListener('DOMContentLoaded', () => runtime.init(), { once: true });
else runtime.init();
