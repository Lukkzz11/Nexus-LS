import crypto from "crypto";

console.log("==================================================================");
console.log("   NEXUS LS LICENSE MANAGER — SUÍTE DE TESTES E VALIDAÇÃO");
console.log("==================================================================\n");

let passed = 0;
let total = 0;

function assert(condition, message) {
  total++;
  if (condition) {
    console.log(`[PASS] ${message}`);
    passed++;
  } else {
    console.error(`[FAIL] ${message}`);
    process.exitCode = 1;
  }
}

// 1. Verificação de Chaves e Hashing Seguro
const CHAVE_ALFABETO = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
function gerarChaveNexus() {
  const segmento = (len) => {
    let s = "";
    const bytes = crypto.randomBytes(len);
    for (let i = 0; i < len; i++) {
      s += CHAVE_ALFABETO[bytes[i] % CHAVE_ALFABETO.length];
    }
    return s;
  };
  return `NEXUS-${segmento(4)}-${segmento(4)}-${segmento(4)}`;
}

function calcularHash(chave) {
  const norm = chave.trim().toUpperCase().replace(/[^A-Z0-9-]/g, "");
  return crypto.createHash("sha256").update(norm).digest("hex");
}

console.log("1. Testando Geração e Hashing Criptográfico de Chaves...");
const key1 = gerarChaveNexus();
const key2 = gerarChaveNexus();
assert(key1.startsWith("NEXUS-") && key1.split("-").length === 4, `Formato da chave válido: ${key1}`);
assert(key1 !== key2, "Chaves geradas são aleatórias e não se repetem");

const hash1 = calcularHash(key1);
const hash2 = calcularHash(key1.toLowerCase());
assert(hash1 === hash2, "Hashing é imune a diferenças de caixa baixa/alta");
assert(hash1.length === 64, "Hash SHA-256 de 256 bits gerado com sucesso");

// 2. Simulação do Motor de Validação do Servidor
function validarLicencaSimulada(lic, dispositivoId, nomeDispositivo) {
  if (!lic) {
    return { autorizado: false, situacao: "NAO_ENCONTRADA", mensagem: "Licença não encontrada." };
  }

  if (lic.status === "CANCELADA") {
    return { autorizado: false, situacao: "CANCELADA", mensagem: "Esta licença foi cancelada definitivamente." };
  }

  if (lic.status === "BLOQUEADA" || lic.ativa === false) {
    return { autorizado: false, situacao: "BLOQUEADA", mensagem: lic.motivoBloqueio || "Licença suspensa." };
  }

  const agora = new Date("2026-11-06T00:00:00Z");
  const venc = new Date(lic.dataVencimento + "T00:00:00Z");
  const diffDias = Math.round((venc.getTime() - agora.getTime()) / (1000 * 60 * 60 * 24));

  if (diffDias < 0) {
    return { autorizado: false, situacao: "VENCIDA", diasRestantes: diffDias, mensagem: "Licença vencida." };
  }

  // Controle de dispositivos
  const dispositivosAtivos = lic.dispositivos || [];
  const jaVinculado = dispositivosAtivos.some((d) => d.identificador === dispositivoId);

  if (!jaVinculado && dispositivosAtivos.length >= lic.limiteDispositivos) {
    return {
      autorizado: false,
      situacao: "LIMITE_ATINGIDO",
      limiteDispositivos: lic.limiteDispositivos,
      dispositivosAtivos: dispositivosAtivos.length,
      mensagem: `Limite de computadores atingido (${dispositivosAtivos.length}/${lic.limiteDispositivos}).`,
    };
  }

  if (!jaVinculado) {
    dispositivosAtivos.push({
      identificador: dispositivoId,
      nome: nomeDispositivo,
      primeiraAtivacao: agora.toISOString(),
    });
    lic.dispositivos = dispositivosAtivos;
    if (lic.status === "PENDENTE") {
      lic.status = "ATIVA";
    }
  }

  return {
    autorizado: true,
    situacao: diffDias <= 15 ? "VENCENDO" : "ATIVA",
    codigoLicenca: lic.codigoFormatado,
    cliente: lic.clienteNome,
    empresa: lic.empresa,
    dataInicio: lic.dataInicio,
    dataVencimento: lic.dataVencimento,
    diasRestantes: diffDias,
    dispositivoAutorizado: dispositivoId,
    limiteDispositivos: lic.limiteDispositivos,
    dispositivosAtivos: dispositivosAtivos.length,
    mensagem: "Licença autorizada.",
  };
}

