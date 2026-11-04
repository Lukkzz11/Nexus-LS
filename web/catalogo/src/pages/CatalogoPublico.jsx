import CampoQuantidade from "../components/catalog/CampoQuantidade";
import { useState, useEffect, useMemo, useRef } from "react";
import { useParams, useSearchParams, useNavigate, Link } from "react-router-dom";
import confetti from "canvas-confetti";
import { useAuth } from "../security/AuthContext";
import {
  fetchPublicCatalog,
  fetchCompanyPublicInfo,
  submitPublicOrder,
  lerCachePublicoDoCatalogo,
  lerEmpresaPublicaDoCache,
  produtosVisiveisDe,
  getVisibleLocalCatalogProducts
} from "../services/catalogService";
import { getProductImageUrl, DEFAULT_PRODUCT_FALLBACK } from "../services/imageUploadService";
import { Button } from "../components/ui/Button";
import { Modal, ModalHeader, ModalBody, ModalFooter } from "../components/ui/Modal";
import { formatCurrency } from "../utils/formatters";
import { cn } from "../utils/cn";
import {
  sanitizarVitrine,
  fotosDoProduto,
  precoVarejoEfetivo,
  selecionarProdutos,
  WIDTH_CLASSES,
  SPACING_CLASSES
} from "../services/vitrineConfig";
import { variaveisCss, corEfetiva, textoLegivelSobre } from "../services/temaVitrine";
import HeroCarousel from "../components/catalog/vitrine/HeroCarousel";
import NovidadesCarousel from "../components/catalog/vitrine/NovidadesCarousel";
import ProductCardGlass from "../components/catalog/vitrine/ProductCardGlass";
import { getColorSwatchClass } from "../components/catalog/vitrine/cores";
import {
  caminhoDoProduto,
  consumirPontoDeRetorno,
  guardarPontoDeRetorno,
  lerSacola,
  salvarSacola
} from "../services/produtoCatalogo";
import {
  Search,
  CheckCircle2,
  Tag,
  Phone,
  MapPin,
  User,
  MessageCircle,
  Plus,
  Minus,
  Trash2,
  ArrowRight,
  ShoppingBag,
  ShieldCheck,
  Truck,
  CreditCard,
  QrCode,
  DollarSign,
  Share2,
  Copy,
  Check,
  Building,
  Store,
  Edit2,
  Sparkles,
  ChevronRight
} from "lucide-react";
import { CIDADE_UF_PADRAO } from "../config/localizacao";

const LOTE_VITRINE = 24;
const chaveJanelaVitrine = (tenantId) => `lifesurf_vitrine_visiveis_${tenantId}`;

function lerJanelaVitrine(tenantId) {
  try {
    const salvo = JSON.parse(sessionStorage.getItem(chaveJanelaVitrine(tenantId)) || "null");
    return salvo && typeof salvo.chave === "string" && Number.isFinite(salvo.n) ? salvo : null;
  } catch {
    return null;
  }
}

/** Avisa o Editor visual (janela pai da pré-visualização). Só fala com a própria origem. */
function enviarAoEditor(mensagem) {
  try {
    window.parent?.postMessage(mensagem, window.location.origin);
  } catch {
    /* sem janela pai: segue */
  }
}

