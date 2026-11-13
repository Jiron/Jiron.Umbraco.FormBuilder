// The screenshot script's helpers (ROADMAP 17.3): a submission id that is the same on every run, the proof of work as
// the server checks it, and the forms read from a page's markup with their consents' wording hashes.
// Run: node --test "scripts/tests/*.test.mjs"
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { test } from 'node:test';
import { formsIn, solveProof, submissionIdOf } from '../screenshots.mjs';

test('a name gives the same submission id on every run, as a GUID, and another name another', () => {
  const id = submissionIdOf('contact-0');
  assert.equal(submissionIdOf('contact-0'), id);
  assert.notEqual(submissionIdOf('contact-1'), id);
  assert.match(id, /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-8[0-9a-f]{3}-[0-9a-f]{12}$/);
});

test("the proof of work's answer gives a hash that starts with as many zero bits as asked", () => {
  const nonce = '3f1c9a7e2b4d4c8e9f0a1b2c3d4e5f60';
  for (const bits of [1, 8, 12]) {
    const answer = solveProof(nonce, bits);
    assert.match(answer, /^\d+$/);
    const hash = createHash('sha256').update(`${nonce}:${answer}`).digest();
    assert.equal(hash.readUInt32BE(0) >>> (32 - bits), 0, `${bits} bits`);
  }
});

test("a page's forms are read with their key, revision, page and consents, a form's end bounding its consents", () => {
  const html = [
    '<main><form class="jfb-form" id="jfb-1" data-jfb-form="aaaa-1" data-jfb-revision="3" data-jfb-content="page-1">',
    '<input name="name"></form>',
    '<form class="jfb-form" id="jfb-2" data-jfb-form="bbbb-2" data-jfb-revision="1" data-jfb-content="page-1">',
    '<input type="hidden" name="jfb-consent-agree" value="e242d51e"></form>',
    '<form action="/search"><input name="q"></form></main>',
  ].join('');
  assert.deepEqual(formsIn(html), [
    { key: 'aaaa-1', revision: 3, content: 'page-1', consents: {} },
    { key: 'bbbb-2', revision: 1, content: 'page-1', consents: { agree: 'e242d51e' } },
  ]);
});
