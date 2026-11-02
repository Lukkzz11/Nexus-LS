import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { HashRouter, Route, Routes } from "react-router-dom";
import { Toaster } from "react-hot-toast";
import "./index.css";
import EditorCatalogo from "./pages/EditorCatalogo";
import CatalogoPublico from "./pages/CatalogoPublico";
import ProdutoPublico from "./pages/ProdutoPublico";

// HashRouter: o WebView2 serve os arquivos de um endereço virtual, sem servidor para reescrever rotas.
// "/" = editor; "/catalogo/..." = o catálogo (usado dentro do editor como pré-visualização).
createRoot(document.getElementById("root")).render(
  <StrictMode>
    <HashRouter>
      <Toaster position="top-right" toastOptions={{ style: { background: "#0f172a", color: "#f1f5f9", border: "1px solid #1e293b" } }} />
      <Routes>
        <Route path="/" element={<EditorCatalogo />} />
        <Route path="/catalogo/:companyId" element={<CatalogoPublico />} />
        <Route path="/catalogo/:companyId/produto/:produtoId" element={<ProdutoPublico />} />
      </Routes>
    </HashRouter>
  </StrictMode>
);
