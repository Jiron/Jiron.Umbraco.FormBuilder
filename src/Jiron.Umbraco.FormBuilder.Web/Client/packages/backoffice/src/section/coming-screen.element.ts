// The screen behind a sidebar item whose step is still to come (ADR-0165): one sentence in the place the screen will take,
// so that the sidebar is whole and each role sees its real menus from the start. Each screen's step replaces it, and no
// release ships with one: step 13.4 checks that no workspace still shows it.
import { css, html } from '@umbraco-cms/backoffice/external/lit';
import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';

export class ComingScreenElement extends UmbLitElement {
  static override styles = css`
    :host {
      display: block;
      padding: var(--uui-size-layout-1);
    }
  `;

  override render() {
    return html`<uui-box><p>${this.localize.term('jfbSection_comingScreen')}</p></uui-box>`;
  }
}

if (!customElements.get('jfb-coming-screen')) customElements.define('jfb-coming-screen', ComingScreenElement);

export { ComingScreenElement as element };

declare global {
  interface HTMLElementTagNameMap {
    'jfb-coming-screen': ComingScreenElement;
  }
}
