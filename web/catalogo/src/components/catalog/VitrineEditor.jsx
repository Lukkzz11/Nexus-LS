import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import toast from "react-hot-toast";
import {
  ArrowDown,
  ArrowUp,
  Eye,
  EyeOff,
  FileDown,
  ImagePlus,
  Layers,
  Plus,
  Redo2,
  RotateCcw,
  Save,
  Sparkles,
  Trash2,
  Undo2
} from "lucide-react";
import { Card } from "../ui/Card";
import { Button } from "../ui/Button";
import { Input } from "../ui/Input";
import { Select } from "../ui/Select";
import { Badge } from "../ui/Badge";
import { cn } from "../../utils/cn";
import BannerFramer from "./BannerFramer";
import PainelModelos from "./editor/PainelModelos";
import PainelCores from "./editor/PainelCores";
import { ItemOrdenavel, ListaOrdenavel } from "./editor/ListaOrdenavel";
import PainelLayout from "./editor/PainelLayout";
import PainelProdutos from "./editor/PainelProdutos";
import PainelLoja from "./editor/PainelLoja";
import PainelPdf from "./editor/PainelPdf";
import PreviaPdf from "./editor/PreviaPdf";
import ImpressaoPdf from "./editor/ImpressaoPdf";
import { Segmentado } from "./editor/campos";
import { useHistorico } from "../../hooks/useHistorico";
import {
  ALIGNS,
  ALIGN_LABELS,
  BLOCK_LABELS,
  DESTINOS_BOTAO,
  DESTINO_LABELS,
  ENQUADRAMENTO_PADRAO,
  IMAGE_SIDES,
  IMAGE_SIDE_LABELS,
  MAX_SLIDES,
  SPACINGS,
  SPACING_LABELS,
  WIDTHS,
  WIDTH_LABELS,
  criarVitrinePadrao,
  limparTexto,
  salvarVitrine,
  sanitizarVitrine,
  selecionarProdutos,
  validarVitrine
} from "../../services/vitrineConfig";
import { uploadProductImageFile, deleteProductImage } from "../../services/productImageService";
import { isPdvOnline } from "../../services/pdvOfflineService";

const DISPOSITIVOS = [
  { id: "celular", rotulo: "Celular", largura: 390 },
  { id: "tablet", rotulo: "Tablet", largura: 768 },
  { id: "pc", rotulo: "Computador", largura: 1280 }
];

/** Ferramentas do painel lateral, agrupadas por assunto. */
const GRUPOS_PAINEL = [
  {
    titulo: "Aparência",
    itens: [
      { id: "modelos", rotulo: "Modelos" },
      { id: "cores", rotulo: "Cores" },
      { id: "layout", rotulo: "Layout e fontes" }
    ]
  },
  {
    titulo: "Conteúdo",
    itens: [
      { id: "produtos", rotulo: "Produtos" },
      { id: "loja", rotulo: "Loja e rodapé" },
      { id: "blocos", rotulo: "Blocos da página" },
      { id: "banners", rotulo: "Banners" }
    ]
  },
  {
    titulo: "Vendas e saída",
    itens: [
      { id: "regras", rotulo: "Atacado" },
      { id: "pdf", rotulo: "Catálogo em PDF" }
    ]
  }
];

const ID_PASTA_BANNERS = "_vitrine"; // produtos/{loja}/_vitrine/... segue a regra de Storage já publicada

function BotaoIcone({ rotulo, onClick, disabled, children }) {
  return (
    <button
      type="button"
      onClick={onClick}
      disabled={disabled}
      aria-label={rotulo}
      title={rotulo}
      className="grid h-8 w-8 shrink-0 place-items-center rounded-full text-slate-300 transition hover:bg-slate-800 hover:text-white disabled:opacity-25"
    >
      {children}
    </button>
  );
}

