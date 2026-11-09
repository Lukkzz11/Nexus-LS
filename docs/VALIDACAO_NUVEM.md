# Validação real da nuvem do Nexus LS

Roteiro para validar, **em ordem de risco**, tudo o que o Nexus LS faz com Firebase/Google. Hoje o envio foi testado contra servidores simulados; este roteiro é o que falta para confiar no serviço real. **Pare no primeiro passo que falhar** e registre a mensagem exata antes de continuar.

> **Aviso importante:** as regras em `firebase/firestore.rules` e `firebase/firestore.operacional.rules` **nunca foram executadas em um emulador do Firebase** neste projeto (falta o JDK 21 na máquina de desenvolvimento). O passo 6 existe justamente para isso. Não publique regras em um cliente sem antes passar por ele.

## Antes de começar

1. **Faça um backup** em Configurações > Backup e confirme que o arquivo abre. Este roteiro mexe em configuração, não em dados de venda, mas o backup é a rede de segurança.
2. Use **projetos Firebase de teste**, nunca o projeto de produção do ERP (`pdv---lifesurf`) nem o projeto real de um cliente. O Nexus bloqueia gravação no projeto do ERP (`GuardaErp`), mas não confie só nisso.
3. Tenha em mãos: id do projeto, Web API Key, e-mail e senha de um usuário de teste e o nome da loja (ex.: `minha-loja-teste`).
4. Ferramentas: navegador, PowerShell, Node.js 20 e, para o emulador, **Java/JDK 21** e `firebase-tools` (`npm install -g firebase-tools`).
5. Anote a versão do Nexus (`Configurações > Atualizações`) e a data/hora de cada teste.
6. **Nunca** cole senhas, refresh tokens ou a chave privada de serviço em issue, chat ou commit. A Web API Key é pública, mas usuário/senha não são.

### Limites de cota (plano gratuito)

A primeira sincronização operacional envia **todo o histórico** de vendas, caixa e financeiro. O Firestore gratuito tem cota diária de leituras/gravações; faça a primeira carga em um dia tranquilo e deixe o programa aberto até a fila zerar. Estourar a cota não corrompe dados: a fila continua local e reenvia depois.

---

## 1. Importação do ERP (somente leitura) — risco baixo

**Por que primeiro:** o Nexus só lê o projeto do ERP; não há gravação no banco de produção.

**Pré-requisitos**
- Projeto Firebase do ERP, Web API Key, empresa (ex.: `lifesurf-principal`) e um usuário que consiga ler as coleções no ERP.
- Backup local feito.
- Ciente de que a primeira importação consome muitas leituras.

**Ação**
1. Abra **Catálogo online > Importar do ERP**.
2. Preencha projeto, chave e empresa; salve.
3. Clique em **Importar do ERP** e entre com o usuário do ERP.
4. Acompanhe o andamento e o resumo por coleção.
5. Sem alterar nada no ERP, importe de novo.
6. Altere **um** preço no ERP (produto de teste) e importe outra vez.

**Resultado esperado**
- Resumo com produtos, clientes, turnos, vendas, contas a receber, pedidos, transações, produção e perdas (conforme o que existir).
- Produtos com preço de varejo/atacado, grade e estoque iguais aos do ERP.
- Clientes com nome, documento, telefone, endereço e limite iguais aos do ERP.
- Vendas com data, total, forma de pagamento, itens e cliente; venda cancelada no ERP aparece cancelada.
- **O estoque local não diminui** por causa das vendas importadas.
- A segunda importação diz que nada mudou (0 a poucas leituras a mais).
- Depois de mudar um preço, só aquele produto aparece como atualizado.

**Se falhar**
- **Permissão negada:** o usuário não lê `empresas/{empresa}/...`; use um usuário com acesso às telas equivalentes no ERP.
- **Chave inválida:** a chave é de outro projeto.
- **Nada em uma coleção:** confira se os documentos têm o campo de data usado pelo importador (`atualizadoEm` ou `criadoEm`).
- **Cota:** aguarde o próximo dia ou use um projeto de teste menor; não apague o banco para "resolver".
- **Estoque mudou:** pare e reporte; a importação não deve baixar estoque.

