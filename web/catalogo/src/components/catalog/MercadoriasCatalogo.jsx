import { useMemo, useState } from "react";
import toast from "react-hot-toast";
import {
  Edit2,
  Eye,
  EyeOff,
  Image as ImageIcon,
  MessageCircle,
  Phone,
  Search,
  ShoppingBag,
  Star,
  Store,
  MapPin,
  Save
} from "lucide-react";
import { Card } from "../ui/Card";
import { Badge } from "../ui/Badge";
import { Button } from "../ui/Button";
import { Input } from "../ui/Input";
import { Select } from "../ui/Select";
import { Modal, ModalBody, ModalFooter, ModalHeader } from "../ui/Modal";
import { formatCurrency } from "../../utils/formatters";
import { cn } from "../../utils/cn";
import { getProductImageUrl } from "../../services/imageUploadService";
import { ajustarProdutoCatalogo } from "../../services/catalogService";

const textoPromo = (valor) => (Number(valor) > 0 ? Number(valor).toFixed(2).replace(".", ",") : "");
const numero = (texto) => Number(String(texto ?? "").replace(",", ".")) || 0;
const totalGrade = (grade) => Object.values(grade || {}).reduce((s, q) => s + (Number(q) || 0), 0);

/** Foto do produto ou um quadro vazio (sem internet nem imagem externa: o catálogo do Nexus funciona offline). */
function Foto({ produto, className }) {
  const [falhou, setFalhou] = useState(false);
  const url = getProductImageUrl(produto);
  return (
    <div className={cn("grid shrink-0 place-items-center overflow-hidden rounded-lg border border-slate-800/90 bg-slate-900", className)}>
      {url && !falhou ? (
        <img src={url} alt={produto.nome} className="h-full w-full object-cover" onError={() => setFalhou(true)} />
      ) : (
        <ImageIcon className="h-5 w-5 text-slate-600" />
      )}
    </div>
  );
}

function Metrica({ rotulo, valor, legenda, Icone, tom }) {
  const tons = {
    sky: ["border-slate-800/80", "text-slate-400", "text-white", "text-slate-500", "bg-sky-500/10 border-sky-500/20 text-sky-400"],
    emerald: ["border-emerald-500/20", "text-emerald-400/90", "text-emerald-400", "text-emerald-500/80", "bg-emerald-500/10 border-emerald-500/25 text-emerald-400"],
    amber: ["border-amber-500/20", "text-amber-400/90", "text-amber-400", "text-amber-500/80", "bg-amber-500/10 border-amber-500/25 text-amber-400"],
    yellow: ["border-yellow-500/20", "text-yellow-400/90", "text-yellow-400", "text-yellow-500/80", "bg-yellow-500/10 border-yellow-500/25 text-yellow-400"]
  }[tom];
  return (
    <Card variant="subtle" className={cn("flex items-center justify-between rounded-xl border bg-slate-900/40 p-4", tons[0])}>
      <div className="space-y-1">
        <span className={cn("block text-[11px] font-semibold uppercase tracking-wider", tons[1])}>{rotulo}</span>
        <div className={cn("text-2xl font-black tracking-tight", tons[2])}>{valor}</div>
        <span className={cn("text-[10px] font-medium", tons[3])}>{legenda}</span>
      </div>
      <div className={cn("flex h-11 w-11 shrink-0 items-center justify-center rounded-xl border", tons[4])}>
        <Icone className="h-5 w-5" />
      </div>
    </Card>
  );
}

