# Nexus LS

PDV (frente de caixa) e controle de estoque para loja de roupas e acessórios, feito em C# com .NET e WPF, para Windows.
Funciona sem internet e sem login, com uma loja só. Todo o programa, o código e os comentários estão em português.

## Telas

O programa abre na tela **Início** (indicadores e atalhos); o menu lateral tem Início e mais três grupos:

| Grupo | Tela | Para que serve |
|---|---|---|
| **Início** | **Início** | Tela de abertura: vendas de hoje (com a comparação com ontem), ticket médio, situação do caixa, produtos abaixo do estoque mínimo (com os piores), quanto os clientes devem (e quanto já venceu), situação do backup e atalhos para todas as telas. Vendas canceladas nunca entram nos números. Em Configurações dá para escolher abrir direto no PDV. |
| **Vendas** | **PDV** | Frente de caixa, todo pelo teclado (veja o fluxo abaixo). |
| | **Caixa** | Vendas de um dia, resumo por forma de pagamento, detalhe, **cancelar** venda (pede o motivo, registra quem e quando cancelou, devolve o estoque se você quiser; a venda nunca é apagada), reimprimir cupom e exportar CSV. |
| | **Fechamento** | Abrir o caixa com troco inicial, registrar sangria, reforço, despesa e entrada avulsa, e fechar conferindo dinheiro, cartão e Pix contra o esperado. Diferença exige observação. Fechamentos nunca mudam: conferir de novo cria uma nova versão. |
| | **Pedidos** | Quadro com as etapas **Novo → Em separação → Separado → Pronto para retirada → Entregue** (e Cancelado). Escolha um cartão para ver itens e histórico (cada mudança guarda data e quem fez), **avançar ou voltar** uma etapa, **cancelar com motivo** (pedido entregue não cancela) e imprimir a **lista de separação** (itens com caixinha, sem preços). Crie pedidos de balcão, WhatsApp, catálogo ou encomenda (com produtos do estoque ou item sem cadastro, como serviço). **Venda a prazo no PDV vira pedido sozinha** (ligado à venda, vendas anteriores a esta versão não ganham pedido) e cancelar a venda cancela o pedido. O pedido só acompanha a entrega: não mexe em estoque nem em caixa. |
| | **Mais vendidos** | Ranking de produtos num período, com opção de separar por tamanho, e exportar CSV. |
| **Estoque** | **Estoque da loja** | Lista de produtos, cadastro (preço de varejo e de atacado, custo, mínimo, referência, categoria, código de barras com botão **Gerar** para EAN-13 interno, código interno automático INT-000001 e calculadora de margem/preço), grade de tamanhos com estoque de cada um e ajuste de estoque com motivo (entrada, perda, correção). Itens abaixo do mínimo ficam em amarelo. Todo ajuste vira uma linha no histórico. **Exportar CSV** salva o estoque inteiro para o Excel (uma linha por tamanho: produto, referência, categoria, códigos, tamanho, estoque, mínimo, preços de varejo e atacado, custo e situação; inclui os desativados, marcados). |
| | **Compras** | Registra a compra de mercadoria: fornecedor (opcional), nota, produtos com tamanho, quantidade e custo. Ao registrar, **numa única operação**: o estoque de cada item aumenta (com movimentação "Compra nº X"), o custo do produto pode ser atualizado e as **contas a pagar** podem ser geradas no Financeiro (à vista, parceladas ou já pagas). Se algo falhar, nada é gravado. Cancelar a compra (com motivo) devolve o estoque e cancela as contas em aberto; não cancela se a mercadoria já foi vendida ou se há conta já paga (estorne antes). |
| | **Fornecedores** | Cadastro de fornecedores (nome, CNPJ/CPF, telefone, e-mail, observação); desativar não apaga as compras antigas. |
| | **Etiquetas** | Etiquetas com nome, preço, tamanho e código de barras (Code 128; número só de dígitos usa o conjunto C, mais curto), em bobina ou folha A4, com prévia. |
| **Gestão** | **Catálogo online** | Configura o Firebase do cliente e tem o botão **Atualizar Firestore** (confere a conexão, mostra o que vai mudar, pede confirmação, envia e registra o histórico). |
| | **Clientes** | Cadastro e histórico de compras de cada cliente. |
| | **Financeiro** | Contas a pagar e a receber (aluguel, energia, fornecedores, serviços...), com **parcelamento** (vence todo mês no mesmo dia; a última parcela leva a sobra dos centavos), baixa (pago/recebido, com a forma), **estorno** do pagamento, edição enquanto não pago e **cancelamento com motivo** (nada é apagado). Mostra o resumo do período: vendas do PDV, lucro bruto, outras receitas, despesas pagas, **resultado**, a pagar, a receber, atrasadas e despesas por categoria. Filtros por período, tipo, situação e busca; exportar CSV. **Classificação** (regra do ERP): cada lançamento diz como conta no resultado — comum (despesa da operação / receita adicional) ou só movimento de saldo (**aporte**, **outra entrada**, **retirada do proprietário**, **outra saída**, que ficam FORA do resultado e aparecem em linha à parte no resumo). **Categorias configuráveis** (a pagar e a receber): o dono acrescenta, desativa e reativa pela própria tela (nada é apagado; lançamentos antigos mantêm o nome). |
| | **Contas e saldos** | Onde o dinheiro está e para onde vai. Quatro abas. **Visão geral:** saldo disponível (gaveta + bancos), a receber, a pagar, em compensação (cheques e cartões que ainda não caíram), projeção do saldo em 7, 30, 60 e 90 dias, saldo por conta e o resultado do período (vendas − custo = lucro bruto, + outras receitas − despesas). **Contas e transferências:** gaveta, cofre e contas de banco, cada uma com saldo inicial e a data dele; transferência entre contas (não é receita nem despesa) e cancelamento com motivo. **Cartão a liquidar:** vendas no cartão que ainda não caíram; ao cair você informa a conta, o dia e a taxa da operadora (a taxa vira despesa). **Cheques:** cheques recebidos pendentes, compensar numa conta, devolvido pelo banco (volta a ser a receber, vencido) ou cancelar. Todos os números vêm do **motor financeiro** (`MotorFinanceiro`), então são os mesmos em toda tela. |
| | **Relatórios** | Vendas por período: total, número de vendas, ticket médio, descontos, canceladas, por dia, por forma de pagamento, por categoria, **lucro bruto** (total vendido menos o custo das mercadorias; o custo fica guardado em cada venda, e vendas antigas usam o custo atual e avisam que é estimativa) e exportar CSV (vendas e itens). |
| | **Pedidos** | Acompanhamento de pedidos de clientes por etapa: Novo → Em separação → Separado → Pronto para retirada → Entregue (ou Cancelado, com motivo). Quadro por etapa, detalhe com histórico (quem e quando), novo pedido (balcão, WhatsApp, catálogo ou encomenda) e **lista de separação** para imprimir. Venda a prazo feita no PDV cria o pedido sozinha. O pedido **não mexe no estoque nem no caixa**: é só o acompanhamento. |
| | **Funcionários** | Cadastro de funcionários com **PIN** (4 a 8 números, guardado só como hash PBKDF2 com sal) e **perfil**: Administrador (tudo), Gerente (tudo menos Configurações e Funcionários), Operador de caixa (PDV, caixa e clientes), Vendedor (PDV e clientes) ou Personalizado (você marca o que pode). Enquanto ninguém está cadastrado o **login fica desligado** e o programa funciona como sempre; ao criar o primeiro administrador o login liga e o programa passa a pedir o PIN ao abrir. Cada pessoa só vê no menu o que o perfil permite, vendas e caixa registram o nome de quem fez, e só quem tem a permissão cancela vendas. Cinco PINs errados bloqueiam 30 segundos. O sistema nunca fica sem administrador ativo. O primeiro administrador recebe um **código de recuperação** (mostrado uma vez) para redefinir o PIN se esquecer (tela de entrada → "Esqueci o PIN"). "Trocar usuário" fica no fim do menu. |
| | **Atividades** | Trilha de quem fez o quê e quando, só para quem tem a permissão de Configurações. Registra: cancelamento de venda, compra, lançamento e pedido; estorno de pagamento; perda e correção de estoque; cadastro, edição, desativação e reativação de funcionário; troca de PIN (o PIN nunca aparece). O registro é gravado **na mesma transação** da ação, então se a ação falha ele também não existe, e nunca é alterado nem apagado. Filtros por período, tipo e busca. |
| | **Configurações** | Dados da loja e do cupom, impressora, largura do papel, impressão automática, com prévia do cupom, e **backup** (automático e manual, escolha da pasta, restauração). |

