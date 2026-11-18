# Plano: NCM/CFOP por produto e aniversário do cliente

Objetivo: guardar NCM e CFOP em cada produto (preparação para a NFC-e) e a data de aniversário em cada cliente, **sem quebrar** o que já está sincronizado com o Firebase.

## Por que exige cuidado
`Produto` e `Cliente` são `EntidadeSincronizavel`: viajam para o Firestore do ERP Life. Dois riscos:
1. **Banco local:** colunas novas precisam de migration; bancos antigos devem abrir sem perder dados.
2. **Nuvem:** o envio de produtos usa *máscara de campos* (`FirestoreRestDestino.CamposDoErp`): só os campos listados são escritos. Campos novos que **não** entrarem na lista nunca sobem e não tocam o que já existe no documento. É o comportamento seguro: NCM/CFOP e aniversário ficam só no Nexus no começo.

## Etapas (cada uma é uma versão pequena)
1. **Schema local (migration aditiva):** `Produto.Ncm` (texto, 8 dígitos, opcional), `Produto.Cfop` (texto, 4 dígitos, opcional), `Cliente.DataNascimento` (data, opcional). Tudo nulo por padrão; nenhuma coluna existente muda. Teste: abrir banco migrado de uma versão anterior (padrão `BancoDeTeste`, que usa `Migrate`).
2. **Sem sincronizar:** não adicionar os campos novos a `CamposDoErp` nem ao hash de sincronização (`hashErp`), para a edição de NCM não disparar reenvio de todos os produtos nem gerar conflito. Teste: alterar só o NCM não muda o hash.
3. **Tela:** campos no cadastro de produto (validação 8/4 dígitos; se vazio, usa o padrão da página Fiscal) e no cadastro de cliente (DatePicker). Importação do ERP (`ImportadorErp`) não apaga esses campos quando reimporta o produto.
4. **Aniversário do cliente:** aviso ao abrir (mesmo padrão dos aniversariantes de funcionários, com flag própria em Notificações) e filtro "aniversariantes do mês" na lista de clientes. Telefone/WhatsApp só leitura; nada é enviado automaticamente.
5. **Backup:** colunas novas entram sozinhas no backup do SQLite; conferir a restauração de backup antigo.
6. **(Opcional, depois)** subir NCM/CFOP ao ERP: só com campo novo nas regras do ERP e nas `CamposDoErp`, combinado com quem mantém o ERP.

## Fora de escopo
Emissão de NFC-e, cálculo de impostos e envio de mensagens de aniversário.
