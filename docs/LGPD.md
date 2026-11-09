# LGPD no Nexus LS

Este documento é um **inventário técnico e um plano de adequação**, não um parecer jurídico. As decisões de base legal, prazos e comunicação de incidentes devem ser confirmadas pelo responsável da loja com contador e advogado. Ele descreve o que o programa faz **hoje** e deixa explícito o que ainda **não** está implementado.

## 1. Papéis

| Papel | Quem é no Nexus LS |
|---|---|
| **Controlador** | O lojista (dono da loja). É quem decide por que e como os dados pessoais são tratados. |
| **Operador** | Quem fornece e mantém o programa Nexus LS, tratando dados apenas conforme a finalidade da loja. |
| **Suboperadores** | Google (Google Drive, quando o backup na nuvem é ligado) e Google/Firebase (Authentication, Firestore e Storage dos projetos usados pela loja). |

O programa é de **uma loja só** e funciona localmente. Não há multiempresa nem compartilhamento de dados entre lojas.

## 2. Inventário de dados pessoais

### 2.1 Banco local — `%LOCALAPPDATA%\NexusLS\nexus.db`

Arquivo SQLite único, sem criptografia, acessível a qualquer processo do mesmo usuário do Windows.

| Grupo | Dados guardados | Finalidade |
|---|---|---|
| **Clientes** | Nome, razão social, CPF/CNPJ, inscrição estadual, telefone, e-mail, endereço completo (CEP, rua, número, complemento, bairro, cidade, UF), observações, limite de crédito, crédito a favor, data de cadastro, histórico de compras, dívidas, pagamentos e ajustes de título | Vender, entregar, cobrar, conceder crédito e cumprir obrigações fiscais/consumeristas |
| **Fornecedores** | Nome, CNPJ/CPF, telefone, e-mail, observações | Compras, contas a pagar e contato comercial |
| **Funcionários** | Nome, perfil/permissões, PIN guardado só como hash PBKDF2 com sal, hash do código de recuperação, data de cadastro, registros de quem fez cada ação | Login, autorização por perfil e trilha de responsabilidade |
| **Vendas e caixa** | Itens, valores, descontos, forma de pagamento, parcelas, operador, cliente vinculado, cancelamentos (motivo, quem e quando), turnos, sangrias, reforços, fechamentos | Operação do PDV, conferência e histórico |
| **Financeiro** | Contas a pagar/receber, contraparte, categoria, valores, vencimentos, pagamentos, estornos, contas bancárias, transferências, cartões, cheques e taxas | Gestão do dinheiro e obrigações fiscais |
| **Compras e pedidos** | Fornecedor, nota, itens, custos, pedido, etapa, histórico e responsável | Reposição de estoque e atendimento |
| **Configuração da loja** | Nome, razão social, CNPJ, endereço, telefone, e-mail, inscrição estadual, chave Pix, dados do cupom | Cupom, documentos e identificação da loja |
| **Atividades** | Quem fez o quê e quando (cancelamentos, ajustes, cadastros, troca de PIN etc.) | Segurança, auditoria e prevenção a fraude |
| **Produtos** | Nome, referência, categoria, código de barras, preços, custo, estoque e tamanhos | Estoque e venda (normalmente não é dado pessoal) |

### 2.2 Fotos e arquivos locais

- Fotos de produtos ficam em `%LOCALAPPDATA%\NexusLS\fotos` (arquivo local; a foto enviada ao catálogo ganha uma URL pública no Firebase Storage).
- A venda em andamento fica em `%LOCALAPPDATA%\NexusLS\rascunho.json` (itens do carrinho e o id interno do cliente escolhido; não guarda CPF nem nome).
- Preferências e configurações ficam em arquivos JSON na mesma pasta (por exemplo, `backup.json`, `google.json`, `sessao.json`, `catalogo-produtos.json`).
- **Segredos** (senha do Firebase, refresh token do Google/Firebase e senha do catálogo) são protegidos com **DPAPI** do Windows; o PIN nunca é gravado, só o hash PBKDF2 com sal.

### 2.3 Backups locais

