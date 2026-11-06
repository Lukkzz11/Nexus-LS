import React, { useState } from "react";
import {
  Users,
  Search,
  Plus,
  Edit2,
  KeyRound,
  Mail,
  Phone,
  Building,
  FileText,
  ChevronRight,
  ExternalLink,
} from "lucide-react";
import { Cliente, Licenca } from "../../types";

interface ClientsViewProps {
  clientes: Cliente[];
  licencas: Licenca[];
  onOpenNovoCliente: () => void;
  onEditarCliente: (cliente: Cliente) => void;
  onGerarLicencaParaCliente: (cliente: Cliente) => void;
}

export const ClientsView: React.FC<ClientsViewProps> = ({
  clientes,
  licencas,
  onOpenNovoCliente,
  onEditarCliente,
  onGerarLicencaParaCliente,
}) => {
  const [busca, setBusca] = useState("");
  const [clienteSelecionado, setClienteSelecionado] = useState<Cliente | null>(null);

  const filtrados = clientes.filter((c) => {
    const termo = busca.toLowerCase();
    return (
      c.nomeResponsavel.toLowerCase().includes(termo) ||
      c.empresa.toLowerCase().includes(termo) ||
      (c.documento && c.documento.toLowerCase().includes(termo)) ||
      c.email.toLowerCase().includes(termo)
    );
  });

  const licencasDoCliente = clienteSelecionado
    ? licencas.filter((l) => l.clienteId === clienteSelecionado.id)
    : [];

  return (
    <div className="space-y-6">
      {/* Top Bar with Search & Action */}
      <div className="bg-white rounded-xl p-4 border border-slate-200 shadow-2xs flex flex-col sm:flex-row sm:items-center justify-between gap-4">
        <div className="relative flex-1 max-w-md">
          <Search className="w-4 h-4 text-slate-400 absolute left-3 top-1/2 -translate-y-1/2" />
          <input
            type="text"
            value={busca}
            onChange={(e) => setBusca(e.target.value)}
            placeholder="Buscar por nome, empresa, documento ou e-mail..."
            className="w-full text-xs pl-9 pr-4 py-2 border border-slate-200 rounded-lg focus:outline-none focus:ring-2 focus:ring-blue-500/20 focus:border-blue-600 transition"
          />
        </div>

        <div className="flex items-center gap-3">
          <span className="text-xs text-slate-500">
            Total: <strong>{filtrados.length}</strong> de {clientes.length}
          </span>
          <button
            onClick={onOpenNovoCliente}
            className="flex items-center gap-2 px-4 py-2 text-xs font-semibold text-white bg-blue-600 hover:bg-blue-700 rounded-lg shadow-xs transition"
          >
            <Plus className="w-4 h-4 stroke-[2.5]" />
            <span>Novo Cliente</span>
          </button>
        </div>
      </div>

      {/* Main Table */}
      <div className="bg-white rounded-xl border border-slate-200 shadow-2xs overflow-hidden">
        <div className="overflow-x-auto">
          <table className="w-full text-left border-collapse text-xs">
            <thead>
              <tr className="bg-slate-50/75 border-b border-slate-100 text-slate-500 font-semibold uppercase text-[10px] tracking-wider">
                <th className="py-3 px-4">Responsável</th>
                <th className="py-3 px-4">Empresa / Loja</th>
                <th className="py-3 px-4">Documento</th>
                <th className="py-3 px-4">Contato</th>
                <th className="py-3 px-4 text-center">Licenças</th>
                <th className="py-3 px-4 text-right">Ações</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100 text-slate-700">
              {filtrados.length === 0 ? (
                <tr>
                  <td colSpan={6} className="py-12 text-center text-slate-400">
                    Nenhum cliente encontrado para os critérios de busca.
                  </td>
                </tr>
              ) : (
                filtrados.map((cli) => {
                  const qtdLicencas = licencas.filter((l) => l.clienteId === cli.id).length;
                  return (
                    <tr
                      key={cli.id}
                      className="hover:bg-slate-50/60 transition duration-75 cursor-pointer"
                      onClick={() => setClienteSelecionado(cli)}
                    >
                      <td className="py-3 px-4 font-semibold text-slate-900">
                        {cli.nomeResponsavel}
                        <p className="text-[10px] font-normal text-slate-400 font-mono mt-0.5">
                          ID: {cli.id}
                        </p>
                      </td>
                      <td className="py-3 px-4 font-medium text-slate-800">{cli.empresa}</td>
                      <td className="py-3 px-4 text-slate-500 font-mono">{cli.documento || "—"}</td>
                      <td className="py-3 px-4">
                        <p className="text-slate-800">{cli.email}</p>
                        {cli.telefone && <p className="text-[11px] text-slate-400">{cli.telefone}</p>}
                      </td>
                      <td className="py-3 px-4 text-center">
                        <span className="inline-flex items-center gap-1 font-semibold text-xs text-slate-700 bg-slate-100 px-2 py-0.5 rounded-full">
                          <KeyRound className="w-3 h-3 text-slate-400" />
                          <span>{qtdLicencas}</span>
                        </span>
                      </td>
                      <td className="py-3 px-4 text-right" onClick={(e) => e.stopPropagation()}>
                        <div className="flex items-center justify-end gap-1.5">
                          <button
                            onClick={() => onGerarLicencaParaCliente(cli)}
                            className="p-1.5 text-blue-600 hover:bg-blue-50 rounded-lg transition"
                            title="Gerar Licença para este cliente"
                          >
                            <KeyRound className="w-4 h-4" />
                          </button>
                          <button
                            onClick={() => onEditarCliente(cli)}
                            className="p-1.5 text-slate-600 hover:bg-slate-100 rounded-lg transition"
                            title="Editar cadastro"
                          >
                            <Edit2 className="w-4 h-4" />
                          </button>
                        </div>
                      </td>
                    </tr>
                  );
                })
              )}
            </tbody>
          </table>
        </div>
      </div>

      {/* Drawer / Modal de Detalhes do Cliente */}
      {clienteSelecionado && (
        <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-slate-900/60 backdrop-blur-xs animate-in fade-in duration-150">
          <div className="relative w-full max-w-xl bg-white rounded-xl shadow-xl border border-slate-200 overflow-hidden">
            <div className="px-6 py-4 border-b border-slate-100 flex items-center justify-between">
              <div>
                <h3 className="text-base font-bold text-slate-900">
                  {clienteSelecionado.nomeResponsavel}
                </h3>
                <p className="text-xs text-slate-400">{clienteSelecionado.empresa}</p>
              </div>
              <button
                onClick={() => setClienteSelecionado(null)}
                className="text-slate-400 hover:text-slate-600 p-1 rounded-lg"
              >
                ✕
              </button>
            </div>

            <div className="p-6 space-y-4 max-h-[70vh] overflow-y-auto text-xs">
              {/* Infos cadastrais */}
              <div className="grid grid-cols-2 gap-3 p-3 bg-slate-50 rounded-lg border border-slate-100">
                <div>
                  <span className="text-[10px] uppercase font-bold text-slate-400">E-mail</span>
                  <p className="font-medium text-slate-800">{clienteSelecionado.email}</p>
                </div>
                <div>
                  <span className="text-[10px] uppercase font-bold text-slate-400">Telefone</span>
                  <p className="font-medium text-slate-800">{clienteSelecionado.telefone || "—"}</p>
                </div>
                <div>
                  <span className="text-[10px] uppercase font-bold text-slate-400">Documento</span>
                  <p className="font-mono text-slate-800">{clienteSelecionado.documento || "—"}</p>
                </div>
                <div>
                  <span className="text-[10px] uppercase font-bold text-slate-400">Cadastrado Por</span>
                  <p className="text-slate-800">{clienteSelecionado.criadoPor || "Admin"}</p>
                </div>
              </div>

              {clienteSelecionado.observacoes && (
                <div>
                  <span className="text-[10px] uppercase font-bold text-slate-400">Observações</span>
                  <p className="p-3 bg-amber-50/50 border border-amber-100 rounded-lg text-slate-700 mt-1">
                    {clienteSelecionado.observacoes}
                  </p>
                </div>
              )}

              {/* Licenças vinculadas a este cliente */}
              <div>
                <div className="flex items-center justify-between mb-2">
                  <span className="text-xs font-bold text-slate-900">
                    Licenças Comerciais Vinculadas ({licencasDoCliente.length})
                  </span>
                  <button
                    onClick={() => {
                      const c = clienteSelecionado;
                      setClienteSelecionado(null);
                      onGerarLicencaParaCliente(c);
                    }}
                    className="text-xs font-semibold text-blue-600 hover:text-blue-700 flex items-center gap-1"
                  >
                    <Plus className="w-3.5 h-3.5" />
                    <span>Gerar Nova</span>
                  </button>
                </div>

                {licencasDoCliente.length === 0 ? (
                  <p className="text-center py-6 text-slate-400 border border-dashed border-slate-200 rounded-lg">
                    Nenhuma licença ativa ou gerada para este cliente.
                  </p>
                ) : (
                  <div className="space-y-2">
                    {licencasDoCliente.map((l) => (
                      <div
                        key={l.id}
                        className="p-3 rounded-lg border border-slate-200 bg-white flex items-center justify-between"
                      >
                        <div>
                          <p className="font-mono font-bold text-slate-900">{l.codigoFormatado}</p>
                          <p className="text-[11px] text-slate-500 mt-0.5">
                            Plano: <span className="capitalize">{l.plano}</span> • Vencimento:{" "}
                            {new Date(l.dataVencimento + "T00:00:00").toLocaleDateString("pt-BR")}
                          </p>
                        </div>
                        <div className="text-right">
                          <span className="text-xs font-semibold px-2 py-0.5 rounded-full bg-slate-100 text-slate-700">
                            {l.status}
                          </span>
                        </div>
                      </div>
                    ))}
                  </div>
                )}
              </div>
            </div>

            <div className="px-6 py-3 bg-slate-50 border-t border-slate-100 flex justify-end">
              <button
                onClick={() => setClienteSelecionado(null)}
                className="px-4 py-2 text-xs font-semibold text-slate-700 hover:bg-slate-200/70 rounded-lg transition"
              >
                Fechar
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
};
