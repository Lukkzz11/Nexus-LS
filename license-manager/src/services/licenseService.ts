import {
  collection,
  doc,
  getDocs,
  getDoc,
  setDoc,
  updateDoc,
  deleteDoc,
  query,
  where,
  orderBy,
  limit,
  serverTimestamp,
  increment,
} from "firebase/firestore";
import { httpsCallable } from "firebase/functions";
import { db, functions, auth } from "./firebase";
import {
  Cliente,
  Licenca,
  DispositivoVinculado,
  HistoricoAuditoria,
  PlanoLicenca,
  StatusLicenca,
  RespostaValidacaoAtualizador,
} from "../types";

// Utilitários de Hash e Geração de Chave
const CHAVE_ALFABETO = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

/** Firestore recusa campos undefined: tira-os antes de gravar. */
function semIndefinidos<T extends Record<string, any>>(o: T): T {
  return Object.fromEntries(Object.entries(o).filter(([, v]) => v !== undefined)) as T;
}

export function normalizarChave(chave: string): string {
  return chave.trim().toUpperCase().replace(/[^A-Z0-9-]/g, "");
}

export async function calcularHashChave(chave: string): Promise<string> {
  const norm = normalizarChave(chave);
  const encoder = new TextEncoder();
  const data = encoder.encode(norm);
  const hashBuffer = await crypto.subtle.digest("SHA-256", data);
  const hashArray = Array.from(new Uint8Array(hashBuffer));
  return hashArray.map((b) => b.toString(16).padStart(2, "0")).join("");
}

export function gerarChaveNexus(): string {
  const segmento = (len: number) => {
    let s = "";
    const array = new Uint8Array(len);
    crypto.getRandomValues(array);
    for (let i = 0; i < len; i++) {
      s += CHAVE_ALFABETO[array[i] % CHAVE_ALFABETO.length];
    }
    return s;
  };
  return `NEXUS-${segmento(4)}-${segmento(4)}-${segmento(4)}`;
}

export function calcularDataVencimentoPorPlano(plano: PlanoLicenca, dataInicioStr?: string): string {
  const base = dataInicioStr ? new Date(dataInicioStr + "T00:00:00") : new Date();
  const dias =
    plano === "mensal"
      ? 30
      : plano === "trimestral"
      ? 90
      : plano === "semestral"
      ? 180
      : plano === "anual"
      ? 365
      : 30;

  base.setDate(base.getDate() + dias);
  return base.toISOString().split("T")[0];
}

export function extrairMaquinasCampo(campoMaquina?: string | null): string[] {
  if (!campoMaquina) return [];
  const limpo = campoMaquina.trim();
  if (!limpo || limpo === "*") return [];
  return Array.from(
    new Set(
      limpo
        .split(",")
        .map((m) => m.trim().toUpperCase())
        .filter((m) => m.length > 0)
    )
  );
}

export function calcularStatusReal(lic: Partial<Licenca>): { status: StatusLicenca; diasRestantes: number } {
  const agora = new Date();
  const agoraZero = new Date(agora.getFullYear(), agora.getMonth(), agora.getDate());

  let vencDate = lic.dataVencimento ? new Date(lic.dataVencimento + "T00:00:00") : null;
  if (!vencDate && lic.validade) {
    vencDate = new Date(lic.validade + "T00:00:00");
  }

  const vencZero = vencDate
    ? new Date(vencDate.getFullYear(), vencDate.getMonth(), vencDate.getDate())
    : agoraZero;

  const diffMs = vencZero.getTime() - agoraZero.getTime();
  const diasRestantes = Math.round(diffMs / (1000 * 60 * 60 * 24));

  if (lic.status === "CANCELADA") {
    return { status: "CANCELADA", diasRestantes };
  }

  if (lic.status === "BLOQUEADA" || lic.ativa === false) {
    return { status: "BLOQUEADA", diasRestantes };
  }

  if (diasRestantes < 0) {
    return { status: "VENCIDA", diasRestantes };
  }

  const temDispositivoVinculado =
    extrairMaquinasCampo(lic.maquina).length > 0 || (lic.dispositivosAtivosCount || 0) > 0;

  if (lic.status === "PENDENTE" && !temDispositivoVinculado) {
    return { status: "PENDENTE", diasRestantes };
  }

  if (diasRestantes <= 15) {
    return { status: "VENCENDO", diasRestantes };
  }

  return { status: "ATIVA", diasRestantes };
}