- Pasta escolhida em Configurações > Backup (padrão `Documentos\Nexus LS\Backups`).
- Cada backup é uma **cópia integral** do `nexus.db`, com nome `nexus_AAAA-MM-DD_HHMMSS_motivo.db`.
- Podem ficar em pen drive, HD externo ou pasta sincronizada com a nuvem (OneDrive, Google Drive etc.).
- **Sem criptografia.** Quem tiver o arquivo abre o banco e lê tudo. Os backups de segurança (`pre-atualizacao`, `pre-restauracao`, `pre-limpeza`) não são apagados sozinhos.

### 2.4 Backup no Google Drive (opcional)

- Envia a mesma cópia integral do banco para a pasta `Nexus LS Backups` do Drive da conta conectada.
- O acesso usa OAuth com escopo `drive.file` (só os arquivos criados pelo próprio programa).
- O refresh token e o Client Secret ficam em `%LOCALAPPDATA%\NexusLS\google.json`, protegidos por DPAPI.
- **O arquivo enviado não é criptografado pelo Nexus LS.** A proteção é a da conta Google e a do provedor.

### 2.5 Firebase operacional (projeto B, privado)

Configurado em Catálogo online > Nuvem operacional. Recebe, pela fila de sincronização, documentos em `lojas/{loja}/{coleção}/{guid}`:

- vendas (com itens, operador, cliente vinculado, valores, cancelamento);
- turnos, movimentos e fechamentos de caixa;
- pagamentos de cliente, lançamentos, contas, transferências, liquidações de cartão e cheques;
- **cadastro de clientes**: nome, CPF/CNPJ, telefone, endereço completo, observação, limite de crédito, inscrição estadual e situação;
- fornecedores (CNPJ/CPF, telefone, e-mail), compras, pedidos e dados de produção.

As regras atuais (`firebase/firestore.operacional.rules`) liberam leitura e gravação apenas para uma **lista fixa de e-mails** autenticados, exigem os campos `origem = "nexus-ls"` e `guid`, e proíbem exclusão. Nada é público. Não há separação por loja além do caminho, nem permissões diferentes por pessoa.

### 2.6 Catálogo público (projeto A)

- Firestore: `produtos/{guid}` e `meta/catalogo`; a vitrine também grava `meta/vitrine` (textos, imagens e dados de contato da loja).
- Campos publicados do produto: nome, categoria, referência, código de barras, preço de varejo, estoque por tamanho, situação, hash e URL da foto. **Não** vão custo, preço de atacado, vendas, clientes ou dívidas.
- A leitura de `produtos` e `meta` é **pública** (visitante sem login); a gravação é restrita à lista de e-mails da licença/usuários.
- A URL da foto no Firebase Storage é pública (contém um token de download gerado pelo Storage).
- Dados de contato da loja (WhatsApp, telefone, endereço, Instagram) podem aparecer na vitrine. Se uma foto ou texto incluir uma pessoa identificável, ela só deve ser publicada com autorização.

### 2.7 Firebase do ERP (somente leitura)

A importação do ERP lê, sem gravar nada no ERP, as coleções de produtos, clientes, turnos, vendas, contas a receber, pedidos, transações financeiras e produção. Dos clientes vêm nome, documento, telefone, cidade, estado, endereço, inscrição estadual e limite de crédito; das vendas vêm itens, valores, pagamentos e vínculo com cliente. Esses dados passam a existir **neste computador** e podem depois seguir para o projeto operacional, conforme a fila.

### 2.8 Resumo: onde cada dado aparece

| Dado | Banco local | Backup local | Google Drive | Firebase B (operacional) | Catálogo público (A) |
|---|---|---|---|---|---|
| CPF/CNPJ de cliente | Sim | Sim | Se o backup no Drive estiver ligado | Sim (cadastro de clientes) | Não |
| Telefone/e-mail/endereço | Sim | Sim | Se ligado | Sim | Só o contato da loja na vitrine |
| Dívidas e pagamentos | Sim | Sim | Se ligado | Sim | Não |
| Faturamento, caixa e financeiro | Sim | Sim | Se ligado | Sim | Não |
| Nome do operador | Sim | Sim | Se ligado | Sim (vendas/caixa) | Não |
| PIN | Só hash PBKDF2 | Só hash | Só hash | Não | Não |
| Produtos e estoque | Sim | Sim | Se ligado | Sim (compras/vendas) | Sim, campos públicos |
| Fotos de produto | Sim | Não (ficam fora do .db) | Não | Não | Sim, no Storage |

