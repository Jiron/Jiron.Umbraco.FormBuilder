import { expect, test } from '../../fixtures.ts';
import { formPages, pathOf } from '../../support/demo-content.ts';
import { fillForm, submitForm } from '../../support/form.ts';
import { bodyOf, headerOf, visibleText, type Mail } from '../../support/mail.ts';

// The demo's contact form tells the team and answers the sender (ROADMAP 8.3, ADR-0160): two mails in the pickup folder, in the
// page's language, with what a visitor typed escaped; the answer to a stranger's address goes out only within the limits, and
// a link typed into it is no link.

const breaker = String.fromCharCode(0x200b);
const contact = (culture: 'en' | 'de'): string => {
  const shown = formPages().find((each) => each.form === 'contact');
  if (shown === undefined) throw new Error('The demo shows no contact form.');
  return pathOf(culture === 'de' ? shown.texts.de : shown.texts.en, culture);
};
const to = (mail: Mail): string => headerOf(mail.raw, 'To');

test.describe('mail from the contact form', () => {
  for (const [culture, teamSubject, senderSubject, greeting] of [
    ['en', 'New message from', 'We have your message', 'Hello'],
    ['de', 'Neue Nachricht von', 'Wir haben Ihre Nachricht erhalten', 'Guten Tag'],
  ] as const) {
    test(`a message in ${culture} reaches the team and the sender, in their language`, async ({
      page,
      waitForMail,
    }) => {
      const sender = `mail-${culture}-${Date.now()}@example.test`;
      await page.goto(contact(culture));
      const form = page.locator('main form.jfb-form');
      await fillForm(form, {
        name: 'Ada <b>Lovelace</b>',
        email: sender,
        message: 'Is it open & free? <script>alert(1)</script>',
      });
      expect((await submitForm(form)).state).toBe('sent');

      const answered = await waitForMail((mail) => to(mail).includes(sender), 60_000);
      const team = await waitForMail(
        (mail) => to(mail).includes('team@lindenhall.example') && bodyOf(mail.raw, 'text/html').includes(sender),
        60_000,
      );

      const answer = bodyOf(answered.raw, 'text/html');
      const told = bodyOf(team.raw, 'text/html');
      expect(headerOf(answered.raw, 'Subject')).toBe(senderSubject);
      expect(headerOf(team.raw, 'Subject')).toContain(teamSubject);
      expect(headerOf(team.raw, 'Reply-To')).toContain(sender);
      expect(answer).toContain(`lang="${culture === 'de' ? 'de' : 'en'}`);
      expect(visibleText(answer)).toContain(greeting);
      for (const body of [answer, told]) {
        expect(body).toContain('&lt;script&gt;');
        expect(body).not.toContain('<script');
        expect(body).not.toContain('<b>Lovelace');
      }
    });
  }

  test('a stranger gets the answer only within the limits, and a link in it is no link', async ({
    page,
    waitForMail,
    request,
  }) => {
    const stranger = `stranger-${Date.now()}@example.test`;
    const send = async (spelling: string): Promise<void> => {
      await page.goto(contact('en'));
      const form = page.locator('main form.jfb-form');
      await fillForm(form, {
        name: 'Mallory',
        email: spelling,
        message: `Buy now at https://evil.example/${spelling} !`,
      });
      expect((await submitForm(form)).state).toBe('sent');
    };

    await send(stranger);
    const first = await waitForMail((mail) => to(mail).toLowerCase().includes(stranger), 60_000);
    const html = bodyOf(first.raw, 'text/html');
    expect(html).not.toContain('https://evil.example');
    expect(html).not.toContain('href="https://evil');
    expect(html).toContain(`https:${breaker}//evil.${breaker}example`);

    await page.setContent(html);
    await expect(page.locator('a[href*="evil"]')).toHaveCount(0);
    expect(await page.locator('a').count()).toBe(0);

    // Two more spellings fill the mailbox's three a day; the fourth is held back and shows as limited.
    const [local, domain] = stranger.split('@') as [string, string];
    await send(stranger.toUpperCase());
    await send(`${local}+a@${domain}`);
    await send(`${local}+b@${domain}`);
    await expect
      .poll(
        async () => {
          const answer = await request.get(`/jfb-testing/mail-log?key=${encodeURIComponent(stranger)}`);
          return ((await answer.json()) as { states: string[] }).states.sort();
        },
        { timeout: 60_000 },
      )
      .toEqual(['limited', 'sent', 'sent', 'sent']);
  });

  test("a site's view writes a mail's body on no request of its own", async ({ request }) => {
    const answer = await request.get('/jfb-testing/mail-view?view=Receipt');
    expect(answer.ok(), await answer.text()).toBe(true);
    const { written } = (await answer.json()) as { written: string };
    expect(written).toContain('Receipt 7 for &lt;Contact&gt;');
    expect(written).toContain('Name: &lt;Ada&gt;');

    const missing = await request.get('/jfb-testing/mail-view?view=NoSuchView');
    expect(((await missing.json()) as { written: string | null }).written).toBeNull();
  });
});