// ----------------------------------------------------------------------------
// SERVIÇOS DE CLIENTES
// ----------------------------------------------------------------------------
export async function listarClientes(): Promise<Cliente[]> {
  try {
    const q = query(collection(db, "clientes"), orderBy("nomeResponsavel", "asc"));
    const snapshot = await getDocs(q);
    return snapshot.docs.map((docSnap) => ({
      id: docSnap.id,
      ...docSnap.data(),
    })) as Cliente[];
  } catch (error) {
    console.error("Erro ao listar clientes:", error);
    throw error;
  }
}

export async function criarCliente(dados: Omit<Cliente, "id" | "totalLicencas" | "criadoEm">): Promise<string> {
  const id = `CLI_${Date.now()}_${Math.random().toString(36).substring(2, 6).toUpperCase()}`;
  const currentUser = auth.currentUser?.email || "Admin";

  const novoCliente = {
    ...dados,
    id,
    totalLicencas: 0,
    criadoEm: serverTimestamp(),
    atualizadoEm: serverTimestamp(),
    criadoPor: currentUser,
  };

  await setDoc(doc(db, "clientes", id), semIndefinidos(novoCliente));

  await registrarAuditoriaLocal({
    tipoAcao: "CRIACAO_CLIENTE",
    clienteId: id,
    clienteNome: dados.nomeResponsavel,
    responsavel: currentUser,
    detalhes: { empresa: dados.empresa, email: dados.email },
  });

  return id;
}

export async function atualizarCliente(id: string, dados: Partial<Cliente>): Promise<void> {
  const currentUser = auth.currentUser?.email || "Admin";
  await updateDoc(doc(db, "clientes", id), semIndefinidos({
    ...dados,
    atualizadoEm: serverTimestamp(),
  }));

  await registrarAuditoriaLocal({
    tipoAcao: "EDICAO_CLIENTE",
    clienteId: id,
    clienteNome: dados.nomeResponsavel || "Cliente",
    responsavel: currentUser,
    detalhes: { camposAlterados: Object.keys(dados) },
  });
}

// ----------------------------------------------------------------------------
// SERVIÇOS DE LICENÇAS
// ----------------------------------------------------------------------------
export async function listarLicencas(): Promise<Licenca[]> {
  try {
    const snapshot = await getDocs(collection(db, "licencas"));
    const licencas: Licenca[] = [];

    // Busca contagem de dispositivos por licença
    const dispSnapshot = await getDocs(query(collection(db, "dispositivos"), where("ativo", "==", true)));
    const dispSetMap: Record<string, Set<string>> = {};
    dispSnapshot.docs.forEach((d) => {
      const data = d.data();
      if (data.licencaId && data.identificadorDispositivo) {
        if (!dispSetMap[data.licencaId]) dispSetMap[data.licencaId] = new Set();
        dispSetMap[data.licencaId].add(String(data.identificadorDispositivo).trim().toUpperCase());
      }
    });

    for (const docSnap of snapshot.docs) {
      const data = docSnap.data();
      const maquinasCampo = extrairMaquinasCampo(data.maquina);
      const setTotal = new Set<string>(dispSetMap[docSnap.id] ? Array.from(dispSetMap[docSnap.id]) : []);
      maquinasCampo.forEach((m) => setTotal.add(m));

      const lic: Licenca = {
        id: docSnap.id,
        hashChave: data.hashChave || "",
        codigoFormatado: data.codigoFormatado || docSnap.id,
        clienteId: data.clienteId || "",
        clienteNome: data.clienteNome || data.cliente || "Cliente",
        empresa: data.empresa || "",
        plano: data.plano || "anual",
        dataInicio: data.dataInicio || (data.criadoEm?.toDate ? data.criadoEm.toDate().toISOString().split("T")[0] : ""),
        dataVencimento: data.dataVencimento || data.validade || "",
        limiteDispositivos: Number(data.limiteDispositivos || 1),
        status: data.status || (data.ativa === false ? "BLOQUEADA" : "ATIVA"),
        motivoBloqueio: data.motivoBloqueio || null,
        bloqueadoEm: data.bloqueadoEm || null,
        bloqueadoPor: data.bloqueadoPor || null,
        canceladoEm: data.canceladoEm || null,
        canceladoPor: data.canceladoPor || null,
        motivoCancelamento: data.motivoCancelamento || null,
        observacoes: data.observacoes || "",
        criadoEm: data.criadoEm,
        atualizadoEm: data.atualizadoEm,
        criadoPor: data.criadoPor || "Admin",
        cliente: data.cliente,
        validade: data.validade,
        ativa: data.ativa !== false,
        maquina: data.maquina || "",
        dispositivosAtivosCount: setTotal.size,
      };

      const { status: statusReal, diasRestantes } = calcularStatusReal(lic);
      lic.status = statusReal;
      lic.diasRestantes = diasRestantes;

      licencas.push(lic);
    }

    return licencas.sort((a, b) => {
      const tA = a.criadoEm?.toDate ? a.criadoEm.toDate().getTime() : 0;
      const tB = b.criadoEm?.toDate ? b.criadoEm.toDate().getTime() : 0;
      return tB - tA;
    });
  } catch (error) {
    console.error("Erro ao listar licenças:", error);
    throw error;
  }
}

