// An email action's live preview (ROADMAP 10.2b, ADR-0189): the mail's document in the scheme chosen, its rules for the
// other scheme never applying, its links no stop of Tab and opening nothing; and the preview as the sidebar shows it:
// who it comes from and goes to, its subject, the mail in a frame that runs nothing with its Light/Dark switch, a
// plain-text mail's text, a view that is missing, settings that don't fit, and a preview that fails with Try again. The
// preview's frame is one of the backoffice's two, which axe leaves alone, each with a check of its own (ADR-0189).
import { readdirSync, readFileSync } from 'node:fs';
import { dirname, join, sep } from 'node:path';
import { fileURLToPath } from 'node:url';
import { afterEach, beforeAll, describe, expect, it, vi } from 'vitest';
import type { EmailPreviewResponseModel } from '../../api';
import { must, registerEnglish, settle, shadow, text } from '../../testing';
import { loadActionWords } from './action-words';
import { mailDocument, previewAnswer, schemed, type EmailPreview } from './email-preview';
import type { EmailPreviewElement } from './email-preview.element';
import './email-preview.element';

const MAIL = `<!DOCTYPE html><html lang="en"><head><meta name="color-scheme" content="light dark"><base href="https://x.example.com/">
<style>body{margin:0}@media (prefers-color-scheme:dark){.jfb-page{background:#121417!important}}@media screen and (prefers-color-scheme: light){.x{color:red}}</style></head>
<body><table class="jfb-page"><tr><td><p>Alex Berger wrote.</p><a href="https://example.com/">Open</a><button>Go</button></td></tr></table></body></html>`;

const mail = (more: Partial<EmailPreviewResponseModel> = {}): EmailPreviewResponseModel => ({
  from: 'Example <forms@example.com>',
  to: ['sales@example.com', 'delivery@example.com'],
  visitor: null,
  cc: [],
  bcc: [],
  replyTo: null,
  subject: 'New from Alex Berger',
  html: MAIL,
  text: 'Alex Berger wrote.',
  direction: 'ltr',
  ...more,
});

async function previewOf(preview: EmailPreview | null, status: 'idle' | 'asking' | 'shown' | 'failed' = 'shown') {
  const element = document.createElement('jfb-email-preview') as EmailPreviewElement;
  element.state = { status, preview };
  element.culture = 'en-US';
  element.view = 'Receipt';
  element.retry = vi.fn();
  document.body.append(element);
  await settle(element);
  const root = shadow(element);
  const meta = () =>
    [...root.querySelectorAll('.meta dt')].map((term) => [text(term), text(term.nextElementSibling)] as const);
  return { element, root, meta };
}

beforeAll(async () => {
  registerEnglish();
  await loadActionWords('en');
});

afterEach(() => document.body.replaceChildren());

