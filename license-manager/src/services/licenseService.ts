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

  if (lic.status === "PENDENTE") {
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

  await setDoc(doc(db, "clientes", id), novoCliente);

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
  await updateDoc(doc(db, "clientes", id), {
    ...dados,
    atualizadoEm: serverTimestamp(),
  });

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
    const dispCountMap: Record<string, number> = {};
    dispSnapshot.docs.forEach((d) => {
      const data = d.data();
      if (data.licencaId) {
        dispCountMap[data.licencaId] = (dispCountMap[data.licencaId] || 0) + 1;
      }
    });

    for (const docSnap of snapshot.docs) {
      const data = docSnap.data();
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
      };

      const { status: statusReal, diasRestantes } = calcularStatusReal(lic);
      lic.status = statusReal;
      lic.diasRestantes = diasRestantes;
      lic.dispositivosAtivosCount = dispCountMap[docSnap.id] || (lic.maquina ? 1 : 0);

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
}): Promise<{ id: string; chave: string }> {
  const currentUser = auth.currentUser?.email || "Admin";

  // Tenta via Cloud Function administrativa
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

  // Fallback seguro direto no Firestore caso functions ainda estejam em implantação
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
    status: "PENDENTE",
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
    maquina: "",
  };

  await setDoc(doc(db, "licencas", docId), novaLicenca);

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
    return snapshot.docs.map((docSnap) => ({
      id: docSnap.id,
      ...docSnap.data(),
    })) as DispositivoVinculado[];
  } catch (error) {
    console.error("Erro ao listar dispositivos:", error);
    return [];
  }
}

export async function listarTodosDispositivos(): Promise<DispositivoVinculado[]> {
  try {
    const snapshot = await getDocs(collection(db, "dispositivos"));
    return snapshot.docs.map((docSnap) => ({
      id: docSnap.id,
      ...docSnap.data(),
    })) as DispositivoVinculado[];
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

  const dispRef = doc(db, "dispositivos", dispositivoDocId);
  const dispSnap = await getDoc(dispRef);
  const dispData = dispSnap.data() || {};

  await deleteDoc(dispRef);

  // Limpa campo maquina legado da licença se for coincidente
  const licRef = doc(db, "licencas", licencaId);
  const licSnap = await getDoc(licRef);
  if (licSnap.exists()) {
    const licData = licSnap.data();
    if (licData.maquina === dispData.identificadorDispositivo) {
      await updateDoc(licRef, { maquina: "", atualizadoEm: serverTimestamp() });
    }
  }

  await registrarAuditoriaLocal({
    tipoAcao: "REMOCAO_DISPOSITIVO",
    licencaId,
    codigoLicenca: licSnap.exists() ? licSnap.data()?.codigoFormatado : null,
    clienteId: dispData.clienteId,
    responsavel: currentUser,
    detalhes: {
      identificadorDispositivo: dispData.identificadorDispositivo,
      nomeDispositivo: dispData.nomeDispositivo,
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

  // Verifica dispositivos
  const dispSnap = await getDocs(
    query(collection(db, "dispositivos"), where("licencaId", "==", licId), where("ativo", "==", true))
  );
  const ativos = dispSnap.docs.map((d) => d.data());
  const jaVinculado = ativos.some((d: any) => d.identificadorDispositivo === dispId);

  if (!jaVinculado && ativos.length >= limiteDispositivos) {
    return {
      autorizado: false,
      situacao: "LIMITE_ATINGIDO",
      codigoLicenca: licData.codigoFormatado || chaveNorm,
      cliente: clienteNome,
      empresa,
      limiteDispositivos,
      dispositivosAtivos: ativos.length,
      mensagem: `Limite de computadores atingido (${ativos.length}/${limiteDispositivos}). Libere um computador no painel de licenças.`,
    };
  }

  // Registra novo dispositivo se não estava registrado
  if (!jaVinculado) {
    const dispRef = doc(db, "dispositivos", `${licId}_${dispId}`);
    await setDoc(dispRef, {
      licencaId: licId,
      clienteId: licData.clienteId,
      identificadorDispositivo: dispId,
      nomeDispositivo: params.nomeDispositivo || "Computador",
      ativo: true,
      primeiraAtivacao: serverTimestamp(),
      ultimaVerificacao: serverTimestamp(),
      sistema: params.sistema || "NexusLS",
    });

    if (licData.status === "PENDENTE") {
      await updateDoc(doc(db, "licencas", licId), {
        status: "ATIVA",
        ativa: true,
        maquina: dispId,
        atualizadoEm: serverTimestamp(),
      });
    }
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
    dispositivosAtivos: jaVinculado ? ativos.length : ativos.length + 1,
    mensagem:
      diasRestantes <= 15
        ? `Licença válida até ${licData.dataVencimento} (atenção: vence em ${diasRestantes} dias).`
        : `Licença válida até ${licData.dataVencimento}.`,
  };
}
