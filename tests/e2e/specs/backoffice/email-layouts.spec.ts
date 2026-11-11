import AxeBuilder from '@axe-core/playwright';
import type { APIRequestContext, Locator, Page, TestInfo } from '@playwright/test';
import { expect, test } from '../../fixtures.ts';
import { axeInside } from '../../support/axe.ts';
import { API, unique } from '../../support/forms-api.ts';
import { bodyOf, headerOf, visibleText } from '../../support/mail.ts';

// Library > Email layouts (ROADMAP 12.1, IA 7, ADR-0215): a designer makes a layout through the screens: the letter look,
// the logo from Umbraco's media picker, a link colour too light for text fixed with the colour the check offers, the
// dark scheme's own colour, and a header and footer; the preview shows a sample entry in the layout, light and dark;
// Send test email saves the layout first, and the test mail in the pickup folder is the saved layout's, to the designer.
// Axe checks the editor, and the sample mail in a page of its own.

const SECTION = '/umbraco/section/jfb-forms';
const LAYOUTS = `${SECTION}/workspace/jfb-email-layouts`;

interface Layout {
  readonly alias: string;
  readonly design: {
    readonly look: string;
    readonly logoMedia: string | null;
    readonly light: Record<string, string>;
    readonly dark: Record<string, string>;
    readonly header: Record<string, string>;
    readonly footer: Record<string, string>;
  };
}

/** Umbraco's open picker and dialogs. */
const modal = (page: Page) => page.locator('umb-backoffice-modal-container');

async function layoutOf(api: APIRequestContext, key: string): Promise<Layout> {
  const answer = await api.get(`${API}/email-layout/${key}`);
  expect(answer.ok(), await answer.text()).toBe(true);
  return (await answer.json()) as Layout;
}

/** A screenshot kept with the run's results, for the screens' review. */
async function look(page: Page, info: TestInfo, what: string): Promise<void> {
  await page.mouse.move(0, 0);
  const path = info.outputPath(`${what.replaceAll(' ', '-')}.png`);
  await page.screenshot({ path });
  await info.attach(what, { path, contentType: 'image/png' });
}

/** Axe on the sample mail in a page of its own: the backoffice's checks leave the preview's frame alone (ADR-0189). */
async function axeOnTheMail(page: Page, view: Locator, scheme: string) {
  const html = await view.locator('iframe').evaluate((frame) => (frame as HTMLIFrameElement).srcdoc);
  const sheet = await page.context().newPage();
  try {
    await sheet.setContent(html);
    const result = await new AxeBuilder({ page: sheet })
      .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'])
      .analyze();
    expect(
      result.violations.map(
        (violation) => `${violation.id}: ${violation.nodes.map((node) => node.target.join(' ')).join(', ')}`,
      ),
      `axe on the mail, ${scheme}`,
    ).toEqual([]);
  } finally {
    await sheet.close();
  }
}

