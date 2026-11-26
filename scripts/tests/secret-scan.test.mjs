// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
// Unit tests for scripts/secret-scan.mjs. Run: node --test "scripts/tests/*.test.mjs"
// Every key-shaped value here is generated at runtime, so this file never contains one.
import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { randomBytes, randomInt } from 'node:crypto';
import { mkdtempSync, rmSync, unlinkSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, describe, test } from 'node:test';
import { FAKE_MARKER, fingerprint, forbiddenPathReason, loadAllowlist, run, scanBuffer, scanText } from '../secret-scan.mjs';

const UPPER_DIGITS = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789';
const ALNUM = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789';
const LOWER_DIGITS = 'abcdefghijklmnopqrstuvwxyz0123456789';
const HEX = '0123456789abcdef';

function randomFrom(alphabet, length) {
  return Array.from({ length }, () => alphabet[randomInt(alphabet.length)]).join('');
}

// Where a test says which rules report a value and no others, the random part is lower-case letters and digits: no
// other rule's shape can turn up in it by chance. A body that began with a reCAPTCHA key's two characters was
// reported by that rule too, once in about 4,000 runs.
const generated = {
  anthropic: () => 'sk-ant-' + randomFrom(LOWER_DIGITS, 40),
  openai: () => 'sk-proj-' + randomBytes(30).toString('base64url'),
  aws: () => 'AKIA' + randomFrom(UPPER_DIGITS, 16),
  github: () => 'ghp_' + randomFrom(ALNUM, 36),
  githubPat: () => 'github_pat_' + randomFrom(ALNUM, 70),
  slack: () => 'xoxb-' + randomFrom(ALNUM, 24),
  slackWebhook: () => 'https://hooks.' + 'slack.com/services/T' + randomFrom(UPPER_DIGITS, 8) + '/B' + randomFrom(UPPER_DIGITS, 8) + '/' + randomFrom(ALNUM, 24),
  teamsWebhook: () => 'https://contoso.' + 'webhook.office.com/webhookb2/' + randomFrom(HEX, 8) + '@' + randomFrom(HEX, 8) + '/IncomingWebhook/' + randomFrom(HEX, 32),
  signedUrl: () => 'https://prod-01.example.test/workflows/run?api-version=1&sp=read&' + 'sig=' + randomFrom(ALNUM, 43),
  pem: () => '-----BEGIN ' + 'RSA PRIVATE KEY-----',
  google: () => 'AIza' + randomFrom(ALNUM + '_-', 35),
  recaptcha: () => '6L' + randomFrom(ALNUM, 38),
  hcaptcha: () => '0x' + randomFrom(HEX, 40),
  stripe: () => 'sk_' + 'live_' + randomFrom(ALNUM, 24),
  sendgrid: () => 'SG.' + randomFrom(ALNUM, 22) + '.' + randomFrom(ALNUM, 43),
  nuget: () => 'oy2' + randomFrom(LOWER_DIGITS, 43),
  npm: () => 'npm_' + randomFrom(ALNUM, 36),
  jwt: () => ['eyJ' + randomBytes(18).toString('base64url'), 'eyJ' + randomBytes(24).toString('base64url'), randomBytes(32).toString('base64url')].join('.'),
  azure: () => 'Account' + 'Key=' + randomBytes(64).toString('base64'),
  urlCredentials: () => 'smtp://mailer:' + randomFrom(ALNUM, 16) + '@mail.example.test:587',
  connection: () => `Server=db.example.test;Database=site;User ID=site;Password=${randomFrom(ALNUM, 16)};`,
};

function rulesOf(hits) {
  return hits.map((hit) => hit.rule).sort();
}

