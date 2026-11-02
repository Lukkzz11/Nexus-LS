import { FileDown } from "lucide-react";
import { Button } from "../../ui/Button";
import { Alternar, Deslizante, Secao, Segmentado } from "./campos";
import { FORMATOS_PDF, totalPaginasPdf } from "../../../services/vitrineConfig";

const ROTULOS_FORMATO = Object.fromEntries(Object.entries(FORMATOS_PDF).map(([id, f]) => [id, f.rotulo]));

/** Formato e estrutura do catálogo em PDF. A exportação usa exatamente o que a pré-visualização mostra. */
export default function PainelPdf({ vitrine, alterar, totalProdutos, onExportar, exportando }) {
  const pdf = vitrine.pdf;
  const set = (campo, chave) => (valor) => alterar((r) => ({ ...r, pdf: { ...r.pdf, [campo]: valor } }), chave || `pdf:${campo}`);
  const paginas = totalPaginasPdf(pdf, totalProdutos);

  return (
    <div className="space-y-3">
      <div className="space-y-2 rounded-2xl border border-slate-800 bg-slate-900/60 p-4">
        <p className="text-sm text-slate-100">
          {totalProdutos} produto(s) em <strong>{paginas}</strong> página(s).
        </p>
        <Button variant="primary" onClick={onExportar} isLoading={exportando} disabled={totalProdutos === 0} leftIcon={<FileDown className="h-4 w-4" />} className="w-full">
          Exportar PDF
        </Button>
        <p className="text-xs text-slate-400">
          Abre a janela de impressão do navegador: escolha <strong>Salvar como PDF</strong> como destino e deixe as margens em “Nenhuma”. As cores e o layout saem como na pré-visualização.
        </p>
      </div>

      <Secao titulo="Página" aberta>
        <Segmentado rotulo="Formato" opcoes={Object.keys(FORMATOS_PDF)} rotulos={ROTULOS_FORMATO} valor={pdf.formato} onChange={set("formato")} />
        <Segmentado rotulo="Orientação" opcoes={["retrato", "paisagem"]} rotulos={{ retrato: "Retrato", paisagem: "Paisagem" }} valor={pdf.orientacao} onChange={set("orientacao")} />
        <Deslizante rotulo="Margem da página" valor={pdf.margemMm} min={5} max={25} unidade=" mm" onChange={set("margemMm")} />
      </Secao>

      <Secao titulo="Produtos por página" aberta resumo={`${pdf.colunas}×${pdf.linhas}`}>
        <Deslizante rotulo="Colunas" valor={pdf.colunas} min={1} max={5} onChange={set("colunas")} />
        <Deslizante rotulo="Linhas" valor={pdf.linhas} min={1} max={6} onChange={set("linhas")} />
        <Segmentado rotulo="Tabela de preços" opcoes={["varejo", "atacado"]} rotulos={{ varejo: "Varejo", atacado: "Atacado" }} valor={pdf.tabela} onChange={set("tabela")} />
        <p className="text-[11px] text-slate-500">O formato dos cartões (cores, bordas, fotos, o que aparece) é o mesmo das abas Layout e Produtos.</p>
      </Secao>

      <Secao titulo="Capa, cabeçalho e rodapé">
        <Alternar rotulo="Página de capa" ajuda="Logotipo, nome, apresentação e a imagem do primeiro banner." valor={pdf.capa} onChange={set("capa")} />
        <Alternar rotulo="Cabeçalho em cada página" valor={pdf.cabecalhoPagina} onChange={set("cabecalhoPagina")} />
        <Alternar rotulo="Rodapé em cada página" ajuda="Mostra o texto do rodapé (ou os contatos)." valor={pdf.rodapePagina} onChange={set("rodapePagina")} />
        <Alternar rotulo="Numeração das páginas" valor={pdf.numeracao} onChange={set("numeracao")} disabled={!pdf.rodapePagina} />
      </Secao>
    </div>
  );
}
