import React from "react";
import { RefreshCw, ShieldCheck, ChevronRight } from "lucide-react";
import { TabId } from "./Sidebar";

interface HeaderProps {
  currentTab: TabId;
  onRefresh: () => void;
  isRefreshing?: boolean;
}

export const Header: React.FC<HeaderProps> = ({ currentTab, onRefresh, isRefreshing = false }) => {
  const titles: Record<TabId, { title: string; subtitle: string }> = {
    dashboard: {
      title: "Painel Geral",
      subtitle: "Visão consolidada das licenças ativas, clientes e métricas operacionais",
    },
    licencas: {
      title: "Gerenciamento de Licenças",
      subtitle: "Geração de chaves, renovações, suspensões e controle de planos",
    },
    clientes: {
      title: "Clientes & Empresas",
      subtitle: "Base comercial de contratantes autorizados do sistema Nexus LS",
    },
    dispositivos: {
      title: "Dispositivos Conectados",
      subtitle: "Computadores autorizados e vinculados a cada licença comercial",
    },
    auditoria: {
      title: "Histórico & Auditoria",
      subtitle: "Registro imutável de todas as ações administrativas e ativações",
    },
    simulador: {
      title: "Simulador do Atualizador",
      subtitle: "Ferramenta de teste do endpoint de validação consumido pelo NexusLS",
    },
    configuracoes: {
      title: "Configurações do Sistema",
      subtitle: "Regras de segurança, permissões e status dos serviços Firebase",
    },
  };

  const current = titles[currentTab] || titles.dashboard;

  return (
    <header className="h-16 px-8 bg-white border-b border-slate-200 flex items-center justify-between">
      <div>
        <div className="flex items-center gap-1.5 text-[11px] font-medium text-slate-400 uppercase tracking-wider mb-0.5">
          <span>Nexus LS</span>
          <ChevronRight className="w-3 h-3" />
          <span className="text-slate-600 font-semibold">{current.title}</span>
        </div>
        <h2 className="text-sm font-semibold text-slate-800">{current.subtitle}</h2>
      </div>

      <div className="flex items-center gap-3">
        <div className="hidden sm:flex items-center gap-1.5 text-xs text-slate-500 bg-slate-50 border border-slate-200 px-3 py-1.5 rounded-lg">
          <ShieldCheck className="w-4 h-4 text-emerald-600" />
          <span>Projeto: <strong className="text-slate-700 font-medium">nexus-ls</strong></span>
        </div>

        <button
          onClick={onRefresh}
          disabled={isRefreshing}
          className="flex items-center gap-1.5 px-3 py-1.5 text-xs font-medium text-slate-700 bg-white border border-slate-200 hover:bg-slate-50 hover:text-slate-900 rounded-lg shadow-2xs transition disabled:opacity-50"
          title="Atualizar dados do servidor"
        >
          <RefreshCw className={`w-3.5 h-3.5 ${isRefreshing ? "animate-spin text-blue-600" : ""}`} />
          <span>{isRefreshing ? "Atualizando..." : "Sincronizar"}</span>
        </button>
      </div>
    </header>
  );
};