## Fluxo do PDV (tudo pelo teclado)

```
BUSCA ─Enter→ TAMANHO ─Enter→ QUANTIDADE ─Enter→ (volta à busca)
BUSCA vazia + carrinho cheio ─Enter→ DESCONTO ─Enter→ PAGAMENTO ─Enter→ SUCESSO ─Enter→ nova venda
```

- O foco volta sempre para a busca. A busca aceita nome, referência ou código de barras.
- **Cadastro de produto:** o código interno (INT-000001...) é automático e nunca repete; o botão Gerar cria um código de barras EAN-13 que começa com 2 (faixa de uso interno). Não se cadastra dois produtos ativos com o mesmo nome e a mesma referência. Margem = lucro sobre o preço; markup = lucro sobre o custo.
- **Vender sem baixa:** marque no produto (serviço, encomenda) para o PDV não conferir nem baixar o estoque dele; o cancelamento só devolve o que de fato saiu do estoque. Em Configurações, desligar "Perguntar o tamanho" faz o PDV ir direto à quantidade quando o produto só tem uma opção (modo rápido). Ligado é o padrão.
- **Enter na busca:** se você desceu com ↑ ↓, vale o item destacado; senão o código de barras exato tem prioridade (leitor); senão vale o primeiro resultado.
- **Tamanho:** sempre abre ao escolher o produto. Grade de 3 colunas, navegue com ← → ↑ ↓ ou digite a letra/número do tamanho (M, GG, 38). No **atacado** aparece também a opção "Sem tamanho".
- **Quantidade:** digite o número ou use ↑ soma / ↓ subtrai (mínimo 1); limitada ao estoque do tamanho.
- **Desconto:** ← escolhe R$, → escolhe %. Pode ficar vazio.
- **Pagamento:** dinheiro, Pix, cartão débito, cartão crédito (1 a 4 parcelas), cheque e a prazo (exige cliente). ← → trocam a forma, ↑ ↓ mudam as parcelas. No dinheiro o valor recebido já vem com o total, o troco é calculado e não deixa finalizar com valor menor. Na venda a prazo, digite o nome do cliente: se ele não existir, o primeiro Enter o cadastra.
- **Sucesso:** botões Reimprimir cupom e Nova venda (← → alternam, Enter confirma).
- **Carrinho:** quantidade, remover item, **editar o preço** direto na linha (por unidade ou pelo total da linha), esvaziar com confirmação. A venda em andamento é **salva sozinha** e volta se você fechar o programa.

