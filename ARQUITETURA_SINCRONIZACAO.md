# Arquitetura de sincronização do Nexus LS

Define quais dados pertencem a cada banco, quem é a fonte oficial de cada um e como cada fluxo é sincronizado.
Estado em 25/10/2026. Marcado como **[feito]**, **[proposto]** ou **[decisão pendente]**.

## 5f. Vitrine (editor do catálogo)

`ConfiguracaoVitrine` (Core/Vitrine) guarda modelo, fonte, cores, textos, loja, atacado, banners e ordem dos produtos em `%LOCALAPPDATA%\NexusLS\vitrine.json` (`VitrineStore`). `Normalizar()` valida tudo antes de salvar ou publicar. `PublicadorVitrineRest` faz `PATCH .../documents/meta/vitrine` (substitui o documento) com o mesmo login do catálogo; ids de modelo, fonte e cor seguem o `temaVitrine.js` do ERP. Nada é enviado sem o clique em Publicar. Não testado contra um Firebase real; a regra do Firestore precisa liberar escrita em `meta/vitrine`.

## 5e. Fotos dos produtos

`Produto.FotoArquivo` (arquivo local em `%LOCALAPPDATA%\NexusLS\fotos`) e `Produto.FotoUrl` (endereço no Firebase Storage). `FotoProdutoService` reduz (via `IProcessadorImagem`, WPF no App), grava no disco e, em `EnviarPendentesAsync`, sobe pelo `StorageRestEnviador` (`POST firebasestorage.googleapis.com/v0/b/{bucket}/o?uploadType=media`, cabeçalho `Authorization: Firebase {idToken}`, mesmo login do catálogo). O bucket padrão é `{projectId}.firebasestorage.app` (campo opcional em Catálogo online). `fotoUrl` entra no documento do produto e no hash (`VersaoDoFormato` = v2, o que reenvia todos os produtos uma vez); a máscara de atualização só inclui `fotoUrl` quando o Nexus tem foto, para não apagar a que veio da vitrine online. As regras do Storage do projeto precisam liberar escrita ao login da licença. Não testado contra um Firebase real.

## 1. Os três lugares dos dados

| Lugar | O que é | Papel |
|---|---|---|
| **SQLite local** (`%LOCALAPPDATA%\NexusLS\pdv.db`) | Banco do programa | **Fonte oficial de tudo o que é operacional.** Toda operação grava aqui primeiro, numa transação, e funciona sem internet. |
| **Firebase do catálogo** (projeto A) | Firestore público para leitura da vitrine | Só o que o cliente final pode ver. **Nenhum dado financeiro, de cliente ou de caixa.** Independente do projeto B. |
| **Firebase operacional** (projeto B) | Firestore privado da loja **[decisão pendente: criar o projeto]** | Cópia na nuvem dos dados operacionais (backup e consulta remota). O SQLite continua sendo a fonte; o B nunca decide nada. |

Regra geral: **grava no SQLite → entra na fila na mesma transação → um processo envia → só depois da confirmação marca como enviado.**

## 2. O que vai para cada lugar

| Dado | SQLite | Projeto A (catálogo) | Projeto B (operacional) |
|---|---|---|---|
| Produto (nome, categoria, referência, código, preço de venda, grade e estoque, ativo) | fonte | **sim, só estes campos** [feito] | sim (completo, com custo e atacado) [proposto] |
| Custo, margem, preço de atacado | fonte | não | sim [proposto] |
| Fotos do produto | arquivo local | sim, URL pública [pendente: miniaturas] | não |
| Vendas e itens | fonte | não | sim, imutável [proposto] |
| Cancelamento de venda | fonte | não | sim, como evento separado [proposto] |
| Movimentação de estoque | fonte | só o saldo, via produto | sim [proposto] |
| Caixa, turnos, fechamentos, sangrias | fonte | não | sim [proposto] |
| Clientes e dívidas | fonte | não | sim [proposto] |
| Financeiro, contas, compras, fornecedores | fonte | não | sim [proposto] |
| Funcionários e PINs | fonte | não | **não** (o hash do PIN não sai do computador) |
| Pedidos feitos pelo site | chegam do A para o SQLite | origem (cliente cria) | cópia depois de importado [proposto] |

## 3. Fonte oficial por informação