export async function gerarNovaLicenca(params: {
  clienteId: string;
  clienteNome: string;
  empresa: string;
  plano: PlanoLicenca;
  dataInicio: string;
  dataVencimento: string;
  limiteDispositivos: number;
  observacoes?: string;
  chavePersonalizada?: string;
  hwidInicial?: string;
}): Promise<{ id: string; chave: string }> {
  const currentUser = auth.currentUser?.email || "Admin";
  const hwidLimpo = params.hwidInicial ? params.hwidInicial.trim().toUpperCase() : "";

  // Tenta via Cloud Function administrativa (se não houver HWID inicial para vínculo direto)
  if (!hwidLimpo) {
    try {
      const fn = httpsCallable<any, any>(functions, "adminGenerateLicense");
      const res = await fn({
        clienteId: params.clienteId,
        plano: params.plano,
        dataInicio: params.dataInicio,
        dataVencimento: params.dataVencimento,
        limiteDispositivos: params.limiteDispositivos,
        observacoes: params.observacoes,
        chavePersonalizada: params.chavePersonalizada,
      });
      if (res.data && res.data.sucesso) {
        return { id: res.data.licencaId, chave: res.data.chave };
      }
    } catch (fnErr) {
      console.warn("Cloud function adminGenerateLicense falhou ou não implantada; aplicando fallback direto seguro no Firestore:", fnErr);
    }
  }

  // Fallback seguro direto no Firestore
  const chave = params.chavePersonalizada ? normalizarChave(params.chavePersonalizada) : gerarChaveNexus();
  const hash = await calcularHashChave(chave);
  const docId = chave;

  const novaLicenca: Record<string, any> = {
    id: docId,
    hashChave: hash,
    codigoFormatado: chave,
    clienteId: params.clienteId,
    clienteNome: params.clienteNome,
    empresa: params.empresa,
    plano: params.plano,
    dataInicio: params.dataInicio,
    dataVencimento: params.dataVencimento,
    limiteDispositivos: Number(params.limiteDispositivos) || 1,
    status: hwidLimpo ? "ATIVA" : "PENDENTE",
    motivoBloqueio: null,
    bloqueadoEm: null,
    bloqueadoPor: null,
    canceladoEm: null,
    canceladoPor: null,
    observacoes: params.observacoes || "",
    criadoEm: serverTimestamp(),
    atualizadoEm: serverTimestamp(),
    criadoPor: currentUser,

    // Compatibilidade com cliente C# legados
    cliente: params.empresa || params.clienteNome,
    validade: params.dataVencimento,
    ativa: true,
    maquina: hwidLimpo,
  };

  await setDoc(doc(db, "licencas", docId), novaLicenca);

  if (hwidLimpo) {
    await setDoc(doc(db, "dispositivos", `${docId}_${hwidLimpo}`), {
      id: `${docId}_${hwidLimpo}`,
      licencaId: docId,
      clienteId: params.clienteId,
      identificadorDispositivo: hwidLimpo,
      nomeDispositivo: `Ativado pelo Admin (${hwidLimpo})`,
      hostname: hwidLimpo,
      sistema: "NexusLS Desktop",
      ativo: true,
      primeiraAtivacao: serverTimestamp(),
      ultimaVerificacao: serverTimestamp(),
    });
  }

  // Incrementa contador do cliente
  await updateDoc(doc(db, "clientes", params.clienteId), {
    totalLicencas: increment(1),
    atualizadoEm: serverTimestamp(),
  });

  await registrarAuditoriaLocal({
    tipoAcao: "CRIACAO_LICENCA",
    licencaId: docId,
    codigoLicenca: chave,
    clienteId: params.clienteId,
    clienteNome: params.clienteNome,
    responsavel: currentUser,
    detalhes: {
      plano: params.plano,
      dataVencimento: params.dataVencimento,
      limiteDispositivos: params.limiteDispositivos,
    },
  });

  return { id: docId, chave };
}

