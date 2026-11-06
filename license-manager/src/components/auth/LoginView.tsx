import React, { useState } from "react";
import { KeyRound, Lock, Mail, ArrowRight, ShieldCheck, AlertCircle } from "lucide-react";
import { useAuth } from "../../contexts/AuthContext";

export const LoginView: React.FC = () => {
  const { login, error, clearError, resetPassword } = useAuth();
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [loading, setLoading] = useState(false);
  const [forgotMsg, setForgotMsg] = useState<string | null>(null);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!email || !password) return;

    setLoading(true);
    setForgotMsg(null);
    try {
      await login(email, password);
    } catch (err) {
      // Error is handled in AuthContext
    } finally {
      setLoading(false);
    }
  };

  const handleForgotPassword = async () => {
    if (!email || !email.includes("@")) {
      alert("Por favor, digite seu e-mail no campo acima para receber as instruções de redefinição.");
      return;
    }
    try {
      await resetPassword(email);
      setForgotMsg(`E-mail de recuperação enviado para ${email}. Verifique sua caixa de entrada.`);
    } catch (err: any) {
      alert(err.message || "Erro ao solicitar redefinição de senha.");
    }
  };

  return (
    <div className="min-h-screen bg-slate-100 flex flex-col justify-center items-center p-4">
      {/* Container */}
      <div className="w-full max-w-md bg-white rounded-2xl shadow-xl border border-slate-200/80 p-8 space-y-6">
        {/* Brand header */}
        <div className="text-center space-y-2">
          <div className="w-12 h-12 bg-blue-600 rounded-xl flex items-center justify-center text-white mx-auto shadow-sm">
            <KeyRound className="w-6 h-6 stroke-[2.2]" />
          </div>
          <h1 className="text-xl font-bold tracking-tight text-slate-900">
            NEXUS <span className="text-blue-600">LS</span>
          </h1>
          <p className="text-xs font-semibold uppercase tracking-wider text-slate-400">
            License Manager • Acesso Administrativo
          </p>
        </div>

        {/* Warning Badge: Private access */}
        <div className="p-3 bg-slate-50 border border-slate-200 rounded-xl flex items-center gap-2.5 text-xs text-slate-600">
          <ShieldCheck className="w-4 h-4 text-blue-600 shrink-0" />
          <span>Painel privado e restrito. Não há criação pública de contas.</span>
        </div>

        {/* Errors / Feedback */}
        {error && (
          <div className="p-3.5 bg-red-50 border border-red-200 rounded-xl flex items-start gap-2.5 text-xs font-medium text-red-800 animate-in fade-in duration-100">
            <AlertCircle className="w-4 h-4 text-red-600 shrink-0 mt-0.5" />
            <div className="flex-1">
              <span>{error}</span>
            </div>
          </div>
        )}

        {forgotMsg && (
          <div className="p-3.5 bg-emerald-50 border border-emerald-200 rounded-xl text-xs font-medium text-emerald-800">
            {forgotMsg}
          </div>
        )}

        {/* Form */}
        <form onSubmit={handleSubmit} className="space-y-4">
          <div>
            <label className="block text-xs font-semibold text-slate-700 mb-1.5">
              E-mail de Administrador
            </label>
            <div className="relative">
              <Mail className="w-4 h-4 text-slate-400 absolute left-3 top-1/2 -translate-y-1/2" />
              <input
                type="email"
                required
                autoComplete="email"
                value={email}
                onChange={(e) => {
                  setEmail(e.target.value);
                  if (error) clearError();
                }}
                placeholder="admin@nexus-ls.com"
                className="w-full text-xs pl-9 pr-3 py-2.5 bg-slate-50 border border-slate-200 rounded-xl focus:bg-white focus:outline-none focus:ring-2 focus:ring-blue-500/20 focus:border-blue-600 transition"
              />
            </div>
          </div>

          <div>
            <div className="flex items-center justify-between mb-1.5">
              <label className="text-xs font-semibold text-slate-700">Senha</label>
              <button
                type="button"
                onClick={handleForgotPassword}
                className="text-[11px] font-medium text-blue-600 hover:text-blue-700 transition"
              >
                Esqueceu a senha?
              </button>
            </div>
            <div className="relative">
              <Lock className="w-4 h-4 text-slate-400 absolute left-3 top-1/2 -translate-y-1/2" />
              <input
                type="password"
                required
                autoComplete="current-password"
                value={password}
                onChange={(e) => {
                  setPassword(e.target.value);
                  if (error) clearError();
                }}
                placeholder="••••••••••••"
                className="w-full text-xs pl-9 pr-3 py-2.5 bg-slate-50 border border-slate-200 rounded-xl focus:bg-white focus:outline-none focus:ring-2 focus:ring-blue-500/20 focus:border-blue-600 transition"
              />
            </div>
          </div>

          <div className="pt-2">
            <button
              type="submit"
              disabled={loading}
              className="w-full flex items-center justify-center gap-2 py-2.5 px-4 text-xs font-semibold text-white bg-blue-600 hover:bg-blue-700 rounded-xl shadow-xs transition disabled:opacity-50"
            >
              <span>{loading ? "Verificando Credenciais..." : "Entrar no Painel"}</span>
              <ArrowRight className="w-4 h-4" />
            </button>
          </div>
        </form>

        {/* Footer info */}
        <div className="pt-4 border-t border-slate-100 text-center">
          <p className="text-[11px] text-slate-400">
            Nexus LS Licensing Architecture • Firebase Project: <strong>nexus-ls</strong>
          </p>
        </div>
      </div>
    </div>
  );
};