describe("an email action's preview", () => {
  it('reads the layout in the scheme chosen, as a device in that scheme does', () => {
    const css = '@media (prefers-color-scheme:dark){a{b:c}} @media screen and ( prefers-color-scheme : LIGHT ){d{e:f}}';
    expect(schemed(css, 'dark')).toBe(
      '@media (min-width: 0px){a{b:c}} @media screen and (max-width: 0px) and (min-width: 1px){d{e:f}}',
    );
    expect(schemed(css, 'light')).toBe(
      '@media (max-width: 0px) and (min-width: 1px){a{b:c}} @media screen and (min-width: 0px){d{e:f}}',
    );
  });

  it("makes the mail's document one whose links go nowhere and none of whose controls is a stop of Tab", () => {
    const shown = new DOMParser().parseFromString(mailDocument(MAIL, 'dark'), 'text/html');
    expect(shown.querySelector('style')?.textContent).toContain('@media (min-width: 0px){.jfb-page');
    expect(shown.querySelector('meta[name="color-scheme"]')?.getAttribute('content')).toBe('dark');
    expect(
      [...shown.querySelectorAll('base')].map((base) => [base.getAttribute('target'), base.getAttribute('href')]),
    ).toEqual([['_blank', null]]);
    expect([...shown.querySelectorAll('a, button')].map((one) => one.getAttribute('tabindex'))).toEqual(['-1', '-1']);
    expect(shown.querySelector('p')?.textContent).toBe('Alex Berger wrote.');
  });

  it('says who the mail comes from and goes to, its subject, and shows it in a frame that runs nothing', async () => {
    const { root, meta } = await previewOf(
      mail({
        visitor: 'alex.berger@example.com',
        cc: ['boss@example.com'],
        bcc: ['audit@example.com'],
        replyTo: 'alex.berger@example.com',
      }),
    );
    expect(text(root.querySelector('h4'))).toBe('Preview with a sample entry');
    expect(meta()).toEqual([
      ['From', 'Example <forms@example.com>'],
      ['To', 'sales@example.com, delivery@example.com'],
      ['Also to', 'alex.berger@example.com, in a mail of its own'],
      ['Copy to', 'boss@example.com'],
      ['Blind copy to', 'audit@example.com'],
      ['Reply to', 'alex.berger@example.com'],
      ['Subject', 'New from Alex Berger'],
    ]);
    const frame = must(root.querySelector('iframe'), 'the mail');
    expect([frame.getAttribute('sandbox'), frame.getAttribute('title'), frame.style.colorScheme]).toEqual([
      'allow-same-origin',
      'The email',
      'light',
    ]);
    expect(frame.srcdoc).toContain('@media (max-width: 0px) and (min-width: 1px){.jfb-page');
    expect(root.querySelector('.shown')?.getAttribute('aria-busy')).toBe('false');
    // The subject is read in its own language and direction.
    const subject = must(root.querySelector('.meta .subject'), 'the subject');
    expect([subject.getAttribute('lang'), subject.getAttribute('dir')]).toEqual(['en-US', 'ltr']);
  });

  it("takes the server's answer as the mail, or as why it shows none by the problem's code", () => {
    const shown = mail();
    expect(previewAnswer(shown, undefined)).toBe(shown);
    expect(previewAnswer(undefined, 'email_view_missing')).toEqual({ unshown: 'viewMissing' });
    expect(previewAnswer(undefined, 'action_settings_invalid')).toEqual({ unshown: 'settingsInvalid' });
    expect(previewAnswer(undefined, 'not_found')).toBeUndefined();
  });

  it("is one of the backoffice's four frames: axe leaves frames alone, so each has a check of its own (ADR-0189)", () => {
    const src = join(dirname(fileURLToPath(import.meta.url)), '..', '..');
    const framed = readdirSync(src, { recursive: true, encoding: 'utf8' })
      .filter((file) => !file.split(sep).some((part) => part.startsWith('.')))
      .filter((file) => file.endsWith('.ts') && !file.endsWith('.test.ts'))
      .filter((file) => readFileSync(join(src, file), 'utf8').includes('<iframe'))
      .map((file) => file.split(sep).join('/'))
      .sort();
    // The mail's in a page of its own (form-email.spec.ts), the visitor preview's in its frame (preview.spec.ts), the
    // theme's sample form in a page of its own (themes.spec.ts), and so is a layout's sample mail (email-layouts.spec.ts).
    expect(framed).toEqual([
      'designer/actions/email-preview.element.ts',
      'designer/visitor-preview/preview.modal.element.ts',
      'email-layouts/email-layout.view.ts',
      'themes/theme.view.ts',
    ]);
  });

  it('shows the mail in the scheme its switch chooses, one pressed at a time', async () => {
    const { element, root } = await previewOf(mail());
    const schemes = () =>
      [...root.querySelectorAll<HTMLButtonElement>('.schemes button')].map((one) => [
        text(one),
        one.getAttribute('aria-pressed'),
      ]);
    expect(root.querySelector('.schemes')?.getAttribute('aria-label')).toBe('Colour scheme');
    expect(schemes()).toEqual([
      ['Light', 'true'],
      ['Dark', 'false'],
    ]);
    must(root.querySelectorAll<HTMLButtonElement>('.schemes button')[1], 'Dark').click();
    await element.updateComplete;
    expect(schemes()).toEqual([
      ['Light', 'false'],
      ['Dark', 'true'],
    ]);
    const frame = must(root.querySelector('iframe'), 'the mail');
    expect([frame.style.colorScheme, frame.srcdoc.includes('@media (min-width: 0px){.jfb-page')]).toEqual([
      'dark',
      true,
    ]);
  });

  it("says a mail goes to no one yet, or only to a visitor's address, and shows a plain-text mail's text", async () => {
    const nobody = await previewOf(mail({ to: [] }));
    expect(nobody.meta()[1]).toEqual(['To', 'No recipient yet']);
    expect(text(nobody.root.querySelector('.help'))).toBe(
      "As the team gets it, with sample answers made from the form's fields.",
    );
    document.body.replaceChildren();
    const visitor = await previewOf(mail({ to: [], visitor: 'alex.berger@example.com', from: '' }));
    expect(visitor.meta().slice(0, 2)).toEqual([
      ['From', "None yet: the site's email settings name no sender"],
      ['To', 'alex.berger@example.com, in a mail of its own'],
    ]);
    // Only the visitor gets it, and the help says so.
    expect(text(visitor.root.querySelector('.help'))).toBe(
      "As the visitor gets it, with sample answers made from the form's fields.",
    );
    document.body.replaceChildren();
    const plain = await previewOf(mail({ html: null, text: 'Alex Berger wrote.\nThanks.', direction: 'rtl' }));
    expect(plain.root.querySelector('iframe')).toBeNull();
    expect(plain.root.querySelector('.schemes')).toBeNull();
    const shown = must(plain.root.querySelector('.text'), 'the text');
    expect([shown.textContent, shown.getAttribute('dir'), shown.getAttribute('lang')]).toEqual([
      'Alex Berger wrote.\nThanks.',
      'rtl',
      'en-US',
    ]);
  });

  it("says a view the site lacks, settings that don't fit, and a preview that fails as it comes, with Try again", async () => {
    const missing = await previewOf({ unshown: 'viewMissing' });
    expect(text(missing.root.querySelector('.said'))).toBe(
      'This site has no Razor view "Receipt", and this email has no body of its own. Write a body, or name a view the site has.',
    );
    document.body.replaceChildren();
    const unfit = await previewOf({ unshown: 'settingsInvalid' });
    expect(text(unfit.root.querySelector('.said'))).toBe(
      "This email's settings have errors. The preview shows once they're fixed.",
    );
    document.body.replaceChildren();
    const failed = await previewOf(null, 'failed');
    const alert = must(failed.root.querySelector('[role="alert"]'), 'the failure');
    expect(text(alert)).toBe("The preview couldn't be shown.");
    must(failed.root.querySelector<HTMLElement>('uui-button'), 'Try again').click();
    // UUI's button presses its own button once it is drawn.
    await settle(failed.element);
    expect(failed.element.retry).toHaveBeenCalledTimes(1);
    document.body.replaceChildren();
    const waiting = await previewOf(null, 'asking');
    expect(waiting.root.querySelector('.shown')?.getAttribute('aria-busy')).toBe('true');
  });
});