---

## 2. Login no projeto operacional (projeto B) — risco baixo/médio

**Pré-requisitos**
- Projeto B novo e privado, com Authentication por e-mail/senha ativo.
- Um usuário de teste criado.
- Regras `firebase/firestore.operacional.rules` publicadas com o e-mail do teste (ou modo de teste temporário, nunca em cliente).
- Firestore criado em modo de produção.

**Ação**
1. Em **Catálogo online > Nuvem operacional**, informe id do projeto, Web API Key e nome da loja; salve.
2. Menu lateral > **Entrar**.
3. Teste primeiro uma senha errada; depois a correta.
4. Feche e abra o programa.
5. Confira `%LOCALAPPDATA%\NexusLS\sessao.json` (o refresh token deve estar protegido; a senha não pode aparecer em lugar nenhum).

**Resultado esperado**
- Senha errada mostra "E-mail ou senha incorretos" e não grava sessão.
- Senha certa mostra o usuário no menu e o indicador da fila.
- Ao reabrir, a sessão é restaurada sem pedir senha (com internet).
- Nenhum arquivo contém a senha em texto claro.

**Se falhar**
- **`OPERATION_NOT_ALLOWED`:** login por e-mail/senha não está ativo no projeto.
- **`API_KEY` inválida:** chave de outro projeto.
- **`USER_DISABLED`:** usuário desativado no console.
- **Sessão não restaura:** veja `sessao.json`, se o DPAPI ainda abre com o mesmo usuário do Windows e se o programa está rodando como outro usuário/elevado.
- **Regras:** o login pode funcionar e a gravação falhar; isso é testado no passo 4.

---

## 3. Fila do catálogo (projeto A) — risco médio

**Pré-requisitos**
- Projeto A (catálogo) separado do operacional e do ERP.
- Authentication com o usuário da licença/editor.
- `firebase/firestore.rules` publicado com os e-mails corretos.
- Alguns produtos válidos, um produto inválido (sem nome ou preço negativo) e, se possível, uma foto.

**Ação**
1. Em **Catálogo online**, preencha projeto, chave, e-mail e senha; salve.
2. Clique em **Testar conexão**.
3. Clique em **Atualizar Firestore**, confira o plano e confirme.
4. No console, abra `produtos/{guid}` e `meta/catalogo`.
5. Altere o preço de um produto no Nexus e sincronize de novo.
6. Desative um produto e sincronize.
7. Tente enviar o produto inválido.

**Resultado esperado**
- O plano mostra novos, alterados, desativados, inalterados e inválidos antes de gravar.
- O documento usa o `Guid` do produto como id; reenviar não duplica.
- A segunda sincronização envia só o produto alterado.
- Produto desativado vira `ativo: false`; **nada é apagado**.
- Produto inválido fica de fora com o motivo e não impede os outros.
- Campos custo, atacado, vendas, clientes e dívidas **não** aparecem no documento.
- O histórico registra cada tentativa.

**Se falhar**
- **Permissão negada:** e-mail fora da lista nas regras ou `origem`/campos obrigatórios ausentes.
- **Teste de conexão passa e envio falha:** regras de escrita; o teste pode só validar leitura/login.
- **Duplicou:** o id não é o `Guid`; pare e reporte.
- **Custo/atacado apareceu:** pare imediatamente e revise o destino/regras.
- **Lote recusado:** o programa refaz item a item; veja qual produto foi recusado.

---

## 4. Fila operacional (projeto B) — risco médio/alto

**Pré-requisitos**
- Passo 2 concluído, usuário logado no projeto B e loja configurada.
- Regras operacionais publicadas.
- Backup feito; de preferência, uma loja/projeto só para o teste.
- Ciente de que a primeira carga envia o histórico inteiro.

