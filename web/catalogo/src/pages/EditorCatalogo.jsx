import { useEffect, useMemo, useState } from "react";
import { Loader2 } from "lucide-react";
import VitrineEditor from "../components/catalog/VitrineEditor";
import { carregarTudo } from "../services/catalogService";

// Tela do Nexus LS: o editor visual da vitrine, com os produtos do estoque local.
export default function EditorCatalogo() {
  const [dados, setDados] = useState(null);
  const [erro, setErro] = useState("");

  useEffect(() => {
    carregarTudo(true).then(setDados).catch((e) => setErro(e?.message || "Não foi possível carregar o catálogo."));
    // Ajuste de produto (visível, destaque, promoção) feito no painel Produtos
    const aoMudar = (e) => setDados((d) => (d ? { ...d, produtos: e.detail } : d));
    window.addEventListener("nexus:produtos", aoMudar);
    return () => window.removeEventListener("nexus:produtos", aoMudar);
  }, []);

  const produtos = useMemo(() => dados?.produtos ?? [], [dados]);
  const categorias = useMemo(() => [...new Set(produtos.map((p) => p.categoria).filter(Boolean))], [produtos]);

  if (erro) return <div className="p-6 text-sm text-red-300">{erro}</div>;
  if (!dados)
    return (
      <div className="grid h-screen place-items-center text-slate-400">
        <Loader2 className="h-6 w-6 animate-spin" />
      </div>
    );

  return (
    <div className="min-h-screen bg-slate-950 p-4 text-slate-100">
      <VitrineEditor tenantId="local" vitrineSalva={dados.vitrine} produtos={produtos} categorias={categorias} empresa={dados.empresa} />
    </div>
  );
}
