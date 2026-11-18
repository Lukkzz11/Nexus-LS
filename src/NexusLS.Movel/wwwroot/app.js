/*
  Nexus Móvel — a lógica da tela do celular.
  Fala só com o computador da loja (mesma rede Wi-Fi). O PIN vem da janela do
  NexusMovel.exe e fica guardado neste aparelho; "Sair" apaga.
*/
(() => {
  "use strict";

  const CHAVE_PIN = "nexus.movel.pin";
  const CHAVE_HISTORICO = "nexus.movel.historico";

  const el = (id) => document.getElementById(id);

  const estado = {
    pin: "",
    loja: "Nexus",
    ocupado: false,
    timerSugestoes: 0,
    timerAviso: 0
  };

  // ---------------------------------------------------------------------------
  // Utilidades
  // ---------------------------------------------------------------------------

  const escapar = (texto) => String(texto ?? "").replace(/[&<>"']/g, (c) => ({
    "&": "&amp;",
    "<": "&lt;",
    ">": "&gt;",
    '"': "&quot;",
    "'": "&#39;"
  }[c]));

  const dinheiro = (centavos) =>
    (Number(centavos || 0) / 100).toLocaleString("pt-BR", { style: "currency", currency: "BRL" });

  function aviso(texto, erro = false) {
    const caixa = el("aviso");
    if (!texto) {
      caixa.hidden = true;
      return;
    }

    caixa.textContent = texto;
    caixa.classList.toggle("erro", erro);
    caixa.hidden = false;

    clearTimeout(estado.timerAviso);
    estado.timerAviso = setTimeout(() => { caixa.hidden = true; }, 3200);
  }

  async function api(caminho) {
    const resposta = await fetch(caminho, {
      headers: { "X-Pin": estado.pin },
      cache: "no-store"
    });

    if (resposta.status === 401 || resposta.status === 429) {
      const corpo = await resposta.json().catch(() => ({}));
      sair(false);
      throw new Error(corpo.erro || "PIN incorreto.");
    }

    if (!resposta.ok) {
      throw new Error("O computador respondeu com erro " + resposta.status + ".");
    }

    return resposta.json();
  }

  function marcarStatus(conectado) {
    const ponto = el("status");
    ponto.classList.toggle("ok", conectado);
    ponto.classList.toggle("ruim", !conectado);
    ponto.title = conectado ? "Conectado ao computador da loja" : "Sem conexão com o computador";
  }

  // ---------------------------------------------------------------------------
  // Entrada e saída (PIN)
  // ---------------------------------------------------------------------------

  function sair(mostrarMensagem) {
    estado.pin = "";
    localStorage.removeItem(CHAVE_PIN);

    el("app").hidden = true;
    el("portao").hidden = false;
    el("resultado").innerHTML = "";
    el("sugestoes").innerHTML = "";
    el("pin").value = "";
    el("pin-erro").textContent = mostrarMensagem ? "A conexão caiu. Digite o PIN de novo." : "";

    setTimeout(() => el("pin").focus(), 60);
  }

  async function entrarComPin(pin) {
    if (!/^\d{6}$/.test(pin)) {
      el("pin-erro").textContent = "Digite os 6 números do PIN.";
      el("pin").focus();
      return;
    }

    estado.pin = pin;
    el("entrar").disabled = true;
    el("pin-erro").textContent = "";

    try {
      const saude = await api("/api/saude");

      estado.loja = saude.loja || "Nexus";
      el("nome-loja").textContent = estado.loja;
      localStorage.setItem(CHAVE_PIN, pin);

      el("portao").hidden = true;
      el("app").hidden = false;

      marcarStatus(saude.ok !== false);
      mostrarHistorico();
      el("codigo").focus();
    } catch (erro) {
      el("pin-erro").textContent = erro.message;
      el("pin").select();
    } finally {
      el("entrar").disabled = false;
    }
  }

  function entrar() {
    const pin = el("pin").value.replace(/\D/g, "").slice(0, 6);
    entrarComPin(pin);
  }

  // ---------------------------------------------------------------------------
  // Consulta
  // ---------------------------------------------------------------------------

  async function consultar(texto) {
    const codigo = String(texto ?? "").trim();
    if (!codigo || estado.ocupado) return;

    estado.ocupado = true;
    el("codigo").value = codigo;
    el("limpar").hidden = false;
    el("sugestoes").innerHTML = "";
    el("resultado").innerHTML = carregandoHtml();

    try {
      const resposta = await api("/api/produto/" + encodeURIComponent(codigo));
      marcarStatus(true);
      el("resultado").innerHTML = resposta.encontrado ? produtoHtml(resposta) : vazioHtml(resposta);

      if (resposta.encontrado) {
        guardarNoHistorico(resposta);
      }
    } catch (erro) {
      marcarStatus(false);
      el("resultado").innerHTML =
        `<div class="cartao"><div class="vazio">
           <span class="simbolo">⚠</span>
           <h3>Sem resposta do computador</h3>
           <p>${escapar(erro.message)}</p>
           <p>Confira se o NexusMovel está aberto e se o celular está no mesmo Wi-Fi.</p>
         </div></div>`;
    } finally {
      estado.ocupado = false;
    }
  }

  async function buscarSugestoes(texto) {
    if (texto.length < 2 || !estado.pin) return;

    try {
      const resposta = await api("/api/busca?texto=" + encodeURIComponent(texto));
      const itens = resposta.itens || [];

      el("sugestoes").innerHTML = itens.map((p) => `
        <button class="sugestao" type="button" data-texto="${escapar(p.referencia || p.nome)}">
          <span>
            <strong>${escapar(p.nome)}</strong>
            <small>${escapar([p.referencia ? "Ref. " + p.referencia : "", p.categoria || ""].filter(Boolean).join(" · "))}</small>
          </span>
          <span class="preco-mini">${dinheiro(p.precoVarejoCentavos)}</span>
        </button>`).join("");
    } catch {
      el("sugestoes").innerHTML = "";
    }
  }

  // ---------------------------------------------------------------------------
  // Desenho da tela
  // ---------------------------------------------------------------------------

  function carregandoHtml() {
    return `<div class="cartao"><div class="vazio">
      <span class="simbolo">⌛</span>
      <h3>Consultando…</h3>
      <p>Falando com o computador da loja.</p>
    </div></div>`;
  }

  function produtoHtml(resposta) {
    const p = resposta.produto;
    const selecionado = p.tamanhos.find((t) => t.selecionado);

    const meta = [
      p.referencia ? `Ref. ${escapar(p.referencia)}` : "",
      p.categoria ? escapar(p.categoria) : "",
      p.cores ? escapar(p.cores) : ""
    ].filter(Boolean).join(" · ");

    const selo = p.venderSemBaixa
      ? `<span class="selo ok">Sem controle de estoque</span>`
      : p.estoqueTotal <= 0
        ? `<span class="selo sem">Sem estoque</span>`
        : p.estoqueBaixo
          ? `<span class="selo baixo">Estoque baixo: ${p.estoqueTotal}</span>`
          : `<span class="selo ok">${p.estoqueTotal} em estoque</span>`;

    const foto = p.foto
      ? `<img class="foto" src="${escapar(p.foto)}?pin=${encodeURIComponent(estado.pin)}" alt="" loading="lazy">`
      : `<div class="foto-vazia" aria-hidden="true">▢</div>`;

    const atacado = p.precoAtacadoCentavos > 0 && p.precoAtacadoCentavos !== p.precoVarejoCentavos
      ? `<div class="preco-caixa">
           <div class="preco-rotulo">Atacado</div>
           <div class="preco-valor">${dinheiro(p.precoAtacadoCentavos)}</div>
         </div>`
      : "";

    const tamanhos = p.tamanhos.map((t) => `
      <button class="tamanho ${t.selecionado ? "selecionado" : ""} ${t.estoque <= 0 ? "esgotado" : ""}"
              type="button"
              data-codigo="${escapar(t.codigo || "")}"
              data-nome="${escapar(t.nome)}"
              title="${t.estoque} em estoque">
        <span>${escapar(t.nome)}</span>
        <span class="qtd">${t.estoque}</span>
      </button>`).join("");

    const linhaTamanho = selecionado
      ? `<p class="codigo-tamanho">Tamanho lido: <strong>${escapar(selecionado.nome)}</strong>${
          selecionado.codigo
            ? ` · código <strong>${escapar(selecionado.codigo)}</strong>
               <button class="link" type="button" data-copiar="${escapar(selecionado.codigo)}">Copiar</button>`
            : ""
        }</p>`
      : "";

    return `
      <div class="cartao">
        <div class="produto-topo">
          ${foto}
          <div>
            <h2 class="produto-nome">${escapar(p.nome)}</h2>
            <p class="produto-meta">${meta || "Sem referência"}</p>
            ${selo}
          </div>
        </div>

        <div class="precos">
          <div class="preco-caixa destaque">
            <div class="preco-rotulo">Varejo</div>
            <div class="preco-valor">${dinheiro(p.precoVarejoCentavos)}</div>
          </div>
          ${atacado}
        </div>

        <div class="grade">
          <div class="grade-titulo">
            <span>Tamanhos</span>
            <span>${p.tamanhos.length} ${p.tamanhos.length === 1 ? "opção" : "opções"}</span>
          </div>
          <div class="tamanhos">${tamanhos}</div>
          ${linhaTamanho}
        </div>
      </div>`;
  }

  function vazioHtml(resposta) {
    const sugestoes = (resposta.sugestoes || []).map((p) => `
      <button class="sugestao" type="button" data-texto="${escapar(p.referencia || p.nome)}">
        <span>
          <strong>${escapar(p.nome)}</strong>
          <small>${escapar([p.referencia ? "Ref. " + p.referencia : "", p.categoria || ""].filter(Boolean).join(" · "))}</small>
        </span>
        <span class="preco-mini">${dinheiro(p.precoVarejoCentavos)}</span>
      </button>`).join("");

    return `
      <div class="cartao">
        <div class="vazio">
          <span class="simbolo">⌕</span>
          <h3>Nada encontrado</h3>
          <p>${escapar(resposta.mensagem || "Confira o código e tente de novo.")}</p>
        </div>
      </div>
      ${sugestoes ? `<div class="sugestoes">${sugestoes}</div>` : ""}`;
  }

  // ---------------------------------------------------------------------------
  // Histórico do aparelho
  // ---------------------------------------------------------------------------

  function lerHistorico() {
    try {
      const lista = JSON.parse(localStorage.getItem(CHAVE_HISTORICO) || "[]");
      return Array.isArray(lista) ? lista : [];
    } catch {
      return [];
    }
  }

  function guardarNoHistorico(resposta) {
    const lista = lerHistorico().filter((item) => item.codigo !== resposta.codigoLido);
    lista.unshift({
      codigo: resposta.codigoLido,
      nome: resposta.produto.nome,
      quando: Date.now()
    });

    localStorage.setItem(CHAVE_HISTORICO, JSON.stringify(lista.slice(0, 12)));
    mostrarHistorico();
  }

  function mostrarHistorico() {
    const lista = lerHistorico();
    el("historico-area").hidden = lista.length === 0;

    el("historico").innerHTML = lista.map((item) => {
      const hora = new Date(item.quando).toLocaleTimeString("pt-BR", { hour: "2-digit", minute: "2-digit" });
      return `<button class="item-historico" type="button" data-texto="${escapar(item.codigo)}">
        <span><strong>${escapar(item.nome)}</strong><br><small>${escapar(item.codigo)}</small></span>
        <span class="quando">${hora}</span>
      </button>`;
    }).join("");
  }

  // ---------------------------------------------------------------------------
  // Câmera
  // ---------------------------------------------------------------------------

  function abrirCamera() {
    const temDetector = "BarcodeDetector" in window;
    const temCamera = !!(navigator.mediaDevices && navigator.mediaDevices.getUserMedia);

    // Ao vivo só quando o navegador considera a página segura (localhost ou HTTPS).
    // No Wi-Fi por HTTP, o celular abre a câmera do sistema e a foto é lida aqui.
    if (window.isSecureContext && temDetector && temCamera) {
      cameraAoVivo();
      return;
    }

    el("foto").click();
  }

  async function cameraAoVivo() {
    const camada = el("leitor");
    const video = el("video");
    let fluxo = null;
    let parado = false;
    let detector = null;

    const fechar = () => {
      parado = true;
      if (fluxo) fluxo.getTracks().forEach((t) => t.stop());
      video.srcObject = null;
      camada.hidden = true;
    };

    el("fechar-leitor").onclick = fechar;
    camada.hidden = false;

    try {
      fluxo = await navigator.mediaDevices.getUserMedia({
        video: { facingMode: { ideal: "environment" } },
        audio: false
      });
      video.srcObject = fluxo;
      await video.play();

      try {
        detector = new BarcodeDetector({
          formats: ["ean_13", "ean_8", "upc_a", "upc_e", "code_128", "code_39", "qr_code"]
        });
      } catch {
        detector = new BarcodeDetector();
      }
    } catch {
      fechar();
      aviso("Não foi possível abrir a câmera. Use o campo de código.", true);
      return;
    }

    const laco = async () => {
      if (parado) return;

      try {
        const codigos = await detector.detect(video);
        if (codigos.length && codigos[0].rawValue) {
          const texto = codigos[0].rawValue;
          fechar();
          consultar(texto);
          return;
        }
      } catch {
        // quadro ruim: tenta de novo
      }

      setTimeout(laco, 220);
    };

    laco();
  }

  async function decodificarFoto(arquivo) {
    const url = URL.createObjectURL(arquivo);

    try {
      if (window.ZXing && window.ZXing.BrowserMultiFormatReader) {
        try {
          const leitor = new ZXing.BrowserMultiFormatReader();
          const resultado = await leitor.decodeFromImageUrl(url);
          if (resultado && resultado.getText()) return resultado.getText();
        } catch {
          // a foto pode estar sem foco: tenta o detector do navegador
        }
      }

      if ("BarcodeDetector" in window) {
        const detector = new BarcodeDetector();
        const imagem = await createImageBitmap(arquivo);
        const codigos = await detector.detect(imagem);
        if (codigos.length && codigos[0].rawValue) return codigos[0].rawValue;
      }

      return null;
    } finally {
      URL.revokeObjectURL(url);
    }
  }

  // ---------------------------------------------------------------------------
  // Eventos
  // ---------------------------------------------------------------------------

  function ligarEventos() {
    el("entrar").addEventListener("click", entrar);

    el("pin").addEventListener("input", () => {
      const limpo = el("pin").value.replace(/\D/g, "").slice(0, 6);
      el("pin").value = limpo;
      el("pin-erro").textContent = "";
      if (limpo.length === 6) entrar();
    });

    el("pin").addEventListener("keydown", (e) => {
      if (e.key === "Enter") entrar();
    });

    el("sair").addEventListener("click", () => sair(false));

    el("codigo").addEventListener("keydown", (e) => {
      if (e.key === "Enter") {
        e.preventDefault();
        consultar(el("codigo").value);
      }
    });

    el("codigo").addEventListener("input", () => {
      const texto = el("codigo").value.trim();
      el("limpar").hidden = texto.length === 0;

      clearTimeout(estado.timerSugestoes);
      if (texto.length < 2) {
        el("sugestoes").innerHTML = "";
        return;
      }

      estado.timerSugestoes = setTimeout(() => buscarSugestoes(texto), 320);
    });

    el("limpar").addEventListener("click", () => {
      el("codigo").value = "";
      el("limpar").hidden = true;
      el("sugestoes").innerHTML = "";
      el("resultado").innerHTML = "";
      el("codigo").focus();
    });

    el("camera").addEventListener("click", abrirCamera);

    el("foto").addEventListener("change", async (e) => {
      const arquivo = e.target.files && e.target.files[0];
      e.target.value = "";
      if (!arquivo) return;

      aviso("Lendo a foto…");

      try {
        const codigo = await decodificarFoto(arquivo);
        if (codigo) {
          aviso("");
          consultar(codigo);
        } else {
          aviso("Não consegui ler o código. Aproxime e tente de novo.", true);
        }
      } catch {
        aviso("Não consegui ler a foto.", true);
      }
    });

    el("sugestoes").addEventListener("click", (e) => {
      const botao = e.target.closest("[data-texto]");
      if (botao) consultar(botao.dataset.texto);
    });

    el("historico").addEventListener("click", (e) => {
      const botao = e.target.closest("[data-texto]");
      if (botao) consultar(botao.dataset.texto);
    });

    el("limpar-historico").addEventListener("click", () => {
      localStorage.removeItem(CHAVE_HISTORICO);
      mostrarHistorico();
    });

    el("resultado").addEventListener("click", async (e) => {
      const copiar = e.target.closest("[data-copiar]");
      if (copiar) {
        try {
          await navigator.clipboard.writeText(copiar.dataset.copiar);
          aviso("Código copiado: " + copiar.dataset.copiar);
        } catch {
          aviso("Não foi possível copiar.", true);
        }
        return;
      }

      const tamanho = e.target.closest(".tamanho");
      if (!tamanho) return;

      el("resultado").querySelectorAll(".tamanho").forEach((b) => b.classList.remove("selecionado"));
      tamanho.classList.add("selecionado");

      const codigo = tamanho.dataset.codigo;
      const linha = el("resultado").querySelector(".codigo-tamanho");

      if (linha) {
        linha.innerHTML = codigo
          ? `Tamanho <strong>${escapar(tamanho.dataset.nome)}</strong> · código <strong>${escapar(codigo)}</strong>
             <button class="link" type="button" data-copiar="${escapar(codigo)}">Copiar</button>`
          : `Tamanho <strong>${escapar(tamanho.dataset.nome)}</strong> · sem código próprio`;
      }
    });

    // Foto que não carrega vira o quadrinho vazio, sem quebrar o layout.
    document.addEventListener("error", (e) => {
      const img = e.target;
      if (!(img instanceof HTMLImageElement) || !img.classList.contains("foto")) return;

      const vazio = document.createElement("div");
      vazio.className = "foto-vazia";
      vazio.setAttribute("aria-hidden", "true");
      vazio.textContent = "▢";
      img.replaceWith(vazio);
    }, true);
  }

  function registrarServiceWorker() {
    // Service worker exige HTTPS ou localhost; na rede da loja por HTTP ele apenas não registra.
    if ("serviceWorker" in navigator && window.isSecureContext) {
      navigator.serviceWorker.register("/sw.js").catch(() => { /* sem problema */ });
    }
  }

  function iniciar() {
    ligarEventos();
    registrarServiceWorker();

    const guardado = localStorage.getItem(CHAVE_PIN) || "";
    if (/^\d{6}$/.test(guardado)) {
      entrarComPin(guardado);
    } else {
      setTimeout(() => el("pin").focus(), 80);
    }
  }

  document.addEventListener("DOMContentLoaded", iniciar);
})();
