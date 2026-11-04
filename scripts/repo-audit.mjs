import { execFileSync } from 'node:child_process';
import { readFileSync } from 'node:fs';
import { extname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

export function validatePolicy(policy) {
  if (!Number.isSafeInteger(policy.maxFileBytes) || policy.maxFileBytes <= 0) {
    throw new Error('maxFileBytes must be a positive integer.');
  }
  for (const key of ['blockedExtensions', 'blockedDirectories', 'blockedRootDirectories']) {
    if (!Array.isArray(policy[key]) || policy[key].some(value => typeof value !== 'string' || !value)) {
      throw new Error(`${key} must be an array of non-empty strings.`);
    }
  }
  if (!Array.isArray(policy.exceptions)) throw new Error('exceptions must be an array.');
  const paths = new Set();
  for (const exception of policy.exceptions) {
    if (typeof exception.path !== 'string' || !exception.path ||
        exception.path.includes('\\') || exception.path.startsWith('/') ||
        exception.path.split('/').some(part => ['..', '.', ''].includes(part)) ||
        /[*?\[\]]/.test(exception.path) || paths.has(exception.path)) {
      throw new Error('Exceptions require unique, exact repository-relative paths without wildcards.');
    }
    if (typeof exception.reason !== 'string' || !exception.reason.trim() ||
        !Number.isSafeInteger(exception.maxBytes) || exception.maxBytes <= 0) {
      throw new Error(`Exception ${exception.path} requires reason and positive maxBytes.`);
    }
    paths.add(exception.path);
  }
}

export function inspectFile(path, size, policy) {
  const exception = policy.exceptions.find(item => item.path === path);
  if (exception && size <= exception.maxBytes) return [];
  const reasons = [];
  if (exception) reasons.push(`exceeds approved exception size (${exception.maxBytes} bytes)`);
  if (size > policy.maxFileBytes) reasons.push(`larger than ${policy.maxFileBytes} bytes`);
  const parts = path.toLowerCase().split('/');
  if (policy.blockedExtensions.map(value => value.toLowerCase()).includes(extname(path).toLowerCase())) {
    reasons.push('model, binary library, installer or archive requires review');
  }
  if (parts.slice(0, -1).some(part => policy.blockedDirectories.some(value => value.toLowerCase() === part)) ||
      (parts.length > 1 && policy.blockedRootDirectories.some(value => value.toLowerCase() === parts[0]))) {
    reasons.push('generated output or local data directory');
  }
  return reasons;
}

function git(...args) {
  return execFileSync('git', args, { maxBuffer: 128 * 1024 * 1024, stdio: ['ignore', 'pipe', 'pipe'] });
}

function checkedCommit(value) {
  if (!/^[0-9a-f]{40}(?:[0-9a-f]{24})?$/i.test(value)) throw new Error('Invalid commit ID.');
  git('cat-file', '-e', `${value}^{commit}`);
  return value;
}

export function auditRepository(policy, { base = '', head = '' } = {}) {
  validatePolicy(policy);
  head = checkedCommit(head || git('rev-parse', 'HEAD').toString().trim());
  const commits = new Set([head]);
  if (base && !/^0+$/.test(base)) {
    base = checkedCommit(base);
    for (const commit of git('rev-list', head, `^${base}`).toString().trim().split('\n').filter(Boolean)) {
      commits.add(commit);
    }
  } else if (base) {
    for (const commit of git('rev-list', head).toString().trim().split('\n').filter(Boolean)) commits.add(commit);
  }
  const seen = new Set();
  const blobSizes = new Map();
  const findings = [];
  for (const commit of commits) {
    for (const record of git('ls-tree', '-r', '-l', '-z', commit).toString('utf8').split('\0').filter(Boolean)) {
      const tab = record.indexOf('\t');
      const [mode, type, oid, bytes] = record.slice(0, tab).trim().split(/\s+/);
      if (type !== 'blob') continue;
      const path = record.slice(tab + 1);
      const key = JSON.stringify([path, oid]);
      if (seen.has(key)) continue;
      seen.add(key);
      let size = Number(bytes);
      // LFS pointers are tiny Git blobs but may reference large external files.
      if (size <= 1024 && mode !== '120000') {
        if (!blobSizes.has(oid)) {
          const content = git('cat-file', 'blob', oid).toString('utf8');
          const pointerSize = /^size (\d+)\r?$/m.exec(content);
          blobSizes.set(oid, /^version https:\/\/git-lfs.github.com\/spec\/v1\r?\n/.test(content) && pointerSize
            ? Number(pointerSize[1]) : size);
        }
        size = blobSizes.get(oid);
      }
      const reasons = inspectFile(path, size, policy);
      if (reasons.length) findings.push({ path, bytes: size, commit, reasons });
    }
  }
  return { commitsChecked: commits.size, findings };
}

const escapeAnnotation = value => value.replaceAll('%', '%25').replaceAll('\r', '%0D').replaceAll('\n', '%0A');

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    const policy = JSON.parse(readFileSync('.repo-audit.json', 'utf8'));
    const result = auditRepository(policy, { base: process.env.AUDIT_BASE, head: process.env.AUDIT_HEAD });
    for (const finding of result.findings) {
      const message = `${JSON.stringify(finding.path)}: ${finding.bytes} bytes; ${finding.reasons.join('; ')}; commit ${finding.commit.slice(0, 12)}`;
      console.log(process.env.GITHUB_ACTIONS ? `::error::${escapeAnnotation(message)}` : message);
    }
    console.log(`Repository audit: ${result.commitsChecked} commit snapshot(s), ${result.findings.length} finding(s).`);
    if (result.findings.length) {
      console.log('Remove unintended files from incoming commits or approve an exact exception in .repo-audit.json. See docs/repository-audit.md.');
      process.exitCode = 1;
    }
  } catch (error) {
    console.error(`Repository audit failed: ${error.message}`);
    process.exitCode = 1;
  }
}
