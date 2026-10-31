import type { CDPSession, Locator, Page } from '@playwright/test';
import { expect, test } from '../../fixtures.ts';
import { pageOf, pathOf } from '../../support/demo-content.ts';

// The screen-reader-name pass over the themes (ROADMAP 6.8e), on the demo's specimen in every theme and both schemes,
// read from Edge's own accessibility tree: the role, name and description of every kind of control and group as a form
// first renders, what `required` says, and `autocomplete` on the personal fields; then after a refused send, with
// JavaScript and without, each refused control invalid and described by its error, and no group invalid itself.
//
// `required` is read from the control: DevTools' tree lists it for text boxes only, while Windows UI Automation shows
// Edge passes it on for every control that carries it (docs/ux/review-6.8c.md). Edge also reports a required radio
// button, box or select that is empty as invalid from the first render, by native validation, so the first render is
// held to what the markup says: no `aria-invalid` anywhere. After a refusal that same report would hold for the five
// controls it covers whatever the markup wrote, so the markup's own mark is read beside the tree's.
//
// An address's parts, the "Other" input and a signature's typed name are optional on the specimen, so here they show
// role and name only; `FormRenderingChoicesTests` and `FormRenderingInputsTests` hold their required and invalid marks
// on forms that refuse them.

const SPECIMEN = pathOf(pageOf('demoSpecimen', 'en'), 'en');
const THEMES = ['default', 'unstyled', 'bootstrap5'] as const;
const SCHEMES = ['', 'dark'] as const;

/** What a screen reader gets for an element. */
interface Spoken {
  role: string;
  name: string;
  description: string;
  invalid: boolean;
}

type Expected = readonly [selector: string, role: string, name: string, description?: string];

const fieldset = (alias: string): string => `[data-jfb-alias="${alias}"] > fieldset`;

/** Every kind of control and group as a form first renders. */
const FIRST_RENDER: readonly Expected[] = [
  ['[name="displayName"]', 'textbox', 'Name on your library card', 'As it should be printed. Up to 40 characters'],
  ['[name="email"]', 'textbox', 'Email address'],
  ['[name="email.confirm"]', 'textbox', 'Repeat your email address'],
  ['[name="cardNumber"]', 'textbox', 'Library card number (optional)', 'The six digits on the back of your card. LIB'],
  ['[name="copies"]', 'spinbutton', 'How many copies? (optional)', 'copies'],
  ['[name="phone"]', 'textbox', 'Phone number (optional)'],
  ['[name="website"]', 'textbox', 'Your website (optional)'],
  ['[name="message"]', 'textbox', 'Your message (optional)', 'Up to 500 characters'],
  ['[name="visitDay"]', 'Date', 'Day of your visit (optional)'],
  ['[name="visitTime"]', 'InputTime', 'Time (optional)'],
  ['[name="returnAt"]', 'DateTime', 'Return by (optional)'],
  ['[name="branch"]', 'combobox', 'Branch'],
  [
    '[name="topics"]',
    'listbox',
    'Topics you read about (optional)',
    'To choose more than one, hold Ctrl (Cmd on a Mac) while you click.',
  ],
  [fieldset('format'), 'group', 'Which format? (optional)'],
  ['[name="format"]', 'radio', 'In print'],
  [fieldset('room'), 'group', 'Where would you like to sit?'],
  ['[name="room"]', 'radio', 'The reading room', 'Quiet, with 40 seats.'],
  [fieldset('interests'), 'group', 'What else interests you? (optional)'],
  ['[name="interests"]', 'checkbox', 'Author readings', 'Usually on Thursday evenings.'],
  ['[name="interests.other"]', 'textbox', 'Your own answer'],
  ['[name="reminder"]', 'checkbox', 'Send me a reminder', 'We write once, the day before.'],
  [fieldset('firstVisit'), 'group', 'Is this your first visit? (optional)'],
  ['[name="firstVisit"]', 'radio', 'Yes'],
  ['[name="newsletter"]', 'switch', 'Get the monthly newsletter'],
  ['[name="country"]', 'combobox', 'Country (optional)'],
  ['[name="privacy"]', 'checkbox', 'I agree that the library keeps my answers for six months.'],
  [fieldset('rating'), 'group', 'How was your last visit?'],
  ['[name="rating"]', 'radio', '1, Poor'],
  [fieldset('recommend'), 'group', 'Would you recommend us?'],
  ['[name="recommend"]', 'radio', '0, Not likely'],
  ['[name="hours"]', 'slider', 'Hours a week you read'],
  [fieldset('opinions'), 'group', 'How happy are you with these?'],
  [`${fieldset('opinions')} fieldset`, 'group', 'The opening hours'],
  ['[name^="opinions."]', 'radio', 'Not happy'],
  [fieldset('priorities'), 'group', 'What matters most to you? (optional)', 'Put the most important first.'],
  [`${fieldset('priorities')} button:nth-of-type(2)`, 'button', 'Move Opening hours down'],
  [fieldset('contact'), 'group', 'Your name'],
  ['[name="contact.first"]', 'textbox', 'First name'],
  ['[name="contact.last"]', 'textbox', 'Last name'],
  [fieldset('address'), 'group', 'Address (optional)'],
  ['[name="address.street"]', 'textbox', 'Street and number'],
  ['[name="address.street2"]', 'textbox', 'Address line 2'],
  ['[name="address.postcode"]', 'textbox', 'Postcode'],
  ['[name="address.city"]', 'textbox', 'Town'],
  ['[name="address.country"]', 'combobox', 'Country'],
  ['[name="attachments"]', 'button', 'Attachments (optional)', 'PDFs or pictures, up to three.'],
  [fieldset('signature'), 'group', 'Your signature (optional)'],
  ['[name="signature.typed"]', 'textbox', 'Your full name', 'Type your full name. It counts as your signature.'],
  ['[data-jfb-alias="deposit"] output', 'status', 'Deposit', 'Two euros a copy.'],
  [fieldset('guests'), 'group', 'Who comes with you (optional)', 'Up to three guests.'],
  [`${fieldset('guests')} button`, 'button', 'Add a guest'],
];