## 3. Base legal e finalidade

Sugestão para validação jurídica (LGPD, art. 7º):

| Tratamento | Base legal sugerida | Observação |
|---|---|---|
| Cadastro, venda, entrega, cobrança e crédito | Execução de contrato (inciso V) e legítimo interesse (inciso IX) | O CPF/CNPJ e o endereço são necessários para a operação e para a cobrança; evite pedir o que não usa. |
| Emissão fiscal, guarda de documentos e obrigações contábeis | Obrigação legal ou regulatória (inciso II) | Prazos definidos pela legislação fiscal/contábil; confirmar com o contador. |
| Prevenção a fraude, segurança e trilha de atividades | Legítimo interesse (inciso IX) | Deve ser proporcional e transparente; não usar para vigilância indiscriminada. |
| Catálogo público e divulgação da loja | Legítimo interesse ou consentimento | Se houver foto de pessoa identificável, use consentimento específico. |
| Marketing, WhatsApp e novidades | Consentimento (inciso I) | Precisa de opt-in e de um caminho fácil de revogar. |
| Funcionários (login, permissões e registro de ações) | Execução de contrato e obrigação legal | O PIN é credencial; o hash não permite recuperar o PIN. |

O programa **não trata dados sensíveis** (art. 5º, II) de forma estruturada. Se o lojista passar a registrar saúde, biometria, religião etc., isso exige base legal específica e revisão deste documento.

## 4. Retenção sugerida

Não há expurgo automático hoje. Os prazos abaixo são uma **sugestão** a ser validada com o contador/advogado; enquanto não houver rotina, o dado fica para sempre.

| Dado | Retenção sugerida | Motivo |
|---|---|---|
| Vendas, cupons, financeiro e caixa | 5 anos após o encerramento do exercício | Guarda fiscal, contábil e defesa em processo |
| Dívidas e pagamentos de clientes | Enquanto a dívida existir + 5 anos após a quitação | Cobrança e prescrição |
| Cadastro de cliente e histórico | Enquanto o cliente estiver ativo + 5 anos após a última compra | Relacionamento, garantia e defesa |
| Fornecedores e compras | Enquanto a relação existir + 5 anos | Obrigação fiscal e comercial |
| Funcionários | Durante o vínculo + prazo trabalhista aplicável (sugestão: 5 anos após o desligamento) | Obrigações trabalhistas e previdenciárias |
| Trilha de atividades/segurança | 6 meses a 5 anos, conforme o risco | Marco Civil da Internet e prevenção a fraude |
| Backups comuns | 30 a 90 dias (rotação) | Recuperação operacional |
| Backups de segurança | Revisar anualmente; manter enquanto houver obrigação legal ou incidente | Não são apagados sozinhos |
| Catálogo público | Enquanto o produto estiver publicado | Não contém dados pessoais do cliente |
| Consentimento de marketing | Até a revogação | O consentimento pode ser retirado a qualquer momento |

**Não implementado:** rotina automática de expurgo, bloqueio, anonimização, "legal hold" e registro de descarte. Isso está no plano da seção 8.

## 5. Direitos do titular

| Direito | Como atender hoje | Lacuna |
|---|---|---|
| Confirmação e acesso | Consultar o cliente na tela Clientes; exportar vendas/financeiro/estoque em CSV | Não existe um "pacote do titular" com todos os dados dele em um clique |
| Correção | Editar o cadastro na tela Clientes | O histórico de vendas guarda o nome/preço da época; corrigir o cadastro não reescreve o passado |
| Portabilidade | CSV das telas de relatórios e financeiro | Formatos não são padronizados para outro sistema |
| Eliminação | Desativar o cliente e, se não houver obrigação legal, avaliar expurgo administrativo no banco | O fluxo normal **não apaga** vendas/financeiro; só a tela **Apagar dados** (administrador, com backup e digitação de `APAGAR`) remove o movimento, e isso não é uma exclusão por titular. A guarda fiscal também limita o prazo |
| Revogação de consentimento | Despublicar produto/vitrine; retirar contato de marketing | Não há tela de consentimento nem histórico de opt-in/opt-out |
| Informação sobre compartilhamento | Este documento e as telas de configuração | Falta um aviso de privacidade curto no programa/site |

