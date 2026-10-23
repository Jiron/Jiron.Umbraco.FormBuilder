// A page with one form in the markup contract (ADR-0015), as a server renders it without JavaScript: every kind of
// control fillForm answers, a field a rule hides, the error summary and the outcome. The smoke spec serves it with
// page.route to check the form helpers before Jiron Form Builder renders forms of its own (6.2); from then on the
// helpers run against real forms.

/** The fields of the contract page and their answers' names. */
export const CONTRACT_FORM = {
  key: '0f6c2b1e-5d4a-4c1b-9e8f-3a2b1c0d9e8f',
  instance: 'jfb-1',
  action: '/contract-form',
  consentHash: 'hash-of-the-consent-wording',
} as const;

const escape = (text: string): string =>
  text.replaceAll('&', '&amp;').replaceAll('<', '&lt;').replaceAll('>', '&gt;').replaceAll('"', '&quot;');

const id = (alias: string): string => `${CONTRACT_FORM.instance}-${alias}`;

function described(alias: string, error: string | undefined): string {
  return error ? ` aria-describedby="${id(alias)}-error" aria-invalid="true"` : '';
}

function errorOf(alias: string, error: string | undefined): string {
  return error ? `<p class="jfb-error" id="${id(alias)}-error" data-jfb-error>${escape(error)}</p>` : '';
}

function field(alias: string, type: string, body: string): string {
  return `<div class="jfb-field jfb-field-${type} jfb-span-12" data-jfb-field="field-${alias}" data-jfb-alias="${alias}" data-jfb-type="${type}">${body}</div>`;
}

function choices(alias: string, type: 'radio' | 'checkbox', legend: string, options: [string, string][]): string {
  const items = options
    .map(
      ([value, label], index) =>
        `<div class="jfb-choice"><input type="${type}" id="${id(alias)}-${index}" name="${alias}" value="${value}"><label for="${id(alias)}-${index}">${label}</label></div>`,
    )
    .join('');
  return field(
    alias,
    type === 'radio' ? 'choice' : 'choices',
    `<fieldset class="jfb-choices"><legend>${legend}</legend>${items}</fieldset>`,
  );
}

/** The page with the form; with errors, as the server renders it again after a post that failed its checks. */
export function contractFormPage(errors: Record<string, string> = {}): string {
  const summary = Object.keys(errors).length
    ? `<div class="jfb-error-summary" data-jfb-summary role="alert" tabindex="-1"><h2>Please check these answers</h2><ul>${Object.entries(
        errors,
      )
        .map(([alias, message]) => `<li><a href="#${id(alias)}">${escape(message)}</a></li>`)
        .join('')}</ul></div>`
    : '';
  const fields = [
    field(
      'name',
      'text',
      `<label class="jfb-label" for="${id('name')}">Your name</label><input class="jfb-control" id="${id('name')}" name="name" type="text" required${described('name', errors.name)}>${errorOf('name', errors.name)}`,
    ),
    field(
      'email',
      'email',
      `<label class="jfb-label" for="${id('email')}">Email</label><input class="jfb-control" id="${id('email')}" name="email" type="email">`,
    ),
    field(
      'guests',
      'number',
      `<label class="jfb-label" for="${id('guests')}">Guests</label><input class="jfb-control" id="${id('guests')}" name="guests" type="number">`,
    ),
    field(
      'topic',
      'select',
      `<label class="jfb-label" for="${id('topic')}">Topic</label><select class="jfb-control" id="${id('topic')}" name="topic"><option value="">Choose</option><option value="books">Books</option><option value="events">Events</option></select>`,
    ),
    choices('visit', 'radio', 'Have you been here before?', [
      ['first', 'This is my first visit'],
      ['again', 'I have been here before'],
    ]),
    choices('interests', 'checkbox', 'What interests you?', [
      ['reading', 'Reading'],
      ['repair', 'Repair cafe'],
      ['games', 'Games'],
    ]),
    field(
      'message',
      'textarea',
      `<label class="jfb-label" for="${id('message')}">Message</label><textarea class="jfb-control" id="${id('message')}" name="message"></textarea>`,
    ),
    field(
      'company',
      'text',
      `<label class="jfb-label" for="${id('company')}">Company</label><input class="jfb-control" id="${id('company')}" name="company" type="text" disabled>`,
    ).replace('<div class="jfb-field', '<div hidden data-jfb-hidden class="jfb-field'),
    field(
      'consent',
      'consent',
      `<input type="checkbox" id="${id('consent')}" name="consent" value="true"><label for="${id('consent')}">I agree that the library stores my answers.</label><input type="hidden" name="jfb-consent-consent" value="${CONTRACT_FORM.consentHash}">`,
    ),
  ].join('');

  return `<!doctype html><html lang="en"><head><meta charset="utf-8"><title>Contract form</title></head><body><main>
<form class="jfb-form jfb-theme-unstyled" id="${CONTRACT_FORM.instance}" data-jfb-form="${CONTRACT_FORM.key}" data-jfb-revision="1" data-jfb-contract="1" lang="en" dir="ltr" method="post" action="${CONTRACT_FORM.action}" novalidate>
${summary}<section class="jfb-page" data-jfb-page="page-1"><div class="jfb-group" data-jfb-group="group-1">${fields}</div></section>
<div class="jfb-hp" aria-hidden="true"><label for="${id('hp')}">Leave this empty</label><input id="${id('hp')}" name="jfb-hp" tabindex="-1" autocomplete="off"></div>
<div data-jfb-live aria-live="polite"></div>
<div class="jfb-navigation"><button class="jfb-button jfb-button-primary" type="submit" name="jfb-nav" value="submit" data-jfb-nav="submit">Send</button></div>
</form></main></body></html>`;
}

/** The page after a successful post. */
export function contractOutcomePage(): string {
  return `<!doctype html><html lang="en"><head><meta charset="utf-8"><title>Sent</title></head><body><main>
<div class="jfb-outcome" data-jfb-outcome tabindex="-1"><h2>Thank you</h2><p>We have your answers.</p></div></main></body></html>`;
}
