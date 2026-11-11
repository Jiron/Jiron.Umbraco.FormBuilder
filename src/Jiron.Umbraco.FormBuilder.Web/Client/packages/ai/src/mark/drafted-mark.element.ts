// "Drafted by AI, please review" (docs/ux/information-architecture.md, section 4; ADR-0018): in the form workspace's
// footer while the open draft is still the version the add-on drafted, so the editor's first save ends it. The add-on
// keeps that version per form; a form it did not draft shows nothing.
import { css, html, nothing } from '@umbraco-cms/backoffice/external/lit';
import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import { tryExecute } from '@umbraco-cms/backoffice/resources';
import { aiFormMark } from '../api';
import { FORM_WORKSPACE_CONTEXT } from '../names';

export class DraftedMarkElement extends UmbLitElement {
  static override properties = { _shown: { state: true } };

  declare _shown: boolean;

  #key: string | undefined;
  #version: number | null = null;
  /** The drafted version of each form read so far: a form's mark changes only when the add-on drafts it. */
  readonly #marks = new Map<string, Promise<number | null>>();

  constructor() {
    super();
    this._shown = false;
    this.consumeContext(FORM_WORKSPACE_CONTEXT, (context) => {
      if (!context) return;
      this.observe(context.form, (form) => {
        this.#key = form?.key;
        void this.#update();
      });
      this.observe(context.draftVersion, (version) => {
        this.#version = version;
        void this.#update();
      });
    });
  }

  async #update() {
    const key = this.#key;
    const version = this.#version;
    if (!key || version === null) {
      this._shown = false;
      return;
    }

    let mark = this.#marks.get(key);
    if (!mark) {
      mark = tryExecute(this, aiFormMark({ path: { key } }), { disableNotifications: true }).then(
        ({ data }) => data?.draftVersion ?? null,
      );
      this.#marks.set(key, mark);
    }

    const drafted = await mark;
    // The form or its draft may have changed while the mark was read.
    if (key === this.#key && version === this.#version) this._shown = drafted !== null && drafted === version;
  }

  override render() {
    return this._shown
      ? html`<uui-tag look="secondary"
          ><uui-icon name="icon-wand" aria-hidden="true"></uui-icon>${this.localize.term('jfbAI_drafted')}</uui-tag
        >`
      : nothing;
  }

  static override styles = css`
    :host {
      display: flex;
      align-items: center;
    }

    uui-icon {
      margin-inline-end: var(--uui-size-space-1);
    }
  `;
}

if (!customElements.get('jfb-ai-drafted-mark')) customElements.define('jfb-ai-drafted-mark', DraftedMarkElement);

export { DraftedMarkElement as element };

declare global {
  interface HTMLElementTagNameMap {
    'jfb-ai-drafted-mark': DraftedMarkElement;
  }
}
