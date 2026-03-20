import type { KVCache, IndexEntry } from '../lib/index-cache.js';
import { getSearchIndex } from '../lib/index-cache.js';

export interface ModuleRefInput {
  module: string;
}

const DOCS_BASE = 'https://granit-fx.dev';

export async function handleModuleRef(input: ModuleRefInput, indexUrl: string, cache: KVCache): Promise<string> {
  const entries = await getSearchIndex(indexUrl, cache);
  const match = findModule(entries, input.module);

  if (!match) {
    const modules = entries
      .filter((e) => e.category === 'module')
      .map((e) => e.title)
      .slice(0, 20);

    return (
      `Module "${input.module}" not found in the documentation.\n\n` +
      `**Available modules (sample):** ${modules.join(', ')}\n\n` +
      'Tip: use `search_granit_docs` with the module name to find it.'
    );
  }

  return (
    `## ${match.title}\n` +
    `**URL:** ${DOCS_BASE}${match.url}\n` +
    (match.description ? `> ${match.description}\n\n` : '\n') +
    match.content
  );
}

/** Extracts the URL slug (last path segment) from a doc URL. */
function urlSlug(url: string): string {
  return url.replace(/\/$/, '').split('/').pop() ?? '';
}

/** Strips all non-alphanumeric for fuzzy comparison. */
function alphaOnly(s: string): string {
  return s.toLowerCase().replaceAll(/[^a-z0-9]/g, '');
}

/**
 * Converts input to kebab-case slug form: "BlobStorage" → "blob-storage",
 * "multi-tenancy" → "multi-tenancy", "Granit.Caching" → "caching".
 */
function toSlug(input: string): string {
  let slug = input
    .replace(/^granit\.?/i, '')
    .replaceAll(/([a-z])([A-Z])/g, '$1-$2') // camelCase → kebab
    .toLowerCase()
    .replaceAll(/[\s_.]+/g, '-');
  // Trim leading and trailing dashes without ReDoS-prone regex
  let start = 0;
  while (start < slug.length && slug[start] === '-') start++;
  let end = slug.length;
  while (end > start && slug[end - 1] === '-') end--;
  return slug.substring(start, end);
}

/** Extract main title before em-dash/en-dash/hyphen separator (e.g. "Module — Details" → "Module"). */
function splitAtDash(title: string): string {
  const separators = [' \u2014 ', ' \u2013 ', ' - '];
  for (const sep of separators) {
    const idx = title.indexOf(sep);
    if (idx !== -1) return title.substring(0, idx);
  }
  return title;
}

function slugRank(candidate: string, target: string): number {
  if (candidate === target) return 0;
  if (candidate.startsWith(target)) return 1;
  return 2;
}

function findModule(entries: IndexEntry[], rawInput: string): IndexEntry | undefined {
  const modules = entries.filter((e) => e.category === 'module');
  const slug = toSlug(rawInput);
  const alpha = alphaOnly(rawInput.replace(/^granit\.?/i, ''));

  // 1. Exact URL slug match (most reliable — slug IS the canonical name)
  const bySlug = modules.find((e) => urlSlug(e.url) === slug);
  if (bySlug) return bySlug;

  // 2. Exact title match — compare alpha-only against main title (before em-dash)
  const exact = modules.find((e) => {
    const mainTitle = splitAtDash(e.title);
    return alphaOnly(mainTitle) === alpha;
  });
  if (exact) return exact;

  // 3. Partial match — prefer slug starts-with, then shortest title
  const partials = modules
    .filter((e) => urlSlug(e.url).includes(slug) || alphaOnly(e.title).includes(alpha))
    .sort((a, b) => {
      // Prefer exact slug prefix over partial
      const aSlug = slugRank(urlSlug(a.url), slug);
      const bSlug = slugRank(urlSlug(b.url), slug);
      if (aSlug !== bSlug) return aSlug - bSlug;
      return a.title.length - b.title.length;
    });
  if (partials.length > 0) return partials[0];

  return undefined;
}