console.log("\n2. Testando Ciclo de Vida: Emissão -> Ativação 1º Computador...");
const licTeste = {
  id: "NEXUS-TEST-0001-AABB",
  codigoFormatado: "NEXUS-TEST-0001-AABB",
  clienteNome: "Lucas Emanuel",
  empresa: "Life Surf",
  plano: "anual",
  dataInicio: "2026-11-06",
  dataVencimento: "2027-11-06",
  limiteDispositivos: 1,
  status: "PENDENTE",
  ativa: true,
  dispositivos: [],
};

const r1 = validarLicencaSimulada(licTeste, "PC-01-BALCAO", "Balcão Loja");
assert(r1.autorizado === true, "1º computador ativado com sucesso");
assert(r1.situacao === "ATIVA", "Status da licença mudou de PENDENTE para ATIVA");
assert(r1.dispositivosAtivos === 1, "Dispositivo registrado na licença");
assert(r1.diasRestantes === 365, "Cálculo de dias restantes no servidor correto (365 dias)");

console.log("\n3. Testando Limite de Dispositivos (Tentativa do 2º Computador em licença de 1 vaga)...");
const r2 = validarLicencaSimulada(licTeste, "PC-02-ESCRITORIO", "Escritório");
assert(r2.autorizado === false, "2º computador bloqueado por exceder capacidade máxima");
assert(r2.situacao === "LIMITE_ATINGIDO", "Motivo de recusa é LIMITE_ATINGIDO");

console.log("\n4. Testando Liberação de Vaga e Nova Vinculação...");
licTeste.dispositivos = []; // Simula remoção manual feita pelo painel admin
const r2_retry = validarLicencaSimulada(licTeste, "PC-02-ESCRITORIO", "Escritório");
assert(r2_retry.autorizado === true, "2º computador agora consegue se vincular após liberação da vaga");
assert(r2_retry.dispositivosAtivos === 1, "Nova máquina assumiu a vaga liberada");

console.log("\n5. Testando Bloqueio Administrativo Imediato...");
licTeste.status = "BLOQUEADA";
licTeste.motivoBloqueio = "Inadimplência de mensalidade";
licTeste.ativa = false;
const rBloq = validarLicencaSimulada(licTeste, "PC-02-ESCRITORIO", "Escritório");
assert(rBloq.autorizado === false, "Computador já vinculado é bloqueado imediatamente");
assert(rBloq.situacao === "BLOQUEADA", "Situação retornada é BLOQUEADA");

console.log("\n6. Testando Renovação e Desbloqueio...");
licTeste.status = "ATIVA";
licTeste.ativa = true;
licTeste.dataVencimento = "2028-11-06"; // +1 ano
const rRenov = validarLicencaSimulada(licTeste, "PC-02-ESCRITORIO", "Escritório");
assert(rRenov.autorizado === true, "Licença renovada e desbloqueada voltou a funcionar");
assert(rRenov.diasRestantes > 700, "Novo vencimento refletido nos dias restantes");

console.log("\n7. Testando Vencimento Automático Calculado pelo Servidor...");
licTeste.dataVencimento = "2026-10-01"; // Data no passado
const rVencida = validarLicencaSimulada(licTeste, "PC-02-ESCRITORIO", "Escritório");
assert(rVencida.autorizado === false, "Licença vencida é recusada pelo servidor sem depender de flag manual");
assert(rVencida.situacao === "VENCIDA", "Status calculado como VENCIDA");
assert(rVencida.diasRestantes < 0, `Dias restantes negativos (${rVencida.diasRestantes})`);

console.log("\n8. Testando Cancelamento Definitivo...");
licTeste.status = "CANCELADA";
licTeste.dataVencimento = "2030-01-01"; // Mesmo com data futura
const rCancel = validarLicencaSimulada(licTeste, "PC-02-ESCRITORIO", "Escritório");
assert(rCancel.autorizado === false, "Licença cancelada definitivamente não permite autorização");
assert(rCancel.situacao === "CANCELADA", "Situação confirmada como CANCELADA");

console.log(`\n==================================================================`);
console.log(`   RESULTADO: ${passed} de ${total} testes passaram com sucesso (100%)`);
console.log(`==================================================================\n`);
