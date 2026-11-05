// Rich text in the inspector (ROADMAP 9.4c, ADR-0173): a paragraph, and a consent's or a checkbox's text, which sits in
// the visitor's label and so is one line of words (inline). It edits on the Tiptap that Umbraco ships, with only what
// the server's sanitiser keeps and such a text needs: paragraphs and line breaks, bold, italic, links, bulleted and
// numbered lists; an inline text bold, italic, links and line breaks only, written without a paragraph around it. The
// toolbar is one stop for the keyboard, its buttons reached with the arrow keys and saying whether they are on; a link
// is edited in a small panel under it, its address one the sanitiser keeps. Typing hands the text on as it goes and
// once more when the box is left; while it has the focus, Ctrl+Z undoes its own typing. A text set from outside, after
// the designer's Undo, in another language or for another field, starts afresh, with nothing of the last one to undo.
// A merge tag goes in where the caret is.
import { css, html, nothing, type PropertyValues, type TemplateResult } from '@umbraco-cms/backoffice/external/lit';
import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import {
  Bold,
  BulletList,
  Document,
  Editor,
  Extension,
  HardBreak,
  Italic,
  Link,
  ListItem,
  OrderedList,
  Paragraph,
  Placeholder,
  Text,
  UndoRedo,
} from '@umbraco-cms/backoffice/tiptap';
import { jfbBackofficeTokens } from '../../styles/tokens';

/**
 * Whether an address is one a link may lead to: http, https, mailto or tel, or a path on the site, which starts with one
 * slash and never two or a backslash, as browsers read those as another site.
 */
export const isLinkAddress = (address: string): boolean =>
  /^(https?:\/\/[^\s]+|mailto:[^\s]+|tel:[+0-9][0-9 ()./-]*|\/(?![/\\])[^\s]*)$/i.test(address.trim());

/** The marks and lists the toolbar turns on and off, in its order. */
const TOOLS = ['bold', 'italic', 'link', 'bulletList', 'orderedList'] as const;
type Tool = (typeof TOOLS)[number];

/** The tools of an inline text, which holds no lists. */
const INLINE_TOOLS: readonly Tool[] = ['bold', 'italic', 'link'];

const ICONS: Readonly<Record<Tool, string>> = {
  bold: 'icon-bold',
  italic: 'icon-italic',
  link: 'icon-link',
  bulletList: 'icon-bulleted-list',
  orderedList: 'icon-ordered-list',
};

/** A document of one paragraph, which an inline text is written in. */
const OneParagraph = Document.extend({ content: 'paragraph' });

/** An inline text takes no new paragraph: Enter does nothing there, and Shift+Enter breaks the line. */
const NoNewParagraph = Extension.create({
  name: 'jfbNoNewParagraph',
  addKeyboardShortcuts: () => ({ Enter: () => true }),
});

/** An inline text's words, without the paragraph the editor writes around them. */
const unwrapped = (html: string) => /^<p>([\s\S]*)<\/p>$/.exec(html)?.[1] ?? html;

export class RichTextElement extends UmbLitElement {
  static override properties = {
    value: { attribute: false },
    label: {},
    description: {},
    language: {},
    placeholder: {},
    inline: { type: Boolean, reflect: true },
    change: { attribute: false },
    _on: { state: true },
    _linking: { state: true },
    _linkProblem: { state: true },
    _toolAt: { state: true },
  };

  /** The text as HTML, in the language edited. */
  declare value: string;
  /** What the box is called, as its label says. */
  declare label: string;
  /** What the box's help says. */
  declare description: string;
  /** The language the text is written in. */
  declare language: string;
  /** The default language's text as plain words, shown faintly where this language has none. */
  declare placeholder: string;
  /** Whether the text sits in a label: one line of words, without paragraphs or lists. */
  declare inline: boolean;
  /** Hands the text on: while typing, and `done` once the box is left after typing. */
  declare change: (html: string, done: boolean) => void;
  declare private _on: ReadonlySet<Tool>;
  declare private _linking: { readonly address: string; readonly newTab: boolean; readonly existing: boolean } | null;
  declare private _linkProblem: boolean;
  /** The toolbar's one stop for the keyboard. */
  declare private _toolAt: number;

  #editor: Editor | undefined;
  /** Whether the text was typed in since the box got the focus, so leaving it hands it on. */
  #typed = false;

  constructor() {
    super();
    this.value = '';
    this.label = '';
    this.description = '';
    this.language = '';
    this.placeholder = '';
    this.inline = false;
    this.change = () => undefined;
    this._on = new Set();
    this._linking = null;
    this._linkProblem = false;
    this._toolAt = 0;
  }