- **Estoque, preço, cadastro:** SQLite. O catálogo mostra um retrato; se o retrato divergir, vale o SQLite e o próximo envio corrige.
- **Pedido do site:** nasce no projeto A; ao ser importado vira pedido no SQLite, que passa a ser o oficial. O A guarda só o status para o cliente acompanhar.
- **Vendas e caixa:** SQLite sempre. O B é cópia; nada volta do B para o SQLite, exceto em restauração explícita.

## 4. Fluxo de saída (já funciona para produtos)

1. Operação grava no SQLite (transação única).
2. O interceptador do EF anota na fila (`FilaSincronizacao`) na mesma transação [feito para produtos].
3. `ProcessadorFila` envia em lotes ao destino (A ou B), com token do usuário logado; sem usuário, usa o login da licença [feito].
4. Confirmou → marca "Enviado". Falhou por conexão → reagenda com espera crescente. Recusado → "Falhou" com o motivo e botão "Reenviar com erro" [feito].
5. Queda no meio → `RecuperarInterrompidosAsync` ao abrir; reenviar é seguro porque o id do documento é estável [feito].

### Contra duplicação

- Produto: id do documento = `Guid` do produto + hash do conteúdo [feito].
- Venda: já existe `ChaveUnica` (nulo em vendas antigas) e a venda offline usa a mesma chave [feito no SQLite]. No B, o id do documento será a `ChaveUnica`; vendas antigas ganham uma chave na primeira migração [proposto].
- Demais entidades: precisam de `Guid` e `AtualizadoEm` (hoje só `Produto` tem Guid) [proposto: migration].

## 5. Conflitos

- **Vendas, cancelamentos, movimentos de caixa e de estoque:** só acrescentam (append-only). Não há conflito possível; cancelar é um evento novo.
- **Cadastros (produto, cliente, fornecedor):** com um único computador por loja, o SQLite sempre vence. Se um dia houver 2 computadores, regra: maior `AtualizadoEm`, empate pelo `DispositivoId` [decisão pendente].

## 6. Segurança

- Login do usuário pelo Firebase Authentication; senha nunca gravada; refresh token protegido pelo Windows (DPAPI) [feito].
- Regras do Firestore do projeto A: leitura pública de `produtos` e `meta`; escrita só para e-mails da lista [feito, não testado no emulador].
- Regras do projeto B (a criar): tudo fechado; leitura e escrita só de usuários da loja autenticados [proposto].
- Nenhuma chave privada ou credencial de administrador no aplicativo.

## 7. Ordem sugerida para o que falta

