// The Answers of a form's Insights view (ROADMAP 11.5b, IA 5.5, ADR-0212): a box per answered field in the form's order,
// each as its kind says: options as bars of their share of the answers, an older option and the visitors' own texts
// named, several choices said to add up past 100%; a ranking by its average position; a grid row by row; a number's
// figures, and a 0-to-10 scale's groups and score; a checkbox ticked or not; free text listed newest first, each
// opening its entry, Show more adding the next ones and giving the first of them the focus; other fields' share of the
// entries, and a field in a repeatable group per row. The fields nobody answered share one line. Every chart shows as
// its table where the switch says so. No entry says so, in words that say whether a filter is set; a failed read offers
// Try again; while another range loads the answers stay, dimmed and busy, and a page of texts that comes too late is let go.
import { afterEach, beforeAll, describe, expect, it, vi } from 'vitest';
import type {
  EntryListFilterRequestModel,
  InsightAnswerOptionResponseModel,
  InsightAnswerResponseModel,
  InsightAnswersResponseModel,
  InsightAnswerTextsResponseModel,
} from '../api';
import { loadEntryWords } from '../entries/entry-words';
import { all, deferred, must, registerEnglish, settle, shadow, testHost, text } from '../testing';
import type { InsightAnswersElement } from './insight-answers.element';
import { loadInsightWords } from './insight-words';

/** What the API answers, in turn, and what was asked of it. */
const api = vi.hoisted(() => ({
  answers: [] as unknown[],
  asked: [] as { form: string; span: unknown; filter: unknown }[],
  pages: [] as unknown[],
  more: [] as { field: string; after: string }[],
  /** What a read of the answers waits for before it answers. */
  hold: Promise.resolve() as Promise<unknown>,
  /** What a read of more texts waits for. */
  holdMore: Promise.resolve() as Promise<unknown>,
}));

vi.mock('./insight-api', async (original) => ({
  ...(await original<typeof import('./insight-api')>()),
  readAnswers: async (_host: unknown, form: string, span: unknown, filter: unknown) => {
    api.asked.push({ form, span, filter });
    await api.hold;
    return api.answers.length > 1 ? api.answers.shift() : api.answers[0];
  },
  readAnswerTexts: async (
    _host: unknown,
    _form: string,
    field: string,
    _span: unknown,
    _filter: unknown,
    after: string,
  ) => {
    api.more.push({ field, after });
    await api.holdMore;
    return api.pages.shift();
  },
}));

await import('./insight-answers.element');

const option = (label: string, count: number, more: Partial<InsightAnswerOptionResponseModel> = {}) => ({
  label,
  count,
  older: false,
  other: false,
  ...more,
});

const field = (
  key: string,
  label: string,
  kind: string,
  answered: number,
  more: Partial<InsightAnswerResponseModel> = {},
) =>
  ({
    key,
    label,
    kind,
    answered,
    removed: false,
    several: false,
    options: [],
    rows: [],
    ticked: 0,
    texts: [],
    ...more,
  }) as InsightAnswerResponseModel;

const answerText = (entry: string, said: string) => ({ entry, received: '2026-11-05T09:30:00Z', text: said });

