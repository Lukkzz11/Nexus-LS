# Manual do usuário do Nexus

Guia curto das telas e das Configurações. O Nexus guarda os dados neste computador e continua funcionando sem internet; a nuvem (Google Drive, Firebase) é opcional.

## Como o programa é organizado

- **Início**: tela de abertura (ou o PDV, se você ligar essa opção em Configurações > Vendas). Mostra vendas do dia e dos últimos 7 dias, mais vendidos da semana, caixa, estoque baixo, crediário e contas a pagar que vencem logo. Vendas canceladas nunca entram nos totais.
- **PDV (tecla F1, de qualquer tela)**: onde se faz a venda.
- **Gestão**: botão do menu lateral que abre as telas administrativas. Quais telas aparecem depende do acesso de cada funcionário (veja Segurança).

## PDV (vendas)

1. Com o caixa fechado, o PDV pede o troco que está na gaveta para abrir. O Nexus só finaliza venda com o caixa aberto e do dia de hoje.
2. Busque o produto por nome, referência ou código de barras (F2) e tecle Enter. Se o produto tem tamanhos, escolha o tamanho.
3. Ajuste quantidade, desconto e forma de pagamento (setas do teclado). Espaço, com a busca vazia, alterna varejo e atacado.
4. Feche a venda com F10 (ou Enter com a busca vazia). Del remove o item selecionado.
5. Descontos acima do limite configurado pedem o PIN de um gerente ou administrador.
6. Venda a prazo (crediário) usa o limite de crédito do cliente e a venda é barrada quando passa dele.
7. Para usar um vale-troca no pagamento, tecle F6.

Lista completa de atalhos: Configurações > Atalhos do teclado.

## Telas da Gestão

- **Agenda**: compromissos e lembretes.
- **Clientes**: cadastro (nome, CPF/CNPJ, telefone, e-mail), limite de venda a prazo, saldo a favor na loja, histórico de alterações e ficha do crediário. Na ficha de quem tem dívida vencida há o botão Cobrar no WhatsApp: o WhatsApp abre com a mensagem pronta e quem envia é você.
- **Pedidos**: quadro de pedidos; escolha um para ver os detalhes e adicionar produtos.
- **Compras**: registro de compras de fornecedores; busque o produto e tecle Enter para adicionar.
- **Fornecedores**: cadastro de fornecedores.
- **Estoque da loja**: cadastro de produto, grade de tamanhos e ajuste de estoque. O estoque só muda na aba "Tamanhos e estoque", para sempre deixar registro.
- **Estoque de fábrica**: ordens de produção e transferência para a loja. Se já existe produto com a mesma referência, as peças somam no estoque dele.
- **Etiquetas**: fila de etiquetas por produto, com opção de começar numa etiqueta específica da folha. A impressora e o desenho ficam no Nexus BarTender.
- **Calculadora de preços**: calcula o custo e o preço a partir de itens, despesas do lote e taxas das formas de pagamento.
- **Trocas e vale-troca**: a peça devolvida volta ao estoque e o valor pago vira vale-troca.
- **Financeiro**: lançamentos de receitas e despesas, com parcelas (vencem todo mês no mesmo dia). Ao corrigir um lançamento, o original fica cancelado no histórico com o motivo.
- **Contas e saldos**: saldos das contas financeiras.
- **Cartões**: informe a taxa que a operadora descontou numa venda; a taxa vira despesa e o valor inteiro da venda continua sendo receita.
- **Cheques** e **Cheques emitidos**: complete número, banco e data dos cheques recebidos e controle os emitidos.
- **Fechamento**: conferência do caixa. Conte o que há de verdade e digite; campo vazio confere com o esperado. Diferença exige observação. Gera PDF com turnos, vendas por forma de pagamento, sangrias, reforços, despesas e diferenças.
- **Relatórios**: gráficos e tabelas do período (mais vendidos por peças e por valor líquido, por categoria).
- **Mais vendidos** (ranking de produtos e como os clientes pagaram) e **Desempenho de vendas**: acompanhamento das vendas.
- **Catálogo online** e **Editor do catálogo**: publicação do catálogo na nuvem (Firebase). Salvar guarda neste computador; Publicar envia. A senha fica protegida pelo Windows.
- **Funcionários**: cadastro da equipe, perfis e acessos.
- **Atividades**: trilha de ações (autorizações de gerente, cancelamentos etc.). Não é apagada.
- **Configurações**: veja abaixo.

