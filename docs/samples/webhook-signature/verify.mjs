// Checks the X-Jfb-Signature header of a webhook from Jiron Form Builder (docs/actions.md).
import { createHmac, timingSafeEqual } from 'node:crypto';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';

/** How far the header's time may be from now, in seconds, so a recorded request cannot be sent again later. */
export const TOLERANCE_SECONDS = 300;

/**
 * Whether the header is genuine for these bytes of the body, exactly as they arrived, and recent.
 * @param {string} secret The signing secret.
 * @param {string | undefined} header The value of the X-Jfb-Signature header.
 * @param {Buffer} body The request's body, before any parsing.
 * @param {number} [now] The time now, in seconds since 1970.
 * @returns {boolean} Whether the request came from the holder of the secret, and not long ago.
 */
export function isGenuine(secret, header, body, now = Date.now() / 1000) {
  const parts = (header ?? '').split(',').map((part) => part.trim());
  const times = parts.filter((part) => part.startsWith('t='));
  const signatures = parts.filter((part) => part.startsWith('v1='));
  if (times.length !== 1 || signatures.length !== 1) return false;
  const time = times[0].slice(2);
  const signature = signatures[0].slice(3);
  if (!/^\d+$/.test(time)) return false;
  if (!Number.isFinite(now) || Math.abs(now - Number(time)) > TOLERANCE_SECONDS) return false;

  const expected = createHmac('sha256', secret).update(`${time}.`).update(body).digest('hex');
  const given = Buffer.from(signature);
  return given.length === expected.length && timingSafeEqual(given, Buffer.from(expected));
}

// From a command line: node verify.mjs <secret> <header> <file with the body> [seconds since 1970, now when left out];
// exit code 0 when genuine.
if (process.argv[1] === fileURLToPath(import.meta.url)) {
  const [secret, header, file, now] = process.argv.slice(2);
  process.exit(isGenuine(secret, header, readFileSync(file), now === undefined ? undefined : Number(now)) ? 0 : 1);
}
