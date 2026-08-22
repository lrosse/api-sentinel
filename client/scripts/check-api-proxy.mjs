import { readdir, readFile } from 'node:fs/promises';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const clientRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const backendRoot = resolve(clientRoot, '..', 'src');
const proxyPath = join(clientRoot, 'proxy.conf.json');

const proxy = JSON.parse(await readFile(proxyPath, 'utf8'));
const backendFiles = await findCSharpFiles(backendRoot);
const requiredPrefixes = new Set();

for (const file of backendFiles) {
  const source = await readFile(file, 'utf8');
  const groupReceivers = new Set();
  const groupPattern = /var\s+(\w+)\s*=\s*\w+\s*\.\s*MapGroup\(\s*"([^"]+)"/g;
  const groupRoutePattern = /\.\s*MapGroup\(\s*"([^"]+)"/g;
  const directRoutePattern = /(\w+)\s*\.\s*Map(?:Get|Post|Put|Patch|Delete)\(\s*"([^"]+)"/g;

  for (const match of source.matchAll(groupPattern)) {
    groupReceivers.add(match[1]);
  }

  for (const match of source.matchAll(groupRoutePattern)) {
    addPrefix(requiredPrefixes, match[1]);
  }

  for (const match of source.matchAll(directRoutePattern)) {
    if (!groupReceivers.has(match[1])) {
      addPrefix(requiredPrefixes, match[2]);
    }
  }
}

const configuredPrefixes = new Set(Object.keys(proxy));
const missingPrefixes = [...requiredPrefixes]
  .filter((prefix) => !configuredPrefixes.has(prefix))
  .sort();

if (missingPrefixes.length > 0) {
  console.error(
    `proxy.conf.json não cobre os prefixos de API: ${missingPrefixes.join(', ')}. ` +
      'Todo novo endpoint público do backend precisa de uma regra no proxy Angular.',
  );
  process.exitCode = 1;
} else {
  console.log(
    `Proxy Angular cobre todos os prefixos de API detectados: ${[...requiredPrefixes].sort().join(', ')}`,
  );
}

function addPrefix(prefixes, route) {
  const firstSegment = route.match(/^\/[^/{]+/)?.[0];
  if (firstSegment) {
    prefixes.add(firstSegment);
  }
}

async function findCSharpFiles(directory) {
  const entries = await readdir(directory, { withFileTypes: true });
  const files = [];

  for (const entry of entries) {
    if (entry.name === 'bin' || entry.name === 'obj') {
      continue;
    }

    const path = join(directory, entry.name);
    if (entry.isDirectory()) {
      files.push(...(await findCSharpFiles(path)));
    } else if (entry.isFile() && entry.name.endsWith('.cs')) {
      files.push(path);
    }
  }

  return files;
}
