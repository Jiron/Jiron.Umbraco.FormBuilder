import { expect, test } from '../../fixtures.ts';
import { headerOf } from '../../support/mail.ts';

// The host writes every e-mail to its own pickup folder, so tests read what a visitor or an editor would receive.
// Umbraco's password reset is the one e-mail a fresh install can send.
test('a password reset e-mail lands in the host mail pickup folder', async ({
  requestPasswordReset,
  accountOf,
  waitForMail,
}) => {
  const designer = accountOf('designer');

  await requestPasswordReset('designer');

  const mail = await waitForMail((candidate) => headerOf(candidate.raw, 'To').includes(designer.email));
  expect(headerOf(mail.raw, 'From')).toContain('e2e-site@jfb.test');
  expect(headerOf(mail.raw, 'Subject')).toMatch(/reset password/i);
  expect(mail.raw).toContain(designer.name);
});
