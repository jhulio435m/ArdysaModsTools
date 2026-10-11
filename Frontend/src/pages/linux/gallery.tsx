// SPDX-License-Identifier: GPL-3.0-or-later
import { createRoot } from "react-dom/client";
import { setHostTransport } from "../../bridge/host";
import { App } from "../hero_gallery/App";
import { store } from "../hero_gallery/store";
import type { Hero, Selections } from "../hero_gallery/types";
import { api, connect } from "./api";
import locale from "../../../../Assets/Locales/es.json";
import "../../design/base.css";
import "./gallery.css";

type Selection = { set: string | null; items: string[]; base: string | null; prismatic: string | null; effects: string[] };
type Set = { assetPath: string; displayName: string; category: string; tag: string | null; thumbnailUrl: string | null;
   styleGroup: string | null; styleLabel: string | null; locks: string[] | null };
type Catalog = { id: string; name: string; attribute: string; sets: Set[] }[];
type State = { draft: Record<string, Selection>; favorites: string[] };
let catalog: Catalog = [];
let busy = false;
let saveTimer: ReturnType<typeof setTimeout> | undefined;
let pendingSave: Promise<unknown> = Promise.resolve();
const strings: Record<string, string> = { ...locale, "heroGallery.generate": "Aplicar selección", "heroGallery.tag": "Linux",
   "heroGallery.savePreset.title": "Exportar selección a un archivo", "heroGallery.loadPreset.title": "Importar selección",
   "linux.category.legacy": "Sets completos", "linux.category.custom": "Sets combinados", "linux.category.item": "Piezas", "linux.category.base": "Héroe base", "linux.category.prismatic": "Prismático", "common.save": "Exportar", "common.load": "Importar", "heroGallery.marquee": "Tus cambios se conservan entre sesiones" };
