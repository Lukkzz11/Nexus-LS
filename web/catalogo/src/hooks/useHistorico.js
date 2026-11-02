import { useCallback, useRef, useState } from "react";

const LIMITE = 100;
const JANELA_AGRUPAR_MS = 800;

/**
 * Estado com desfazer e refazer. `alterar(proximo, chave)` aceita o novo valor ou uma função (como o setState).
 * Alterações seguidas com a MESMA `chave` dentro de 0,8 s viram um único passo do histórico
 * (ex.: arrastar o seletor de cor ou digitar um texto), em vez de um passo por tecla.
 */
export function useHistorico(inicial) {
  const [estado, setEstado] = useState({ atual: inicial, passado: [], futuro: [] });
  const ultima = useRef({ chave: null, em: 0 });

  const alterar = useCallback((proximo, chave = null) => {
    setEstado((e) => {
      const valor = typeof proximo === "function" ? proximo(e.atual) : proximo;
      if (valor === e.atual) return e;
      const agora = Date.now();
      const agrupar = chave && ultima.current.chave === chave && agora - ultima.current.em < JANELA_AGRUPAR_MS && e.passado.length > 0;
      ultima.current = { chave, em: agora };
      return {
        atual: valor,
        passado: agrupar ? e.passado : [...e.passado, e.atual].slice(-LIMITE),
        futuro: []
      };
    });
  }, []);

  const desfazer = useCallback(() => {
    ultima.current = { chave: null, em: 0 };
    setEstado((e) => {
      if (!e.passado.length) return e;
      const anterior = e.passado[e.passado.length - 1];
      return { atual: anterior, passado: e.passado.slice(0, -1), futuro: [e.atual, ...e.futuro] };
    });
  }, []);

  const refazer = useCallback(() => {
    ultima.current = { chave: null, em: 0 };
    setEstado((e) => {
      if (!e.futuro.length) return e;
      const [proximo, ...resto] = e.futuro;
      return { atual: proximo, passado: [...e.passado, e.atual], futuro: resto };
    });
  }, []);

  /** Troca o valor e zera o histórico (ex.: depois de publicar ou descartar). */
  const reiniciar = useCallback((valor) => {
    ultima.current = { chave: null, em: 0 };
    setEstado({ atual: valor, passado: [], futuro: [] });
  }, []);

  return {
    valor: estado.atual,
    alterar,
    desfazer,
    refazer,
    reiniciar,
    podeDesfazer: estado.passado.length > 0,
    podeRefazer: estado.futuro.length > 0
  };
}
