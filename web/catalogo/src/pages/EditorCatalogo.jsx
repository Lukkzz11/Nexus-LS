import { useEffect, useMemo, useState } from "react";
import { Globe, Loader2 } from "lucide-react";
import VitrineEditor from "../components/catalog/VitrineEditor";
import MercadoriasCatalogo from "../components/catalog/MercadoriasCatalogo";
import { Badge } from "../components/ui/Badge";
import { carregarTudo } from "../services/catalogService";
import { cn } from "../utils/cn";

const ABAS = [
  ["mercadorias", "Mercadorias e dados da loja"],
  ["editor", "Editor visual da vitrine"]
];

// Tela do Nexus LS no mesmo formato da tela "Editar Catálogo & Vitrine Online" do ERP: cabeçalho, duas abas
// (mercadorias e dados da loja | editor visual) e os produtos do estoque local. Nada daqui fala com o Firebase.
export default function EditorCatalogo() {
  const [dados, setDados] = useState(null);
  const [erro, setErro] = useState("");
  const [aba, setAba] = useState("editor");
  const [editorVisitado, setEditorVisitado] = useState(true);
  const [painelPedido, setPainelPedido] = useState(null);

  useEffect(() => {
    carregarTudo(true).then(setDados).catch((e) => setErro(e?.message || "Não foi possível carregar o catálogo."));
    // Ajuste de produto (visível, destaque, promoção) feito na tabela ou no painel Produtos
    const aoMudar = (e) => setDados((d) => (d ? { ...d, produtos: e.detail } : d));
    window.addEventListener("nexus:produtos", aoMudar);
    return () => window.removeEventListener("nexus:produtos", aoMudar);
  }, []);

  const produtos = useMemo(() => dados?.produtos ?? [], [dados]);
  const categorias = useMemo(() => [...new Set(produtos.map((p) => p.categoria).filter(Boolean))], [produtos]);

  const irParaEditor = (painel) => {
    setAba("editor");
    setEditorVisitado(true);
    if (painel) setPainelPedido({ id: painel, n: (painelPedido?.n ?? 0) + 1 });
  };

  if (erro) return <div className="p-6 text-sm text-red-300">{erro}</div>;
  if (!dados)
    return (
      <div className="grid h-screen place-items-center text-slate-400">
        <Loader2 className="h-6 w-6 animate-spin" />
      </div>
    );

  const nomeLoja = dados.vitrine?.loja?.nome || dados.empresa?.nome || "Minha loja";
  return (
    <div className="min-h-screen w-full space-y-6 bg-slate-950 p-5 text-slate-100">
      <div className="flex flex-col justify-between gap-4 border-b border-slate-800 pb-4 lg:flex-row lg:items-center">
        <div>
          <div className="mb-1 flex items-center gap-2">
            <Badge variant="info" size="sm" withDot>Vitrine &amp; Catálogo Digital</Badge>
            <span className="text-xs text-slate-400">
              Loja: <strong className="uppercase text-white">{nomeLoja}</strong>
            </span>
          </div>
          <h1 className="flex items-center gap-2 text-2xl font-extrabold tracking-tight text-white sm:text-3xl">
            <Globe className="h-7 w-7 text-sky-400" />
            Editar Catálogo &amp; Vitrine Online
          </h1>
          <p className="max-w-3xl text-xs text-slate-400">
            Personalize o nome da sua loja, o WhatsApp de pedidos e o visual da vitrine, e escolha o que aparece, o que é destaque e o que está em promoção.
            Salvar guarda neste computador; Salvar e publicar envia ao catálogo online.
          </p>
        </div>
      </div>

      <div className="flex flex-wrap gap-2" role="tablist" aria-label="Seções do catálogo">
        {ABAS.map(([id, rotulo]) => (
          <button
            key={id}
            type="button"
            role="tab"
            aria-selected={aba === id}
            onClick={() => (id === "editor" ? irParaEditor() : setAba(id))}
            className={cn(
              "cursor-pointer rounded-full border px-4 py-2 text-sm font-medium transition-colors",
              aba === id ? "border-sky-400 bg-sky-500 font-semibold text-slate-950" : "border-slate-700 bg-slate-900 text-slate-300 hover:text-white"
            )}
          >
            {rotulo}
          </button>
        ))}
      </div>

      {/* O editor continua montado ao voltar para Mercadorias, para não perder o que ainda não foi publicado */}
      {(aba === "editor" || editorVisitado) && (
        <div hidden={aba !== "editor"}>
          <VitrineEditor
            tenantId="local"
            vitrineSalva={dados.vitrine}
            produtos={produtos}
            categorias={categorias}
            empresa={dados.empresa}
            painelPedido={painelPedido}
            aoSalvar={(vitrine) => setDados((d) => (d ? { ...d, vitrine } : d))}
          />
        </div>
      )}

      {aba === "mercadorias" && (
        <MercadoriasCatalogo produtos={produtos} vitrine={dados.vitrine} empresa={dados.empresa} aoEditarLoja={() => irParaEditor("loja")} />
      )}
    </div>
  );
}
