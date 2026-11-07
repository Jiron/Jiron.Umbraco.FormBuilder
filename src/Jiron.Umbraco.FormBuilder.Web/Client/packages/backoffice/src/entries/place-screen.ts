// The Inbox's and Spam's screens (ROADMAP 11.1, IA 6.1 and 6.2, ADR-0209): the entries list of every form the user may
// view, filling the workspace its sidebar item opens. The sidebar names the screen, so it has no title of its own
// (ADR-0021); the list scrolls inside it and keeps its bulk bar at its foot.
import { css, html } from '@umbraco-cms/backoffice/external/lit';
import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import type { ListPlace } from './entry-list-model';
import '../designer/entries/entries.view';

/** Defines the screen of a place under its tag, once, and gives its class. */
export function placeScreen(tag: string, place: Exclude<ListPlace, 'form'>): typeof UmbLitElement {
  class PlaceScreenElement extends UmbLitElement {
    static override styles = css`
      :host {
        display: block;
        block-size: 100%;
      }
    `;

    override render() {
      return html`<jfb-entries-view place=${place}></jfb-entries-view>`;
    }
  }

  if (!customElements.get(tag)) customElements.define(tag, PlaceScreenElement);
  return PlaceScreenElement;
}
