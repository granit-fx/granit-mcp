/**
 * Generates a structured front-end code index (front-index.json) from
 * the Granit TypeScript monorepo (granit-front).
 *
 * Parses all public exports from each @granit/* package by reading
 * the src/index.ts entry points and extracting exported declarations.
 *
 * Usage (from granit-front repo root):
 *   node <path-to>/generate-front-index.mjs [--root .] [--out ./front-index.json]
 *
 * This script lives in granit-mcp but runs against granit-front source.
 */

import { readFileSync, writeFileSync, readdirSync, statSync, existsSync } from 'node:fs';
import { join, dirname, isAbsolute } from 'node:path';
import { fileURLToPath } from 'node:url';

const __dir = dirname(fileURLToPath(import.meta.url));

// ─── CLI args ─────────────────────────────────────────────────────────────────

function parseArgs() {
  const args = process.argv.slice(2);
  let root = process.cwd();
  let output = join(process.cwd(), 'front-index.json');

  for (let i = 0; i < args.length; i++) {
    if (args[i] === '--root' && args[i + 1]) {
      const v = args[++i];
      root = isAbsolute(v) ? v : join(process.cwd(), v);
    }
    if (args[i] === '--out' && args[i + 1]) {
      const v = args[++i];
      output = isAbsolute(v) ? v : join(process.cwd(), v);
    }
  }

  return { root, output };
}

const { root: ROOT, output: OUTPUT } = parseArgs();
const PACKAGES_DIR = join(ROOT, 'packages/@granit');

// ─── Package discovery ────────────────────────────────────────────────────────

function discoverPackages() {
  if (!existsSync(PACKAGES_DIR)) {
    console.error(`Packages directory not found: ${PACKAGES_DIR}`);
    process.exit(1);
  }

  const entries = readdirSync(PACKAGES_DIR);
  const packages = [];

  for (const entry of entries) {
    const pkgDir = join(PACKAGES_DIR, entry);
    if (!statSync(pkgDir).isDirectory()) continue;

    const pkgJsonPath = join(pkgDir, 'package.json');
    if (!existsSync(pkgJsonPath)) continue;

    const pkgJson = JSON.parse(readFileSync(pkgJsonPath, 'utf-8'));
    const name = pkgJson.name || `@granit/${entry}`;
    const description = pkgJson.description || '';

    // Find entry point
    const indexPath = join(pkgDir, 'src', 'index.ts');
    if (!existsSync(indexPath)) continue;

    packages.push({ name, description, dir: pkgDir, indexPath });
  }

  return packages.sort((a, b) => a.name.localeCompare(b.name));
}

// ─── TypeScript export extraction ─────────────────────────────────────────────

function extractExports(indexPath, pkgDir) {
  const content = readFileSync(indexPath, 'utf-8');
  const exports = [];

  // Process direct exports and re-exports from other files
  exports.push(
    ...extractDirectExports(content),
    ...extractReExports(content, pkgDir),
  );

  // Deduplicate by name
  const seen = new Set();
  return exports.filter((e) => {
    if (seen.has(e.name)) return false;
    seen.add(e.name);
    return true;
  });
}