export async function renovarLicenca(
  licencaId: string,
  novaDataVencimento: string,
  diasAdicionais?: number
): Promise<void> {
  const currentUser = auth.currentUser?.email || "Admin";

  try {
    const fn = httpsCallable<any, any>(functions, "adminRenewLicense");
    await fn({ licencaId, novaDataVencimento, diasAdicionais });
    return;
  } catch (err) {
    console.warn("Fallback direto para renovação:", err);
  }

  const licRef = doc(db, "licencas", licencaId);
  const licSnap = await getDoc(licRef);
  const licData = licSnap.data() || {};

  await updateDoc(licRef, {
    dataVencimento: novaDataVencimento,
    validade: novaDataVencimento,
    status: "ATIVA",
    ativa: true,
    atualizadoEm: serverTimestamp(),
  });

  await registrarAuditoriaLocal({
    tipoAcao: "RENOVACAO",
    licencaId,
    codigoLicenca: licData.codigoFormatado || licencaId,
    clienteId: licData.clienteId,
    clienteNome: licData.clienteNome,
    responsavel: currentUser,
    detalhes: {
      vencimentoAnterior: licData.dataVencimento,
      novoVencimento: novaDataVencimento,
      diasAdicionais: diasAdicionais || null,
    },
  });
}

export async function bloquearLicenca(licencaId: string, motivo: string): Promise<void> {
  const currentUser = auth.currentUser?.email || "Admin";

  try {
    const fn = httpsCallable<any, any>(functions, "adminBlockLicense");
    await fn({ licencaId, motivo });
    return;
  } catch (err) {
    console.warn("Fallback direto para bloqueio:", err);
  }

  const licRef = doc(db, "licencas", licencaId);
  const licSnap = await getDoc(licRef);
  const licData = licSnap.data() || {};

  await updateDoc(licRef, {
    status: "BLOQUEADA",
    ativa: false,
    motivoBloqueio: motivo,
    bloqueadoEm: serverTimestamp(),
    bloqueadoPor: currentUser,
    atualizadoEm: serverTimestamp(),
  });

  await registrarAuditoriaLocal({
    tipoAcao: "BLOQUEIO",
    licencaId,
    codigoLicenca: licData.codigoFormatado || licencaId,
    clienteId: licData.clienteId,
    clienteNome: licData.clienteNome,
    responsavel: currentUser,
    detalhes: { motivo },
  });
}

export async function desbloquearLicenca(licencaId: string): Promise<void> {
  const currentUser = auth.currentUser?.email || "Admin";

  try {
    const fn = httpsCallable<any, any>(functions, "adminUnblockLicense");
    await fn({ licencaId });
    return;
  } catch (err) {
    console.warn("Fallback direto para desbloqueio:", err);
  }

  const licRef = doc(db, "licencas", licencaId);
  const licSnap = await getDoc(licRef);
  const licData = licSnap.data() || {};

  const venc = new Date(licData.dataVencimento || licData.validade);
  const novoStatus = venc < new Date() ? "VENCIDA" : "ATIVA";

  await updateDoc(licRef, {
    status: novoStatus,
    ativa: novoStatus === "ATIVA",
    motivoBloqueio: null,
    bloqueadoEm: null,
    bloqueadoPor: null,
    atualizadoEm: serverTimestamp(),
  });

  await registrarAuditoriaLocal({
    tipoAcao: "DESBLOQUEIO",
    licencaId,
    codigoLicenca: licData.codigoFormatado || licencaId,
    clienteId: licData.clienteId,
    clienteNome: licData.clienteNome,
    responsavel: currentUser,
    detalhes: { novoStatus },
  });
}

