# Roadmap de pendências do Nexus LS

Registro feito em **09/11/2026**, na versão **1.2.6**. O objetivo é separar o que foi entregue nesta rodada do que continua em aberto, com o motivo e o pré-requisito de cada item.

## Entregue nesta rodada

| Entrega | Onde | Observação |
|---|---|---|
| Pipeline de CI | `.github/workflows/ci.yml` | Windows, .NET 10, restore/build/Release, duas suítes com `.trx`, auditoria de pacotes vulneráveis e job separado do catálogo (`npm ci` + `npm run build`). Sem segredos. |
| Assinatura Authenticode opcional | `publicar-versao.ps1` | Parâmetros `-Certificado` (thumbprint ou `.pfx`) e `-SenhaCertificado`; variável `NEXUS_CERT_THUMBPRINT`; valida com `Get-AuthenticodeSignature` e só calcula o SHA-256 depois de assinar. Sem certificado, avisa e não falha. |
| Inventário LGPD e plano de adequação | `docs/LGPD.md` | Dados locais, backups, Drive, Firebase A/B e ERP; base legal, retenção sugerida, direitos, incidente, riscos e fases. |
| Roteiro de validação real da nuvem | `docs/VALIDACAO_NUVEM.md` | Ordem por risco, pré-requisito/ação/resultado/falha, incluindo emulador de regras e comandos do Firebase CLI. |
| Roadmap de pendências | `docs/ROADMAP_PENDENCIAS.md` | Este arquivo. |
| README pontual | `README.md` | Versão atual 1.2.6 e "mais de 1.300 testes automatizados". |
| Log rotativo e captura global de erros | `Core/Diagnostico/RegistroDeLog.cs`, `App/App.Diagnostico.cs`, `App.xaml.cs` | Log diário em `%LOCALAPPDATA%\NexusLS\logs` (14 arquivos), exceções de UI/domínio/tarefas, falhas de migração/login/backup. Sem dados pessoais. |
| Bloqueio de PIN persistente e KDF versionado | `Core/Seguranca/TentativasLoginStore.cs`, `Core/Services/FuncionarioService.cs` | 5 erros/30 s continuam iguais, mas sobrevivem ao reinício; PBKDF2 novo em `v2:600000`, hash legado aceito e regravado no login. |
| Venda exige caixa aberto | `App/ViewModels/PdvViewModel.Pagamento.cs` | A tela bloqueia a finalização quando não há turno aberto; testes de ViewModel sem serviço de caixa seguem livres. |
| Concorrência e consultas do caixa | `Core/Services/CaixaService.cs` | Abertura/fechamento/movimentos serializados; retry único no conflito de versão; filtros de período no SQL e paginação do histórico. |
| Retenção de fila e logs | `Core/Services/RetencaoService.cs`, `App.xaml.cs` | Apaga só fila `Enviado` com mais de 90 dias e mantém 1.000 logs; roda em segundo plano na abertura. |
| SQLite endurecido e índices | `Core/Data/CaminhoBanco.cs`, `Core/Data/PreparadorBanco.cs`, migration `IndicesDeConsulta` | WAL, `synchronous=NORMAL`, `busy_timeout`, `foreign_keys` em todas as conexões; índices para vendas, recebimentos, movimentos e fila. |

> A menção ao banco antigo do PDV Simples foi ajustada para a pasta `%LOCALAPPDATA%\PdvSimples`, para não confundir com o banco atual (`nexus.db`); o arquivo antigo continua sendo copiado na primeira abertura.

## Pendências

Legenda de esforço: **P** = até 1 dia; **M** = 2 a 5 dias; **G** = mais de uma semana (estimativa grossa, sem contar espera externa).

