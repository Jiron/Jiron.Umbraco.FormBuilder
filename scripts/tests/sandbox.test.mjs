// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
// Unit tests for scripts/sandbox.mjs. Nothing here starts dotnet or a site.
import assert from 'node:assert/strict';
import { spawn, spawnSync } from 'node:child_process';
import { mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { createServer } from 'node:net';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { describe, test } from 'node:test';
import { fileURLToPath } from 'node:url';
import {
  BUILD_ENV,
  ISOLATION_FILES,
  SITES,
  childEnv,
  isOurProcess,
  linuxStartText,
  loadSecrets,
  loginLines,
  newPassword,
  parseArgs,
  pinClientPackages,
  portOpen,
  processStartTime,
  readPidRecord,
  sitePaths,
} from '../sandbox.mjs';

const script = fileURLToPath(new URL('../sandbox.mjs', import.meta.url));

function withRoot(fn) {
  const root = mkdtempSync(join(tmpdir(), 'jfb-sandbox-'));
  try {
    return fn(root);
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
}

describe('parseArgs', () => {
  test('takes a command and an optional site', () => {
    assert.deepEqual(parseArgs(['start']), { command: 'start', site: 'sandbox' });
    assert.deepEqual(parseArgs(['stop', '--site', 'sandbox']), { command: 'stop', site: 'sandbox' });
  });

  test('refuses unknown commands, sites and arguments', () => {
    assert.match(parseArgs([]).error, /unknown command/);
    assert.match(parseArgs(['launch']).error, /unknown command/);
    assert.match(parseArgs(['start', '--site', 'nope']).error, /unknown site/);
    assert.match(parseArgs(['start', '--port', '1']).error, /unknown argument/);
  });

  test('a usage error exits with code 2', () => {
    const result = spawnSync(process.execPath, [script, 'launch'], { encoding: 'utf8' });
    assert.equal(result.status, 2);
    assert.match(result.stderr, /unknown command launch/);
  });
});

describe('sites', () => {
  test('every site uses a port of its own in the 5500 to 5599 range (CLAUDE.md rule 6)', () => {
    const ports = Object.values(SITES).map((site) => site.port);
    assert.equal(new Set(ports).size, ports.length);
    for (const port of ports) assert.ok(port >= 5500 && port <= 5599, `port ${port}`);
  });

  test('every site lives under the git-ignored .local folder and has its own template hive', () => {
    for (const [name, site] of Object.entries(SITES)) {
      assert.match(site.folder, /^\.local\//);
      assert.match(sitePaths('C:/repo', name).hive, /jfb-templates-\d+\.\d+\.\d+$/);
    }
  });
});

describe('newPassword', () => {
  test('is long, mixes every character class and is never repeated', () => {
    const passwords = Array.from({ length: 20 }, () => newPassword());
    assert.equal(new Set(passwords).size, passwords.length);
    for (const password of passwords) {
      assert.ok(password.length >= 30);
      assert.match(password, /[A-Z]/);
      assert.match(password, /[a-z]/);
      assert.match(password, /[0-9]/);
      assert.match(password, /[^A-Za-z0-9]/);
    }
  });
});

describe('loadSecrets', () => {
  test('creates the admin once and reuses it on the next call', () => {
    withRoot((root) => {
      const first = loadSecrets(root, 'sandbox');
      const second = loadSecrets(root, 'sandbox');
      assert.equal(second.admin.password, first.admin.password);
      assert.equal(second.imagingHmacSecretKey, first.imagingHmacSecretKey);
      assert.equal(first.url, 'http://127.0.0.1:5510/umbraco');
      assert.match(first.admin.email, /@sandbox\.jfb\.test$/);
    });
  });

  test('keeps the other entries of credentials.json as they are', () => {
    withRoot((root) => {
      const paths = sitePaths(root, 'sandbox');
      loadSecrets(root, 'sandbox');
      const before = JSON.parse(readFileSync(paths.credentials, 'utf8'));
      writeFileSync(paths.credentials, JSON.stringify({ ...before, demo: { admin: { email: 'a@demo.test' } } }));
      loadSecrets(root, 'sandbox');
      const after = JSON.parse(readFileSync(paths.credentials, 'utf8'));
      assert.deepEqual(after.demo, { admin: { email: 'a@demo.test' } });
      assert.equal(after.sandbox.admin.password, before.sandbox.admin.password);
    });
  });
});

describe('childEnv', () => {
  test('passes the unattended admin, the address, plain http, the mail folder and the build settings', () => {
    withRoot((root) => {
      const paths = sitePaths(root, 'sandbox');
      const secrets = loadSecrets(root, 'sandbox');
      const env = childEnv({ PATH: 'x' }, paths, secrets);
      assert.equal(env.PATH, 'x');
      assert.equal(env.ASPNETCORE_URLS, 'http://127.0.0.1:5510');
      assert.equal(env.Umbraco__CMS__Unattended__InstallUnattended, 'true');
      assert.equal(env.Umbraco__CMS__Unattended__UnattendedUserEmail, secrets.admin.email);
      assert.equal(env.Umbraco__CMS__Unattended__UnattendedUserPassword, secrets.admin.password);
      assert.equal(env.Umbraco__CMS__Global__UseHttps, 'false');
      assert.equal(env.Umbraco__CMS__Global__Smtp__PickupDirectoryLocation, paths.mail);
      for (const [name, value] of Object.entries(BUILD_ENV)) assert.equal(env[name], value);
    });
  });

  test('leaves this process\'s own environment alone', () => {
    withRoot((root) => {
      // Compared as one yes or no, so a failure never prints the environment into a log.
      const before = JSON.stringify({ ...process.env });
      childEnv(process.env, sitePaths(root, 'sandbox'), loadSecrets(root, 'sandbox'));
      assert.ok(JSON.stringify({ ...process.env }) === before, 'the environment of this process changed');
      assert.ok(process.env.Umbraco__CMS__Unattended__UnattendedUserPassword === undefined, 'the password reached this process');
    });
  });
});

describe('login', () => {
  test('names the address, the admin and the file, never the password', () => {
    withRoot((root) => {
      const secrets = loadSecrets(root, 'sandbox');
      const text = loginLines(secrets, sitePaths(root, 'sandbox')).join('\n');
      assert.match(text, /http:\/\/127\.0\.0\.1:5510\/umbraco/);
      assert.match(text, /credentials\.json/);
      assert.ok(!text.includes(secrets.admin.password));
      assert.ok(!text.includes(secrets.imagingHmacSecretKey));
    });
  });
});

describe('isolation files', () => {
  test('stop the walk into the repository\'s build files and editor settings', () => {
    assert.match(ISOLATION_FILES['Directory.Build.props'], /^<Project>[\s\S]*<\/Project>\n$/);
    assert.match(ISOLATION_FILES['Directory.Build.targets'], /^<Project>[\s\S]*<\/Project>\n$/);
    assert.equal(ISOLATION_FILES['.editorconfig'], 'root = true\n');
  });
});

describe('the PID record', () => {
  test('reads a valid record and ignores a missing or broken one', () => {
    withRoot((root) => {
      const file = join(root, 'x.pid.json');
      assert.equal(readPidRecord(file), null);
      writeFileSync(file, 'not json');
      assert.equal(readPidRecord(file), null);
      writeFileSync(file, JSON.stringify({ pid: ' 12', startedAt: 'x' }));
      assert.equal(readPidRecord(file), null);
      writeFileSync(file, JSON.stringify({ pid: 12, startedAt: '2026-10-22T10:00:00.0000000Z' }));
      assert.deepEqual(readPidRecord(file), { pid: 12, startedAt: '2026-10-22T10:00:00.0000000Z' });
    });
  });

  test('a recycled PID with another start time is not ours, so stop never touches it', () => {
    const record = { pid: 4242, startedAt: '2026-10-22T10:00:00.0000000Z' };
    assert.equal(isOurProcess(record, () => '2026-10-22T10:00:00.0000000Z'), true);
    assert.equal(isOurProcess(record, () => '2026-10-22T11:30:00.0000000Z'), false);
    assert.equal(isOurProcess(record, () => null), false);
    assert.equal(isOurProcess(null, () => 'never asked'), false);
  });

  test('the start time of this test process is read from Windows', { skip: process.platform !== 'win32' }, () => {
    const started = processStartTime(process.pid);
    assert.match(started, /^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d\.\d+Z$/);
    assert.equal(processStartTime(process.pid), started);
  });

  test('the start time of this test process is read from Linux', { skip: process.platform !== 'linux' }, () => {
    const started = processStartTime(process.pid);
    assert.match(started, /^linux:\d+:\d+$/);
    assert.equal(processStartTime(process.pid), started);
  });

  test('a child has a start time of its own while it runs and none once it has ended', async () => {
    const child = spawn(process.execPath, ['-e', 'setTimeout(() => {}, 60000)'], { stdio: 'ignore' });
    try {
      await new Promise((resolve) => child.once('spawn', resolve));
      const started = processStartTime(child.pid);
      assert.equal(typeof started, 'string');
      assert.notEqual(started, processStartTime(process.pid));
      assert.equal(processStartTime(child.pid), started);
    } finally {
      child.kill();
      await new Promise((resolve) => child.once('exit', resolve));
    }
    assert.equal(processStartTime(child.pid), null);
  });

  test('no PID, or not a number, has no start time', () => {
    assert.equal(processStartTime(0), null);
    assert.equal(processStartTime(-3), null);
    assert.equal(processStartTime('x'), null);
  });

  describe('on Linux, from /proc', () => {
    const boot = 'cpu  1 2 3\nbtime 1763856000\nprocesses 99\n';
    // Fields 3 to 22 after the name; field 22 (the start in ticks after boot) is 123456.
    const after = (state, ticks) => `${state} 1 1 1 0 -1 4194560 100 0 0 0 1 1 0 0 20 0 1 0 ${ticks} 1000 10`;
    const files = (stat) => (file) => {
      if (file === '/proc/stat') return boot;
      if (file === '/proc/77/stat' && stat !== null) return stat;
      throw Object.assign(new Error(`ENOENT ${file}`), { code: 'ENOENT' });
    };

    test('reads the start in ticks and the boot time, whatever the command name holds', () => {
      assert.equal(linuxStartText(`77 (node) ${after('S', 123456)}`, boot), 'linux:1763856000:123456');
      assert.equal(linuxStartText(`77 (a ) b (c)) ${after('R', 123456)}`, boot), 'linux:1763856000:123456');
      assert.equal(processStartTime(77, { platform: 'linux', readText: files(`77 (dotnet) ${after('S', 5)}`) }), 'linux:1763856000:5');
    });

    test('a recycled PID starts at another tick, so its text differs', () => {
      const first = processStartTime(77, { platform: 'linux', readText: files(`77 (dotnet) ${after('S', 5)}`) });
      const second = processStartTime(77, { platform: 'linux', readText: files(`77 (bash) ${after('S', 9000)}`) });
      assert.notEqual(first, second);
    });

    test('a process that is gone, a zombie or a file it cannot read has none', () => {
      assert.equal(processStartTime(77, { platform: 'linux', readText: files(null) }), null);
      assert.equal(linuxStartText(`77 (node) ${after('Z', 5)}`, boot), null);
      assert.equal(linuxStartText('77 node S 1', boot), null);
      assert.equal(linuxStartText(`77 (node) ${after('S', 'x')}`, boot), null);
      assert.equal(linuxStartText(`77 (node) ${after('S', 5)}`, 'cpu 1 2 3\n'), null);
    });
  });
});

describe('portOpen', () => {
  test('sees a listening port and a closed one', async () => {
    const server = createServer().listen(0, '127.0.0.1');
    await new Promise((resolve) => server.once('listening', resolve));
    const { port } = server.address();
    try {
      assert.equal(await portOpen(port), true);
    } finally {
      await new Promise((resolve) => server.close(resolve));
    }
    assert.equal(await portOpen(port), false);
  });
});

describe('pinClientPackages', () => {
  test('pins the client generator and the backoffice package to the profile\'s exact versions', () => {
    withRoot((root) => {
      writeFileSync(join(root, 'package.json'), JSON.stringify({ devDependencies: { '@hey-api/openapi-ts': '^0.85.2', '@umbraco-cms/backoffice': '^17.7.1', vite: '^7.3.1' } }));
      pinClientPackages(root, SITES.sandbox);
      const pinned = JSON.parse(readFileSync(join(root, 'package.json'), 'utf8')).devDependencies;
      assert.equal(pinned['@hey-api/openapi-ts'], SITES.sandbox.heyApi);
      assert.equal(pinned['@umbraco-cms/backoffice'], SITES.sandbox.umbraco);
      assert.equal(pinned.vite, '^7.3.1');
    });
  });
});

describe('the started program', () => {
  test('is the built site itself, inside the site folder, not dotnet run', () => {
    const paths = sitePaths('C:/repo', 'sandbox');
    assert.ok(paths.exe.endsWith(join('.local', 'sandbox', 'Sandbox', 'bin', 'Debug', 'net10.0', 'Sandbox.exe')), paths.exe);
  });
});
