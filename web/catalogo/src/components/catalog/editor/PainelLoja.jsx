import { useRef } from "react";
import { ImagePlus } from "lucide-react";
import { Button } from "../../ui/Button";
import { Alternar, CampoTexto, Deslizante, Secao, Segmentado } from "./campos";
import { ALIGNS, ALIGN_LABELS } from "../../../services/vitrineConfig";

/**
 * Identidade da loja no catálogo. O que ficar em branco usa o cadastro da empresa (Configurações):
 * mudar aqui altera só o catálogo, nunca o cadastro principal.
 */
export default function PainelLoja({ vitrine, alterar, empresa, enviandoLogo, onEnviarLogo, onRemoverLogo }) {
  const arquivoRef = useRef(null);
  const l = vitrine.loja;
  const r = vitrine.rodape;
  const tx = vitrine.textos;
  const emp = empresa || {};

  const setLoja = (campo) => (valor) => alterar((v) => ({ ...v, loja: { ...v.loja, [campo]: valor } }), `loja:${campo}`);
  const setRodape = (campo) => (valor) => alterar((v) => ({ ...v, rodape: { ...v.rodape, [campo]: valor } }), `rodape:${campo}`);
  const setTexto = (campo) => (valor) => alterar((v) => ({ ...v, textos: { ...v.textos, [campo]: valor } }), `textos:${campo}`);
  const enderecoEmpresa = [emp.endereco, emp.bairro, emp.cidade].filter(Boolean).join(", ");

  return (
    <div className="space-y-3">
      <Secao titulo="Logotipo e nome" aberta>
        <input
          ref={arquivoRef}
          type="file"
          accept="image/jpeg,image/png,image/webp"
          className="hidden"
          onChange={(e) => e.target.files?.[0] && onEnviarLogo(e.target.files[0], arquivoRef)}
        />
        <div className="flex items-center gap-3 rounded-xl border border-slate-800 bg-slate-950/40 p-3">
          <div className="grid h-14 w-24 shrink-0 place-items-center overflow-hidden rounded-lg border border-slate-700 bg-white">
            {l.logoUrl ? <img src={l.logoUrl} alt="Logotipo atual" className="max-h-full max-w-full object-contain" /> : <span className="px-1 text-center text-[10px] text-slate-500">Logotipo padrão do sistema</span>}
          </div>
          <div className="flex flex-wrap gap-2">
            <Button size="sm" variant="outline" isLoading={enviandoLogo} onClick={() => arquivoRef.current?.click()} leftIcon={<ImagePlus className="h-4 w-4" />}>
              {l.logoUrl ? "Trocar logotipo" : "Enviar logotipo"}
            </Button>
            {l.logoUrl && (
              <Button size="sm" variant="ghost" onClick={onRemoverLogo}>
                Remover
              </Button>
            )}
          </div>
        </div>
        <p className="text-[11px] text-slate-500">PNG com fundo transparente funciona melhor. Imagens são reduzidas automaticamente.</p>
        <Alternar rotulo="Mostrar o logotipo" valor={l.mostrarLogo} onChange={setLoja("mostrarLogo")} />
        <Alternar rotulo="Mostrar o nome da loja ao lado" valor={l.mostrarNome} onChange={setLoja("mostrarNome")} />
        <Deslizante rotulo="Altura do logotipo" valor={l.logoAltura} min={20} max={96} unidade=" px" onChange={setLoja("logoAltura")} />
        <Segmentado rotulo="Alinhamento do cabeçalho" opcoes={ALIGNS} rotulos={ALIGN_LABELS} valor={l.alinhamento} onChange={setLoja("alinhamento")} />
        <CampoTexto rotulo="Nome da loja no catálogo" valor={l.nome} onChange={setLoja("nome")} maxLength={80} placeholder={emp.nome || "Nome cadastrado da empresa"} ajuda="Em branco usa o nome cadastrado da empresa." />
      </Secao>

      <Secao titulo="Textos da página">
        <CampoTexto rotulo="Aviso no topo (deixe vazio para ocultar)" valor={tx.anuncio} onChange={setTexto("anuncio")} maxLength={160} />
        <CampoTexto rotulo="Texto pequeno acima do título" valor={tx.eyebrow} onChange={setTexto("eyebrow")} maxLength={60} />
        <CampoTexto rotulo="Título da vitrine" valor={tx.titulo} onChange={setTexto("titulo")} maxLength={80} />
        <label className="block">
          <span className="mb-1 block text-xs text-slate-400">Texto de apresentação</span>
          <textarea
            rows={3}
            maxLength={300}
            value={l.apresentacao}
            placeholder={emp.mensagemCatalogo || "Apresente a loja em uma ou duas frases"}
            onChange={(e) => setLoja("apresentacao")(e.target.value)}
            className="w-full rounded-lg border border-slate-700 bg-slate-950 px-3 py-2 text-sm text-slate-100 outline-none placeholder:text-slate-600 focus:border-sky-400"
          />
          <span className="mt-1 block text-[11px] text-slate-500">Em branco usa a mensagem do catálogo cadastrada na empresa.</span>
        </label>
      </Secao>

      <Secao titulo="Contatos e redes sociais">
        <CampoTexto rotulo="Telefone" valor={l.telefone} onChange={setLoja("telefone")} maxLength={30} placeholder={emp.telefone || ""} />
        <CampoTexto rotulo="WhatsApp" valor={l.whatsapp} onChange={setLoja("whatsapp")} maxLength={30} placeholder={emp.whatsapp || ""} />
        <CampoTexto rotulo="Instagram" valor={l.instagram} onChange={setLoja("instagram")} maxLength={60} placeholder="@sualoja" />
        <CampoTexto rotulo="Facebook" valor={l.facebook} onChange={setLoja("facebook")} maxLength={60} placeholder="facebook.com/sualoja" />
        <CampoTexto rotulo="E-mail" valor={l.email} onChange={setLoja("email")} maxLength={80} placeholder={emp.email || ""} tipo="email" />
        <CampoTexto rotulo="Endereço" valor={l.endereco} onChange={setLoja("endereco")} maxLength={160} placeholder={enderecoEmpresa} />
        <p className="text-xs text-slate-400">Escolha o que aparece no catálogo:</p>
        <div className="space-y-2">
          <Alternar rotulo="Telefone" valor={l.mostrarTelefone} onChange={setLoja("mostrarTelefone")} />
          <Alternar rotulo="WhatsApp (botão no cabeçalho e no rodapé)" valor={l.mostrarWhatsapp} onChange={setLoja("mostrarWhatsapp")} />
          <Alternar rotulo="Redes sociais" valor={l.mostrarRedes} onChange={setLoja("mostrarRedes")} />
          <Alternar rotulo="Endereço" valor={l.mostrarEndereco} onChange={setLoja("mostrarEndereco")} />
          <Alternar rotulo="E-mail" valor={l.mostrarEmail} onChange={setLoja("mostrarEmail")} />
        </div>
      </Secao>

      <Secao titulo="Rodapé">
        <label className="block">
          <span className="mb-1 block text-xs text-slate-400">Texto do rodapé (opcional)</span>
          <textarea
            rows={2}
            maxLength={300}
            value={r.texto}
            onChange={(e) => setRodape("texto")(e.target.value)}
            className="w-full rounded-lg border border-slate-700 bg-slate-950 px-3 py-2 text-sm text-slate-100 outline-none focus:border-sky-400"
          />
        </label>
        <Alternar rotulo="Mostrar contatos no rodapé" valor={r.mostrarContato} onChange={setRodape("mostrarContato")} />
        <Alternar rotulo="Mostrar “todos os direitos reservados”" valor={r.mostrarCopyright} onChange={setRodape("mostrarCopyright")} />
      </Secao>
    </div>
  );
}
