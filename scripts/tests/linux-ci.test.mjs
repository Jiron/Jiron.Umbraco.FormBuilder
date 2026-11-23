// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
// Unit tests for scripts/linux-ci.mjs: its arguments and the Docker commands it builds. Nothing here starts Docker.
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { describe, test } from 'node:test';
import { fileURLToPath } from 'node:url';
import {
  CONTAINER,
  IMAGE,
  INSIDE,
  STAGES,
  VOLUME,
  buildImageArgs,
  dockerProgram,
  execArgs,
  insideCalls,
  parseArgs,
  runContainerArgs,
} from '../linux-ci.mjs';

const inside = readFileSync(fileURLToPath(new URL('../linux/linux-ci.sh', import.meta.url)), 'utf8');
const dockerfile = readFileSync(fileURLToPath(new URL('../linux/Dockerfile', import.meta.url)), 'utf8');

describe('parseArgs', () => {
  test('runs every stage by default, and the stages named, each once, in their order', () => {
    assert.deepEqual(parseArgs([]).stages, STAGES);
    assert.deepEqual(parseArgs(['smoke', 'build', 'smoke']).stages, ['smoke', 'build']);
    assert.deepEqual(parseArgs(['integration', '--keep']), { stages: ['integration'], stageArgs: [], run: null, stop: false, keep: true, rebuild: false });
    assert.equal(parseArgs(['--rebuild']).rebuild, true);
  });

  test('gives the arguments after -- to the last stage named', () => {
    const options = parseArgs(['build', 'integration', '--', '--share', '2/6']);
    assert.deepEqual(options.stageArgs, ['--share', '2/6']);
    assert.deepEqual(insideCalls(options).map((call) => call.args), [['sync'], ['build'], ['integration', '--share', '2/6']]);
    assert.match(parseArgs(['--', '--share', '1/6']).error, /belong to a stage/);
  });

  test('runs one command in a folder of the copy, and nothing else with it', () => {
    const options = parseArgs(['run', 'tests/e2e', 'npx', 'tsc', '--', '--noEmit']);
    assert.deepEqual(options.run, { dir: 'tests/e2e', command: ['npx', 'tsc', '--noEmit'] });
    assert.deepEqual(insideCalls(options).map((call) => call.args), [['sync'], ['run', 'tests/e2e', 'npx', 'tsc', '--noEmit']]);
    assert.match(parseArgs(['run', '.']).error, /a command/);
    assert.match(parseArgs(['run', '/etc', 'ls']).error, /one of the repository/);
    assert.match(parseArgs(['run', '../other', 'ls']).error, /one of the repository/);
    assert.match(parseArgs(['build', 'run', '.', 'ls']).error, /no stage/);
  });

  test('stops the container, alone, without a sync or a stage', () => {
    const options = parseArgs(['stop']);
    assert.equal(options.stop, true);
    assert.deepEqual(insideCalls(options), []);
    assert.match(parseArgs(['stop', 'build']).error, /nothing else/);
  });

  test('refuses an unknown argument', () => {
    assert.match(parseArgs(['deploy']).error, /unknown argument deploy/);
    assert.match(parseArgs(['--force']).error, /unknown argument/);
  });

  test('refuses anything that asks for SQL Server, in its arguments or its settings', () => {
    for (const argv of [
      ['integration', '--', '--settings', 'sqlserver.runsettings'],
      ['run', '.', 'docker', 'run', 'mcr.microsoft.com/mssql/server:2022-latest'],
      ['integration', '--', 'Tests__Database__DatabaseType=SqlServer'],
      ['run', '.', 'echo', 'SQL Server'],
    ]) {
      assert.match(parseArgs(argv).error, /^refused: no SQL Server/, argv.join(' '));
    }

    assert.match(parseArgs(['integration'], { Tests__Database__DatabaseType: 'SqlServer' }).error, /^refused: .*SQLite/);
    assert.equal(parseArgs(['integration'], { Tests__Database__DatabaseType: 'Sqlite' }).error, undefined);
  });
});

describe('the Docker commands', () => {
  test('build the image from scripts/linux under its own name', () => {
    const args = buildImageArgs('/repo');
    assert.deepEqual(args.slice(0, 3), ['build', '--tag', IMAGE]);
    assert.ok(args.at(-1).endsWith('linux') && args.includes(args.at(-1)), args.join(' '));
    assert.ok(args[args.indexOf('--file') + 1].endsWith('Dockerfile'));
  });

  test('start the container with the repository read-only and the work volume, and nothing published', () => {
    const args = runContainerArgs('C:\\Code\\repo');
    assert.equal(args[0], 'run');
    assert.equal(args[args.indexOf('--name') + 1], CONTAINER);
    assert.ok(args.includes('C:\\Code\\repo:/src:ro'), 'the repository read-only');
    assert.ok(args.includes(`${VOLUME}:/work`), 'the work volume');
    assert.deepEqual(args.slice(-3), [IMAGE, 'sleep', 'infinity'], 'only the image this script builds');
    assert.ok(!args.some((arg) => /^(-p|--publish|--privileged|--network)$/.test(arg)), 'no port, no privilege, the default network');
  });

  test('run the in-container script from the repository, as the container sees it', () => {
    assert.deepEqual(execArgs(['sync']), ['exec', '--workdir', '/work', CONTAINER, 'bash', INSIDE, 'sync']);
    assert.equal(INSIDE, '/src/scripts/linux/linux-ci.sh');
  });

  test('name no database server anywhere', () => {
    const every = [buildImageArgs('/repo'), runContainerArgs('/repo'), ...insideCalls(parseArgs([])).map((call) => execArgs(call.args))].flat().join(' ');
    assert.doesNotMatch(every, /mssql|sql\s*server/i);
    assert.doesNotMatch(dockerfile.replace(/^#.*$/gm, ''), /mssql|sqlserver|sql server/i, 'the image installs none');
  });

  test('use Docker Desktop on Windows when PATH lacks it, and docker elsewhere', () => {
    assert.match(dockerProgram('win32', () => true), /Docker\\resources\\bin\\docker\.exe$/);
    assert.equal(dockerProgram('win32', () => false), 'docker');
    assert.equal(dockerProgram('linux', () => true), 'docker');
  });
});

describe('the in-container script', () => {
  test('has a case for every stage, the sync and one command', () => {
    for (const name of [...STAGES, 'sync', 'run']) assert.match(inside, new RegExp(`^  ${name}\\)$`, 'm'), name);
  });

  test('runs the workflow\'s commands for the build job and the smoke tests', () => {
    const workflow = readFileSync(fileURLToPath(new URL('../../.github/workflows/ci.yml', import.meta.url)), 'utf8');
    for (const command of [
      'dotnet build -c Release --no-restore',
      'dotnet format --verify-no-changes --no-restore',
      'node scripts/size-check.mjs',
      'npx playwright test --project=smoke --project=website',
      'npm ci --prefix scripts --no-audit --no-fund',
    ]) {
      assert.ok(workflow.includes(command), `the workflow runs ${command}`);
      assert.ok(inside.includes(command), `the container runs ${command}`);
    }
  });

  test('refuses SQL Server for the integration tests, and runs the tests as the runner\'s user', () => {
    assert.match(inside, /refused: no SQL Server here/);
    assert.match(inside, /Tests__Database__DatabaseType/);
    assert.match(inside, /runuser -u runner/);
  });
});
