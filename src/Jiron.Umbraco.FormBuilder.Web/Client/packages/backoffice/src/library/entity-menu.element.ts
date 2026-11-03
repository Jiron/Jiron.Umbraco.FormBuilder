// An item's "..." outside the tree (docs/ux/information-architecture.md, section 3; ADR-0166): Umbraco's own list of the entity
// actions registered for the item's entity type, under their conditions, in a screen's header or a list's row.
import { html } from '@umbraco-cms/backoffice/external/lit';
import { UmbEntityContext } from '@umbraco-cms/backoffice/entity';
import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';

export class EntityMenuElement extends UmbLitElement {
  static override properties = {
    entityType: { type: String, attribute: 'entity-type' },
    unique: { type: String },
    label: { type: String },
  };

  declare entityType: string;
  declare unique: string | null;
  declare label: string;

  readonly #entity = new UmbEntityContext(this);

  protected override willUpdate() {
    this.#entity.setEntityType(this.entityType);
    this.#entity.setUnique(this.unique ?? null);
  }

  override render() {
    return html`<umb-entity-actions-dropdown compact .label=${this.label}>
      <uui-symbol-more slot="label"></uui-symbol-more>
    </umb-entity-actions-dropdown>`;
  }
}

if (!customElements.get('jfb-entity-menu')) customElements.define('jfb-entity-menu', EntityMenuElement);

declare global {
  interface HTMLElementTagNameMap {
    'jfb-entity-menu': EntityMenuElement;
  }
}
