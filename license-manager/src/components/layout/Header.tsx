import React from "react";
import { RefreshCw, ShieldCheck, ChevronRight, Menu, Plus } from "lucide-react";
import { TabId } from "./Sidebar";

interface HeaderProps {
  currentTab: TabId;
  onRefresh: () => void;
  isRefreshing?: boolean;
  onOpenMobileMenu?: () => void;
  onQuickNovaLicenca?: () => void;
}

export const Header: React.FC<HeaderProps> = ({
  currentTab,
  onRefresh,
  isRefreshing = false,
  onOpenMobileMenu,
  onQuickNovaLicenca,
}) => {
  const titles: Record<TabId, { title: string; subtitle: string }> = {
    dashboard: {
      title: "Painel Geral",
      subtitle: "Visão consolidada das licenças ativas, clientes e métricas operacionais",
    },
    licencas: {
      title: "Gerenciamento de Licenças",
      subtitle: "Geração de chaves, renovações, suspensões e controle de planos",
    },
    verificar: {
      title: "Verificar Chave",
      subtitle: "Consulta de validade e conexão de uma chave de licença",
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
    <header className="h-14 sm:h-16 px-4 md:px-8 bg-white border-b border-slate-200 flex items-center justify-between gap-2 shrink-0">
      <div className="flex items-center gap-2.5 min-w-0">
        {onOpenMobileMenu && (
          <button
            onClick={onOpenMobileMenu}
            className="md:hidden p-2 -ml-1 text-slate-600 hover:bg-slate-100 rounded-lg transition shrink-0"
            title="Abrir menu"
          >
            <Menu className="w-5 h-5" />
          </button>
        )}

        <div className="min-w-0">
          <div className="flex items-center gap-1.5 text-[10px] sm:text-[11px] font-medium text-slate-400 uppercase tracking-wider mb-0.5 truncate">
            <span>Nexus LS</span>
            <ChevronRight className="w-3 h-3 shrink-0" />
            <span className="text-slate-700 font-bold truncate">{current.title}</span>
          </div>
          <h2 className="hidden sm:block text-sm font-semibold text-slate-800 truncate">
            {current.subtitle}
          </h2>
        </div>
      </div>

      <div className="flex items-center gap-2 sm:gap-3 shrink-0">
        <div className="hidden lg:flex items-center gap-1.5 text-xs text-slate-500 bg-slate-50 border border-slate-200 px-3 py-1.5 rounded-lg">
          <ShieldCheck className="w-4 h-4 text-emerald-600" />
          <span>
            Projeto: <strong className="text-slate-700 font-medium">nexus-ls</strong>
          </span>
        </div>

        {onQuickNovaLicenca && (
          <button
            onClick={onQuickNovaLicenca}
            className="md:hidden flex items-center gap-1 px-2.5 py-1.5 text-xs font-semibold text-white bg-blue-600 hover:bg-blue-700 rounded-lg shadow-2xs transition"
          >
            <Plus className="w-3.5 h-3.5 stroke-[2.5]" />
            <span>Ativar</span>
          </button>
        )}

        <button
          onClick={onRefresh}
          disabled={isRefreshing}
          className="flex items-center gap-1.5 px-2.5 sm:px-3 py-1.5 text-xs font-medium text-slate-700 bg-white border border-slate-200 hover:bg-slate-50 hover:text-slate-900 rounded-lg shadow-2xs transition disabled:opacity-50"
          title="Atualizar dados do servidor"
        >
          <RefreshCw className={`w-3.5 h-3.5 ${isRefreshing ? "animate-spin text-blue-600" : ""}`} />
          <span className="hidden sm:inline">
            {isRefreshing ? "Atualizando..." : "Sincronizar"}
          </span>
        </button>
      </div>
    </header>
  );
};
