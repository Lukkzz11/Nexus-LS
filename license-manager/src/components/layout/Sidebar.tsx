import React from "react";
import {
  LayoutDashboard,
  KeyRound,
  Users,
  Monitor,
  History,
  Terminal,
  Shield,
  LogOut,
  ChevronRight,
} from "lucide-react";
import { useAuth } from "../../contexts/AuthContext";

export type TabId =
  | "dashboard"
  | "licencas"
  | "clientes"
  | "dispositivos"
  | "auditoria"
  | "simulador"
  | "configuracoes";

interface SidebarProps {
  currentTab: TabId;
  setCurrentTab: (tab: TabId) => void;
  counts?: {
    licencasAtivas: number;
    clientes: number;
    dispositivos: number;
    vencendo: number;
  };
}

export const Sidebar: React.FC<SidebarProps> = ({ currentTab, setCurrentTab, counts }) => {
  const { user, logout, isSuperAdmin } = useAuth();

  const menuItems: Array<{
    id: TabId;
    label: string;
    icon: React.ComponentType<{ className?: string }>;
    badge?: number;
    badgeColor?: string;
  }> = [
    {
      id: "dashboard",
      label: "Painel Principal",
      icon: LayoutDashboard,
    },
    {
      id: "licencas",
      label: "Licenças",
      icon: KeyRound,
      badge: counts?.vencendo ? counts.vencendo : counts?.licencasAtivas,
      badgeColor: counts?.vencendo ? "bg-amber-100 text-amber-800" : "bg-slate-100 text-slate-700",
    },
    {
      id: "clientes",
      label: "Clientes & Lojas",
      icon: Users,
      badge: counts?.clientes,
    },
    {
      id: "dispositivos",
      label: "Dispositivos",
      icon: Monitor,
      badge: counts?.dispositivos,
    },
    {
      id: "auditoria",
      label: "Histórico & Auditoria",
      icon: History,
    },
    {
      id: "simulador",
      label: "Teste Atualizador",
      icon: Terminal,
    },
    {
      id: "configuracoes",
      label: "Configurações",
      icon: Shield,
    },
  ];

  return (
    <aside className="w-64 bg-white border-r border-slate-200 flex flex-col justify-between select-none">
      {/* Top Branding */}
      <div>
        <div className="h-16 px-6 flex items-center gap-3 border-b border-slate-200">
          <div className="w-8 h-8 rounded-lg bg-blue-600 flex items-center justify-center text-white shadow-xs">
            <KeyRound className="w-4 h-4 stroke-[2.2]" />
          </div>
          <div>
            <h1 className="text-sm font-bold tracking-tight text-slate-900 leading-none">
              NEXUS <span className="text-blue-600">LS</span>
            </h1>
            <p className="text-[10px] font-semibold text-slate-400 uppercase tracking-wider mt-1">
              License Manager
            </p>
          </div>
        </div>

        {/* Navigation Items */}
        <nav className="p-3 space-y-1">
          {menuItems.map((item) => {
            const Icon = item.icon;
            const active = currentTab === item.id;
            return (
              <button
                key={item.id}
                onClick={() => setCurrentTab(item.id)}
                className={`w-full flex items-center justify-between px-3 py-2.5 rounded-lg text-sm font-medium transition duration-150 ${
                  active
                    ? "bg-blue-50 text-blue-700"
                    : "text-slate-600 hover:bg-slate-50 hover:text-slate-900"
                }`}
              >
                <div className="flex items-center gap-3">
                  <Icon
                    className={`w-4 h-4 ${
                      active ? "text-blue-600" : "text-slate-400 group-hover:text-slate-600"
                    }`}
                  />
                  <span>{item.label}</span>
                </div>
                {item.badge !== undefined && item.badge > 0 && (
                  <span
                    className={`text-[11px] font-semibold px-2 py-0.5 rounded-full ${
                      item.badgeColor || "bg-slate-100 text-slate-600"
                    }`}
                  >
                    {item.badge}
                  </span>
                )}
              </button>
            );
          })}
        </nav>
      </div>

      {/* Footer Admin info & Logout */}
      <div className="p-3 border-t border-slate-200 bg-slate-50/50">
        <div className="px-3 py-2 rounded-lg bg-white border border-slate-200 shadow-xs mb-2">
          <div className="flex items-center justify-between">
            <span className="text-[10px] uppercase font-bold tracking-wider text-slate-400">
              Operador
            </span>
            <span className="inline-flex items-center gap-1 text-[10px] font-semibold text-emerald-600 bg-emerald-50 px-1.5 py-0.5 rounded">
              <span className="w-1.5 h-1.5 rounded-full bg-emerald-500" />
              {isSuperAdmin ? "Superadmin" : "Admin"}
            </span>
          </div>
          <p className="text-xs font-semibold text-slate-800 truncate mt-1" title={user?.email || ""}>
            {user?.email || "Administrador"}
          </p>
        </div>

        <button
          onClick={logout}
          className="w-full flex items-center justify-center gap-2 px-3 py-2 text-xs font-medium text-slate-600 hover:text-red-600 hover:bg-red-50 rounded-lg transition"
        >
          <LogOut className="w-3.5 h-3.5" />
          <span>Encerrar Sessão</span>
        </button>
      </div>
    </aside>
  );
};