**Ação**
1. Clique em **Sincronizar agora** e deixe a fila zerar.
2. Faça uma venda de teste no PDV (com item e forma de pagamento).
3. Aguarde até 3 minutos ou clique em Sincronizar agora.
4. Cancele a mesma venda no Nexus.
5. Abra e feche um turno de caixa.
6. Registre um pagamento de cliente e um lançamento financeiro.
7. No console, confira `lojas/{loja}/...`.

**Resultado esperado**
- Coleções `vendas`, `turnos`, `movimentosCaixa`, `fechamentosCaixa`, `pagamentosCliente`, `lancamentos`, `contas`, `clientes` etc., conforme o que foi gerado.
- Venda em `vendas/{guid}` com itens dentro do documento.
- Cancelamento atualiza **o mesmo documento** (campos de cancelamento); não cria segundo documento.
- Caixa: turno e fechamento no mesmo documento, com versão atualizada.
- Reenvio não duplica; documento cancelado continua existindo.
- O indicador do menu mostra pendências e erros; "Reenviar com erro" devolve à fila.

**Se falhar**
- **"Entre com seu usuário":** sessão ausente/expirada; faça login de novo.
- **Permissão negada:** e-mail fora das regras, `origem` ou `guid` ausente, caminho fora de `lojas/{loja}`.
- **Fila parada em "Enviando":** feche e reabra; o programa recupera o que ficou interrompido.
- **Cota/limite:** aguarde; a fila local não se perde.
- **Documento duplicado:** pare e reporte o caminho e o `guid`.
- **Dados errados no documento:** não apague a fila nem o banco; exporte o histórico e reporte.

---

## 5. Storage e fotos — risco médio

**Pré-requisitos**
- Storage ativado no projeto A, bucket correto e regras de Storage configuradas no console.
- **Não existe `storage.rules` no repositório.** As regras de Storage precisam ser criadas/revisadas no console do Firebase antes deste teste. O catálogo público precisa ler as imagens; o upload precisa exigir o usuário autenticado.
- Produto com foto local e sem `fotoUrl`.

**Ação**
1. Cadastre/altere a foto de um produto no Nexus.
2. Clique em **Atualizar Firestore** (o envio de fotos acontece antes do catálogo).
3. Abra a URL gravada em `fotoUrl` no navegador, inclusive em janela anônima.
4. Troque a foto e sincronize; remova a foto e sincronize.
5. Tente enviar sem estar logado (se o programa permitir) e confira a recusa.

**Resultado esperado**
- Upload responde 200 e devolve `downloadTokens`; `fotoUrl` é gravada no produto.
- A URL abre a imagem sem login (catálogo público).
- Substituir/remover limpa o endereço local antigo.
- **O arquivo antigo no Storage não é apagado pelo Nexus** (não há operação de exclusão); isso é esperado e deve entrar na política de retenção.
- Sem login, o Storage recusa a gravação (403), mas a leitura pública continua.

**Se falhar**
- **404:** Storage não ativado ou nome do bucket errado.
- **403 no upload:** regra de Storage não permite o usuário autenticado.
- **403 na leitura pública:** regra de leitura pública ausente; o catálogo não mostrará fotos.
- **Upload passa e `fotoUrl` não:** veja se o produto foi enviado ao catálogo depois da foto; a foto é enviada antes, mas falha de foto não impede o catálogo.
- **Foto antiga ainda acessível:** esperado; planeje o expurgo no Storage.

---

## 6. Regras do Firestore no emulador — risco alto (nunca testado)

**Por que aqui:** é o teste que mais segurança dá antes de publicar em cliente. As regras atuais **nunca rodaram em emulador**.

**Pré-requisitos**
- Node.js 20, `firebase-tools` e **JDK 21** (`java -version`).
- Nenhum projeto real será tocado: use um id que comece com `demo-` (ex.: `demo-nexus`), que o emulador aceita sem login.
- `firebase/firebase.json` temporário (não existe no repositório hoje; crie e remova depois):

```json
{
  "firestore": {
    "rules": "firestore.rules"
  }
}
```

Para testar as regras operacionais, troque para `"rules": "firestore.operacional.rules"`.

**Ação — subir o emulador**

```powershell
cd C:\NexusLS\firebase
firebase emulators:start --only firestore --project demo-nexus
```