## Configurações

O menu lateral é dividido por assunto. Ctrl+S salva.

**Loja**
- *Loja e cupom*: identificação da empresa, endereço, modelo e logo do cupom, mensagens, impressora e largura do papel (a prévia acompanha).
- *Aparência*: tamanho do conteúdo das telas e tema.
- *Etiquetas*: padrão que a tela de Etiquetas já abre escolhido.

**Vendas e caixa**
- *Vendas*: abrir direto no PDV, perguntar o tamanho, desconto sugerido de atacado, limite de desconto sem gerente.
- *Parâmetros*: valores sugeridos (troco inicial, margem desejada, estoque mínimo de produtos novos).
- *Caixa*: sangria máxima sem gerente e exigir PIN de gerente para cancelar venda.
- *Crediário e clientes*: multa e juros por atraso (aparecem na ficha como valor a cobrar por fora; não mudam a dívida registrada) e limite de crédito de clientes novos. O prazo da venda a prazo é de 30 dias.
- *Comissões*: percentual padrão e por vendedor sobre a venda líquida, com opção de descontar devoluções. Só calcula; o pagamento é por fora.
- *Estoque e produtos*: aviso de estoque baixo (produto abaixo do mínimo cadastrado).

**Pagamentos e fiscal**
- *Pagamentos (Pix)*: configuração do Mercado Pago.
- *Fiscal*: dados preparatórios para NFC-e. O Nexus ainda não emite nota fiscal.

**Equipe e segurança**
- *Segurança*: bloqueio por PIN após inatividade, esconder a Gestão de quem não tem acesso, PIN errado e registro de acessos. Só vale com funcionários cadastrados.

**Dados e nuvem**
- *Backup e nuvem*: backup local (quantos guardar), restauração e envio ao Google Drive (pasta "Nexus Backups"), que exige criar um Client ID e Client Secret no Google Cloud.
- *Integrações*: situação dos serviços, exportação mensal para o contador (3 planilhas do Excel: vendas, itens vendidos e contas) e resumo do dia para o celular.
- *Comunicação*: texto da mensagem de cobrança por WhatsApp.
- *Apagar dados*: começar do zero ou tirar dados de teste. Faz um backup de segurança antes; funcionários, configurações e atividades nunca são apagados.

**Sistema**
- *Sistema e licença*: dados deste computador e licença; também as atualizações (o Nexus procura versão nova ao abrir e só instala quando você clica em Atualizar agora).
- *Atalhos do teclado*: lista de teclas.
- *Notificações*: avisos reunidos numa janela ao abrir o Nexus (estoque baixo, contas a pagar, crediário vencido). Backup que falhou e nova versão avisam sozinhos.
- *Várias lojas*: ainda não disponível.

## Resumo do dia para o celular

Em Configurações > Integrações, clique em **Gerar resumo...** e escolha uma pasta. O Nexus cria uma página HTML única, que não precisa de internet nem de programa extra, e se adapta a tela pequena e ao modo escuro. Ela traz:

- vendas do dia, número de peças, ticket médio e comparação com ontem;
- caixa (aberto desde que horas);
- últimos dias, com barras;
- mais vendidos da semana;
- contas a pagar (próximos 7 dias e atrasadas) e crediário (em aberto e vencido);
- estoque baixo.

O arquivo se chama `resumo-do-dia.html` e é sobrescrito a cada geração. Marque **Salvar com a data no nome** para guardar um por dia (`resumo-AAAA-MM-DD.html`). Envie o arquivo por WhatsApp, e-mail ou Drive e abra no navegador do celular. É uma foto do momento; gere de novo para atualizar.

## Backup e segurança em poucas linhas

- Faça backup com frequência e guarde uma cópia fora do computador (Google Drive).
- Configure funcionários e PINs para controlar descontos, cancelamentos e o acesso à Gestão.
- Antes de qualquer limpeza de dados o Nexus faz um backup de segurança, que nunca é apagado sozinho.
