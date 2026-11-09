import React, { useState, useEffect, useCallback } from "react";
import {
  LayoutDashboard,
  KeyRound,
  Users,
  Monitor,
  Plus,
} from "lucide-react";
import { useAuth } from "./contexts/AuthContext";
import { LoginView } from "./components/auth/LoginView";
import { Sidebar, TabId } from "./components/layout/Sidebar";
import { Header } from "./components/layout/Header";
import { DashboardView } from "./components/dashboard/DashboardView";
import { LicensesView } from "./components/licenses/LicensesView";
import { ClientsView } from "./components/clients/ClientsView";
import { DevicesView } from "./components/devices/DevicesView";
import { AuditView } from "./components/audit/AuditView";
import { UpdaterSimulatorView } from "./components/simulator/UpdaterSimulatorView";
import { SettingsView } from "./components/config/SettingsView";

import { GenerateLicenseModal } from "./components/licenses/GenerateLicenseModal";
import { RenewLicenseModal } from "./components/licenses/RenewLicenseModal";
import { BlockLicenseModal } from "./components/licenses/BlockLicenseModal";
import { DeviceListModal } from "./components/licenses/DeviceListModal";
import { ClientModal } from "./components/clients/ClientModal";

import { Cliente, Licenca } from "./types";
import {
  listarClientes,
  listarLicencas,
  criarCliente,
  atualizarCliente,
  gerarNovaLicenca,
  renovarLicenca,
  bloquearLicenca,
  desbloquearLicenca,
  cancelarLicenca,
} from "./services/licenseService";