window.t = (key, vars) => Object.entries(vars ?? {}).reduce((value, [name, text]) => value.split(`{${name}}`).join(String(text)), strings[key] ?? key);
function error(e: unknown) {
   const message = e instanceof Error ? e.message : String(e);
   store.set({ status: message, alert: { visible: true, title: "No se pudo completar", message, type: "warning", hasLog: false } });
}
function paths(selections: Selections): Record<string, Selection> {
   const result: Record<string, Selection> = {};
   for (const [id, value] of Object.entries(selections)) {
      const hero = catalog.find(h => h.id === id);
      if (!hero) throw new Error("Héroe desconocido en la selección");
      const path = (index: number | null | undefined) => index == null ? null : hero.sets[index]?.assetPath ?? null;
      result[id] = { set: path(value.set), items: value.items.map(i => path(i)!).filter(Boolean), base: path(value.base),
         prismatic: path(value.prismatic), effects: (value.effects ?? []).map(i => path(i)!).filter(Boolean) };
   }
   return result;
}
function indices(values: Record<string, Selection>): Selections {
   const result: Selections = {};
   for (const [id, value] of Object.entries(values)) {
      const hero = catalog.find(h => h.id === id);
      if (!hero) continue;
      const idx = (path: string | null) => path ? hero.sets.findIndex(s => s.assetPath === path) : -1;
      const one = (path: string | null) => idx(path) < 0 ? null : idx(path);
      result[id] = { set: one(value.set), items: (value.items ?? []).map(idx).filter(i => i >= 0), base: one(value.base),
         prismatic: one(value.prismatic), effects: (value.effects ?? []).map(idx).filter(i => i >= 0) };
   }
   return result;
}
function save() {
   const snapshot = { selections: paths(store.get().selections), favorites: Array.from(store.get().favorites) };
   pendingSave = pendingSave.catch(() => undefined).then(() => api("gallery/save", snapshot));
   return pendingSave;
}
function autoSave() {
   if (busy) return;
   clearTimeout(saveTimer);
   saveTimer = setTimeout(() => { void save().catch(error); }, 600);
}
async function apply() {
   if (busy) return;
   busy = true;
   clearTimeout(saveTimer);
   store.set({ cachingVisible: true, generationBusy: true, status: "Preparando selección…", generationLogLines: [] });
   const poll = window.setInterval(() => {
      void api<{ lines: string[] }>("gallery/progress").then(p => store.set({ generationLogLines: p.lines, status: p.lines[p.lines.length - 1] ?? "Preparando selección…" })).catch(() => undefined);
   }, 1000);
   try {
      await pendingSave;
      const result = await api<{ message: string }>("gallery/apply", { selections: paths(store.get().selections), favorites: Array.from(store.get().favorites) });
      store.set({ status: result.message, alert: { visible: true, title: "Selección aplicada", message: result.message, type: "success", hasLog: true } });
   } catch (e) { error(e); }
   finally { window.clearInterval(poll); busy = false; store.set({ cachingVisible: false, generationBusy: false }); }
}
setHostTransport(message => {
   switch (message.type) {
      case "selectionChanged": case "favoritesChanged": autoSave(); break;
      case "cancel": if (busy) void api("gallery/cancel", {}).catch(error); break;
      case "generate": void apply(); break;
      case "close": window.location.assign("/"); break;
      case "alertPlay": window.location.assign("steam://rungameid/570"); break;
      case "savePreset": {
         const data = JSON.stringify({ version: 1, selections: paths(store.get().selections), favorites: Array.from(store.get().favorites) }, null, 2);
         const url = URL.createObjectURL(new Blob([data], { type: "application/json" }));
         const link = document.createElement("a"); link.href = url; link.download = "ardysa-seleccion.json"; link.click(); URL.revokeObjectURL(url); break;
      }
      case "loadPreset": {
         const input = document.createElement("input"); input.type = "file"; input.accept = ".json";
         input.onchange = () => { void (async () => {
            const file = input.files?.[0]; if (!file) return;
            if (file.size > 1024 * 1024) throw new Error("El archivo de selección es demasiado grande.");
            const value = JSON.parse(await file.text()) as { version: number; selections: Record<string, Selection>; favorites?: string[] };
            if (value.version !== 1 || typeof value.selections !== "object" || value.selections === null) throw new Error("Formato de selección no compatible.");
            await pendingSave;
            await api("gallery/save", value);
            store.set({ selections: indices(value.selections), favorites: new globalThis.Set(value.favorites ?? []), status: "Selección importada. Pulsa Aplicar selección para instalarla." });
         })().catch(error); }; input.click(); break;
      }
   }
});
createRoot(document.getElementById("root")!).render(<App linux />);
store.set({ cachingVisible: true, canApplyEmpty: true, status: "Cargando catálogo…", version: "Linux · experimental" });
void (async () => {
   await connect();
   const value = await api<{ heroes: Catalog; state: State }>("gallery"); catalog = value.heroes;
   const heroes: Hero[] = catalog.map(h => ({ id: h.id, name: h.name, displayName: h.name,
      attribute: ({ strength: "str", agility: "agi", intelligence: "int" } as Record<string, string>)[h.attribute] ?? h.attribute,
      thumbnail: `https://cdn.cloudflare.steamstatic.com/apps/dota2/images/dota_react/heroes/${h.id.replace("npc_dota_hero_", "")}.png`,
      sets: h.sets.map(s => ({ name: s.displayName, displayName: s.displayName, category: s.category as Hero["sets"][number]["category"],
         tag: s.tag, thumbnailUrl: s.thumbnailUrl, styleGroup: s.styleGroup, styleLabel: s.styleLabel, locks: s.locks ?? [] })) }));
   store.set({ heroes, selections: indices(value.state.draft), favorites: new globalThis.Set(value.state.favorites), cachingVisible: false,
      status: `${heroes.length} héroes · Selección guardada cargada` });
})().catch(e => { store.set({ cachingVisible: false }); error(e); });
