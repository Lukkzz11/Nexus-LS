import React, { useState } from "react";
import {
  ShieldCheck,
  Server,
  Key,
  Code2,
  Copy,
  Check,
  Database,
  Terminal,
  ExternalLink,
} from "lucide-react";
import { useAuth } from "../../contexts/AuthContext";
import { firebaseConfig } from "../../services/firebase";

export const SettingsView: React.FC = () => {
  const { user, isSuperAdmin } = useAuth();
  const [copiadoIdx, setCopiadoIdx] = useState<number | null>(null);

  const copyToClipboard = (text: string, idx: number) => {
    navigator.clipboard.writeText(text);
    setCopiadoIdx(idx);
    setTimeout(() => setCopiadoIdx(null), 2000);
  };

  const snippetCsharp = `// Atualizador Nexus LS - Validação via Cloud Function
var client = new HttpClient();
var payload = new
{
    chave = chaveDigitada,
    dispositivoId = LicencaService.CodigoDaMaquina(),
    nomeDispositivo = Environment.MachineName,
    sistema = "NexusLS"
};

var response = await client.PostAsJsonAsync(
    "https://southamerica-east1-nexus-ls.cloudfunctions.net/validateLicense",
    payload
);

var data = await response.Content.ReadFromJsonAsync<ResultadoNuvem>();
if (data.autorizado)
{
    // Permite execução e salva licença local
}`;

  return (
    <div className="space-y-6 max-w-5xl">
      {/* Overview Card */}
      <div className="bg-white rounded-xl p-6 border border-slate-200 shadow-2xs">
        <div className="flex items-start justify-between">
          <div>
            <h3 className="text-base font-bold text-slate-900">
              Ambiente Firebase & Arquitetura de Licenciamento
            </h3>
            <p className="text-xs text-slate-500 mt-1 max-w-2xl leading-relaxed">
              O <strong>Nexus LS License Manager</strong> opera centralizadamente no projeto{" "}
              <code className="text-blue-600 font-mono bg-blue-50 px-1 py-0.5 rounded font-bold">
                nexus-ls
              </code>
              . A segurança não depende apenas do frontend nem da Web API Key, mas de regras do
              Firestore e computação server-side nas Cloud Functions.
            </p>
          </div>
          <span className="inline-flex items-center gap-1.5 px-3 py-1 rounded-full text-xs font-semibold bg-emerald-50 text-emerald-700 border border-emerald-200">
            <span className="w-2 h-2 rounded-full bg-emerald-500" />
            Operacional
          </span>
        </div>

        <div className="grid grid-cols-1 sm:grid-cols-3 gap-4 mt-6 pt-6 border-t border-slate-100 text-xs">
          <div>
            <span className="text-[10px] uppercase font-bold text-slate-400">Projeto Firebase</span>
            <p className="font-mono font-bold text-slate-900 mt-0.5">{firebaseConfig.projectId}</p>
          </div>
          <div>
            <span className="text-[10px] uppercase font-bold text-slate-400">Região Cloud Functions</span>
            <p className="font-mono font-bold text-slate-900 mt-0.5">southamerica-east1 (São Paulo)</p>
          </div>
          <div>
            <span className="text-[10px] uppercase font-bold text-slate-400">Operador Atual</span>
            <p className="font-medium text-slate-800 mt-0.5">{user?.email || "Admin"}</p>
          </div>
        </div>
      </div>

      {/* Como Implantar as Funções e Regras */}
      <div className="bg-white rounded-xl p-6 border border-slate-200 shadow-2xs space-y-4">
        <h4 className="text-sm font-bold text-slate-900 flex items-center gap-2">
          <Terminal className="w-4 h-4 text-slate-600" />
          <span>Comandos de Implantação e Publicação no Firebase</span>
        </h4>
        <p className="text-xs text-slate-500 leading-relaxed">
          Para publicar as Cloud Functions e as regras de segurança no projeto <code>nexus-ls</code>, execute no terminal:
        </p>

        <div className="space-y-2">
          {[
            {
              title: "1. Publicar Regras de Segurança do Firestore",
              cmd: "firebase deploy --only firestore:rules",
            },
            {
              title: "2. Publicar Cloud Functions (validateLicense, renovações, auditoria)",
              cmd: "firebase deploy --only functions",
            },
            {
              title: "3. Publicar o Painel Administrativo no Firebase Hosting",
              cmd: "npm run build && firebase deploy --only hosting",
            },
          ].map((item, idx) => (
            <div
              key={idx}
              className="p-3 bg-slate-900 rounded-lg flex items-center justify-between gap-4 text-xs font-mono text-emerald-400"
            >
              <div>
                <span className="text-[10px] font-sans font-semibold text-slate-400 block mb-0.5">
                  {item.title}
                </span>
                <span>{item.cmd}</span>
              </div>
              <button
                onClick={() => copyToClipboard(item.cmd, idx)}
                className="p-1.5 text-slate-400 hover:text-white rounded bg-slate-800 transition shrink-0"
                title="Copiar comando"
              >
                {copiadoIdx === idx ? <Check className="w-3.5 h-3.5 text-emerald-400" /> : <Copy className="w-3.5 h-3.5" />}
              </button>
            </div>
          ))}
        </div>
      </div>

      {/* Integração com o Atualizador C# */}
      <div className="bg-white rounded-xl p-6 border border-slate-200 shadow-2xs space-y-4">
        <div className="flex items-center justify-between">
          <h4 className="text-sm font-bold text-slate-900 flex items-center gap-2">
            <Code2 className="w-4 h-4 text-blue-600" />
            <span>Código de Integração para o Atualizador em C# (NexusLS.Atualizador)</span>
          </h4>
          <button
            onClick={() => copyToClipboard(snippetCsharp, 99)}
            className="flex items-center gap-1.5 text-xs font-semibold text-blue-600 hover:text-blue-700"
          >
            {copiadoIdx === 99 ? <Check className="w-3.5 h-3.5" /> : <Copy className="w-3.5 h-3.5" />}
            <span>{copiadoIdx === 99 ? "Copiado!" : "Copiar Código"}</span>
          </button>
        </div>

        <p className="text-xs text-slate-500 leading-relaxed">
          Substitua a chamada direta no <code>LicencaNuvem.cs</code> pelo endpoint seguro de Cloud Function abaixo.
          Dessa forma, o Atualizador não precisa de permissão de escrita no Firestore:
        </p>

        <pre className="p-4 bg-slate-900 text-slate-200 rounded-lg text-[11px] font-mono overflow-x-auto leading-relaxed border border-slate-800">
          {snippetCsharp}
        </pre>
      </div>

      {/* Escalabilidade para a Família Nexus */}
      <div className="bg-slate-50 rounded-xl p-6 border border-slate-200 text-xs text-slate-600 space-y-2">
        <h5 className="font-bold text-slate-900 uppercase tracking-wider text-[11px]">
          Arquitetura Multi-Módulo (Família de Softwares Nexus)
        </h5>
        <p className="leading-relaxed">
          O schema do banco de dados e os endpoints já incluem o parâmetro <code>sistema</code> (ex: <code>"NexusLS"</code>, <code>"NexusERP"</code>, <code>"NexusPDV"</code>). Quando novos módulos forem lançados, eles poderão compartilhar esta mesma central de licenciamento passando seu identificador de sistema, aproveitando a mesma base de clientes, auditoria e controle de computadores autorizados.
        </p>
      </div>
    </div>
  );
};