### Atalhos

| Tecla | Ação |
|---|---|
| `F1` | Abre o PDV, de qualquer tela |
| `F2` | Foco na busca (e seleciona o texto) |
| `Espaço` | Com a busca vazia, alterna a tabela de preço: varejo / atacado |
| `F10` | Atalho extra: avança para o fechamento da venda (como Enter com a busca vazia) |
| `Enter` | Avança a etapa |
| `Esc` | Fecha a janela atual e volta à busca (não esvazia o carrinho) |
| `Del` | Remove o item selecionado do carrinho |
| `Ctrl+S` / `Ctrl+N` | Salvar / novo, nas telas de cadastro |

### Proteções contra venda duplicada

Enter repetido (tecla segurada) é ignorado; Enter já tratado por outro elemento (botão, campo) não é tratado de novo; uma venda em andamento bloqueia um segundo envio; com uma janela aberta a busca não recebe o Enter; e a tela de sucesso ignora o Enter nos primeiros instantes, para um Enter duplo não pular a confirmação.

## Regras importantes

- **Dinheiro em centavos** (número inteiro, `long`), nunca `double`. A tela converte para "R$ 12,50".
- **Venda e baixa de estoque** (por produto **e tamanho**) acontecem na **mesma transação** do banco: ou grava tudo, ou nada.
- **Estoque nunca fica negativo**, e o operador é avisado.
- **Nunca se apaga uma venda**: só se cancela, com motivo obrigatório (mínimo 3 letras), guardando quem cancelou e quando. O estoque volta ao mesmo tamanho, a menos que se escolha não devolver (peça danificada). O nome do operador fica em Configurações. Venda cancelada não entra em totais.
- **Caixa por turno**: só um caixa aberto por vez; as vendas pertencem ao turno pelo horário, então turnos não se cruzam. Cancelar venda de um caixa já fechado avisa para refazer a conferência.
- **Todo movimento de estoque deixa registro** (estoque inicial, ajustes, vendas e cancelamentos), com o tamanho.
- Cada produto tem **dois preços**: varejo e atacado. Atacado vazio vale o de varejo.
- O item da venda guarda o nome, o tamanho e o preço **da hora da venda**.
- Todo produto tem a opção "Sem tamanho" (onde fica o estoque de quem não usa grade).
- **Venda a prazo** exige cliente e vira uma dívida (vence em 30 dias). Em Clientes você recebe pagamentos parciais: cada pagamento abate primeiro a venda mais antiga. O limite de crédito do cliente (zero = sem limite) bloqueia vendas a prazo que o ultrapassem, e uma venda a prazo que já recebeu pagamento não pode ser cancelada. Pagamentos recebidos entram no esperado do caixa aberto. Não há cobrança, juros nem carnê.

## Regras do dinheiro (motor financeiro)

Cada fato vira um lançamento com id único (a mesma venda nunca é contada duas vezes) e só o que já aconteceu mexe no saldo.