| # | Pendência | Por que ficou | Esforço | Pré-requisitos |
|---|---|---|---|---|
| 1 | **Criptografia do banco local** (SQLCipher ou equivalente) | Decisão de produto: mexe em backup, restauração, migrações, atualizador, desempenho e suporte. Um banco criptografado sem plano de chave pode virar perda total de dados. | **G** | Prova de conceito em branch separada; provider EF Core compatível; definição de chave (DPAPI + recuperação); testes de migração/restauração; aval do produto. |
| 2 | **Criptografia dos backups** (AES-256-GCM + chave protegida por DPAPI) | Ainda não implementada. Precisa de formato versionado, chave de recuperação, compatibilidade com backups antigos e testes de restauração. | **M** | Definir formato e estratégia de recuperação da chave; implementar no `BackupService` e no envio ao Drive; testes no `Core`; documentar. |
| 3 | **Teste real com Firebase** (projetos A e B) | Falta credencial de um projeto de teste e **JDK 21** para o emulador. O envio só foi validado contra servidor simulado. | **M** (execução) / **G** (correções que aparecerem) | Projeto Firebase de teste, usuários de teste, regras publicadas, internet e o roteiro `docs/VALIDACAO_NUVEM.md`. |
| 4 | **Testes das regras do Firestore no emulador** | Os arquivos `firebase/firestore.rules` e `firebase/firestore.operacional.rules` nunca rodaram em emulador. Não há suíte de regras no repositório. | **P/M** | Node 20, `firebase-tools`, JDK 21 e uma suíte com `@firebase/rules-unit-testing` (ou validação manual documentada). |
| 5 | **NFC-e / fiscal** | Fora do escopo atual e sem código. Depende de decisão contábil/fiscal e de integração com SEFAZ. | **G** | Contador, certificado digital A1, definição de regime/UF, biblioteca ou serviço de emissão, homologação e testes. |
| 6 | **Wizard de primeiro uso** | O instalador (`src/NexusLS.Setup`) tem assistente de instalação/licença, mas o programa ainda não guia o dono no primeiro uso (dados da loja, backup, usuários, Firebase). | **M** | Definir o fluxo mínimo e os campos; reutilizar `ConfiguracaoService`/backup; decidir se aparece só quando o banco está vazio; testes de ViewModel. |
| 7 | **Display de cliente** | Não existe. Exige segunda tela, janela separada e comunicação com o PDV; é um recurso novo, não um ajuste. | **M** | Definir conteúdo (itens, total, forma de pagamento, marca), hardware/monitor e estratégia de sincronização com o carrinho. |
| 8 | **Assinatura de código obrigatória** | O script já suporta, mas falta o certificado. Sem certificado de Code Signing, a assinatura não passa a validação `Valid` e o Windows mostra "editor desconhecido". | **P** (script) + obtenção externa | Certificado de assinatura de código (ICP-Brasil ou AC internacional); decidir onde guardar a senha do `.pfx` com segurança; testar em máquina limpa. |
| 9 | **Expurgo/anonimização de dados comerciais e históricos** | A retenção de fila e logs já foi entregue. O expurgo de venda/cliente/fornecedor depende de prazos legais e de uma rotina que não viole a regra "nada é apagado" sem trilha. | **M** | Prazos definidos com contador/advogado; rotina com atividade, "legal hold" e anonimização; testes. |
| 10 | **Exportação completa por titular e anonimização (LGPD)** | Não existe tela/fluxo. O CSV atual é por tela, não por pessoa. | **M** | Depende do item 9; definir formato, o que sai, como comprovar identidade e como registrar a solicitação. |
| 11 | **Acesso ao Firebase operacional (App Check, custom claims, auditoria)** | Hoje a proteção é uma lista fixa de e-mails; não há permissão por loja/usuário, App Check nem retenção no Firestore. | **M** | Console Firebase, decisão de modelo de acesso, ajuste no `ServicoLogin`/destino, regras novas testadas no emulador e DPA com o provedor. |
| 12 | **Regras de Storage** | Não há `storage.rules` no repositório; as fotos dependem de configuração manual no console. | **P** | Definir se a leitura é pública (catálogo) e a escrita só autenticada; criar o arquivo, testar no emulador e publicar. |
| 13 | **Proteção do computador e do perfil do Windows** | Medida operacional, não de código: BitLocker, conta individual por funcionário, bloqueio de tela e restrição da pasta de backup. | **P** | Notebooks com TPM/BitLocker; política da loja; treinamento da equipe. |
| 14 | **Aviso de privacidade e contrato/DPA** | Depende de revisão jurídica e do fornecedor (Google/Firebase). | **P/M** (jurídico) | Texto aprovado pelo responsável; canais de contato; DPA do Firebase/Google. |
| 15 | **Primeira execução do CI no GitHub** | O workflow foi validado como YAML/PowerShell e o build/testes foi rodado localmente (1.541 testes verdes), mas ainda não executou no GitHub Actions. | **P** | Commit na `main`, GitHub Actions habilitado e primeiro push/PR; conferir se os caminhos dos `.trx` aparecem no artefato. |

## Sequência sugerida

1. **Curto prazo (segurança que não muda o produto):** itens 2 (criptografia de backup), 4 (regras no emulador), 12 (Storage) e 8 (assinatura, quando houver certificado).
2. **Depois:** item 3 (validação real) com o roteiro pronto; corrigir o que aparecer antes de vender para cliente novo.
3. **Médio prazo:** itens 9 e 10 (retenção/direitos), 11 (acesso ao Firebase) e 6 (primeiro uso).
4. **Decisão de produto:** item 1 (SQLCipher) só depois de prova de conceito e plano de chave; item 5 (fiscal) quando o contador definir o modelo; item 7 (display) quando houver hardware.

## Verificação desta rodada

- `dotnet build NexusLS.slnx -c Release`: **êxito, 0 erros e 0 avisos**.
- `dotnet test tests/NexusLS.Tests`: **1.114 aprovados, 0 falhas**.
- `dotnet test tests/NexusLS.App.Tests`: **427 aprovados, 0 falhas**.
- `dotnet list NexusLS.slnx package --vulnerable --include-transitive`: nenhum pacote vulnerável conhecido.
- Corrigido de passagem um erro de compilação pré-existente: `SetupTests` usava `Instalador.HashConfere`, removido na reformulação do instalador; o helper foi restaurado.

## O que não foi possível fazer nesta rodada

- Não assinei nem publiquei nenhum executável: não há certificado disponível e a publicação não foi solicitada.
- Não validei Firebase real nem emulador: faltam credenciais, projeto de teste e JDK 21.
- Não executei o workflow no GitHub Actions (só validação local do YAML e das suítes).
- Criptografia do banco e dos backups, wizard de primeiro uso, display de cliente e NFC-e continuam pendentes (itens 1, 2, 5, 6 e 7).