describe('patterns', () => {
  test('a key-shaped string is a hit, and the hit never carries the value', () => {
    const secret = generated.anthropic();
    const hits = scanText('src/app.js', `const client = new Client("${secret}");`);
    assert.deepEqual(rulesOf(hits), ['anthropic-key'], 'one key is reported by exactly one rule');
    assert.equal(hits[0].line, 1);
    assert.ok(!JSON.stringify(hits).includes(secret));
  });

  test('a key whose body has the shape of another key is reported by both rules', () => {
    const secret = 'sk-ant-' + '6L' + randomFrom(LOWER_DIGITS, 38);
    assert.deepEqual(rulesOf(scanText('src/app.js', `key: ${secret}`)), ['anthropic-key', 'recaptcha-key']);
  });

  for (const [name, make, rule] of [
    ['OpenAI', generated.openai, 'openai-key'],
    ['AWS', generated.aws, 'aws-access-key'],
    ['GitHub', generated.github, 'github-token'],
    ['GitHub fine-grained', generated.githubPat, 'github-pat'],
    ['Slack', generated.slack, 'slack-token'],
    ['Slack incoming webhook', generated.slackWebhook, 'slack-webhook'],
    ['Teams incoming webhook', generated.teamsWebhook, 'teams-webhook'],
    ['signed address', generated.signedUrl, 'signed-url'],
    ['PEM private key', generated.pem, 'private-key'],
    ['Google API key', generated.google, 'google-api-key'],
    ['reCAPTCHA key', generated.recaptcha, 'recaptcha-key'],
    ['hCaptcha secret', generated.hcaptcha, 'hcaptcha-secret'],
    ['Stripe live key', generated.stripe, 'stripe-live-key'],
    ['SendGrid key', generated.sendgrid, 'sendgrid-key'],
    ['NuGet API key', generated.nuget, 'nuget-key'],
    ['npm token', generated.npm, 'npm-token'],
    ['JWT', generated.jwt, 'jwt'],
    ['Azure storage account key', generated.azure, 'azure-account-key'],
    ['credentials in an address', generated.urlCredentials, 'url-credentials'],
    ['connection string password', generated.connection, 'connection-string-password'],
  ]) {
    test(`${name} shapes are hits`, () => {
      assert.ok(rulesOf(scanText('x.txt', `value ${make()} end`)).includes(rule));
    });
  }

  test('prose that only mentions secrets is not a hit', () => {
    const prose = [
      'Paste the webhook address in the action; the token is never logged.',
      'reCAPTCHA keys come from the host configuration, and PEM private-key headers are blocked.',
      'Set the password in appsettings.Local.json, and the SMTP host in Umbraco:CMS:Global:Smtp.',
      'Links such as https://example.test/path?a=1 and mailto:info@example.test are fine.',
    ].join('\n');
    assert.deepEqual(scanText('README.md', prose), []);
  });

  test('code and placeholders that only name a password are not hits', () => {
    const lines = [
      'var user = new TestUser { Email = Email, Name = "Sam", Password = password };',
      'Server=db.example.test;Database=site;User ID=site;Password=$DB_PASSWORD;',
      'Server=db.example.test;Database=site;User ID=site;Password={password};',
      'Data Source=db;Initial Catalog=site;Pwd=<your password>;',
      'smtp://${SMTP_USER}:${SMTP_PASSWORD}@mail.example.test',
      '$admin = @{ email = "admin@demo.test"; password = (New-Password) }',
    ].join('\n');
    assert.deepEqual(scanText('src/Setup.cs', lines), []);
  });

  test('a fake marker is not a hit', () => {
    const fake = 'sk-ant-' + FAKE_MARKER + '_' + '0'.repeat(32);
    assert.deepEqual(scanText('tests/fake.cs', `var key = "${fake}";`), []);
  });

  test('the line number points at the hit', () => {
    const hits = scanText('a.txt', `one\ntwo\r\nthree ${generated.aws()}\nfour`);
    assert.equal(hits[0].line, 3);
  });

  test('binary content is not scanned line by line', () => {
    const binary = Buffer.concat([Buffer.from([0, 1, 2]), Buffer.from(` ${generated.aws()} `)]);
    assert.deepEqual(scanBuffer('image.png', binary), []);
  });
});

describe('JSON and appsettings keys', () => {
  test('a literal secret value of 12 or more characters is a hit', () => {
    const json = JSON.stringify({ Smtp: { Password: randomFrom(ALNUM, 16) } });
    assert.deepEqual(rulesOf(scanText('appsettings.json', json)), ['json-secret']);
  });

  test('config references, short values, fakes and other key names are not hits', () => {
    const json = JSON.stringify(
      {
        CrmToken: '$JironFormBuilder:Secrets:CrmToken',
        ClientSecret: 'short',
        AccessToken: 'value-' + FAKE_MARKER + '-123456',
        PasswordResetUrl: 'https://example.test/reset-password',
        Description: randomFrom(LOWER_DIGITS, 40),
      },
      null,
      2,
    );
    assert.deepEqual(scanText('appsettings.json', json), []);
  });
});