/** Resumo da identidade da loja no catálogo. Os campos se editam no Editor visual (painel Loja). */
function IdentificacaoDaLoja({ vitrine, empresa, aoEditar }) {
  const l = vitrine?.loja || {};
  const e = empresa || {};
  const nome = l.nome || e.nome || "";
  const whatsapp = l.whatsapp || e.whatsapp || "";
  const telefone = l.telefone || e.telefone || "";
  const endereco = l.endereco || [e.endereco, e.bairro, e.cidade].filter(Boolean).join(", ");
  const apresentacao = l.apresentacao || e.mensagemCatalogo || "";
  const Campo = ({ Icone, rotulo, valor, ajuda }) => (
    <div>
      <label className="mb-1 block text-xs font-bold text-slate-300">{rotulo}</label>
      <div className="flex h-10 items-center gap-2 rounded-lg border border-slate-700/80 bg-slate-950/80 px-3.5 text-sm text-white">
        <Icone className="h-4 w-4 shrink-0 text-slate-400" />
        <span className={cn("truncate", !valor && "text-slate-500")}>{valor || "Não informado"}</span>
      </div>
      <span className="mt-0.5 block text-[11px] text-slate-500">{ajuda}</span>
    </div>
  );
  return (
    <Card variant="subtle" className="space-y-4 border-sky-500/20 p-5 shadow-lg">
      <div className="flex items-center justify-between border-b border-slate-800 pb-3">
        <div className="flex items-center gap-2.5">
          <div className="flex h-9 w-9 items-center justify-center rounded-xl border border-sky-500/20 bg-sky-500/10 text-sky-400">
            <Store className="h-5 w-5" />
          </div>
          <div>
            <h2 className="text-sm font-bold text-white sm:text-base">Identificação da Loja no Catálogo</h2>
            <p className="text-xs text-slate-400">Estes dados aparecem no cabeçalho e no rodapé do catálogo público visto pelo cliente.</p>
          </div>
        </div>
        <Button variant="primary" size="sm" onClick={aoEditar} leftIcon={<Edit2 className="h-4 w-4" />}>
          Editar dados da loja
        </Button>
      </div>
      <div className="grid grid-cols-1 gap-4 md:grid-cols-2 xl:grid-cols-4">
        <Campo Icone={Store} rotulo="Nome da Loja na Vitrine" valor={nome} ajuda="Nome de destaque exibido no topo do catálogo." />
        <Campo Icone={MessageCircle} rotulo="WhatsApp para Receber Pedidos" valor={whatsapp} ajuda="Os pedidos da sacola abrem direto neste número." />
        <Campo Icone={Phone} rotulo="Telefone" valor={telefone} ajuda="Mostrado no cabeçalho e no rodapé, se ligado." />
        <Campo Icone={MapPin} rotulo="Endereço de Retirada / Cidade" valor={endereco} ajuda="Exibido na opção de retirada no balcão." />
      </div>
      <div>
        <label className="mb-1 block text-xs font-bold text-slate-300">Mensagem do Banner de Boas-Vindas</label>
        <p className={cn("rounded-xl border border-slate-800 bg-slate-900 p-2.5 text-xs", apresentacao ? "text-white" : "text-slate-500")}>
          {apresentacao || "Sem mensagem. Escreva a apresentação da loja no painel Loja do editor visual."}
        </p>
      </div>
    </Card>
  );
}

