# NFC-e: o que existe e o que falta decidir

Pesquisa rápida na web em nov/2026. Preço e regra fiscal mudam: confirme na fonte oficial e com o contador.

## O que o Nexus já tem
- Dados fiscais por produto (NCM, CFOP, CSOSN/CST, alíquotas) e configuração em Configurações > Fiscal.
- Chave de acesso de 44 dígitos e XML **de conferência** da última venda (sem valor fiscal).
- **Não tem:** assinatura com certificado, QR Code, envio à SEFAZ, cancelamento, contingência nem DANFE.
- O código IBGE do município ainda não é guardado.

## Os dois caminhos
| | API de um provedor | Direto na SEFAZ |
|---|---|---|
| Custo | mensalidade | sem mensalidade |
| Trabalho no Nexus | menor | bem maior |
| Mudanças de leiaute (IBS/CBS) | o provedor acompanha a maior parte | nós acompanhamos |
| Dependência | do provedor | só da SEFAZ |

**Recomendação:** começar por um provedor. A reforma tributária está gerando notas técnicas novas em sequência.

## Provedores (valores vistos)
- **Focus NFe:** plano Solo R$ 89,90/mês para 1 CNPJ ([preços](https://focusnfe.com.br/precos/)). Exige certificado A1. Confirme quantas notas o plano inclui.
- **NFE.io:** plano base R$ 220/mês na [página de NFC-e](https://nfe.io/precos/emissao-nfce), sem data clara.
- **Nuvem Fiscal e PlugNotas:** sem preço oficial encontrado. Pedir cotação.

## Perguntar ao contador
1. Regime: MEI, Simples Nacional ou normal?
2. A loja já tem inscrição estadual e está credenciada para NFC-e na UF?
3. Quais NCM, CFOP e CSOSN usar nos produtos?
4. O que já é obrigatório informar de IBS/CBS no seu regime? As fontes que achei divergem.

## O que providenciar
- Certificado digital **A1** (e-CNPJ).
- **CSC** e ID do CSC, no portal da SEFAZ do estado (homologação e produção). Confirme se a versão atual do QR Code ainda os exige.
- Volume médio de notas por mês, para escolher o plano.

Certificado, senha e CSC não vão para o chat nem para o Git: você digita no app ou no painel do provedor.
