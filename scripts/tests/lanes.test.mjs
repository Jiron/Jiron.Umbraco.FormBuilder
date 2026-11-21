// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
// Unit tests for scripts/lanes.mjs. Run: node --test "scripts/tests/*.test.mjs"
import assert from 'node:assert/strict';
import { existsSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, describe, test } from 'node:test';
import { invocation, lineWriter, markOf, parseArgs, runLanes, splitCommand, summaryLines } from '../lanes.mjs';

const dir = mkdtempSync(join(tmpdir(), 'jfb-lanes-test-'));
after(() => rmSync(dir, { recursive: true, force: true }));

/** A script in the test's folder; returns the words of a command that runs it with arguments. */
let scripts = 0;
function script(body, ...args) {
  const file = join(dir, `script-${++scripts}.mjs`);
  writeFileSync(file, body);
  return ['node', file, ...args];
}

const lane = (name, ...commands) => ({ name, cwd: '.', commands });

/** Whether a process with this PID still runs. */
function alive(pid) {
  try {
    process.kill(pid, 0);
    return true;
  } catch {
    return false;
  }
}

describe('parseArgs', () => {
  test('reads lanes, each with its folder and commands in order, a time limit and a report', () => {
    const options = parseArgs(['--timeout', '5', '--report', 'r.json', '--lane', 'Checks', 'node a.mjs --x', 'node b.mjs', '--lane', 'Client', '--cwd', 'src/c', 'npm ci --no-audit']);
    assert.equal(options.mode, 'run');
    assert.equal(options.timeout, 5);
    assert.equal(options.report, 'r.json');
    assert.deepEqual(options.lanes, [
      { name: 'Checks', cwd: '.', commands: [['node', 'a.mjs', '--x'], ['node', 'b.mjs']] },
      { name: 'Client', cwd: 'src/c', commands: [['npm', 'ci', '--no-audit']] },
    ]);
    assert.equal(parseArgs(['--lane', 'a', 'node x']).timeout, 60);
    assert.equal(parseArgs(['--stop']).mode, 'stop');
  });

  test('refuses what does not make a lane', () => {
    assert.match(parseArgs([]).error, /no lane/);
    assert.match(parseArgs(['node x']).error, /belongs to no lane/);
    assert.match(parseArgs(['--lane', 'a']).error, /has no command/);
    assert.match(parseArgs(['--lane', 'a', 'node x', '--lane', 'a', 'node y']).error, /two lanes/);
    assert.match(parseArgs(['--cwd', 'x', '--lane', 'a', 'node x']).error, /after --lane/);
    assert.match(parseArgs(['--lane', 'a', 'node x', '--cwd', 'y']).error, /before its commands/);
    assert.match(parseArgs(['--lane']).error, /needs a value/);
    assert.match(parseArgs(['--lane', 'a', 'node x', '--timeout', '0']).error, /more than 0/);
    assert.match(parseArgs(['--lane', 'a', 'node x', '--fast']).error, /unknown argument/);
    assert.match(parseArgs(['--stop', '--lane', 'a', 'node x']).error, /no lanes/);
  });

  test('a command is plain words: no quotes and no shell syntax', () => {
    assert.deepEqual(splitCommand('  node --test scripts/tests/*.test.mjs '), ['node', '--test', 'scripts/tests/*.test.mjs']);
    for (const bad of ['node "a b"', "node 'a'", 'npm ci && rm x', 'a | b', 'a > b', 'a; b', 'echo %PATH%', 'echo $(x)']) {
      assert.throws(() => splitCommand(bad), /quote or shell syntax/, bad);
    }
    assert.throws(() => splitCommand('   '), /empty/);
    assert.match(parseArgs(['--lane', 'a', 'node "x"']).error, /quote or shell syntax/);
  });

  test('runs node as this Node, and the rest through the shell on Windows only', () => {
    assert.deepEqual(invocation(['node', 'a.mjs'], { windows: true, node: 'N' }), { file: 'N', args: ['a.mjs'], shell: false });
    assert.deepEqual(invocation(['npm', 'ci', '--no-audit'], { windows: true, node: 'N' }), { file: 'npm ci --no-audit', args: [], shell: true });
    assert.deepEqual(invocation(['npm', 'ci'], { windows: false, node: 'N' }), { file: 'npm', args: ['ci'], shell: false });
  });
});

describe('the output', () => {
  test('goes out in whole lines, each with its lane mark, a last unfinished line on flush', () => {
    const lines = [];
    const writer = lineWriter('[a]', (line) => lines.push(line));
    writer.push('one\r\ntw');
    writer.push('o\nthr');
    assert.deepEqual(lines, ['[a] one', '[a] two']);
    writer.flush();
    writer.flush();
    assert.deepEqual(lines, ['[a] one', '[a] two', '[a] thr']);
  });

  test('marks line up whatever the length of a lane name', () => {
    assert.equal(markOf('.NET', 7), '[.NET]   ');
    assert.equal(markOf('Clients', 7), '[Clients]');
  });

  test('the summary gives each lane its seconds and result, and the failure', () => {
    const lines = summaryLines(
      [
        { name: 'Checks', seconds: 31.4, result: 'passed', failed: null },
        { name: 'Client tests', seconds: 9.6, result: 'failed', failed: 'npm test' },
        { name: '.NET', seconds: 12, result: 'stopped', failed: 'dotnet build' },
      ],
      false,
      'lane "Client tests": "npm test" exited with code 1',
    );
    assert.deepEqual(lines, [
      'lanes:   lane          seconds  result',
      'lanes:   Checks             31  passed',
      'lanes:   Client tests       10  failed at "npm test"',
      'lanes:   .NET               12  stopped at "dotnet build"',
      'lanes: FAILED: lane "Client tests": "npm test" exited with code 1',
    ]);
    assert.equal(summaryLines([{ name: 'a', seconds: 1, result: 'passed', failed: null }], true, null).at(-1), 'lanes: every lane passed');
  });
});

