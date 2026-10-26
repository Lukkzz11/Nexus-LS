# Arquitetura de sincronização do Nexus LS

Define quais dados pertencem a cada banco, quem é a fonte oficial de cada um e como cada fluxo é sincronizado.
Estado em 25/10/2026. Marcado como **[feito]**, **[proposto]** ou **[decisão pendente]**.

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
2. Migration: `Guid`, `AtualizadoEm` e `ChaveUnica` obrigatória em vendas, clientes, caixa e lançamentos.
3. Generalizar a fila: de "um produto" para "uma entidade" (`Entidade`, `EntidadeGuid`), mantendo a tabela atual compatível.
4. Destino do projeto B e envio das **vendas** (maior valor, menor risco: imutáveis).
5. Caixa e financeiro; depois clientes e cadastros.
6. Importar pedidos do site (A → SQLite).
7. Miniaturas e leitura incremental no catálogo.
