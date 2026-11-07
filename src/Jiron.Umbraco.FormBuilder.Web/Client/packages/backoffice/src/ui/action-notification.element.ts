// A toast that says something in the section's words and may offer one button (ROADMAP 11.4, docs/ux/copy.md, Loading
// and long work): Umbraco's own toast layout, as the Undo toast has. It lives in Umbraco's notification container, which
// outlives the screen that showed it, so it says its words itself, by their keys, and a long task can change what it says
// while the user works elsewhere: a background export's progress, then its file (ADR-0210).
import { css, html, nothing } from '@umbraco-cms/backoffice/external/lit';
import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import type { UmbNotificationHandler } from '@umbraco-cms/backoffice/notification';
import { jfbBackofficeTokens } from '../styles/tokens';

/** A sentence by its dictionary key and the values it takes. */
export interface Said {
  readonly key: string;
  readonly args?: readonly unknown[];
}

/** What the toast says, how it hears a new sentence, and its one button. */
export interface ActionNotificationData {
  /** Umbraco's own text of a toast, which this one does not show: it says {@link said} in the user's words. */
  readonly message: '';
  readonly said: Said;
  /** Tells the toast each new sentence from now on; the toast stops listening when it closes. */
  readonly watch?: (show: (said: Said) => void) => () => void;
  /** The button: its label and what it does; the toast closes when it is pressed. */
  readonly action?: { readonly label: Said; readonly run: () => Promise<void> };
  /** Whether the toast is Umbraco's positive one, whose text needs its own contrast colour. */
  readonly positive?: boolean;
}

export const ACTION_NOTIFICATION = 'jfb-action-notification';

export class ActionNotificationElement extends UmbLitElement {
  static override properties = {
    data: { type: Object },
    notificationHandler: { attribute: false },
    _said: { state: true },
    _changed: { state: true },
  };

  // Outside the section's elements, it brings the tokens itself (ADR-0182).
  static override styles = [
    jfbBackofficeTokens,
    css`
      /* On the dark theme's positive toast, Umbraco's grey label is 4.4:1 and its contrast colour 5:1 (ADR-0182). */
      #message.positive {
        color: var(--uui-color-positive-contrast);
      }
    `,
  ];

  declare data: ActionNotificationData;
  declare notificationHandler: UmbNotificationHandler | undefined;
  declare private _said: Said | undefined;
  declare private _changed: boolean;
  #stop: (() => void) | undefined;
  #acting = false;

  constructor() {
    super();
    this._changed = false;
  }

  override connectedCallback() {
    super.connectedCallback();
    this.#stop?.();
    this.#stop = this.data?.watch?.((said) => {
      this._said = said;
      this._changed = true;
    });
  }

  override disconnectedCallback() {
    this.#stop?.();
    this.#stop = undefined;
    super.disconnectedCallback();
  }

  async #act() {
    if (this.#acting || !this.data.action) return;
    this.#acting = true;
    this.notificationHandler?.close();
    await this.data.action.run();
  }

  #say(said: Said): string {
    return this.localize.term(said.key, ...(said.args ?? []));
  }

  override render() {
    const said = this._said ?? this.data.said;
    const action = this.data.action;
    // The toast's first sentence is announced as it appears; its progress after that is not read out at every step.
    return html`<uui-toast-notification-layout>
      <div id="message" class=${this.data.positive ? 'positive' : ''} aria-live=${this._changed ? 'off' : nothing}>
        ${this.#say(said)}
      </div>
      ${
        action
          ? html`<uui-button
              slot="actions"
              look="secondary"
              label=${this.#say(action.label)}
              @click=${this.#act}
            ></uui-button>`
          : nothing
      }
    </uui-toast-notification-layout>`;
  }
}

if (!customElements.get(ACTION_NOTIFICATION)) customElements.define(ACTION_NOTIFICATION, ActionNotificationElement);

declare global {
  interface HTMLElementTagNameMap {
    'jfb-action-notification': ActionNotificationElement;
  }
}
