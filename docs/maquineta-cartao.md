# Maquineta de cartão ligada ao Nexus (estudo)

Objetivo: o PDV manda o valor para a maquineta, o cliente passa o cartão e o Nexus só dá a venda como paga quando a maquineta confirma, sem digitar valor duas vezes.

## Formas de ligar (do mais simples ao mais pesado)

| Forma | Como liga | Serve para o Nexus? |
|---|---|---|
| **Mercado Pago Point (API Orders)** | Nuvem: o Nexus cria um pedido por internet, o terminal Point (Wi-Fi/4G) busca o pedido e cobra. Sem cabo. | **Melhor encaixe**: já temos token, webhook e túnel do Pix. |
| TEF com PinPad (Stone Connect, Cielo, Getnet, PayGo, Sitef) | Cabo USB/serial ou Bluetooth do PinPad ao PC + software TEF instalado (DLL/serviço local). | Possível, mas cada adquirente exige contrato, homologação e DLL própria. É o caminho do cabo. |
| SmartPOS próprio da adquirente (Cielo LIO, Stone, Getnet) | Aplicativo no próprio terminal + API/deep link. | Depende de cada fabricante. Mais trabalho, sem ganho para quem já usa Mercado Pago. |
| Digitar o valor na maquininha e marcar "cartão" no Nexus | Nenhuma. | É o que se faz hoje. |

## Mercado Pago Point (o que a documentação diz)

- Integração pela **API Orders** (`POST /v1/orders`, tipo `point`). A API antiga de "intenção de pagamento" será desativada. Terminais citados: Point Smart 1/2 e Point Pro 2/3 (a lista fechada de compatíveis com PDV **não** aparece nas páginas lidas: confirmar com o Mercado Pago antes de comprar).
- **Não há cabo nem Bluetooth entre PC e terminal** na documentação: a ligação é pela nuvem. O PC e o terminal só precisam de internet.
- Terminal precisa estar em **modo PDV** (`PATCH /terminals/v1/setup` com `operating_mode: PDV`, reiniciar o terminal). No modo padrão (STANDALONE) ele não recebe pedidos.
- Pré-requisitos: aplicação no painel de integrações, loja e ponto de venda (um terminal por ponto), terminal vinculado pelo app do Mercado Pago, parcelas configuradas na conta.
- Fluxo: criar pedido (valor, `external_reference`, validade de 30 s a 3 h) → o terminal mostra o valor → cliente paga → **webhook** `order.processed` (ou `order.failed`, `order.expired`, `order.canceled`, `order.refunded`) → o Nexus confirma. O aviso exige resposta 200/201 em até 22 s e traz assinatura `x-signature` para validar.
- Cancelar: `POST /v1/orders/{id}/cancel` (se já está no terminal, precisa do cabeçalho `x-allow-cancelable-status: at_terminal`, e só vale após o webhook). Estornar: `POST /v1/orders/{id}/refund`, sempre total, até 90 dias.
- Taxas: não constam nas páginas lidas. Ver no contrato da conta.

## Como encaixa no Nexus

Quase tudo já existe da parte do Pix:
- Receptor de webhook local + túnel Cloudflare (hoje só `/webhook/mercadopago` para Pix; a mesma URL pode receber os eventos `order.*` com outra checagem de tipo).
- Token do Mercado Pago protegido (DPAPI), tela de configuração, conferência e estorno.
- Regra de ouro mantida: o aviso só diz QUAL pedido mudou; o status real é sempre consultado na API antes de dar a venda como paga.

Trabalho novo (estimativa: uma sessão boa de trabalho):
1. `IGatewayCartao` + `MercadoPagoPoint` (criar pedido, consultar, cancelar, estornar), parecido com `IGatewayPix`.
2. Tela de configuração: escolher loja, ponto de venda, terminal e ativar modo PDV (listar terminais pela API).
3. PDV: forma de pagamento "Cartão na maquineta" que cria o pedido, mostra "Aguardando o cliente na maquineta..." e conclui ao confirmar. Débito/crédito/parcelas vindos da resposta.
4. Validação da assinatura `x-signature` do webhook.
5. Testes com usuário de teste do Mercado Pago (o sandbox do Point exige usuário de teste).

## O que eu preciso de você para testar de verdade
- Ter (ou comprar) um terminal Point compatível e confirmar com o Mercado Pago que o modelo aceita modo PDV.
- Access Token e vincular o terminal no app do Mercado Pago (você digita o token; eu nunca recebo no chat).
- Um dia de teste com a maquineta na mão: nada de pagamento real sem você presente.

## Se for outra maquineta (Stone, Cielo, Getnet, Rede...)
Cada uma tem seu pacote (Stone Connect, Cielo LIO/TEF, Getnet TEF, Rede). Em geral exigem contrato com a adquirente, DLL/serviço local e homologação. Antes de qualquer trabalho: qual maquineta a loja usa hoje? Se o contrato é com outra adquirente, o caminho do cabo (PinPad + TEF) passa pelo software que ela fornecer.

Fontes: documentação do Mercado Pago Developers (Point / Orders API) e páginas de TEF das adquirentes, lidas em 13/11/2026. A documentação mudou recentemente (migração para Orders): reconferir antes de implementar.