Abra a interface do emulador no endereço mostrado (normalmente `http://127.0.0.1:4000`) e confira que as regras carregaram sem erro.

Para testar **usuário autenticado** manualmente, inclua também o emulador de Authentication (`--only firestore,auth`) e crie o usuário na interface; em um teste com `@firebase/rules-unit-testing`, use `authenticatedContext` com um token que tenha o campo `email` igual ao da lista.

**Ação — rodar um teste e derrubar o emulador**

```powershell
cd C:\NexusLS\firebase
firebase emulators:exec --only firestore --project demo-nexus "node caminho\do\seu-teste.mjs"
```

`emulators:exec` sobe o emulador, executa o comando e derruba tudo no fim. Se o teste precisar de usuários de Authentication, acrescente `auth` ao `--only` (`--only firestore,auth`). **Não há suíte automatizada de regras no repositório hoje**: crie um script de teste com `@firebase/rules-unit-testing` (fora do repositório ou em uma branch de teste) ou faça as verificações manuais na interface. Se só quiser conferir que as regras sobem, use um comando simples que termine (ex.: `node -e "console.log('ok')"`); isso **não** valida permissões.

**Matriz mínima — catálogo (`firestore.rules`)**

| Cenário | Esperado |
|---|---|
| Visitante sem login lê `produtos/{id}` e `meta/catalogo` | Permitir |
| Visitante sem login grava `produtos/{id}` | Negar |
| Usuário autenticado **fora** da lista de e-mails grava | Negar |
| Usuário da lista grava produto com `origem: "nexus-ls"`, `nome` string e `precoVendaCentavos` inteiro >= 0 | Permitir |
| Usuário da lista tenta excluir produto ou `meta` | Negar |
| Qualquer acesso a outra coleção | Negar |

**Matriz mínima — operacional (`firestore.operacional.rules`)**

| Cenário | Esperado |
|---|---|
| Sem login lê ou grava `lojas/{loja}/vendas/{guid}` | Negar |
| Usuário autenticado fora da lista lê/grava | Negar |
| Usuário da lista lê `lojas/{loja}/vendas/{guid}` existente | Permitir |
| Usuário da lista cria/atualiza documento com `origem: "nexus-ls"` e `guid` igual ao id | Permitir |
| `guid` diferente do id do documento | Negar |
| Excluir qualquer documento | Negar |
| Acessar fora de `lojas/{loja}/...` | Negar |

**Resultado esperado**
- O emulador sobe sem erro de sintaxe e todas as linhas da matriz batem.
- Usuário da lista é identificado pelo **e-mail no token**; um token sem e-mail ou com e-mail diferente deve ser negado.

**Se falhar**
- **Java não encontrado:** instale JDK 21 e ajuste `JAVA_HOME`; o emulador do Firestore exige Java.
- **Regras não carregam:** erro de sintaxe/indentação no arquivo; corrija e suba de novo.
- **Tudo permitido:** a regra `match /{outro=**}` não está fechando, ou o path testado não é o esperado.
- **Tudo negado para o usuário certo:** o e-mail do token do teste não é exatamente o da lista (maiúsculas/espaços) ou falta `origem`/`guid`.
- **Não conseguiu automatizar:** valide manualmente na interface e registre; **não publique** as regras em cliente sem esse teste.

---

## 7. Queda de internet — risco médio

**Pré-requisitos**
- Passos 2–4 concluídos e fila zerada.
- Uma venda em andamento ou produtos para alterar.

**Ação**
1. Desligue a internet (cabo/Wi-Fi) com o programa aberto e logado.
2. Faça 2 ou 3 vendas, altere um produto e abra/feche um caixa.
3. Feche o programa pelo Gerenciador de Tarefas (simulando queda).
4. Abra de novo, ainda sem internet.
5. Reconecte e espere até 3 minutos (ou clique em Sincronizar agora).

