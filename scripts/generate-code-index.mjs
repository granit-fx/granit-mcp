/**
 * Generates a structured code index (code-index.json) from the Granit .NET source.
 *
 * Parses all public types and their public members from .cs files under src/,
 * plus the project dependency graph from .csproj files.
 *
 * Output is published as a GitHub Release asset and consumed by the granit-mcp Worker.
 *
 * Usage (from granit-dotnet repo root):
 *   node <path-to>/generate-code-index.mjs [--src ./src] [--out ./code-index.json]
 *
 * This script lives in granit-docs-mcp but runs against granit-dotnet source.
 * Copy it to granit-dotnet/docs-site/scripts/ or reference it directly.
 */

import { readFileSync, writeFileSync, readdirSync, statSync } from 'node:fs';
import { join, relative, basename, dirname, isAbsolute } from 'node:path';
import { fileURLToPath } from 'node:url';

const __dir = dirname(fileURLToPath(import.meta.url));

// ─── CLI args ─────────────────────────────────────────────────────────────────

function parseArgs() {
  const args = process.argv.slice(2);
  let srcRoot = join(process.cwd(), 'src');
  let output = join(process.cwd(), 'code-index.json');
  let repoRoot = process.cwd();

  for (let i = 0; i < args.length; i++) {
    if (args[i] === '--src' && args[i + 1]) { const v = args[++i]; srcRoot = isAbsolute(v) ? v : join(process.cwd(), v); }
    if (args[i] === '--out' && args[i + 1]) { const v = args[++i]; output = isAbsolute(v) ? v : join(process.cwd(), v); }
  }

  return { srcRoot, output, repoRoot };
}

const { srcRoot: SRC_ROOT, output: OUTPUT, repoRoot: REPO_ROOT } = parseArgs();

// ─── Walk helpers ─────────────────────────────────────────────────────────────

function walkDir(dir, ext) {
  const files = [];
  for (const entry of readdirSync(dir)) {
    if (entry === 'bin' || entry === 'obj' || entry === 'node_modules' || entry === 'bundles') continue;
    const full = join(dir, entry);
    const stat = statSync(full);
    if (stat.isDirectory()) {
      files.push(...walkDir(full, ext));
    } else if (full.endsWith(ext)) {
      files.push(full);
    }
  }
  return files;
}

// ─── Project graph from .csproj (regex-based XML parsing) ─────────────────────

function parseProjectGraph() {
  const csprojFiles = walkDir(SRC_ROOT, '.csproj');
  const projects = [];

  for (const file of csprojFiles) {
    const projectName = basename(file, '.csproj');
    if (projectName.includes('Analyzers') || projectName.includes('CodeFixes')) continue;
    if (projectName.includes('SourceGenerator')) continue;

    const xml = readFileSync(file, 'utf-8');

    // Extract TargetFramework(s)
    const fwMatch = xml.match(/<TargetFramework(?:s)?>(.*?)<\/TargetFramework(?:s)?>/);
    const framework = fwMatch ? fwMatch[1] : '';

    // Extract ProjectReference dependencies
    const deps = [];
    const refRe = /<ProjectReference\s+Include="([^"]+)"/g;
    let m;
    while ((m = refRe.exec(xml)) !== null) {
      // Normalize Windows backslashes before extracting basename
      const normalized = m[1].replaceAll('\\', '/');
      const depName = basename(normalized, '.csproj');
      if (depName) deps.push(depName);
    }

    projects.push({ name: projectName, deps, framework });
  }

  return projects.sort((a, b) => a.name.localeCompare(b.name));
}

// ─── C# public type + member extraction ───────────────────────────────────────

// Matches: public [modifiers] (class|interface|record|struct|enum) Name[<T>]
const TYPE_MODIFIERS = /(?:abstract|static|sealed|partial|readonly|ref)\s+/;
const TYPE_RE = new RegExp(
  String.raw`^[ \t]*public\s+(?:${TYPE_MODIFIERS.source})*(?<kind>class|interface|record|struct|enum)\s+(?<name>\w+)(?:<[^>]+>)?`,
  'gm',
);

function extractSymbols(csFile, projectName) {
  const content = readFileSync(csFile, 'utf-8');
  const relFile = relative(REPO_ROOT, csFile).replaceAll('\\', '/');

  const nsMatch = content.match(/^namespace\s+([\w.]+)/m);
  const namespace = nsMatch ? nsMatch[1] : projectName;

  const symbols = [];

  for (const match of content.matchAll(TYPE_RE)) {
    const kind = match.groups.kind;
    const name = match.groups.name;
    const fqn = `${namespace}.${name}`;
    const lineIdx = content.substring(0, match.index).split('\n').length;

    const members = extractMembers(content, match.index, kind);

    symbols.push({
      name,
      fqn,
      kind,
      project: projectName,
      file: relFile,
      line: lineIdx,
      members,
    });
  }

  return symbols;
}