describe('runLanes', () => {
  test('runs the lanes side by side and the commands of a lane in order, every line marked', async () => {
    // Each lane waits for a file the other lane writes: run one after the other, neither would end.
    const ready = (mine, theirs) => script(`import { existsSync, writeFileSync } from 'node:fs';
writeFileSync(${JSON.stringify(join(dir, mine))}, '');
const deadline = Date.now() + 20000;
while (!existsSync(${JSON.stringify(join(dir, theirs))})) { if (Date.now() > deadline) process.exit(3); await new Promise((r) => setTimeout(r, 20)); }
console.log('${mine} saw ${theirs}');`);
    const order = join(dir, 'order.txt');
    const append = (text) => script(`import { appendFileSync } from 'node:fs'; appendFileSync(${JSON.stringify(order)}, '${text}'); console.log('wrote ${text}'); process.stdout.write('no line break');`);
    const lines = [];
    const outcome = await runLanes([lane('first', ready('a.flag', 'b.flag'), append('1'), append('2')), lane('second lane', ready('b.flag', 'a.flag'))], { out: (line) => lines.push(line), timeoutMinutes: 1 });

    assert.equal(outcome.ok, true, outcome.failure);
    assert.deepEqual(outcome.results.map((result) => [result.name, result.result]), [['first', 'passed'], ['second lane', 'passed']]);
    assert.equal(readFileSync(order, 'utf8'), '12');
    assert.ok(lines.every((line) => line.startsWith('[first]       ') || line.startsWith('[second lane] ')), lines.join('\n'));
    assert.ok(lines.includes('[first]       a.flag saw b.flag'));
    assert.ok(lines.includes('[second lane] b.flag saw a.flag'));
    assert.ok(lines.includes('[first]       no line break'));
    assert.ok(lines.some((line) => /^\[first\] +ok after \d+ s: node .*script-\d+\.mjs$/.test(line)));
    assert.ok(outcome.results.every((result) => result.seconds >= 0));
  });

  test('a failure fails the run naming the lane and command, runs no more of that lane, and stops the other lanes by PID', async () => {
    const pidOf = join(dir, 'sleeper.pid');
    const sleeper = script(`import { writeFileSync } from 'node:fs'; writeFileSync(${JSON.stringify(pidOf)}, String(process.pid)); setTimeout(() => {}, 120000);`);
    const fail = script(`import { existsSync } from 'node:fs';
while (!existsSync(${JSON.stringify(pidOf)})) await new Promise((r) => setTimeout(r, 20));
console.error('broken'); process.exit(4);`);
    const never = script(`import { writeFileSync } from 'node:fs'; writeFileSync(${JSON.stringify(join(dir, 'never.txt'))}, 'ran');`);
    const lines = [];
    const began = Date.now();
    const outcome = await runLanes([lane('slow', sleeper, never), lane('broken', fail, never)], { out: (line) => lines.push(line), timeoutMinutes: 1 });

    assert.equal(outcome.ok, false);
    assert.equal(outcome.failure, `lane "broken": "${fail.join(' ')}" exited with code 4`);
    assert.deepEqual(outcome.results.map((result) => [result.name, result.result, result.failed]), [
      ['slow', 'stopped', sleeper.join(' ')],
      ['broken', 'failed', fail.join(' ')],
    ]);
    assert.ok(Date.now() - began < 60000, 'the sleeping lane was stopped, not waited for');
    assert.equal(alive(Number(readFileSync(pidOf, 'utf8'))), false, 'its process is gone');
    assert.equal(existsSync(join(dir, 'never.txt')), false, 'no lane went on after the failure');
    assert.ok(lines.includes('[broken] broken'));
  });

  test('a run past its time limit stops every lane and fails naming the commands that still ran', async () => {
    const slow = script('setTimeout(() => {}, 120000);');
    const lines = [];
    const outcome = await runLanes([lane('slow', slow)], { out: (line) => lines.push(line), timeoutMinutes: 0.02 });
    assert.equal(outcome.ok, false);
    assert.match(outcome.failure, /ran past 0\.02 minutes; stopped "slow" at "node .*script-\d+\.mjs"/);
    assert.equal(outcome.results[0].result, 'stopped');
  });

  test('records its processes in the PID file while they run, and removes it after', async () => {
    const pidFile = join(dir, 'state', 'run.json');
    const seen = join(dir, 'seen.json');
    const look = script(`import { readFileSync, writeFileSync } from 'node:fs';
const deadline = Date.now() + 20000;
for (;;) {
  let text = '';
  let mine = false;
  try {
    text = readFileSync(${JSON.stringify(pidFile)}, 'utf8');
    mine = JSON.parse(text).children.some((child) => child.pid === process.pid && child.startedAt);
  } catch {}
  if (mine) { writeFileSync(${JSON.stringify(seen)}, text); break; }
  if (Date.now() > deadline) process.exit(3);
  await new Promise((r) => setTimeout(r, 20));
}`);
    const outcome = await runLanes([lane('look', look)], { out: () => {}, timeoutMinutes: 1, pidFile });
    assert.equal(outcome.ok, true, outcome.failure);
    const record = JSON.parse(readFileSync(seen, 'utf8'));
    assert.equal(record.pid, process.pid);
    assert.deepEqual(record.children.map((child) => [child.label, child.command]), [['look', look.join(' ')]]);
    assert.ok(Number.isInteger(record.children[0].pid));
    assert.equal(existsSync(pidFile), false);
  });
});