/** Edição de uma mercadoria no catálogo: os ajustes do catálogo valem aqui; nome, preço, foto e grade vêm do Estoque. */
function ModalMercadoria({ produto, aoFechar }) {
  const [visivel, setVisivel] = useState(produto.ativoNoCatalogo !== false);
  const [destaque, setDestaque] = useState(Boolean(produto.destaque));
  const [promo, setPromo] = useState(textoPromo(produto.precoPromocional));
  const [salvando, setSalvando] = useState(false);

  const salvar = async (e) => {
    e.preventDefault();
    const valor = numero(promo);
    if (valor < 0) return toast.error("O preço promocional não pode ser negativo.");
    if (valor > 0 && valor >= (Number(produto.precoVarejo) || 0)) return toast.error("O preço promocional precisa ser menor que o preço de varejo.");
    setSalvando(true);
    try {
      await ajustarProdutoCatalogo(produto.id, { ativoNoCatalogo: visivel, destaque, precoPromocional: valor });
      toast.success(`Mercadoria "${produto.nome}" atualizada!`);
      aoFechar();
    } catch (err) {
      toast.error(err?.message || "Não foi possível guardar o ajuste.");
    } finally {
      setSalvando(false);
    }
  };

  const Somente = ({ rotulo, valor }) => (
    <div>
      <span className="mb-1 block text-xs font-bold text-slate-300">{rotulo}</span>
      <div className="flex h-10 items-center rounded-lg border border-slate-800 bg-slate-900/60 px-3.5 text-sm text-slate-300">{valor || "-"}</div>
    </div>
  );

  const grade = Object.entries(produto.gradeTamanhos || {});
  return (
    <Modal isOpen onClose={aoFechar} size="lg">
      <ModalHeader onClose={aoFechar}>
        <div className="flex items-center gap-2">
          <ShoppingBag className="h-5 w-5 text-sky-400" />
          <span>Editar Mercadoria do Catálogo</span>
        </div>
      </ModalHeader>
      <form onSubmit={salvar}>
        <ModalBody className="max-h-[75vh] space-y-4 overflow-y-auto">
          <div className="flex gap-4">
            <Foto produto={produto} className="h-32 w-24" />
            <div className="grid min-w-0 flex-1 grid-cols-1 gap-3 sm:grid-cols-2">
              <div className="sm:col-span-2"><Somente rotulo="Nome da Mercadoria" valor={produto.nome} /></div>
              <Somente rotulo="Categoria" valor={produto.categoria} />
              <Somente rotulo="Código / Referência" valor={produto.referencia} />
              <Somente rotulo="Preço Varejo (Consumidor)" valor={formatCurrency(produto.precoVarejo)} />
              <Somente rotulo="Preço Atacado (Lojistas)" valor={formatCurrency(produto.precoAtacado)} />
            </div>
          </div>
          <p className="rounded-lg border border-sky-500/25 bg-sky-500/10 px-3 py-2 text-xs text-sky-300">
            Nome, preços, foto e grade de tamanhos são cadastrados no Estoque da loja (valem para o PDV e para o catálogo). Aqui você escolhe como a peça aparece no catálogo online.
          </p>
          <div>
            <span className="mb-1 block text-xs font-bold text-slate-300">Estoque por tamanho</span>
            <div className="flex flex-wrap gap-1.5">
              {grade.length === 0 ? (
                <span className="text-xs text-slate-500">Sem grade de tamanhos.</span>
              ) : (
                grade.map(([tam, qtd]) => (
                  <span key={tam} className="rounded-lg border border-slate-700 bg-slate-900 px-2.5 py-1 text-xs text-slate-200">
                    {tam}: <strong className="text-white">{qtd}</strong>
                  </span>
                ))
              )}
              {grade.length > 0 && <span className="px-1 py-1 text-xs text-slate-400">Total: {totalGrade(produto.gradeTamanhos)}</span>}
            </div>
          </div>
          <Input
            type="text"
            inputMode="decimal"
            label="Preço promocional no catálogo online (opcional)"
            placeholder="Deixe vazio se não estiver em promoção"
            value={promo}
            onChange={(e) => setPromo(e.target.value)}
            leftIcon={<span className="font-mono text-xs font-bold text-slate-400">R$</span>}
            helperText="Vale para o varejo do catálogo online. Precisa ser menor que o preço de varejo. Não muda o preço do PDV."
          />
          <div className="grid grid-cols-1 gap-3 border-t border-slate-800 pt-2 sm:grid-cols-2">
            <label className="flex cursor-pointer items-center gap-3 rounded-xl border border-slate-800 bg-slate-900/60 p-3 hover:border-slate-700">
              <input type="checkbox" checked={visivel} onChange={(e) => setVisivel(e.target.checked)} className="h-4 w-4" />
              <div>
                <div className="text-xs font-bold text-white">Visível no Catálogo</div>
                <div className="text-[11px] text-slate-400">Exibir esta peça para clientes na vitrine</div>
              </div>
            </label>
            <label className="flex cursor-pointer items-center gap-3 rounded-xl border border-slate-800 bg-slate-900/60 p-3 hover:border-slate-700">
              <input type="checkbox" checked={destaque} onChange={(e) => setDestaque(e.target.checked)} className="h-4 w-4" />
              <div>
                <div className="flex items-center gap-1 text-xs font-bold text-white">
                  <span>Produto em Destaque</span>
                  <Star className="h-3 w-3 fill-amber-400 text-amber-400" />
                </div>
                <div className="text-[11px] text-slate-400">Aparece com selo e destaque na vitrine</div>
              </div>
            </label>
          </div>
        </ModalBody>
        <ModalFooter>
          <Button type="button" variant="ghost" onClick={aoFechar}>Cancelar</Button>
          <Button type="submit" variant="primary" isLoading={salvando} leftIcon={<Save className="h-4 w-4" />}>Salvar Alterações</Button>
        </ModalFooter>
      </form>
    </Modal>
  );
}

