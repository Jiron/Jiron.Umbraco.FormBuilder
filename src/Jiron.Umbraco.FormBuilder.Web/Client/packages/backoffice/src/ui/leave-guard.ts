// A screen that holds changes not saved yet asks before the backoffice leaves it (ROADMAP 21.1), as Umbraco's own
// workspaces do, with Umbraco's own dialog: the section's screens (Settings, Access, Retention) are no workspaces, so
// nothing asked for them.
import { UmbControllerBase } from '@umbraco-cms/backoffice/class-api';
import type { UmbControllerHost } from '@umbraco-cms/backoffice/controller-api';
import { UMB_DISCARD_CHANGES_MODAL, umbOpenModal } from '@umbraco-cms/backoffice/modal';

/** The router's event before the address changes, which a listener may stop. */
const LEAVING = 'willchangestate';

/**
 * Asks before the address leaves the screen while `changed` says it holds changes. An address below the screen's own
 * (a dialog of its own that the address names) is no leaving. Discard goes on to the address asked for; anything else
 * stays, with the changes as they are.
 */
export class LeaveGuard extends UmbControllerBase {
  readonly #changed: () => boolean;
  #home = '';
  #allowed = false;

  constructor(host: UmbControllerHost, changed: () => boolean) {
    super(host);
    this.#changed = changed;
  }

  override hostConnected(): void {
    super.hostConnected();
    this.#home = location.pathname;
    window.addEventListener(LEAVING, this.#onLeaving);
  }

  override hostDisconnected(): void {
    super.hostDisconnected();
    window.removeEventListener(LEAVING, this.#onLeaving);
  }

  readonly #onLeaving = (event: Event): void => {
    const url = (event as CustomEvent<{ url?: string | URL | null }>).detail?.url;
    if (this.#allowed || url === undefined || url === null || !this.#changed()) return;
    const path = new URL(url, location.href).pathname;
    const below = this.#home.endsWith('/') ? this.#home : `${this.#home}/`;
    if (path === this.#home || path.startsWith(below)) return;
    event.preventDefault();
    void umbOpenModal(this, UMB_DISCARD_CHANGES_MODAL).then(
      () => {
        // Pushing the address raises the event again: this time it goes through.
        this.#allowed = true;
        history.pushState({}, '', url);
        this.#allowed = false;
      },
      () => undefined,
    );
  };
}