test('a designer makes an email layout through the screens, previews it and gets its test mail', async ({
  page,
  loginAs,
  apiAs,
  accountOf,
  waitForMail,
}, info) => {
  test.setTimeout(300_000);
  const api = await apiAs('designer');
  const designer = accountOf('designer');
  const name = unique('Brand mail');
  const header = `${name} greets you`;

  await page.setViewportSize({ width: 1440, height: 900 });
  await loginAs('designer');
  await page.goto(SECTION);
  await page
    .locator('umb-menu-item-layout[label="Email layouts"], uui-menu-item[label="Email layouts"]')
    .first()
    .click();
  await expect(page).toHaveURL(new RegExp(`${LAYOUTS}$`));
  await look(page, info, 'the list screen');

  // A new layout has no users to ask for: only a saved one reads them.
  const usersAsked: string[] = [];
  page.on('request', (request) => {
    if (request.url().includes('/used-by')) usersAsked.push(request.url());
  });
  await page.getByRole('button', { name: 'Create email layout' }).click();
  await expect(page).toHaveURL(/\/workspace\/jfb-email-layout\/create$/);
  const view = page.locator('jfb-email-layout-view');
  await expect(view.locator('iframe')).toBeVisible();
  const box = page.getByRole('textbox', { name: 'Name of the email layout' });
  await box.fill(name);
  await box.press('Tab');

  // The letter look and the logo from the media library.
  await view.getByLabel('Look', { exact: true }).selectOption('letter');
  await view.locator('umb-input-media').getByRole('button', { name: 'Choose' }).click();
  const picture = page.getByRole('img', { name: 'Sample picture' });
  await expect(picture).toBeInViewport({ ratio: 1 });
  await picture.click({ force: true });
  await modal(page).getByRole('button', { name: 'Choose', exact: true }).click();
  await expect(view.locator('umb-input-media').getByText('Sample picture')).toBeVisible();
  // The picker gives the focus back to its button as it closes; typing before that would go to the button.
  await expect(page.locator('umb-media-picker-modal')).toHaveCount(0);

  // Links too light for text, fixed with the colour the check offers; the dark scheme's own link colour.
  const links = view.getByRole('textbox', { name: 'Links and buttons', exact: true });
  await links.fill('#7fb3ff');
  await expect(links).toHaveValue('#7fb3ff');
  await links.press('Tab');
  await expect(view.locator('#colour-light-accent-check')).toContainText('on the mail: text needs 4.5:1.');
  await view.locator('#colour-light-accent-check').getByRole('button').click();
  await expect(view.locator('#colour-light-accent-check')).toContainText('on the mail: passes');
  await view.getByRole('group', { name: 'Colours to set' }).getByRole('button', { name: 'Dark', exact: true }).click();
  const darkLinks = view.getByRole('textbox', { name: 'Links and buttons', exact: true });
  await darkLinks.fill('#9cc4ff');
  await darkLinks.press('Tab');

  // The header and the footer, in the site's default language.
  const headerBox = view.getByLabel('Header', { exact: true });
  await headerBox.fill(`<p>${header}</p>`);
  await headerBox.press('Tab');
  const footerBox = view.getByLabel('Footer', { exact: true });
  await footerBox.fill('<p>Brand Ltd, Main Street 1</p>');
  await footerBox.press('Tab');
  const mail = page.frameLocator('jfb-email-layout-view iframe');
  await expect(mail.getByText(header)).toBeVisible();
  await expect(mail.locator('img')).toHaveCount(1);
  await axeInside(page, 'jfb-email-layout-view');
  await axeOnTheMail(page, view, 'light');
  await look(page, info, 'the editor');
  await view.getByRole('group', { name: 'Colour scheme of the preview' }).getByRole('button', { name: 'Dark' }).click();
  await expect(mail.getByText(header)).toBeVisible();
  await axeOnTheMail(page, view, 'dark');
  await look(page, info, 'the dark preview');

  // Send test email saves the layout first, then mails the saved layout to the designer.
  expect(usersAsked).toEqual([]);
  await view.getByRole('button', { name: 'Send test email' }).click();
  await expect(page).toHaveURL(/\/workspace\/jfb-email-layout\/edit\/[0-9a-f-]{36}/);
  const said = `A test mail is on its way to ${designer.email}.`;
  // One alert says so. A toast is short-lived, and under load closes soon after it shows, so one assertion catches it.
  await expect(page.getByRole('alert').filter({ hasText: said })).toHaveCount(1);
  const key = /\/edit\/([0-9a-f-]{36})/.exec(page.url())?.[1] ?? '';
  const saved = await layoutOf(api, key);
  expect(saved.design.look).toBe('letter');
  expect(saved.design.logoMedia).toMatch(/^[0-9a-f-]{36}$/);
  expect(saved.design.light['accent']).toMatch(/^#[0-9a-f]{6}$/);
  expect(saved.design.dark['accent']).toBe('#9cc4ff');
  expect(saved.design.header['']).toBe(`<p>${header}</p>`);
  const sent = await waitForMail(
    (candidate) =>
      headerOf(candidate.raw, 'To').includes(designer.email) && visibleText(bodyOf(candidate.raw)).includes(header),
  );
  expect(visibleText(bodyOf(sent.raw))).toContain('Brand Ltd, Main Street 1');
  await expect(page.locator('jfb-library-item-editor').getByText('Not used by any action')).toBeVisible();

  // Narrow: one side at a time, by the switch.
  await page.setViewportSize({ width: 1024, height: 768 });
  await expect(view.getByRole('button', { name: 'Preview', exact: true })).toBeVisible();
  await axeInside(page, 'jfb-email-layout-view');
  await look(page, info, 'the editor in a narrow window');
  await page.setViewportSize({ width: 1440, height: 900 });

  await page.goto(LAYOUTS);
  await expect(page.getByRole('link', { name })).toBeVisible();
  await look(page, info, 'the list with the layout');
});