/** The refused controls after a send of the first page with only a message, and the error each is described by. */
const REFUSED: readonly Expected[] = [
  [
    '[name="displayName"]',
    'textbox',
    'Name on your library card',
    'As it should be printed. Up to 40 characters Enter an answer',
  ],
  ['[name="email"]', 'textbox', 'Email address', 'Enter an email address'],
  ['[name="email.confirm"]', 'textbox', 'Repeat your email address', 'Enter an email address'],
  ['[name="branch"]', 'combobox', 'Branch', 'Choose an answer'],
  ['[name="room"]', 'radio', 'The reading room', 'Quiet, with 40 seats. Choose an answer'],
  [
    '[name="privacy"]',
    'checkbox',
    'I agree that the library keeps my answers for six months.',
    'Tick the box to agree',
  ],
  ['[name="rating"]', 'radio', '1, Poor', 'Choose a rating'],
  ['[name="recommend"]', 'radio', '0, Not likely', 'Choose a point on the scale'],
  ['[name^="opinions."]', 'radio', 'Not happy', 'Choose an answer in each row'],
  ['[name="contact.first"]', 'textbox', 'First name'],
  ['[name="contact.last"]', 'textbox', 'Last name'],
];

/** The personal fields' `autocomplete`, which fills them in for a visitor who cannot easily type (WCAG 1.3.5). */
const AUTOCOMPLETE: readonly (readonly [string, string])[] = [
  ['displayName', 'name'],
  ['email', 'email'],
  ['email.confirm', 'email'],
  ['phone', 'tel'],
  ['website', 'url'],
  ['country', 'country'],
  ['contact.first', 'given-name'],
  ['contact.last', 'family-name'],
  ['address.street', 'address-line1'],
  ['address.street2', 'address-line2'],
  ['address.postcode', 'postal-code'],
  ['address.city', 'address-level2'],
  ['address.country', 'country'],
  ['signature.typed', 'name'],
];

const REQUIRED = [
  'displayName',
  'email',
  'email.confirm',
  'branch',
  'room',
  'privacy',
  'rating',
  'recommend',
  'contact.first',
  'contact.last',
];

const formOn = (page: Page): Locator => page.locator('main form.jfb-form');

