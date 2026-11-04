import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { mkdtempSync, readFileSync, writeFileSync, mkdirSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import test from 'node:test';
import { auditRepository, inspectFile, validatePolicy } from './repo-audit.mjs';

const policy = JSON.parse(readFileSync(new URL('../.repo-audit.json', import.meta.url), 'utf8'));

test('source files and dependency manifests are allowed; limit is inclusive', () => {
  for (const path of ['src/App.cs', 'src/MainWindow.xaml', 'App.csproj', 'App.sln', 'packages.lock.json', 'docs/note.md']) {
    assert.deepEqual(inspectFile(path, policy.maxFileBytes, policy), []);
  }
  assert.ok(inspectFile('image.png', policy.maxFileBytes + 1, policy).length);
});

test('model, binary and archive types are caught irrespective of case or size', () => {
  for (const path of ['weights.GGUF', 'model.onnx', 'model.safetensors', 'lib.DLL', 'app.exe', 'backup.zip', 'weights.bin']) {
    assert.ok(inspectFile(path, 10, policy).length, path);
  }
});

test('generated directories match whole segments; local data folders are root-only', () => {
  for (const path of ['src/bin/a.txt', 'src/OBJ/cache.json', 'TestResults/a.trx', 'models/weights.txt', '.local/cache']) {
    assert.ok(inspectFile(path, 10, policy).length, path);
  }
  for (const path of ['src/Models/User.cs', 'src/Data/Task.cs', 'src/binary/source.cs']) {
    assert.deepEqual(inspectFile(path, 10, policy), [], path);
  }
});

test('exceptions require exact paths and a reason, and enforce their size limit', () => {
  const configured = { ...policy, exceptions: [{ path: 'vendor/needed.dll', reason: 'Approved SDK', maxBytes: 100 }] };
  validatePolicy(configured);
  assert.deepEqual(inspectFile('vendor/needed.dll', 100, configured), []);
  assert.ok(inspectFile('vendor/needed.dll', 101, configured).length);
  assert.ok(inspectFile('other/needed.dll', 10, configured).length);
  for (const exception of [
    { path: 'vendor/*.dll', reason: 'x', maxBytes: 10 },
    { path: '../needed.dll', reason: 'x', maxBytes: 10 },
    { path: 'needed.dll', reason: '', maxBytes: 10 },
    { path: 'needed.dll', reason: 'x', maxBytes: 0 },
  ]) assert.throws(() => validatePolicy({ ...policy, exceptions: [exception] }));
});

test('Git integration: scans incoming history, LFS sizes, unusual paths, and deletions', () => {
  const root = mkdtempSync(join(tmpdir(), 'dsp-audit-'));
  const originalDirectory = process.cwd();
  const git = (...args) => execFileSync('git', ['-C', root, ...args], { encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] }).trim();
  const commit = message => {
    git('add', '--all');
    git('-c', 'user.name=Audit Test', '-c', 'user.email=audit@example.invalid', 'commit', '-m', message);
    return git('rev-parse', 'HEAD');
  };
  try {
    git('init');
    writeFileSync(join(root, 'source.cs'), '// fixture\n');
    const base = commit('base');
    mkdirSync(join(root, 'vendor'));
    writeFileSync(join(root, 'vendor', 'library with spaces.DLL'), 'binary fixture');
    writeFileSync(join(root, 'large image.png'),
      'version https://git-lfs.github.com/spec/v1\r\noid sha256:' + 'a'.repeat(64) + '\r\nsize 20000000\r\n');
    const bad = commit('unintended files');
    git('rm', '--', 'vendor/library with spaces.DLL');
    const head = commit('delete binary');
    process.chdir(root);
    const current = auditRepository(policy);
    assert.equal(current.findings.length, 1);
    assert.equal(current.findings[0].bytes, 20000000);
    const incoming = auditRepository(policy, { base, head });
    assert.ok(incoming.findings.some(finding => finding.path === 'vendor/library with spaces.DLL' && finding.commit === bad));
    assert.ok(incoming.findings.some(finding => finding.path === 'large image.png' && finding.bytes === 20000000));
    assert.ok(auditRepository(policy, { base: '0'.repeat(40), head }).commitsChecked >= 3);
    assert.throws(() => auditRepository(policy, { base: '--all', head }));
    assert.throws(() => auditRepository(policy, { base: 'f'.repeat(40), head }));
  } finally {
    process.chdir(originalDirectory);
    // Delete only the exact temporary fixture directory created above.
    if (dirname(resolve(root)) !== resolve(tmpdir()) || !root.startsWith(join(tmpdir(), 'dsp-audit-'))) {
      throw new Error('Refusing to clean unexpected fixture path.');
    }
    rmSync(root, { recursive: true, force: true });
  }
});