export function App() {
  const { user, loading: authLoading } = useAuth();
  const [currentTab, setCurrentTab] = useState<TabId>("dashboard");
  const [mobileMenuOpen, setMobileMenuOpen] = useState(false);

  // Estados de dados principais
  const [clientes, setClientes] = useState<Cliente[]>([]);
  const [licencas, setLicencas] = useState<Licenca[]>([]);
  const [carregandoDados, setCarregandoDados] = useState(false);

  // Estados dos Modais
  const [modalNovaLicenca, setModalNovaLicenca] = useState(false);
  const [clientePreselecionado, setClientePreselecionado] = useState<Cliente | null>(null);

  const [modalCliente, setModalCliente] = useState(false);
  const [clienteParaEditar, setClienteParaEditar] = useState<Cliente | null>(null);

  const [modalRenovar, setModalRenovar] = useState(false);
  const [licencaParaRenovar, setLicencaParaRenovar] = useState<Licenca | null>(null);

  const [modalBloquear, setModalBloquear] = useState(false);
  const [licencaParaBloquear, setLicencaParaBloquear] = useState<Licenca | null>(null);

  const [modalDispositivos, setModalDispositivos] = useState(false);
  const [licencaParaDispositivos, setLicencaParaDispositivos] = useState<Licenca | null>(null);

  // Carregar dados
  const carregarDados = useCallback(async () => {
    if (!user) return;
    setCarregandoDados(true);
    try {
      const [listaClientes, listaLicencas] = await Promise.all([
        listarClientes().catch((e) => {
          console.warn("Erro ao buscar clientes:", e);
          return [];
        }),
        listarLicencas().catch((e) => {
          console.warn("Erro ao buscar licenças:", e);
          return [];
        }),
      ]);
      setClientes(listaClientes);
      setLicencas(listaLicencas);
    } catch (err) {
      console.error("Erro na sincronização de dados:", err);
    } finally {
      setCarregandoDados(false);
    }
  }, [user]);

  useEffect(() => {
    if (user) {
      carregarDados();
    }
  }, [user, carregarDados]);

  // Se estiver carregando auth
  if (authLoading) {
    return (
      <div className="min-h-screen bg-slate-50 flex items-center justify-center">
        <div className="text-center space-y-3">
          <div className="w-8 h-8 border-3 border-blue-600 border-t-transparent rounded-full animate-spin mx-auto" />
          <p className="text-xs font-semibold text-slate-500 uppercase tracking-wider">
            Iniciando Nexus LS License Manager...
          </p>
        </div>
      </div>
    );
  }

  // Se não autenticado como administrador, exibe tela de login privada
  if (!user) {
    return <LoginView />;
  }

  // Ações de Licenças
  const handleAbrirNovaLicenca = (cliente?: Cliente) => {
    setClientePreselecionado(cliente || null);
    setModalNovaLicenca(true);
  };

  const handleGerarLicencaSubmit = async (params: any) => {
    await gerarNovaLicenca(params);
    await carregarDados();
  };

  const handleAbrirRenovar = (lic: Licenca) => {
    setLicencaParaRenovar(lic);
    setModalRenovar(true);
  };

  const handleRenovarSubmit = async (licencaId: string, novaData: string, dias?: number) => {
    await renovarLicenca(licencaId, novaData, dias);
    await carregarDados();
  };

  const handleAbrirBloquear = (lic: Licenca) => {
    setLicencaParaBloquear(lic);
    setModalBloquear(true);
  };

  const handleBloquearSubmit = async (licencaId: string, motivo: string) => {
    await bloquearLicenca(licencaId, motivo);
    await carregarDados();
  };

  const handleDesbloquear = async (licencaId: string) => {
    await desbloquearLicenca(licencaId);
    await carregarDados();
  };

  const handleCancelar = async (licencaId: string) => {
    await cancelarLicenca(licencaId);
    await carregarDados();
  };

  const handleAbrirDispositivos = (lic: Licenca) => {
    setLicencaParaDispositivos(lic);
    setModalDispositivos(true);
  };

  // Ações de Clientes
  const handleAbrirNovoCliente = () => {
    setClienteParaEditar(null);
    setModalCliente(true);
  };

  const handleAbrirEditarCliente = (cli: Cliente) => {
    setClienteParaEditar(cli);
    setModalCliente(true);
  };

  const handleSalvarClienteSubmit = async (dados: any) => {
    if (clienteParaEditar) {
      await atualizarCliente(clienteParaEditar.id, dados);
    } else {
      await criarCliente(dados);
    }
    await carregarDados();
  };

  // Métricas para badges da sidebar
  const counts = {
    licencasAtivas: licencas.filter((l) => l.status === "ATIVA").length,
    clientes: clientes.length,
    dispositivos: licencas.reduce((acc, l) => acc + (l.dispositivosAtivosCount || 0), 0),
    vencendo: licencas.filter((l) => l.status === "VENCENDO").length,
  };

  return (
    <div className="flex h-screen bg-slate-50 overflow-hidden text-slate-800">
      {/* Sidebar de Navegação (Desktop + Drawer Mobile) */}
      <Sidebar
        currentTab={currentTab}
        setCurrentTab={setCurrentTab}
        mobileOpen={mobileMenuOpen}
        onCloseMobile={() => setMobileMenuOpen(false)}
        counts={counts}
      />

      {/* Área Principal de Conteúdo */}
      <div className="flex-1 flex flex-col min-w-0 overflow-hidden">
        <Header
          currentTab={currentTab}
          onRefresh={carregarDados}
          isRefreshing={carregandoDados}
          onOpenMobileMenu={() => setMobileMenuOpen(true)}
          onQuickNovaLicenca={() => handleAbrirNovaLicenca()}
        />

        <main className="flex-1 overflow-y-auto p-3.5 sm:p-6 md:p-8 pb-24 md:pb-8">
          {currentTab === "dashboard" && (
            <DashboardView
              clientes={clientes}
              licencas={licencas}
              onNavigate={setCurrentTab}
              onOpenNovaLicenca={() => handleAbrirNovaLicenca()}
              onOpenNovoCliente={handleAbrirNovoCliente}
            />
          )}

          {currentTab === "licencas" && (
            <LicensesView
              licencas={licencas}
              onOpenNovaLicenca={() => handleAbrirNovaLicenca()}
              onRenovar={handleAbrirRenovar}
              onBloquear={handleAbrirBloquear}
              onDesbloquear={handleDesbloquear}
              onCancelar={handleCancelar}
              onVerDispositivos={handleAbrirDispositivos}
            />
          )}

          {currentTab === "clientes" && (
            <ClientsView
              clientes={clientes}
              licencas={licencas}
              onOpenNovoCliente={handleAbrirNovoCliente}
              onEditarCliente={handleAbrirEditarCliente}
              onGerarLicencaParaCliente={(cli) => handleAbrirNovaLicenca(cli)}
            />
          )}

          {currentTab === "dispositivos" && (
            <DevicesView
              licencas={licencas}
              onRefreshLicencas={carregarDados}
            />
          )}

          {currentTab === "auditoria" && <AuditView />}

          {currentTab === "simulador" && (
            <UpdaterSimulatorView
              licencas={licencas}
              onRefreshLicencas={carregarDados}
            />
          )}

          {currentTab === "configuracoes" && <SettingsView />}
        </main>

        {/* Barra de Navegação Inferior Exclusiva para Celular (< md) */}
        <nav className="md:hidden fixed bottom-0 inset-x-0 z-40 bg-white border-t border-slate-200 px-2 py-1.5 flex items-center justify-around shadow-lg">
          <button
            onClick={() => setCurrentTab("dashboard")}
            className={`flex flex-col items-center gap-0.5 px-2 py-1 rounded-lg text-[10px] font-semibold transition ${
              currentTab === "dashboard" ? "text-blue-600" : "text-slate-500"
            }`}
          >
            <LayoutDashboard className="w-4 h-4" />
            <span>Painel</span>
          </button>

          <button
            onClick={() => setCurrentTab("licencas")}
            className={`flex flex-col items-center gap-0.5 px-2 py-1 rounded-lg text-[10px] font-semibold transition ${
              currentTab === "licencas" ? "text-blue-600" : "text-slate-500"
            }`}
          >
            <KeyRound className="w-4 h-4" />
            <span>Licenças</span>
          </button>

          <button
            onClick={() => handleAbrirNovaLicenca()}
            className="flex flex-col items-center justify-center -mt-4 bg-blue-600 text-white w-11 h-11 rounded-full shadow-md active:scale-95 transition"
            title="Gerar ou Ativar Licença"
          >
            <Plus className="w-5 h-5 stroke-[2.5]" />
          </button>

          <button
            onClick={() => setCurrentTab("clientes")}
            className={`flex flex-col items-center gap-0.5 px-2 py-1 rounded-lg text-[10px] font-semibold transition ${
              currentTab === "clientes" ? "text-blue-600" : "text-slate-500"
            }`}
          >
            <Users className="w-4 h-4" />
            <span>Clientes</span>
          </button>

          <button
            onClick={() => setCurrentTab("dispositivos")}
            className={`flex flex-col items-center gap-0.5 px-2 py-1 rounded-lg text-[10px] font-semibold transition ${
              currentTab === "dispositivos" ? "text-blue-600" : "text-slate-500"
            }`}
          >
            <Monitor className="w-4 h-4" />
            <span>Máquinas</span>
          </button>
        </nav>
      </div>

      {/* Modais Globais */}
      <GenerateLicenseModal
        isOpen={modalNovaLicenca}
        onClose={() => setModalNovaLicenca(false)}
        clientes={clientes}
        clientePreselecionado={clientePreselecionado}
        onGenerate={handleGerarLicencaSubmit}
      />

      <ClientModal
        isOpen={modalCliente}
        onClose={() => setModalCliente(false)}
        clienteParaEditar={clienteParaEditar}
        onSave={handleSalvarClienteSubmit}
      />

      <RenewLicenseModal
        isOpen={modalRenovar}
        onClose={() => setModalRenovar(false)}
        licenca={licencaParaRenovar}
        onRenew={handleRenovarSubmit}
      />

      <BlockLicenseModal
        isOpen={modalBloquear}
        onClose={() => setModalBloquear(false)}
        licenca={licencaParaBloquear}
        onBlock={handleBloquearSubmit}
      />

      <DeviceListModal
        isOpen={modalDispositivos}
        onClose={() => setModalDispositivos(false)}
        licenca={licencaParaDispositivos}
        onDevicesUpdated={carregarDados}
      />
    </div>
  );
}

export default App;