const answers = (more: Partial<InsightAnswersResponseModel> = {}): InsightAnswersResponseModel => ({
  entries: 389,
  fields: [
    field('topic', 'Topic', 'options', 389, {
      options: [
        option('Sales', 163),
        option('Support', 121),
        option('Delivery', 74),
        option('Press', 31, { older: true }),
        option('Fax', 1),
      ],
    }),
    field('extras', 'Extras', 'options', 100, {
      several: true,
      options: [option('Coffee', 80), option('Tea', 60), option('', 5, { other: true })],
    }),
    field('rank', 'What matters most', 'ranking', 50, {
      options: [
        option('Price', 50, { position: 1.4 }),
        option('Speed', 50, { position: 2.1 }),
        option('Support', 50, { position: 2.5 }),
      ],
    }),
    field('grid', 'Rate each', 'grid', 40, {
      rows: [
        { key: 'r1', label: 'Food', answered: 40, options: [option('Good', 30), option('Bad', 10)], removed: false },
        { key: 'r2', label: 'Room', answered: 20, options: [option('Good', 5), option('Bad', 15)], removed: false },
        {
          key: 'r3',
          label: 'Parking',
          answered: 2500,
          options: [option('Good', 1), option('Bad', 2499)],
          removed: true,
        },
      ],
    }),
    field('nps', 'Recommend us', 'number', 200, {
      numbers: {
        average: 7.333,
        median: 8,
        lowest: 0,
        highest: 10,
        groups: { low: 50, middle: 60, high: 90, score: 20 },
      },
    }),
    field('age', 'Age', 'number', 10, { numbers: { average: 41.5, median: 39, lowest: 18, highest: 77 } }),
    field('news', 'Newsletter', 'checkbox', 120, { ticked: 120 }),
    field('wheelchair', 'Needs a wheelchair', 'checkbox', 3, { ticked: 3, parent: 'g1', parentLabel: 'Passengers' }),
    field('message', 'Your message', 'texts', 7, {
      texts: [answerText('e1', 'We would like a quote for 40 licences.'), answerText('e2', 'Please call me back.')],
      more: 'cursor-1',
    }),
    field('phone', 'Phone', 'answered', 226),
    field('pax', 'Passenger name', 'answered', 52, { parent: 'g1', parentLabel: 'Passengers' }),
    field('company', 'Company', 'texts', 0),
    field('fax', 'Fax', 'answered', 0, { removed: true }),
  ],
  ...more,
});

const SPAN = { from: '2026-10-09', to: '2026-11-07' };
const NO_FILTER: EntryListFilterRequestModel = { states: [], fields: [], forms: [] };

async function answersOf(first: unknown, ...then: unknown[]) {
  api.answers = [first, ...then];
  api.asked = [];
  api.pages = [];
  api.more = [];
  api.hold = Promise.resolve();
  api.holdMore = Promise.resolve();
  const host = testHost();
  const element = document.createElement('jfb-insight-answers') as InsightAnswersElement;
  element.form = 'form1';
  element.span = SPAN;
  element.filter = NO_FILTER;
  host.append(element);
  await settle(element);
  const root = shadow(element);
  const box = (heading: string) =>
    must(
      all(element, 'uui-box').find((each) => each.getAttribute('headline') === heading),
      heading,
    );
  const bars = (heading: string, at = 0) =>
    must(box(heading).querySelectorAll('jfb-insight-bars')[at], `the bars of ${heading}`);
  return { element, root, box, bars };
}

const said = (bars: { values: readonly { name: string; said: string }[] }) =>
  bars.values.map((value) => `${value.name}: ${value.said}`);

/** A table row's cells, each as its text, apart by one space. */
const cells = (row: Element): string => [...row.children].map((cell) => text(cell)).join(' ');

beforeAll(async () => {
  registerEnglish();
  await Promise.all([loadInsightWords('en'), loadEntryWords('en')]);
});

afterEach(() => {
  document.body.replaceChildren();
});