describe('allowlist', () => {
  test('an allowlisted fingerprint is accepted, but only for its own path', () => {
    const line = JSON.stringify({ ExampleToken: randomFrom(LOWER_DIGITS, 20) });
    const allowlist = [{ path: 'docs/example.md', sha256: fingerprint(line), reason: 'documented example' }];
    assert.deepEqual(scanText('docs/example.md', line, { allowlist }), []);
    assert.deepEqual(rulesOf(scanText('docs/other.md', line, { allowlist })), ['json-secret']);
  });

  test('entries without a reason are rejected', () => {
    const root = mkdtempSync(join(tmpdir(), 'jfb-allow-'));
    try {
      writeFileSync(join(root, '.secret-scan-allowlist.json'), JSON.stringify({ entries: [{ path: 'a', sha256: 'f'.repeat(64) }] }));
      assert.throws(() => loadAllowlist(root), /reason/);
    } finally {
      rmSync(root, { recursive: true, force: true });
    }
  });
});

describe('forbidden paths', () => {
  test('.private, .local, key files, *.Local.json, .env and live settings are forbidden', () => {
    for (const path of [
      '.private/GOAL.md',
      '.private/research/notes.md',
      'sub/.private/x.txt',
      'anthropic-key.txt',
      'sub/openai-key.txt',
      '.local/credentials.json',
      'demo/appsettings.Development.Local.json',
      '.env',
      'web/.env.production',
      'demo/appsettings.Production.json',
      'site/appsettings.staging.json',
      'certs/site.pfx',
      'certs/site.pem',
    ]) {
      assert.ok(forbiddenPathReason(path), path);
    }
  });

  test('ordinary files are allowed', () => {
    for (const path of ['README.md', 'src/Key.cs', 'docs/local.md', 'docs/private.md', 'appsettings.json', 'scripts/environment.mjs', '.editorconfig', 'src/KeyValueRepository.cs']) {
      assert.equal(forbiddenPathReason(path), null, path);
    }
  });
});

describe('git modes (temporary repository)', () => {
  const repo = mkdtempSync(join(tmpdir(), 'jfb-scan-'));
  const git = (...args) => execFileSync('git', args, { cwd: repo, stdio: ['ignore', 'pipe', 'pipe'] }).toString();
  git('init', '-q', '-b', 'main');
  git('config', 'user.name', 'Scanner Test');
  git('config', 'user.email', 'scanner@example.test');
  git('config', 'core.autocrlf', 'false');
  git('config', 'core.hooksPath', '.no-hooks');
  after(() => rmSync(repo, { recursive: true, force: true }));

  test('staged mode finds a staged secret and prints no value', async () => {
    const secret = generated.github();
    writeFileSync(join(repo, 'config.js'), `export const token = '${secret}';\n`);
    git('add', 'config.js');
    const result = await run({ mode: 'staged', cwd: repo });
    assert.equal(result.exitCode, 1);
    assert.match(result.output.join('\n'), /config\.js:1 {2}github-token/);
    assert.ok(!result.output.join('\n').includes(secret));
    git('rm', '-q', '--cached', 'config.js');
    unlinkSync(join(repo, 'config.js'));
  });

  test('history mode finds secrets and forbidden files that were removed later', async () => {
    writeFileSync(join(repo, 'README.md'), '# test\n');
    writeFileSync(join(repo, 'old.txt'), `aws ${generated.aws()}\n`);
    writeFileSync(join(repo, '.env'), '');
    git('add', 'README.md', 'old.txt', '.env');
    git('commit', '-q', '-m', 'add');
    git('rm', '-q', 'old.txt', '.env');
    git('commit', '-q', '-m', 'remove');

    const tracked = await run({ mode: 'tracked', cwd: repo });
    assert.equal(tracked.exitCode, 0, tracked.output.join('\n'));

    const history = await run({ mode: 'history', cwd: repo });
    const text = history.output.join('\n');
    assert.equal(history.exitCode, 1);
    assert.match(text, /commit [0-9a-f]{7} old\.txt:1 {2}aws-access-key/);
    assert.match(text, /\.env {2}forbidden-path/);
  });

  test('a clean repository exits 0', async () => {
    const clean = mkdtempSync(join(tmpdir(), 'jfb-scan-clean-'));
    try {
      execFileSync('git', ['init', '-q', '-b', 'main'], { cwd: clean });
      execFileSync('git', ['config', 'core.autocrlf', 'false'], { cwd: clean });
      writeFileSync(join(clean, 'a.txt'), 'hello\n');
      execFileSync('git', ['add', 'a.txt'], { cwd: clean });
      const result = await run({ mode: 'staged', cwd: clean });
      assert.equal(result.exitCode, 0, result.output.join('\n'));
      assert.match(result.output[0], /clean \(staged, 1 files/);
    } finally {
      rmSync(clean, { recursive: true, force: true });
    }
  });
});
