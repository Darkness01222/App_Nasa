import { mkdir, readFile, writeFile, copyFile, cp } from 'node:fs/promises';
import { brotliDecompressSync } from 'node:zlib';

const root = new URL('../', import.meta.url);
const output = new URL('dist/', root);
await mkdir(output, { recursive: true });

// Pages serves ordinary static files: decompress Unity assets at build time.
for (const name of ['Builds.data', 'Builds.framework.js', 'Builds.wasm']) {
  const bytes = brotliDecompressSync(await readFile(new URL(`${name}.br`, root)));
  await writeFile(new URL(name, output), bytes);
  console.log(`${name}: ${bytes.length} bytes`);
}
await copyFile(new URL('Builds.loader.js', root), new URL('Builds.loader.js', output));
for (const folder of ['TemplateData', 'StreamingAssets']) {
  await cp(new URL(folder, root), new URL(folder, output), { recursive: true });
}
let html = await readFile(new URL('index.html', root), 'utf8');
for (const name of ['Builds.data', 'Builds.framework.js', 'Builds.wasm']) {
  html = html.replaceAll(`${name}.br`, name);
}
await writeFile(new URL('index.html', output), html);
await writeFile(new URL('.nojekyll', output), '');
console.log('GitHub Pages site ready in dist/');
