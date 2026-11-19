# Proposta: nova Gestão do Nexus

Para aprovar antes de construir (etapa 6 e 7 do PLANO_PROXIMA_FASE.md). Só desenho; nada foi alterado no app.

## 1. Pesquisa (resumo dos padrões)

Fontes públicas, em sua maioria blogs e páginas comerciais; nem tudo está em documentação oficial. Onde não achei confirmação, está dito.

- Bling: relatórios gerenciais prontos (DRE, saldos de depósito), curva ABC e dashboards de estoque e vendas ("Meu Negócio").
  [Curva ABC](https://blog.bling.com.br/curva-abc/), [Dashboard](https://blog.bling.com.br/dashboard-ecommerce/), [Relatório gerencial](https://blog.bling.com.br/?p=14565), [Relatório de estoque e giro](https://blog.bling.com.br/relatorio-de-estoque/)
- Conta Azul: relatórios agrupados em financeiros (fluxo de caixa diário/mensal, a pagar e a receber, DRE gerencial por competência, vencimento ou baixa), de vendas (por cliente, produto, vendedor) e agendador de relatórios.
  [Relatórios](https://contaazul.com/relatorios), [Fluxo de caixa](https://contaazul.com/controle-financeiro/fluxo-caixa), [DRE](https://contaazul.com/dre)
- Omie: modelos prontos por módulo; relatórios de contas em aberto, pagamentos realizados e atrasadas por faixa de atraso; campo Empresa nos relatórios. Dashboards ricos vêm de terceiros.
  [Relatórios](https://www.omie.com.br/funcionalidades/relatorios/), [Contas a pagar](https://ajuda.omie.com.br/pt-BR/articles/17508169-conhecendo-os-relatorios-de-contas-a-pagar-e-pagamentos), [Atrasadas](https://ajuda.omie.com.br/pt-BR/articles/1400012-gerando-o-relatorio-de-contas-a-pagar-e-a-receber-atrasadas), [Relatórios gerenciais para PMEs](https://www.omie.com.br/blog/principais-relatorios-gerenciais-para-pmes-lista-e-usos/)
- Nuvemshop: painel "Estatísticas" em abas (visão geral, vendas e clientes, produtos, cupons, tempo real); Top 50 de produtos com clique para abrir o detalhe (vendas por dia, estoque); exportar vendas em planilha.
  [Estatísticas](https://atendimento.nuvemshop.com.br/estatisticas/como-analisar-as-vendas-receitas-e-o-perfil-dos-clientes-pelas-estatisticas), [Relatórios gerenciais](https://www.nuvemshop.com.br/blog/relatorios-gerenciais/)
- Tiny/Olist: múltiplos estoques (depósitos) e sincronização entre empresas; não achei documentação oficial de giro ou parados (reclamações de sincronia multiempresa: [Reclame Aqui](https://www.reclameaqui.com.br/olist-oficial/problemas-com-multiempresas-e-sincronia-de-estoque-3-meses-sem-solucao_PV87_OHfdOw1EEWE/)).
- Linx: sem página oficial achada sobre painel multiloja. Material de varejo em geral: [KPIs do varejo (Omie)](https://www.omie.com.br/blog/indicadores-de-desempenho-kpis-essenciais-para-o-varejo/), [Indicadores (Tecnospeed)](https://blog.tecnospeed.com.br/indicadores-do-varejo/).
- Gestão Click: não consegui confirmar nada em fonte pública; fica sem conclusão.
- Anexo em lançamento: não achei confirmação nas fontes acima (Conta Azul e Omie). É padrão do mercado, mas aqui é desenho próprio.

Padrões que valem para o Nexus:
1. Painel em duas camadas: visão geral com 5 a 8 indicadores e um clique para o relatório completo.
2. Relatórios separados por assunto: financeiros (caixa, a pagar/receber, DRE) e de produtos/vendas. Nunca misturados.
3. Todo relatório tem período (hoje, 7 dias, mês, mês anterior), filtros e exportação CSV/PDF.
4. Produtos: mais vendidos, curva ABC (A = ~80% do faturamento, B = próximos ~15%, C = resto), giro (saídas / estoque médio), parados (sem venda há N dias), ruptura.
5. Contas a pagar/receber: aberto, pago e atrasado por faixas (1-7, 8-30, 30+ dias).
6. Multi-loja: filtro "Empresa/Loja" no topo de cada relatório e um comparativo lado a lado; estoque por depósito.
7. Sugestões: cartões curtos de alerta, cada um com um número e um botão que abre a tela certa.

## 2. O que já existe no Nexus (hoje)

Menu da Gestão (GestaoWindow): Agenda, Editor do catálogo, Clientes, Financeiro, Contas e saldos, Relatórios (abas Gráficos, Vendas, DRE), Funcionários, Atividades, Configurações. Catálogo online fica em Configurações. Permissões em `GestaoViewModel.Permitido` (Clientes, Financeiro, Relatórios, Funcionarios, Configuracoes).
Fora da Gestão (menu do balcão): Mais vendidos, Desempenho de vendas (filtros, melhores dias/horários, menos vendidos, sem venda), Compras, Fornecedores, Calculadora de preços, Estoque da loja, Resumo de estoque, Fábrica, Trocas.

## 3. Menu proposto (cada item abre tela própria)

```
GESTÃO
 VISÃO GERAL
   Painel                      NOVO     indicadores + Consultor Nexus
 VENDAS
   Desempenho de vendas        existe   (move do balcão para cá também; segue no balcão para o gerente)
   Clientes                    existe   (aniversários, fiado)
   Agenda                      existe
 FINANCEIRO
   Lançamentos (Financeiro)    existe   + anexo (novo)
   Contas e saldos             existe
   Relatório financeiro        NOVO     (separa da aba atual de Relatórios)
 ESTOQUE E COMPRAS
   Relatório de produtos       NOVO     mais vendidos, ABC, giro, parados
   Compras                     existe   (entra na Gestão)
   Fornecedores                existe   (entra na Gestão)
   Calculadora de preços       existe   (entra na Gestão)
 PESSOAS
   Funcionários                existe
   Atividades                  existe
 EMPRESA
   Gestão da empresa           NOVO     lojas, desempenho por loja
   Entradas e saídas           NOVO     lançamento com anexo
   Configurações               existe   (tela separada, etapa 8)
```
Saem: Editor do catálogo (vira outro programa) e a aba Relatórios atual (dividida em Financeiro e Produtos). Permissões: itens novos reutilizam Financeiro, Relatorios e Configuracoes; "Gestão da empresa" e preço por loja só administrador.

## 4. Telas novas

### 4.1 Painel (Visão geral)
```
Período: [Hoje][7 dias][Este mês][Mês ant.]     Loja: [Todas v] (só admin)
+-------------+-------------+-------------+-------------+
| Faturamento | Nº vendas   | Ticket médio| Lucro bruto |
| R$ 12.400   | 86          | R$ 144      | R$ 5.100    |
| +8% x ant.  | -3%         | +12%        | +5%         |
+-------------+-------------+-------------+-------------+
| A pagar 7d  | Atrasado    | Saldo contas| Estoque     |
| R$ 3.200    | R$ 450 (2)  | R$ 9.800    | parado R$ X |
+-------------+-------------+-------------+-------------+
[ Faturamento por dia (barras) ]   [ Formas de pagamento ]
Consultor Nexus (até 5 cartões, ver 4.6)
```
Cada indicador é clicável e abre o relatório. Comparação com o período anterior de mesmo tamanho.

### 4.2 Relatório financeiro
```
Período [..] Visão: (x)Caixa ( )Competência   Loja [v]   [CSV][PDF]
Abas: Resumo | Fluxo de caixa | A pagar/receber | DRE
Resumo:  Entradas | Saídas | Resultado | Saldo final
Fluxo:   tabela dia a dia (entrada, saída, saldo acumulado) + linha do gráfico
A pagar/receber: Em aberto | Pagas | Atrasadas (1-7, 8-30, 30+ dias)
DRE:     já existe (Receita, CMV, Lucro bruto, Despesas, Resultado)
Rodapé: despesas por categoria (pizza) e top 5 fornecedores
```
Reaproveita o Demonstrativo (DRE) e os dados do Financeiro; novo: fluxo diário e faixas de atraso.

### 4.3 Relatório de produtos
```
Período [..]  Loja [v]  Categoria [v]  [CSV]
Abas: Mais vendidos | Curva ABC | Giro | Parados | Ruptura
Mais vendidos:  # | Ref | Produto | Qtd | R$ | % | (clique abre detalhe por tamanho)
Curva ABC:      ref ordenada por faturamento, % acumulado, classe A/B/C
                  A: até 80% acumulado, B: até 95%, C: resto
Giro:           ref | vendidas no período | estoque médio | giro | dias de cobertura
Parados:        sem venda há [60] dias, com estoque; mostra peças e custo parado
Ruptura:        vendia bem (classe A) e estoque 0 ou abaixo do mínimo
```
Existe: mais vendidos, menos vendidos e sem venda (Desempenho). Novo: ABC, giro, cobertura, ruptura, parado em R$. Tudo calculável só com vendas e estoque locais.

### 4.4 Gestão da empresa (lojas)
```
Lojas                                      [+ Adicionar loja]
+----------------------------------------------------------+
| Loja            | Faturamento | Vendas | Ticket | Margem  | Estoque R$ |
| Matriz          | 40.200      | 310    | 130    | 41%     | 88.000     |
| Loja 2          | 18.900      | 150    | 126    | 38%     | 31.000     |
| Total           | 59.100      | 460    | 128    | 40%     | 119.000    |
+----------------------------------------------------------+
[barras: faturamento por loja]   clique na linha = Painel filtrado nessa loja
Ações da loja: renomear, desativar, preços da loja (admin), local de estoque
```
Adicionar loja = nome + local de estoque (etapa 5). Produto e custo únicos; só o preço pode variar por loja.

### 4.5 Entradas e saídas com anexo
```
[+ Entrada] [+ Saída]            Período [..] Loja [v] Tipo [v]  Com anexo [ ]
Data | Tipo | Descrição | Categoria | Loja | Valor | Anexo (clipe)
Novo lançamento:
  Tipo ( ) Entrada ( ) Saída   Valor [   ]  Data [   ]  Loja [v]
  Categoria [v]  Conta [v]  Descrição [          ]
  Anexo: [Escolher arquivo] [Tirar foto]   (JPG, PNG, PDF até 10 MB)
  [Salvar]
```
Anexo guardado em pasta ao lado do banco (entra no backup); no banco só o caminho relativo e o nome. É o mesmo lançamento do Financeiro (não criar um segundo livro-caixa): a tela nova é um atalho filtrado, e o Financeiro ganha a coluna e o campo de anexo.

### 4.6 Consultor Nexus (10 regras, sem IA, sem internet)
Roda ao abrir o Painel, em segundo plano, sobre dados locais. Cada cartão: título, número, ação. Limiares ficam em Configurações (valores abaixo são o padrão).

| # | Regra | Dispara quando | Ação do botão |
|---|-------|----------------|---------------|
| 1 | Estoque parado | peça com estoque e sem venda há 60 dias (ou mais) e custo parado maior que R$ 500 | abre Parados |
| 2 | Risco de ruptura | produto classe A com cobertura menor que 15 dias | abre Compras com o item |
| 3 | Venda em queda | faturamento dos últimos 7 dias menor que 85% da média das 4 semanas anteriores | abre Desempenho |
| 4 | Margem baixa | produto com margem menor que 25% (ou abaixo da margem mínima) e 10 ou mais vendidas no mês | abre Calculadora de preços |
| 5 | Contas a vencer | a pagar nos próximos 7 dias maior que o saldo das contas | abre Contas a pagar |
| 6 | Atraso | qualquer título vencido há mais de 3 dias (a pagar ou a receber) | abre lista de atrasadas |
| 7 | Cliente sumido | cliente com 2 ou mais compras e sem comprar há 90 dias | abre Clientes (mensagem pronta) |
| 8 | Aniversariantes | clientes que fazem aniversário nos próximos 7 dias | abre Clientes |
| 9 | Descontos altos | desconto médio do mês acima de 12%, ou um vendedor com mais de 1,5x a média da loja | abre Desempenho por vendedor |
| 10 | Melhor horário/dia | o horário ou dia da semana que mais fatura (para escalar equipe; mostra se o dia mais fraco está abaixo de 50% da média) | abre Desempenho |

Extras possíveis depois: caixa com diferença na conferência, tamanho sempre em falta numa referência. Limite de 5 cartões por vez, ordem: financeiro urgente, ruptura, resto. O cartão some se o usuário toca em "Dispensar" (guardado por 7 dias).

## 5. Visão por conta
- Administrador: vê "Todas as lojas" e o seletor de loja em todas as telas; edita preço por loja.
- Usuário de loja: não vê o seletor; o filtro fixo é a loja dele; sem "Gestão da empresa" nem preço por loja.
- Regra única no serviço (não só na tela): toda consulta recebe `lojaId` obrigatório para quem não é admin.

## 6. Ordem de construção
1. Menu agrupado e abrir telas próprias (mover Compras, Fornecedores, Calculadora; remover Editor do catálogo). Sem mudança de banco.
2. Relatório de produtos (ABC, giro, parados): só leitura e cálculo, sem banco novo.
3. Relatório financeiro (fluxo, faixas de atraso) separando o DRE.
4. Anexo nos lançamentos (migração: coluna `Anexo` no financeiro + pasta de anexos + backup).
5. Painel com indicadores.
6. Lojas: tabela `Loja`, `LojaId` em vendas, estoque e lançamentos (depende da etapa 5 do plano: locais de estoque); Gestão da empresa e visão por conta.
7. Consultor Nexus (depende de 2, 3 e 5; começar com as regras 1, 2, 5, 6 e 8).

## 7. Riscos
- Banco: `LojaId` em tabelas existentes exige migração com valor padrão (loja atual) e backup antes; fazer em versão própria e testar com cópia de banco real.
- Desempenho: ABC, giro e Consultor leem muitas vendas; usar consulta agregada em SQL, cache de 5 minutos e execução em segundo plano para computador simples.
- Permissões: filtro por loja só na tela é furado; aplicar na camada de serviço e testar com usuário de loja.
- Sincronização: vendas de várias lojas só se consolidam se o dado chegar ao mesmo banco; hoje a sincronização é do catálogo. Se as lojas forem computadores separados, "desempenho por loja" precisa de envio de vendas (decisão pendente: banco único ou sincronização de vendas). Tiny/Olist tem reclamações justamente de sincronia multiempresa.
- Anexos: tamanho da pasta no backup e no Drive; limitar a 10 MB e comprimir fotos; não apagar anexo ao excluir lançamento sem confirmar.
- Dados antigos: sem custo ou sem data de entrada, giro e margem saem errados; mostrar "sem dados" em vez de zero.
- Limiares do Consultor: valores iniciais são palpite; deixar editáveis e revisar com a loja real depois de 2 semanas.

## 7. Lojas em computadores diferentes (decisão do Lucas)

Cada computador é uma loja, com banco SQLite próprio. Nada é compartilhado sozinho: só quando o usuário clica **"Compartilhar com a nuvem"** (Gestão da empresa ou Configurações > Backup e nuvem).

**Identificação:** Configurações guarda `LojaId` (código curto estável, ex.: `loja-1`) e o nome (ex.: "Loja Centro"). Gerado uma vez; não muda.

**Firestore (estrutura nova, sem tocar nas coleções que o ERP Life já usa):**
```
empresas/{empresaId}/lojas/{lojaId}                  nome, atualizadoEm, versão do Nexus
empresas/{empresaId}/lojas/{lojaId}/dias/{AAAA-MM-DD} resumo do dia: faturamento, nº vendas, peças, por forma de pagamento, por produto/tamanho, canceladas, trocas
empresas/{empresaId}/lojas/{lojaId}/estoque/{produto} quantidade por tamanho (foto do momento do envio)
empresas/{empresaId}/lojas/{lojaId}/lancamentos/{id}  entradas e saídas (anexo: só o nome; arquivo continua na loja, ou vai ao Storage numa etapa futura)
empresas/{empresaId}/precos/{produto}/lojas/{lojaId}  preço definido pelo administrador para a loja
```
- Envio **agregado por dia** (leve, barato, bom para computador simples). Venda por venda fica só no banco local.
- Reenviar o mesmo dia **substitui** o documento do dia (idempotente).

**Visão do administrador:** a Gestão da empresa lê `lojas/*` e mostra comparativo e desempenho de cada loja. Sem internet, mostra o último dado baixado (cache local) com a data.

**Preço por loja:** o administrador edita na Gestão da empresa; a loja **recebe** os preços ao clicar em "Receber da nuvem" (ou ao compartilhar) e os aplica localmente.

**Permissões:** no programa, administrador vê todas as lojas; usuário de loja vê só a sua. Atenção: o que impede uma loja de ler outra na nuvem são as **regras do Firestore** e as credenciais de cada computador, não a tela. Precisam de regras por loja (o usuário publica as regras; o Nexus nunca muda `firestore.rules`).

**Estoque:** como cada loja é um computador, "Loja 2" não é um local dentro do mesmo banco. O comparativo de estoque entre lojas vem da nuvem. Um local extra dentro da mesma loja (ex.: depósito) fica como opção futura.

**Riscos:** regras do Firestore por loja; relógio dos computadores (dia do resumo); dois computadores com o mesmo `LojaId` (cópia do banco); nunca gravar em coleções do ERP Life; testar só com servidor simulado, não com o Firestore real.