/** Célula do preço promocional: edita ao clicar (Enter salva, Esc cancela). */
function PromoRapida({ produto }) {
  const [valor, setValor] = useState(textoPromo(produto.precoPromocional));
  const confirmar = async () => {
    const novo = numero(valor);
    if (novo === (Number(produto.precoPromocional) || 0)) return;
    try {
      await ajustarProdutoCatalogo(produto.id, { precoPromocional: novo });
      toast.success(`Promoção de "${produto.nome}" atualizada!`);
    } catch (err) {
      toast.error(err?.message || "Não foi possível guardar a promoção.");
      setValor(textoPromo(produto.precoPromocional));
    }
  };
  return (
    <input
      type="text"
      inputMode="decimal"
      value={valor}
      placeholder="Sem promo"
      aria-label={`Preço promocional: ${produto.nome}`}
      title="Clique para editar o preço promocional (Enter salva, Esc cancela)"
      onChange={(e) => setValor(e.target.value)}
      onBlur={confirmar}
      onKeyDown={(e) => {
        if (e.key === "Enter") e.currentTarget.blur();
        if (e.key === "Escape") {
          setValor(textoPromo(produto.precoPromocional));
          e.currentTarget.blur();
        }
      }}
      className="h-8 w-28 rounded-md border border-transparent bg-transparent px-2 text-right font-mono text-sm font-bold text-rose-300 outline-none placeholder:font-normal placeholder:text-slate-600 hover:border-slate-700 focus:border-sky-400 focus:bg-slate-950"
    />
  );
}

const semAcento = (t) => String(t || "").normalize("NFD").replace(/[̀-ͯ]/g, "").toLowerCase();

