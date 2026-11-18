// The five places at the top of the sidebar (ROADMAP 22.2, ADR-0257): one navigation list without a heading, before the
// tree of forms, each place a link to the first of its tabs the user may open, drawn as Umbraco draws a menu item. A place
// is the open one on its tabs' screens and on the editors they open; Settings also on the tabs a site or an add-on adds. The Inbox carries the unseen count as UUI's badge
// (ADR-0209): up to 99, then "99+", and nothing at none; screen readers hear "Inbox, 12 unseen", while the item shows
// its name and the badge its number. The count comes from the unseen poll, which waits at the idle gate, so the badge
// never keeps an idle person signed in.
import { css, html, nothing } from '@umbraco-cms/backoffice/external/lit';
import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import { readUnseen } from '../entries/entries-api';
import { UnseenPoll, type UnseenCount } from '../entries/unseen-poll';
import { jfbBackofficeTokens } from '../styles/tokens';
import { idleGate, startIdleGate } from '../ui/idle-gate';
import { ADDED_TABS_PLACE, observeAddedTabs } from './added-tabs';
import { screenPath } from './entity-types';
import { myPermissions, myPermissionsNow } from './my-permissions';
import { placeOf, placesFor, type MyPermissions, type Place, type Tab } from './sidebar';

/** The place whose item carries the unseen count. */
const INBOX = 'inbox';

/** The entity type of the workspace an address opens, if it opens one. */
const workspaceOf = (pathname: string): string | undefined => /\/workspace\/([^/]+)/.exec(pathname)?.[1];

const placesOf = (permissions: MyPermissions | undefined) => (permissions ? placesFor(permissions) : []);

export class PlacesElement extends UmbLitElement {
  static override properties = {
    _places: { state: true },
    _open: { state: true },
    _count: { state: true },
  };

  // The tokens give the badge its white number on Umbraco's dark fill, as the section's other badges have it (16.6).
  static override styles = [
    jfbBackofficeTokens,
    css`
      :host {
        display: block;
        padding-block-start: var(--uui-size-space-3);
      }
    `,
  ];

  declare private _places: { place: Place; first: Tab }[];
  declare private _open: string | undefined;
  declare private _count: UnseenCount | undefined;

  #stopListening: (() => void) | undefined;
  /** The entity types of the tabs added to Settings. */
  #added: string[] = [];
  readonly #poll = new UnseenPoll(async () => readUnseen(this), idleGate);

  constructor() {
    super();
    // What the user holds is mostly answered by the time the sidebar draws, since its extensions' conditions asked.
    this._places = placesOf(myPermissionsNow());
    void myPermissions(this).then((permissions) => {
      this._places = placesOf(permissions);
      this.#listen();
    });
    observeAddedTabs(this, (added) => {
      this.#added = added.map((tab) => tab.entityType);
      this.#checkOpen();
    });
  }

  override connectedCallback() {
    super.connectedCallback();
    window.addEventListener('navigationend', this.#checkOpen);
    this.#checkOpen();
    this.#listen();
  }

  override disconnectedCallback() {
    super.disconnectedCallback();
    window.removeEventListener('navigationend', this.#checkOpen);
    this.#stopListening?.();
    this.#stopListening = undefined;
  }

  /** Polls the unseen count while the Inbox is one of the user's places. */
  #listen() {
    if (!this.isConnected || this.#stopListening || !this._places.some(({ place }) => place.id === INBOX)) return;
    // The gate follows the session from the first poll on; the backoffice's other calls are the user's own.
    void startIdleGate(this);
    this.#stopListening = this.#poll.listen((count) => (this._count = count));
  }

  /** The open place: the one the open workspace belongs to, by the address, as Umbraco's own items tell. */
  readonly #checkOpen = () => {
    const entityType = workspaceOf(window.location.pathname);
    if (!entityType) this._open = undefined;
    else this._open = placeOf(entityType)?.id ?? (this.#added.includes(entityType) ? ADDED_TABS_PLACE : undefined);
  };

  #item(place: Place, first: Tab) {
    const label = this.localize.term(`jfbSection_${place.id}`);
    const count = place.id === INBOX ? this._count : undefined;
    const badge = count && count.count > 0 ? (count.more ? `${count.count}+` : String(count.count)) : undefined;
    // With a count, the item's name for screen readers says it; the label slot shows the name alone.
    return html`<uui-menu-item
      label=${badge ? this.localize.term('jfbSection_inboxUnseen', label, badge) : label}
      href=${screenPath(first.entityType)}
      ?active=${place.id === this._open}
    >
      <umb-icon slot="icon" name=${place.icon}></umb-icon>
      ${
        badge
          ? html`<span slot="label">${label}</span> <uui-badge slot="badge" aria-hidden="true">${badge}</uui-badge>`
          : nothing
      }
    </uui-menu-item>`;
  }

  override render() {
    if (this._places.length === 0) return nothing;
    return html`<nav aria-label=${this.localize.term('jfbSection_section')}>
      ${this._places.map(({ place, first }) => this.#item(place, first))}
    </nav>`;
  }
}

if (!customElements.get('jfb-places')) customElements.define('jfb-places', PlacesElement);

export { PlacesElement as element };

declare global {
  interface HTMLElementTagNameMap {
    'jfb-places': PlacesElement;
  }
}