/** Mostra a vitrine real num iframe, na largura do aparelho escolhido, com a configuração em edição. */
function Previa({ tenantId, vitrine, selecionado, largura, onSelecionar, onEnquadrar }) {
  const caixaRef = useRef(null);
  const frameRef = useRef(null);
  const [larguraCaixa, setLarguraCaixa] = useState(0);
  const [pronta, setPronta] = useState(false);

  useEffect(() => {
    const el = caixaRef.current;
    if (!el) return undefined;
    const ro = new ResizeObserver(([entrada]) => setLarguraCaixa(entrada.contentRect.width));
    ro.observe(el);
    return () => ro.disconnect();
  }, []);

  const enviar = useCallback((mensagem) => {
    frameRef.current?.contentWindow?.postMessage(mensagem, window.location.origin);
  }, []);

  // A configuração segue por mensagem (sem recarregar o iframe) depois de uma pausa curta
  useEffect(() => {
    if (!pronta) return undefined;
    const t = setTimeout(() => enviar({ type: "lifesurf:vitrine-preview", vitrine }), 60);
    return () => clearTimeout(t);
  }, [vitrine, pronta, enviar]);

  useEffect(() => {
    if (pronta) enviar({ type: "lifesurf:highlight", id: selecionado });
  }, [selecionado, pronta, enviar]);

  useEffect(() => {
    const aoReceber = (e) => {
      if (e.origin !== window.location.origin || e.source !== frameRef.current?.contentWindow) return;
      const d = e.data;
      if (d?.type === "lifesurf:preview-ready") setPronta(true);
      if (d?.type === "lifesurf:select-block" && typeof d.id === "string") onSelecionar(d.id);
      if (d?.type === "lifesurf:frame-banner") onEnquadrar(typeof d.slideId === "string" ? d.slideId : undefined);
    };
    window.addEventListener("message", aoReceber);
    return () => window.removeEventListener("message", aoReceber);
  }, [onSelecionar, onEnquadrar]);

  const escala = larguraCaixa ? Math.min(1, larguraCaixa / largura) : 1;
  const altura = 760;

  return (
    <div ref={caixaRef} className="overflow-hidden rounded-2xl border border-slate-800 bg-slate-950" style={{ height: altura * escala + 2 }}>
      <iframe
        ref={frameRef}
        src={`${window.location.pathname}#/catalogo/${encodeURIComponent(tenantId)}?preview=1`}
        title="Pré-visualização da vitrine"
        className="mx-auto block origin-top-left border-0 bg-white"
        style={{
          width: largura,
          height: altura,
          transform: `scale(${escala})`,
          marginLeft: Math.max(0, (larguraCaixa - largura * escala) / 2)
        }}
      />
    </div>
  );
}

/** Produtos que o catálogo realmente mostra (mesmo filtro da vitrine pública). */
function produtosVisiveis(produtos) {
  return produtos.filter((p) => p.ativo !== false && p.ativoNoCatalogo !== false && p.ocultoNoCatalogo !== true);
}