/** A aba "Mercadorias e dados da loja", feita sobre o estoque local do Nexus. */
export default function MercadoriasCatalogo({ produtos, vitrine, empresa, aoEditarLoja }) {
  const [busca, setBusca] = useState("");
  const [filtroCategoria, setFiltroCategoria] = useState("TODAS");
  const [filtroStatus, setFiltroStatus] = useState("TODOS");
  const [editando, setEditando] = useState(null);

  const lista = useMemo(() => produtos.filter((p) => p.ativo !== false), [produtos]);
  const categorias = useMemo(() => [...new Set(lista.map((p) => p.categoria).filter(Boolean))].sort((a, b) => a.localeCompare(b, "pt-BR")), [lista]);

  const contadores = useMemo(() => {
    const ativos = lista.filter((p) => p.ativoNoCatalogo !== false).length;
    return { total: lista.length, ativos, ocultos: lista.length - ativos, destaques: lista.filter((p) => p.destaque).length };
  }, [lista]);

  const filtrados = useMemo(() => {
    const termos = semAcento(busca).split(/\s+/).filter(Boolean);
    return lista.filter((p) => {
      if (filtroCategoria !== "TODAS" && p.categoria !== filtroCategoria) return false;
      if (filtroStatus === "ATIVOS" && p.ativoNoCatalogo === false) return false;
      if (filtroStatus === "OCULTOS" && p.ativoNoCatalogo !== false) return false;
      if (filtroStatus === "DESTAQUES" && !p.destaque) return false;
      if (termos.length === 0) return true;
      const alvo = semAcento(`${p.nome} ${p.referencia || ""} ${p.categoria || ""}`);
      return termos.every((t) => alvo.includes(t));
    });
  }, [lista, busca, filtroCategoria, filtroStatus]);

  const alternar = async (produto, mudanca, mensagem) => {
    try {
      await ajustarProdutoCatalogo(produto.id, mudanca);
      toast.success(mensagem);
    } catch (err) {
      toast.error(err?.message || "Não foi possível guardar o ajuste.");
    }
  };

  return (
    <div className="space-y-6">
      <IdentificacaoDaLoja vitrine={vitrine} empresa={empresa} aoEditar={aoEditarLoja} />

      <div className="grid grid-cols-2 gap-3.5 lg:grid-cols-4">
        <Metrica rotulo="Total Cadastrado" valor={contadores.total} legenda="Itens no estoque" Icone={ShoppingBag} tom="sky" />
        <Metrica rotulo="Visíveis na Vitrine" valor={contadores.ativos} legenda="Exibição pública ativa" Icone={Eye} tom="emerald" />
        <Metrica rotulo="Ocultos / Pausados" valor={contadores.ocultos} legenda="Fora da vitrine" Icone={EyeOff} tom="amber" />
        <Metrica rotulo="Em Destaque" valor={contadores.destaques} legenda="Topo do catálogo" Icone={Star} tom="yellow" />
      </div>

      <Card variant="subtle" className="flex flex-col items-stretch gap-3 rounded-xl border border-slate-800/80 p-4 md:flex-row md:items-center">
        <div className="flex-1">
          <Input placeholder="Buscar mercadoria por nome, referência..." value={busca} onChange={(e) => setBusca(e.target.value)} leftIcon={<Search className="h-4 w-4" />} aria-label="Buscar mercadoria" />
        </div>
        <div className="w-full md:w-56">
          <Select value={filtroCategoria} onChange={(e) => setFiltroCategoria(e.target.value)} aria-label="Filtrar por categoria">
            <option value="TODAS">Todas as Categorias</option>
            {categorias.map((c) => (
              <option key={c} value={c}>{c}</option>
            ))}
          </Select>
        </div>
        <div className="w-full md:w-56">
          <Select value={filtroStatus} onChange={(e) => setFiltroStatus(e.target.value)} aria-label="Filtrar por status">
            <option value="TODOS">Todos os Status</option>
            <option value="ATIVOS">Apenas Visíveis</option>
            <option value="OCULTOS">Apenas Retirados / Ocultos</option>
            <option value="DESTAQUES">Apenas Destaques</option>
          </Select>
        </div>
      </Card>

      <div className="flex items-center gap-2 rounded-lg border border-sky-500/25 bg-sky-500/10 px-3 py-2 text-xs text-sky-300">
        <Edit2 className="h-3.5 w-3.5 shrink-0" />
        <span>
          <strong>Edição rápida:</strong> clique no preço promocional para editar direto na tabela (Enter salva, Esc cancela); destaque e visibilidade valem com 1 clique.
          Nome, preços, foto e grade são cadastrados no Estoque da loja. Os ajustes ficam neste computador e vão para o catálogo online ao publicar.
        </span>
      </div>

      <Card variant="subtle" className="overflow-hidden rounded-xl border border-slate-800/80 shadow-lg">
        <div className="overflow-x-auto">
          <table className="w-full text-left text-sm">
            <thead>
              <tr className="border-b border-slate-800 bg-slate-950/80 text-[11px] uppercase tracking-wide text-slate-400">
                <th className="w-16 px-4 py-3 font-semibold">Foto</th>
                <th className="px-4 py-3 font-semibold">Mercadoria / Nome</th>
                <th className="px-4 py-3 font-semibold">Categoria &amp; Ref</th>
                <th className="px-4 py-3 font-semibold">Preço Varejo</th>
                <th className="px-4 py-3 font-semibold">Preço Atacado</th>
                <th className="px-4 py-3 text-right font-semibold">Promoção (catálogo)</th>
                <th className="px-4 py-3 text-center font-semibold">Destaque</th>
                <th className="px-4 py-3 text-center font-semibold">Visibilidade (Catálogo)</th>
                <th className="px-4 py-3 text-right font-semibold">Ações</th>
              </tr>
            </thead>
            <tbody>
              {filtrados.length === 0 ? (
                <tr>
                  <td colSpan={9} className="px-4 py-10 text-center text-slate-500">Nenhuma mercadoria encontrada com os filtros selecionados.</td>
                </tr>
              ) : (
                filtrados.map((p) => {
                  const visivel = p.ativoNoCatalogo !== false;
                  const total = totalGrade(p.gradeTamanhos);
                  return (
                    <tr key={p.id} className={cn("border-b border-slate-800/40 transition-colors even:bg-slate-900/25 hover:bg-slate-800/40", !visivel && "bg-slate-950/40 opacity-60")}>
                      <td className="px-4 py-2.5"><Foto produto={p} className="h-14 w-12" /></td>
                      <td className="px-4 py-2.5">
                        <div className="flex items-center gap-1.5 text-sm font-bold text-white">
                          <span>{p.nome}</span>
                          {p.destaque && <Star className="inline h-3.5 w-3.5 fill-amber-400 text-amber-400" aria-label="Em destaque" />}
                        </div>
                        <div className="mt-0.5 text-[11px] text-slate-500">
                          {Object.keys(p.gradeTamanhos || {}).length > 0 ? `Estoque: ${total} (${Object.entries(p.gradeTamanhos).map(([t, q]) => `${t} ${q}`).join(" · ")})` : "Sem grade de tamanhos"}
                        </div>
                      </td>
                      <td className="px-4 py-2.5">
                        <Badge variant="subtle" size="sm">{p.categoria || "Geral"}</Badge>
                        <div className="mt-1 font-mono text-[11px] text-slate-400">Ref: {p.referencia || "S/Ref"}</div>
                      </td>
                      <td className="px-4 py-2.5">
                        <div className="font-mono text-sm font-bold text-emerald-400">{formatCurrency(p.precoVarejo)}</div>
                        <div className="text-[10px] text-slate-500">Consumidor</div>
                      </td>
                      <td className="px-4 py-2.5">
                        <div className="font-mono text-sm font-bold text-amber-400">{formatCurrency(p.precoAtacado)}</div>
                        <div className="text-[10px] text-slate-500">Revendedor</div>
                      </td>
                      <td className="px-4 py-2.5 text-right"><PromoRapida key={`${p.id}-${p.precoPromocional}`} produto={p} /></td>
                      <td className="px-4 py-2.5 text-center">
                        <button
                          type="button"
                          aria-pressed={Boolean(p.destaque)}
                          onClick={() => alternar(p, { destaque: !p.destaque }, p.destaque ? `Destaque removido de "${p.nome}".` : `"${p.nome}" marcado como DESTAQUE na vitrine!`)}
                          title={p.destaque ? "Remover dos destaques" : "Marcar como destaque na vitrine"}
                          className={cn(
                            "inline-flex cursor-pointer items-center justify-center rounded-lg border p-1.5 transition-all",
                            p.destaque ? "border-amber-500/30 bg-amber-500/10 text-amber-400 hover:bg-amber-500/20" : "border-slate-800 bg-slate-900 text-slate-600 hover:border-slate-700 hover:text-slate-400"
                          )}
                        >
                          <Star className={cn("h-4 w-4", p.destaque && "fill-amber-400")} />
                        </button>
                      </td>
                      <td className="px-4 py-2.5 text-center">
                        <button
                          type="button"
                          aria-pressed={visivel}
                          onClick={() => alternar(p, { ativoNoCatalogo: !visivel }, visivel ? `"${p.nome}" foi retirado da vitrine pública.` : `"${p.nome}" agora está visível no catálogo online!`)}
                          title={visivel ? "Clique para retirar esta mercadoria do catálogo" : "Clique para exibir na vitrine"}
                          className={cn(
                            "mx-auto flex cursor-pointer items-center justify-center gap-1.5 rounded-xl border px-2.5 py-1.5 text-xs font-bold transition-all",
                            visivel
                              ? "border-emerald-500/30 bg-emerald-500/15 text-emerald-400 hover:border-rose-500/30 hover:bg-rose-500/15 hover:text-rose-400"
                              : "border-slate-700 bg-slate-800/80 text-slate-400 hover:border-emerald-500/30 hover:bg-emerald-500/15 hover:text-emerald-400"
                          )}
                        >
                          {visivel ? <Eye className="h-3.5 w-3.5" /> : <EyeOff className="h-3.5 w-3.5 text-amber-400" />}
                          <span>{visivel ? "Na Vitrine" : "Retirado (Oculto)"}</span>
                        </button>
                      </td>
                      <td className="px-4 py-2.5 text-right">
                        <Button variant="ghost" size="sm" onClick={() => setEditando(p)} title="Editar como a peça aparece no catálogo" aria-label={`Editar ${p.nome}`} className="h-8 px-2 text-sky-400 hover:bg-sky-500/10 hover:text-sky-300">
                          <Edit2 className="h-3.5 w-3.5" />
                        </Button>
                      </td>
                    </tr>
                  );
                })
              )}
            </tbody>
          </table>
        </div>
      </Card>

      {editando && <ModalMercadoria key={editando.id} produto={editando} aoFechar={() => setEditando(null)} />}
    </div>
  );
}
