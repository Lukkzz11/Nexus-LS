# Plano da próxima fase (aprovado pelo Lucas)

Ordem de execução. Cada etapa vira uma versão publicada, com testes.

1. **Pesquisa de ERPs e desenho da nova Gestão** — como Bling, Tiny, Omie, Conta Azul, Linx e Nuvemshop organizam o painel; proposta de menu e de relatórios para aprovar antes de construir.
2. **Trocas e vale-troca** — busca lista todas as vendas (filtros por data, categoria, cliente, nº do cupom); clicar abre tela de detalhe da venda e da troca; conferir ligação com caixa e estoque (volta ao tamanho certo, vale-troca, caixa).
3. **Caixa → Pedidos** — enviar venda do caixa para Pedidos (fica um aviso no caixa); em Pedidos, arrastar o cartão com o mouse entre as etapas; visual melhorado.
4. **Leitor de código de barras no PDV** — conferir BarTender x PDV (regra única referência + número do tamanho); bips da mesma referência acumulam numa linha com grade de tamanhos; comprovante mostra tamanho e quantidade.
5. **Estoque e identificação da loja** — só Resumo, Estoque da loja e Estoque fábrica; cada computador é UMA loja, com banco próprio e uma identificação (Loja 1, Loja 2...) em Configurações; o comparativo entre lojas vem da nuvem (etapa 6). Produto e custo são únicos; **o preço pode ser ajustado por loja** (só o administrador, via nuvem).
6. **Gestão** — cada item abre tela própria; Compras, Fornecedores e Calculadora de preços vêm para a Gestão; Editor do catálogo sai (vira outro programa); relatórios só financeiro e só produtos; **Gestão da empresa**: lojas com desempenho de cada uma e botão para adicionar loja, lançamento de entradas e saídas com anexo (foto ou documento). **Visão por conta**: administrador completo vê todas as lojas e edita preços de cada uma; usuário de uma loja vê só a dela.
7. **Consultor Nexus (sugestões por regras)** — cartões de sugestão de vendas e de finanças dentro do programa, calculados por regras, leves, sem internet e sem instalar nada. **Sem IA local (Ollama/DeepSeek)**: pesada para computador simples, fica de fora.
8. **Configurações em tela separada + tema claro/escuro/automático (segue o Windows).**
9. **BarTender (por último)** — seleção por retângulo: clicar e arrastar no fundo da área de criação seleciona os elementos tocados; camadas podem subir e descer (ordem de empilhamento).

**Lojas = computadores diferentes, cada um com seu banco.** Só quando o usuário quiser (botão), o computador envia seus dados ao Firestore, separados por loja (`empresas/{empresa}/lojas/{lojaId}/...`). O administrador vê todas as lojas lendo dessa nuvem; quem é de uma loja vê só a dela. Detalhes em docs/PROPOSTA_GESTAO.md, seção 7.

Suposições: vários locais de estoque mudam a quantidade (e o preço por loja, só admin); produto e custo continuam únicos; anexos ficam numa pasta ao lado do banco e entram no backup; venda enviada a Pedidos continua contando no faturamento.
