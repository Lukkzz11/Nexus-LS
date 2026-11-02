import { RotateCcw } from "lucide-react";
import { Button } from "../../ui/Button";
import { Alternar, Deslizante, Secao, Segmentado } from "./campos";
import { FONTES, IDS_FONTE, MODELOS, PROPORCOES } from "../../../services/temaVitrine";
import { ALIGNS, ALIGN_LABELS } from "../../../services/vitrineConfig";

const ROTULOS_BORDA = { nenhuma: "Sem borda", solida: "Sólida", tracejada: "Tracejada" };
const ROTULOS_PRECO = { abaixo: "Abaixo do nome", foto: "Sobre a foto" };
const PESOS = { 400: "Normal", 600: "Semi", 700: "Negrito", 800: "Extra" };

function Restaurar({ onClick, rotulo }) {
  return (
    <Button size="sm" variant="ghost" onClick={onClick} leftIcon={<RotateCcw className="h-3.5 w-3.5" />}>
      {rotulo}
    </Button>
  );
}

/** Layout dos cartões, tipografia, margens e efeito vidro. Cada seção pode ser restaurada separadamente. */
export default function PainelLayout({ vitrine, alterar }) {
  const p = vitrine.produtos;
  const t = vitrine.tema.tipografia;
  const look = (MODELOS[vitrine.tema.modelo] || MODELOS.padrao).look;

  const set = (campo, chave) => (valor) => alterar((r) => ({ ...r, produtos: { ...r.produtos, [campo]: valor } }), chave || `produtos:${campo}`);
  const setTip = (campo, chave) => (valor) => alterar((r) => ({ ...r, tema: { ...r.tema, tipografia: { ...r.tema.tipografia, [campo]: valor } } }), chave || `tip:${campo}`);
  const setMargem = (campo) => (valor) => alterar((r) => ({ ...r, margens: { ...r.margens, [campo]: valor } }), `margem:${campo}`);

  const restaurarCartoes = () =>
    alterar((r) => ({
      ...r,
      produtos: {
        ...r.produtos,
        colunasPc: look.colunasPc, colunasCelular: look.colunasCelular, espacamento: look.espacamento, raio: look.raio,
        borda: look.borda, bordaLargura: look.bordaLargura, proporcao: look.proporcao, alinhamento: look.alinhamento,
        sombraForca: look.sombraForca, precoPosicao: "abaixo", porPagina: 0
      }
    }));
  const restaurarTipografia = () =>
    alterar((r) => ({ ...r, tema: { ...r.tema, tipografia: { fonte: look.fonte, pesoTitulo: 800, tamanho: 100 } } }));
  const restaurarMargens = () => alterar((r) => ({ ...r, margens: { externa: 24, interna: 16 } }));

  return (
    <div className="space-y-3">
      <Secao titulo="Cartões de produto" aberta resumo={`${p.colunasPc} colunas`}>
        <Deslizante rotulo="Produtos por linha (computador)" valor={p.colunasPc} min={2} max={6} onChange={set("colunasPc")} />
        <Segmentado rotulo="Produtos por linha (celular)" opcoes={[1, 2]} rotulos={{ 1: "1 coluna", 2: "2 colunas" }} valor={p.colunasCelular} onChange={set("colunasCelular")} />
        <Deslizante rotulo="Espaço entre produtos" valor={p.espacamento} min={4} max={48} unidade=" px" onChange={set("espacamento")} />
        <Deslizante rotulo="Arredondamento dos cartões" valor={p.raio} min={0} max={32} unidade=" px" onChange={set("raio")} />
        <Segmentado rotulo="Estilo da borda" opcoes={["nenhuma", "solida", "tracejada"]} rotulos={ROTULOS_BORDA} valor={p.borda} onChange={set("borda")} />
        {p.borda !== "nenhuma" && <Deslizante rotulo="Espessura da borda" valor={p.bordaLargura} min={1} max={4} unidade=" px" onChange={set("bordaLargura")} />}
        <Deslizante rotulo="Intensidade da sombra" valor={p.sombraForca} min={0} max={100} unidade="%" onChange={set("sombraForca")} />
        <label className="block text-xs text-slate-400">
          Proporção da foto
          <select
            value={p.proporcao}
            onChange={(e) => set("proporcao")(e.target.value)}
            className="mt-1 h-9 w-full rounded-lg border border-slate-700 bg-slate-950 px-3 text-sm text-slate-100 outline-none focus:border-sky-400"
          >
            {Object.entries(PROPORCOES).map(([id, nome]) => (
              <option key={id} value={id}>
                {nome}
              </option>
            ))}
          </select>
        </label>
        <Segmentado rotulo="Alinhamento dos textos e preços" opcoes={ALIGNS} rotulos={ALIGN_LABELS} valor={p.alinhamento} onChange={set("alinhamento")} />
        <Segmentado rotulo="Posição do preço" opcoes={["abaixo", "foto"]} rotulos={ROTULOS_PRECO} valor={p.precoPosicao} onChange={set("precoPosicao")} />
        <label className="block text-xs text-slate-400">
          Produtos por página (0 = todos numa página só)
          <input
            type="number"
            min="0"
            max="60"
            value={p.porPagina}
            onChange={(e) => {
              const n = Math.round(Number(e.target.value) || 0);
              set("porPagina", "produtos:porPagina")(n === 0 ? 0 : Math.max(4, Math.min(60, n)));
            }}
            className="mt-1 h-9 w-full rounded-lg border border-slate-700 bg-slate-950 px-3 text-sm text-slate-100 outline-none focus:border-sky-400"
          />
        </label>
        <Alternar
          rotulo="Efeito vidro (glassmorphism)"
          ajuda="Cartões translúcidos com desfoque. Desligado, os cartões ficam sólidos na cor do cartão."
          valor={vitrine.estilo.vidro}
          onChange={(v) => alterar((r) => ({ ...r, estilo: { ...r.estilo, vidro: v } }))}
        />
        <Restaurar onClick={restaurarCartoes} rotulo="Restaurar cartões ao padrão do modelo" />
      </Secao>

      <Secao titulo="Fontes e textos" resumo={FONTES[t.fonte]?.rotulo}>
        <label className="block text-xs text-slate-400">
          Família da fonte
          <select
            value={t.fonte}
            onChange={(e) => setTip("fonte")(e.target.value)}
            className="mt-1 h-9 w-full rounded-lg border border-slate-700 bg-slate-950 px-3 text-sm text-slate-100 outline-none focus:border-sky-400"
            style={{ fontFamily: FONTES[t.fonte]?.pilha }}
          >
            {IDS_FONTE.map((id) => (
              <option key={id} value={id}>
                {FONTES[id].rotulo}
              </option>
            ))}
          </select>
        </label>
        <Segmentado rotulo="Peso dos títulos" opcoes={[400, 600, 700, 800]} rotulos={PESOS} valor={t.pesoTitulo} onChange={setTip("pesoTitulo")} />
        <Deslizante rotulo="Tamanho dos textos dos produtos" valor={t.tamanho} min={85} max={130} unidade="%" onChange={setTip("tamanho")} />
        <Restaurar onClick={restaurarTipografia} rotulo="Restaurar fontes ao padrão do modelo" />
      </Secao>

      <Secao titulo="Margens e espaçamentos" resumo={`${vitrine.margens.externa}/${vitrine.margens.interna} px`}>
        <Deslizante rotulo="Margem externa da página" valor={vitrine.margens.externa} min={0} max={64} unidade=" px" onChange={setMargem("externa")} />
        <Deslizante rotulo="Margem interna dos cartões" valor={vitrine.margens.interna} min={0} max={48} unidade=" px" onChange={setMargem("interna")} />
        <Restaurar onClick={restaurarMargens} rotulo="Restaurar margens" />
      </Secao>
    </div>
  );
}
