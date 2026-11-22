// "Learn more" (ROADMAP 17.2a): every topic of the map has a page under docs/ with the heading it names (the link check,
// scripts/link-check.mjs, and its test hold the map to the pages), a link says its topic in words that stand alone, opens
// in a new tab and says so to a screen reader, and the dictionaries hold exactly one phrase per topic in both languages.
import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { afterEach, beforeAll, describe, expect, it } from 'vitest';
import de from '../lang/learn-more.de';
import en from '../lang/learn-more.en';
import { FIELD_TOPICS, LEAD_TOPICS } from '../settings/settings-screen.element';
import { must, registerEnglish, shadow, text } from '../testing';
import { LEARN_MORE_TOPICS, LearnMoreElement, learnMoreUrl, type LearnMoreTopic } from './learn-more.element';
import map from './learn-more.json';
import { sourceFiles } from '../source-files';

async function linkOf(topic: string | undefined) {
  const element = new LearnMoreElement();
  element.topic = topic as LearnMoreTopic | undefined;
  document.body.append(element);
  await element.updateComplete;
  return shadow(element).querySelector('a');
}

beforeAll(() => registerEnglish());

afterEach(() => {
  document.body.replaceChildren();
});

describe('a Learn more link', () => {
  it("goes to its topic's page and heading in the documentation", () => {
    expect(learnMoreUrl('retention')).toBe(
      'https://github.com/Jiron/Jiron.Umbraco.FormBuilder/blob/main/docs/privacy.md#retention',
    );
    expect(map.base).toMatch(/^https:\/\/github\.com\/Jiron\/Jiron\.Umbraco\.FormBuilder\/blob\/main\/docs\/$/);
    for (const topic of LEARN_MORE_TOPICS)
      expect(map.pages[topic], topic).toMatch(/^[a-z-]+(\/[a-z-]+)*\.md(#[a-z0-9-]+)?$/);
  });

  it('says its topic in words that stand alone, opens in a new tab and tells a screen reader so', async () => {
    const link = must(await linkOf('retention'), 'the link');
    expect(link.getAttribute('href')).toBe(learnMoreUrl('retention'));
    expect([link.getAttribute('target'), link.getAttribute('rel')]).toEqual(['_blank', 'noopener noreferrer']);
    expect(text(link)).toBe('Learn more about retention (opens in a new tab)');
    expect(text(link.querySelector('.visually-hidden'))).toBe('(opens in a new tab)');
  });

  it('shows nothing for a topic the map lacks, or none', async () => {
    expect(await linkOf('nothing')).toBeNull();
    expect(await linkOf(undefined)).toBeNull();
  });
});

describe('the Learn more topics', () => {
  it('are each linked from a screen of the section', () => {
    const src = join(dirname(fileURLToPath(import.meta.url)), '..');
    const code = sourceFiles(src)
      .filter((file) => file.endsWith('.ts') && !file.endsWith('.test.ts') && !/^(docs|lang|api)[\\/]/.test(file))
      .map((file) => readFileSync(join(src, file), 'utf8'))
      .join(String.fromCharCode(10));
    // A link is written learnMore('topic'), or names its topic in the settings screen's maps of boxes and fields.
    const mapped = new Set(
      [...Object.values(LEAD_TOPICS), ...Object.values(FIELD_TOPICS)].flatMap((topics) => Object.values(topics)),
    );
    for (const topic of LEARN_MORE_TOPICS)
      expect(code.includes(`learnMore('${topic}')`) || mapped.has(topic), topic).toBe(true);
  });
});

describe("the Learn more links' words", () => {
  it('are one phrase per topic of the map, and what a screen reader hears after it, in English and German', () => {
    const keys = ['newTab', ...LEARN_MORE_TOPICS].sort();
    expect(Object.keys(en.jfbLearnMore).sort()).toEqual(keys);
    expect(Object.keys(de.jfbLearnMore).sort()).toEqual(keys);
    for (const topic of LEARN_MORE_TOPICS) {
      expect(en.jfbLearnMore[topic], topic).toMatch(/^Learn more about /);
      expect(de.jfbLearnMore[topic], topic).toMatch(/^Mehr (zu|zum|zur|dazu)[ ,]/);
    }
  });
});
