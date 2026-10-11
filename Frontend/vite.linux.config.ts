// SPDX-License-Identifier: GPL-3.0-or-later
import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

export default defineConfig({
   plugins: [react()],
   build: {
      outDir: "../Linux/Host/wwwroot",
      emptyOutDir: true,
      rollupOptions: { input: "src/pages/linux/index.html" },
      target: "es2020",
   },
});
