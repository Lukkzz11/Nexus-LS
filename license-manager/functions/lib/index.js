"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
exports.setAdminClaim = exports.adminRemoveDevice = exports.adminCancelLicense = exports.adminUnblockLicense = exports.adminBlockLicense = exports.adminRenewLicense = exports.adminGenerateLicense = exports.validateLicense = void 0;
const functions = require("firebase-functions");
const admin = require("firebase-admin");
const crypto = require("crypto");
if (!admin.apps.length) {
    admin.initializeApp();
}
const db = admin.firestore();
// Formato padrão de chave: NEXUS-8K29-XP41-MZ73
const CHAVE_ALFABETO = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // sem 0/O, 1/I
function normalizarChave(chave) {
    return chave.trim().toUpperCase().replace(/[^A-Z0-9-]/g, "");
}
function calcularHashChave(chave) {
    const norm = normalizarChave(chave);
    return crypto.createHash("sha256").update(norm).digest("hex");
}
function gerarChaveAleatoria() {
    const segmento = (tamanho) => {
        let res = "";
        const bytes = crypto.randomBytes(tamanho);
        for (let i = 0; i < tamanho; i++) {
            res += CHAVE_ALFABETO[bytes[i] % CHAVE_ALFABETO.length];
        }
        return res;
    };
    return `NEXUS-${segmento(4)}-${segmento(4)}-${segmento(4)}`;
}
// Helper de auditoria
async function registrarAuditoria(params) {
    try {
        await db.collection("historico_auditoria").add({
            tipoAcao: params.tipoAcao,
            licencaId: params.licencaId || null,
            codigoLicenca: params.codigoLicenca || null,
            clienteId: params.clienteId || null,
            clienteNome: params.clienteNome || null,
            responsavel: params.responsavel,
            detalhes: params.detalhes,
            dataHora: admin.firestore.FieldValue.serverTimestamp(),
        });
    }
    catch (err) {
        console.error("Falha ao gravar histórico de auditoria:", err);
    }
}
// Helper para verificar se o chamador é administrador
async function verificarPermissaoAdmin(context) {
    if (!context.auth) {
        throw new functions.https.HttpsError("unauthenticated", "Apenas administradores autenticados podem realizar esta operação.");
    }
    // Verifica Custom Claims (token.admin) ou se o e-mail está na coleção de admins
    const isAdminClaim = context.auth.token.admin === true;
    const email = (context.auth.token.email || "").toLowerCase();
    if (isAdminClaim) {
        return { email, uid: context.auth.uid };
    }
    // Fallback: consulta doc na coleção `admins`
    if (email) {
        const adminDoc = await db.collection("admins").doc(email).get();
        if (adminDoc.exists && adminDoc.data()?.ativo === true) {
            return { email, uid: context.auth.uid };
        }
    }
    throw new functions.https.HttpsError("permission-denied", "Acesso restrito. Seu usuário não possui privilégios de administrador.");
}
// ============================================================================
// 1. ENDPOINT PÚBLICO HTTP PARA O ATUALIZADOR DO NEXUS LS
// ============================================================================
exports.validateLicense = functions
    .region("southamerica-east1")
    .https.onRequest(async (req, res) => {
    // Configura headers CORS
    res.set("Access-Control-Allow-Origin", "*");
    res.set("Access-Control-Allow-Methods", "POST, OPTIONS");
    res.set("Access-Control-Allow-Headers", "Content-Type, Authorization, X-Requested-With");
    if (req.method === "OPTIONS") {
        res.status(204).send("");
        return;
    }
    if (req.method !== "POST") {
        res.status(405).json({
            autorizado: false,
            situacao: "ERRO",
            mensagem: "Método não permitido. Utilize POST.",
        });
        return;
    }
    try {
        const { acao, chave, dispositivoId, nomeDispositivo, sistema } = req.body || {};
        if (!chave || typeof chave !== "string") {
            res.status(400).json({
                autorizado: false,
                situacao: "CHAVE_INVALIDA",
                mensagem: "Chave de licença não informada.",
            });
            return;
        }
        if (!dispositivoId || typeof dispositivoId !== "string") {
            res.status(400).json({
                autorizado: false,
                situacao: "DISPOSITIVO_INVALIDO",
                mensagem: "Identificador do computador não informado.",
            });
            return;
        }
        const chaveNormalizada = normalizarChave(chave);
        const hashChave = calcularHashChave(chaveNormalizada);
        // Localiza a licença por hash ou por doc id (para compatibilidade)
        let licencaRef = null;
        let licencaData = null;
        // 1. Tenta buscar por hashChave
        const queryHash = await db.collection("licencas").where("hashChave", "==", hashChave).limit(1).get();
        if (!queryHash.empty) {
            licencaRef = queryHash.docs[0].ref;
            licencaData = queryHash.docs[0].data();
        }
        else {
            // 2. Tenta por ID direto (compatibilidade com schemas anteriores de doc id = chave)
            const docDireto = await db.collection("licencas").doc(chaveNormalizada).get();
            if (docDireto.exists) {
                licencaRef = docDireto.ref;
                licencaData = docDireto.data();
            }
        }
        if (!licencaRef || !licencaData) {
            res.status(404).json({
                autorizado: false,
                situacao: "NAO_ENCONTRADA",
                mensagem: "Licença não encontrada no sistema. Verifique a chave digitada.",
            });
            return;
        }
        // Verificação de vínculo com cliente
        const clienteId = licencaData.clienteId || "";
        const clienteNome = licencaData.clienteNome || licencaData.cliente || "Cliente";
        const empresa = licencaData.empresaNome || licencaData.empresa || "";
        const plano = licencaData.plano || "PERSONALIZADO";
        if (acao === "desvincular") {
            const dispIdSanitizado = dispositivoId.trim();
            const dispDocRef = db.collection("dispositivos").doc(`${licencaRef.id}_${dispIdSanitizado}`);
            const dispSnap = await dispDocRef.get();
            if (dispSnap.exists && dispSnap.data()?.ativo === true) {
                await dispDocRef.update({
                    ativo: false,
                    removidoEm: admin.firestore.FieldValue.serverTimestamp(),
                    removidoPor: "CLIENTE_ATUALIZADOR",
                });
                if (licencaData.maquina === dispIdSanitizado) {
                    await licencaRef.update({ maquina: "" });
                }
                await registrarAuditoria({
                    tipoAcao: "REMOCAO_DISPOSITIVO",
                    licencaId: licencaRef.id,
                    codigoLicenca: licencaData.codigoFormatado || chaveNormalizada,
                    clienteId,
                    clienteNome,
                    responsavel: `Atualizador (${dispIdSanitizado})`,
                    detalhes: {
                        dispositivoId: dispIdSanitizado,
                        motivo: "Removido voluntariamente pelo usuário no Atualizador",
                    },
                });
            }
            res.status(200).json({
                autorizado: false,
                situacao: "DESVINCULADO",
                mensagem: "Computador desvinculado com sucesso.",
            });
            return;
        }
        // Verificação de status manual
        const statusAtual = (licencaData.status || (licencaData.ativa ? "ATIVA" : "BLOQUEADA")).toUpperCase();
        if (statusAtual === "CANCELADA") {
            res.status(403).json({
                autorizado: false,
                situacao: "CANCELADA",
                codigoLicenca: licencaData.codigoFormatado || chaveNormalizada,
                cliente: clienteNome,
                empresa,
                mensagem: "Esta licença foi cancelada definitivamente.",
            });
            return;
        }
        if (statusAtual === "BLOQUEADA" || licencaData.ativa === false) {
            res.status(403).json({
                autorizado: false,
                situacao: "BLOQUEADA",
                codigoLicenca: licencaData.codigoFormatado || chaveNormalizada,
                cliente: clienteNome,
                empresa,
                mensagem: licencaData.motivoBloqueio
                    ? `Licença suspensa administrativamente: ${licencaData.motivoBloqueio}`
                    : "Licença suspensa administrativamente. Entre em contato com o suporte.",
            });
            return;
        }
        // Cálculo no servidor da validade
        const agora = new Date();
        let validadeDate = null;
        if (licencaData.dataVencimento) {
            validadeDate = new Date(licencaData.dataVencimento);
        }
        else if (licencaData.validade) {
            validadeDate = new Date(licencaData.validade);
        }
        if (!validadeDate || isNaN(validadeDate.getTime())) {
            res.status(500).json({
                autorizado: false,
                situacao: "ERRO_DATA",
                mensagem: "Data de validade da licença inválida no cadastro do servidor.",
            });
            return;
        }
        // Zerar horário para comparar dias corridos
        const agoraZero = new Date(agora.getFullYear(), agora.getMonth(), agora.getDate());
        const vencZero = new Date(validadeDate.getFullYear(), validadeDate.getMonth(), validadeDate.getDate());
        const diffMs = vencZero.getTime() - agoraZero.getTime();
        const diasRestantes = Math.round(diffMs / (1000 * 60 * 60 * 24));
        if (diasRestantes < 0) {
            // Atualiza status para VENCIDA se ainda constava como ATIVA
            if (licencaData.status !== "VENCIDA") {
                await licencaRef.update({
                    status: "VENCIDA",
                    ativa: false,
                    atualizadoEm: admin.firestore.FieldValue.serverTimestamp(),
                });
            }
            res.status(403).json({
                autorizado: false,
                situacao: "VENCIDA",
                codigoLicenca: licencaData.codigoFormatado || chaveNormalizada,
                cliente: clienteNome,
                empresa,
                dataVencimento: validadeDate.toISOString().split("T")[0],
                diasRestantes,
                mensagem: `Licença vencida em ${validadeDate.toLocaleDateString("pt-BR")}. Renove seu plano para continuar.`,
            });
            return;
        }
        // Controle de dispositivos vinculados
        const limiteDispositivos = Number(licencaData.limiteDispositivos || 1);
        const dispIdSanitizado = dispositivoId.trim();
        // Busca dispositivos já registrados para esta licença
        const dispSnapshot = await db
            .collection("dispositivos")
            .where("licencaId", "==", licencaRef.id)
            .where("ativo", "==", true)
            .get();
        const dispositivosAtivos = dispSnapshot.docs.map((doc) => ({
            id: doc.id,
            ...doc.data(),
        }));
        const dispExistente = dispositivosAtivos.find((d) => d.identificadorDispositivo === dispIdSanitizado);
        const dispDocRef = db.collection("dispositivos").doc(`${licencaRef.id}_${dispIdSanitizado}`);
        if (dispExistente) {
            // Computador já autorizado -> atualiza última verificação
            await dispDocRef.set({
                ultimaVerificacao: admin.firestore.FieldValue.serverTimestamp(),
                nomeDispositivo: nomeDispositivo || dispExistente.nomeDispositivo || "Computador",
                ipUltimoAcesso: req.ip || "desconhecido",
                sistema: sistema || "NexusLS",
            }, { merge: true });
        }
        else {
            // Computador novo: confere limite
            if (dispositivosAtivos.length >= limiteDispositivos) {
                res.status(403).json({
                    autorizado: false,
                    situacao: "LIMITE_ATINGIDO",
                    codigoLicenca: licencaData.codigoFormatado || chaveNormalizada,
                    cliente: clienteNome,
                    empresa,
                    limiteDispositivos,
                    dispositivosAtivos: dispositivosAtivos.length,
                    mensagem: `Limite de computadores atingido (${dispositivosAtivos.length}/${limiteDispositivos}). Libere um computador no painel de licenças.`,
                });
                return;
            }
            // Registra o novo computador
            await dispDocRef.set({
                licencaId: licencaRef.id,
                clienteId,
                identificadorDispositivo: dispIdSanitizado,
                nomeDispositivo: nomeDispositivo || "Computador",
                ativo: true,
                primeiraAtivacao: admin.firestore.FieldValue.serverTimestamp(),
                ultimaVerificacao: admin.firestore.FieldValue.serverTimestamp(),
                ipUltimoAcesso: req.ip || "desconhecido",
                sistema: sistema || "NexusLS",
            });
            // Se o status da licença era PENDENTE, agora vira ATIVA
            const updates = {
                atualizadoEm: admin.firestore.FieldValue.serverTimestamp(),
            };
            if (licencaData.status === "PENDENTE") {
                updates.status = "ATIVA";
                updates.ativa = true;
            }
            // Atualiza campo legado maquina se estiver vazio
            if (!licencaData.maquina) {
                updates.maquina = dispIdSanitizado;
            }
            await licencaRef.update(updates);
            await registrarAuditoria({
                tipoAcao: "ATIVACAO_DISPOSITIVO",
                licencaId: licencaRef.id,
                codigoLicenca: licencaData.codigoFormatado || chaveNormalizada,
                clienteId,
                clienteNome,
                responsavel: `Atualizador (${dispIdSanitizado})`,
                detalhes: {
                    dispositivoId: dispIdSanitizado,
                    nomeDispositivo: nomeDispositivo || "Computador",
                    ip: req.ip,
                },
            });
        }
        const totalAtivos = dispExistente ? dispositivosAtivos.length : dispositivosAtivos.length + 1;
        // Retorno formatado para o atualizador do Nexus LS
        res.status(200).json({
            autorizado: true,
            situacao: diasRestantes <= 15 ? "VENCENDO" : "ATIVA",
            codigoLicenca: licencaData.codigoFormatado || chaveNormalizada,
            cliente: clienteNome,
            empresa,
            plano,
            dataInicio: licencaData.dataInicio || new Date().toISOString().split("T")[0],
            dataVencimento: validadeDate.toISOString().split("T")[0],
            diasRestantes,
            identificadorDispositivo: dispIdSanitizado,
            dispositivoAutorizado: dispIdSanitizado,
            limiteDispositivos,
            dispositivosAtivos: totalAtivos,
            dispositivosUsados: totalAtivos,
            mensagem: diasRestantes <= 15
                ? `Licença válida até ${validadeDate.toLocaleDateString("pt-BR")} (atenção: vence em ${diasRestantes} dias).`
                : `Licença válida até ${validadeDate.toLocaleDateString("pt-BR")}.`,
        });
    }
    catch (error) {
        console.error("Erro na validação de licença:", error);
        res.status(500).json({
            autorizado: false,
            situacao: "ERRO_INTERNO",
            mensagem: "Erro interno no servidor de licenças. Tente novamente mais tarde.",
        });
    }
});
// ============================================================================
// 2. FUNÇÕES CALLABLE ADMINISTRATIVAS (Somente Admin Autenticado)
// ============================================================================
// Gerar nova licença
exports.adminGenerateLicense = functions
    .region("southamerica-east1")
    .https.onCall(async (data, context) => {
    const adminUser = await verificarPermissaoAdmin(context);
    const { clienteId, plano, dataInicio, dataVencimento, limiteDispositivos, observacoes, chavePersonalizada, } = data;
    if (!clienteId) {
        throw new functions.https.HttpsError("invalid-argument", "Cliente é obrigatório.");
    }
    const clienteDoc = await db.collection("clientes").doc(clienteId).get();
    if (!clienteDoc.exists) {
        throw new functions.https.HttpsError("not-found", "Cliente não encontrado.");
    }
    const clienteData = clienteDoc.data();
    // Chave única
    const chaveBruta = chavePersonalizada ? normalizarChave(chavePersonalizada) : gerarChaveAleatoria();
    const hash = calcularHashChave(chaveBruta);
    // Verificar se chave ou hash já existe
    const existente = await db.collection("licencas").where("hashChave", "==", hash).limit(1).get();
    if (!existente.empty) {
        throw new functions.https.HttpsError("already-exists", "Esta chave de licença já existe no sistema.");
    }
    const docId = chaveBruta; // Permite consulta direta por doc id e compatibilidade legado
    const inicioStr = dataInicio || new Date().toISOString().split("T")[0];
    const vencimentoStr = dataVencimento;
    if (!vencimentoStr) {
        throw new functions.https.HttpsError("invalid-argument", "Data de vencimento é obrigatória.");
    }
    const novaLicenca = {
        id: docId,
        hashChave: hash,
        codigoFormatado: chaveBruta,
        clienteId,
        clienteNome: clienteData.nomeResponsavel || "",
        empresa: clienteData.empresa || "",
        plano: plano || "anual",
        dataInicio: inicioStr,
        dataVencimento: vencimentoStr,
        limiteDispositivos: Number(limiteDispositivos) || 1,
        status: "PENDENTE",
        motivoBloqueio: null,
        bloqueadoEm: null,
        bloqueadoPor: null,
        observacoes: observacoes || "",
        criadoEm: admin.firestore.FieldValue.serverTimestamp(),
        atualizadoEm: admin.firestore.FieldValue.serverTimestamp(),
        criadoPor: adminUser.email,
        // Campos de compatibilidade legado
        cliente: clienteData.empresa || clienteData.nomeResponsavel,
        validade: vencimentoStr,
        ativa: true,
        maquina: "",
    };
    await db.collection("licencas").doc(docId).set(novaLicenca);
    // Incrementa contador no cliente
    await db.collection("clientes").doc(clienteId).update({
        totalLicencas: admin.firestore.FieldValue.increment(1),
        atualizadoEm: admin.firestore.FieldValue.serverTimestamp(),
    });
    await registrarAuditoria({
        tipoAcao: "CRIACAO_LICENCA",
        licencaId: docId,
        codigoLicenca: chaveBruta,
        clienteId,
        clienteNome: clienteData.nomeResponsavel,
        responsavel: adminUser.email,
        detalhes: {
            plano,
            dataVencimento: vencimentoStr,
            limiteDispositivos: Number(limiteDispositivos) || 1,
        },
    });
    return {
        sucesso: true,
        licencaId: docId,
        chave: chaveBruta,
        dataVencimento: vencimentoStr,
    };
});
// Renovar licença
exports.adminRenewLicense = functions
    .region("southamerica-east1")
    .https.onCall(async (data, context) => {
    const adminUser = await verificarPermissaoAdmin(context);
    const { licencaId, novaDataVencimento, diasAdicionais } = data;
    if (!licencaId) {
        throw new functions.https.HttpsError("invalid-argument", "ID da licença é obrigatório.");
    }
    const licRef = db.collection("licencas").doc(licencaId);
    const licDoc = await licRef.get();
    if (!licDoc.exists) {
        throw new functions.https.HttpsError("not-found", "Licença não encontrada.");
    }
    const licData = licDoc.data();
    let finalVencimento = novaDataVencimento;
    if (!finalVencimento && diasAdicionais) {
        const base = licData.dataVencimento ? new Date(licData.dataVencimento) : new Date();
        const refDate = base > new Date() ? base : new Date();
        refDate.setDate(refDate.getDate() + Number(diasAdicionais));
        finalVencimento = refDate.toISOString().split("T")[0];
    }
    if (!finalVencimento) {
        throw new functions.https.HttpsError("invalid-argument", "Nova data de vencimento não informada.");
    }
    await licRef.update({
        dataVencimento: finalVencimento,
        validade: finalVencimento,
        status: "ATIVA",
        ativa: true,
        atualizadoEm: admin.firestore.FieldValue.serverTimestamp(),
    });
    await registrarAuditoria({
        tipoAcao: "RENOVACAO",
        licencaId,
        codigoLicenca: licData.codigoFormatado,
        clienteId: licData.clienteId,
        clienteNome: licData.clienteNome,
        responsavel: adminUser.email,
        detalhes: {
            anteriorVencimento: licData.dataVencimento,
            novoVencimento: finalVencimento,
            diasAdicionais: diasAdicionais || null,
        },
    });
    return { sucesso: true, novaDataVencimento: finalVencimento };
});
// Bloquear licença
exports.adminBlockLicense = functions
    .region("southamerica-east1")
    .https.onCall(async (data, context) => {
    const adminUser = await verificarPermissaoAdmin(context);
    const { licencaId, motivo } = data;
    if (!licencaId || !motivo) {
        throw new functions.https.HttpsError("invalid-argument", "ID da licença e motivo do bloqueio são obrigatórios.");
    }
    const licRef = db.collection("licencas").doc(licencaId);
    const licDoc = await licRef.get();
    if (!licDoc.exists) {
        throw new functions.https.HttpsError("not-found", "Licença não encontrada.");
    }
    const licData = licDoc.data();
    await licRef.update({
        status: "BLOQUEADA",
        ativa: false,
        motivoBloqueio: motivo,
        bloqueadoEm: admin.firestore.FieldValue.serverTimestamp(),
        bloqueadoPor: adminUser.email,
        atualizadoEm: admin.firestore.FieldValue.serverTimestamp(),
    });
    await registrarAuditoria({
        tipoAcao: "BLOQUEIO",
        licencaId,
        codigoLicenca: licData.codigoFormatado,
        clienteId: licData.clienteId,
        clienteNome: licData.clienteNome,
        responsavel: adminUser.email,
        detalhes: { motivo },
    });
    return { sucesso: true };
});
// Desbloquear licença
exports.adminUnblockLicense = functions
    .region("southamerica-east1")
    .https.onCall(async (data, context) => {
    const adminUser = await verificarPermissaoAdmin(context);
    const { licencaId } = data;
    if (!licencaId) {
        throw new functions.https.HttpsError("invalid-argument", "ID da licença é obrigatório.");
    }
    const licRef = db.collection("licencas").doc(licencaId);
    const licDoc = await licRef.get();
    if (!licDoc.exists) {
        throw new functions.https.HttpsError("not-found", "Licença não encontrada.");
    }
    const licData = licDoc.data();
    // Avalia se já venceu
    const venc = new Date(licData.dataVencimento);
    const status = venc < new Date() ? "VENCIDA" : "ATIVA";
    await licRef.update({
        status,
        ativa: status === "ATIVA",
        motivoBloqueio: null,
        bloqueadoEm: null,
        bloqueadoPor: null,
        atualizadoEm: admin.firestore.FieldValue.serverTimestamp(),
    });
    await registrarAuditoria({
        tipoAcao: "DESBLOQUEIO",
        licencaId,
        codigoLicenca: licData.codigoFormatado,
        clienteId: licData.clienteId,
        clienteNome: licData.clienteNome,
        responsavel: adminUser.email,
        detalhes: { novoStatus: status },
    });
    return { sucesso: true, novoStatus: status };
});
// Cancelar licença definitivamente
exports.adminCancelLicense = functions
    .region("southamerica-east1")
    .https.onCall(async (data, context) => {
    const adminUser = await verificarPermissaoAdmin(context);
    const { licencaId, motivo } = data;
    if (!licencaId) {
        throw new functions.https.HttpsError("invalid-argument", "ID da licença é obrigatório.");
    }
    const licRef = db.collection("licencas").doc(licencaId);
    const licDoc = await licRef.get();
    if (!licDoc.exists) {
        throw new functions.https.HttpsError("not-found", "Licença não encontrada.");
    }
    const licData = licDoc.data();
    await licRef.update({
        status: "CANCELADA",
        ativa: false,
        canceladoEm: admin.firestore.FieldValue.serverTimestamp(),
        canceladoPor: adminUser.email,
        motivoCancelamento: motivo || "Cancelada pelo administrador",
        atualizadoEm: admin.firestore.FieldValue.serverTimestamp(),
    });
    await registrarAuditoria({
        tipoAcao: "CANCELAMENTO",
        licencaId,
        codigoLicenca: licData.codigoFormatado,
        clienteId: licData.clienteId,
        clienteNome: licData.clienteNome,
        responsavel: adminUser.email,
        detalhes: { motivo: motivo || null },
    });
    return { sucesso: true };
});
// Remover dispositivo vinculado (para liberar computador)
exports.adminRemoveDevice = functions
    .region("southamerica-east1")
    .https.onCall(async (data, context) => {
    const adminUser = await verificarPermissaoAdmin(context);
    const { dispositivoDocId, licencaId } = data;
    if (!dispositivoDocId || !licencaId) {
        throw new functions.https.HttpsError("invalid-argument", "IDs de dispositivo e licença são obrigatórios.");
    }
    const dispRef = db.collection("dispositivos").doc(dispositivoDocId);
    const dispDoc = await dispRef.get();
    if (!dispDoc.exists) {
        throw new functions.https.HttpsError("not-found", "Dispositivo não encontrado.");
    }
    const dispData = dispDoc.data();
    await dispRef.delete();
    // Também limpa o campo legado `maquina` da licença se coincidir
    const licRef = db.collection("licencas").doc(licencaId);
    const licDoc = await licRef.get();
    if (licDoc.exists) {
        const licData = licDoc.data();
        if (licData.maquina === dispData.identificadorDispositivo) {
            await licRef.update({
                maquina: "",
                atualizadoEm: admin.firestore.FieldValue.serverTimestamp(),
            });
        }
    }
    await registrarAuditoria({
        tipoAcao: "REMOCAO_DISPOSITIVO",
        licencaId,
        codigoLicenca: licDoc.exists ? licDoc.data()?.codigoFormatado : null,
        clienteId: dispData.clienteId,
        responsavel: adminUser.email,
        detalhes: {
            identificadorDispositivo: dispData.identificadorDispositivo,
            nomeDispositivo: dispData.nomeDispositivo,
        },
    });
    return { sucesso: true };
});
// Configurar custom claim de administrador
exports.setAdminClaim = functions
    .region("southamerica-east1")
    .https.onCall(async (data, context) => {
    // Permitido se já for admin, OU se a coleção `admins` estiver completamente vazia (bootstrap inicial)
    const { targetEmail } = data;
    if (!targetEmail || typeof targetEmail !== "string") {
        throw new functions.https.HttpsError("invalid-argument", "Email alvo não informado.");
    }
    const emailNormalizado = targetEmail.trim().toLowerCase();
    const adminsSnapshot = await db.collection("admins").get();
    const isFirstAdmin = adminsSnapshot.empty;
    if (!isFirstAdmin) {
        await verificarPermissaoAdmin(context);
    }
    // Busca o usuário no Firebase Auth
    let userRecord;
    try {
        userRecord = await admin.auth().getUserByEmail(emailNormalizado);
    }
    catch (err) {
        throw new functions.https.HttpsError("not-found", `Usuário ${emailNormalizado} não encontrado no Firebase Authentication.`);
    }
    // Define custom claim
    await admin.auth().setCustomUserClaims(userRecord.uid, { admin: true });
    // Salva na coleção de admins
    await db.collection("admins").doc(emailNormalizado).set({
        email: emailNormalizado,
        uid: userRecord.uid,
        ativo: true,
        role: "superadmin",
        criadoEm: admin.firestore.FieldValue.serverTimestamp(),
    });
    await registrarAuditoria({
        tipoAcao: "CRIACAO_ADMINISTRADOR",
        responsavel: context.auth?.token.email || "Bootstrap Inicial",
        detalhes: { email: emailNormalizado, uid: userRecord.uid },
    });
    return {
        sucesso: true,
        mensagem: `Usuário ${emailNormalizado} promovido a Administrador com sucesso.`,
    };
});
//# sourceMappingURL=index.js.map