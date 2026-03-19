import type { KVCache } from '../lib/index-cache.js';
import { getPackageInfo } from '../lib/nuget.js';

export interface PackageInfoInput {
  package: string;
  version?: string;
}

export async function handlePackageInfo(input: PackageInfoInput, cache: KVCache): Promise<string> {
  const info = await getPackageInfo(input.package, cache);

  if (!info) {
    return (
      `Package "${input.package}" not found on NuGet.\n\n` +
      'Tip: use `list_packages` to see all available Granit packages.'
    );
  }

  if (input.version) {
    const match = info.versions.find((v) => v.version === input.version);
    if (!match) {
      const available = info.versions
        .filter((v) => v.listed)
        .slice(-10)
        .map((v) => v.version)
        .join(', ');
      return `Version "${input.version}" not found for ${info.id}.\n\n**Recent versions:** ${available}`;
    }
  }

  const displayVersion = input.version ?? info.latestVersion;

  const lines: string[] = [
    `## ${info.id} v${displayVersion}`,
    '',
    info.description ? `> ${info.description}` : '',
    '',
  ];

  lines.push(
    ...formatMetadata(info),
    ...formatDependencyGroups(info.dependencyGroups),
    ...formatVersionHistory(info.versions),
  );

  return lines.filter((l) => l !== undefined).join('\n');
}

function formatMetadata(info: import('../lib/nuget.js').PackageInfo): string[] {
  const lines: string[] = [`**Authors:** ${info.authors}`];
  if (info.license) lines.push(`**License:** ${info.license}`);
  if (info.projectUrl) lines.push(`**Project:** ${info.projectUrl}`);
  if (info.tags.length > 0) lines.push(`**Tags:** ${info.tags.join(', ')}`);
  lines.push('');
  return lines;
}

function formatDependencyGroups(
  groups: import('../lib/nuget.js').PackageInfo['dependencyGroups'],
): string[] {
  if (groups.length === 0) return [];

  const lines: string[] = ['### Dependencies', ''];
  for (const group of groups) {
    lines.push(`**${group.framework}**`);
    const depLines = group.dependencies.length === 0
      ? ['- *(none)*']
      : group.dependencies.map((dep) => `- ${dep.id} ${dep.range}`);
    lines.push(...depLines, '');
  }
  return lines;
}

function formatVersionHistory(
  versions: import('../lib/nuget.js').PackageVersion[],
): string[] {
  const listedVersions = versions.filter((v) => v.listed);
  const recentVersions = listedVersions.slice(-10).reverse();
  if (recentVersions.length === 0) return [];

  const lines: string[] = ['### Recent versions', ''];
  const versionLines = recentVersions.map((v) => {
    const date = v.published ? ` (${v.published.split('T')[0]})` : '';
    return `- v${v.version}${date}`;
  });
  lines.push(...versionLines);
  if (listedVersions.length > 10) {
    lines.push(`- *… and ${listedVersions.length - 10} earlier versions*`);
  }
  return lines;
}