describe('the Answers', () => {
  it('reads the span and the filter, and gives each answered field its box in the order of the form', async () => {
    const { root, element } = await answersOf(answers());
    expect(api.asked).toEqual([{ form: 'form1', span: SPAN, filter: NO_FILTER }]);
    expect(text(root.querySelector('.lead'))).toBe('Answers from 389 entries.');
    expect(all(element, 'uui-box').map((each) => each.getAttribute('headline'))).toEqual([
      'Topic',
      'Extras',
      'What matters most',
      'Rate each',
      'Recommend us',
      'Age',
      'Newsletter',
      'Needs a wheelchair',
      'Your message',
      'Phone',
      'Passenger name',
    ]);
    expect(text(root.querySelector('.unanswered'))).toBe(
      'Not answered in this period: Company and Fax (no longer on the form).',
    );
  });

  it('draws options as bars of their share of the answers, naming an older option and the own texts', async () => {
    const { box, bars } = await answersOf(answers());
    expect(text(box('Topic').querySelector('.count'))).toBe('389 answers');
    expect(bars('Topic').whole).toBe(389);
    expect(said(bars('Topic'))).toEqual([
      'Sales: 42% (163)',
      'Support: 31% (121)',
      'Delivery: 19% (74)',
      'Press (no longer an option): 8% (31)',
      'Fax: 0.3% (1)',
    ]);
    // Each bar is its share of the answers, not of the largest option: 163 of 389 is 41.9% of the width.
    await settle(bars('Topic'));
    const widths = [...shadow(bars('Topic')).querySelectorAll('rect.bar')].map((rect) =>
      Number(rect.getAttribute('width')),
    );
    expect(widths[0]).toBeCloseTo(41.9, 1);
    expect(widths[3]).toBeCloseTo(8, 0);
    expect(box('Topic').querySelector('.note')).toBeNull();
    expect(said(bars('Extras'))).toEqual(['Coffee: 80% (80)', 'Tea: 60% (60)', 'Other, in their own words: 5% (5)']);
    expect(text(box('Extras').querySelector('.note'))).toBe(
      'More than one could be chosen, so the shares add up to more than 100%.',
    );
  });

  it("ranks a ranking's options by their average position, the first the longest bar", async () => {
    const { box, bars } = await answersOf(answers());
    const ranking = bars('What matters most');
    expect(ranking.ordered).toBe(true);
    expect(ranking.whole).toBe(3);
    expect(ranking.values.map((value) => value.value)).toEqual(
      [2.6, 1.9, 1.5].map((value) => expect.closeTo(value, 5)),
    );
    expect(said(ranking)).toEqual([
      'Price: average position 1.4',
      'Speed: average position 2.1',
      'Support: average position 2.5',
    ]);
    expect(text(box('What matters most').querySelector('.note'))).toBe(
      'The higher an option ranks on average, the longer its bar.',
    );
  });

  it('goes through a grid row by row, each of its own answers, across the whole width', async () => {
    const { box, bars } = await answersOf(answers());
    const grid = box('Rate each');
    expect(grid.classList.contains('wide')).toBe(true);
    expect([...grid.querySelectorAll('h3')].map((heading) => text(heading))).toEqual([
      'Food',
      'Room',
      'Parking (no longer on the form)',
    ]);
    // A share far under 1% keeps its first figure.
    expect(said(bars('Rate each', 2))).toEqual(['Good: 0.04% (1)', 'Bad: 100% (2,499)']);
    expect(said(bars('Rate each', 0))).toEqual(['Good: 75% (30)', 'Bad: 25% (10)']);
    expect(bars('Rate each', 1).whole).toBe(20);
    expect(said(bars('Rate each', 1))).toEqual(['Good: 25% (5)', 'Bad: 75% (15)']);
  });

  it('gives a number its figures, and a 0-to-10 scale its three groups and its recommendation score', async () => {
    const { box, bars } = await answersOf(answers());
    const scale = box('Recommend us');
    expect([...scale.querySelectorAll('.figures div')].map((figure) => text(figure))).toEqual([
      'Average 7.33',
      'Median 8',
      'Lowest 0',
      'Highest 10',
    ]);
    expect(said(bars('Recommend us'))).toEqual(['0 to 6: 25% (50)', '7 and 8: 30% (60)', '9 and 10: 45% (90)']);
    expect(text(scale.querySelector('.score'))).toBe('Recommendation score: +20');
    const age = box('Age');
    expect(text(age.querySelector('.figures'))).toBe('Average 41.5 Median 39 Lowest 18 Highest 77');
    expect(age.querySelector('jfb-insight-bars')).toBeNull();
    expect(age.querySelector('.score')).toBeNull();
  });

  it("says how many of the entries ticked a checkbox and how many did not, and a row's box how many rows ticked it", async () => {
    const { box, bars } = await answersOf(answers());
    expect(bars('Newsletter').whole, 'an unticked box stores nothing').toBe(389);
    expect(said(bars('Newsletter'))).toEqual(['Ticked: 31% (120)', 'Not ticked: 69% (269)']);
    expect(box('Newsletter').querySelector('.count'), 'its ticks are said once').toBeNull();
    expect(text(box('Needs a wheelchair').querySelector('.line'))).toBe('Ticked in 3 rows.');
  });

  it('says how often any other field was answered, and that a field in a repeatable group is answered per row', async () => {
    const { box } = await answersOf(answers());
    expect(text(box('Phone').querySelector('.line'))).toBe('Answered in 58% of the entries.');
    const passenger = box('Passenger name');
    expect(text(passenger.querySelector('.count'))).toBe('52 answers');
    expect(passenger.querySelector('.line')).toBeNull();
    expect(text(passenger.querySelector('.note'))).toBe('One answer for each row of Passengers.');
  });

  it('lists free-text answers, each opening its entry, and Show more adds the next ones and moves the focus to them', async () => {
    const { element, box } = await answersOf(answers());
    const texts = box('Your message');
    expect(texts.querySelector('ul')?.getAttribute('aria-label')).toBe('The newest answers to Your message');
    const links = () => [...box('Your message').querySelectorAll<HTMLAnchorElement>('.texts a')];
    expect(links().map((link) => [text(link), link.getAttribute('href')])).toEqual([
      ['We would like a quote for 40 licences.', 'section/jfb-forms/workspace/jfb-entry/edit/e1'],
      ['Please call me back.', 'section/jfb-forms/workspace/jfb-entry/edit/e2'],
    ]);
    expect(text(texts.querySelector('.when'))).toContain('Nov');
    api.pages = [
      {
        texts: [answerText('e3', 'Is the workshop in English?')],
        more: null,
      } satisfies InsightAnswerTextsResponseModel,
    ];
    must(texts.querySelector<HTMLElement>('.more uui-button'), 'Show more').click();
    await settle(element);
    expect(api.more).toEqual([{ field: 'message', after: 'cursor-1' }]);
    expect(links().map((link) => text(link))).toEqual([
      'We would like a quote for 40 licences.',
      'Please call me back.',
      'Is the workshop in English?',
    ]);
    expect(shadow(element).activeElement, 'the first new answer').toBe(links()[2]);
    expect(box('Your message').querySelector('.more'), 'nothing more to show').toBeNull();
  });

  it('keeps Show more and says so when the next answers could not be read', async () => {
    const { element, box } = await answersOf(answers());
    api.pages = [undefined];
    must(box('Your message').querySelector<HTMLElement>('.more uui-button'), 'Show more').click();
    await settle(element);
    expect(text(box('Your message').querySelector('.why[role="alert"]'))).toBe("More answers couldn't be loaded.");
    expect(box('Your message').querySelector('.more uui-button')).not.toBeNull();
    expect(box('Your message').querySelectorAll('.texts li')).toHaveLength(2);
  });

  it('lets a page of texts go that comes after another range was asked for', async () => {
    const later = answers({
      fields: [field('message', 'Your message', 'texts', 1, { texts: [answerText('e9', 'Newer')] })],
    });
    const { element, box } = await answersOf(answers(), later);
    const page = deferred<undefined>();
    api.holdMore = page.promise;
    api.pages = [{ texts: [answerText('e3', 'Too late')], more: null }];
    must(box('Your message').querySelector<HTMLElement>('.more uui-button'), 'Show more').click();
    element.span = { from: '2026-11-01', to: '2026-11-07' };
    await settle(element);
    page.resolve(undefined);
    await settle(element);
    expect([...box('Your message').querySelectorAll('.texts a')].map((link) => text(link))).toEqual(['Newer']);
  });

  it('shows every chart as its table where the switch says so', async () => {
    const { element, box } = await answersOf(answers());
    element.tables = true;
    await settle(element);
    const rows = (heading: string) => [...box(heading).querySelectorAll('tr')].map(cells);
    expect(rows('Topic')).toEqual([
      'Option Answers Share',
      'Sales 163 42%',
      'Support 121 31%',
      'Delivery 74 19%',
      'Press (no longer an option) 31 8%',
      'Fax 1 0.3%',
    ]);
    expect(rows('What matters most')).toEqual([
      'Option Average position Answers',
      'Price 1.4 50',
      'Speed 2.1 50',
      'Support 2.5 50',
    ]);
    expect(rows('Rate each')).toEqual([
      'Row Good Bad',
      'Food 75% (30) 25% (10)',
      'Room 25% (5) 75% (15)',
      'Parking (no longer on the form) 0.04% (1) 100% (2,499)',
    ]);
    expect(rows('Recommend us')).toEqual(['Group Answers Share', '0 to 6 50 25%', '7 and 8 60 30%', '9 and 10 90 45%']);
    expect(text(box('Recommend us').querySelector('caption'))).toBe('Recommend us in three groups');
    expect(rows('Newsletter')).toEqual(['Option Answers Share', 'Ticked 120 31%', 'Not ticked 269 69%']);
    expect(box('Topic').querySelector('jfb-insight-bars')).toBeNull();
    // Figures and texts have no chart to swap.
    expect(box('Recommend us').querySelector('.figures')).not.toBeNull();
    expect(box('Your message').querySelectorAll('.texts li')).toHaveLength(2);
  });

  it('says no entry is in the period, in words that say whether a filter is set', async () => {
    const { root, element } = await answersOf(answers({ entries: 0, fields: [] }));
    expect(text(root.querySelector('uui-box .line'))).toBe('No entries in this period.');
    element.filter = { states: ['approved'], fields: [], forms: [] };
    await settle(element);
    expect(text(root.querySelector('uui-box .line'))).toBe('No entries in this period match these filters.');
  });

  it('offers Try again where the answers could not be read', async () => {
    const { root, element } = await answersOf(undefined, answers());
    expect(text(root.querySelector('[role="alert"] span'))).toBe("The answers couldn't be loaded.");
    must(root.querySelector<HTMLElement>('[role="alert"] uui-button'), 'Try again').click();
    await settle(element);
    expect(api.asked).toHaveLength(2);
    expect(root.querySelector('.lead')).not.toBeNull();
  });

  it('lets a page of texts go that was asked for while another range was on its way', async () => {
    const later = answers({
      fields: [field('message', 'Your message', 'texts', 1, { texts: [answerText('e9', 'Newer')] })],
    });
    const { element, box } = await answersOf(answers(), later);
    const range = deferred<undefined>();
    api.hold = range.promise;
    element.span = { from: '2026-11-01', to: '2026-11-07' };
    await settle(element);
    // The old answers still show, dimmed, and Show more is pressed on them.
    const page = deferred<undefined>();
    api.holdMore = page.promise;
    api.pages = [{ texts: [answerText('e3', 'Too late')], more: null }];
    must(box('Your message').querySelector<HTMLElement>('.more uui-button'), 'Show more').click();
    range.resolve(undefined);
    await settle(element);
    page.resolve(undefined);
    await settle(element);
    expect([...box('Your message').querySelectorAll('.texts a')].map((link) => text(link))).toEqual(['Newer']);
  });

  it('keeps the answers shown, dimmed and busy, while another range loads', async () => {
    const { root, element } = await answersOf(answers(), answers({ entries: 12 }));
    const held = deferred<undefined>();
    api.hold = held.promise;
    element.span = { from: '2026-11-01', to: '2026-11-07' };
    await settle(element);
    expect(root.querySelector('.answers')?.getAttribute('aria-busy')).toBe('true');
    expect(text(root.querySelector('.lead'))).toBe('Answers from 389 entries.');
    expect(text(root.querySelector('[role="status"]'))).toBe('Loading the answers');
    held.resolve(undefined);
    await settle(element);
    expect(root.querySelector('.answers')?.getAttribute('aria-busy')).toBe('false');
    expect(text(root.querySelector('.lead'))).toBe('Answers from 12 entries.');
  });
});
