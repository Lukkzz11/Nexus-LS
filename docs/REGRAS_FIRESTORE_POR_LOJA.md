# Regras do Firestore por loja

Arquivo pronto: [`firebase/firestore.lojas.rules`](../firebase/firestore.lojas.rules). Nada foi publicado nem testado no Firestore real.

**Qual projeto:** o mesmo do **Catálogo online** (o Nexus usa essas credenciais para as lojas). Nunca o projeto do ERP Life; o app recusa gravar nele.

## Passo a passo
1. **Authentication:** crie uma conta (e-mail e senha) para o administrador e uma para cada outra loja.
2. **Em cada computador:** Catálogo online > informe projeto, chave da API e a conta daquela loja. Em Gestão da empresa > Lojas, preencha "Empresa na nuvem" (igual em todos) e copie o "Código da loja".
3. **No arquivo de regras:** preencha `ehDonoDaLicenca()` com os e-mails já publicados hoje e `contas()` com um e-mail por linha (`loja: '*'` = administrador).
4. **Console > Firestore > Regras:** cole o arquivo, teste no Simulador e publique.

## Testes no Simulador (provider "password", com o e-mail no token)
Troque `E` pela empresa e `L2` pelo código da loja 2.

| Conta | Operação | Caminho | Esperado |
|---|---|---|---|
| loja 2 | get | `/empresas/E/lojas/L2` | permitido |
| loja 2 | create | `/empresas/E/lojas/L2/dias/2026-01-01` | permitido |
| loja 2 | get | `/empresas/E/lojas/OUTRA` | negado |
| loja 2 | create | `/empresas/E/precos/p1/lojas/L2` | negado |
| admin | create | `/empresas/E/precos/p1/lojas/L2` | permitido |
| sem login | get | `/empresas/E/lojas/L2` | negado |
| sem login | get | `/produtos/qualquer` | permitido (catálogo público) |

## Cuidados
- Loja que também publica o catálogo precisa estar nas duas listas.
- Banco copiado de um computador para outro repete o "Código da loja": não faça isso.
- Loja nova: criar a conta, acrescentar a linha em `contas()` e publicar de novo.
