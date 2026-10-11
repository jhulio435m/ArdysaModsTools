// SPDX-License-Identifier: GPL-3.0-or-later
import test from 'node:test';
import assert from 'node:assert/strict';
import { build } from 'esbuild';
import { mkdtemp, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { pathToFileURL } from 'node:url';

test('Choosing a style repeatedly keeps it selected; explicit deselect clears it', async () => {
  const folder = await mkdtemp(join(tmpdir(), 'ardysa-gallery-state-'));
  const output = join(folder, 'store.mjs');
  globalThis.window = { addEventListener() {}, chrome: { webview: { postMessage() {} } } };
  try {
    await build({ entryPoints: ['src/pages/hero_gallery/store.ts'], outfile: output, bundle: true, platform: 'node', format: 'esm' });
    const { store, openStylePreview, selectStyleFromPreview, deselectStylePreview } = await import(pathToFileURL(output).href);
    const hero = { id: 'hero', name: 'Hero', thumbnail: '', sets: [
      { name: 'Base style 1', category: 'basehero', styleGroup: 'base' },
      { name: 'Base style 2', category: 'basehero', styleGroup: 'base' },
    ] };
    store.set({ heroes: [hero], selections: { hero: { set: null, items: [], base: 0, prismatic: null } } });
    openStylePreview(hero, 'base', 'base');
    selectStyleFromPreview(hero, 1);
    selectStyleFromPreview(hero, 1);
    assert.equal(store.get().selections.hero.base, 1);
    deselectStylePreview();
    assert.equal(store.get().selections.hero?.base ?? null, null);
  } finally { await rm(folder, { recursive: true, force: true }); delete globalThis.window; }
});
