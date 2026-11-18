// The Inbox's and Spam's screens (ROADMAP 11.1, IA 6.1 and 6.2, ADR-0209): the entries list of every form the user may
// view, filling the workspace under the Inbox's tabs (ADR-0257), which name it, so it has no title of its own. The
// list scrolls inside it and keeps its bulk bar at its foot, so the body layout around it has no padding and never
// scrolls itself.
import { css, html } from '@umbraco-cms/backoffice/external/lit';
import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import type { ListPlace } from './entry-list-model';
import { jfbBackofficeTokens } from '../styles/tokens';
import '../designer/entries/entries.view';
import '../section/place-tabs.element';

/** Defines the screen of a place under its tag, once, and gives its class. */
export function placeScreen(tag: string, place: Exclude<ListPlace, 'form'>): typeof UmbLitElement {
  class PlaceScreenElement extends UmbLitElement {
    static override styles = [
      jfbBackofficeTokens,
      css`
        :host {
          display: block;
          block-size: 100%;
        }
      `,
    ];

    override render() {
      return html`<umb-body-layout main-no-padding>
        <jfb-place-tabs tab=${place}></jfb-place-tabs>
        <jfb-entries-view place=${place}></jfb-entries-view>
      </umb-body-layout>`;
    }
  }

  if (!customElements.get(tag)) customElements.define(tag, PlaceScreenElement);
  return PlaceScreenElement;
}