function isSkippableLine(line) {
  const prefixes = ['//', '///', '*', '#', '['];
  return !line || prefixes.some((p) => line.startsWith(p));
}

function isSignatureComplete(pendingLine) {
  const trimmed = pendingLine.trimEnd();
  if (trimmed.endsWith(';') || trimmed.endsWith('{') ||
      trimmed.includes('=>') || trimmed.endsWith(')')) {
    return true;
  }
  const openParens = (pendingLine.match(/\(/g) || []).length;
  const closeParens = (pendingLine.match(/\)/g) || []).length;
  return openParens > 0 && openParens === closeParens;
}

function isPublicMember(fullLine, typeKind) {
  if (fullLine.startsWith('public ')) return true;
  if (typeKind !== 'interface') return false;
  return !fullLine.startsWith('private ') && !fullLine.startsWith('protected ');
}

function countBraceDepth(line, depth) {
  for (const ch of line) {
    if (ch === '{') depth++;
    if (ch === '}') depth--;
  }
  return depth;
}

function tryCompleteMember(pendingLine, typeKind) {
  const fullLine = pendingLine.replaceAll(/\s+/g, ' ').trim();
  if (!isPublicMember(fullLine, typeKind)) return null;
  return parseMemberLine(fullLine, typeKind);
}

function extractMembers(content, typeStartOffset, typeKind) {
  const members = [];

  const afterType = content.substring(typeStartOffset);
  const braceIdx = afterType.indexOf('{');
  if (braceIdx === -1) return members;

  const bodyStart = typeStartOffset + braceIdx + 1;
  const lines = content.substring(bodyStart).split('\n');

  let depth = 1;
  let pendingLine = '';

  for (const rawLine of lines) {
    depth = countBraceDepth(rawLine, depth);

    if (depth <= 0) break;
    if (depth !== 1) { pendingLine = ''; continue; }

    const line = rawLine.trim();
    if (isSkippableLine(line)) continue;

    pendingLine = pendingLine ? `${pendingLine} ${line}` : line;
    if (!isSignatureComplete(pendingLine)) continue;

    const member = tryCompleteMember(pendingLine, typeKind);
    if (member) members.push(member);
    pendingLine = '';
  }

  return members;
}

// ─── String-based parsing helpers (avoid ReDoS-vulnerable regexes) ───────────

