// A place's tabs along the top of each of its screens (ROADMAP 22.2, ADR-0257), in the header of Umbraco's body layout
// where a headline would stand: Umbraco's tab group, as its workspace editor draws a workspace's views, so the keyboard
// and the folding of tabs that do not fit are Umbraco's. Each tab is a link to its screen's own address and shows only
// to who may open it; Settings ends with the tabs a site or an add-on adds. With one tab left the row is gone, and with
// it the header: the element then stands at the top of the screen's content, holding its heading alone. A heading for
// screen readers names the place and the tab (the name once where the tab has the place's), since the screen has no
// visible title (the sidebar names the place, the tab the screen). It draws at once from what the user holds, read as
// the section opened, so the header does not flicker from tab to tab.
import { css, html, nothing, type PropertyValues } from '@umbraco-cms/backoffice/external/lit';
import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import { jfbBackofficeTokens, jfbVisuallyHidden } from '../styles/tokens';
import { ADDED_TABS_PLACE, observeAddedTabs, type AddedTab } from './added-tabs';
import { screenPath } from './entity-types';
import { myPermissions, myPermissionsNow } from './my-permissions';
import { PLACES, tabsFor, type MyPermissions, type Place } from './sidebar';

/** A tab as the row shows it, the table's or an added one. */
interface ShownTab {
  id: string;
  entityType: string;
  icon: string;
  label: string;
}

export class PlaceTabsElement extends UmbLitElement {
  static override properties = {
    tab: {},
    _permissions: { state: true },
    _added: { state: true },
  };

  static override styles = [
    jfbBackofficeTokens,
    jfbVisuallyHidden,
    css`
      :host {
        display: flex;
        align-self: stretch;
        min-inline-size: 0;
      }

      /* As Umbraco's workspace editor sets its views' tab group. */
      uui-tab-group {
        --uui-tab-divider: var(--uui-color-border);
        flex: 1 1 auto;
        min-inline-size: 0;
        block-size: 100%;
        border-inline: 1px solid var(--uui-color-border);
      }
    `,
  ];

  /** The open tab: its id in the table of the sidebar, or an added tab's manifest alias. */
  declare tab: string;
  declare private _permissions: MyPermissions | undefined;
  declare private _added: AddedTab[];

  constructor() {
    super();
    this.tab = '';
    this._added = [];
    this._permissions = myPermissionsNow();
    if (!this._permissions) void myPermissions(this).then((permissions) => (this._permissions = permissions));
  }

  override connectedCallback() {
    super.connectedCallback();
    if (this.#place()?.id === ADDED_TABS_PLACE) observeAddedTabs(this, (added) => (this._added = added));
  }

  /** The open tab's place: the table's, or Settings for an added tab. */
  #place(): Place | undefined {
    if (!this.tab) return undefined;
    return (
      PLACES.find((place) => place.tabs.some((tab) => tab.id === this.tab)) ??
      PLACES.find((place) => place.id === ADDED_TABS_PLACE)
    );
  }

  /** The place's tabs the user may open, and the open one among them. */
  #tabs(place: Place): { tabs: ShownTab[]; open: ShownTab | undefined } {
    // Until the answer comes, the open tab alone, which the user holds or the screen would not be open.
    const own = this._permissions ? tabsFor(place, this._permissions) : place.tabs.filter((tab) => tab.id === this.tab);
    const tabs = [
      ...own.map((tab) => ({ ...tab, label: this.localize.term(`jfbSection_${tab.id}`) })),
      ...(place.id === ADDED_TABS_PLACE
        ? this._added.map((tab) => ({ ...tab, label: this.localize.string(tab.label) }))
        : []),
    ];
    return { tabs, open: tabs.find((tab) => tab.id === this.tab) };
  }

  protected override willUpdate(changed: PropertyValues<this>) {
    super.willUpdate(changed);
    // A row of tabs goes in the body layout's header; a heading alone needs none.
    const place = this.#place();
    this.slot = place && this.#tabs(place).tabs.length > 1 ? 'header' : '';
  }

  /**
   * The tab group marks a clicked tab active at once, but the click only asks the router to go: a screen with changes
   * not saved asks first, and Stay keeps it. So the open screen's tab is marked again; a move that goes on draws the
   * next screen with its own.
   */
  readonly #keepOpen = () => {
    setTimeout(() => {
      for (const tab of this.shadowRoot?.querySelectorAll<HTMLElement & { active: boolean }>('uui-tab') ?? [])
        tab.active = tab.hasAttribute('data-open');
    });
  };

  #tab(tab: ShownTab, open: boolean) {
    return html`<uui-tab href=${screenPath(tab.entityType)} label=${tab.label} ?active=${open} ?data-open=${open}>
      <div slot="icon"><umb-icon name=${tab.icon}></umb-icon></div>
      ${tab.label}
    </uui-tab>`;
  }

  override render() {
    const place = this.#place();
    if (!place) return nothing;
    const { tabs, open } = this.#tabs(place);
    const name = this.localize.term(`jfbSection_${place.id}`);
    // A second-level heading: Umbraco's own headlines, in its dialogs over the screen, are third-level, so the page's
    // headings keep their order.
    return html`<h2 class="visually-hidden">${open && open.label !== name ? `${name}: ${open.label}` : name}</h2>
      ${
        tabs.length > 1
          ? html`<uui-tab-group aria-label=${name} @click=${this.#keepOpen}>
              ${tabs.map((tab) => this.#tab(tab, tab === open))}
            </uui-tab-group>`
          : nothing
      }`;
  }
}

if (!customElements.get('jfb-place-tabs')) customElements.define('jfb-place-tabs', PlaceTabsElement);

declare global {
  interface HTMLElementTagNameMap {
    'jfb-place-tabs': PlaceTabsElement;
  }
}
