// SPDX-License-Identifier: GPL-3.0-or-later
import { useEffect, useRef, useState, type FormEvent } from "react";
import { api, connect, type Installation, type Preferences, type Status } from "./api";

type Tab = "mods" | "vpk" | "generate" | "download" | "performance";
const tabs: { id: Tab; label: string }[] = [
   { id: "mods", label: "Mods" }, { id: "vpk", label: "Archivos VPK" },
   { id: "generate", label: "Generar selección" }, { id: "download", label: "Descargas" },
   { id: "performance", label: "Límites de FPS" },
];

export default function App() {
   const [tab, setTab] = useState<Tab>("mods");
   const [installations, setInstallations] = useState<Installation[]>([]);
   const [preferences, setPreferences] = useState<Preferences>({ dotaRoot: null, experimentalMount: false });
   const [status, setStatus] = useState<Status | null>(null);
   const [root, setRoot] = useState("");
   const [input, setInput] = useState("");
   const [output, setOutput] = useState("");
   const [source, setSource] = useState("");
   const [buildOutput, setBuildOutput] = useState("");
   const [fpsMax, setFpsMax] = useState("120");
   const [menuFps, setMenuFps] = useState("60");
   const [recipe, setRecipe] = useState("");
   const [url, setUrl] = useState("");
   const [sha, setSha] = useState("");
   const [busy, setBusy] = useState(false);
   const [ready, setReady] = useState(false);
   const [error, setError] = useState("");
   const [message, setMessage] = useState("");
   const [result, setResult] = useState("");
   const controller = useRef<AbortController | null>(null);
   async function refresh() {
      const [detected, saved] = await Promise.all([
         api<Installation[]>("detect"), api<Preferences>("preferences"),
      ]);
      setInstallations(detected); setPreferences(saved);
      setRoot(saved.dotaRoot ?? (detected.length === 1 ? detected[0]!.root : ""));
      if (saved.dotaRoot || detected.length === 1) setStatus(await api<Status>("status"));
      else setStatus(null);
   }
   useEffect(() => {
      let active = true;
      void connect().then(async () => { if (active) { await refresh(); setReady(true); } })
         .catch((e: unknown) => { if (active) setError(String(e instanceof Error ? e.message : e)); });
      return () => { active = false; };
   }, []);
   useEffect(() => {
      if (!ready || !root || busy) return;
      const timer = window.setInterval(() => {
         void api<Status>("status").then(setStatus).catch((e: unknown) => setError(String(e)));
      }, 5000);
      return () => window.clearInterval(timer);
   }, [ready, root, busy]);
   async function action(path: string, body: unknown, success: string) {
      setBusy(true); setError(""); setMessage(""); setResult("");
      controller.current = new AbortController();
      try {
         const value = await api<unknown>(path, body, controller.current.signal);
         if (value !== null) setResult(JSON.stringify(value, null, 2));
         setMessage(success); await refresh();
      } catch (e: unknown) {
         setError(e instanceof Error && e.name === "AbortError"
            ? "Operación cancelada. Revisa el estado antes de continuar." : String(e instanceof Error ? e.message : e));
      } finally { setBusy(false); controller.current = null; }
   }
   function submit(event: FormEvent, path: string, body: unknown, success: string) {
      event.preventDefault(); void action(path, body, success);
   }
   const savedRoot = preferences.dotaRoot ?? (installations.length === 1 ? installations[0]!.root : "");
   const mutationBlocked = !ready || busy || !root || root !== savedRoot || !!status?.gameRunning || !!status?.recoveryRequired;
   return <div className="linux-app">
      <header className="app-header"><div><h1>ArdysaModsTools</h1><p>Linux · versión experimental</p></div>
         <span className="platform">Dota 2 nativo</span></header>
      <div className="workspace">
         <nav aria-label="Herramientas">{tabs.map(item => <button key={item.id}
            aria-current={tab === item.id ? "page" : undefined} onClick={() => setTab(item.id)}>{item.label}</button>)}
            <a href="https://github.com/jhulio435m/ArdysaModsTools/tree/linux-port/Linux" target="_blank" rel="noreferrer">Documentación</a>
         </nav>
         <main>
            <section className="installation" aria-labelledby="installation-title">
               <h2 id="installation-title">Tu instalación</h2>
               <form onSubmit={e => submit(e, "preferences", { ...preferences, dotaRoot: root }, "Instalación guardada.")}>
                  <label htmlFor="dota-root">Carpeta raíz de Dota 2</label>
                  <div className="input-row"><input id="dota-root" value={root} onChange={e => setRoot(e.target.value)}
                     list="steam-libraries" required placeholder="/ruta/steamapps/common/dota 2 beta" disabled={busy || !ready} />
                     <button disabled={busy || !ready}>Guardar</button></div>
                  <datalist id="steam-libraries">{installations.map(item => <option key={item.root} value={item.root} />)}</datalist>
               </form>
               <p className="status-line">{!ready ? "Conectando con la aplicación local…" : !root ? "Selecciona una instalación para continuar."
                  : status?.gameRunning ? "Dota 2 está abierto. Ciérralo antes de instalar o restaurar mods."
                  : status?.installed ? "Mods Linux instalados · Dota 2 cerrado" : "Sin mods Linux administrados · Dota 2 cerrado"}</p>
               {status?.details && <p className="secondary">Build instalado: {status.details.buildId ?? "desconocido"}</p>}
            </section>
            {(status?.patchChanged || status?.buildChanged) && <div className="notice" role="status">
               Dota cambió desde la instalación del mod. Revisa el paquete y vuelve a aplicar el montaje antes de usarlo.
            </div>}
            {status?.recoveryRequired && <div className="notice">Hay una operación interrumpida.
               <button disabled={busy || !!status.gameRunning} onClick={() => void action("recover", {}, "Recuperación completada.")}>Recuperar archivos</button></div>}

            {tab === "mods" && <section aria-labelledby="mods-title">
               <h2 id="mods-title">Instalar un paquete</h2>
               <p>Usa un VPK local. Los paquetes divididos necesitan todos sus archivos en la misma carpeta.</p>
               <div className="notice">El montaje de mods en Dota Linux todavía no se ha validado dentro del juego.
                  La aplicación guarda el archivo original para restaurarlo.</div>
               <label className="check"><input type="checkbox" checked={preferences.experimentalMount}
                  disabled={busy || !ready} onChange={e => void action("preferences", {
                     ...preferences, dotaRoot: root || null, experimentalMount: e.target.checked,
                  }, "Modo experimental actualizado.")} /> Habilitar la instalación experimental</label>
               <form onSubmit={e => submit(e, "install", { input }, "Paquete instalado. La carga dentro del juego queda por comprobar.")}>
                  <label htmlFor="mod-vpk">Ruta del archivo VPK</label>
                  <input id="mod-vpk" value={input} onChange={e => setInput(e.target.value)} required placeholder="/ruta/pak01_dir.vpk" disabled={busy} />
                  <div className="actions"><button className="primary" disabled={mutationBlocked || !preferences.experimentalMount}>Instalar paquete</button>
                     <button type="button" disabled={mutationBlocked || !status?.installed || !preferences.experimentalMount}
                        onClick={() => void action("reapply", {}, "Montaje aplicado a la configuración actual de Dota.")}>Volver a aplicar</button>
                     <button type="button" disabled={mutationBlocked || !status?.installed}
                        onClick={() => void action("disable", {}, "Mods desactivados. Archivos administrados restaurados.")}>Desactivar mods</button></div>
               </form>
            </section>}
            {tab === "vpk" && <section aria-labelledby="vpk-title"><h2 id="vpk-title">Archivos VPK</h2>
               <p>Inspecciona y verifica paquetes antes de instalarlos. La extracción requiere una carpeta vacía.</p>
               <label htmlFor="vpk-input">Archivo VPK</label><input id="vpk-input" value={input} onChange={e => setInput(e.target.value)} placeholder="/ruta/pak01_dir.vpk" />
               <div className="actions"><button disabled={!ready || busy || !input} onClick={() => void action("vpk/list", { input }, "Directorio leído. Se muestran hasta 200 entradas y el total del paquete.")}>Ver contenido</button>
                  <button disabled={!ready || busy || !input} onClick={() => void action("vpk/verify", { input }, "CRC de todas las entradas verificado.")}>Verificar integridad</button></div>
               <form onSubmit={e => submit(e, "vpk/extract", { input, output }, "Archivos extraídos y CRC verificado.")}>
                  <label htmlFor="extract-output">Carpeta de extracción</label><input id="extract-output" value={output} onChange={e => setOutput(e.target.value)} required placeholder="/ruta/carpeta-vacia" />
                  <button disabled={!ready || busy || !input}>Extraer paquete</button></form>
               <form onSubmit={e => submit(e, "vpk/build", { source, output: buildOutput }, "VPK generado y verificado.")}>
                  <h3>Crear un VPK desde una carpeta</h3><p>El archivo de salida debe ser nuevo y estar fuera de la carpeta de origen.</p>
                  <label htmlFor="build-source">Carpeta de origen</label><input id="build-source" value={source} onChange={e => setSource(e.target.value)} required placeholder="/ruta/contenido" />
                  <label htmlFor="build-output">Archivo de salida</label><input id="build-output" value={buildOutput} onChange={e => setBuildOutput(e.target.value)} required placeholder="/ruta/nuevo_dir.vpk" />
                  <button disabled={!ready || busy}>Crear paquete</button></form>
            </section>}
            {tab === "generate" && <section aria-labelledby="generate-title"><h2 id="generate-title">Generar una selección</h2>
               <p>Una receta describe los archivos, sus hashes y los elementos seleccionados. La generación usa el items_game de tu Dota actual.</p>
               <p className="secondary">La galería visual completa de héroes todavía requiere conectar los catálogos del proyecto original.</p>
               <form onSubmit={e => submit(e, "generate", { recipe, output }, "Selección generada y VPK verificado.")}>
                  <label htmlFor="recipe">Receta JSON</label><input id="recipe" value={recipe} onChange={e => setRecipe(e.target.value)} required placeholder="/ruta/seleccion.json" />
                  <label htmlFor="generated-output">VPK de salida</label><input id="generated-output" value={output} onChange={e => setOutput(e.target.value)} required placeholder="/ruta/seleccion_dir.vpk" />
                  <button className="primary" disabled={!ready || busy || !root}>Generar VPK</button></form>
            </section>}
            {tab === "download" && <section aria-labelledby="download-title"><h2 id="download-title">Descargar un archivo verificado</h2>
               <p>Introduce un enlace directo de un CDN del proyecto o GitHub y el SHA-256 publicado en un manifiesto de confianza.</p>
               <form onSubmit={e => submit(e, "download", { urls: [url], sha256: sha, output }, "Descarga completada y SHA-256 verificado.")}>
                  <label htmlFor="url">URL HTTPS</label><input id="url" type="url" value={url} onChange={e => setUrl(e.target.value)} required placeholder="https://cdn.ardysamods.my.id/…" />
                  <label htmlFor="sha">SHA-256 esperado</label><input id="sha" value={sha} onChange={e => setSha(e.target.value)} required pattern="[a-fA-F0-9]{64}" minLength={64} maxLength={64} spellCheck={false} />
                  <label htmlFor="download-output">Archivo de destino</label><input id="download-output" value={output} onChange={e => setOutput(e.target.value)} required placeholder="/ruta/archivo-nuevo" />
                  <button className="primary" disabled={!ready || busy}>Descargar y verificar</button></form>
            </section>}
            {tab === "performance" && <section aria-labelledby="fps-title"><h2 id="fps-title">Límites de FPS</h2>
               <p>Guarda los límites en un bloque propio de autoexec.cfg. Se conserva el resto de tu configuración. 0 significa sin límite.</p>
               <form onSubmit={e => submit(e, "performance", { fpsMax: Number(fpsMax), menuFps: Number(menuFps) }, "Límites guardados en autoexec.cfg. Su efecto se comprueba al iniciar Dota.")}>
                  <label htmlFor="fps-max">FPS dentro del juego</label><input id="fps-max" type="number" min="0" max="1000" value={fpsMax} onChange={e => setFpsMax(e.target.value)} required />
                  <label htmlFor="menu-fps">FPS en el menú</label><input id="menu-fps" type="number" min="0" max="1000" value={menuFps} onChange={e => setMenuFps(e.target.value)} required />
                  <div className="actions"><button className="primary" disabled={mutationBlocked}>Guardar límites</button>
                     <button type="button" disabled={mutationBlocked} onClick={() => void action("performance", { fpsMax: null, menuFps: null }, "Bloque de límites eliminado.")}>Quitar estos límites</button></div>
               </form></section>}
            {busy && <div className="operation" role="status"><progress aria-label="Operación en curso" /> Procesando…
               <button onClick={() => controller.current?.abort()}>Cancelar</button></div>}
            {error && <div className="error" role="alert">{error}</div>}
            {message && <div className="message" role="status">{message}</div>}
            {result && <details open><summary>Resultado</summary><pre>{result}</pre></details>}
         </main>
      </div>
      <footer>Fork independiente · GPL-3.0 · Sin afiliación con Valve o Steam</footer>
   </div>;
}
