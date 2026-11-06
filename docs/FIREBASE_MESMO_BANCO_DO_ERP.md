# Firebase: o Nexus LS no mesmo projeto do ERP PDV Life

Regra de ouro: **o Nexus só LÊ o Firestore do ERP.** O projeto do ERP (`pdv---lifesurf`, empresa `lifesurf-principal`) é a base real da loja. Nada do Nexus grava lá, e nenhum teste fala com a internet.

## O que existe hoje

| Peça | Arquivo | O que faz |
|---|---|---|
| Configuração do ERP | `Core/Importacao/Erp/FonteErp.cs` (`ConfiguracaoErp`, `ConfiguracaoErpStore`) | Projeto, empresa e chave da API, em `%LOCALAPPDATA%\NexusLS\erp.json` (fora do repositório; nunca vai para o Git). |
| Login e "Testar conexão" | `Core/Importacao/Erp/ContaErp.cs` | Entra com o e-mail e a senha do ERP digitados pela pessoa e lê **no máximo 1 produto** (`runQuery`, só leitura). A senha não é guardada; só o refresh token, protegido pelo DPAPI. |
| Janela "Conectar banco de dados" | `App/Views/ConexaoBancoWindow.cs` | A primeira conta ("ERP PDV Life — mesmo banco da loja") já vem preenchida com projeto e empresa do `erp.json`. A chave da API fica **oculta** (campo de senha, só diz "salva, deixe em branco para manter"). Botão **Testar conexão**. |
| Importação (leitura) | `Core/Importacao/Erp/ImportadorErp.cs` e `FirestoreErpFonte` | Clientes, produtos, estoque e vendas do ERP para o Nexus, só por leitura. |
| Trava | `Core/Importacao/Erp/GuardaErp.cs` | Todos os destinos de **gravação** do Nexus (catálogo online, nuvem operacional, vitrine, fotos) se recusam a apontar para o projeto do ERP, tanto ao salvar a configuração quanto ao ser criados. |
| Montagem no formato do ERP | `Core/Importacao/Erp/GravadorErp.cs` | `MontadorErp` monta (sem enviar) os documentos no formato do ERP; `GravadorErp` é a única porta de saída e recusa tudo se a gravação estiver desligada. |

## Como o usuário conecta (a senha é dele)

1. Abrir **Conectar banco de dados**. A conta "ERP PDV Life" já aparece com o projeto e a empresa (se existir `erp.json`).
2. Se a chave da API ainda não estiver salva, colar a Web API Key do projeto do ERP (ela some da tela ao salvar).
3. Digitar o **e-mail e a senha do ERP** e clicar em **Testar conexão**. Mensagens possíveis: conexão confirmada; login confirmado mas sem produtos; senha errada; permissão negada pelas regras do Firestore; sem internet.
4. Importar pela tela de importação do ERP.

O agente que desenvolveu isto nunca digitou senha nem imprimiu a chave da API.

## Gravação no ERP: DESLIGADA por padrão

- `ConfiguracaoErp.GravacaoAtivada` é `false` por padrão e continua `false` para qualquer `erp.json` antigo.
- **Não existe, no programa, nenhuma implementação real de `IDestinoErp`** (a interface que enviaria os documentos ao Firestore). Mesmo com a chave ligada, nada é enviado. Só os testes têm um destino, e ele é falso (guarda em memória).
- Para um dia ligar a gravação é preciso, nesta ordem: (1) decisão explícita do dono da loja; (2) escrever e revisar um `IDestinoErp` real, que use `updateMask`/`currentDocument` e só toque os campos de `MontadorErp.CamposProduto` (nunca descrição, cores nem fotos do ERP); (3) ligar `GravacaoAtivada` no `erp.json` à mão; (4) testar primeiro num projeto Firebase de teste, nunca no real.
- O estoque (`gradeTamanhos`, `estoqueTotal`) fica fora da gravação por padrão, porque com dois sistemas gravando o saldo quem gravar por último apaga o outro.
- O `GravadorErp` ainda recusa documento que não esteja em `empresas/{empresa configurada}/...`.

## Formato dos documentos (para quando a gravação existir)

- `empresas/{empresa}/produtos/{id}`: preços em **reais** (número), grade como mapa `tamanho -> quantidade`, datas como timestamp UTC, `empresaId` carimbado (as regras do ERP exigem), `origem = "nexus-ls"`, `nexusGuid` para casar de volta.
- `empresas/{empresa}`, campo `vitrine`: o JSON do editor do catálogo (mesmo formato do `vitrineConfig.js` do ERP), mais `atualizadoEm`.

## Testes (`tests/NexusLS.Tests/ErpMesmoBancoTests.cs`)

Todos usam `ServidorFalso` (nenhuma rede): "Testar conexão" só faz POST de login e `runQuery` com `limit 1` (nunca PATCH, PUT, DELETE nem `commit`); a senha não aparece em nenhum arquivo; a chave não aparece nas mensagens; os quatro destinos de gravação recusam o projeto do ERP; a gravação vem desligada; o gravador, mesmo ligado, só entrega ao destino falso e só para a empresa configurada.

## O que NÃO foi testado

Nenhuma chamada real ao Firebase do ERP foi feita pelo desenvolvimento. O primeiro teste real é o botão "Testar conexão", com o login do usuário.
