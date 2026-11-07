// An entry's own screen (ROADMAP 11.2, IA 6.3, ADR-0208): where the link of a mail, Recent entries or a member's
// entries lands, the entry panel alone in the workspace with its title and its form. The link names the entry only; the
// server says which form holds it, and an entry that is gone, or that the user may not view, shows Umbraco's "not found"
// and nothing of it. Deleted here, the screen moves to its form's Entries view.
import { css, html, type PropertyValues } from '@umbraco-cms/backoffice/external/lit';
import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import { open } from '../library/library-changes';
import { formPath } from '../section/entity-types';
import { jfbBackofficeTokens } from '../styles/tokens';
import { jfbSkeleton, skeletonLines } from '../ui/skeleton';
import { locateEntry } from './entry-panel-api';
import { loadEntryWords } from './entry-words';
import './entry-panel.element';

export class EntryScreenElement extends UmbLitElement {
  static override properties = {
    unique: { type: String },
    entityType: { type: String },
    _words: { state: true },
    _form: { state: true },
  };

  static override styles = [
    jfbBackofficeTokens,
    jfbSkeleton,
    css`
      :host {
        display: flex;
        flex-direction: column;
        block-size: 100%;
      }

      jfb-entry-panel {
        flex: 1;
        min-block-size: 0;
      }

      .gone {
        padding: var(--uui-size-layout-1);
      }

      .gone p {
        margin: 0;
      }
    `,
  ];

  /** The entry, from the screen's address. */
  declare unique: string | null;
  declare entityType: string;
  declare private _words: boolean;
  /** The entry's form; null for an entry that is gone or that the user may not view, undefined while it is asked. */
  declare private _form: string | null | undefined;

  #reads = 0;

  constructor() {
    super();
    this.unique = null;
    this.entityType = '';
    this._words = false;
  }

  override connectedCallback() {
    super.connectedCallback();
    void loadEntryWords(this.localize.lang()).then(() => (this._words = true));
  }

  protected override willUpdate(changed: PropertyValues<this>) {
    if (changed.has('unique')) void this.#locate();
  }

  async #locate() {
    const read = ++this.#reads;
    this._form = undefined;
    const entry = this.unique;
    const form = entry ? await locateEntry(this, entry) : null;
    if (read === this.#reads) this._form = form ?? null;
  }

  override render() {
    if (!this._words || this._form === undefined)
      return html`<div class="gone">
        ${skeletonLines(this._words ? this.localize.term('jfbEntries_panel_loading') : '', [60, 80, 70])}
      </div>`;
    // Gone or out of reach, the link gets the backoffice's own answer for an item it can't find, the same for both.
    if (this._form === null || !this.unique)
      return html`<umb-entity-detail-not-found
        entity-type=${this.localize.term('jfbEntries_notFound_entity')}
      ></umb-entity-detail-not-found>`;
    const form = this._form;
    return html`<jfb-entry-panel
      place="alone"
      .form=${form}
      .entry=${this.unique}
      @jfb-entry-gone=${() => open(`${formPath(form)}/view/entries`)}
    ></jfb-entry-panel>`;
  }
}

if (!customElements.get('jfb-entry-screen')) customElements.define('jfb-entry-screen', EntryScreenElement);

export { EntryScreenElement as element };

declare global {
  interface HTMLElementTagNameMap {
    'jfb-entry-screen': EntryScreenElement;
  }
}