export async function cancelarLicenca(licencaId: string, motivo?: string): Promise<void> {
  const currentUser = auth.currentUser?.email || "Admin";

  try {
    const fn = httpsCallable<any, any>(functions, "adminCancelLicense");
    await fn({ licencaId, motivo });
    return;
  } catch (err) {
    console.warn("Fallback direto para cancelamento:", err);
  }

  const licRef = doc(db, "licencas", licencaId);
  const licSnap = await getDoc(licRef);
  const licData = licSnap.data() || {};

  await updateDoc(licRef, {
    status: "CANCELADA",
    ativa: false,
    canceladoEm: serverTimestamp(),
    canceladoPor: currentUser,
    motivoCancelamento: motivo || "Encerrada definitivamente pelo administrador",
    atualizadoEm: serverTimestamp(),
  });

  await registrarAuditoriaLocal({
    tipoAcao: "CANCELAMENTO",
    licencaId,
    codigoLicenca: licData.codigoFormatado || licencaId,
    clienteId: licData.clienteId,
    clienteNome: licData.clienteNome,
    responsavel: currentUser,
    detalhes: { motivo },
  });
}

// ----------------------------------------------------------------------------
// SERVIÇOS DE DISPOSITIVOS VINCULADOS
// ----------------------------------------------------------------------------
export async function listarDispositivosPorLicenca(licencaId: string): Promise<DispositivoVinculado[]> {
  try {
    const q = query(collection(db, "dispositivos"), where("licencaId", "==", licencaId));
    const snapshot = await getDocs(q);
    const lista = (snapshot.docs
      .map((docSnap) => ({
        id: docSnap.id,
        ...docSnap.data(),
      })) as DispositivoVinculado[]).filter((d) => d.ativo !== false);

    // Sincroniza também HWIDs presentes no campo "maquina" da licença (caso ativado direto via Firestore REST)
    const licSnap = await getDoc(doc(db, "licencas", licencaId));
    if (licSnap.exists()) {
      const licData = licSnap.data();
      const hwids = extrairMaquinasCampo(licData.maquina);
      const jaListados = new Set(lista.map((d) => d.identificadorDispositivo.toUpperCase()));
      for (const hwid of hwids) {
        if (!jaListados.has(hwid)) {
          lista.push({
            id: `${licencaId}_${hwid}`,
            licencaId,
            clienteId: licData.clienteId || "",
            identificadorDispositivo: hwid,
            nomeDispositivo: `Terminal (${hwid})`,
            ativo: true,
            primeiraAtivacao: licData.atualizadoEm || licData.criadoEm || null,
            ultimaVerificacao: licData.atualizadoEm || licData.criadoEm || null,
            sistema: "NexusLS Desktop",
          });
        }
      }
    }

    return lista;
  } catch (error) {
    console.error("Erro ao listar dispositivos:", error);
    return [];
  }
}

export async function listarTodosDispositivos(): Promise<DispositivoVinculado[]> {
  try {
    const snapshot = await getDocs(collection(db, "dispositivos"));
    const lista = (snapshot.docs
      .map((docSnap) => ({
        id: docSnap.id,
        ...docSnap.data(),
      })) as DispositivoVinculado[]).filter((d) => d.ativo !== false);

    const licSnapshot = await getDocs(collection(db, "licencas"));
    const existentes = new Set(lista.map((d) => `${d.licencaId}_${d.identificadorDispositivo.toUpperCase()}`));

    for (const licDoc of licSnapshot.docs) {
      const licData = licDoc.data();
      const hwids = extrairMaquinasCampo(licData.maquina);
      for (const hwid of hwids) {
        const chaveUnica = `${licDoc.id}_${hwid}`;
        if (!existentes.has(chaveUnica)) {
          existentes.add(chaveUnica);
          lista.push({
            id: chaveUnica,
            licencaId: licDoc.id,
            clienteId: licData.clienteId || "",
            identificadorDispositivo: hwid,
            nomeDispositivo: `Terminal (${hwid})`,
            ativo: true,
            primeiraAtivacao: licData.atualizadoEm || licData.criadoEm || null,
            ultimaVerificacao: licData.atualizadoEm || licData.criadoEm || null,
            sistema: "NexusLS Desktop",
          });
        }
      }
    }

    return lista;
  } catch (error) {
    console.error("Erro ao listar todos os dispositivos:", error);
    return [];
  }
}

