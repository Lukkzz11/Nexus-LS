import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { HashRouter, Navigate, Route, Routes } from "react-router-dom";
import "./index.css";
import CatalogoPublico from "./pages/CatalogoPublico";
import ProdutoPublico from "./pages/ProdutoPublico";

// O catálogo para o site publicado: lê o Firestore (leitura pública), sem ponte e sem editor.
window.__nexusPublico = true;

createRoot(document.getElementById("root")).render(
  <StrictMode>
    <HashRouter>
      <Routes>
        <Route path="/" element={<Navigate to="/catalogo/loja" replace />} />
        <Route path="/catalogo/:companyId" element={<CatalogoPublico />} />
        <Route path="/catalogo/:companyId/produto/:produtoId" element={<ProdutoPublico />} />
      </Routes>
    </HashRouter>
  </StrictMode>
);
