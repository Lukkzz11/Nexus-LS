/**
 * Localização padrão do sistema (loja em Caruaru, Pernambuco, Brasil).
 * Só vale quando a empresa ainda não cadastrou o próprio endereço: dados digitados em
 * Configurações → Dados da Loja sempre têm prioridade sobre estes valores.
 */
export const CIDADE_PADRAO = "Caruaru";
export const ESTADO_PADRAO = "PE";
export const CIDADE_UF_PADRAO = `${CIDADE_PADRAO} - ${ESTADO_PADRAO}`;
export const PAIS_PADRAO = "Brasil";
/** Pernambuco usa o mesmo horário de Brasília (UTC-3), sem horário de verão. */
export const FUSO_HORARIO_PADRAO = "America/Recife";