- **Saldo disponível** = gaveta + cofre + contas de banco. Só entra o que está **realizado**. Cheque pendente, cartão a liquidar, venda a prazo e conta em aberto **não** são saldo.
- **Onde cada venda entra:** dinheiro → gaveta · Pix → conta de banco padrão · cartão → "em compensação" (débito previsto para o dia seguinte, crédito para 30 dias) até você registrar que caiu · cheque → "em compensação" até compensar (a venda no cheque cria o cheque pendente sozinha, com "bom para" em 30 dias) · a prazo → só a receber (a dívida do cliente); o pagamento dele entra pela forma usada e **não** é receita de novo.
- **Compra de mercadoria não é despesa.** O dinheiro sai da conta, mas o custo só entra no resultado quando a peça é vendida; contar os dois descontaria o custo duas vezes.
- **Sangria e reforço do caixa** mexem no saldo da gaveta e não são despesa nem receita. O **troco inicial** do turno não é entrada: é dinheiro que já estava lá. Para levar dinheiro ao banco use **Transferência**.
- **Lançamento sem conta** (por exemplo, Pix sem nenhuma conta de banco cadastrada) fica fora dos saldos e aparece num aviso, para você corrigir.
- **Saldo inicial** de cada conta vale a partir da data informada; o que aconteceu antes dela não conta de novo.
- Venda de cartão já liquidada, ou cheque já compensado, **trava** o cancelamento da venda: estorne antes, para o saldo não ficar com dinheiro de venda cancelada.
- O **"Limpar tudo"** apaga cheques, liquidações e transferências junto com as vendas; as contas (configuração) ficam.

## Cupom e etiquetas

- **Cupom não fiscal** impresso pela impressora do Windows (bobina térmica ou comum), sem abrir caixa de diálogo, na impressora escolhida em Configurações (ou na padrão do Windows). Ele sai sozinho ao finalizar a venda (dá para desligar em Configurações), e o botão **Reimprimir** existe no PDV e no Caixa. Se a impressora falhar, a venda continua gravada e o programa avisa.
- **Etiquetas** usam o código de barras do produto em **Code 128** (letras, números e símbolos sem acento). Produto sem código, ou com acento no código, sai sem barras. Formatos: bobina 50 × 30 mm, bobina 40 × 25 mm e folha A4 3 × 7 (63,5 × 38,1 mm); na folha dá para começar numa etiqueta que não seja a primeira, para aproveitar uma folha já usada em parte. A impressora é escolhida na caixa de impressão do Windows. **Legibilidade:** se o código de barras ficaria com barras mais finas que 0,25 mm no papel escolhido (código comprido em etiqueta estreita), a tela avisa e a impressão é bloqueada até você trocar de papel ou remover o item. **Meus modelos de bobina:** salve bobinas de tamanho próprio (nome, largura e altura em mm); ficam em `%LOCALAPPDATA%\NexusLS\etiquetas.json`.
- **Exportar** gera CSV para o Excel brasileiro (separador `;`, vírgula decimal, acentos corretos).

## Importar produtos de uma planilha (CSV)

Em **Estoque da loja → Importar CSV** você escolhe um arquivo `.csv` (no Excel: *Salvar como → CSV*) e o programa mostra o resumo antes de cadastrar. Um arquivo de exemplo está em `modelos/modelo_produtos.csv`.

- A **primeira linha** tem os títulos das colunas, em qualquer ordem. Aceita `;` `,` tabulação ou `|` como separador, UTF-8 ou Windows-1252, e preço com vírgula (`59,90`).
- Colunas: **Nome** e **Preço de venda** são obrigatórias; opcionais: Código de barras, Referência, Categoria, Preço de atacado, Preço de custo, Estoque, Estoque mínimo, Tamanho. Títulos parecidos também funcionam (Produto, Descrição, EAN, Preço, Qtd, Grupo...).
- Várias linhas do mesmo produto (mesmo código de barras, ou mesmo nome e referência) com **Tamanho** diferente viram um produto com a grade de tamanhos; cada linha traz o estoque do seu tamanho.
- O estoque entra como movimentação de entrada com o motivo "Importação de CSV".
- **Nunca altera nem apaga** produto que já existe: a linha é ignorada e o motivo aparece no relatório. Importar o mesmo arquivo duas vezes não duplica nada. Linhas com problema (sem nome, preço inválido...) ficam de fora com o número da linha, e as outras entram normalmente.

## Estrutura do projeto

```
NexusLS.slnx
src/
  NexusLS.Core/          Regras de negócio, sem nenhuma referência à interface
    Models/              Produto, tamanhos, Venda, Cliente, configuração da loja e enums
    Data/                PdvDbContext (acesso ao banco)
    Migrations/          Histórico de versões da estrutura do banco
    Services/            Venda, Produto, Estoque, Cliente, Carrinho, Rascunho, Cupom, Relatório, CSV...
    Etiquetas/           Code 128, formatos e diagramação das etiquetas
    Backup/              Backup e restauração do banco, nomes, retenção e preferências
    Sincronizacao/       Catálogo online: o que se publica, hash, validação, plano, sincronizador em lotes e cliente REST do Firebase
  NexusLS.App/           Interface WPF (MVVM com CommunityToolkit.Mvvm)
    Views/               Telas (XAML)
    ViewModels/          Lógica de cada tela
    Converters/          Conversores de exibição
    Impressao/, Etiquetas/   Impressão no Windows e desenho das etiquetas
    Resources/           Cores e estilos
tests/
  NexusLS.Tests/         Testes das regras (xUnit, SQLite em memória)
  NexusLS.App.Tests/     Testes das telas: o fluxo do PDV tecla por tecla, sem abrir janela
```

