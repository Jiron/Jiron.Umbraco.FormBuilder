// "Learn more" (ROADMAP 17.2a, section 4.13, docs/ux/copy.md): a setting or notice whose help needs more than two
// sentences links to its page of the documentation, with words that make sense on their own ("Learn more about
// retention"). Every address comes from one map, learn-more.json, which the link check (scripts/link-check.mjs) holds to
// the pages and headings under docs/, so a link never names a page or heading that isn't there. The link opens in a new
// tab, as leaving the backoffice would lose what the user was doing, and a screen reader hears that it does.
import { css, html, type TemplateResult } from '@umbraco-cms/backoffice/external/lit';
import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import { jfbBackofficeTokens, jfbVisuallyHidden } from '../styles/tokens';
import map from './learn-more.json';

/** The topics the documentation has a page or heading for. */
export type LearnMoreTopic = keyof typeof map.pages;

/** Every topic of the map, in its order. */
export const LEARN_MORE_TOPICS = Object.keys(map.pages) as readonly LearnMoreTopic[];

/** The address of a topic's page, and its heading where it names one. */
export const learnMoreUrl = (topic: LearnMoreTopic): string => `${map.base}${map.pages[topic]}`;

export const LEARN_MORE = 'jfb-learn-more';

/** The link to a topic's page, in the user's words. */
export class LearnMoreElement extends UmbLitElement {
  static override properties = { topic: { type: String } };

  static override styles = [
    jfbBackofficeTokens,
    jfbVisuallyHidden,
    css`
      :host {
        display: inline;
      }

      /* Umbraco's link colour, as the section's other links: the browser's own blue is 1.4:1 on the dark theme's box. */
      a {
        color: var(--uui-color-interactive);
        text-decoration: underline;
      }

      a:hover {
        color: var(--uui-color-interactive-emphasis);
      }
    `,
  ];

  declare topic: LearnMoreTopic | undefined;

  override render() {
    const topic = this.topic;
    if (!topic || !Object.hasOwn(map.pages, topic)) return html``;
    return html`<a href=${learnMoreUrl(topic)} target="_blank" rel="noopener noreferrer"
      >${this.localize.term(`jfbLearnMore_${topic}`)}<span class="visually-hidden">
        ${this.localize.term('jfbLearnMore_newTab')}</span
      ></a
    >`;
  }
}

if (!customElements.get(LEARN_MORE)) customElements.define(LEARN_MORE, LearnMoreElement);

/** A topic's link, to put after a help text or a notice. */
export const learnMore = (topic: LearnMoreTopic): TemplateResult =>
  html`<jfb-learn-more topic=${topic}></jfb-learn-more>`;

declare global {
  interface HTMLElementTagNameMap {
    'jfb-learn-more': LearnMoreElement;
  }
}
