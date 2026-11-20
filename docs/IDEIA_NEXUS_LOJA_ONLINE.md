# Ideia: Nexus Loja Online (app de catálogo online e site)

Rascunho de 19/11/2026. Marcado como **[existe]**, **[proposto]** ou **[decisão pendente]**. Nada deste documento foi construído ainda; ele serve para fechar o escopo antes de criar o projeto.

## 1. O problema

Hoje o catálogo online é uma parte escondida do Nexus LS: o dono precisa abrir o PDV, ir em Gestão, editar a vitrine e clicar em publicar. O cliente final só vê um catálogo. A loja não tem um site de verdade (endereço próprio, página inicial, sobre, contato) e o pedido do cliente não fecha com pagamento.

Ideia: um **app separado** (como o BarTender), focado só em vender pela internet. Ele conversa com o Nexus LS, mas pode ser aberto sozinho.

## 2. O que já existe e será reaproveitado

- **[existe]** Catálogo web em React/Vite/Tailwind (`web/catalogo`), com editor de vitrine (modelos, fontes, cores, banners, ordem dos produtos).
- **[existe]** Publicação no Firebase (projeto A, público só para leitura): produtos, fotos, estoque, vitrine (`Core/Vitrine`, `Core/Sincronizacao`).
- **[existe]** Pedido feito pelo site chega do Firebase para o Nexus LS (`PedidoService`).
- **[existe]** WhatsApp semi-automático (`Core/Comunicacao/WhatsApp.cs`): ofertas e comprovantes.
- **[existe]** Cloudflare Tunnel e `web/construir.ps1` para servir páginas.
- **[existe]** Fiscal completo do produto (NCM, CEST, impostos) e fotos.

## 3. O que o app novo faria

1. **Site da loja**: página inicial, catálogo, página do produto, sobre, contato, links (Instagram, WhatsApp, mapa). Modelos prontos, sem o dono mexer em código.
2. **Catálogo com carrinho**: o cliente escolhe produto e tamanho, vê o estoque real (só mostra o que tem), monta o pedido.
3. **Fechar pelo WhatsApp** (versão 1, sem custo): o pedido vira uma mensagem pronta para a loja, com itens, tamanhos e total.
4. **Pagamento** (versão 2): Pix e cartão por um provedor (Mercado Pago é o candidato). Só com o dono configurando a própria conta.
5. **Pedido volta para o Nexus LS**: entra como pedido, baixa estoque quando confirmado, aparece no caixa. O SQLite continua sendo a fonte oficial.
6. **Ofertas e cupons**: preço promocional por produto, cupom de desconto, "novidades" e "sobras da loja" (já existe a marca de sobra).
7. **Link e QR code do catálogo**: para imprimir na loja, colocar na etiqueta e mandar no WhatsApp.
8. **Painel simples**: visitas, produtos mais vistos, pedidos do dia.

## 4. Como seria por dentro

| Peça | Escolha proposta |
|---|---|
| App do dono | Desktop Windows (WPF, .NET 10), igual ao resto do Nexus. Edita o site, publica, vê pedidos. |
| Site do cliente | Páginas estáticas geradas (React/Vite) hospedadas de graça (Firebase Hosting ou Cloudflare Pages). |
| Dados públicos | Firebase projeto A (leitura pública), como hoje. Nada de dado financeiro ou de cliente. |
| Pedidos | Coleção própria no Firebase; o Nexus LS importa (já existe o caminho). |
| Domínio | Endereço grátis da hospedagem; domínio próprio opcional (o dono compra). |
| Distribuição | Mesmo release do GitHub e mesmo instalador/atualizador do Nexus. |

Regra mantida: **grava local, publica só no clique, a nuvem é cópia.**

## 5. Etapas sugeridas

1. **Base** (separar o que existe): criar o projeto `NexusLS.LojaOnline`, mover o editor de vitrine e o botão de publicar para ele, com o Nexus LS abrindo o app por um atalho.
2. **Site completo**: páginas além do catálogo (início, sobre, contato), modelos e prévia no próprio app.
3. **Carrinho e pedido por WhatsApp**: sem pagamento, sem custo.
4. **Pedido de volta no Nexus LS**: confirmar, baixar estoque, gerar venda.
5. **Pagamento Pix/cartão**: provedor, webhook de confirmação, conciliação com o caixa.
6. **Ofertas, cupons, painel de visitas.**

## 6. Riscos

- Estoque no site pode ficar desatualizado: o catálogo é um retrato. Mitigar mostrando "confirmar disponibilidade" e conferindo no pedido.
- Pagamento online exige conta do provedor do dono, segurança de chaves e regras de estorno; fica para depois do WhatsApp.
- Regras do Firebase (Firestore e Storage) ainda precisam ser testadas contra um projeto real (pendência 12 do roadmap).
- LGPD: pedido do site tem nome e telefone do cliente; guardar o mínimo e avisar no site.

## 7. Decisões pendentes (preciso da sua resposta)

1. É **um site completo da loja** ou só **catálogo + carrinho**? (muda o tamanho das etapas 2 e 3)
2. Primeiro fechamento do pedido: **só WhatsApp** ou já **Pix/cartão**?
3. Hospedagem: **grátis com endereço do provedor** ou o dono já tem domínio?
4. O app novo **substitui** a vitrine dentro do Nexus LS ou **convive** com ela por um tempo?
5. Vendas só **varejo** ou também **atacado** (com preço e quantidade mínima próprios)?
6. Nome: **Nexus Loja Online**, **Nexus Vitrine** ou outro?