export async function desvincularDispositivo(dispositivoDocId: string, licencaId: string): Promise<void> {
  const currentUser = auth.currentUser?.email || "Admin";

  try {
    const fn = httpsCallable<any, any>(functions, "adminRemoveDevice");
    await fn({ dispositivoDocId, licencaId });
    return;
  } catch (err) {
    console.warn("Fallback direto para desvinculação de dispositivo:", err);
  }

  let hwidAlvo = "";
  const dispRef = doc(db, "dispositivos", dispositivoDocId);
  const dispSnap = await getDoc(dispRef);
  const dispData = dispSnap.exists() ? dispSnap.data() : {};

  if (dispData?.identificadorDispositivo) {
    hwidAlvo = String(dispData.identificadorDispositivo).trim().toUpperCase();
  } else if (dispositivoDocId.startsWith(`${licencaId}_`)) {
    hwidAlvo = dispositivoDocId.substring(licencaId.length + 1).trim().toUpperCase();
  }

  if (dispSnap.exists()) {
    await deleteDoc(dispRef);
  }

  // Remove o HWID da lista separada por vírgula no campo maquina da licença
  const licRef = doc(db, "licencas", licencaId);
  const licSnap = await getDoc(licRef);
  if (licSnap.exists()) {
    const licData = licSnap.data();
    const restantes = extrairMaquinasCampo(licData.maquina).filter(
      (m) => m.toUpperCase() !== hwidAlvo
    );
    await updateDoc(licRef, {
      maquina: restantes.join(","),
      atualizadoEm: serverTimestamp(),
    });
  }

  await registrarAuditoriaLocal({
    tipoAcao: "REMOCAO_DISPOSITIVO",
    licencaId,
    codigoLicenca: licSnap.exists() ? licSnap.data()?.codigoFormatado : null,
    clienteId: dispData?.clienteId || (licSnap.exists() ? licSnap.data()?.clienteId : null),
    responsavel: currentUser,
    detalhes: {
      identificadorDispositivo: hwidAlvo || dispData?.identificadorDispositivo,
      nomeDispositivo: dispData?.nomeDispositivo || `Terminal (${hwidAlvo})`,
    },
  });
}

export async function vincularDispositivoManual(
  licencaId: string,
  identificadorDispositivo: string,
  nomeDispositivo?: string
): Promise<void> {
  const currentUser = auth.currentUser?.email || "Admin";
  const hwid = identificadorDispositivo.trim().toUpperCase();
  if (!hwid) throw new Error("Informe o Código do Computador (HWID).");

  const licRef = doc(db, "licencas", licencaId);
  const licSnap = await getDoc(licRef);
  if (!licSnap.exists()) throw new Error("Licença não encontrada.");

  const licData = licSnap.data();
  const limite = Number(licData.limiteDispositivos || 1);
  const atuais = extrairMaquinasCampo(licData.maquina);

  if (!atuais.includes(hwid) && atuais.length >= limite) {
    throw new Error(`Limite de ${limite} computador(es) já atingido nesta licença. Desvincule um terminal antes.`);
  }

  const novaLista = atuais.includes(hwid) ? atuais : [...atuais, hwid];
  const dispDocId = `${licencaId}_${hwid}`;

  await setDoc(
    doc(db, "dispositivos", dispDocId),
    {
      id: dispDocId,
      licencaId,
      clienteId: licData.clienteId || "",
      identificadorDispositivo: hwid,
      nomeDispositivo: (nomeDispositivo || "").trim() || `Terminal (${hwid})`,
      hostname: (nomeDispositivo || "").trim() || hwid,
      sistema: "NexusLS Desktop",
      ativo: true,
      primeiraAtivacao: serverTimestamp(),
      ultimaVerificacao: serverTimestamp(),
    },
    { merge: true }
  );

  await updateDoc(licRef, {
    maquina: novaLista.join(","),
    status: licData.status === "PENDENTE" ? "ATIVA" : licData.status,
    ativa: true,
    atualizadoEm: serverTimestamp(),
  });

  await registrarAuditoriaLocal({
    tipoAcao: "ATIVACAO_DISPOSITIVO",
    licencaId,
    codigoLicenca: licData.codigoFormatado || licencaId,
    clienteId: licData.clienteId,
    clienteNome: licData.clienteNome,
    responsavel: currentUser,
    detalhes: {
      identificadorDispositivo: hwid,
      nomeDispositivo: (nomeDispositivo || "").trim() || `Terminal (${hwid})`,
      origem: "Painel Admin / Celular",
    },
  });
}

