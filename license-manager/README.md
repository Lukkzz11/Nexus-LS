# Nexus LS — License Manager

Sistema Web Administrativo corporativo para gestão centralizada de licenças comerciais do ecossistema **Nexus LS**, controle rigoroso de computadores autorizados e integração com o Atualizador.

---

## 1. Visão Geral da Arquitetura

O sistema é construído sobre o projeto Firebase existente **`nexus-ls`**:

```
                              [ PAINEL ADMINISTRATIVO ]
                           (React + TypeScript + Tailwind)
                                      |
                     Firebase Auth (Admin Custom Claims)
                                      |
                                      v
  [ CLOUD FUNCTIONS ] <---> [ CLOUD FIRESTORE ] <---> [ ATUALIZADOR NEXUS LS ]
  - validateLicense            - clientes               - FonteLicencaCloudFunction
  - adminGenerateLicense       - licencas               - Identificador de máquina
  - adminRenewLicense          - dispositivos           - Tolerância offline (7d)
  - adminBlockLicense          - historico_auditoria
  - adminCancelLicense         - admins
  - adminRemoveDevice
```

---

## 2. Coleções do Firestore

1. **`clientes`**:
   - `id`: Identificador único (ex: `CLI_173000...`)
   - `nomeResponsavel`: Nome do responsável legal / comercial
   - `empresa`: Nome da empresa ou loja contratante
   - `documento`: CPF ou CNPJ
   - `telefone`: WhatsApp / telefone comercial
   - `email`: E-mail de contato
   - `observacoes`: Anotações administrativas
   - `totalLicencas`: Quantidade de licenças contratadas

2. **`licencas`**:
   - `id`: Document ID (e chave formatada legada `NEXUS-XXXX-XXXX-XXXX`)
   - `codigoFormatado`: Chave amigável para digitação do cliente
   - `hashChave`: Hash criptográfico SHA-256 da chave normalizada
   - `clienteId`: Referência ao cliente
   - `clienteNome`: Nome do cliente
   - `empresa`: Razão social ou nome fantasia
   - `plano`: `mensal` | `trimestral` | `semestral` | `anual` | `personalizado`
   - `dataInicio`: Data de início de vigência (YYYY-MM-DD)
   - `dataVencimento`: Data de expiração (YYYY-MM-DD)
   - `limiteDispositivos`: Quantidade máxima de computadores simultâneos
   - `status`: `ATIVA` | `VENCENDO` | `VENCIDA` | `BLOQUEADA` | `CANCELADA` | `PENDENTE`
   - `motivoBloqueio`: Motivo caso suspensa
   - `bloqueadoEm` / `bloqueadoPor`: Auditoria de suspensão
   - `cliente`, `validade`, `ativa`, `maquina`: Campos para retrocompatibilidade com versões legadas

3. **`dispositivos`**:
   - `id`: `{licencaId}_{identificadorDispositivo}`
   - `licencaId`: ID da licença vinculada
   - `identificadorDispositivo`: Hardware fingerprint gerado pelo `LicencaService.CodigoDaMaquina()`
   - `nomeDispositivo`: Nome da máquina (`Environment.MachineName`)
   - `ativo`: Booleano indicando se a máquina ocupa a vaga
   - `primeiraAtivacao`: Timestamp de vinculação
   - `ultimaVerificacao`: Timestamp da última checagem online feita pelo Atualizador
   - `sistema`: Identificador do produto (`NexusLS`)

4. **`historico_auditoria`**:
   - Trilha imutável de eventos: `CRIACAO_LICENCA`, `ATIVACAO_DISPOSITIVO`, `RENOVACAO`, `BLOQUEIO`, `DESBLOQUEIO`, `CANCELAMENTO`, `REMOCAO_DISPOSITIVO`, `CRIACAO_CLIENTE`.

5. **`admins`**:
   - Registros de e-mails com permissões administrativas verificadas pelo backend.

---

## 3. Endpoints e Cloud Functions

### Endpoint HTTP do Atualizador (`validateLicense`)
- **URL**: `https://southamerica-east1-nexus-ls.cloudfunctions.net/validateLicense`
- **Método**: `POST`
- **Payload de Entrada**:
  ```json
  {
    "chave": "NEXUS-8K29-XP41-MZ73",
    "dispositivoId": "A1B2-C3D4-E5F6",
    "nomeDispositivo": "Caixa 01",
    "sistema": "NexusLS"
  }
  ```
- **Resposta Autorizada (HTTP 200)**:
  ```json
  {
    "autorizado": true,
    "situacao": "ATIVA",
    "codigoLicenca": "NEXUS-8K29-XP41-MZ73",
    "cliente": "Lucas Emanuel",
    "empresa": "Life Surf",
    "dataInicio": "2026-11-06",
    "dataVencimento": "2027-11-06",
    "diasRestantes": 365,
    "identificadorDispositivo": "A1B2-C3D4-E5F6",
    "dispositivoAutorizado": "A1B2-C3D4-E5F6",
    "limiteDispositivos": 2,
    "dispositivosAtivos": 1,
    "mensagem": "Licença válida até 06/11/2027."
  }
  ```
- **Respostas de Recusa**:
  - `404 NAO_ENCONTRADA`: Chave não existe no sistema.
  - `403 BLOQUEADA`: Licença suspensa administrativamente com motivo.
  - `403 CANCELADA`: Licença cancelada definitivamente.
  - `403 VENCIDA`: Período expirado calculado com base na hora do servidor.
  - `403 LIMITE_ATINGIDO`: Quantidade de computadores autorizados excedida.

---

## 4. Como Configurar seu Usuário Administrador

O sistema conta com proteção rigorosa:
1. No [Firebase Console](https://console.firebase.google.com/project/nexus-ls/authentication):
   - Vá em **Authentication > Users** e certifique-se de que sua conta de e-mail e senha está criada (ou crie uma).
2. Na primeira inicialização:
   - A coleção `admins` começa vazia. Ao fazer login com suas credenciais do Firebase, o sistema reconhece o primeiro operador e o registra como Administrador Geral (`superadmin`).
   - Se desejar definir manualmente via Custom Claims, use a função administrativa `setAdminClaim`.

---

## 5. Publicação no Firebase

No diretório `C:\NexusLS\license-manager`:

```bash
# 1. Publicar Regras do Firestore
firebase deploy --only firestore:rules

# 2. Publicar Cloud Functions
firebase deploy --only functions

# 3. Compilar e Publicar o Painel Web
npm run build
firebase deploy --only hosting
```

---

## 6. Como Executar Localmente em Desenvolvimento

```bash
cd C:\NexusLS\license-manager
npm run dev
```

Acesse `http://localhost:5173` no navegador.