function isWordChar(ch) {
  return (ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z') || (ch >= '0' && ch <= '9') || ch === '_';
}

/** Split "returnType name" by the last word → [prefix, word] or null. */
function splitLastWord(str) {
  const t = str.trimEnd();
  let i = t.length - 1;
  while (i >= 0 && isWordChar(t[i])) i--;
  if (i < 0 || i === t.length - 1) return null;
  const word = t.substring(i + 1);
  const before = t.substring(0, i + 1).trimEnd();
  return before ? [before, word] : null;
}

/** Truncate at the first occurrence of any char in `chars`, trimming trailing whitespace. */
function truncateAt(str, chars) {
  for (let i = 0; i < str.length; i++) {
    if (chars.includes(str[i])) return str.substring(0, i).trimEnd();
  }
  return str;
}

/** Find index of first char in `chars` within `str`, or -1. */
function indexOfAny(str, chars) {
  for (let i = 0; i < str.length; i++) {
    if (chars.includes(str[i])) return i;
  }
  return -1;
}

/** Strip leading "public " prefix (with any trailing whitespace). */
function stripPublicPrefix(str) {
  if (!str.startsWith('public ')) return str;
  let i = 7;
  while (i < str.length && (str[i] === ' ' || str[i] === '\t')) i++;
  return str.substring(i);
}

function tryParseEvent(cleanLine) {
  if (!cleanLine.includes(' event ')) return null;
  const idx = cleanLine.indexOf(' event ') + 7;
  const afterEvent = cleanLine.substring(idx).trim();
  const spaceIdx = afterEvent.indexOf(' ');
  if (spaceIdx === -1) return null;
  const rest = afterEvent.substring(spaceIdx + 1).trim();
  // Extract event name (first word)
  let end = 0;
  while (end < rest.length && isWordChar(rest[end])) end++;
  if (end === 0) return null;
  return {
    name: rest.substring(0, end),
    kind: 'event',
    signature: stripPublicPrefix(truncateAt(cleanLine, '{;')),
  };
}

function tryParseMethod(noAccess) {
  if (!noAccess.includes('(')) return null;
  const parenIdx = noAccess.indexOf('(');
  if (parenIdx === -1) return null;
  const beforeParen = noAccess.substring(0, parenIdx).trimEnd();
  const parts = splitLastWord(beforeParen);
  if (!parts) return null;
  const [retType, name] = parts;
  // Parse optional generics and params
  const after = noAccess.substring(parenIdx);
  let generics = '';
  let searchFrom = 0;
  if (after.startsWith('<')) {
    const gtIdx = after.indexOf('>');
    if (gtIdx === -1) return null;
    generics = after.substring(0, gtIdx + 1);
    searchFrom = gtIdx + 1;
  }
  const openIdx = after.indexOf('(', searchFrom);
  if (openIdx === -1) return null;
  const closeIdx = after.indexOf(')', openIdx);
  if (closeIdx === -1) return null;
  const params = after.substring(openIdx + 1, closeIdx).trim();
  return { name, kind: 'method', signature: `${name}${generics}(${params})`, returnType: retType };
}

function tryParseProperty(line, noAccess, typeKind) {
  // Detect get/set accessors or arrow expression
  const braceIdx = line.indexOf('{');
  let isGetSet = line.includes('=>');
  if (!isGetSet && braceIdx !== -1) {
    const afterBrace = line.substring(braceIdx + 1).trimStart();
    isGetSet = afterBrace.startsWith('get') || afterBrace.startsWith('set');
  }
  const isInterfaceProp = typeKind === 'interface' && line.includes(';') && line.includes('}');
  if (!isGetSet && !isInterfaceProp) return null;

  // Find the property name (last identifier before { or =)
  const charIdx = indexOfAny(noAccess, '{=');
  if (charIdx === -1) return null;
  const propSig = noAccess.substring(0, charIdx).trimEnd();
  const propParts = splitLastWord(propSig);
  if (!propParts) return null;
  const [retType, name] = propParts;
  return { name, kind: 'property', signature: `${retType} ${name}`, returnType: retType };
}

function parseMemberLine(line, typeKind) {
  if (line.startsWith('[') || line.startsWith('//')) return null;
  if (/^public\s+(?:class|interface|struct|record|enum)\s/.test(line)) return null;

  const cleanLine = typeKind === 'interface' && !line.startsWith('public ')
    ? `public ${line}`
    : line;

  const noAccess = cleanLine
    .replace(/^public\s+/, '')
    .replaceAll(/(?:new|virtual|abstract|override|static|async|readonly)\s+/g, '');

  return tryParseEvent(cleanLine)
    ?? tryParseMethod(noAccess)
    ?? tryParseProperty(line, noAccess, typeKind);
}

// ─── Main ─────────────────────────────────────────────────────────────────────

console.log('Parsing project graph...');
const projectGraph = parseProjectGraph();
console.log(`  Found ${projectGraph.length} projects`);

console.log('Extracting public symbols...');
const csFiles = walkDir(SRC_ROOT, '.cs');

const sourceFiles = csFiles.filter(f => {
  const rel = relative(REPO_ROOT, f);
  if (rel.includes('/obj/') || rel.includes('/bin/')) return false;
  if (f.endsWith('.g.cs') || f.endsWith('.generated.cs')) return false;
  return true;
});

console.log(`  Scanning ${sourceFiles.length} .cs files...`);

const allSymbols = [];
for (const file of sourceFiles) {
  const rel = relative(SRC_ROOT, file);
  const projectName = rel.split('/')[0];
  const symbols = extractSymbols(file, projectName);
  allSymbols.push(...symbols);
}

// Deduplicate by fqn (partial classes)
const byFqn = new Map();
for (const sym of allSymbols) {
  const existing = byFqn.get(sym.fqn);
  if (existing) {
    existing.members.push(...sym.members);
  } else {
    byFqn.set(sym.fqn, sym);
  }
}

const symbols = [...byFqn.values()].sort((a, b) => a.fqn.localeCompare(b.fqn));

const index = {
  generatedAt: new Date().toISOString(),
  repo: 'granit-dotnet',
  projectGraph,
  symbols,
};

writeFileSync(OUTPUT, JSON.stringify(index, null, 2), 'utf-8');

const sizeKb = (JSON.stringify(index).length / 1024).toFixed(0);
const totalMembers = symbols.reduce((n, s) => n + s.members.length, 0);
console.log(`\n✓ code-index.json generated:`);
console.log(`  ${symbols.length} types, ${totalMembers} members`);
console.log(`  ${projectGraph.length} projects in dependency graph`);
console.log(`  ${sizeKb} KB`);