export default function CatalogoPublico() {
  const { isAuthenticated } = useAuth();
  const { companyId } = useParams();
  const tenantId = companyId || "lifesurf";

  // Pré-visualização dentro do Editor visual (?preview=1): recebe a configuração por mensagem, sem salvar
  const navigate = useNavigate();
  const [searchParams, setSearchParams] = useSearchParams();
  const emPrevia = searchParams.get("preview") === "1" && window.parent !== window;
  const [previewVitrine, setPreviewVitrine] = useState(null);
  const [blocoSelecionado, setBlocoSelecionado] = useState("banner");
  const refVitrine = useRef(null);
  const refNovidades = useRef(null);

  // Cache primeiro: com empresa e produtos guardados da última visita, a vitrine aparece sem esperar a rede e atualiza por trás
  const [cacheInicial] = useState(() => (emPrevia ? null : lerCachePublicoDoCatalogo(tenantId)));
  const [empresaInicial] = useState(() => (emPrevia ? null : lerEmpresaPublicaDoCache(tenantId)));
  const [companyInfo, setCompanyInfo] = useState(empresaInicial);
  const [produtos, setProdutos] = useState(cacheInicial?.produtos ?? []);
  const [infoPronta, setInfoPronta] = useState(Boolean(empresaInicial));
  const [produtosProntos, setProdutosProntos] = useState(Boolean(cacheInicial));
  const loading = !infoPronta || !produtosProntos;
  const primeiraCarga = useRef(true);
  const [janelaVitrine, setJanelaVitrine] = useState(() => lerJanelaVitrine(tenantId));

  // Modo de Compra: Varejo vs Atacado (Lojista)
  // Filtros e tabela ficam no endereço (?cat=&q=&pag=&tabela=): a página do produto volta para o mesmo ponto
  const [tipoTabela, setTipoTabela] = useState(searchParams.get("tabela") === "atacado" ? "atacado" : "varejo"); // "varejo" | "atacado"

  // Filtros de Vitrine
  const [busca, setBusca] = useState(searchParams.get("q") || "");
  const [categoriaAtiva, setCategoriaAtiva] = useState(searchParams.get("cat") || "Todos");
  const [pagina, setPagina] = useState(Math.max(1, Number(searchParams.get("pag")) || 1));

  // Modal de Seleção de Variação (Tamanho & Cor)
  const [produtoModal, setProdutoModal] = useState(null);
  const [tamanhoSelecionado, setTamanhoSelecionado] = useState("");
  const [corSelecionada, setCorSelecionada] = useState("");
  const [quantidadeItem, setQuantidadeItem] = useState(1);

  // Carrinho de Compras
  // A sacola fica guardada no navegador (por empresa) e é a mesma da página do produto
  const [carrinho, setCarrinho] = useState(() => (emPrevia ? [] : lerSacola(tenantId)));
  const [carrinhoAberto, setCarrinhoAberto] = useState(Boolean(searchParams.get("sacola")));
  // Varejo e atacado são pedidos SEPARADOS: cada um tem a sua sacola
  const [sacolaAtiva, setSacolaAtiva] = useState(searchParams.get("sacola") === "atacado" ? "atacado" : "varejo");
  const [fotoModalIdx, setFotoModalIdx] = useState(0);

  // Checkout de Fricção Zero
  const [nomeCliente, setNomeCliente] = useState("");
  const [whatsappCliente, setWhatsappCliente] = useState("");
  const [tipoEntrega, setTipoEntrega] = useState("entrega"); // "entrega" | "retirada"
  const [enderecoCliente, setEnderecoCliente] = useState("");
  const [cidadeCliente, setCidadeCliente] = useState("");
  const [bairroCliente, setBairroCliente] = useState("");
  const [formaPagamento, setFormaPagamento] = useState("pix");
  const [observacoes, setObservacoes] = useState("");
  const [enviandoPedido, setEnviandoPedido] = useState(false);

  // Feedback de Cópia
  const [pixCopiado, setPixCopiado] = useState(false);
  const [linkCopiado, setLinkCopiado] = useState(false);

  // Comprovante de Sucesso
  const [pedidoConcluido, setPedidoConcluido] = useState(null);
  const [modalSucessoAberto, setModalSucessoAberto] = useState(false);

  // 1. CARREGAMENTO DOS DADOS PÚBLICOS
  useEffect(() => {
    let ativo = true;

    // Trocou de loja sem recarregar a página: recomeça pelo cache da nova loja
    if (!primeiraCarga.current) {
      const cache = emPrevia ? null : lerCachePublicoDoCatalogo(tenantId);
      const empresa = emPrevia ? null : lerEmpresaPublicaDoCache(tenantId);
      setProdutos(cache?.produtos ?? []);
      setProdutosProntos(Boolean(cache));
      setCompanyInfo(empresa);
      setInfoPronta(Boolean(empresa));
    }
    primeiraCarga.current = false;

    // Empresa e produtos chegam por conta própria: um não espera o outro (a tela só espera a empresa, que é leve)
    fetchCompanyPublicInfo(tenantId)
      .then((info) => ativo && setCompanyInfo(info))
      .catch((err) => console.error("[CatalogoPublico] Erro ao carregar a empresa:", err))
      .finally(() => ativo && setInfoPronta(true));
    fetchPublicCatalog(tenantId)
      .then((prods) => ativo && setProdutos(prods))
      .catch((err) => console.error("[CatalogoPublico] Erro ao carregar os produtos:", err))
      .finally(() => ativo && setProdutosProntos(true));

    // Catálogo editado no painel: usa o que o admin acabou de salvar (sem reler o Firestore inteiro)
    const aoAtualizarCatalogo = (e) => {
      const detalhe = e?.detail;
      const lista =
        detalhe?.companyId === tenantId && Array.isArray(detalhe.products)
          ? produtosVisiveisDe(detalhe.products)
          : getVisibleLocalCatalogProducts(tenantId);
      if (lista.length > 0) setProdutos(lista);
    };
    // Outra aba salvou: só interessa a chave do catálogo desta loja
    const aoMudarArmazenamento = (e) => {
      if (e.key && e.key !== `lifesurf_catalog_products_${tenantId}`) return;
      aoAtualizarCatalogo();
    };

    window.addEventListener("lifesurf:catalog_updated", aoAtualizarCatalogo);
    window.addEventListener("storage", aoMudarArmazenamento);

    return () => {
      ativo = false;
      window.removeEventListener("lifesurf:catalog_updated", aoAtualizarCatalogo);
      window.removeEventListener("storage", aoMudarArmazenamento);
    };
    // emPrevia não entra: só decide a leitura do cache e não muda durante a vida da página
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tenantId]);


  // Configuração visual (Editor visual): salva em empresas/{id}.vitrine; sem ela vale o padrão
  const vitrineCfg = useMemo(
    () => sanitizarVitrine(previewVitrine ?? companyInfo?.vitrine),
    [previewVitrine, companyInfo]
  );
  const atacadoMin = vitrineCfg.atacado.minPecas;
  const cfgProdutos = vitrineCfg.produtos;
  const variaveis = useMemo(() => variaveisCss(vitrineCfg), [vitrineCfg]);

  // Produtos que o editor escolheu mostrar (todos, categorias ou peças específicas) e a ordem
  const produtosDoCatalogo = useMemo(() => selecionarProdutos(produtos, vitrineCfg.selecao), [produtos, vitrineCfg.selecao]);

  // 2. EXTRAÇÃO E FILTRO DE CATEGORIAS
  const categorias = useMemo(() => {
    const cats = new Set(["Todos"]);
    produtosDoCatalogo.forEach((p) => {
      if (p.categoria && p.categoria.trim()) {
        cats.add(p.categoria.trim());
      }
    });
    return Array.from(cats);
  }, [produtosDoCatalogo]);

  const produtosFiltrados = useMemo(() => {
    return produtosDoCatalogo.filter((p) => {
      const matchCat = categoriaAtiva === "Todos" || p.categoria === categoriaAtiva;
      const matchBusca =
        !busca.trim() ||
        p.nome?.toLowerCase().includes(busca.toLowerCase()) ||
        p.referencia?.toLowerCase().includes(busca.toLowerCase()) ||
        p.categoria?.toLowerCase().includes(busca.toLowerCase());
      return matchCat && matchBusca;
    });
  }, [produtosDoCatalogo, categoriaAtiva, busca]);

  // Páginas da vitrine (0 = todos numa página só)
  const porPagina = cfgProdutos.porPagina;
  const totalPaginas = porPagina > 0 ? Math.max(1, Math.ceil(produtosFiltrados.length / porPagina)) : 1;
  const paginaAtual = Math.min(pagina, totalPaginas);
  // Sem páginas (porPagina 0): desenha em blocos de LOTE_VITRINE e mostra "Ver mais". Muitos cartões de uma vez
  // pesam no celular. O tamanho do bloco fica na sessão, então voltar da página do produto reencontra a rolagem.
  const assinaturaFiltro = `${categoriaAtiva}|${busca.trim()}|${tipoTabela}`;
  const quantidadeVisivel = janelaVitrine?.chave === assinaturaFiltro ? janelaVitrine.n : LOTE_VITRINE;
  const janelaAtiva = porPagina === 0 && !emPrevia;
  const restantesNaVitrine = janelaAtiva ? Math.max(0, produtosFiltrados.length - quantidadeVisivel) : 0;
  const verMaisProdutos = () => {
    const nova = { chave: assinaturaFiltro, n: quantidadeVisivel + LOTE_VITRINE };
    setJanelaVitrine(nova);
    try {
      sessionStorage.setItem(chaveJanelaVitrine(tenantId), JSON.stringify(nova));
    } catch {
      /* sem sessionStorage: o bloco só não é lembrado ao voltar */
    }
  };
  const produtosDaPagina =
    porPagina > 0
      ? produtosFiltrados.slice((paginaAtual - 1) * porPagina, paginaAtual * porPagina)
      : janelaAtiva
        ? produtosFiltrados.slice(0, quantidadeVisivel)
        : produtosFiltrados;

  // Dados da loja no catálogo: o que foi personalizado no editor ou, se vazio, o cadastro da empresa
  const loja = vitrineCfg.loja;
  const contato = {
    nome: loja.nome || companyInfo?.nome || "Loja",
    telefone: loja.telefone || companyInfo?.telefone || "",
    whatsapp: loja.whatsapp || companyInfo?.whatsapp || "",
    instagram: loja.instagram || companyInfo?.instagram || "",
    facebook: loja.facebook || companyInfo?.facebook || "",
    email: loja.email || companyInfo?.email || "",
    endereco:
      loja.endereco ||
      [companyInfo?.endereco, companyInfo?.bairro, companyInfo?.cidade].filter(Boolean).join(", ")
  };
  const cabecalhoEscuro = textoLegivelSobre(corEfetiva(vitrineCfg.tema, "cabecalho")) === "#ffffff";

  useEffect(() => {
    if (!emPrevia) return undefined;
    const aoReceber = (e) => {
      if (e.origin !== window.location.origin || e.source !== window.parent) return;
      const dados = e.data;
      if (dados?.type === "lifesurf:vitrine-preview") setPreviewVitrine(dados.vitrine);
      if (dados?.type === "lifesurf:highlight" && typeof dados.id === "string") setBlocoSelecionado(dados.id);
    };
    window.addEventListener("message", aoReceber);
    // Ajuste de produto no editor (visível, destaque, promoção): atualiza a lista sem recarregar a prévia
    const aoMudarProdutos = (e) => setProdutos(produtosVisiveisDe(e.detail));
    window.parent.addEventListener("nexus:produtos", aoMudarProdutos);
    enviarAoEditor({ type: "lifesurf:preview-ready" });
    return () => {
      window.removeEventListener("message", aoReceber);
      window.parent.removeEventListener("nexus:produtos", aoMudarProdutos);
    };
  }, [emPrevia]);

  const acaoDoBotaoBanner = (slide) => {
    if (emPrevia) return;
    const rolarPara = (ref) => ref.current?.scrollIntoView({ behavior: "smooth", block: "start" });
    if (slide.ctaDestino === "atacado") setTipoTabela("atacado");
    if (slide.ctaDestino === "categoria") setCategoriaAtiva(slide.ctaCategoria || "Todos");
    rolarPara(slide.ctaDestino === "novidades" && refNovidades.current ? refNovidades : refVitrine);
  };

  // Clique numa peça: abre a página própria dela (endereço compartilhável). Na pré-visualização do
  // Editor visual continua o seletor rápido, para a prévia não sair da página do editor.
  const abrirProduto = (produto) => {
    if (emPrevia) {
      abrirSeletorVariacao(produto);
      return;
    }
    guardarPontoDeRetorno(tenantId, { search: window.location.search, rolagem: window.scrollY });
    navigate(`${caminhoDoProduto(tenantId, produto.id)}${tipoTabela === "atacado" ? "?tabela=atacado" : ""}`, {
      state: { deCatalogo: true }
    });
  };

  // Filtros no endereço: voltar da página do produto reabre a mesma busca, categoria, página e tabela
  useEffect(() => {
    if (emPrevia) return;
    const atual = new URLSearchParams(window.location.search);
    const novo = new URLSearchParams(window.location.search);
    const definir = (chave, valor) => (valor ? novo.set(chave, valor) : novo.delete(chave));
    definir("q", busca.trim());
    definir("cat", categoriaAtiva !== "Todos" ? categoriaAtiva : "");
    definir("pag", pagina > 1 ? String(pagina) : "");
    definir("tabela", tipoTabela === "atacado" ? "atacado" : "");
    novo.delete("sacola");
    if (novo.toString() !== atual.toString()) setSearchParams(novo, { replace: true });
  }, [busca, categoriaAtiva, pagina, tipoTabela, emPrevia, setSearchParams]);

  // A sacola persiste no navegador (mesma da página do produto)
  useEffect(() => {
    if (!emPrevia) salvarSacola(tenantId, carrinho);
  }, [carrinho, tenantId, emPrevia]);

  // Ao voltar da página do produto, devolve a rolagem de onde o cliente saiu
  useEffect(() => {
    if (loading || emPrevia) return;
    const ponto = consumirPontoDeRetorno(tenantId);
    if (ponto?.rolagem > 0) requestAnimationFrame(() => window.scrollTo(0, ponto.rolagem));
  }, [loading, emPrevia, tenantId]);

  // 3. ABERTURA DO MODAL DE VARIAÇÃO (pré-visualização do editor)
  const abrirSeletorVariacao = (produto) => {
    setProdutoModal(produto);
    setFotoModalIdx(0);

    // Pré-seleciona primeiro tamanho disponível
    const tamanhos = produto.gradeTamanhos ? Object.keys(produto.gradeTamanhos) : ["P", "M", "G", "GG"];
    setTamanhoSelecionado(tamanhos[0] || "M");

    // Pré-seleciona primeira cor disponível
    let cores = ["Padrão"];
    if (Array.isArray(produto.cores) && produto.cores.length > 0) {
      cores = produto.cores;
    } else if (typeof produto.cores === "string" && produto.cores.trim()) {
      cores = produto.cores.split(",").map((c) => c.trim());
    }
    setCorSelecionada(cores[0] || "Padrão");
    setQuantidadeItem(1);
  };

  // 4. ADICIONAR ITEM À SACOLA
  const handleAdicionarAoCarrinho = () => {
    if (!produtoModal) return;

    const precoVarejoCheio = Number(produtoModal.precoVarejo) || 0;
    const precoVarejo = precoVarejoEfetivo(produtoModal);
    const precoAtacado = Number(produtoModal.precoAtacado) || precoVarejoCheio;
    const precoUnitario = tipoTabela === "atacado" ? precoAtacado : precoVarejo;

    const itemKey = `${produtoModal.id}-${tamanhoSelecionado}-${corSelecionada}-${tipoTabela}`;

    setCarrinho((prev) => {
      const index = prev.findIndex((it) => it.key === itemKey);
      if (index >= 0) {
        const novo = [...prev];
        novo[index].quantidade += quantidadeItem;
        return novo;
      }
      return [
        ...prev,
        {
          key: itemKey,
          id: produtoModal.id,
          nome: produtoModal.nome,
          referencia: produtoModal.referencia || "",
          fotoUrl: getProductImageUrl(produtoModal) || null,
          tamanho: tamanhoSelecionado,
          cor: corSelecionada,
          tipoVenda: tipoTabela,
          precoUnitario,
          precoVarejoReferencia: precoVarejoCheio,
          quantidade: quantidadeItem
        }
      ];
    });

    setProdutoModal(null);
    setSacolaAtiva(tipoTabela);
    setCarrinhoAberto(true);
  };

  // 5. MANIPULAÇÃO DO CARRINHO
  const alterarQtdCarrinho = (key, delta) => {
    setCarrinho((prev) =>
      prev
        .map((it) => {
          if (it.key === key) {
            const novaQtd = it.quantidade + delta;
            return novaQtd > 0 ? { ...it, quantidade: novaQtd } : null;
          }
          return it;
        })
        .filter(Boolean)
    );
  };

  const definirQtdCarrinho = (key, qtd) => {
    setCarrinho((prev) => prev.map((it) => (it.key === key ? { ...it, quantidade: Math.max(1, qtd) } : it)));
  };

  const removerDoCarrinho = (key) => {
    setCarrinho((prev) => prev.filter((it) => it.key !== key));
  };

  const itensSacola = useMemo(
    () => carrinho.filter((it) => (it.tipoVenda || "varejo") === sacolaAtiva),
    [carrinho, sacolaAtiva]
  );

  const pecasPorSacola = useMemo(() => {
    const total = { varejo: 0, atacado: 0 };
    carrinho.forEach((it) => {
      total[(it.tipoVenda || "varejo") === "atacado" ? "atacado" : "varejo"] += it.quantidade;
    });
    return total;
  }, [carrinho]);

  const subtotalSacola = useMemo(() => {
    return itensSacola.reduce((acc, it) => acc + it.precoUnitario * it.quantidade, 0);
  }, [itensSacola]);

  const totalItensSacola = useMemo(() => {
    return itensSacola.reduce((acc, it) => acc + it.quantidade, 0);
  }, [itensSacola]);

  // Atacado: pedido separado e só vale a partir do mínimo de peças definido no Editor visual
  const faltamParaAtacado = sacolaAtiva === "atacado" ? Math.max(0, atacadoMin - totalItensSacola) : 0;

  const economiaTotal = useMemo(() => {
    return itensSacola.reduce((acc, it) => {
      if (it.tipoVenda === "atacado" && it.precoVarejoReferencia > it.precoUnitario) {
        return acc + (it.precoVarejoReferencia - it.precoUnitario) * it.quantidade;
      }
      return acc;
    }, 0);
  }, [itensSacola]);

  // 6. FINALIZAR PEDIDO (CHECKOUT SEM SENHA)
  const handleFinalizarPedido = async (e) => {
    if (e) e.preventDefault();

    if (itensSacola.length === 0) {
      alert("Sua sacola está vazia!");
      return;
    }
    if (sacolaAtiva === "atacado" && totalItensSacola < atacadoMin) {
      alert(`O pedido de atacado precisa de pelo menos ${atacadoMin} peças. Faltam ${atacadoMin - totalItensSacola}.`);
      return;
    }
    if (!nomeCliente.trim()) {
      alert("Por favor, digite seu nome completo.");
      return;
    }
    if (!whatsappCliente.trim()) {
      alert("Por favor, digite seu WhatsApp com DDD para contato.");
      return;
    }
    if (tipoEntrega === "entrega" && !enderecoCliente.trim()) {
      alert("Por favor, digite seu endereço de entrega (Rua, Número, Bairro).");
      return;
    }

    setEnviandoPedido(true);
    try {
      const enderecoFormatado =
        tipoEntrega === "retirada"
          ? "Retirar no Balcão da Fábrica / Loja"
          : `${enderecoCliente.trim()}${bairroCliente ? `, Bairro: ${bairroCliente.trim()}` : ""}${cidadeCliente ? ` - ${cidadeCliente.trim()}` : ""}`;

      const payload = {
        nome: nomeCliente.trim(),
        whatsapp: whatsappCliente.trim(),
        endereco: enderecoFormatado,
        tipoEntrega,
        formaPagamento,
        tipoVenda: sacolaAtiva,
        observacoes: observacoes.trim(),
        subtotal: subtotalSacola,
        total: subtotalSacola,
        economia: economiaTotal,
        itens: itensSacola.map((it) => ({
          id: it.id,
          nome: it.nome,
          referencia: it.referencia,
          tamanho: it.tamanho,
          cor: it.cor,
          tipoVenda: it.tipoVenda || sacolaAtiva,
          quantidade: it.quantidade,
          precoUnitario: it.precoUnitario,
          subtotal: it.quantidade * it.precoUnitario
        }))
      };

      const pedidoGravado = await submitPublicOrder(tenantId, payload);

      setPedidoConcluido({ ...pedidoGravado, tipoVenda: sacolaAtiva });
      // Só a sacola finalizada é esvaziada; a outra (varejo/atacado) continua
      setCarrinho((prev) => prev.filter((it) => (it.tipoVenda || "varejo") !== sacolaAtiva));
      setCarrinhoAberto(false);
      setModalSucessoAberto(true);

      confetti({
        particleCount: 100,
        spread: 75,
        origin: { y: 0.6 }
      });
    } catch (err) {
      console.error("[CatalogoPublico] Falha ao enviar pedido:", err);
      alert("Ocorreu um erro ao registrar seu pedido. Tente novamente.");
    } finally {
      setEnviandoPedido(false);
    }
  };

  // Link para enviar mensagem pronta no WhatsApp da loja
  const abrirWhatsAppComPedido = () => {
    if (!pedidoConcluido) return;

    const itensTexto = pedidoConcluido.itens
      ?.map(
        (it) =>
          `• ${it.quantidade}x ${it.nome} (${it.tamanho}/${it.cor}) - ${formatCurrency(it.subtotal)}`
      )
      .join("\n");

    const zapLoja = companyInfo?.whatsapp || "";
    if (!zapLoja) {
      alert("Esta loja ainda não cadastrou um WhatsApp. Guarde o número do pedido e fale com a loja pelos contatos do rodapé.");
      return;
    }
    const mensagem = `Olá, equipe *${companyInfo?.nome || "Loja"}*! 👋\n\nAcabei de fazer o pedido *#${pedidoConcluido.numeroPedido}* pelo Catálogo Online:\n\n${itensTexto}\n\n*Total:* ${formatCurrency(pedidoConcluido.total)}\n*Modo:* ${pedidoConcluido.tipoVenda === "atacado" ? "Atacado / Lojista" : "Varejo"}\n*Pagamento:* ${pedidoConcluido.formaPagamento.toUpperCase()}\n*Cliente:* ${pedidoConcluido.cliente?.nome}\n*WhatsApp:* ${pedidoConcluido.cliente?.telefone}\n*Entrega:* ${pedidoConcluido.cliente?.endereco}\n\nPoderia confirmar a disponibilidade das peças e chave para pagamento?`;

    window.open(
      `https://wa.me/55${zapLoja.replace(/\D/g, "")}?text=${encodeURIComponent(mensagem)}`,
      "_blank"
    );
  };

  // Copia a Chave PIX da empresa
  const copiarChavePix = () => {
    const chave = companyInfo?.chavePix || companyInfo?.cnpj || "";
    if (!chave) return;
    navigator.clipboard.writeText(chave);
    setPixCopiado(true);
    setTimeout(() => setPixCopiado(false), 2500);
  };

  // Compartilha o Catálogo Online
  const compartilharCatalogo = () => {
    if (navigator.share) {
      navigator
        .share({
          title: companyInfo?.nome || "Catálogo digital",
          text: "Confira as novidades e coleção surfwear direto da confecção!",
          url: window.location.href
        })
        .catch(() => { });
    } else {
      navigator.clipboard.writeText(window.location.href);
      setLinkCopiado(true);
      setTimeout(() => setLinkCopiado(false), 2500);
    }
  };

  return (
    <div
      className={cn("vt-bg min-h-screen flex flex-col font-sans selection:bg-neutral-900 selection:text-white pb-20 md:pb-0", !vitrineCfg.estilo.vidro && "vt-flat")}
      style={variaveis}
      data-tema={vitrineCfg.tema.modelo}
      data-fonte={vitrineCfg.tema.tipografia.fonte === "sistema" ? undefined : "custom"}
    >
      {/* 0. BARRA DE ADMINISTRAÇÃO (QUANDO LOGADO NO ERP) */}
      {isAuthenticated && !emPrevia && (
        <div className="bg-neutral-900 border-b border-neutral-800 px-4 py-2 flex flex-wrap items-center justify-between text-xs text-neutral-300 gap-2 sticky top-0 z-50 shadow-sm">
          <div className="flex items-center gap-2">
            <span className="w-2 h-2 rounded-full bg-emerald-400 animate-pulse"></span>
            <span className="font-semibold text-white">Visualização de Cliente</span>
            <span className="text-neutral-400 hidden sm:inline">• Modo Administrador Ativo</span>
          </div>
          <Link
            to="/gerenciar-catalogo"
            className="flex items-center gap-1.5 px-3 py-1 rounded-md bg-white hover:bg-neutral-100 text-neutral-900 font-semibold text-xs transition-colors shadow-sm cursor-pointer ml-auto"
          >
            <Edit2 className="w-3.5 h-3.5" />
            <span>Editar Catálogo & Mercadorias</span>
          </Link>
        </div>
      )}

      {/* 1. BARRA DE AVISO (texto editável no Editor visual) */}
      {(tipoTabela === "atacado" || vitrineCfg.textos.anuncio) && (
        <div className="vt-accent py-2 px-4 text-center text-xs font-medium tracking-tight flex items-center justify-center gap-2">
          <Sparkles className="w-3.5 h-3.5" />
          <span>
            {tipoTabela === "atacado"
              ? `TABELA DE ATACADO: pedido separado do varejo, a partir de ${atacadoMin} peças, com preço de fábrica.`
              : vitrineCfg.textos.anuncio}
          </span>
        </div>
      )}

      {/* 2. CABEÇALHO DA VITRINE (logotipo, nome e contatos editáveis no Editor visual) */}
      <header className="vt-header sticky top-0 z-40 vt-glass-strong border-x-0 border-t-0 rounded-none" data-bloco-fixo="cabecalho">
        <div
          className={cn(
            "mx-auto flex max-w-7xl gap-x-4 gap-y-2 py-2",
            loja.alinhamento === "centro" ? "flex-col items-center" : "min-h-16 items-center justify-between"
          )}
          style={{ paddingInline: "var(--vt-margin, 24px)" }}
        >
          {/* Logotipo e nome da loja */}
          <div className="flex items-center gap-3">
            {loja.mostrarLogo && (
              <img
                src={loja.logoUrl || "/assets/logo-white.png"}
                alt={contato.nome}
                style={{ height: loja.logoAltura, filter: loja.logoUrl || cabecalhoEscuro ? undefined : "invert(1)" }}
                className="w-auto max-w-[220px] object-contain select-none"
                onError={(e) => {
                  e.currentTarget.style.display = "none";
                  e.currentTarget.nextElementSibling?.classList.remove("hidden");
                }}
              />
            )}

            {/* Nome (aparece quando escolhido, ou no lugar do logotipo se a imagem falhar) */}
            <div className={cn("items-center gap-2.5", loja.mostrarNome || !loja.mostrarLogo ? "flex" : "hidden")}>
              <div>
                <h1 className="vt-store text-sm font-bold tracking-tight uppercase leading-tight">{contato.nome}</h1>
                {companyInfo?.cidade && <p className="vt-sub text-[10px] uppercase tracking-wider">{companyInfo.cidade}</p>}
              </div>
            </div>
          </div>

          {/* Alternador Varejo vs Atacado */}
          <div className="hidden md:flex items-center p-1 rounded-full border text-xs font-medium" style={{ borderColor: "var(--vt-border)" }}>
            <button
              type="button"
              onClick={() => setTipoTabela("varejo")}
              className={cn(
                "px-3.5 py-1 rounded-full transition-all cursor-pointer flex items-center gap-1.5",
                tipoTabela === "varejo" ? "vt-btn font-semibold shadow-xs" : "opacity-70 hover:opacity-100"
              )}
            >
              <Store className="w-3.5 h-3.5" />
              <span>Varejo</span>
            </button>
            <button
              type="button"
              onClick={() => setTipoTabela("atacado")}
              className={cn(
                "px-3.5 py-1 rounded-full transition-all cursor-pointer flex items-center gap-1.5",
                tipoTabela === "atacado" ? "vt-btn font-semibold shadow-xs" : "opacity-70 hover:opacity-100"
              )}
            >
              <Building className="w-3.5 h-3.5" />
              <span>Atacado (Lojista)</span>
            </button>
          </div>

          {/* Ações do Header */}
          <div className="flex items-center gap-2">
            <button
              type="button"
              onClick={compartilharCatalogo}
              className="p-2 rounded-full border transition-colors cursor-pointer hidden sm:flex hover:opacity-80"
              style={{ borderColor: "var(--vt-border)" }}
              title="Compartilhar catálogo"
            >
              {linkCopiado ? <Check className="w-4 h-4 text-emerald-600" /> : <Share2 className="w-4 h-4" />}
            </button>

            {loja.mostrarWhatsapp && contato.whatsapp && (
              <a
                href={`https://wa.me/55${contato.whatsapp.replace(/\D/g, "")}`}
                target="_blank"
                rel="noreferrer"
                className="hidden sm:flex items-center gap-1.5 px-3 py-1.5 rounded-full border text-xs font-medium transition-colors hover:opacity-80"
                style={{ borderColor: "var(--vt-border)" }}
              >
                <MessageCircle className="w-3.5 h-3.5 text-emerald-600" />
                <span>WhatsApp</span>
              </a>
            )}

            <button
              type="button"
              onClick={() => setCarrinhoAberto(true)}
              className="vt-btn flex items-center gap-2 px-3.5 py-2 rounded-full font-medium text-xs shadow-xs transition-all cursor-pointer group"
            >
              <ShoppingBag className="w-4 h-4 transition-transform group-hover:scale-110" />
              <span className="hidden sm:inline">Sacola</span>
              <span className="vt-tag font-mono text-[11px] font-bold px-1.5 py-0.2 rounded-full">
                {pecasPorSacola.varejo + pecasPorSacola.atacado}
              </span>
            </button>
          </div>
        </div>
      </header>

      {/* 3. BLOCOS DA VITRINE NA ORDEM CONFIGURADA NO EDITOR VISUAL (Gerenciar Catálogo) */}
      <div className="flex-1 w-full pt-4 pb-10" style={{ paddingInline: "var(--vt-margin, 24px)" }} data-previa={emPrevia ? "1" : undefined}>
        {!companyInfo ? (
          // Espera a configuração da loja para não mostrar o layout padrão e depois reorganizar
          <div className="space-y-3 py-24 text-center" role="status">
            <div className="mx-auto h-8 w-8 animate-spin rounded-full border-2 border-neutral-900 border-t-transparent" />
            <p className="text-xs font-medium text-neutral-500">Carregando catálogo...</p>
          </div>
        ) : (
          vitrineCfg.layout.blocks
          .filter((b) => b.visible)
          .map((b) => {
            const centro = b.align === "centro";
            let conteudo = null;

            if (b.id === "banner") {
              conteudo = (
                <HeroCarousel
                  slides={vitrineCfg.banners}
                  intervaloSeg={vitrineCfg.carrossel.intervaloSeg}
                  align={b.align}
                  imageSide={b.imageSide}
                  produtos={produtos}
                  onCta={acaoDoBotaoBanner}
                  onDoubleClick={(slideId) => emPrevia && enviarAoEditor({ type: "lifesurf:frame-banner", slideId })}
                />
              );
            }

            if (b.id === "beneficios") {
              conteudo = (
                <div className="grid grid-cols-2 gap-3 md:grid-cols-4">
                  {[
                    { icone: Truck, titulo: "Pronta-entrega", texto: "Peças com fabricação própria" },
                    { icone: ShieldCheck, titulo: "Envio para todo o Brasil", texto: "Ou retire no balcão, sem frete" },
                    { icone: Building, titulo: `Atacado a partir de ${atacadoMin} peças`, texto: "Pedido separado do varejo" },
                    { icone: QrCode, titulo: "Pague no PIX", texto: "Chave da loja no checkout" }
                  ].map(({ icone: Icone, titulo, texto }) => (
                    <div key={titulo} className="vt-glass flex items-start gap-3 rounded-2xl p-3.5">
                      <span className="vt-accent flex h-9 w-9 shrink-0 items-center justify-center rounded-xl">
                        <Icone className="h-4 w-4" />
                      </span>
                      <div className={centro ? "text-left" : undefined}>
                        <p className="vt-title text-xs leading-tight">{titulo}</p>
                        <p className="vt-sub mt-0.5 text-[11px] leading-snug">{texto}</p>
                      </div>
                    </div>
                  ))}
                </div>
              );
            }

            if (b.id === "novidades") {
              conteudo = (
                <div ref={refNovidades}>
                  <NovidadesCarousel
                    produtos={produtos}
                    quantidade={vitrineCfg.carrossel.novidadesQtd}
                    tipoTabela={tipoTabela}
                    atacadoMin={atacadoMin}
                    onAbrir={abrirProduto}
                    align={b.align}
                    cfg={cfgProdutos}
                  />
                </div>
              );
            }

            if (b.id === "busca") {
              conteudo = (
                <section className={cn("vt-glass-strong space-y-4 rounded-3xl p-5 sm:p-7", centro && "text-center")}>
                  {/* Seletor Varejo/Atacado Mobile */}
                  <div className="flex md:hidden items-center justify-center">
                    <div className="flex w-full max-w-xs items-center justify-between rounded-full border border-neutral-200/80 bg-white/70 p-1 text-xs font-medium">
                      <button
                        type="button"
                        onClick={() => setTipoTabela("varejo")}
                        className={cn(
                          "flex flex-1 cursor-pointer items-center justify-center gap-1.5 rounded-full py-1 transition-all",
                          tipoTabela === "varejo" ? "vt-btn font-semibold shadow-xs" : "opacity-70"
                        )}
                      >
                        <Store className="h-3.5 w-3.5" />
                        <span>Varejo</span>
                      </button>
                      <button
                        type="button"
                        onClick={() => setTipoTabela("atacado")}
                        className={cn(
                          "flex flex-1 cursor-pointer items-center justify-center gap-1.5 rounded-full py-1 transition-all",
                          tipoTabela === "atacado" ? "vt-btn font-semibold shadow-xs" : "opacity-70"
                        )}
                      >
                        <Building className="h-3.5 w-3.5" />
                        <span>Atacado</span>
                      </button>
                    </div>
                  </div>

                  <div className="space-y-1">
                    {vitrineCfg.textos.eyebrow && <span className="vt-sub text-[11px] font-semibold uppercase tracking-widest">{vitrineCfg.textos.eyebrow}</span>}
                    {vitrineCfg.textos.titulo && <h2 className="vt-title text-2xl tracking-tight sm:text-3xl">{vitrineCfg.textos.titulo}</h2>}
                    <p className={cn("vt-sub max-w-lg text-xs sm:text-sm", centro && "mx-auto")}>
                      {tipoTabela === "atacado"
                        ? `Atacado para lojistas: pedido separado do varejo, a partir de ${atacadoMin} peças, com o preço de fábrica.`
                        : loja.apresentacao || companyInfo?.mensagemCatalogo || "Peças exclusivas com tecidos nobres, alta durabilidade e caimento impecável."}
                    </p>
                  </div>

                  <div className={cn("relative max-w-xl pt-1", centro && "mx-auto")}>
                    <Search className="absolute left-4 top-1/2 h-4 w-4 -translate-y-1/2 text-neutral-400" />
                    <input
                      type="text"
                      placeholder="Buscar por nome da peça, categoria ou referência..."
                      value={busca}
                      onChange={(e) => {
                        setBusca(e.target.value);
                        setPagina(1);
                      }}
                      style={{ borderColor: "var(--vt-border)", background: "color-mix(in srgb, var(--vt-card) 85%, transparent)", color: "var(--vt-pname)" }}
                      className="h-11 w-full rounded-full border pl-11 pr-4 text-xs shadow-xs transition-all placeholder:text-neutral-400 focus:outline-none focus:ring-1"
                    />
                  </div>

                  <div className={cn("flex flex-wrap items-center gap-2 pt-1", centro && "justify-center")}>
                    {categorias.map((cat) => {
                      const isAtiva = categoriaAtiva === cat;
                      return (
                        <button
                          key={cat}
                          type="button"
                          onClick={() => {
                            setCategoriaAtiva(cat);
                            setPagina(1);
                          }}
                          style={isAtiva ? undefined : { borderColor: "var(--vt-border)" }}
                          className={cn(
                            "cursor-pointer rounded-full px-4 py-1.5 text-xs transition-all",
                            isAtiva ? "vt-btn font-semibold shadow-xs" : "vt-sub border font-medium hover:opacity-80"
                          )}
                        >
                          {cat}
                        </button>
                      );
                    })}
                  </div>
                </section>
              );
            }

            if (b.id === "vitrine") {
              conteudo = (
                <main ref={refVitrine}>
                  {loading ? (
                    <div className="space-y-3 py-24 text-center">
                      <div className="mx-auto h-8 w-8 animate-spin rounded-full border-2 border-neutral-900 border-t-transparent" />
                      <p className="text-xs font-medium text-neutral-500">Carregando catálogo...</p>
                    </div>
                  ) : produtosFiltrados.length === 0 ? (
                    <div className="vt-glass mx-auto max-w-md space-y-3 rounded-2xl p-8 py-16 text-center">
                      <Tag className="mx-auto h-10 w-10 text-neutral-400" />
                      <h3 className="text-base font-bold text-neutral-900">Nenhuma peça encontrada</h3>
                      <p className="text-xs text-neutral-500">Tente buscar por outro termo ou escolha a categoria "Todos".</p>
                      <button
                        type="button"
                        onClick={() => {
                          setBusca("");
                          setCategoriaAtiva("Todos");
                          setPagina(1);
                        }}
                        className="vt-btn mt-2 rounded-lg px-4 py-2 text-xs font-semibold transition"
                      >
                        Ver Todas as Peças
                      </button>
                    </div>
                  ) : (
                    <>
                      <div className="vt-grid">
                        {produtosDaPagina.map((produto) => (
                          <ProductCardGlass key={produto.id} produto={produto} tipoTabela={tipoTabela} atacadoMin={atacadoMin} onAbrir={abrirProduto} cfg={cfgProdutos} />
                        ))}
                      </div>
                      {restantesNaVitrine > 0 && (
                        <div className="mt-6 flex justify-center">
                          <button
                            type="button"
                            onClick={verMaisProdutos}
                            className="vt-btn rounded-full px-6 py-2 text-xs font-semibold"
                          >
                            Ver mais peças ({restantesNaVitrine})
                          </button>
                        </div>
                      )}
                      {totalPaginas > 1 && (
                        <nav className="mt-6 flex flex-wrap items-center justify-center gap-2" aria-label="Páginas do catálogo">
                          <button
                            type="button"
                            disabled={paginaAtual <= 1}
                            onClick={() => setPagina(paginaAtual - 1)}
                            className="vt-glass vt-sub rounded-full px-4 py-1.5 text-xs disabled:opacity-40"
                          >
                            Anterior
                          </button>
                          <span className="vt-sub text-xs">
                            Página {paginaAtual} de {totalPaginas}
                          </span>
                          <button
                            type="button"
                            disabled={paginaAtual >= totalPaginas}
                            onClick={() => setPagina(paginaAtual + 1)}
                            className="vt-btn rounded-full px-4 py-1.5 text-xs disabled:opacity-40"
                          >
                            Próxima
                          </button>
                        </nav>
                      )}
                    </>
                  )}
                </main>
              );
            }

            return (
              <div
                key={b.id}
                data-bloco={b.id}
                onClick={emPrevia ? () => enviarAoEditor({ type: "lifesurf:select-block", id: b.id }) : undefined}
                className={cn(
                  "mx-auto w-full",
                  WIDTH_CLASSES[b.width],
                  SPACING_CLASSES[b.spacing],
                  emPrevia && blocoSelecionado === b.id && "vt-selecionado"
                )}
              >
                {conteudo}
              </div>
            );
          })
        )}
      </div>

      {/* 5. MODAL DE ESCOLHA DE TAMANHO & VARIAÇÃO (CLEAN & MODERNO) */}
      {produtoModal && (
        <Modal isOpen={Boolean(produtoModal)} onClose={() => setProdutoModal(null)} size="md">
          <ModalHeader
            title={produtoModal.nome}
            description={`Selecione tamanho e cor • ${tipoTabela === "atacado" ? `Atacado (pedido separado, mín. ${atacadoMin} peças)` : "Varejo"}`}
            onClose={() => setProdutoModal(null)}
          />
          <ModalBody className="space-y-4 text-neutral-900 bg-white">
            <div className="flex items-center gap-3 p-3 bg-neutral-50 rounded-xl border border-neutral-200">
              {(() => {
                const fotos = fotosDoProduto(produtoModal);
                if (fotos.length === 0) return null;
                const atual = fotos[Math.min(fotoModalIdx, fotos.length - 1)];
                return (
                  <div className="shrink-0 space-y-1.5">
                    <img
                      src={atual}
                      alt={produtoModal.nome}
                      className="h-24 w-[4.5rem] rounded-lg border border-neutral-200 object-cover"
                      onError={(e) => {
                        e.currentTarget.onerror = null;
                        e.currentTarget.src = DEFAULT_PRODUCT_FALLBACK;
                      }}
                    />
                    {fotos.length > 1 && (
                      <div className="flex gap-1">
                        {fotos.map((f, i) => (
                          <button
                            key={f}
                            type="button"
                            aria-label={`Ver foto ${i + 1}`}
                            onClick={() => setFotoModalIdx(i)}
                            className={cn("h-6 w-4 overflow-hidden rounded border", i === fotoModalIdx ? "border-neutral-900" : "border-neutral-300 opacity-70")}
                          >
                            <img src={f} alt="" className="h-full w-full object-cover" />
                          </button>
                        ))}
                      </div>
                    )}
                  </div>
                );
              })()}
              <div className="flex-1 min-w-0">

                <span className="text-xs text-neutral-500 block uppercase tracking-wider">{produtoModal.categoria}</span>
                <div className="flex items-baseline gap-2 mt-0.5">
                  <strong className="text-xl font-bold text-neutral-900 font-sans">
                    {formatCurrency(
                      tipoTabela === "atacado"
                        ? produtoModal.precoAtacado || produtoModal.precoVarejo
                        : precoVarejoEfetivo(produtoModal)
                    )}
                  </strong>
                  {tipoTabela !== "atacado" && precoVarejoEfetivo(produtoModal) < (Number(produtoModal.precoVarejo) || 0) && (
                    <span className="text-xs text-neutral-400 line-through">{formatCurrency(produtoModal.precoVarejo)}</span>
                  )}
                  {tipoTabela === "atacado" && produtoModal.precoVarejo > produtoModal.precoAtacado && (
                    <span className="text-xs text-neutral-400 line-through">
                      {formatCurrency(produtoModal.precoVarejo)}
                    </span>
                  )}
                </div>
                <span className="inline-block mt-1 text-[11px] font-medium px-2 py-0.5 rounded-full bg-neutral-200 text-neutral-800">
                  {tipoTabela === "atacado" ? "Preço de Atacado" : "Preço de Varejo"}
                </span>
              </div>
            </div>

            {/* Escolha de Tamanho */}
            <div>
              <span className="text-xs font-semibold text-neutral-700 uppercase tracking-wider block mb-2">
                1. Escolha o Tamanho:
              </span>
              <div className="grid grid-cols-4 sm:grid-cols-6 gap-2">
                {(produtoModal.gradeTamanhos
                  ? Object.keys(produtoModal.gradeTamanhos)
                  : ["P", "M", "G", "GG"]
                ).map((tam) => {
                  const isSelected = tamanhoSelecionado === tam;
                  return (
                    <button
                      key={tam}
                      type="button"
                      onClick={() => setTamanhoSelecionado(tam)}
                      className={cn(
                        "py-2.5 rounded-lg border text-center font-mono font-semibold text-xs transition-all cursor-pointer",
                        isSelected
                          ? "bg-neutral-900 text-white border-neutral-900 shadow-xs"
                          : "bg-white border-neutral-200 text-neutral-700 hover:border-neutral-400"
                      )}
                    >
                      {tam}
                    </button>
                  );
                })}
              </div>
            </div>

            {/* Escolha de Cor */}
            <div>
              <span className="text-xs font-semibold text-neutral-700 uppercase tracking-wider block mb-2">
                2. Escolha a Cor:
              </span>
              <div className="flex flex-wrap gap-2">
                {(Array.isArray(produtoModal.cores)
                  ? produtoModal.cores
                  : typeof produtoModal.cores === "string"
                    ? produtoModal.cores.split(",").map((c) => c.trim())
                    : ["Padrão"]
                ).map((cor) => {
                  const isSelected = corSelecionada === cor;
                  return (
                    <button
                      key={cor}
                      type="button"
                      onClick={() => setCorSelecionada(cor)}
                      className={cn(
                        "px-3.5 py-1.5 rounded-lg border text-xs font-medium transition-all cursor-pointer flex items-center gap-2",
                        isSelected
                          ? "bg-neutral-900 text-white border-neutral-900 shadow-xs"
                          : "bg-white border-neutral-200 text-neutral-700 hover:border-neutral-400"
                      )}
                    >
                      <span
                        className={cn(
                          "w-3 h-3 rounded-full border shrink-0",
                          getColorSwatchClass(cor)
                        )}
                      />
                      <span>{cor}</span>
                    </button>
                  );
                })}
              </div>
            </div>

            {/* Quantidade */}
            <div className="flex items-center justify-between p-3 bg-neutral-50 rounded-xl border border-neutral-200">
              <div>
                <span className="text-xs text-neutral-700 font-semibold block">Quantidade:</span>
                <span className="text-[11px] text-neutral-500 font-mono">
                  Subtotal:{" "}
                  {formatCurrency(
                    (tipoTabela === "atacado"
                      ? produtoModal.precoAtacado || produtoModal.precoVarejo
                      : precoVarejoEfetivo(produtoModal)) * quantidadeItem
                  )}
                </span>
              </div>
              <div className="flex items-center gap-2">
                <button
                  type="button"
                  onClick={() => setQuantidadeItem((q) => Math.max(1, q - 1))}
                  className="w-8 h-8 rounded-lg bg-white border border-neutral-200 text-neutral-800 flex items-center justify-center hover:bg-neutral-100 cursor-pointer"
                >
                  <Minus className="w-3.5 h-3.5" />
                </button>
                <CampoQuantidade
                  value={quantidadeItem}
                  onChange={setQuantidadeItem}
                  ariaLabel="Quantidade (clique e digite)"
                  className="w-12 text-center font-mono font-bold text-neutral-900 text-sm bg-white border border-neutral-200 rounded-md py-1"
                />
                <button
                  type="button"
                  onClick={() => setQuantidadeItem((q) => q + 1)}
                  className="w-8 h-8 rounded-lg bg-white border border-neutral-200 text-neutral-800 flex items-center justify-center hover:bg-neutral-100 cursor-pointer"
                >
                  <Plus className="w-3.5 h-3.5" />
                </button>
              </div>
            </div>
          </ModalBody>
          <ModalFooter>
            <Button variant="ghost" onClick={() => setProdutoModal(null)} className="text-neutral-600">
              Cancelar
            </Button>
            <Button
              variant="primary"
              onClick={handleAdicionarAoCarrinho}
              rightIcon={<ArrowRight className="w-4 h-4" />}
              className="bg-neutral-900 hover:bg-neutral-800 text-white font-semibold text-xs"
            >
              Adicionar à Sacola
            </Button>
          </ModalFooter>
        </Modal>
      )}

      {/* 6. DRAWER DA SACOLA E CHECKOUT DE FRICÇÃO ZERO (DESIGN CLEAN) */}
      {carrinhoAberto && (
        <div className="fixed inset-0 z-50 flex justify-end bg-black/50 backdrop-blur-xs animate-in fade-in">
          <div className="w-full max-w-md bg-white h-full border-l border-neutral-200 flex flex-col justify-between shadow-2xl animate-in slide-in-from-right duration-200">
            {/* Header da Sacola */}
            <div className="p-4 border-b border-neutral-200 flex items-center justify-between bg-white">
              <div className="flex items-center gap-2">
                <ShoppingBag className="w-5 h-5 text-neutral-900" />
                <h3 className="text-base font-bold text-neutral-900">Sua Sacola de Compras</h3>
              </div>
              <button
                type="button"
                onClick={() => setCarrinhoAberto(false)}
                className="text-neutral-400 hover:text-neutral-900 p-1.5 rounded-lg hover:bg-neutral-100 transition-colors cursor-pointer"
              >
                ✕
              </button>
            </div>

            {/* Varejo e atacado são pedidos separados: uma sacola para cada */}
            <div className="bg-white px-4 pt-3">
              <div className="grid grid-cols-2 gap-1 rounded-full border border-neutral-200 bg-neutral-100 p-1 text-xs font-semibold" role="tablist" aria-label="Tipo de pedido">
                {[
                  ["varejo", "Varejo"],
                  ["atacado", "Atacado"]
                ].map(([id, rotulo]) => (
                  <button
                    key={id}
                    type="button"
                    role="tab"
                    aria-selected={sacolaAtiva === id}
                    onClick={() => setSacolaAtiva(id)}
                    className={cn(
                      "cursor-pointer rounded-full py-1.5 transition-all",
                      sacolaAtiva === id ? "bg-neutral-900 text-white shadow-xs" : "text-neutral-600 hover:text-neutral-900"
                    )}
                  >
                    {rotulo} ({pecasPorSacola[id]})
                  </button>
                ))}
              </div>
              <p className="mt-2 text-[11px] text-neutral-500">
                {sacolaAtiva === "atacado"
                  ? `Atacado é um pedido à parte, com o preço de fábrica a partir de ${atacadoMin} peças.`
                  : "Varejo e atacado são pedidos separados."}
              </p>
            </div>

            {/* Conteúdo da Sacola */}
            <div className="flex-1 overflow-y-auto p-4 space-y-4 scrollbar-thin">
              {itensSacola.length === 0 ? (
                <div className="text-center py-20 space-y-3 text-neutral-400">
                  <ShoppingBag className="w-12 h-12 mx-auto text-neutral-300" />
                  <p className="text-sm font-semibold text-neutral-700">Sua sacola de {sacolaAtiva} está vazia.</p>
                  <p className="text-xs text-neutral-500">Escolha peças no catálogo para finalizar.</p>
                  <button
                    type="button"
                    onClick={() => setCarrinhoAberto(false)}
                    className="mt-2 px-4 py-2 rounded-lg bg-neutral-900 text-white text-xs font-semibold hover:bg-neutral-800 transition-colors"
                  >
                    Explorar Peças
                  </button>
                </div>
              ) : (
                <>
                  {/* Lista de Itens */}
                  <div className="space-y-2.5">
                    {itensSacola.map((item) => (
                      <div
                        key={item.key}
                        className="p-3 bg-neutral-50 rounded-xl border border-neutral-200 flex items-center justify-between gap-3"
                      >
                        {item.fotoUrl && (
                          <div className="w-12 h-14 rounded-lg bg-neutral-200 border border-neutral-300 overflow-hidden shrink-0">
                            <img
                              src={item.fotoUrl}
                              alt={item.nome}
                              className="w-full h-full object-cover"
                              onError={(e) => {
                                e.currentTarget.style.display = "none";
                              }}
                            />
                          </div>
                        )}
                        <div className="flex-1 min-w-0">
                          <h4 className="text-xs font-semibold text-neutral-900 truncate">{item.nome}</h4>
                          <div className="flex items-center gap-2 text-[11px] text-neutral-500 mt-0.5 font-mono">
                            <span>Tam: <strong>{item.tamanho}</strong></span>
                            <span>•</span>
                            <span>{item.cor}</span>
                            {item.tipoVenda === "atacado" && (
                              <>
                                <span>•</span>
                                <span className="text-neutral-900 font-bold">Atacado</span>
                              </>
                            )}
                          </div>
                          <strong className="text-xs font-bold text-neutral-900 font-mono mt-1 block">
                            {formatCurrency(item.precoUnitario * item.quantidade)}
                          </strong>
                        </div>


                        {/* Botões de Quantidade */}
                        <div className="flex items-center gap-1.5 bg-white p-1 rounded-lg border border-neutral-200">
                          <button
                            type="button"
                            onClick={() => alterarQtdCarrinho(item.key, -1)}
                            className="w-6 h-6 rounded flex items-center justify-center text-neutral-500 hover:text-neutral-900 cursor-pointer"
                          >
                            <Minus className="w-3 h-3" />
                          </button>
                          <CampoQuantidade
                            value={item.quantidade}
                            onChange={(n) => definirQtdCarrinho(item.key, n)}
                            ariaLabel="Quantidade (clique e digite)"
                            className="w-10 text-center font-mono font-bold text-xs text-neutral-900 bg-transparent outline-none"
                          />
                          <button
                            type="button"
                            onClick={() => alterarQtdCarrinho(item.key, 1)}
                            className="w-6 h-6 rounded flex items-center justify-center text-neutral-500 hover:text-neutral-900 cursor-pointer"
                          >
                            <Plus className="w-3 h-3" />
                          </button>
                        </div>

                        <button
                          type="button"
                          onClick={() => removerDoCarrinho(item.key)}
                          className="text-neutral-400 hover:text-rose-600 p-1 cursor-pointer transition-colors"
                        >
                          <Trash2 className="w-4 h-4" />
                        </button>
                      </div>
                    ))}
                  </div>

                  {/* FORMULÁRIO DE CHECKOUT RÁPIDO */}
                  <form
                    id="checkout-form"
                    onSubmit={handleFinalizarPedido}
                    className="pt-3 border-t border-neutral-200 space-y-3"
                  >
                    <div className="flex items-center justify-between text-xs font-bold text-neutral-900 uppercase tracking-wider">
                      <span className="flex items-center gap-1.5">
                        <ShieldCheck className="w-4 h-4 text-emerald-600" />
                        Dados de Envio & Contato
                      </span>
                      <span className="text-[11px] text-neutral-500 font-normal">Sem senha necessária</span>
                    </div>

                    <div className="space-y-2">
                      <div className="relative">
                        <User className="w-3.5 h-3.5 absolute left-3 top-1/2 -translate-y-1/2 text-neutral-400" />
                        <input
                          type="text"
                          required
                          placeholder="Seu Nome Completo"
                          value={nomeCliente}
                          onChange={(e) => setNomeCliente(e.target.value)}
                          className="w-full h-9 pl-9 pr-3 rounded-lg bg-neutral-50 border border-neutral-200 text-xs text-neutral-900 focus:bg-white focus:border-neutral-900 focus:outline-none placeholder:text-neutral-400"
                        />
                      </div>

                      <div className="relative">
                        <Phone className="w-3.5 h-3.5 absolute left-3 top-1/2 -translate-y-1/2 text-neutral-400" />
                        <input
                          type="text"
                          required
                          placeholder="WhatsApp com DDD (Ex: 81988887777)"
                          value={whatsappCliente}
                          onChange={(e) => setWhatsappCliente(e.target.value)}
                          className="w-full h-9 pl-9 pr-3 rounded-lg bg-neutral-50 border border-neutral-200 text-xs text-neutral-900 focus:bg-white focus:border-neutral-900 focus:outline-none placeholder:text-neutral-400 font-mono"
                        />
                      </div>

                      {/* Modalidade de Entrega */}
                      <div className="pt-1">
                        <label className="text-[11px] text-neutral-500 block mb-1">
                          Como deseja receber seu pedido?
                        </label>
                        <div className="grid grid-cols-2 gap-2">
                          <button
                            type="button"
                            onClick={() => setTipoEntrega("entrega")}
                            className={cn(
                              "p-2 rounded-lg border text-xs font-semibold flex items-center justify-center gap-1.5 cursor-pointer transition-all",
                              tipoEntrega === "entrega"
                                ? "bg-neutral-900 border-neutral-900 text-white shadow-xs"
                                : "bg-white border-neutral-200 text-neutral-600 hover:border-neutral-300"
                            )}
                          >
                            <Truck className="w-3.5 h-3.5" />
                            <span>Entrega / Motoboy</span>
                          </button>

                          <button
                            type="button"
                            onClick={() => setTipoEntrega("retirada")}
                            className={cn(
                              "p-2 rounded-lg border text-xs font-semibold flex items-center justify-center gap-1.5 cursor-pointer transition-all",
                              tipoEntrega === "retirada"
                                ? "bg-neutral-900 border-neutral-900 text-white shadow-xs"
                                : "bg-white border-neutral-200 text-neutral-600 hover:border-neutral-300"
                            )}
                          >
                            <Store className="w-3.5 h-3.5" />
                            <span>Retirar no Balcão</span>
                          </button>
                        </div>
                      </div>

                      {tipoEntrega === "entrega" ? (
                        <div className="space-y-2 pt-1">
                          <div className="relative">
                            <MapPin className="w-3.5 h-3.5 absolute left-3 top-1/2 -translate-y-1/2 text-neutral-400" />
                            <input
                              type="text"
                              required
                              placeholder="Endereço (Rua, Número, Complemento)"
                              value={enderecoCliente}
                              onChange={(e) => setEnderecoCliente(e.target.value)}
                              className="w-full h-9 pl-9 pr-3 rounded-lg bg-neutral-50 border border-neutral-200 text-xs text-neutral-900 focus:bg-white focus:border-neutral-900 focus:outline-none placeholder:text-neutral-400"
                            />
                          </div>

                          <div className="grid grid-cols-2 gap-2">
                            <input
                              type="text"
                              placeholder="Bairro"
                              value={bairroCliente}
                              onChange={(e) => setBairroCliente(e.target.value)}
                              className="w-full h-9 px-3 rounded-lg bg-neutral-50 border border-neutral-200 text-xs text-neutral-900 focus:bg-white focus:border-neutral-900 focus:outline-none placeholder:text-neutral-400"
                            />
                            <input
                              type="text"
                              placeholder="Cidade / UF"
                              value={cidadeCliente}
                              onChange={(e) => setCidadeCliente(e.target.value)}
                              className="w-full h-9 px-3 rounded-lg bg-neutral-50 border border-neutral-200 text-xs text-neutral-900 focus:bg-white focus:border-neutral-900 focus:outline-none placeholder:text-neutral-400"
                            />
                          </div>
                        </div>
                      ) : (
                        <div className="p-3 bg-neutral-50 rounded-lg border border-neutral-200 text-xs text-neutral-700 space-y-1">
                          <strong className="text-neutral-900 flex items-center gap-1.5">
                            <Building className="w-3.5 h-3.5 text-neutral-700" />
                            {companyInfo?.nome || "Loja"}
                          </strong>
                          <p className="text-[11px] text-neutral-500">
                            {[companyInfo?.endereco, companyInfo?.cidade || CIDADE_UF_PADRAO].filter(Boolean).join(" - ")}
                          </p>
                          <span className="text-[10px] text-emerald-700 font-semibold block">
                            ✓ Sem taxa de frete • Retirada direta na fábrica
                          </span>
                        </div>
                      )}

                      {/* Método de Pagamento */}
                      <div className="pt-1">
                        <label className="text-[11px] text-neutral-500 block mb-1.5">
                          Forma de Pagamento:
                        </label>
                        <div className="grid grid-cols-3 gap-1.5">
                          {[
                            { id: "pix", label: "PIX", icon: QrCode },
                            { id: "cartao", label: "Cartão", icon: CreditCard },
                            { id: "dinheiro", label: "Dinheiro", icon: DollarSign }
                          ].map((pg) => {
                            const Icon = pg.icon;
                            const isSelected = formaPagamento === pg.id;
                            return (
                              <button
                                key={pg.id}
                                type="button"
                                onClick={() => setFormaPagamento(pg.id)}
                                className={cn(
                                  "py-2 px-1 rounded-lg border text-center text-[11px] font-semibold flex flex-col items-center gap-1 transition-all cursor-pointer",
                                  isSelected
                                    ? "bg-neutral-900 border-neutral-900 text-white shadow-xs"
                                    : "bg-white border-neutral-200 text-neutral-600 hover:border-neutral-300"
                                )}
                              >
                                <Icon className="w-3.5 h-3.5" />
                                {pg.label}
                              </button>
                            );
                          })}
                        </div>
                      </div>

                      {/* Chave PIX se selecionado */}
                      {formaPagamento === "pix" && (companyInfo?.chavePix || companyInfo?.cnpj) && (
                        <div className="p-2.5 rounded-lg bg-emerald-50 border border-emerald-200 text-xs flex items-center justify-between">
                          <div>
                            <span className="text-[10px] text-emerald-800 uppercase tracking-wider block font-bold">
                              Chave PIX da Loja
                            </span>
                            <span className="text-emerald-950 font-mono font-bold text-[11px]">
                              {companyInfo?.chavePix || companyInfo?.cnpj}
                            </span>
                          </div>
                          <button
                            type="button"
                            onClick={copiarChavePix}
                            className="px-2.5 py-1 rounded bg-emerald-600 text-white font-semibold text-[10px] hover:bg-emerald-700 transition-colors flex items-center gap-1 cursor-pointer shadow-xs"
                          >
                            {pixCopiado ? <Check className="w-3 h-3" /> : <Copy className="w-3 h-3" />}
                            {pixCopiado ? "Copiada!" : "Copiar"}
                          </button>
                        </div>
                      )}

                      <textarea
                        rows={2}
                        placeholder="Observações adicionais (ex: deixar na portaria, troco para 100...)"
                        value={observacoes}
                        onChange={(e) => setObservacoes(e.target.value)}
                        className="w-full p-2.5 rounded-lg bg-neutral-50 border border-neutral-200 text-xs text-neutral-900 focus:bg-white focus:border-neutral-900 focus:outline-none placeholder:text-neutral-400"
                      />
                    </div>
                  </form>
                </>
              )}
            </div>

            {/* Rodapé da Sacola com Totais e Botão */}
            {itensSacola.length > 0 && (
              <div className="p-4 border-t border-neutral-200 bg-white space-y-3">
                <div className="space-y-1 text-xs">
                  <div className="flex justify-between text-neutral-500">
                    <span>Subtotal ({totalItensSacola} peças):</span>
                    <span className="font-mono text-neutral-900 font-semibold">{formatCurrency(subtotalSacola)}</span>
                  </div>

                  {economiaTotal > 0 && (
                    <div className="flex justify-between text-emerald-700 font-medium">
                      <span>Economia no Atacado:</span>
                      <span className="font-mono">-{formatCurrency(economiaTotal)}</span>
                    </div>
                  )}

                  <div className="flex justify-between text-neutral-500">
                    <span className="flex items-center gap-1">
                      <Truck className="w-3.5 h-3.5 text-neutral-700" />
                      Entrega:
                    </span>
                    <span className="text-neutral-800 font-medium">
                      {tipoEntrega === "retirada" ? "Grátis (Balcão)" : "A combinar com motoboy"}
                    </span>
                  </div>

                  <div className="flex justify-between pt-2 border-t border-neutral-200 text-sm font-bold">
                    <span className="text-neutral-900">Total a Pagar:</span>
                    <span className="text-neutral-900 font-mono text-lg font-extrabold">
                      {formatCurrency(subtotalSacola)}
                    </span>
                  </div>
                </div>

                {sacolaAtiva === "atacado" && (
                  <div
                    role="status"
                    className={cn(
                      "rounded-xl border p-3 text-xs",
                      faltamParaAtacado > 0 ? "border-amber-200 bg-amber-50 text-amber-900" : "border-emerald-200 bg-emerald-50 text-emerald-800"
                    )}
                  >
                    {faltamParaAtacado > 0 ? (
                      <>
                        Faltam <strong>{faltamParaAtacado}</strong> peça(s) para o pedido de atacado ({totalItensSacola}/{atacadoMin}).
                      </>
                    ) : (
                      <>Pedido de atacado liberado: {totalItensSacola} peças com o preço de atacado.</>
                    )}
                    <div className="mt-2 h-1.5 overflow-hidden rounded-full bg-white/80">
                      <div
                        className={cn("h-full rounded-full transition-all", faltamParaAtacado > 0 ? "bg-amber-500" : "bg-emerald-600")}
                        style={{ width: `${Math.min(100, (totalItensSacola / atacadoMin) * 100)}%` }}
                      />
                    </div>
                  </div>
                )}

                <button
                  type="submit"
                  form="checkout-form"
                  disabled={enviandoPedido || faltamParaAtacado > 0}
                  className="w-full h-12 rounded-xl bg-neutral-900 hover:bg-neutral-800 text-white font-bold text-xs uppercase tracking-wider flex items-center justify-center gap-2 transition-colors shadow-md cursor-pointer disabled:opacity-50"
                >
                  {enviandoPedido ? (
                    <span>Registrando Pedido...</span>
                  ) : (
                    <>
                      <span>CONFIRMAR PEDIDO</span>
                      <ArrowRight className="w-4 h-4" />
                    </>
                  )}
                </button>
              </div>
            )}
          </div>
        </div>
      )}

      {/* 7. MODAL DE SUCESSO DO PEDIDO */}
      {modalSucessoAberto && pedidoConcluido && (
        <Modal
          isOpen={modalSucessoAberto}
          onClose={() => setModalSucessoAberto(false)}
          size="md"
        >
          <ModalBody className="p-6 text-center space-y-4 bg-white text-neutral-900">
            <div className="w-14 h-14 rounded-full bg-emerald-50 border border-emerald-200 text-emerald-600 flex items-center justify-center mx-auto">
              <CheckCircle2 className="w-8 h-8" />
            </div>

            <div>
              <span className="text-[11px] font-bold text-emerald-700 uppercase tracking-widest block font-mono">
                Pedido Registrado com Sucesso
              </span>
              <h3 className="text-2xl font-extrabold text-neutral-900 mt-1">
                {pedidoConcluido.numeroPedido}
              </h3>
              <p className="text-xs text-neutral-500 mt-1 max-w-sm mx-auto">
                Seu pedido foi recebido pela confecção e já está na fila de separação da expedição!
              </p>
            </div>

            {/* Resumo do Pedido */}
            <div className="bg-neutral-50 p-4 rounded-xl border border-neutral-200 text-left text-xs space-y-2">
              <div className="flex justify-between">
                <span className="text-neutral-500">Cliente:</span>
                <strong className="text-neutral-900">{pedidoConcluido.cliente?.nome}</strong>
              </div>
              <div className="flex justify-between">
                <span className="text-neutral-500">WhatsApp:</span>
                <span className="text-neutral-800 font-mono">{pedidoConcluido.cliente?.telefone}</span>
              </div>
              <div className="flex justify-between">
                <span className="text-neutral-500">Recebimento:</span>
                <span className="text-neutral-800">{pedidoConcluido.cliente?.endereco}</span>
              </div>
              <div className="flex justify-between">
                <span className="text-neutral-500">Forma de Pagto:</span>
                <strong className="text-neutral-900 uppercase">{pedidoConcluido.formaPagamento}</strong>
              </div>
              <div className="flex justify-between pt-1 border-t border-neutral-200">
                <span className="text-neutral-500">Total a Pagar:</span>
                <strong className="text-neutral-900 text-sm font-mono font-bold">
                  {formatCurrency(pedidoConcluido.total)}
                </strong>
              </div>
            </div>

            {/* Botão Enviar WhatsApp */}
            <div className="space-y-2 pt-2">
              <button
                type="button"
                className="w-full h-11 rounded-xl bg-emerald-600 hover:bg-emerald-700 text-white font-bold text-xs uppercase tracking-wider flex items-center justify-center gap-2 transition-colors shadow-sm cursor-pointer"
                onClick={abrirWhatsAppComPedido}
              >
                <MessageCircle className="w-4 h-4" />
                <span>Enviar Pedido no WhatsApp da Loja</span>
              </button>

              <button
                type="button"
                className="w-full py-2 text-xs text-neutral-500 hover:text-neutral-900 font-medium cursor-pointer"
                onClick={() => setModalSucessoAberto(false)}
              >
                Continuar Navegando
              </button>
            </div>
          </ModalBody>
        </Modal>
      )}

      {/* 8. BARRA FLUTUANTE INFERIOR MOBILE */}
      <div className="md:hidden fixed bottom-0 left-0 right-0 z-40 bg-white/95 backdrop-blur-md border-t border-neutral-200 px-4 py-2.5 flex items-center justify-between shadow-lg">
        <div className="flex items-center gap-1.5">
          <span className="text-xs text-neutral-500">Total:</span>
          <strong className="text-base font-bold text-neutral-900 font-mono">
            {formatCurrency(subtotalSacola)}
          </strong>
        </div>

        <button
          type="button"
          onClick={() => setCarrinhoAberto(true)}
          className="vt-btn flex items-center gap-2 px-4 py-2 rounded-full text-xs font-semibold shadow-xs cursor-pointer"
        >
          <ShoppingBag className="w-4 h-4" />
          <span>Ver Sacola ({totalItensSacola})</span>
        </button>
      </div>

      {/* 9. RODAPÉ (texto, contatos e direitos editáveis no Editor visual) */}
      <footer
        className="vt-footer mt-auto border-t py-8 text-center text-xs space-y-1.5"
        style={{ paddingInline: "var(--vt-margin, 24px)" }}
        data-bloco-fixo="rodape"
      >
        {vitrineCfg.rodape.texto && <p className="font-semibold whitespace-pre-line">{vitrineCfg.rodape.texto}</p>}
        {vitrineCfg.rodape.mostrarContato && (
          <p className="text-[11px] opacity-80">
            {[
              loja.mostrarEndereco && contato.endereco,
              loja.mostrarTelefone && contato.telefone && `Tel: ${contato.telefone}`,
              loja.mostrarWhatsapp && contato.whatsapp && `WhatsApp: ${contato.whatsapp}`,
              loja.mostrarEmail && contato.email,
              loja.mostrarRedes && contato.instagram && `Instagram: ${contato.instagram}`,
              loja.mostrarRedes && contato.facebook && `Facebook: ${contato.facebook}`
            ]
              .filter(Boolean)
              .join(" • ")}
          </p>
        )}
        {vitrineCfg.rodape.mostrarCopyright && (
          <p className="text-[11px] opacity-80">
            © {new Date().getFullYear()} {contato.nome}. Todos os direitos reservados.
          </p>
        )}
      </footer>
    </div>
  );
}