A regra de negócio fica no `Core`, que **não conhece o WPF**, e por isso é testada sem abrir janela. O fluxo do PDV (e as outras telas) é testado nos ViewModels, simulando as teclas.

## Preparo para a sincronização com o Firestore

Esta é a base segura que o envio usa. O SQLite continua sendo a única fonte dos dados; o Firestore é uma cópia de publicação atualizada só quando o usuário clica em "Atualizar Firestore" (veja a próxima seção).

- **Id global:** todo produto tem um `Guid` (identificador único e permanente), além do `Id` inteiro que só vale neste banco. Será o id do documento remoto, então reenviar o mesmo produto nunca cria duplicado. Os produtos que já existiam ganharam o seu na atualização, sem perder nenhum dado.
- **AtualizadoEm:** o produto guarda a data (UTC) da última alteração, atualizada sozinha em cadastro, edição, ajuste de estoque, venda e cancelamento. É só informativa.
- **O que mudou é decidido por um hash** (SHA-256) do conteúdo publicado, e não pela data, porque o relógio do computador pode estar errado. Mudou o nome, o preço, o código de barras, o estoque de um tamanho ou o status: o hash muda. Mudou custo, preço de atacado ou estoque mínimo: não muda, porque esses dados **não são publicados**.
- **O que se publica (por enquanto):** nome, categoria, referência, código de barras, preço de varejo, se o produto não controla estoque, se está ativo e o estoque de cada tamanho. Nada de custo, atacado, vendas, clientes ou dívidas. Aumentar essa lista é decisão sua.
- **Plano antes de enviar:** o programa calcula e mostra quantos produtos são novos, alterados, desativados, inalterados ou inválidos (sem nome, preço negativo, estoque negativo, código de barras repetido...). Produto inválido fica na lista para corrigir e **nunca** é enviado, e não atrapalha os outros. Produto desativado vira "desativado" no remoto; nada é apagado.
- **Controle no banco:** a tabela `SincronizacaoProdutos` guarda o hash do que o servidor já confirmou (um registro por produto, atualizado, nunca duplicado), e `SincronizacaoLogs` guarda cada tentativa (quando, quantos criados/alterados/desativados/ignorados, erros e resultado). Se um envio falhar no meio, só o que foi confirmado é anotado, e a próxima vez continua de onde parou.
- Antes de aplicar essa mudança no banco, o programa faz um backup automático ("pre-atualizacao").

## Fila de sincronização e login do usuário (núcleo, ainda sem tela)

- **Fila de saída (`FilaSincronizacao`)**: toda entidade sincronizável salva (produto, venda, cliente, lançamento, caixa, compra, pedido, estoque...) entra na fila **na mesma transação** (`FilaSincronizacaoInterceptor`). Uma linha por entidade (`Entidade` + `EntidadeGuid`, índice único); várias mudanças viram um envio só. Todas essas entidades têm `Guid` (id do documento remoto) e `AtualizadoEm`. Hoje só os produtos são enviados (ao catálogo); as outras entidades esperam o destino do Firebase operacional. Ver `ARQUITETURA_SINCRONIZACAO.md`.
- **`ProcessadorFila`**: lê o produto atual, envia pelo mesmo destino do catálogo e só marca "Enviado" depois da confirmação. Sem internet: reagenda com espera crescente (30 s até 30 min). Dado recusado: "Falhou" com o motivo (volta à fila quando o produto mudar). Ao abrir o programa, `RecuperarInterrompidosAsync` devolve à fila o que ficou "Enviando" por uma queda. Sem duplicar: id do documento = Guid do produto, hash do que já foi enviado, um envio por vez, e produto alterado durante o envio continua pendente.
- **`ServicoLogin`** (Firebase Authentication, REST, só chave pública): login por e-mail e senha; a senha nunca é gravada, o refresh token fica protegido em `sessao.json`; restaura ao abrir (renova com internet; offline vale por 14 dias desde a última validação); serve de `IProvedorToken` para o envio.
- **Na interface (menu lateral, "Catálogo online")**: indicador da fila ("Catálogo em dia", "3 aguardando envio", "Sem conexão — 3 aguardando", "2 com erro", "Sem login"), botões **Sincronizar agora**, **Entrar** e **Sair da conta**. Ao abrir o programa: restaura a sessão, devolve à fila o que uma queda deixou pela metade e processa; depois repete a cada 3 minutos. Se o Firebase está configurado e ninguém entrou, abre a janela de login, com **Continuar sem entrar** (a fila só acumula; vendas e estoque seguem normais).
- **Quem grava no Firestore**: com usuário logado, o envio usa o token DELE (`DestinoFilaFirestore`); sem usuário, cai no login da licença configurado em Catálogo online. As regras (`firebase/firestore.rules`) agora aceitam uma **lista de e-mails**: coloque lá cada usuário que vai entrar.
- **Nuvem operacional (vendas)**: em Catálogo online > "Nuvem operacional" informe o projeto Firebase operacional (outro projeto, privado), a chave da API e o nome da loja. Com isso os **usuários passam a entrar nesse projeto** (menu lateral > Entrar) e cada venda, caixa (turnos, movimentos, fechamentos), pagamento de cliente, lançamento, conta, transferência, liquidação de cartão e cheque, com cancelamentos e correções, é gravada em `lojas/{loja}/{coleção}/{guid}` pela mesma fila (offline, reenvio automático, sem duplicar). O catálogo continua no projeto dele, com o login da licença. Regras do projeto operacional em `firebase/firestore.operacional.rules` (troque os e-mails e publique). Ainda não testado com um Firebase real.
- **Produtos antigos**: ao abrir, os produtos que ainda não estão na fila entram nela (o hash evita reenviar o que o catálogo já tem), então o catálogo se completa sozinho.
- **Itens com erro**: aparece o botão **Reenviar com erro**, que devolve à fila o que o servidor recusou.
- O botão "Atualizar Firestore" continua funcionando como antes.

