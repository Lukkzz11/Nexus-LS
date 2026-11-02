import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import tailwindcss from "@tailwindcss/vite";

// Saída relativa (base "./"): o WebView2 serve os arquivos de um endereço virtual, não da raiz de um site.
export default defineConfig({
  base: "./",
  plugins: [tailwindcss(), react()],
  build: { outDir: "dist", emptyOutDir: true, chunkSizeWarningLimit: 2000 }
});
