import type { Page } from '@playwright/test';
import { expect, test } from '../../fixtures.ts';
import { LANGUAGES, formPages, pathOf } from '../../support/demo-content.ts';

// The runtime's frame on the demo (ROADMAP 6.4b, ADR-0110): every form of the markup contract is enhanced once and
// says so once with `jfb:ready`, a form the page puts in later is enhanced through `JironFormBuilder.init(root)`, the
// chunk with the logic loads only once the visitor touches a form, or at once where the server marked a default to fill
// as the form shows (ADR-0118, ADR-0124), and nothing writes an error to the console.

interface Ready {
  readonly id: string;
  readonly key: string;
  readonly alias: string;
}

declare global {
  interface Window {
    jfbReadies?: Ready[];
    JironFormBuilder?: { readonly version: string; init(root?: ParentNode): void };
  }
}

const CHUNK = /\/jfb\/assets\/runtime\/chunks\/form-[^/]+\.js$/;

/** Records every `jfb:ready` from the start of each document, and every chunk the page asks for. */
async function watch(page: Page): Promise<{ chunks: string[]; errors: string[] }> {
  await page.addInitScript(() => {
    window.jfbReadies = [];
    document.addEventListener('jfb:ready', (event) => {
      const detail = (event as CustomEvent<{ key: string; alias: string }>).detail;
      window.jfbReadies?.push({ id: (event.target as HTMLElement).id, key: detail.key, alias: detail.alias });
    });
  });
  const chunks: string[] = [];
  const errors: string[] = [];
  page.on('request', (request) => {
    if (CHUNK.test(new URL(request.url()).pathname)) chunks.push(request.url());
  });
  page.on('console', (message) => {
    if (message.type() === 'error') errors.push(message.text());
  });
  page.on('pageerror', (error) => errors.push(error.message));
  return { chunks, errors };
}

const readies = (page: Page): Promise<Ready[]> => page.evaluate(() => window.jfbReadies ?? []);

/** Each form of the contract on the page with its key and its model data's alias, in document order. */
const formsOn = (page: Page): Promise<Ready[]> =>
  page.evaluate(() =>
    [...document.querySelectorAll<HTMLFormElement>('form[data-jfb-form]')].map((form) => ({
      id: form.id,
      key: form.dataset['jfbForm'] ?? '',
      alias:
        (JSON.parse(form.querySelector('script[data-jfb-model]')?.textContent ?? '{}') as { alias?: string }).alias ??
        '',
    })),
  );

test('every form on every demo page is enhanced once, says so once, and loads no logic until it is touched', async ({
  page,
}) => {
  const { chunks, errors } = await watch(page);
  const filling: string[] = [];
  for (const language of LANGUAGES) {
    for (const path of [`/${language}/`, ...formPages().map((shown) => pathOf(shown.texts[language], language))]) {
      // A default to fill as the form shows loads the logic at once; the page's own markup says whether it has one.
      const marked = (await (await page.request.get(path)).text()).includes('data-jfb-default');
      const before = chunks.length;
      await page.goto(path);
      const forms = await formsOn(page);
      expect(forms.length, `${path}: a form at least`).toBeGreaterThan(0);
      await expect.poll(() => readies(page), `${path}: one jfb:ready per form, in order`).toEqual(forms);
      await page.waitForLoadState('networkidle');
      expect(await readies(page), `${path}: and no more`).toEqual(forms);
      if (marked) {
        filling.push(path);
        await expect(page.locator('[data-jfb-default]'), `${path}: its defaults filled as it shows`).toHaveCount(0);
      } else {
        expect(chunks.slice(before), `${path}: no logic before a form is touched`).toEqual([]);
      }
    }
  }
  expect(filling, 'the contact form, prefilled from the query, fills as it shows').toEqual(
    LANGUAGES.map((language) =>
      pathOf(formPages().find((shown) => shown.form === 'contact')!.texts[language], language),
    ),
  );
  expect(errors, 'nothing wrote an error to the console').toEqual([]);
});

test("the logic loads on a form's first interaction, once for the page, and on a form's change without focus", async ({
  page,
}) => {
  const { chunks, errors } = await watch(page);
  // The feedback form has nothing to fill as it shows, so its logic waits for the visitor.
  const feedback = formPages().find((shown) => shown.form === 'feedback') ?? formPages()[0]!;
  await page.goto(pathOf(feedback.texts.en, 'en'));
  const forms = page.locator('form[data-jfb-form]');
  await expect(forms, 'the page form and the newsletter').toHaveCount(2);
  await page.waitForLoadState('networkidle');
  expect(chunks).toEqual([]);

  const loaded = page.waitForResponse((response) => CHUNK.test(new URL(response.url()).pathname));
  await forms.first().locator('input:not([type="hidden"]):not([tabindex="-1"])').first().focus();
  expect((await loaded).status(), 'the chunk is served').toBe(200);
  await forms.last().locator('input:not([type="hidden"]):not([tabindex="-1"])').first().focus();
  await page.waitForLoadState('networkidle');
  expect(chunks, 'one request for the page, however many forms are touched').toHaveLength(1);

  // A browser's autofill changes a field without focusing it.
  const fresh = await page.context().newPage();
  const watched = await watch(fresh);
  await fresh.goto(pathOf(feedback.texts.en, 'en'));
  await fresh.waitForLoadState('networkidle');
  const changed = fresh.waitForResponse((response) => CHUNK.test(new URL(response.url()).pathname));
  await fresh
    .locator('form[data-jfb-form] input[type="email"]')
    .first()
    .evaluate((input: HTMLInputElement) => {
      input.value = 'ada@example.test';
      input.dispatchEvent(new Event('change', { bubbles: true }));
    });
  expect((await changed).status()).toBe(200);
  expect(watched.errors).toEqual([]);
  await fresh.close();
  expect(errors, 'nothing wrote an error to the console').toEqual([]);
});

test('a form the page puts in later is enhanced through init(root), once, and one of another contract is left alone', async ({
  page,
}) => {
  const { chunks, errors } = await watch(page);
  await page.goto('/en/');
  await expect.poll(() => readies(page)).toHaveLength(1);
  const before = await readies(page);

  // The page takes its form out and puts it back, as a site that renders forms by script does.
  await page.evaluate(() => {
    const form = document.querySelector<HTMLFormElement>('form[data-jfb-form]')!;
    const holder = document.createElement('div');
    holder.id = 'later';
    const copy = form.cloneNode(true) as HTMLFormElement;
    form.replaceWith(holder);
    holder.append(copy);
    const other = copy.cloneNode(true) as HTMLFormElement;
    other.id = 'jfb-other';
    other.dataset['jfbContract'] = '2';
    holder.append(other);
    window.JironFormBuilder?.init(holder);
    window.JironFormBuilder?.init(holder);
    window.JironFormBuilder?.init();
  });
  const after = await readies(page);
  expect(after.slice(0, before.length)).toEqual(before);
  expect(
    after.slice(before.length),
    'the form put back says it is ready once; the other contract says nothing',
  ).toEqual(before);

  const loaded = page.waitForResponse((response) => CHUNK.test(new URL(response.url()).pathname));
  await page.locator('#later form').first().locator('input[type="email"]').focus();
  await loaded;
  expect(chunks).toHaveLength(1);
  expect(errors, 'nothing wrote an error to the console').toEqual([]);
});