1. **Teste manual do que existe** com um projeto Firebase de teste (login, produto, queda de conexão).
2. **[feito em 25/10/2026]** Migration `EntidadesSincronizaveis`: `Guid` (único) e `AtualizadoEm` em venda, cliente, pagamento de cliente, turno, movimento e fechamento de caixa, lançamento, fornecedor, compra, pedido, movimentação de estoque, conta financeira, transferência, liquidação de cartão e cheque. Linhas antigas ganham Guid por SQL; a venda que tinha `ChaveUnica` usa a mesma como Guid. O id global da venda é o `Guid` (= `ChaveUnica` quando existe); `ChaveUnica` continua opcional.
3. **[feito em 25/10/2026]** Fila generalizada: `FilaSincronizacao` tem `Entidade` + `EntidadeGuid` (era `ProdutoGuid`; linhas antigas viraram `produto`). O interceptador enfileira qualquer entidade sincronizável e, ao alterar uma parte (tamanho, item de venda/compra/pedido, evento de pedido), enfileira a dona. O `ProcessadorFila` do catálogo continua tratando só `produto`; as demais ficam pendentes até existir o destino delas.
4. **[feito em 30/10/2026, código e testes; sem Firebase real]** Destino do projeto B e envio das **vendas**: `ConfiguracaoOperacional` (projeto, chave da API, nome da loja; tela Catálogo online > "Nuvem operacional"), `MontadorOperacional` (venda + itens num documento `lojas/{loja}/vendas/{guid}`; o cancelamento são campos do mesmo documento, não um evento separado), `FirestoreOperacionalDestino` (REST, token do usuário), `ProcessadorFilaOperacional` (mesmas garantias do catálogo) e `firebase/firestore.operacional.rules`. **Os usuários entram no projeto B** (usuários do Firebase existem dentro de um projeto só): com o B configurado, o login do menu lateral é no B e o catálogo (A) continua enviando com o login da licença. Sem o B configurado, nada muda: um projeto só, como antes.
5. **[feito em 30/10/2026, código e testes; sem Firebase real]** Caixa e financeiro: turnos (`turnos`), movimentos (`movimentosCaixa`), fechamentos (`fechamentosCaixa`), pagamentos de cliente (`pagamentosCliente`), lançamentos (`lancamentos`), contas (`contas`), transferências (`transferencias`), liquidações de cartão (`liquidacoesCartao`) e cheques (`cheques`). Chaves locais (turno, conta, venda, cliente, compra...) viram o `Guid` da entidade referenciada; momentos vão em UTC e datas de calendário (vencimento, bom para, data da transferência) como texto `AAAA-MM-DD`. **Cadastros (31/10/2026):** clientes (`clientes`), fornecedores (`fornecedores`), compras com itens (`compras`) e pedidos com itens e histórico (`pedidos`) também são enviados, com as referências por Guid; o Pedido guarda `atualizadoEm` em hora local e é convertido para UTC no envio.
5b. **[feito em 31/10/2026, código e testes; sem Firebase real]** Importar do ERP (Firebase do ERP → SQLite, só leitura): produtos (com grade e estoque), clientes, turnos de caixa e vendas, em `Importacao/Erp`. Configuração e login próprios (`erp.json` e `sessao_erp.json`; mesmo e-mail e senha do ERP), seção "Importar do ERP" em Catálogo online. **Poucas leituras:** a primeira vez lê cada coleção uma vez; depois pede ao Firestore só os documentos com `atualizadoEm` (e `criadoEm`, em vendas e turnos) igual ou depois do último ponto lido, em páginas de 300; o que não mudou (hash) nem é regravado. Cada documento do ERP vira um registro daqui, ligado por `ImportacaoErp`, então importar de novo atualiza o mesmo registro. Vendas importadas **não baixam estoque** (o saldo vem do produto). Produto que sumiu do ERP mas aparece numa venda vira produto inativo, só para o histórico. O importado entra na fila e segue para as nuvens do Nexus como qualquer dado. **Dívidas (31/10):** os títulos `contas_receber` trazem só os pagamentos recebidos, que viram pagamentos do cliente (a dívida daqui = vendas a prazo - pagamentos); pagamento estornado no ERP some daqui; título sem venda de origem (saldo anterior) fica de fora, com aviso. **Pedidos (31/10):** os pedidos do ERP (atacado, WhatsApp, catálogo, venda a prazo) viram pedidos daqui com itens, cliente e produto ligados; mudança de etapa no ERP atualiza o mesmo pedido e entra no histórico. **Movimentos do caixa e gastos (02/11):** `transacoes_financeiras` do ERP: sangria, reforço, despesa e entrada avulsa da gaveta que pertencem a um turno já importado viram movimentos do turno; gastos e entradas do Financeiro viram lançamentos (pagos, ou cancelados quando cancelados no ERP); retirada/reforço sem turno importado fica de fora, com aviso. **Fora desta etapa:** fechamentos de caixa (o ERP fecha por período e o Nexus por turno: modelos diferentes), saldo anterior, vendas com pagamento misto, fotos.
5c. **[feito em 01/11/2026; sincronização e importação em 02/11/2026, só com rede simulada]** Fábrica: matéria-prima, ordens de produção (com grade) e perdas agora são entidades sincronizáveis (`Guid` e `AtualizadoEm`; os registros antigos ganham Guid na migration `FabricaSincronizacao`). Entram na fila do projeto B (coleções `materias_primas`, `ordens_producao` com a grade dentro, `perdas_producao` ligadas à ordem e à matéria pelo Guid) e voltam à fila quando mudam (avançar etapa, mexer na grade, entrada/saída de matéria). A importação do ERP traz `materia_prima`, `ordens_producao` (código repetido ganha sufixo) e `perdas_producao` (ligadas à ordem pelo código e à matéria pelo nome; **não baixam estoque de novo**, o saldo vem do ERP). A transferência já soma no produto da loja, grava as entradas no histórico de estoque e conclui a ordem numa gravação só.
6. Importar pedidos do site (A → SQLite).
7. Miniaturas e leitura incremental no catálogo.


5d. **[feito em 02/11/2026, só local]** Agenda (compromissos com dia, hora, dia inteiro e concluir) fica só neste computador: não entra na fila de envio. Licença offline assinada (ECDSA), sem bloqueio de uso enquanto `LicencaService.Obrigatoria` for falso.
