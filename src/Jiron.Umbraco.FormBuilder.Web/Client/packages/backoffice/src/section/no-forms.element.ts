// The sidebar of a user who may see no form (docs/ux/information-architecture.md, section 2): Umbraco's heading as above the tree,
// and in the tree's place one sentence that says who can change that.
import { css, html } from '@umbraco-cms/backoffice/external/lit';
import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import { jfbBackofficeTokens } from '../styles/tokens';

export class NoFormsElement extends UmbLitElement {
  static override styles = [
    jfbBackofficeTokens,
    css`
      :host {
        display: block;
        padding: var(--uui-size-5) 0 0;
      }

      /* As Umbraco's sidebar headings, so nothing moves between a user with forms and one without. */
      h3 {
        margin: 0;
        padding: var(--uui-size-4) var(--uui-size-8);
        font-size: 14px;
        font-weight: 700;
      }

      p {
        margin: 0;
        padding: 0 var(--uui-size-8);
        color: var(--jfb-bo-color-text-muted);
      }
    `,
  ];

  override render() {
    return html`
      <h3>${this.localize.term('jfbSection_forms')}</h3>
      <p>${this.localize.term('jfbSection_noForms')}</p>
    `;
  }
}

if (!customElements.get('jfb-no-forms')) customElements.define('jfb-no-forms', NoFormsElement);

export { NoFormsElement as element };

declare global {
  interface HTMLElementTagNameMap {
    'jfb-no-forms': NoFormsElement;
  }
}
