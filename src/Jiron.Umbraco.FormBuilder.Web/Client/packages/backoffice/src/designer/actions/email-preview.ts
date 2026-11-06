// An email action's live preview (ROADMAP 10.2b, ADR-0189): the server renders the mail of the draft as the sidebar
// would make it, for the sample entry, as the team gets it, asked as the draft's previewer asks. The mail's HTML shows
// in a frame that runs nothing, in the colour scheme chosen: the layout's rules for that scheme apply whatever the
// device's own, as a mail client in that scheme shows them; its links go nowhere, and none is a stop of Tab.
import type { EmailPreviewResponseModel } from '../../api';
import { DraftPreviewer } from '../preview/draft-previewer';

/** Why the server shows no mail: the view is missing with no body, or the settings don't fit the type. */
export type Unshown = 'viewMissing' | 'settingsInvalid';

/** What the server answers for an email action: its mail, or why it shows none. */
export type EmailPreview = EmailPreviewResponseModel | { readonly unshown: Unshown };

const UNSHOWN: Readonly<Record<string, Unshown>> = {
  email_view_missing: 'viewMissing',
  action_settings_invalid: 'settingsInvalid',
};

/** The server's answer as the preview takes it: the mail, or why it shows none by the problem's code. */
export function previewAnswer(
  data: EmailPreviewResponseModel | undefined,
  code: string | undefined,
): EmailPreview | undefined {
  const unshown = code === undefined ? undefined : UNSHOWN[code];
  return unshown ? { unshown } : data;
}

/** The email action's previewer: the draft's, for a mail. */
export class EmailPreviewer extends DraftPreviewer<EmailPreview> {}

/** The colour scheme the mail is shown in. */
export type Scheme = 'light' | 'dark';

// A query that every device matches, and one that none does, in the syntax every engine reads.
const ALWAYS = '(min-width: 0px)';
const NEVER = '(max-width: 0px) and (min-width: 1px)';

/** A style sheet's colour-scheme queries as a device in one scheme reads them. */
export function schemed(css: string, scheme: Scheme): string {
  return css.replace(/\(\s*prefers-color-scheme\s*:\s*(light|dark)\s*\)/gi, (_query, which: string) =>
    which.toLowerCase() === scheme ? ALWAYS : NEVER,
  );
}

/**
 * The mail's document as the frame shows it in a colour scheme: its style sheets read in that scheme, the scheme said
 * to the frame's own controls, its links and buttons no stop of Tab, and a link's window one the frame may not open.
 */
export function mailDocument(html: string, scheme: Scheme): string {
  const parsed = new DOMParser().parseFromString(html, 'text/html');
  for (const style of parsed.querySelectorAll('style')) style.textContent = schemed(style.textContent ?? '', scheme);
  for (const meta of parsed.querySelectorAll('meta[name="color-scheme"], meta[name="supported-color-schemes"]'))
    meta.setAttribute('content', scheme);
  for (const stop of parsed.querySelectorAll('a[href], area[href], button, input, select, textarea, [tabindex]'))
    stop.setAttribute('tabindex', '-1');
  for (const base of parsed.querySelectorAll('base')) base.remove();
  const base = parsed.createElement('base');
  base.setAttribute('target', '_blank');
  parsed.head.prepend(base);
  return `<!DOCTYPE html>${parsed.documentElement.outerHTML}`;
}
