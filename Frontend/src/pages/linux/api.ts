// SPDX-License-Identifier: GPL-3.0-or-later
export type Installation = { root: string; library: string; buildId: string | null };
export type Preferences = { dotaRoot: string | null; experimentalMount: boolean };
export type Status = {
   installed: boolean; gameRunning: boolean; patchChanged: boolean;
   buildChanged: boolean; recoveryRequired: boolean;
   details: { vpkSha256: string; installedAt: string; buildId: string | null } | null;
};
export async function api<T>(path: string, body?: unknown, signal?: AbortSignal): Promise<T> {
   const response = await fetch(`/api/${path}`, {
      method: body === undefined ? "GET" : "POST",
      headers: body === undefined ? undefined : { "Content-Type": "application/json" },
      body: body === undefined ? undefined : JSON.stringify(body),
      credentials: "same-origin", signal,
   });
   const text = await response.text();
   const result = text ? JSON.parse(text) as { error?: string } : null;
   if (!response.ok) throw new Error(result?.error ?? (response.status === 401
      ? "Sesión cerrada. Abre la aplicación desde el lanzador." : `La operación falló (${response.status}).`));
   return result as T;
}
let session: Promise<void> | undefined;
export function connect(): Promise<void> {
   session ??= (async () => {
      const token = new URLSearchParams(window.location.hash.slice(1)).get("session");
      window.history.replaceState(null, "", window.location.pathname);
      if (token) await api("session", { token });
      await api("preferences");
   })();
   return session;
}