  #term = (key: string, ...args: unknown[]) => this.localize.term(`jfbInspector_richText_${key}`, ...args);

  get #tools(): readonly Tool[] {
    return this.inline ? INLINE_TOOLS : TOOLS;
  }

  /** The text as HTML, empty when nothing is written; an inline text without its paragraph. */
  #html(): string {
    const editor = this.#editor;
    if (!editor || editor.isEmpty) return '';
    return this.inline ? unwrapped(editor.getHTML()) : editor.getHTML();
  }

  #hand(done: boolean) {
    if (done) this.#typed = false;
    this.change(this.#html(), done);
  }

  /** Puts text in where the caret is, such as a merge tag, and gives the box the focus. */
  insertText(text: string) {
    this.#editor?.chain().focus().insertContent({ type: 'text', text }).run();
    this.#hand(true);
  }

  protected override firstUpdated(changed: PropertyValues) {
    super.firstUpdated(changed);
    this.#create();
  }

  override connectedCallback() {
    super.connectedCallback();
    // Moved in the page: drawn before, its editor went when it left.
    if (this.hasUpdated && !this.#editor) void this.updateComplete.then(() => this.#create());
  }

  // A new editor on the value: its own history, nothing of a text it showed before.
  #create() {
    this.#editor?.destroy();
    this.#editor = undefined;
    this.#typed = false;
    const element = this.renderRoot.querySelector<HTMLElement>('.content');
    if (!element || !this.isConnected) return;
    this.#editor = new Editor({
      element,
      injectCSS: false,
      extensions: [
        ...(this.inline ? [OneParagraph, NoNewParagraph] : [Document]),
        Paragraph,
        Text,
        HardBreak,
        Bold,
        Italic,
        ...(this.inline ? [] : [BulletList, OrderedList, ListItem]),
        // A link opens in a new tab only when the editor asks; the server adds what such a link needs (ADR-0084).
        Link.configure({
          openOnClick: false,
          autolink: false,
          linkOnPaste: false,
          HTMLAttributes: { target: null, rel: null },
        }),
        UndoRedo,
        Placeholder.configure({ placeholder: () => this.placeholder }),
      ],
      content: this.value,
      editorProps: { attributes: this.#attributes() },
      onUpdate: () => {
        this.#typed = true;
        this.#hand(false);
      },
      onBlur: ({ event }) => {
        // Leaving for the toolbar or the link's panel is no leaving, and a visit without typing changes nothing.
        if (event.relatedTarget instanceof Node && this.renderRoot.contains(event.relatedTarget)) return;
        if (this.#typed) this.#hand(true);
      },
      onTransaction: () => this.#refresh(),
    });
    this.#refresh();
  }

  #attributes(): Record<string, string> {
    return {
      role: 'textbox',
      'aria-multiline': this.inline ? 'false' : 'true',
      'aria-label': this.label,
      ...(this.description ? { 'aria-description': this.description } : {}),
      ...(this.language ? { lang: this.language } : {}),
      class: 'editor',
    };
  }

  protected override updated(changed: PropertyValues) {
    super.updated(changed);
    const editor = this.#editor;
    if (!editor) return;
    // The form's text, after Undo, in another language or for another field: never while it is being typed, so the
    // caret stays.
    if (changed.has('inline') || (changed.has('value') && !editor.isFocused && this.value !== this.#html())) {
      this.#create();
      return;
    }
    if (['label', 'description', 'language', 'placeholder'].some((name) => changed.has(name)))
      editor.setOptions({ editorProps: { attributes: this.#attributes() } });
  }

  override disconnectedCallback() {
    this.#editor?.destroy();
    this.#editor = undefined;
    super.disconnectedCallback();
  }

  #refresh() {
    const editor = this.#editor;
    if (!editor) return;
    const on = new Set(this.#tools.filter((tool) => editor.isActive(tool)));
    if (on.size !== this._on.size || [...on].some((tool) => !this._on.has(tool))) this._on = on;
  }

  #press(tool: Tool) {
    const editor = this.#editor;
    if (!editor) return;
    const chain = editor.chain().focus();
    if (tool === 'bold') chain.toggleBold().run();
    else if (tool === 'italic') chain.toggleItalic().run();
    else if (tool === 'bulletList') chain.toggleBulletList().run();
    else if (tool === 'orderedList') chain.toggleOrderedList().run();
    else {
      const link = editor.getAttributes('link') as { href?: string; target?: string | null };
      this._linkProblem = false;
      this._linking = { address: link.href ?? '', newTab: link.target === '_blank', existing: Boolean(link.href) };
      void this.updateComplete.then(() => this.renderRoot.querySelector<HTMLInputElement>('#address')?.focus());
    }
  }

  // The toolbar's arrow keys, Home and End move its one stop, as a toolbar's do.
  #moveTool(event: KeyboardEvent) {
    const count = this.#tools.length;
    const to =
      event.key === 'ArrowRight'
        ? (this._toolAt + 1) % count
        : event.key === 'ArrowLeft'
          ? (this._toolAt - 1 + count) % count
          : event.key === 'Home'
            ? 0
            : event.key === 'End'
              ? count - 1
              : null;
    if (to === null) return;
    event.preventDefault();
    this._toolAt = to;
    void this.updateComplete.then(() =>
      this.renderRoot.querySelector<HTMLElement>(`[data-tool="${this.#tools[to] ?? ''}"]`)?.focus(),
    );
  }

  #applyLink(form: HTMLFormElement) {
    const editor = this.#editor;
    if (!editor) return;
    const address = (form.elements.namedItem('address') as HTMLInputElement).value.trim();
    const newTab = (form.elements.namedItem('newTab') as HTMLInputElement).checked;
    if (!isLinkAddress(address)) {
      this._linkProblem = true;
      return;
    }
    const attributes = { href: address, target: newTab ? '_blank' : null };
    const chain = editor.chain().focus().extendMarkRange('link');
    if (editor.state.selection.empty && !editor.isActive('link'))
      chain.insertContent({ type: 'text', text: address, marks: [{ type: 'link', attrs: attributes }] }).run();
    else chain.setLink(attributes).run();
    this.#closeLink();
    this.#hand(true);
  }

  #removeLink() {
    this.#editor?.chain().focus().extendMarkRange('link').unsetLink().run();
    this.#closeLink();
    this.#hand(true);
  }

  #closeLink(focusTool = false) {
    this._linking = null;
    this._linkProblem = false;
    if (focusTool)
      void this.updateComplete.then(() => this.renderRoot.querySelector<HTMLElement>('[data-tool="link"]')?.focus());
  }

  protected override render(): TemplateResult {
    const tools = this.#tools;
    const at = Math.min(this._toolAt, tools.length - 1);
    return html`<div class="frame">
      <div
        class="toolbar"
        role="toolbar"
        aria-label=${this.#term('toolbar', this.label)}
        @keydown=${(event: KeyboardEvent) => this.#moveTool(event)}
      >
        ${tools.map(
          (tool, index) =>
            html`<button
              type="button"
              data-tool=${tool}
              tabindex=${index === at ? '0' : '-1'}
              ?data-on=${this._on.has(tool)}
              aria-pressed=${tool === 'link' ? nothing : this._on.has(tool) ? 'true' : 'false'}
              aria-expanded=${tool === 'link' ? (this._linking ? 'true' : 'false') : nothing}
              aria-controls=${tool === 'link' && this._linking ? 'link-panel' : nothing}
              aria-label=${this.#term(tool)}
              title=${this.#term(tool)}
              @focus=${() => (this._toolAt = index)}
              @mousedown=${(event: MouseEvent) => event.preventDefault()}
              @click=${() => this.#press(tool)}
            >
              <uui-icon name=${ICONS[tool]} aria-hidden="true"></uui-icon>
            </button>`,
        )}
      </div>
      ${this._linking ? this.#linkPanel(this._linking) : nothing}
      <div
        class="content"
        @click=${(event: MouseEvent) => {
          // A link in the text is edited, never followed: the backoffice's router would leave the form for it.
          if (event.composedPath().some((each) => each instanceof HTMLAnchorElement)) {
            event.preventDefault();
            event.stopPropagation();
          }
        }}
      ></div>
    </div>`;
  }

  #linkPanel(link: NonNullable<RichTextElement['_linking']>): TemplateResult {
    return html`<form
      class="link"
      id="link-panel"
      @submit=${(event: Event) => {
        event.preventDefault();
        this.#applyLink(event.currentTarget as HTMLFormElement);
      }}
      @keydown=${(event: KeyboardEvent) => {
        if (event.key !== 'Escape') return;
        event.preventDefault();
        event.stopPropagation();
        this.#closeLink(true);
      }}
    >
      <label for="address">${this.#term('address')}</label>
      <input
        id="address"
        name="address"
        type="text"
        inputmode="url"
        autocomplete="off"
        spellcheck="false"
        aria-invalid=${this._linkProblem ? 'true' : 'false'}
        aria-describedby=${this._linkProblem ? 'address-why' : nothing}
        .value=${link.address}
      />
      ${this._linkProblem ? html`<p class="why" id="address-why">${this.#term('addressInvalid')}</p>` : nothing}
      <label class="check"
        ><input type="checkbox" name="newTab" .checked=${link.newTab} />${this.#term('newTab')}</label
      >
      <div class="actions">
        <uui-button type="submit" look="primary" label=${this.#term('apply')}></uui-button>
        ${
          link.existing
            ? html`<uui-button
                look="secondary"
                label=${this.#term('removeLink')}
                @click=${() => this.#removeLink()}
              ></uui-button>`
            : nothing
        }
        <uui-button look="secondary" label=${this.#term('cancel')} @click=${() => this.#closeLink(true)}></uui-button>
      </div>
    </form>`;
  }

  static override styles = [
    jfbBackofficeTokens,
    css`
      :host {
        display: block;
      }

      .frame {
        border: 1px solid var(--jfb-bo-color-control-border, var(--uui-color-border-standalone));
        border-radius: var(--uui-border-radius);
        background: var(--uui-color-surface);
      }

      .frame:focus-within {
        outline: 2px solid var(--jfb-bo-color-focus);
        outline-offset: 0;
      }

      .toolbar {
        display: flex;
        flex-wrap: wrap;
        gap: 2px;
        padding: 2px;
        border-block-end: 1px solid var(--uui-color-border);
      }

      .toolbar button {
        display: inline-grid;
        place-items: center;
        inline-size: 32px;
        block-size: 32px;
        padding: 0;
        border: 0;
        border-radius: var(--uui-border-radius);
        background: none;
        color: var(--uui-color-text);
        cursor: pointer;
      }

      .toolbar button:hover {
        background: var(--uui-color-surface-emphasis);
      }

      .toolbar button[data-on] {
        background: var(--uui-color-selected);
        color: var(--uui-color-selected-contrast);
      }

      .toolbar button:focus-visible,
      .link input:focus-visible {
        outline: 2px solid var(--jfb-bo-color-focus);
        outline-offset: -2px;
      }

      .content .editor {
        min-block-size: 96px;
        padding: var(--uui-size-space-2) var(--uui-size-space-3);
        outline: none;
        white-space: pre-wrap;
        overflow-wrap: break-word;
      }

      :host([inline]) .content .editor {
        min-block-size: 0;
      }

      .content .editor > :first-child {
        margin-block-start: 0;
      }

      .content .editor > :last-child {
        margin-block-end: 0;
      }

      .content .editor p {
        margin: 0 0 var(--uui-size-space-2);
      }

      .content .editor ul,
      .content .editor ol {
        margin: 0 0 var(--uui-size-space-2);
        padding-inline-start: 1.4em;
      }

      .content .editor a {
        color: var(--uui-color-interactive);
        text-decoration: underline;
      }

      .content .editor p.is-editor-empty:first-child::before {
        content: attr(data-placeholder);
        float: inline-start;
        block-size: 0;
        color: var(--jfb-bo-color-text-muted, var(--uui-color-text-alt));
        pointer-events: none;
      }

      .link {
        display: grid;
        gap: var(--uui-size-space-2);
        padding: var(--uui-size-space-3);
        border-block-end: 1px solid var(--uui-color-border);
        background: var(--uui-color-surface-alt);
      }

      .link label {
        font-weight: 600;
      }

      .link input[type='text'] {
        box-sizing: border-box;
        inline-size: 100%;
        min-height: 36px;
        padding: var(--uui-size-space-2) var(--uui-size-space-3);
        border: 1px solid var(--jfb-bo-color-control-border, var(--uui-color-border-standalone));
        border-radius: var(--uui-border-radius);
        background: var(--uui-color-surface);
        color: inherit;
        font: inherit;
      }

      .link input[aria-invalid='true'] {
        border: 2px solid var(--jfb-bo-color-danger-text);
      }

      .link .check {
        display: flex;
        align-items: center;
        gap: var(--uui-size-space-2);
        font-weight: 400;
      }

      .why {
        margin: 0;
        color: var(--jfb-bo-color-danger-text, var(--uui-color-danger));
        font-size: var(--uui-type-small-size);
      }

      .actions {
        display: flex;
        flex-wrap: wrap;
        gap: var(--uui-size-space-2);
      }

      @media (forced-colors: active) {
        .toolbar button[data-on] {
          outline: 2px solid Highlight;
        }
      }
    `,
  ];
}

if (!customElements.get('jfb-rich-text')) customElements.define('jfb-rich-text', RichTextElement);

declare global {
  interface HTMLElementTagNameMap {
    'jfb-rich-text': RichTextElement;
  }
}