export default function VitrineEditor({ tenantId, vitrineSalva, produtos, categorias, empresa }) {
  const inicial = useMemo(() => sanitizarVitrine(vitrineSalva), [vitrineSalva]);
  const [salvo, setSalvo] = useState(inicial);
  const historico = useHistorico(inicial);
  const rascunho = historico.valor;
  const setRascunho = historico.alterar;
  const [painel, setPainel] = useState("modelos");
  const [vista, setVista] = useState("online"); // online | pdf
  const [blocoId, setBlocoId] = useState("banner");
  const [slideId, setSlideId] = useState(inicial.banners[0]?.id ?? "");
  const [dispositivo, setDispositivo] = useState("pc");
  const [enviando, setEnviando] = useState(false);
  const [enviandoLogo, setEnviandoLogo] = useState(false);
  const [salvando, setSalvando] = useState(false);
  const [exportando, setExportando] = useState(false);
  const [publicadoEm, setPublicadoEm] = useState(null);
  const arquivoRef = useRef(null);
  // Imagens enviadas nesta sessão (apagadas se descartadas) e imagens salvas que foram trocadas (apagadas ao publicar)
  const enviadosNaSessao = useRef(new Set());
  const trocadasParaApagar = useRef(new Set());

  const sujo = JSON.stringify(rascunho) !== JSON.stringify(salvo);
  const bloco = rascunho.layout.blocks.find((b) => b.id === blocoId) ?? rascunho.layout.blocks[0];
  const slide = rascunho.banners.find((s) => s.id === slideId) ?? rascunho.banners[0];
  const erroValidacao = validarVitrine(rascunho);
  const visiveis = useMemo(() => produtosVisiveis(produtos), [produtos]);
  const produtosDoCatalogo = useMemo(() => selecionarProdutos(visiveis, rascunho.selecao), [visiveis, rascunho.selecao]);

  const atualizarBloco = (id, mudanca) =>
    setRascunho((r) => ({ ...r, layout: { blocks: r.layout.blocks.map((b) => (b.id === id ? { ...b, ...mudanca } : b)) } }));

  const mover = (id, para) =>
    setRascunho((r) => {
      const blocks = [...r.layout.blocks];
      const de = blocks.findIndex((b) => b.id === id);
      if (de === -1 || para < 0 || para >= blocks.length) return r;
      const [item] = blocks.splice(de, 1);
      blocks.splice(para, 0, item);
      return { ...r, layout: { blocks } };
    });

  const atualizarSlide = useCallback(
    (mudanca, id = slideId, chave) =>
      setRascunho((r) => ({ ...r, banners: r.banners.map((s) => (s.id === id ? { ...s, ...mudanca } : s)) }), chave),
    [slideId, setRascunho]
  );

  const moverSlide = (id, delta) =>
    setRascunho((r) => {
      const de = r.banners.findIndex((s) => s.id === id);
      const para = de + delta;
      if (para < 0 || para >= r.banners.length) return r;
      const lista = [...r.banners];
      const [item] = lista.splice(de, 1);
      lista.splice(para, 0, item);
      return { ...r, banners: lista };
    });

  const adicionarSlide = () => {
    const novo = {
      ...criarVitrinePadrao().banners[0],
      id: `slide-${Date.now().toString(36)}`,
      badge: "Novidade",
      title: "Novo slide",
      highlight: "",
      subtitle: "",
      ...ENQUADRAMENTO_PADRAO
    };
    setRascunho((r) => ({ ...r, banners: [...r.banners, novo] }));
    setSlideId(novo.id);
  };

  /** Imagem que saiu do rascunho: se foi enviada agora vai para a lista de "apagar se não for usada", senão idem. */
  const largarImagem = (path) => {
    if (path) trocadasParaApagar.current.add(path);
  };

  const removerSlide = (id) => {
    const i = rascunho.banners.findIndex((s) => s.id === id);
    const removido = rascunho.banners[i];
    const resto = rascunho.banners.filter((s) => s.id !== id);
    largarImagem(removido?.imagePath);
    setRascunho((r) => ({ ...r, banners: resto }));
    setSlideId(resto[Math.max(0, i - 1)]?.id ?? "");
  };

  const enviarImagem = async (file) => {
    if (!slide) return;
    setEnviando(true);
    try {
      const res = await uploadProductImageFile({ tenantId, productId: ID_PASTA_BANNERS, file, maxDimension: 1920 });
      enviadosNaSessao.current.add(res.path);
      largarImagem(slide.imagePath);
      atualizarSlide({ image: res.url, imagePath: res.path, ...ENQUADRAMENTO_PADRAO });
      toast.success("Imagem enviada. Dê duplo clique na prévia do slide para enquadrar.");
    } catch (err) {
      console.error("[VitrineEditor] Erro no envio da imagem do banner:", err);
      toast.error(err?.message || "Falha no envio da imagem.");
    } finally {
      setEnviando(false);
      if (arquivoRef.current) arquivoRef.current.value = "";
    }
  };

  const removerImagemDoSlide = () => {
    largarImagem(slide?.imagePath);
    atualizarSlide({ image: "", imagePath: "", ...ENQUADRAMENTO_PADRAO });
  };

  const enviarLogo = async (file, inputRef) => {
    setEnviandoLogo(true);
    try {
      const res = await uploadProductImageFile({ tenantId, productId: ID_PASTA_BANNERS, file, maxDimension: 800 });
      enviadosNaSessao.current.add(res.path);
      largarImagem(rascunho.loja.logoPath);
      setRascunho((r) => ({ ...r, loja: { ...r.loja, logoUrl: res.url, logoPath: res.path, mostrarLogo: true } }));
      toast.success("Logotipo enviado.");
    } catch (err) {
      console.error("[VitrineEditor] Erro no envio do logotipo:", err);
      toast.error(err?.message || "Falha no envio do logotipo.");
    } finally {
      setEnviandoLogo(false);
      if (inputRef?.current) inputRef.current.value = "";
    }
  };

  const removerLogo = () => {
    largarImagem(rascunho.loja.logoPath);
    setRascunho((r) => ({ ...r, loja: { ...r.loja, logoUrl: "", logoPath: "" } }));
  };

  /** Apaga do Storage só o que nenhuma versão (publicada ou em edição) usa mais. */
  const limparImagensSemUso = async (emUso) => {
    const candidatas = new Set([...trocadasParaApagar.current, ...enviadosNaSessao.current]);
    await Promise.all([...candidatas].filter((p) => !emUso.has(p)).map((p) => deleteProductImage(p)));
    trocadasParaApagar.current = new Set();
    enviadosNaSessao.current = new Set();
  };

  const imagensDe = (v) => new Set([...v.banners.map((b) => b.imagePath), v.loja.logoPath].filter(Boolean));

  const publicar = async () => {
    if (erroValidacao) return toast.error(erroValidacao);
    if (!isPdvOnline()) return toast.error("Sem conexão com a internet. Conecte-se para publicar a vitrine.");
    setSalvando(true);
    try {
      const gravado = await salvarVitrine(tenantId, rascunho);
      // Só agora (com o novo layout gravado) apaga do Storage o que ficou de fora
      await limparImagensSemUso(imagensDe(gravado));
      setSalvo(gravado);
      historico.reiniciar(gravado);
      setPublicadoEm(new Date());
      toast.success("Vitrine publicada! O catálogo online já mostra as mudanças.");
    } catch (err) {
      console.error("[VitrineEditor] Erro ao publicar a vitrine:", err);
      toast.error(
        err?.code === "permission-denied"
          ? "Sem permissão para gravar. Entre com um usuário autorizado e confira as regras do Firestore."
          : "Não foi possível publicar: " + (err?.message || "tente novamente.")
      );
    } finally {
      setSalvando(false);
    }
  };

  const descartar = () => {
    if (!window.confirm("Descartar todas as alterações não publicadas e voltar ao que está no ar? Você ainda pode desfazer.")) return;
    // Imagens enviadas e não publicadas não ficam órfãs no Storage (as que o publicado usa são mantidas)
    const emUsoPublicado = imagensDe(salvo);
    enviadosNaSessao.current.forEach((p) => {
      if (!emUsoPublicado.has(p)) deleteProductImage(p);
    });
    enviadosNaSessao.current = new Set();
    trocadasParaApagar.current = new Set();
    setRascunho(salvo);
    setSlideId(salvo.banners[0]?.id ?? "");
  };

  const restaurarPadrao = () => {
    if (!window.confirm("Restaurar o catálogo inteiro ao modelo padrão (cores, layout, textos, blocos e banners)? Nada é publicado até você clicar em Salvar e publicar, e dá para desfazer.")) return;
    setRascunho(criarVitrinePadrao());
    setSlideId("novidades");
  };

  const selecionarBloco = useCallback((id) => {
    setBlocoId(id);
    setPainel((p) => (p === "regras" ? "blocos" : p));
  }, []);

  const enquadrarDaPrevia = useCallback((id) => {
    setBlocoId("banner");
    setPainel("banners");
    if (id) setSlideId(id);
  }, []);

  const escolherPainel = (id) => {
    setPainel(id);
    setVista(id === "pdf" ? "pdf" : "online");
  };

  // Atalhos: Ctrl+Z desfaz, Ctrl+Y (ou Ctrl+Shift+Z) refaz. Não interfere ao digitar em campos de texto.
  useEffect(() => {
    const aoTeclar = (e) => {
      if (!(e.ctrlKey || e.metaKey)) return;
      const alvo = e.target;
      const digitando = alvo?.tagName === "TEXTAREA" || (alvo?.tagName === "INPUT" && !["range", "checkbox", "radio", "color", "button"].includes(alvo.type)) || alvo?.isContentEditable;
      if (digitando) return;
      const tecla = e.key.toLowerCase();
      if (tecla === "z" && !e.shiftKey) {
        e.preventDefault();
        historico.desfazer();
      } else if (tecla === "y" || (tecla === "z" && e.shiftKey)) {
        e.preventDefault();
        historico.refazer();
      }
    };
    window.addEventListener("keydown", aoTeclar);
    return () => window.removeEventListener("keydown", aoTeclar);
  }, [historico]);

  // Avisa antes de fechar a aba com alterações não publicadas
  useEffect(() => {
    if (!sujo) return undefined;
    const aviso = (e) => {
      e.preventDefault();
      e.returnValue = "";
    };
    window.addEventListener("beforeunload", aviso);
    return () => window.removeEventListener("beforeunload", aviso);
  }, [sujo]);

  const nomeArquivo = `Catalogo - ${rascunho.loja.nome || empresa?.nome || "Loja"} - ${new Date().toLocaleDateString("pt-BR").replace(/\//g, "-")}`;

  return (
    <div className="space-y-4">
      {/* BARRA SUPERIOR: situação, desfazer/refazer, restaurar, exportar e publicar */}
      <div className="flex flex-wrap items-center justify-between gap-3 rounded-2xl border border-slate-800 bg-slate-900/70 px-4 py-3">
        <div className="flex flex-wrap items-center gap-2">
          <h2 className="mr-1 text-sm font-semibold text-white">Editor do catálogo</h2>
          {sujo ? (
            <Badge variant="warning" size="sm">Alterações não publicadas</Badge>
          ) : (
            <Badge variant="success" size="sm">
              {publicadoEm ? `Publicado às ${publicadoEm.toLocaleTimeString("pt-BR", { hour: "2-digit", minute: "2-digit" })}` : "Tudo publicado"}
            </Badge>
          )}
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <Button variant="ghost" size="sm" onClick={historico.desfazer} disabled={!historico.podeDesfazer} title="Desfazer (Ctrl+Z)" leftIcon={<Undo2 className="h-4 w-4" />}>
            Desfazer
          </Button>
          <Button variant="ghost" size="sm" onClick={historico.refazer} disabled={!historico.podeRefazer} title="Refazer (Ctrl+Y)" leftIcon={<Redo2 className="h-4 w-4" />}>
            Refazer
          </Button>
          <Button variant="ghost" size="sm" onClick={restaurarPadrao} leftIcon={<RotateCcw className="h-4 w-4" />}>
            Restaurar padrão
          </Button>
          {sujo && (
            <Button variant="outline" size="sm" onClick={descartar}>
              Descartar alterações
            </Button>
          )}
          <Button variant="outline" size="sm" onClick={() => escolherPainel("pdf")} leftIcon={<FileDown className="h-4 w-4" />}>
            Exportar PDF
          </Button>
          <Button variant="primary" size="sm" onClick={publicar} isLoading={salvando} disabled={!sujo || Boolean(erroValidacao)} leftIcon={<Save className="h-4 w-4" />}>
            Salvar e publicar
          </Button>
        </div>
      </div>
      {erroValidacao && sujo && (
        <p role="alert" className="text-sm text-rose-400">
          {erroValidacao}
        </p>
      )}

      <div className="grid gap-5 lg:grid-cols-[380px_minmax(0,1fr)]">
        {/* PAINEL LATERAL: ferramentas */}
        <aside className="space-y-4 lg:max-h-[calc(100vh-11rem)] lg:overflow-y-auto lg:pr-1">
          <nav aria-label="Ferramentas do editor" className="space-y-2">
            {GRUPOS_PAINEL.map((g) => (
              <div key={g.titulo}>
                <p className="mb-1 text-[11px] text-slate-500">{g.titulo}</p>
                <div className="flex flex-wrap gap-1.5">
                  {g.itens.map((item) => (
                    <button
                      key={item.id}
                      type="button"
                      onClick={() => escolherPainel(item.id)}
                      aria-pressed={painel === item.id}
                      className={cn(
                        "rounded-full border px-3 py-1.5 text-xs font-medium transition",
                        painel === item.id ? "border-sky-400 bg-sky-500 text-slate-950" : "border-slate-700 bg-slate-900 text-slate-300 hover:text-white"
                      )}
                    >
                      {item.rotulo}
                    </button>
                  ))}
                </div>
              </div>
            ))}
          </nav>

          {painel === "modelos" && <PainelModelos vitrine={rascunho} alterar={setRascunho} />}
          {painel === "cores" && <PainelCores vitrine={rascunho} alterar={setRascunho} />}
          {painel === "layout" && <PainelLayout vitrine={rascunho} alterar={setRascunho} />}
          {painel === "produtos" && <PainelProdutos vitrine={rascunho} alterar={setRascunho} produtos={visiveis} />}
          {painel === "loja" && (
            <PainelLoja
              vitrine={rascunho}
              alterar={setRascunho}
              empresa={empresa}
              enviandoLogo={enviandoLogo}
              onEnviarLogo={enviarLogo}
              onRemoverLogo={removerLogo}
            />
          )}
          {painel === "pdf" && (
            <PainelPdf
              vitrine={rascunho}
              alterar={setRascunho}
              totalProdutos={produtosDoCatalogo.length}
              exportando={exportando}
              onExportar={() => setExportando(true)}
            />
          )}

          {painel === "blocos" && (
            <>
              <Card className="space-y-3 p-4">
                <h3 className="flex items-center gap-2 px-1 text-sm font-semibold text-white">
                  <Layers className="h-4 w-4 text-sky-400" />
                  Blocos da página
                </h3>
                <p className="px-1 text-xs text-slate-400">Arraste para mudar a ordem. Clique num bloco (aqui ou na prévia) para ajustá-lo.</p>
                <ListaOrdenavel
                  ids={rascunho.layout.blocks.map((b) => b.id)}
                  onReordenar={(ids) => setRascunho((r) => ({ ...r, layout: { blocks: ids.map((id) => r.layout.blocks.find((b) => b.id === id)) } }))}
                  rotulo="Blocos da página"
                  className="space-y-2"
                >
                  {rascunho.layout.blocks.map((b, i) => (
                    <ItemOrdenavel
                      key={b.id}
                      id={b.id}
                      rotuloAlca={`Arrastar ${BLOCK_LABELS[b.id]}`}
                      className={cn(
                        "rounded-xl border p-2 transition",
                        blocoId === b.id ? "border-sky-400 bg-sky-500/10" : "border-slate-800 bg-slate-950/50"
                      )}
                    >
                      {(alca) => (
                        <div className="flex items-center gap-1.5">
                          {alca}
                          <button
                            type="button"
                            className={cn("min-w-0 flex-1 truncate text-left text-sm", b.visible ? "text-slate-100" : "text-slate-500 line-through")}
                            onClick={() => setBlocoId(b.id)}
                          >
                            {i + 1}. {BLOCK_LABELS[b.id]}
                          </button>
                          <BotaoIcone rotulo={`Subir ${BLOCK_LABELS[b.id]}`} disabled={i === 0} onClick={() => mover(b.id, i - 1)}>
                            <ArrowUp className="h-4 w-4" />
                          </BotaoIcone>
                          <BotaoIcone rotulo={`Descer ${BLOCK_LABELS[b.id]}`} disabled={i === rascunho.layout.blocks.length - 1} onClick={() => mover(b.id, i + 1)}>
                            <ArrowDown className="h-4 w-4" />
                          </BotaoIcone>
                          <BotaoIcone
                            rotulo={b.visible ? `Ocultar ${BLOCK_LABELS[b.id]}` : `Mostrar ${BLOCK_LABELS[b.id]}`}
                            onClick={() => atualizarBloco(b.id, { visible: !b.visible })}
                          >
                            {b.visible ? <Eye className="h-4 w-4" /> : <EyeOff className="h-4 w-4" />}
                          </BotaoIcone>
                        </div>
                      )}
                    </ItemOrdenavel>
                  ))}
                </ListaOrdenavel>
              </Card>

              <Card className="space-y-4 p-5">
                <h3 className="text-sm font-semibold text-white">Ajustes: {BLOCK_LABELS[bloco.id]}</h3>
                <Segmentado rotulo="Largura" opcoes={WIDTHS} rotulos={WIDTH_LABELS} valor={bloco.width} onChange={(width) => atualizarBloco(bloco.id, { width })} />
                <Segmentado rotulo="Espaço acima" opcoes={SPACINGS} rotulos={SPACING_LABELS} valor={bloco.spacing} onChange={(spacing) => atualizarBloco(bloco.id, { spacing })} />
                <Segmentado rotulo="Alinhamento" opcoes={ALIGNS} rotulos={ALIGN_LABELS} valor={bloco.align} onChange={(align) => atualizarBloco(bloco.id, { align })} />
                {bloco.id === "banner" && (
                  <Segmentado rotulo="Lado das fotos (PC)" opcoes={IMAGE_SIDES} rotulos={IMAGE_SIDE_LABELS} valor={bloco.imageSide} onChange={(imageSide) => atualizarBloco(bloco.id, { imageSide })} />
                )}
              </Card>
            </>
          )}

          {painel === "banners" && (
            <>
              <Card className="space-y-3 p-4">
                <h3 className="px-1 text-sm font-semibold text-white">
                  Slides <span className="font-normal text-slate-500">({rascunho.banners.length}/{MAX_SLIDES})</span>
                </h3>
                <ListaOrdenavel
                  ids={rascunho.banners.map((b) => b.id)}
                  onReordenar={(ids) => setRascunho((r) => ({ ...r, banners: ids.map((id) => r.banners.find((b) => b.id === id)) }))}
                  rotulo="Slides do banner"
                  className="space-y-2"
                >
                  {rascunho.banners.map((s, i) => (
                    <ItemOrdenavel
                      key={s.id}
                      id={s.id}
                      rotuloAlca={`Arrastar slide ${i + 1}`}
                      className={cn("rounded-xl border p-2", s.id === slide?.id ? "border-sky-400 bg-sky-500/10" : "border-slate-800 bg-slate-950/50")}
                    >
                      {(alca) => (
                        <div className="flex items-center gap-1.5">
                          {alca}
                      <button type="button" className="min-w-0 flex-1 text-left" onClick={() => setSlideId(s.id)}>
                            <span className="block truncate text-sm text-slate-100">
                              {i + 1}. {s.title} {s.highlight}
                            </span>
                            <span className="text-xs text-slate-500">{s.image ? "Imagem própria" : "Colagem das novidades"}</span>
                          </button>
                          <BotaoIcone rotulo={`Subir slide ${i + 1}`} disabled={i === 0} onClick={() => moverSlide(s.id, -1)}>
                            <ArrowUp className="h-4 w-4" />
                          </BotaoIcone>
                          <BotaoIcone rotulo={`Descer slide ${i + 1}`} disabled={i === rascunho.banners.length - 1} onClick={() => moverSlide(s.id, 1)}>
                            <ArrowDown className="h-4 w-4" />
                          </BotaoIcone>
                          <BotaoIcone rotulo={`Remover slide ${i + 1}`} disabled={rascunho.banners.length <= 1} onClick={() => removerSlide(s.id)}>
                            <Trash2 className="h-4 w-4" />
                          </BotaoIcone>
                        </div>
                      )}
                    </ItemOrdenavel>
                  ))}
                </ListaOrdenavel>
                <Button variant="outline" size="sm" className="w-full" onClick={adicionarSlide} disabled={rascunho.banners.length >= MAX_SLIDES} leftIcon={<Plus className="h-4 w-4" />}>
                  Adicionar slide
                </Button>
              </Card>

              {slide && (
                <Card className="space-y-3 p-5">
                  <h3 className="text-sm font-semibold text-white">Texto e botão do slide</h3>
                  <Input label="Selo (opcional)" value={slide.badge} maxLength={40} onChange={(e) => atualizarSlide({ badge: limparTexto(e.target.value, 40) }, slideId, `slide:${slideId}:badge`)} />
                  <Input label="Título" required value={slide.title} maxLength={60} onChange={(e) => atualizarSlide({ title: e.target.value.slice(0, 60) }, slideId, `slide:${slideId}:title`)} />
                  <Input label="Destaque no fim do título (opcional)" value={slide.highlight} maxLength={30} onChange={(e) => atualizarSlide({ highlight: e.target.value.slice(0, 30) }, slideId, `slide:${slideId}:highlight`)} />
                  <Input label="Subtítulo (opcional)" value={slide.subtitle} maxLength={160} onChange={(e) => atualizarSlide({ subtitle: e.target.value.slice(0, 160) }, slideId, `slide:${slideId}:subtitle`)} />
                  <Input label="Texto do botão" required value={slide.ctaLabel} maxLength={30} onChange={(e) => atualizarSlide({ ctaLabel: e.target.value.slice(0, 30) }, slideId, `slide:${slideId}:cta`)} />
                  <Select label="O botão leva para" value={slide.ctaDestino} onChange={(e) => atualizarSlide({ ctaDestino: e.target.value })}>
                    {DESTINOS_BOTAO.map((d) => (
                      <option key={d} value={d}>{DESTINO_LABELS[d]}</option>
                    ))}
                  </Select>
                  {slide.ctaDestino === "categoria" && (
                    <Select label="Categoria" value={slide.ctaCategoria} onChange={(e) => atualizarSlide({ ctaCategoria: e.target.value })}>
                      <option value="">Escolha...</option>
                      {categorias.map((c) => (
                        <option key={c} value={c}>{c}</option>
                      ))}
                    </Select>
                  )}

                  <div className="space-y-2 rounded-xl border border-slate-800 bg-slate-950/50 p-3">
                    <p className="text-sm font-semibold text-white">Imagem de fundo</p>
                    <p className="text-xs text-slate-400">JPG, PNG ou WebP, de preferência na horizontal (ex.: 1920×900). Sem imagem, o slide mostra a colagem das novidades.</p>
                    <input ref={arquivoRef} type="file" accept="image/jpeg,image/png,image/webp" className="hidden" onChange={(e) => e.target.files?.[0] && enviarImagem(e.target.files[0])} />
                    <div className="flex flex-wrap gap-2">
                      <Button variant="outline" size="sm" isLoading={enviando} onClick={() => arquivoRef.current?.click()} leftIcon={<ImagePlus className="h-4 w-4" />}>
                        {slide.image ? "Trocar imagem" : "Enviar imagem"}
                      </Button>
                      {slide.image && (
                        <Button variant="ghost" size="sm" onClick={removerImagemDoSlide}>
                          Remover imagem
                        </Button>
                      )}
                    </div>
                  </div>
                </Card>
              )}

              <Card className="space-y-3 p-5">
                <h3 className="text-sm font-semibold text-white">Carrossel</h3>
                <Input
                  label="Trocar de slide a cada (segundos)"
                  type="number"
                  min="5"
                  max="120"
                  value={rascunho.carrossel.intervaloSeg}
                  onChange={(e) => setRascunho((r) => ({ ...r, carrossel: { ...r.carrossel, intervaloSeg: e.target.value } }), "carrossel:intervalo")}
                  helperText="Padrão: 40 s. Pausa quando o cliente passa o mouse e reinicia quando ele interage."
                />
                <Input
                  label="Peças no carrossel de novidades"
                  type="number"
                  min="3"
                  max="16"
                  value={rascunho.carrossel.novidadesQtd}
                  onChange={(e) => setRascunho((r) => ({ ...r, carrossel: { ...r.carrossel, novidadesQtd: e.target.value } }), "carrossel:qtd")}
                  helperText="Mostra primeiro as peças em destaque e depois as cadastradas mais recentemente."
                />
              </Card>
            </>
          )}

          {painel === "regras" && (
            <Card className="space-y-3 p-5">
              <h3 className="flex items-center gap-2 text-sm font-semibold text-white">
                <Sparkles className="h-4 w-4 text-sky-400" />
                Atacado
              </h3>
              <Input
                label="Mínimo de peças do pedido de atacado"
                type="number"
                min="1"
                max="1000"
                value={rascunho.atacado.minPecas}
                onChange={(e) => setRascunho((r) => ({ ...r, atacado: { minPecas: e.target.value } }), "atacado:min")}
                helperText="O atacado é um pedido separado do varejo: só pode ser finalizado a partir deste total de peças, já com o preço de atacado."
              />
            </Card>
          )}
        </aside>

        {/* ÁREA CENTRAL: pré-visualização */}
        <div className="min-w-0 space-y-4">
          <div className="flex flex-wrap items-center gap-2">
            <div className="flex rounded-full border border-slate-700 bg-slate-950/60 p-1" role="tablist" aria-label="Tipo de pré-visualização">
              {[
                ["online", "Vitrine online"],
                ["pdf", "Catálogo em PDF"]
              ].map(([id, rotulo]) => (
                <button
                  key={id}
                  type="button"
                  role="tab"
                  aria-selected={vista === id}
                  onClick={() => setVista(id)}
                  className={cn("rounded-full px-3.5 py-1.5 text-xs transition", vista === id ? "bg-sky-500 font-semibold text-slate-950" : "text-slate-300 hover:text-white")}
                >
                  {rotulo}
                </button>
              ))}
            </div>
            {vista === "online" &&
              DISPOSITIVOS.map((d) => (
                <button
                  key={d.id}
                  type="button"
                  onClick={() => setDispositivo(d.id)}
                  aria-pressed={dispositivo === d.id}
                  className={cn(
                    "rounded-full border px-3 py-1.5 text-xs transition",
                    dispositivo === d.id ? "border-sky-400 bg-sky-500 text-slate-950" : "border-slate-700 bg-slate-900 text-slate-300 hover:text-white"
                  )}
                >
                  {d.rotulo}
                </button>
              ))}
            <span className="ml-auto hidden text-xs text-slate-500 xl:inline">
              {vista === "online" ? "Clique num bloco da prévia para selecioná-lo; duplo clique no banner para enquadrar a imagem." : "As páginas abaixo são as mesmas que serão exportadas."}
            </span>
          </div>

          {vista === "online" ? (
            <Previa
              tenantId={tenantId}
              vitrine={rascunho}
              selecionado={blocoId}
              largura={DISPOSITIVOS.find((d) => d.id === dispositivo).largura}
              onSelecionar={selecionarBloco}
              onEnquadrar={enquadrarDaPrevia}
            />
          ) : (
            <PreviaPdf vitrine={rascunho} produtos={produtosDoCatalogo} empresa={empresa} atacadoMin={rascunho.atacado.minPecas} />
          )}

          {painel === "banners" && vista === "online" && slide && (
            <Card className="space-y-3 p-5">
              <div className="flex flex-wrap items-center justify-between gap-2">
                <h3 className="text-sm font-semibold text-white">Enquadramento do slide</h3>
                <span className="text-xs text-slate-500">
                  {slide.image ? "Arraste a imagem e use o zoom." : "Envie uma imagem para poder enquadrar."}
                </span>
              </div>
              <BannerFramer
                key={slide.id + (slide.image ? "-img" : "")}
                slide={slide}
                align={bloco.id === "banner" ? bloco.align : rascunho.layout.blocks.find((b) => b.id === "banner")?.align}
                imageSide={rascunho.layout.blocks.find((b) => b.id === "banner")?.imageSide}
                produtos={produtos}
                onChange={(enq) => atualizarSlide({ focusX: enq.focusX, focusY: enq.focusY, zoom: enq.zoom }, slideId, `enq:${slideId}`)}
                comecarAjustando={Boolean(slide.image)}
              />
            </Card>
          )}
        </div>
      </div>

      {exportando && (
        <ImpressaoPdf
          vitrine={rascunho}
          produtos={produtosDoCatalogo}
          empresa={empresa}
          atacadoMin={rascunho.atacado.minPecas}
          nomeArquivo={nomeArquivo}
          onFim={() => setExportando(false)}
        />
      )}
    </div>
  );
}
