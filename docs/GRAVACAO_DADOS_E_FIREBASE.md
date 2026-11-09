# Gravação de dados: SQLite e Firebase

Resumo de como o Nexus guarda produtos, clientes e vendas, e como isso conversa com o Firebase.

## SQLite (`%LOCALAPPDATA%\NexusLS\nexus.db`)

É a fonte da verdade. Toda alteração grava aqui primeiro, em transação ("tudo ou nada").

### Produto (`Produtos`)
Nome, referência, código de barras, código interno, categoria, **cores** (texto separado por vírgula, só informativo),
preços de venda / atacado / custo (centavos), estoque atual e mínimo, vender sem baixa, sobra, ativo, foto, `Guid`
e data de alteração.

### Tamanho (`TamanhosProduto`)
Uma linha por produto e tamanho, com o estoque daquele tamanho. Grades prontas (`GradesDeTamanho`), cadastradas de uma vez
na aba "Tamanhos e estoque":

| Grade | Tamanhos |
|---|---|
| Letras | PP P M G GG G1 G2 G3 G4 G5 G6 G7 G8 G9 G10 |
| Calça e bermuda | 36 38 40 ... 70 (de 2 em 2) |
| Camisa de botão | 1 2 3 ... 10 |

### Cliente (`Clientes`)
Nome, razão social, CPF/CNPJ (só dígitos; a tela mostra a máscara enquanto se digita), telefone, e-mail, observação,
inscrição estadual, **endereço, número, complemento, bairro, cidade, estado (UF) e CEP**, limite de crédito, crédito a favor,
ativo, data de cadastro e `Guid`. A dívida não fica no cliente: é calculada pelos títulos das vendas a prazo e pelos pagamentos.

### Venda (`Vendas`, `ItensVenda`, `Movimentacoes`)
A venda, os itens (nome e preço copiados no momento), a baixa de estoque por tamanho e as movimentações ligadas à venda
são gravados na mesma transação. Cancelar não apaga: marca motivo, quem e quando.

## Firebase

| Destino | O que recebe | Situação |
|---|---|---|
| Catálogo online (projeto próprio) | produtos | fila pronta; precisa de configuração |
| Nuvem operacional (projeto próprio) | vendas, clientes, caixa, pedidos, financeiro, produção | fila pronta; precisa de configuração |
| ERP PDV Life (`pdv---lifesurf`) | leitura (importação) | funciona; gravação **desligada** por padrão |

Cada loja usa o seu projeto: **Conexão com a nuvem** (menu) tem o botão "Colar configuração do Firebase", que lê o
`firebaseConfig` do console. Nada fica fixo no programa.

Uma fila no SQLite (`FilaSincronizacao`) recebe tudo o que muda. Reenviar nunca duplica, porque o id do documento é o `Guid`.

## Mudanças desta versão
- Produto: campo **Cores** e grades de tamanho prontas.
- Cliente: **cidade, estado, endereço, número, bairro, complemento, CEP e inscrição estadual** (migração
  `ClienteEnderecoEProdutoCores`).
- Importação do ERP passa cidade, estado, endereço e inscrição estadual do cliente.
- Documentos de cliente enviados à nuvem operacional incluem o endereço.

## Pendente
- Gravar no banco do ERP no formato dele (só com a chave `GravacaoAtivada`, backup e teste em empresa separada).
- Resumo de estoque com gráficos (Estoque).
- Exportar/importar o endereço do cliente no CSV.
