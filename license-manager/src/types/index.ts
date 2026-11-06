export type StatusLicenca = 'ATIVA' | 'VENCENDO' | 'VENCIDA' | 'BLOQUEADA' | 'CANCELADA' | 'PENDENTE';

export type PlanoLicenca = 'mensal' | 'trimestral' | 'semestral' | 'anual' | 'personalizado';

export interface Cliente {
  id: string;
  nomeResponsavel: string;
  empresa: string;
  documento?: string; // CPF ou CNPJ
  telefone?: string;
  email: string;
  observacoes?: string;
  totalLicencas?: number;
  criadoEm?: any;
  atualizadoEm?: any;
  criadoPor?: string;
}

export interface Licenca {
  id: string; // Chave ou ID do documento
  hashChave: string;
  codigoFormatado: string;
  clienteId: string;
  clienteNome: string;
  empresa: string;
  plano: PlanoLicenca;
  dataInicio: string; // YYYY-MM-DD
  dataVencimento: string; // YYYY-MM-DD
  limiteDispositivos: number;
  status: StatusLicenca;
  motivoBloqueio?: string | null;
  bloqueadoEm?: any;
  bloqueadoPor?: string | null;
  canceladoEm?: any;
  canceladoPor?: string | null;
  motivoCancelamento?: string;
  observacoes?: string;
  criadoEm?: any;
  atualizadoEm?: any;
  criadoPor?: string;

  // Campos legados para compatibilidade com o atualizador existente
  cliente?: string;
  validade?: string;
  ativa?: boolean;
  maquina?: string;

  // Calculados em tempo real na exibição
  diasRestantes?: number;
  dispositivosAtivosCount?: number;
}

export interface DispositivoVinculado {
  id: string;
  licencaId: string;
  clienteId: string;
  identificadorDispositivo: string;
  nomeDispositivo: string;
  ativo: boolean;
  primeiraAtivacao: any;
  ultimaVerificacao: any;
  ipUltimoAcesso?: string;
  sistema?: string;
}

export type TipoAcaoAuditoria =
  | 'CRIACAO_LICENCA'
  | 'ATIVACAO_DISPOSITIVO'
  | 'RENOVACAO'
  | 'BLOQUEIO'
  | 'DESBLOQUEIO'
  | 'CANCELAMENTO'
  | 'REMOCAO_DISPOSITIVO'
  | 'CRIACAO_CLIENTE'
  | 'EDICAO_CLIENTE'
  | 'CRIACAO_ADMINISTRADOR';

export interface HistoricoAuditoria {
  id: string;
  tipoAcao: TipoAcaoAuditoria;
  licencaId?: string | null;
  codigoLicenca?: string | null;
  clienteId?: string | null;
  clienteNome?: string | null;
  responsavel: string;
  detalhes: Record<string, any>;
  dataHora: any;
}

export interface AdminUser {
  email: string;
  uid: string;
  role: 'superadmin' | 'admin';
  ativo: boolean;
  criadoEm?: any;
}

export interface RespostaValidacaoAtualizador {
  autorizado: boolean;
  situacao: 'ATIVA' | 'VENCENDO' | 'VENCIDA' | 'BLOQUEADA' | 'CANCELADA' | 'LIMITE_ATINGIDO' | 'NAO_ENCONTRADA' | 'ERRO';
  codigoLicenca?: string;
  cliente?: string;
  empresa?: string;
  dataInicio?: string;
  dataVencimento?: string;
  diasRestantes?: number;
  identificadorDispositivo?: string;
  dispositivoAutorizado?: string;
  limiteDispositivos?: number;
  dispositivosAtivos?: number;
  mensagem: string;
}
