import React, { useState, useEffect } from "react";
import { Monitor, Search, Trash2, KeyRound, Building, Clock, CheckCircle } from "lucide-react";
import { DispositivoVinculado, Licenca } from "../../types";
import {
  listarTodosDispositivos,
  desvincularDispositivo,
} from "../../services/licenseService";

interface DevicesViewProps {
  licencas: Licenca[];
  onRefreshLicencas: () => void;
}

export const DevicesView: React.FC<DevicesViewProps> = ({ licencas, onRefreshLicencas }) => {
  const [dispositivos, setDispositivos] = useState<DispositivoVinculado[]>([]);
  const [carregando, setCarregando] = useState(true);
  const [busca, setBusca] = useState("");
  const [removendoId, setRemovendoId] = useState<string | null>(null);

  const carregar = async () => {
    setCarregando(true);
    try {
      const lista = await listarTodosDispositivos();
      setDispositivos(lista);
    } catch (err) {
      console.error("Erro ao listar todos dispositivos:", err);
    } finally {
      setCarregando(false);
    }
  };

  useEffect(() => {
    carregar();
  }, []);

  const licMap = new Map(licencas.map((l) => [l.id, l]));

  const filtrados = dispositivos.filter((d) => {
    const lic = licMap.get(d.licencaId);
    const termo = busca.toLowerCase();
    return (
      d.nomeDispositivo.toLowerCase().includes(termo) ||
      d.identificadorDispositivo.toLowerCase().includes(termo) ||
      (lic && lic.clienteNome.toLowerCase().includes(termo)) ||
      (lic && lic.empresa.toLowerCase().includes(termo)) ||
      (lic && lic.codigoFormatado.toLowerCase().includes(termo))
    );
  });

  const handleRemover = async (disp: DispositivoVinculado) => {
    if (!window.confirm(`Desvincular o computador "${disp.nomeDispositivo}" (${disp.identificadorDispositivo})? Esta ação liberará a vaga para outro equipamento.`)) {
      return;
    }

    setRemovendoId(disp.id);
    try {
      await desvincularDispositivo(disp.id, disp.licencaId);
      await carregar();
      onRefreshLicencas();
    } catch (err) {
      alert("Falha ao desvincular computador.");
    } finally {
      setRemovendoId(null);
    }
  };

  return (
    <div className="space-y-6">
      {/* Top Filter Bar */}
      <div className="bg-white rounded-xl p-4 border border-slate-200 shadow-2xs flex flex-col sm:flex-row sm:items-center justify-between gap-4">
        <div className="relative flex-1 max-w-md">
          <Search className="w-4 h-4 text-slate-400 absolute left-3 top-1/2 -translate-y-1/2" />
          <input
            type="text"
            value={busca}
            onChange={(e) => setBusca(e.target.value)}
            placeholder="Buscar por computador, identificador, cliente ou licença..."
            className="w-full text-xs pl-9 pr-4 py-2 border border-slate-200 rounded-lg focus:outline-none focus:ring-2 focus:ring-blue-500/20 focus:border-blue-600 transition"
          />
        </div>

        <div className="flex items-center gap-3">
          <span className="text-xs text-slate-500">
            Total Conectados: <strong>{filtrados.length}</strong>
          </span>
          <button
            onClick={carregar}
            className="px-3 py-2 text-xs font-semibold text-slate-700 bg-slate-50 hover:bg-slate-100 border border-slate-200 rounded-lg transition"
          >
            Atualizar Lista
          </button>
        </div>
      </div>

      {/* Main Table */}
      <div className="bg-white rounded-xl border border-slate-200 shadow-2xs overflow-hidden">
        <div className="overflow-x-auto">
          <table className="w-full text-left border-collapse text-xs">
            <thead>
              <tr className="bg-slate-50/75 border-b border-slate-100 text-slate-500 font-semibold uppercase text-[10px] tracking-wider">
                <th className="py-3 px-4">Computador / Sistema</th>
                <th className="py-3 px-4">Identificador da Máquina</th>
                <th className="py-3 px-4">Licença Vinculada</th>
                <th className="py-3 px-4">Cliente / Loja</th>
                <th className="py-3 px-4">Primeira Ativação</th>
                <th className="py-3 px-4">Última Checagem</th>
                <th className="py-3 px-4 text-right">Ação</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100 text-slate-700">
              {carregando ? (
                <tr>
                  <td colSpan={7} className="py-12 text-center text-slate-400">
                    Carregando dispositivos vinculados...
                  </td>
                </tr>
              ) : filtrados.length === 0 ? (
                <tr>
                  <td colSpan={7} className="py-12 text-center text-slate-400">
                    Nenhum computador registrado ainda.
                  </td>
                </tr>
              ) : (
                filtrados.map((disp) => {
                  const lic = licMap.get(disp.licencaId);
                  const data1 = disp.primeiraAtivacao?.toDate
                    ? disp.primeiraAtivacao.toDate().toLocaleDateString("pt-BR")
                    : "—";
                  const data2 = disp.ultimaVerificacao?.toDate
                    ? disp.ultimaVerificacao.toDate().toLocaleString("pt-BR")
                    : "—";

                  return (
                    <tr key={disp.id} className="hover:bg-slate-50/60 transition duration-75">
                      <td className="py-3 px-4">
                        <div className="flex items-center gap-2">
                          <Monitor className="w-4 h-4 text-slate-500" />
                          <span className="font-semibold text-slate-900">{disp.nomeDispositivo}</span>
                        </div>
                        <span className="text-[10px] text-slate-400 ml-6">
                          {disp.sistema || "NexusLS"}
                        </span>
                      </td>
                      <td className="py-3 px-4 font-mono text-[11px] text-slate-700">
                        <span className="bg-slate-100 px-2 py-0.5 rounded">
                          {disp.identificadorDispositivo}
                        </span>
                      </td>
                      <td className="py-3 px-4 font-mono font-medium text-slate-800">
                        {lic?.codigoFormatado || disp.licencaId}
                      </td>
                      <td className="py-3 px-4">
                        <p className="font-medium text-slate-900">{lic?.clienteNome || "—"}</p>
                        <p className="text-[11px] text-slate-400">{lic?.empresa || "—"}</p>
                      </td>
                      <td className="py-3 px-4 text-slate-500">{data1}</td>
                      <td className="py-3 px-4 text-slate-500">{data2}</td>
                      <td className="py-3 px-4 text-right">
                        <button
                          onClick={() => handleRemover(disp)}
                          disabled={removendoId === disp.id}
                          className="px-2.5 py-1.5 text-xs font-semibold text-red-600 hover:bg-red-50 rounded-lg border border-red-200 transition disabled:opacity-50"
                          title="Liberar máquina"
                        >
                          Liberar Vaga
                        </button>
                      </td>
                    </tr>
                  );
                })
              )}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
};
