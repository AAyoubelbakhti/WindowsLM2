/**
 * Publishes a release folder to the Windows update feed in Vercel Blob, or lists the feed. Packages go first and indexes
 * last, with a short cache so that clients soon see the new version; then the previous version leaves, as the feed keeps
 * only the latest. Takes BLOB_READ_WRITE_TOKEN from the environment, from a file kept out of the repository.
 *
 *   pnpm --dir tools install
 *   node --env-file=<token file> tools/feed.mjs publish <folder> <version> <previous version>
 *   node --env-file=<token file> tools/feed.mjs show
 */
import { readFileSync, statSync } from 'node:fs';
import { join } from 'node:path';
import { del, list, put } from '@vercel/blob';

const FEED = 'updates/win/';
const INDEX_MAX_AGE_SECONDS = 60;
const [command, folder, version, previous] = process.argv.slice(2);

async function show() {
  const { blobs } = await list({ prefix: FEED });
  for (const blob of blobs) console.log(blob.pathname, blob.size);
}

async function publish() {
  for (const name of [`LinkManager-${version}-full.nupkg`, `LinkManager-${version}-delta.nupkg`]) {
    const path = join(folder, name);
    await put(FEED + name, readFileSync(path), {
      access: 'public',
      addRandomSuffix: false,
      allowOverwrite: true,
      multipart: statSync(path).size > 50_000_000,
    });
    console.log('uploaded', name);
  }
  for (const name of ['RELEASES', 'releases.win.json']) {
    await put(FEED + name, readFileSync(join(folder, name)), {
      access: 'public',
      addRandomSuffix: false,
      allowOverwrite: true,
      cacheControlMaxAge: INDEX_MAX_AGE_SECONDS,
    });
    console.log('uploaded', name);
  }
  const { blobs } = await list({ prefix: FEED });
  const stale = blobs.filter((blob) => blob.pathname.includes(`LinkManager-${previous}-`));
  for (const blob of stale) {
    await del(blob.url);
    console.log('removed', blob.pathname);
  }
  await show();
}

await (command === 'publish' ? publish() : show());
