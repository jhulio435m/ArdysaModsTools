// SPDX-License-Identifier: GPL-3.0-or-later
import type { Plugin } from "vite";

// Keep the embedded Windows pages self-contained without micromatch/braces.
// Linux serves normal static assets and does not need this plugin.
export function inlinePage(): Plugin {
   return {
      name: "amt-inline-page",
      apply: "build",
      enforce: "post",
      config: () => ({ build: { rollupOptions: { output: { inlineDynamicImports: true } } } }),
      generateBundle(_options, bundle) {
         const inlined = new Set<string>();
         for (const asset of Object.values(bundle)) {
            if (asset.type !== "asset" || !asset.fileName.endsWith(".html")) continue;
            let html = String(asset.source);
            html = html.replace(/<script\b([^>]*?)\bsrc="([^"]+)"([^>]*)><\/script>/g,
               (tag: string, before: string, url: string, after: string) => {
                  const item = bundle[url.replace(/^\//, "")];
                  if (!item || item.type !== "chunk") return tag;
                  inlined.add(item.fileName);
                  return `<script${before}${after}>${item.code.replace(/<\/script/gi, "<\\/script")}</script>`;
               });
            html = html.replace(/<link\b[^>]*href="([^"]+)"[^>]*>/g, (tag: string, url: string) => {
               const item = bundle[url.replace(/^\//, "")];
               if (!item || item.type !== "asset" || !item.fileName.endsWith(".css")) return tag;
               inlined.add(item.fileName);
               return `<style>${String(item.source).replace(/<\/style/gi, "<\\/style")}</style>`;
            });
            asset.source = html;
         }
         for (const name of inlined) delete bundle[name];
      },
   };
}
