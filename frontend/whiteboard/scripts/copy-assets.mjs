import { cp, mkdir, readdir, readFile, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = fileURLToPath(new URL('../', import.meta.url));
const output = path.resolve(root, '../../src/Lms.Poc/wwwroot/vendor/whiteboard');
await cp(path.join(root, 'node_modules/@excalidraw/excalidraw/dist/prod/fonts'), path.join(output, 'fonts'), { recursive: true });

// Preserve installed dependency notices alongside the redistributable bundle.
const notices = [];
async function visit(directory) {
  for (const entry of await readdir(directory, { withFileTypes: true })) {
    const location = path.join(directory, entry.name);
    if (entry.isDirectory()) await visit(location);
    else if (/^(licen[sc]e|copying|notice)(\.|$|-)/i.test(entry.name)) {
      const relative = path.relative(path.join(root, 'node_modules'), location).replaceAll('\\', '/');
      notices.push(`\n===== ${relative} =====\n${await readFile(location, 'utf8')}`);
    }
  }
}
await visit(path.join(root, 'node_modules'));
await mkdir(output, { recursive: true });
await writeFile(path.join(output, 'THIRD-PARTY-NOTICES.txt'), notices.sort().join('\n'));
console.log('Local fonts and dependency notices copied.');