**Procedimento sugerido:** o lojista define um canal (e-mail/WhatsApp), registra o pedido, confirma a identidade do titular, responde em até **15 dias** (art. 19) e documenta a decisão. Se o dado não puder ser apagado por obrigação legal, informe o motivo e ofereça bloqueio/anonimização quando tecnicamente possível.

**Não implementado:** formulário/tela de direitos, exportação completa por titular, anonimização assistida e registro de solicitações.

## 6. Resposta a incidente

1. **Detectar e registrar:** data, hora, o que foi exposto, quantas pessoas, como descobriu.
2. **Conter:** desconectar o equipamento da rede, trocar senhas do Firebase/Google, revogar usuários e refresh tokens, desligar o backup no Drive se a conta foi comprometida.
3. **Avaliar o risco:** dados de clientes (CPF, endereço, dívidas) e credenciais são alto risco; catálogo público sem dados pessoais tende a risco baixo.
4. **Preservar evidências:** não apagar logs, arquivos ou o banco; guardar cópia do estado.
5. **Comunicar:** se houver risco relevante, comunicar a **ANPD** e os titulares afetados (art. 48), em linguagem clara, com o que aconteceu, quais dados, medidas e canal de contato. Sugestão interna: decisão em até **3 dias úteis** e comunicação no menor prazo razoável.
6. **Corrigir e aprender:** registrar a causa, atualizar regras, treinar a equipe e revisar este documento.

Cenários para ensaiar: notebook/Pen drive perdido, conta Google comprometida, usuário do Firebase removido/recriado, vazamento de CSV, ransomware e venda de computador sem apagar o perfil do Windows. **Não implementado:** plano de resposta formal, plantão, modelo de comunicado e registro de incidentes.

## 7. Riscos atuais (o que ainda falta)

1. **Banco e backups sem criptografia.** O `nexus.db`, os backups locais e o arquivo enviado ao Google Drive são legíveis por quem tiver o arquivo. DPAPI protege apenas segredos (tokens/senhas), não o banco.
2. **Backup em nuvem/pendrive.** Se a pasta de backup estiver em um serviço sincronizado ou o Drive for comprometido, a cópia integral (com CPF, endereço e dívidas) vai junto.
3. **Firebase operacional com dados pessoais.** O projeto B recebe CPF/CNPJ, endereço, telefone, dívidas e faturamento. A proteção é uma lista de e-mails nas regras; não há App Check, permissão por loja, campo obrigatório de versão, auditoria de leituras nem retenção automática. Quem estiver na lista lê tudo da loja.
4. **Catálogo público.** Produtos e fotos são públicos por projeto. Um campo sensível digitado por engano (nome de cliente, custo, observação) pode ficar visível; o programa limita os campos enviados, mas não substitui a revisão humana.
5. **PIN e bloqueio local.** O PIN é hash PBKDF2 com sal e o bloqueio é de 5 erros por 30 s. A versão antiga guardava o bloqueio só em memória; hoje ele sobrevive a reinício em `%LOCALAPPDATA%\NexusLS\seguranca\tentativas.json`, mas continua sendo um arquivo local: quem tem acesso ao perfil do Windows pode apagá-lo e zerar o bloqueio. Não há bloqueio no servidor, MFA nem bloqueio automático de sessão. O bloqueio protege contra tentativa casual, não contra acesso local deliberado.
6. **Sem assinatura de código.** O `.exe` publicado não tem assinatura Authenticode (o `publicar-versao.ps1` agora suporta assinatura opcional, mas depende de certificado). Isso dificulta detectar arquivo adulterado e faz o Windows exibir "editor desconhecido".
7. **Exportações e tela.** CSV, área de transferência, capturas e o próprio monitor podem expor dados; não há marca d'água, senha ou restrição por perfil nas exportações.
8. **Sessão offline.** O refresh token fica protegido por DPAPI, mas a sessão pode valer até 14 dias sem internet; um perfil roubado pode usá-la nesse prazo.
9. **Importação do ERP.** A importação lê muitos dados de clientes e vendas. O lojista precisa ter autorização para trazer esses dados e conferir se o ERP não está sendo apontado por engano como destino de gravação (o `GuardaErp` bloqueia o projeto de produção do ERP, mas a configuração ainda deve ser revisada).
10. **Sem política de retenção, DPA ou aviso de privacidade no produto.**