## Catálogo online (envio para o Firestore)

Na tela **Catálogo online** o botão **Atualizar Firestore** publica o catálogo no Firebase do seu cliente. O programa continua funcionando normalmente sem internet; o SQLite é sempre a fonte dos dados, e a nuvem é só uma cópia, atualizada **somente quando você clica**.

**O que acontece ao clicar:**

1. Confere a conexão, o login e a permissão **sem gravar nada** (se falhar, mostra o motivo em português e registra a tentativa no histórico).
2. Calcula o que mudou (novos, alterados, desativados, sem mudança, inválidos) e **mostra o resumo para você confirmar**. Se não há nada a enviar, diz isso e registra "sem mudanças".
3. Ao confirmar, envia em lotes (400 por vez; o limite do Firestore é 500), com barra de progresso e botão de cancelar.
4. Só depois que o servidor **confirma** cada lote o programa anota no banco local o que foi enviado. Se a conexão cair no meio, o que já foi confirmado fica valendo e a próxima atualização continua de onde parou, sem repetir nada.
5. Se o servidor recusar um lote por dado inválido, o lote é refeito produto a produto: só o culpado fica de fora, com o motivo na tela.
6. Mostra o resultado (sucesso, parcial ou falha) e guarda uma linha no **histórico** (quando, quantos criados, alterados e desativados, ignorados, erros e a mensagem).

**Garantias:**

- **Nada é apagado na nuvem.** Produto desativado é publicado como inativo. O envio usa o `Guid` do produto como id do documento (`produtos/{guid}`), então enviar de novo atualiza o mesmo documento e nunca duplica.
- **Só os campos do catálogo** são enviados e atualizados (nome, categoria, referência, código de barras, preço de varejo, se controla estoque, ativo, estoque por tamanho e o hash). Fotos, descrições e destaques que a vitrine online criar **não são tocados**. Custo, preço de atacado, vendas, clientes e dívidas **nunca saem deste computador**.
- **Só uma atualização por vez.** Um segundo clique durante o envio é recusado.
- **Sem chave de administrador.** O programa usa o login de e-mail e senha da licença (Firebase Authentication, pela API REST). O que esse login pode fazer é definido pelas regras de segurança do Firestore do cliente (`firebase/firestore.rules`).
- **A senha** é guardada com o DPAPI do Windows (só o mesmo usuário, neste computador, consegue ler) e nunca aparece em arquivo, tela, log ou mensagem de erro. O token fica só na memória e é renovado sozinho.

**Configurar um cliente (uma vez):** no projeto Firebase **dele** (um projeto por cliente), ative Authentication por e-mail/senha e crie um usuário; crie o Firestore em modo de produção; cole `firebase/firestore.rules` nas regras (trocando o e-mail) e publique. Na tela Catálogo online informe o id do projeto, a chave da API (Web API Key, que não é segredo), o e-mail e a senha, clique em **Salvar** e em **Testar conexão**.

**Não verificado:** o envio foi testado com um servidor Firebase de mentira (formato das requisições, erros, lotes, repetição, cancelamento), mas **ainda não contra um projeto Firebase real**, e as regras do Firestore ainda não foram executadas num emulador. Faça o teste num projeto de teste antes de entregar a um cliente.

## Visual

