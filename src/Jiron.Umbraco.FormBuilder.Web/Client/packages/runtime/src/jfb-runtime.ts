/**
 * The website runtime's entry (ADR-0004): one module script per page, however many forms the page shows. Features
 * that only some forms use (uploads, signature, repeater, calculations, captchas) load on demand as their own
 * chunks, so the entry stays within the core budget of ROADMAP section 4.12. Enhancing the forms comes in 6.4.
 */
import { installGlobal } from './global';

declare global {
  /** The package version, set at build time (vite.config.ts). */
  const __JFB_VERSION__: string;
}

installGlobal(window, __JFB_VERSION__);
