import assert from 'node:assert/strict';
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, test } from 'node:test';
import { bodyOf, headerOf, readMailFolder, visibleText, waitForMailIn } from './mail.ts';

// Reading the host's pickup folder: only .eml files, a wait that names the folder when nothing arrives, headers
// unfolded, bodies decoded from quoted-printable and base64, and the text a reader sees in the HTML.

const root = mkdtempSync(join(tmpdir(), 'jfb-mail-'));
after(() => rmSync(root, { recursive: true, force: true }));

// Code files stay ASCII (scripts/check-text.mjs), so the umlaut is built from its code point.
const u = String.fromCharCode(0xfc);

const multipart = [
  'From: e2e-site@jfb.test',
  'To: mila@visitor.jfb.test',
  'Subject: Your booking at',
  '  Lindenhall Library',
  'Content-Type: multipart/alternative; boundary="b1"',
  '',
  '--b1',
  'Content-Type: text/plain; charset=utf-8',
  'Content-Transfer-Encoding: base64',
  '',
  Buffer.from(`Gr${u}ezi Mila`, 'utf8').toString('base64'),
  '--b1',
  'Content-Type: text/html; charset=utf-8',
  'Content-Transfer-Encoding: quoted-printable',
  '',
  '<p>Gr=C3=BCezi &amp; <b>welcome</b>, a long line that goes on and on =',
  'and on</p>',
  '--b1--',
].join('\r\n');

test('a header is unfolded, and a missing one is empty', () => {
  assert.equal(headerOf(multipart, 'subject'), 'Your booking at Lindenhall Library');
  assert.equal(headerOf(multipart, 'Reply-To'), '');
});

test('a header is decoded from its encoded words, base64 and quoted alike', () => {
  const dot = String.fromCharCode(0xb7);
  const b = Buffer.from(`Ada ${dot} Entry #3`, 'utf8').toString('base64');
  const raw = `Subject: =?utf-8?B?${b}?=\r\n =?utf-8?Q?_of_Job_application?=\r\n\r\nBody`;
  assert.equal(headerOf(raw, 'Subject'), `Ada ${dot} Entry #3 of Job application`);
});

test('each part is decoded from its transfer encoding', () => {
  assert.equal(bodyOf(multipart, 'text/plain').trim(), `Gr${u}ezi Mila`);
  assert.match(bodyOf(multipart, 'text/html'), /&amp; <b>welcome<\/b>, a long line that goes on and on and on/);
  assert.equal(visibleText(bodyOf(multipart)), `Gr${u}ezi & welcome , a long line that goes on and on and on`);
});

test('a mail with one part is its own body', () => {
  const single = 'To: a@b.test\r\nContent-Type: text/html\r\n\r\n<p>Hi&nbsp;<br>there</p>';
  assert.equal(visibleText(bodyOf(single)), 'Hi&nbsp;\nthere');
});

test('the folder gives its .eml files in name order, and nothing when it is missing', () => {
  const dir = join(root, 'pickup');
  mkdirSync(dir);
  writeFileSync(join(dir, 'b.eml'), 'To: b@jfb.test\r\n\r\nB');
  writeFileSync(join(dir, 'a.eml'), 'To: a@jfb.test\r\n\r\nA');
  writeFileSync(join(dir, 'notes.txt'), 'not a mail');

  assert.deepEqual(
    readMailFolder(dir).map((mail) => mail.file),
    ['a.eml', 'b.eml'],
  );
  assert.deepEqual(readMailFolder(join(root, 'missing')), []);
});

test('the wait finds a mail that arrives while it waits, and names the folder when none does', async () => {
  const dir = join(root, 'arrivals');
  mkdirSync(dir);
  setTimeout(() => writeFileSync(join(dir, 'late.eml'), 'To: late@jfb.test\r\n\r\nLate'), 50);

  const mail = await waitForMailIn(dir, (candidate) => headerOf(candidate.raw, 'To') === 'late@jfb.test', 5_000, 10);

  assert.equal(mail.file, 'late.eml');
  await assert.rejects(
    waitForMailIn(dir, () => false, 50, 10),
    (error: Error) => error.message.includes(dir) && error.message.includes('50 ms'),
  );
});