/** Reads an element's role, name, description and invalid state from the browser's accessibility tree. */
async function spokenOf(page: Page, cdp: CDPSession, selector: string): Promise<Spoken> {
  await cdp.send('Accessibility.enable');
  const { root } = await cdp.send('DOM.getDocument', { depth: 0 });
  const { nodeId } = await cdp.send('DOM.querySelector', {
    nodeId: root.nodeId,
    selector: `main form.jfb-form ${selector}`,
  });
  if (nodeId === 0) throw new Error(`${page.url()}: nothing at ${selector}`);
  const { node } = await cdp.send('DOM.describeNode', { nodeId });
  const { nodes } = await cdp.send('Accessibility.getPartialAXTree', {
    backendNodeId: node.backendNodeId,
    fetchRelatives: false,
  });
  const found = nodes[0];
  const invalid = found?.properties?.find((property) => property.name === 'invalid')?.value.value as string | undefined;
  return {
    role: String(found?.role?.value ?? ''),
    name: String(found?.name?.value ?? '').trim(),
    description: String(found?.description?.value ?? '').trim(),
    invalid: invalid !== undefined && invalid !== 'false',
  };
}

for (const theme of THEMES) {
  for (const scheme of SCHEMES) {
    const variant = `${theme}${scheme === '' ? '' : ` ${scheme}`}`;

    test(`${variant}: every control and group has its role, name and description, as a form first renders`, async ({
      page,
      context,
    }) => {
      await page.goto(`${SPECIMEN}?theme=${theme}&scheme=${scheme}`);
      const cdp = await context.newCDPSession(page);
      for (const [selector, role, name, description] of FIRST_RENDER) {
        const spoken = await spokenOf(page, cdp, selector);
        expect([spoken.role, spoken.name, spoken.description], selector).toEqual([role, name, description ?? '']);
      }
      await expect(formOn(page).locator('[aria-invalid]'), 'nothing is marked invalid yet').toHaveCount(0);

      const required = await formOn(page)
        .locator('input[required], select[required], textarea[required]')
        .evaluateAll((controls) => [...new Set(controls.map((control) => (control as HTMLInputElement).name))]);
      expect(
        required.filter((name) => !name.startsWith('guests') && !name.startsWith('opinions.')).sort(),
        'exactly the required controls say so',
      ).toEqual([...REQUIRED].sort());

      for (const [name, token] of AUTOCOMPLETE) {
        await expect(formOn(page).locator(`[name="${name}"]`).first(), `${name}'s autocomplete`).toHaveAttribute(
          'autocomplete',
          token,
        );
      }
    });

    for (const javaScriptEnabled of [true, false]) {
      test.describe(`${variant} ${javaScriptEnabled ? 'with' : 'without'} JavaScript`, () => {
        // Without Bootstrap's smooth scrolling, as the state after the post counts here, not the way to its button.
        test.use({ javaScriptEnabled, contextOptions: { reducedMotion: 'reduce' } });

        test('each refused control is invalid and described by its error, and no group is invalid itself', async ({
          page,
          context,
        }) => {
          await page.goto(`${SPECIMEN}?theme=${theme}&scheme=${scheme}`);
          const form = formOn(page);
          await form.locator('[name="message"]').fill('A question.');
          await form.locator('.jfb-navigation .jfb-button-primary').click();
          await expect(form.locator('.jfb-error-summary')).toBeVisible();

          const cdp = await context.newCDPSession(page);
          for (const [selector, role, name, description] of REFUSED) {
            const spoken = await spokenOf(page, cdp, selector);
            expect([spoken.role, spoken.name, spoken.description, spoken.invalid], selector).toEqual([
              role,
              name,
              description ?? '',
              true,
            ]);
            await expect(form.locator(selector).first(), `${selector}: the markup's own mark`).toHaveAttribute(
              'aria-invalid',
              'true',
            );
          }
          const name = await spokenOf(page, cdp, fieldset('contact'));
          expect([name.description, name.invalid], "a name's group names its error, and is not invalid").toEqual([
            'Enter the name in full',
            false,
          ]);
          await expect(form.locator('fieldset[aria-invalid]'), 'no fieldset is invalid').toHaveCount(0);

          const links = await form.locator('.jfb-error-summary a').allTextContents();
          expect(links.map((link) => link.trim())).toEqual([
            'Name on your library card: Enter an answer',
            'Email address: Enter an email address',
            'Branch: Choose an answer',
            'Where would you like to sit? Choose an answer',
            'Privacy: Tick the box to agree',
            'How was your last visit? Choose a rating',
            'Would you recommend us? Choose a point on the scale',
            'How happy are you with these? Choose an answer in each row',
            'Your name: Enter the name in full',
          ]);
        });
      });
    }
  }
}
