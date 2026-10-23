import { existsSync, readdirSync, readFileSync } from 'node:fs';
import { join } from 'node:path';
import { setTimeout as delay } from 'node:timers/promises';

// Reading the .eml files Umbraco writes to the host's pickup folder: the mails there, a wait for one that matches, a
// header's value, and the text of the HTML or plain part decoded from quoted-printable or base64, so a spec can check
// what a reader sees.

/** An e-mail Umbraco wrote to the host's pickup folder. */
export interface Mail {
  file: string;
  /** The raw message: headers and body. */
  raw: string;
}

/** Every .eml file in the folder. A file Umbraco is still writing, or one that went away, is read on the next call. */
export function readMailFolder(dir: string): Mail[] {
  if (!existsSync(dir)) return [];
  return readdirSync(dir)
    .filter((file) => file.endsWith('.eml'))
    .sort()
    .flatMap((file) => {
      try {
        return [{ file, raw: readFileSync(join(dir, file), 'utf8') }];
      } catch (error) {
        // Windows refuses to share a file that is still being written (EBUSY, EPERM).
        if (['EBUSY', 'EPERM', 'ENOENT'].includes((error as NodeJS.ErrnoException).code ?? '')) return [];
        throw error;
      }
    });
}

/** Waits for a mail in the folder that matches; fails with the folder and the time it waited. */
export async function waitForMailIn(
  dir: string,
  match: (mail: Mail) => boolean,
  timeoutMs = 15_000,
  pollMs = 250,
): Promise<Mail> {
  const deadline = Date.now() + timeoutMs;
  for (;;) {
    const found = readMailFolder(dir).find(match);
    if (found) return found;
    if (Date.now() >= deadline) throw new Error(`No matching e-mail arrived in ${dir} within ${timeoutMs} ms.`);
    await delay(pollMs);
  }
}

/** A header's value (the first one), unfolded; empty when the mail has none. */
export function headerOf(raw: string, name: string): string {
  const head = raw.split(/\r?\n\r?\n/)[0] ?? '';
  const unfolded = head.replace(/\r?\n[ \t]+/g, ' ');
  const line = unfolded.split(/\r?\n/).find((each) => each.toLowerCase().startsWith(`${name.toLowerCase()}:`));
  return line ? line.slice(name.length + 1).trim() : '';
}

function decodeQuotedPrintable(text: string): string {
  const bytes: number[] = [];
  const soft = text.replace(/=\r?\n/g, '');
  for (let i = 0; i < soft.length; i++) {
    const char = soft[i] ?? '';
    const hex = soft.slice(i + 1, i + 3);
    if (char === '=' && /^[0-9A-F]{2}$/i.test(hex)) {
      bytes.push(parseInt(hex, 16));
      i += 2;
    } else {
      bytes.push(...Buffer.from(char, 'utf8'));
    }
  }
  return Buffer.from(bytes).toString('utf8');
}

/** The decoded body of the mail's part of the given type (text/html or text/plain), or of the mail itself. */
export function bodyOf(raw: string, type: 'text/html' | 'text/plain' = 'text/html'): string {
  const parts = raw.split(/\r?\n--[^\r\n]+\r?\n/);
  const part =
    parts.find((each) => new RegExp(`^Content-Type:\\s*${type.replace('/', '\\/')}`, 'im').test(each)) ?? raw;
  // The last part ends with the closing boundary.
  const [head = '', ...rest] = part.replace(/\r?\n--[^\r\n]+--\s*$/, '').split(/\r?\n\r?\n/);
  const body = rest.join('\n\n');
  const encoding = /^Content-Transfer-Encoding:\s*(\S+)/im.exec(head)?.[1]?.toLowerCase();
  if (encoding === 'base64') return Buffer.from(body.replace(/\s+/g, ''), 'base64').toString('utf8');
  if (encoding === 'quoted-printable') return decodeQuotedPrintable(body);
  return body;
}

/** The text a reader sees in an HTML body: no tags, entities decoded, white space collapsed. */
export function visibleText(html: string): string {
  return html
    .replace(/<style[\s\S]*?<\/style>/gi, ' ')
    .replace(/<br\s*\/?>/gi, '\n')
    .replace(/<[^>]+>/g, ' ')
    .replace(/&lt;/g, '<')
    .replace(/&gt;/g, '>')
    .replace(/&quot;/g, '"')
    .replace(/&#x27;|&#39;/g, "'")
    .replace(/&amp;/g, '&')
    .replace(/[ \t]+/g, ' ')
    .replace(/\s*\n\s*/g, '\n')
    .trim();
}