function extractDirectExports(content) {
  const exports = [];
  const lines = content.split('\n');

  for (let i = 0; i < lines.length; i++) {
    const line = lines[i].trim();

    // export interface Foo { ... }
    const ifaceMatch = line.match(/^export\s+interface\s+(\w+)(?:<[^>]+>)?/);
    if (ifaceMatch) {
      const members = extractInterfaceMembers(lines, i);
      exports.push({
        name: ifaceMatch[1],
        kind: 'interface',
        signature: line.replace(/\s*\{.*$/, '').replace(/^export\s+/, ''),
        members,
      });
      continue;
    }

    // export type Foo = ...
    const typeMatch = line.match(/^export\s+type\s+(\w+)(?:<[^>]+>)?/);
    if (typeMatch) {
      const sig = collectSignature(lines, i).replace(/^export\s+/, '');
      exports.push({ name: typeMatch[1], kind: 'type', signature: sig });
      continue;
    }

    // export function foo(...)
    const funcMatch = line.match(/^export\s+(?:async\s+)?function\s+(\w+)/);
    if (funcMatch) {
      const sig = collectSignature(lines, i)
        .replace(/^export\s+/, '')
        .replace(/\s*\{.*$/, '');
      exports.push({ name: funcMatch[1], kind: 'function', signature: sig });
      continue;
    }

    // export class Foo
    const classMatch = line.match(/^export\s+(?:abstract\s+)?class\s+(\w+)/);
    if (classMatch) {
      exports.push({
        name: classMatch[1],
        kind: 'class',
        signature: line.replace(/\s*\{.*$/, '').replace(/^export\s+/, ''),
      });
      continue;
    }

    // export enum Foo
    const enumMatch = line.match(/^export\s+enum\s+(\w+)/);
    if (enumMatch) {
      exports.push({
        name: enumMatch[1],
        kind: 'enum',
        signature: `enum ${enumMatch[1]}`,
      });
      continue;
    }

    // export const foo = ...
    const constMatch = line.match(/^export\s+const\s+(\w+)/);
    if (constMatch) {
      const sig = collectSignature(lines, i)
        .replace(/^export\s+/, '')
        .replace(/\s*=.*$/, '');
      exports.push({ name: constMatch[1], kind: 'const', signature: sig });
    }
  }

  return exports;
}

function extractReExports(content, pkgDir) {
  const exports = [];
  const lines = content.split('\n');

  for (const line of lines) {
    const trimmed = line.trim();

    // export { Foo, Bar } from './module'
    const namedReExport = trimmed.match(/^export\s+\{([^}]+)\}\s+from\s+['"]([^'"]+)['"]/);
    if (namedReExport) {
      exports.push(...resolveAndExtractNames(namedReExport[1], namedReExport[2], pkgDir, true));
      continue;
    }

    // export type { Foo } from './module'
    const typeReExport = trimmed.match(/^export\s+type\s+\{([^}]+)\}\s+from\s+['"]([^'"]+)['"]/);
    if (typeReExport) {
      exports.push(...resolveAndExtractNames(typeReExport[1], typeReExport[2], pkgDir, false));
    }
  }

  return exports;
}

/**
 * Resolves a module path and extracts named exports from it.
 * @param {string} rawNames - Comma-separated export names (e.g. "Foo, Bar as Baz")
 * @param {string} modulePath - Relative module path
 * @param {string} pkgDir - Package directory
 * @param {boolean} stripTypePrefix - Whether to strip "type " prefix from names
 */
function resolveAndExtractNames(rawNames, modulePath, pkgDir, stripTypePrefix) {
  const resolvedFile = resolveModule(pkgDir, modulePath);
  if (!resolvedFile) return [];

  const moduleContent = readFileSync(resolvedFile, 'utf-8');
  const names = rawNames.split(',').map((n) => n.trim());
  const results = [];

  for (const raw of names) {
    const cleaned = stripTypePrefix ? raw.replace(/^type\s+/, '') : raw;
    const { originalName, exportedName } = parseAliasedName(cleaned);

    const found = findExportInContent(moduleContent, originalName);
    if (found) {
      results.push({ ...found, name: exportedName });
    }
  }

  return results;
}

/**
 * Parses a potentially aliased name like "Foo as Bar" into original and exported names.
 */
function parseAliasedName(name) {
  const alias = name.match(/(\w+)\s+as\s+(\w+)/);
  return {
    originalName: alias ? alias[1] : name,
    exportedName: alias ? alias[2] : name,
  };
}

function findExportInContent(content, name) {
  const lines = content.split('\n');
  for (let i = 0; i < lines.length; i++) {
    const line = lines[i].trim();

    if (line.match(new RegExp(String.raw`^export\s+interface\s+${name}\b`))) {
      const members = extractInterfaceMembers(lines, i);
      return {
        name,
        kind: 'interface',
        signature: line.replace(/\s*\{.*$/, '').replace(/^export\s+/, ''),
        members,
      };
    }
    if (line.match(new RegExp(String.raw`^export\s+type\s+${name}\b`))) {
      const sig = collectSignature(lines, i).replace(/^export\s+/, '');
      return { name, kind: 'type', signature: sig };
    }
    if (line.match(new RegExp(String.raw`^export\s+(?:async\s+)?function\s+${name}\b`))) {
      const sig = collectSignature(lines, i)
        .replace(/^export\s+/, '')
        .replace(/\s*\{.*$/, '');
      return { name, kind: 'function', signature: sig };
    }
    if (line.match(new RegExp(String.raw`^export\s+(?:abstract\s+)?class\s+${name}\b`))) {
      return {
        name,
        kind: 'class',
        signature: line.replace(/\s*\{.*$/, '').replace(/^export\s+/, ''),
      };
    }
    if (line.match(new RegExp(String.raw`^export\s+enum\s+${name}\b`))) {
      return { name, kind: 'enum', signature: `enum ${name}` };
    }
    if (line.match(new RegExp(String.raw`^export\s+const\s+${name}\b`))) {
      const sig = collectSignature(lines, i)
        .replace(/^export\s+/, '')
        .replace(/\s*=.*$/, '');
      return { name, kind: 'const', signature: sig };
    }
  }
  return null;
}

// ─── Helpers ──────────────────────────────────────────────────────────────────

function resolveModule(pkgDir, modulePath) {
  const srcDir = join(pkgDir, 'src');
  // Strip .js/.mjs extension (TS source uses .js in imports for ESM compat)
  const cleaned = modulePath.replace(/^\.\//, '').replace(/\.m?js$/, '');
  const base = join(srcDir, cleaned);

  const candidates = [
    `${base}.ts`,
    `${base}.tsx`,
    join(base, 'index.ts'),
  ];

  for (const candidate of candidates) {
    if (existsSync(candidate)) return candidate;
  }
  return null;
}

function collectSignature(lines, startIdx) {
  let sig = lines[startIdx].trim();
  let depth = 0;

  // Count opening/closing for multi-line signatures
  for (const ch of sig) {
    if (ch === '(' || ch === '<' || ch === '{') depth++;
    if (ch === ')' || ch === '>' || ch === '}') depth--;
  }

  let j = startIdx + 1;
  while (depth > 0 && j < lines.length) {
    const nextLine = lines[j].trim();
    sig += ' ' + nextLine;
    for (const ch of nextLine) {
      if (ch === '(' || ch === '<' || ch === '{') depth++;
      if (ch === ')' || ch === '>' || ch === '}') depth--;
    }
    j++;
  }

  // Clean up: collapse whitespace, trim trailing semicolons
  return sig.replaceAll(/\s+/g, ' ').replace(/;$/, '').trim();
}

function extractInterfaceMembers(lines, startIdx) {
  const members = [];
  let depth = 0;
  let started = false;

  for (let i = startIdx; i < lines.length; i++) {
    const line = lines[i];
    ({ depth, started } = updateBraceDepth(line, depth, started));

    if (started && depth === 0) break;
    if (depth !== 1 || i === startIdx) continue;

    const member = parseMemberLine(lines, i);
    if (member) members.push(member);
  }

  return members;
}

/**
 * Updates brace depth tracking for interface body parsing.
 */
function updateBraceDepth(line, depth, started) {
  for (const ch of line) {
    if (ch === '{') { depth++; started = true; }
    if (ch === '}') depth--;
  }
  return { depth, started };
}

/**
 * Attempts to parse a single interface member line, returning null if the line
 * is empty, a comment, or not a recognized member pattern.
 */
function parseMemberLine(lines, idx) {
  const trimmed = lines[idx].trim();
  if (!trimmed || trimmed.startsWith('//') || trimmed.startsWith('/*') || trimmed.startsWith('*')) {
    return null;
  }

  // Parse member: name: type or name(...): type
  const memberMatch = trimmed.match(/^(\w+)(\??):\s*(.+?)[\s;]*$/);
  if (memberMatch) {
    const mName = memberMatch[1];
    const optional = memberMatch[2];
    const mType = memberMatch[3].replace(/;$/, '').trim();
    return {
      name: mName,
      kind: mType.includes('=>') || mType.startsWith('(') ? 'method' : 'property',
      signature: `${mName}${optional}: ${mType}`,
    };
  }

  // Method: name(...): ReturnType
  const methodMatch = trimmed.match(/^(\w+)\s*\(/);
  if (methodMatch) {
    const sig = collectSignature(lines, idx).replace(/;$/, '').trim();
    return {
      name: methodMatch[1],
      kind: 'method',
      signature: sig,
    };
  }

  return null;
}

// ─── Main ─────────────────────────────────────────────────────────────────────

console.log('Discovering @granit/* packages...');
const packages = discoverPackages();
console.log(`  Found ${packages.length} packages`);

console.log('Extracting exports...');
const result = [];

for (const pkg of packages) {
  const exports = extractExports(pkg.indexPath, pkg.dir);
  result.push({
    name: pkg.name,
    description: pkg.description,
    exports,
  });

  if (exports.length > 0) {
    console.log(`  ${pkg.name}: ${exports.length} exports`);
  }
}

const index = {
  generatedAt: new Date().toISOString(),
  repo: 'granit-front',
  packages: result,
};

writeFileSync(OUTPUT, JSON.stringify(index, null, 2), 'utf-8');

const totalExports = result.reduce((n, p) => n + p.exports.length, 0);
const totalMembers = result.reduce(
  (n, p) => n + p.exports.reduce((m, e) => m + (e.members?.length || 0), 0),
  0,
);
const sizeKb = (JSON.stringify(index).length / 1024).toFixed(0);

console.log(`\n✓ front-index.json generated:`);
console.log(`  ${packages.length} packages, ${totalExports} exports, ${totalMembers} members`);
console.log(`  ${sizeKb} KB`);