A interface usa a biblioteca **[WPF-UI](https://github.com/lepoco/wpfui)** (estilo Fluent, do Windows 11): barra de título e fundo Mica, caixas de texto, tabelas, listas, abas, calendário e ícones modernos. As cores e os estilos próprios (botões, cartões, menu) ficam em `src/NexusLS.App/Resources/Estilos.xaml`.

## Requisitos

- Windows 10 ou 11
- [SDK do .NET](https://dotnet.microsoft.com/download) 10 (confira com `dotnet --version`)

## Como rodar e testar

Na pasta do projeto:

```bash
dotnet build
dotnet test
dotnet run --project src/NexusLS.App
```

Na primeira execução o banco é criado sozinho (`Database.Migrate()` ao abrir o programa).

## Onde ficam os dados e como fazer backup

Tudo fica em **um único arquivo** SQLite:

```
%LOCALAPPDATA%\NexusLS\nexus.db
```

Normalmente `C:\Users\<seu usuário>\AppData\Local\NexusLS\nexus.db`. Para abrir a pasta, cole `%LOCALAPPDATA%\NexusLS` na barra de endereço do Explorer.

- **Backup (Configurações > Backup dos dados):** o programa faz cópias do banco sozinho e sob demanda, **com o programa aberto**.
  - **Automático:** ao abrir e a cada 15 minutos ele confere se já passou o intervalo escolhido (desligado, 1, 6, 12 horas, todo dia ou toda semana; padrão: todo dia). Se falhar, aparece um aviso na tela.
  - **Manual:** botão "Fazer backup agora".
  - **Onde:** você escolhe a pasta (padrão `Documentos\Nexus LS\Backups`). Se a pasta ficar no mesmo disco do banco, a tela avisa: prefira pen drive, HD externo ou pasta sincronizada na nuvem.
  - **Nome:** `nexus_AAAA-MM-DD_HHMMSS_motivo.db` (motivo: manual, auto, pre-atualizacao, pre-restauracao, pre-limpeza).
  - **Segurança da cópia:** usa a API de backup do SQLite (copiar o `.db` à mão pode sair incompleto por causa do arquivo `-wal`), grava num arquivo temporário, confere a integridade (`PRAGMA integrity_check`) e só então dá o nome final. Cópia que não passa na conferência é descartada.
  - **Quantos guardar:** você define (de 3 a 200). Os mais antigos, manuais e automáticos, são apagados; só arquivos com o nome padrão acima são tocados. Os de segurança (antes de atualizar ou de restaurar) nunca são apagados sozinhos.
  - **Antes de atualizar o banco:** quando uma versão nova muda a estrutura das tabelas, o programa faz um backup antes. Se a mudança falhar, o banco volta a esse backup.
  - As preferências ficam em `%LOCALAPPDATA%\NexusLS\backup.json`, fora do banco.
- **Restaurar:** em Configurações, escolha o backup na lista (ou nenhum, para procurar um arquivo) e clique em "Restaurar". O programa confere o arquivo, **guarda uma cópia dos dados atuais** (`pre-restauracao`), troca o banco e reinicia. Se o banco estiver em uso ou algo falhar, nada é alterado. Restaurar à mão continua possível: com o programa fechado, substitua o `nexus.db` (apague os `-wal` e `-shm` ao lado).
- **Começar do zero:** em Configurações → **Apagar dados** (só administrador). Escolha **só o movimento** (vendas, caixa, pedidos, compras, financeiro e histórico de estoque; mantém produtos, clientes, fornecedores, funcionários e configurações) ou **tudo da loja** (também produtos, clientes e fornecedores), digite `APAGAR` e confirme. O programa faz antes um backup `pre-limpeza` (se o backup falhar, nada é apagado; esse backup nunca é apagado sozinho), apaga numa só transação, registra em Atividades e reinicia. Funcionários, configurações e a trilha de atividades nunca são apagados. Também dá para, com o programa fechado, apagar o `nexus.db`; um novo, vazio, é criado na próxima abertura.
- **Vindo da versão antiga (PDV Simples):** na primeira abertura o programa copia `%LOCALAPPDATA%\PdvSimples\pdv.db` para `nexus.db`. O arquivo antigo não é apagado.
- A venda em andamento fica em `%LOCALAPPDATA%\NexusLS\rascunho.json` (pode ser apagado sem problema; ele só guarda o carrinho aberto).

## Gerar o executável

Gera um único `.exe` que roda em qualquer Windows 64 bits, **sem precisar instalar o .NET**:

```bash
dotnet publish src/NexusLS.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publicar
```

O resultado é `publicar\NexusLS.exe` (cerca de 140 MB, porque leva o .NET dentro). Copie esse arquivo para o computador do caixa e crie um atalho na Área de Trabalho. Os dados ficam no `%LOCALAPPDATA%` do computador onde o programa roda.

Um instalador com assistente (`.msi`) não faz parte desta versão. Para um arquivo menor, troque `--self-contained true` por `false`; aí o computador de destino precisa ter o [.NET Desktop Runtime](https://dotnet.microsoft.com/download) 10.

## Atualizações do programa

**Para o usuário:** ao abrir, o Nexus LS procura sozinho uma versão nova (sem internet, ou se não houver novidade, não aparece nada). Havendo, surge uma faixa no alto da janela com **Atualizar agora**, **O que mudou** e **Depois**. Nada é instalado sem o clique. Ao atualizar, o programa baixa o arquivo, confere, fecha e abre de novo; uma venda em andamento fica salva e volta, e o banco é preservado (se a versão nova mudar o banco, o backup `pre-atualizacao` é feito antes, como sempre). Em Configurações → Atualizações aparece a versão e o botão **Verificar atualizações**.

**Segurança:** só baixa por HTTPS e só de endereços do GitHub; confere o tamanho, se é mesmo um executável e o **SHA-256** (publicado junto com a versão). Arquivo incompleto, alterado ou sem assinatura é descartado e nada é instalado. O programa instalado não carrega nenhuma senha nem token.

**Para publicar uma versão nova** (quem desenvolve):

1. Uma vez só: crie no GitHub um repositório **público** só para os instaladores (padrão: `Lukkzz11/Nexus-LS-Atualizacoes`; nunca coloque o código-fonte nele), instale o GitHub CLI (`winget install GitHub.cli`) e entre com `gh auth login`.
2. Faça o commit do código e rode, na pasta do projeto:

```bash
.\publicar-versao.ps1 -Versao 1.2.0 -Notas "- O que mudou 1`n- O que mudou 2"
```

O script confere que tudo está commitado e que a versão é maior que a última, **roda todos os testes (se um falhar, nada é enviado)**, gera o `NexusLS.exe` com a versão gravada, calcula o SHA-256 e cria o release `v1.2.0` com o `.exe` e o `.sha256`. Para ensaiar sem enviar, use `-SoGerar`. As notas (`-Notas` ou `-NotasArquivo`) são o texto que o usuário lê em **O que mudou**.

Para usar outro repositório sem recompilar, crie `%LOCALAPPDATA%\NexusLS\atualizacao.json` com `{ "repositorio": "dono/nome" }`. O programa instalado em `Program Files` não consegue trocar a si mesmo sem permissão de administrador: instale numa pasta do usuário (por exemplo `Documentos\Nexus LS`).

## Mexendo nos models (migrations)

Uma *migration* é um "passo de versão" da estrutura do banco. Depois de alterar uma classe em `Models/` (ou o `PdvDbContext`), gere uma nova:

```bash
dotnet tool restore
dotnet ef migrations add NomeDaMudanca --project src/NexusLS.Core
```

O programa aplica as migrations pendentes sozinho ao abrir, preservando os dados existentes.

## Fora do escopo desta versão

Multiempresa (o Nexus é de uma loja só, por decisão), loja online completa (só o envio do catálogo existe), cobrança com juros e carnê, fábrica, impressora fiscal. O plano de trazer o restante do ERP anterior está em `PLANO_MIGRACAO_NEXUS.md`.

## Editor do catálogo (JSX dentro do WebView2)

O **Editor do catálogo** e a pré-visualização do catálogo são telas em React/JSX (migradas do ERP), em `web/catalogo`, hospedadas no programa por um WebView2. O resto do Nexus LS continua WPF/XAML.

- **Dados:** a página nunca fala com o Firebase. Ela pede tudo ao programa por mensagens (`web/catalogo/src/ponte.js` ↔ `Web/PonteCatalogo.cs` ↔ `Core/Vitrine/ServicoCatalogoWeb.cs`): produtos do estoque local, vitrine salva, imagens do editor e publicação.
- **Publicar** (só no clique): guarda no computador, sobe ao Storage as imagens locais e grava a vitrine completa no campo `v2` de `meta/vitrine` (os demais campos do documento não são tocados).
- **Alterou o JSX?** rode `.\web\construir.ps1` (gera `src/NexusLS.App/Web/catalogo.zip`, embutido no .exe e extraído em `%LocalAppData%\NexusLS\web`) e depois compile. Para ver o editor no navegador com dados de demonstração: `cd web/catalogo; npm run dev`.
- Precisa do Microsoft Edge WebView2 Runtime (já vem no Windows 11).

### Ajustes por peça e catálogo para o site

- No painel **Produtos** do editor, a seção **Ajustes por peça** define, para cada produto, se aparece no catálogo, se é destaque e o preço promocional (menor que o varejo). Fica em `%LocalAppData%\NexusLS\catalogo-produtos.json` (não mexe no cadastro nem no PDV) e vai para `produtos/{id}` do Firestore, só nos campos `ativoNoCatalogo`, `destaque` e `precoPromocionalCentavos`, ao clicar em **Salvar e publicar**. Produto ainda não sincronizado é pulado (nada é criado).
- **Catálogo para o site:** o build gera também `web/catalogo/dist/publico.html`, que lê `meta/vitrine` (campo `v2`) e `produtos` do Firestore por leitura pública (REST, sem login). Hospede a pasta `dist` (por exemplo no Firebase Hosting) e preencha `config.json` com `{ "projectId": "SEU-PROJETO" }` (ou use `?projeto=ID`). Atacado e custo nunca saem do computador, então o site mostra só o varejo. A sacola não grava pedido na nuvem (as regras só permitem leitura): finaliza pelo WhatsApp da loja.