## 8. Plano recomendado em fases

### Fase A (curto prazo) — criptografia do backup

- Formato novo de backup com **AES-256-GCM**, nonce/IV aleatório por arquivo, cabeçalho versionado e verificação de integridade (tag).
- Chave protegida por **DPAPI** (mesmo usuário/máquina) e possibilidade de exportar uma **chave de recuperação** para o dono guardar offline; sem isso, perder o perfil do Windows torna os backups inúteis.
- Restauração compatível com backups antigos (sem criptografia) e mensagem clara quando a chave não confere.
- Aplicar também ao envio para o Google Drive; nunca enviar o arquivo em claro "só para testar".
- Testes no `Core`: ida e volta, senha/chave errada, arquivo truncado, backup antigo e restauração.
- **Ainda não implementado.**

### Fase B (médio prazo) — avaliar SQLCipher no banco

- SQLCipher criptografa o `nexus.db` inteiro. Antes de adotar: licença/compatibilidade, provider EF Core, impacto em migrações, backup/restauração, desempenho, recuperação de chave e suporte ao atualizador.
- A chave também precisa de DPAPI + recuperação; banco criptografado sem plano de chave é risco de perda total.
- Fazer uma prova de conceito em branch separada, com testes de migração e restauração antes de decidir.
- **Ainda não implementado; é decisão de produto.**

### Fase C (médio prazo) — retenção, expurgo e direitos

- Definir os prazos da seção 4 com o contador/advogado.
- Criar rotina de expurgo/anonimização com trilha de atividades, "legal hold" e confirmação antes de apagar.
- Criar exportação completa por titular e tela de solicitações (LGPD).
- Publicar aviso de privacidade curto no programa e no catálogo.
- **Ainda não implementado.**

### Fase D (médio prazo) — acesso ao Firebase operacional

- Revisar se CPF/endereço/telefone precisam mesmo ir para a nuvem; aplicar minimização (enviar só o necessário).
- Trocar a lista fixa de e-mails por **custom claims** ou outro controle por loja/usuário; revisar usuários inativos.
- Ativar **App Check**, alertas de uso, auditoria de leituras e política de retenção no Firestore.
- Definir o contrato/DPA com o fornecedor Firebase e documentar a região dos dados.
- **Ainda não implementado.**

### Medidas de apoio

- Assinatura Authenticode do `.exe` (depende de certificado de code signing).
- BitLocker/criptografia de disco nos computadores da loja; conta do Windows individual por funcionário; bloqueio de tela.
- Restringir exportações CSV por permissão e orientar a equipe.
- Formalizar plano de incidente, contatos e treinamento anual.

## 9. O que **não** está implementado (resumo honesto)

- Criptografia do banco local (`nexus.db`).
- Criptografia dos backups locais e do backup enviado ao Google Drive.
- Expurgo/anonimização automática e política de retenção aplicada pelo programa.
- Exportação completa por titular e fluxo de solicitações LGPD.
- App Check, permissões por loja, auditoria de leituras e retenção no Firebase operacional.
- Assinatura de código obrigatória (o script suporta, mas falta o certificado e a decisão de exigir).
- Aviso de privacidade dentro do produto e registro de consentimento de marketing.
- DPA/contrato de tratamento com Google/Firebase e revisão jurídica formal.

Enquanto essas fases não forem concluídas, trate o computador da loja, o perfil do Windows, a pasta de backup e a conta Google/Firebase como **ativos de alto risco**: acesso a qualquer um deles equivale a acesso aos dados pessoais dos clientes.