// ----------------------------------------------------------------------------
// SERVIÇOS DE AUDITORIA
// ----------------------------------------------------------------------------
export async function listarAuditoria(limiteRegistros = 100): Promise<HistoricoAuditoria[]> {
  try {
    const q = query(
      collection(db, "historico_auditoria"),
      orderBy("dataHora", "desc"),
      limit(limiteRegistros)
    );
    const snapshot = await getDocs(q);
    return snapshot.docs.map((d) => ({
      id: d.id,
      ...d.data(),
    })) as HistoricoAuditoria[];
  } catch (error) {
    console.error("Erro ao buscar histórico de auditoria:", error);
    return [];
  }
}

async function registrarAuditoriaLocal(dados: Omit<HistoricoAuditoria, "id" | "dataHora">) {
  try {
    const ref = doc(collection(db, "historico_auditoria"));
    await setDoc(ref, {
      ...dados,
      dataHora: serverTimestamp(),
    });
  } catch (err) {
    console.warn("Falha ao gravar histórico de auditoria local:", err);
  }
}

// ----------------------------------------------------------------------------
// SIMULADOR / TESTADOR DE VALIDAÇÃO DO ATUALIZADOR
// ----------------------------------------------------------------------------
export async function simularValidacaoAtualizador(params: {
  chave: string;
  dispositivoId: string;
  nomeDispositivo?: string;
  sistema?: string;
}): Promise<RespostaValidacaoAtualizador> {
  const chaveNorm = normalizarChave(params.chave);
  const dispId = params.dispositivoId.trim();

  // 1. Tenta invocar a Cloud Function via HTTP direto
  const functionUrl = "https://southamerica-east1-nexus-ls.cloudfunctions.net/validateLicense";
  try {
    const response = await fetch(functionUrl, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        chave: chaveNorm,
        dispositivoId: dispId,
        nomeDispositivo: params.nomeDispositivo || "Simulador Web",
        sistema: params.sistema || "NexusLS",
      }),
    });

    const data = await response.json();
    return data as RespostaValidacaoAtualizador;
  } catch (httpErr) {
    console.warn("Cloud function offline ou em implantação. Executando validação pelo motor local idêntico:", httpErr);
  }

  // 2. Motor de validação idêntico executado localmente para testes imediatos
  const hash = await calcularHashChave(chaveNorm);

  // Busca licença
  let licData: any = null;
  let licId = "";

  const queryHash = await getDocs(query(collection(db, "licencas"), where("hashChave", "==", hash), limit(1)));
  if (!queryHash.empty) {
    licData = queryHash.docs[0].data();
    licId = queryHash.docs[0].id;
  } else {
    const docSnap = await getDoc(doc(db, "licencas", chaveNorm));
    if (docSnap.exists()) {
      licData = docSnap.data();
      licId = docSnap.id;
    }
  }

  if (!licData) {
    return {
      autorizado: false,
      situacao: "NAO_ENCONTRADA",
      mensagem: "Licença não encontrada no sistema. Verifique a chave digitada.",
    };
  }

  const { status, diasRestantes } = calcularStatusReal(licData);
  const clienteNome = licData.clienteNome || licData.cliente || "Cliente";
  const empresa = licData.empresa || "";
  const limiteDispositivos = Number(licData.limiteDispositivos || 1);

  if (status === "CANCELADA") {
    return {
      autorizado: false,
      situacao: "CANCELADA",
      codigoLicenca: licData.codigoFormatado || chaveNorm,
      cliente: clienteNome,
      empresa,
      mensagem: "Esta licença foi cancelada definitivamente.",
    };
  }

  if (status === "BLOQUEADA") {
    return {
      autorizado: false,
      situacao: "BLOQUEADA",
      codigoLicenca: licData.codigoFormatado || chaveNorm,
      cliente: clienteNome,
      empresa,
      mensagem: licData.motivoBloqueio
        ? `Licença suspensa administrativamente: ${licData.motivoBloqueio}`
        : "Licença suspensa administrativamente.",
    };
  }

  if (status === "VENCIDA" || diasRestantes < 0) {
    return {
      autorizado: false,
      situacao: "VENCIDA",
      codigoLicenca: licData.codigoFormatado || chaveNorm,
      cliente: clienteNome,
      empresa,
      dataVencimento: licData.dataVencimento,
      diasRestantes,
      mensagem: `Licença vencida em ${licData.dataVencimento}. Renove o plano para continuar.`,
    };
  }

  // Verifica dispositivos (coleção dispositivos + campo maquina)
  const dispSnap = await getDocs(
    query(collection(db, "dispositivos"), where("licencaId", "==", licId), where("ativo", "==", true))
  );
  const ativosDocs = dispSnap.docs.map((d) => d.data());
  const hwidsConjunto = new Set<string>(
    ativosDocs.map((d: any) => String(d.identificadorDispositivo || "").trim().toUpperCase()).filter(Boolean)
  );
  extrairMaquinasCampo(licData.maquina).forEach((m) => hwidsConjunto.add(m));

  const dispIdUpper = dispId.toUpperCase();
  const jaVinculado = hwidsConjunto.has(dispIdUpper);

  if (!jaVinculado && hwidsConjunto.size >= limiteDispositivos) {
    return {
      autorizado: false,
      situacao: "LIMITE_ATINGIDO",
      codigoLicenca: licData.codigoFormatado || chaveNorm,
      cliente: clienteNome,
      empresa,
      limiteDispositivos,
      dispositivosAtivos: hwidsConjunto.size,
      mensagem: `Limite de computadores atingido (${hwidsConjunto.size}/${limiteDispositivos}). Libere um computador no painel de licenças.`,
    };
  }

  // Registra novo dispositivo se não estava registrado
  if (!jaVinculado) {
    hwidsConjunto.add(dispIdUpper);
    const dispRef = doc(db, "dispositivos", `${licId}_${dispIdUpper}`);
    await setDoc(dispRef, {
      licencaId: licId,
      clienteId: licData.clienteId,
      identificadorDispositivo: dispIdUpper,
      nomeDispositivo: params.nomeDispositivo || "Computador",
      ativo: true,
      primeiraAtivacao: serverTimestamp(),
      ultimaVerificacao: serverTimestamp(),
      sistema: params.sistema || "NexusLS",
    });

    await updateDoc(doc(db, "licencas", licId), {
      status: "ATIVA",
      ativa: true,
      maquina: Array.from(hwidsConjunto).join(","),
      atualizadoEm: serverTimestamp(),
    });
  }

  return {
    autorizado: true,
    situacao: diasRestantes <= 15 ? "VENCENDO" : "ATIVA",
    codigoLicenca: licData.codigoFormatado || chaveNorm,
    cliente: clienteNome,
    empresa,
    dataInicio: licData.dataInicio,
    dataVencimento: licData.dataVencimento,
    diasRestantes,
    identificadorDispositivo: dispId,
    dispositivoAutorizado: dispId,
    limiteDispositivos,
    dispositivosAtivos: hwidsConjunto.size,
    mensagem:
      diasRestantes <= 15
        ? `Licença válida até ${licData.dataVencimento} (atenção: vence em ${diasRestantes} dias).`
        : `Licença válida até ${licData.dataVencimento}.`,
  };
}
