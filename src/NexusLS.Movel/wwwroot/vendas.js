/*
  Nexus Móvel — aba Vendas (resumo do dia/semana/mês e pesquisa por referência/produto).
  Tudo que vem do banco é escrito com textContent (nunca montado como HTML).
  Usa o PIN e o acesso à API do app.js (window.NexusMovel).
*/
(() => {
  "use strict";

  const el = (id) => document.getElementById(id);
  let periodo = "hoje";
  let timerBusca = 0;
  let sequencia = 0;

  function no(tag, classe, texto) {
    const e = document.createElement(tag);
    if (classe) e.className = classe;
    if (texto !== undefined && texto !== null) e.textContent = String(texto);
    return e;
  }

  const dinheiro = (c) => window.NexusMovel.dinheiro(c);

  function variacao(c, formato) {
    const v = no("div", "var");
    if (c.variacaoPct === null || c.variacaoPct === undefined) {
      v.textContent = "Sem base no período anterior";
      return v;
    }

    const pct = Number(c.variacaoPct);
    const sinal = pct > 0 ? "▲ +" : pct < 0 ? "▼ " : "= ";
    v.classList.add(pct > 0 ? "sobe" : pct < 0 ? "desce" : "igual");
    v.textContent = sinal + pct.toLocaleString("pt-BR", { maximumFractionDigits: 1 }) + "% (antes: " + formato(c.anterior) + ")";
    return v;
  }

  function kpi(rotulo, comparativo, formato, grande) {
    const k = no("div", "kpi" + (grande ? " grande" : ""));
    k.appendChild(no("div", "rotulo", rotulo));
    k.appendChild(no("div", "valor", formato(comparativo.atual)));
    k.appendChild(variacao(comparativo, formato));
    return k;
  }

  function linha(titulo, detalhe, numero) {
    const l = no("div", "linha-dado");
    const esq = no("span");
    esq.appendChild(document.createTextNode(titulo));
    if (detalhe) esq.appendChild(no("small", "", detalhe));
    l.appendChild(esq);
    l.appendChild(no("span", "num", numero));
    return l;
  }

  function bloco(titulo, linhas, vazio) {
    const b = no("section", "bloco");
    b.appendChild(no("h3", "", titulo));
    if (!linhas.length) b.appendChild(no("p", "vazio-texto", vazio));
    linhas.forEach((l) => b.appendChild(l));
    return b;
  }

  const plural = (n, um, varios) => n + " " + (n === 1 ? um : varios);
  const data = (iso) => iso.split("-").reverse().join("/");

  function desenharResumo(r) {
    const raiz = el("vendas-resumo");
    raiz.replaceChildren();

    const rotuloPeriodo = r.periodo.dias === 1
      ? data(r.periodo.inicio)
      : data(r.periodo.inicio) + " a " + data(r.periodo.fim);
    raiz.appendChild(no("p", "vazio-texto", "Período: " + rotuloPeriodo + " (comparado com " +
      data(r.anterior.inicio) + (r.anterior.dias > 1 ? " a " + data(r.anterior.fim) : "") + ")"));

    const kpis = no("div", "kpis");
    kpis.appendChild(kpi("Total vendido", r.totalCentavos, dinheiro, true));
    kpis.appendChild(kpi("Vendas", r.vendas, (n) => String(n)));
    kpis.appendChild(kpi("Peças", r.pecas, (n) => String(n)));
    kpis.appendChild(kpi("Ticket médio", r.ticketMedioCentavos, dinheiro, true));
    raiz.appendChild(kpis);

    if (r.canceladas > 0) {
      raiz.appendChild(no("p", "vazio-texto", plural(r.canceladas, "venda cancelada fora dos totais.", "vendas canceladas fora dos totais.")));
    }

    const grupos = no("div", "kpis");
    grupos.style.gridTemplateColumns = "1fr";
    grupos.appendChild(bloco("Por forma de pagamento",
      r.porForma.map((f) => linha(f.forma, plural(f.vendas, "venda", "vendas"), dinheiro(f.valorCentavos))),
      "Nenhuma venda no período."));
    grupos.appendChild(bloco("Mais vendidos",
      r.maisVendidos.map((p) => linha(p.nome, dinheiro(p.valorCentavos), plural(p.pecas, "peça", "peças"))),
      "Nenhuma venda no período."));
    raiz.appendChild(grupos);
  }

  function desenharBusca(b) {
    const raiz = el("vendas-busca");
    raiz.replaceChildren();

    if (!b.achou) {
      raiz.appendChild(no("p", "vazio-texto", "Nada vendido com “" + b.texto + "” neste período."));
      return;
    }

    const kpis = no("div", "kpis");
    const u = no("div", "kpi");
    u.appendChild(no("div", "rotulo", "Unidades"));
    u.appendChild(no("div", "valor", b.unidades));
    const v = no("div", "kpi");
    v.appendChild(no("div", "rotulo", "Valor vendido"));
    v.appendChild(no("div", "valor", dinheiro(b.valorCentavos)));
    kpis.appendChild(u);
    kpis.appendChild(v);
    raiz.appendChild(kpis);

    raiz.appendChild(bloco("Por tamanho",
      b.variacoes.map((x) => linha(x.tamanho || "Sem tamanho", dinheiro(x.valorCentavos), plural(x.unidades, "un.", "un."))),
      ""));

    raiz.appendChild(bloco("Últimas vendas",
      b.vendas.map((x) => {
        const quando = new Date(x.dataHora).toLocaleString("pt-BR", { day: "2-digit", month: "2-digit", hour: "2-digit", minute: "2-digit" });
        return linha(x.produto, quando + (x.tamanho ? " · " + x.tamanho : ""), x.quantidade + " × " + dinheiro(x.valorCentavos));
      }),
      ""));
  }

  function erroEm(raiz, erro) {
    raiz.replaceChildren(no("p", "vazio-texto", erro.message || "Não foi possível consultar."));
  }

  async function carregarResumo() {
    const minha = ++sequencia;
    el("vendas-resumo").replaceChildren(no("p", "vazio-texto", "Carregando…"));
    try {
      const r = await window.NexusMovel.api("/api/vendas/resumo?periodo=" + encodeURIComponent(periodo));
      if (minha === sequencia) desenharResumo(r);
    } catch (erro) {
      if (minha === sequencia) erroEm(el("vendas-resumo"), erro);
    }
  }

  async function pesquisar() {
    const texto = el("vendas-texto").value.trim();
    const raiz = el("vendas-busca");
    if (texto.length < 2) {
      raiz.replaceChildren();
      return;
    }

    try {
      const b = await window.NexusMovel.api(
        "/api/vendas/busca?periodo=" + encodeURIComponent(periodo) + "&texto=" + encodeURIComponent(texto));
      if (el("vendas-texto").value.trim() === texto) desenharBusca(b);
    } catch (erro) {
      erroEm(raiz, erro);
    }
  }

  function atualizar() {
    carregarResumo();
    pesquisar();
  }

  function mostrarAba(nome) {
    const vendas = nome === "vendas";
    el("aba-consulta").hidden = vendas;
    el("aba-vendas").hidden = !vendas;
    [["consulta", !vendas], ["vendas", vendas]].forEach(([id, ativa]) => {
      const b = el("aba-btn-" + id);
      b.classList.toggle("ativa", ativa);
      b.setAttribute("aria-selected", String(ativa));
    });
    if (vendas) atualizar();
  }

  document.addEventListener("DOMContentLoaded", () => {
    el("aba-btn-consulta").addEventListener("click", () => mostrarAba("consulta"));
    el("aba-btn-vendas").addEventListener("click", () => mostrarAba("vendas"));

    document.querySelectorAll(".periodo").forEach((b) => b.addEventListener("click", () => {
      periodo = b.dataset.periodo;
      document.querySelectorAll(".periodo").forEach((x) => x.classList.toggle("ativa", x === b));
      atualizar();
    }));

    el("vendas-texto").addEventListener("input", () => {
      clearTimeout(timerBusca);
      timerBusca = setTimeout(pesquisar, 350);
    });
    el("vendas-texto").addEventListener("keydown", (e) => {
      if (e.key === "Enter") {
        e.preventDefault();
        clearTimeout(timerBusca);
        pesquisar();
      }
    });
  });
})();
