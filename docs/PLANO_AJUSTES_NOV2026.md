# Plano de ajustes — Nov/2026

Divisão: **Claude** = lógica delicada (dinheiro, caixa, dados). **OpenCode** = telas, layout, trabalho mecânico.
Regra geral: cada item termina com testes passando, commit e release com versão maior (ver `gh release list`).

## A. Nexus — caixa e vendas
| # | Item | Quem |
|---|---|---|
| 1 | Corrigir abertura do caixa: com o caixa aberto a venda deve passar | Claude |
| 2 | Obrigar fechar o caixa ao encerrar o dia / fechar o app | Claude |
| 3 | Baixar relatório do dia em PDF | OpenCode |
| 4 | Cupom: logo mais preta (P&B puro com limiar), nome da empresa maior, caixa "CUPOM NÃO FISCAL" com linha contínua (sem pontilhado) | OpenCode |

## B. Nexus — produtos e estoque
| # | Item | Quem |
|---|---|---|
| 5 | Importar outro CSV: comparar com o existente, adicionar só o novo, atualizar só o que mudou; pré-visualização antes de gravar | Claude |
| 6 | Calculadora de preço: ao alterar o valor, recalcula tudo e grava o novo preço no produto | Claude |
| 7 | Editor de produto: refazer edição/recadastro de tamanhos | OpenCode (Claude revisa regras de estoque por tamanho) |
| 8 | Código de barras na criação: mesmo padrão (referência + tamanho) no Nexus e no Bartender | Claude (regra) + OpenCode (telas) |

## C. Pix (Mercado Pago + SyncPay)
Base (Claude): `IPaymentGateway`, `MercadoPagoGateway`, `SyncPayGateway`, `PaymentRouter` (menor taxa; tarifas e faixas em configuração, não no código), `PaymentService`, `PaymentRepository` (SQLite), idempotência, venda pendente sem internet (consultar status antes de nova cobrança), credenciais protegidas no Windows (quem digita é o usuário), logs sem segredos.
Confirmação do pagamento: **consulta de status por tempo** (polling); Webhook fica para uma fase posterior (exige serviço público na nuvem).
Faixas iniciais (a confirmar nas contas): até R$ 75,75 Mercado Pago; 75,76–150 SyncPay; acima de 150 Mercado Pago.
Tela "Configurações de Pagamento" (ativar/desativar, credenciais, tarifas, regras, desempate, histórico, custos por gateway): OpenCode, depois da base.
Testes obrigatórios: aprovado, expirado, cancelado, queda de internet, cobrança duplicada, escolha pelo menor custo, registro no SQLite.

## D. Programa de impressão / Bartender (etiquetas)
| # | Item | Quem |
|---|---|---|
| 11 | Estudo de papel: modelo "perfil de papel" (largura, altura, colunas 1/2/3, espaço entre etiquetas, margens) — **fazer primeiro** | Claude |
| 9 | Criar uma etiqueta e enviar para todos os produtos; tamanho, referência e código de barras trocados por produto | Claude (dados/variáveis) + OpenCode (tela) |
| 10 | Local para imprimir etiqueta: escolher modelo, impressora e quantidade | OpenCode |

## E. Atualização / instalador
| # | Item | Quem |
|---|---|---|
| 12 | Atualizar instalador e programa de update; migrar o update antigo do outro PC | Claude |

## Ordem
1. Claude: 1, 2, 6. Em paralelo OpenCode: 3, 4 (arquivos diferentes).
2. Claude: 5, 12, 11.
3. Pix: base (Claude) → tela (OpenCode).
4. Etiquetas: 9 e 10 após o 11.

## Em aberto
- "Bartender" e "programa de impressão" são o Estúdio de etiquetas do Nexus ou outro programa? (pasta)
- Versão do update antigo no outro PC.