**Resultado esperado**
- Vendas, estoque e caixa continuam funcionando; o indicador mostra "Sem conexão — N aguardando".
- A venda em andamento volta (rascunho).
- Nada é perdido ao fechar à força; a fila é recuperada ao abrir.
- Ao reconectar, a fila envia sozinha; cada venda aparece **uma vez** (mesmo `guid`).
- Erros de rede são reagendados com espera crescente (30 s até 30 min), sem loop agressivo.

**Se falhar**
- **Programa travou:** anote a tela e o log; o banco local não deve corromper.
- **Fila não volta:** feche e reabra; verifique "Enviando" preso e o mecanismo de recuperação.
- **Duplicou:** pare e reporte o `guid` e o caminho.
- **Venda sumiu:** verifique se o rascunho e a venda foram gravados; não apague o banco.

---

## 8. Token expirado / sessão revogada — risco médio

**Pré-requisitos**
- Usuário logado no projeto B e acesso ao console.
- Fila com pelo menos um item pendente (para ver se ela é preservada).

**Ação**
1. No console > Authentication > usuário > **Revoke refresh tokens** (ou desative o usuário).
2. No Nexus, clique em **Sincronizar agora** e faça uma nova operação.
3. Observe a mensagem e o indicador.
4. Reative o usuário (se desativou) e entre de novo.

**Resultado esperado**
- A renovação falha com "Sua sessão expirou ou foi revogada. Entre novamente." (ou "usuário desativado").
- Ao reabrir o programa (ou ao tentar entrar de novo), a sessão local é limpa e o programa pede login; **a fila e os dados locais permanecem**.
- Depois do novo login, a fila volta a enviar sem perder itens.
- Nenhuma venda/estoque fica bloqueado por causa da sessão.

**Se falhar**
- **Continua sincronizando com token revogado:** pare e reporte; a revogação deveria invalidar o refresh token.
- **Fila foi apagada:** pare imediatamente; isso não deve acontecer.
- **Login não volta:** confira usuário ativo, senha e regras; use outro usuário de teste.

---

## 9. Restauração de sessão offline — risco médio

**Pré-requisitos**
- Sessão válida criada **com internet** no mesmo dia.
- Programa fechado.

**Ação**
1. Desligue a internet.
2. Abra o programa.
3. Confira o menu lateral, a fila e uma venda de teste.
4. Deixe sem internet por alguns minutos e faça vendas.
5. Reconecte e espere a renovação.

**Resultado esperado**
- A sessão é restaurada sem pedir senha, com aviso de modo offline (vale por **14 dias** desde a última validação online).
- Vendas/estoque funcionam; a fila acumula e envia ao reconectar.
- Depois de reconectar, a sessão é renovada e o aviso offline some.

**Como validar o limite de 14 dias sem esperar duas semanas**
- O prazo é coberto por testes automatizados (`ServicoLoginTests`: restaura offline dentro do prazo e exige login depois dele). Rode-os no CI/testes locais.
- **Não** edite `sessao.json` à mão para simular: o refresh token é protegido por DPAPI e a edição pode corromper a sessão.

**Se falhar**
- **Pede login mesmo dentro do prazo:** confira `UltimaValidacaoOnline`, se o relógio do Windows está correto e se o mesmo usuário do Windows abriu o programa.
- **Passou de 14 dias e ainda entra:** verifique a data no arquivo e reporte; o limite deveria ser aplicado.
- **Fila não envia ao reconectar:** veja o passo 7; a sessão pode ter expirado durante o período offline.

---

## 10. Registro do teste

Preencha uma linha por passo:

| Passo | Data/hora | Versão | Resultado | Mensagem/observação |
|---|---|---|---|---|
| 1. Importação do ERP | | | | |
| 2. Login | | | | |
| 3. Catálogo | | | | |
| 4. Operacional | | | | |
| 5. Storage/fotos | | | | |
| 6. Regras no emulador | | | | |
| 7. Queda de internet | | | | |
| 8. Token expirado | | | | |
| 9. Sessão offline | | | | |

Se algo falhar, envie: passo, horário, versão, mensagem exata da tela, texto do indicador da fila e o histórico de sincronização. **Nunca envie senha, token ou o `.env`.**
