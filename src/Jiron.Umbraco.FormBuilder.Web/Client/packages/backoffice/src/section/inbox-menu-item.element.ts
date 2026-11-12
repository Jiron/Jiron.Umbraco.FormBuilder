// The Inbox's item in the sidebar (ROADMAP 11.1, IA 1, ADR-0209): Umbraco's menu item, drawn as Umbraco draws one, with the
// unseen count as UUI's badge in its badge slot: up to 99, then "99+", and nothing at none. Screen readers hear the item
// as "Inbox, 12 unseen", while the item shows its name and the badge its number. The count comes from the unseen poll,
// which waits at the idle gate, so the badge never keeps an idle person signed in.
import { css, html, nothing, type PropertyValues } from '@umbraco-cms/backoffice/external/lit';
import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import type { ManifestMenuItem } from '@umbraco-cms/backoffice/menu';
import { UMB_SECTION_CONTEXT } from '@umbraco-cms/backoffice/section';
import { readUnseen } from '../entries/entries-api';
import { jfbBackofficeTokens } from '../styles/tokens';
import { UnseenPoll, type UnseenCount } from '../entries/unseen-poll';
import { idleGate, startIdleGate } from '../ui/idle-gate';

/** A path with a slash at its end, so "/inbox/" never matches "/inbox-2/". */
const slashed = (path: string): string => (path.endsWith('/') ? path : `${path}/`);

export class InboxMenuItemElement extends UmbLitElement {
  static override properties = {
    manifest: { attribute: false },
    _href: { state: true },
    _active: { state: true },
    _count: { state: true },
  };

  // The tokens give the badge its white number on Umbraco's dark fill, as the section's other badges have it (16.6).
  static override styles = [
    jfbBackofficeTokens,
    css`
      :host {
        display: block;
      }
    `,
  ];

  declare manifest?: ManifestMenuItem;
  declare private _href: string | undefined;
  declare private _active: boolean;
  declare private _count: UnseenCount | undefined;

  #pathname: string | undefined;
  #stopListening: (() => void) | undefined;
  readonly #poll = new UnseenPoll(async () => readUnseen(this), idleGate);

  constructor() {
    super();
    this._active = false;
    this.consumeContext(UMB_SECTION_CONTEXT, (section) => {
      this.observe(
        section?.pathname,
        (pathname) => {
          this.#pathname = pathname ?? undefined;
          this.#checkActive();
        },
        'pathname',
      );
    });
    // The gate follows the session from the first poll on; the backoffice's other calls are the user's own.
    void startIdleGate(this);
  }

  override connectedCallback() {
    super.connectedCallback();
    window.addEventListener('navigationend', this.#checkActive);
    this.#checkActive();
    this.#stopListening = this.#poll.listen((count) => (this._count = count));
  }

  protected override willUpdate(changed: PropertyValues<this>) {
    if (changed.has('manifest')) this.#checkActive();
  }

  override disconnectedCallback() {
    super.disconnectedCallback();
    window.removeEventListener('navigationend', this.#checkActive);
    this.#stopListening?.();
    this.#stopListening = undefined;
  }

  /** The item is the open one while the address is its screen's, as Umbraco's own items tell. */
  readonly #checkActive = () => {
    const entityType = this.manifest?.meta.entityType;
    this._href = this.#pathname && entityType ? `section/${this.#pathname}/workspace/${entityType}` : undefined;
    this._active = !!this._href && slashed(window.location.pathname).includes(slashed(this._href));
  };

  override render() {
    const manifest = this.manifest;
    if (!manifest) return nothing;
    const label = this.localize.string(manifest.meta.label ?? manifest.name);
    const count = this._count;
    const badge = count && count.count > 0 ? (count.more ? `${count.count}+` : String(count.count)) : undefined;
    // With a count, the item's name for screen readers says it; the label slot shows the name alone.
    return html`<uui-menu-item
      label=${badge ? this.localize.term('jfbSection_inboxUnseen', label, badge) : label}
      href=${this._href ?? nothing}
      ?active=${this._active}
    >
      <umb-icon slot="icon" name=${manifest.meta.icon ?? ''}></umb-icon>
      ${
        badge
          ? html`<span slot="label">${label}</span> <uui-badge slot="badge" aria-hidden="true">${badge}</uui-badge>`
          : nothing
      }
    </uui-menu-item>`;
  }
}

if (!customElements.get('jfb-inbox-menu-item')) customElements.define('jfb-inbox-menu-item', InboxMenuItemElement);

export { InboxMenuItemElement as element };

declare global {
  interface HTMLElementTagNameMap {
    'jfb-inbox-menu-item': InboxMenuItemElement;
  }
}
