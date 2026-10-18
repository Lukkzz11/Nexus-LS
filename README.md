# Nexus LS

PDV (frente de caixa) e controle de estoque para loja de roupas e acessórios, feito em C# com .NET e WPF, para Windows.
Funciona sem internet e sem login, com uma loja só. Todo o programa, o código e os comentários estão em português.

## Telas

O menu lateral tem três grupos:

| Grupo | Tela | Para que serve |
|---|---|---|
| **Vendas** | **PDV** | Frente de caixa, todo pelo teclado (veja o fluxo abaixo). |
| | **Caixa** | Vendas de um dia, resumo por forma de pagamento, detalhe, **cancelar** venda (devolve o estoque; a venda nunca é apagada), reimprimir cupom e exportar CSV. |
| | **Mais vendidos** | Ranking de produtos num período, com opção de separar por tamanho, e exportar CSV. |
| **Estoque** | **Estoque da loja** | Lista de produtos, cadastro (preço de varejo e de atacado, custo, mínimo, referência, código de barras), grade de tamanhos com estoque de cada um e ajuste de estoque com motivo (entrada, perda, correção). Itens abaixo do mínimo ficam em amarelo. Todo ajuste vira uma linha no histórico. |
| | **Etiquetas** | Etiquetas com nome, preço, tamanho e código de barras (Code 128), em bobina ou folha A4, com prévia. |
| **Gestão** | **Clientes** | Cadastro e histórico de compras de cada cliente. |
| | **Relatórios** | Vendas por período: total, número de vendas, ticket médio, descontos, canceladas, por dia, por forma de pagamento, e exportar CSV (vendas e itens). |
| | **Configurações** | Dados da loja e do cupom, impressora, largura do papel, impressão automática, com prévia do cupom. |

## Fluxo do PDV (tudo pelo teclado)

```
BUSCA ─Enter→ TAMANHO ─Enter→ QUANTIDADE ─Enter→ (volta à busca)
BUSCA vazia + carrinho cheio ─Enter→ DESCONTO ─Enter→ PAGAMENTO ─Enter→ SUCESSO ─Enter→ nova venda
```

- O foco volta sempre para a busca. A busca aceita nome, referência ou código de barras.
- **Enter na busca:** se você desceu com ↑ ↓, vale o item destacado; senão o código de barras exato tem prioridade (leitor); senão vale o primeiro resultado.
- **Tamanho:** sempre abre ao escolher o produto. Grade de 3 colunas, navegue com ← → ↑ ↓. No **atacado** aparece também a opção "Sem tamanho".
- **Quantidade:** ↑ soma, ↓ subtrai (mínimo 1), limitada ao estoque do tamanho.
- **Desconto:** ← escolhe R$, → escolhe %. Pode ficar vazio.
- **Pagamento:** dinheiro, Pix, cartão débito, cartão crédito (1 a 4 parcelas), cheque e a prazo (exige cliente). ← → trocam a forma, ↑ ↓ mudam as parcelas. No dinheiro o valor recebido já vem com o total, o troco é calculado e não deixa finalizar com valor menor. Na venda a prazo, digite o nome do cliente: se ele não existir, o primeiro Enter o cadastra.
- **Sucesso:** botões Reimprimir cupom e Nova venda (← → alternam, Enter confirma).
- **Carrinho:** quantidade, remover item, **editar o preço** direto na linha (por unidade ou pelo total da linha), esvaziar com confirmação. A venda em andamento é **salva sozinha** e volta se você fechar o programa.

### Atalhos

| Tecla | Ação |
|---|---|
| `F2` | Foco na busca (e seleciona o texto) |
| `F8` | Alterna a tabela de preço: varejo / atacado |
| `F10` | Atalho extra: avança para o fechamento da venda (como Enter com a busca vazia) |
| `Enter` | Avança a etapa |
| `Esc` | Fecha a janela atual e volta à busca (não esvazia o carrinho) |
| `Del` | Remove o item selecionado do carrinho |
| `Ctrl+S` / `Ctrl+N` | Salvar / novo, nas telas de cadastro |

### Proteções contra venda duplicada

Enter repetido (tecla segurada) é ignorado; Enter já tratado por outro elemento (botão, campo) não é tratado de novo; uma venda em andamento bloqueia um segundo envio; com uma janela aberta a busca não recebe o Enter; e a tela de sucesso ignora o Enter nos primeiros instantes, para um Enter duplo não pular a confirmação.

## Regras importantes

- **Dinheiro em centavos** (número inteiro, `long`), nunca `double`. A tela converte para "R$ 12,50".
- **Venda e baixa de estoque** (por produto **e tamanho**) acontecem na **mesma transação** do banco: ou grava tudo, ou nada.
- **Estoque nunca fica negativo**, e o operador é avisado.
- **Nunca se apaga uma venda**: só se cancela (o estoque volta ao mesmo tamanho). Venda cancelada não entra em totais.
- **Todo movimento de estoque deixa registro** (estoque inicial, ajustes, vendas e cancelamentos), com o tamanho.
- Cada produto tem **dois preços**: varejo e atacado. Atacado vazio vale o de varejo.
- O item da venda guarda o nome, o tamanho e o preço **da hora da venda**.
- Todo produto tem a opção "Sem tamanho" (onde fica o estoque de quem não usa grade).
- **Venda a prazo** só registra a venda com o cliente. O programa **não controla recebimentos** (financeiro está fora do escopo).

## Cupom e etiquetas

- **Cupom não fiscal** impresso pela impressora do Windows (bobina térmica ou comum), sem abrir caixa de diálogo, na impressora escolhida em Configurações (ou na padrão do Windows). Ele sai sozinho ao finalizar a venda (dá para desligar em Configurações), e o botão **Reimprimir** existe no PDV e no Caixa. Se a impressora falhar, a venda continua gravada e o programa avisa.
- **Etiquetas** usam o código de barras do produto em **Code 128** (letras, números e símbolos sem acento). Produto sem código, ou com acento no código, sai sem barras. Formatos: bobina 50 × 30 mm, bobina 40 × 25 mm e folha A4 3 × 7 (63,5 × 38,1 mm); na folha dá para começar numa etiqueta que não seja a primeira, para aproveitar uma folha já usada em parte. A impressora é escolhida na caixa de impressão do Windows.
- **Exportar** gera CSV para o Excel brasileiro (separador `;`, vírgula decimal, acentos corretos).

## Estrutura do projeto

```
NexusLS.slnx
src/
  NexusLS.Core/          Regras de negócio, sem nenhuma referência à interface
    Models/              Produto, tamanhos, Venda, Cliente, configuração da loja e enums
    Data/                PdvDbContext (acesso ao banco)
    Migrations/          Histórico de versões da estrutura do banco
    Services/            Venda, Produto, Estoque, Cliente, Carrinho, Rascunho, Cupom, Relatório, CSV...
    Etiquetas/           Code 128, formatos e diagramação das etiquetas
  NexusLS.App/           Interface WPF (MVVM com CommunityToolkit.Mvvm)
    Views/               Telas (XAML)
    ViewModels/          Lógica de cada tela
    Converters/          Conversores de exibição
    Impressao/, Etiquetas/   Impressão no Windows e desenho das etiquetas
    Resources/           Cores e estilos
tests/
  NexusLS.Tests/         Testes das regras (xUnit, SQLite em memória)
  NexusLS.App.Tests/     Testes das telas: o fluxo do PDV tecla por tecla, sem abrir janela
```

A regra de negócio fica no `Core`, que **não conhece o WPF**, e por isso é testada sem abrir janela. O fluxo do PDV (e as outras telas) é testado nos ViewModels, simulando as teclas.

## Visual

A interface usa a biblioteca **[WPF-UI](https://github.com/lepoco/wpfui)** (estilo Fluent, do Windows 11): barra de título e fundo Mica, caixas de texto, tabelas, listas, abas, calendário e ícones modernos. As cores e os estilos próprios (botões, cartões, menu) ficam em `src/NexusLS.App/Resources/Estilos.xaml`.

## Requisitos

- Windows 10 ou 11
- [SDK do .NET](https://dotnet.microsoft.com/download) 10 (confira com `dotnet --version`)

## Como rodar e testar

Na pasta do projeto:

```bash
dotnet build
dotnet test
dotnet run --project src/NexusLS.App
```

Na primeira execução o banco é criado sozinho (`Database.Migrate()` ao abrir o programa).

## Onde ficam os dados e como fazer backup

Tudo fica em **um único arquivo** SQLite:

```
%LOCALAPPDATA%\NexusLS\nexus.db
```

Normalmente `C:\Users\<seu usuário>\AppData\Local\NexusLS\nexus.db`. Para abrir a pasta, cole `%LOCALAPPDATA%\NexusLS` na barra de endereço do Explorer.

- **Backup:** feche o programa e copie o `nexus.db` para um pendrive ou serviço de nuvem. Se houver arquivos `nexus.db-wal` e `nexus.db-shm` ao lado, copie junto.
- **Restaurar:** com o programa fechado, substitua o `nexus.db` pela cópia.
- **Começar do zero:** com o programa fechado, apague o `nexus.db`; um novo, vazio, é criado na próxima abertura.
- **Vindo da versão antiga (PDV Simples):** na primeira abertura o programa copia `%LOCALAPPDATA%\PdvSimples\pdv.db` para `nexus.db`. O arquivo antigo não é apagado.
- A venda em andamento fica em `%LOCALAPPDATA%\NexusLS\rascunho.json` (pode ser apagado sem problema; ele só guarda o carrinho aberto).

## Gerar o executável

Gera um único `.exe` que roda em qualquer Windows 64 bits, **sem precisar instalar o .NET**:

```bash
dotnet publish src/NexusLS.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publicar
```

O resultado é `publicar\NexusLS.exe` (cerca de 140 MB, porque leva o .NET dentro). Copie esse arquivo para o computador do caixa e crie um atalho na Área de Trabalho. Os dados ficam no `%LOCALAPPDATA%` do computador onde o programa roda.

Um instalador com assistente (`.msi`) não faz parte desta versão. Para um arquivo menor, troque `--self-contained true` por `false`; aí o computador de destino precisa ter o [.NET Desktop Runtime](https://dotnet.microsoft.com/download) 10.

## Mexendo nos models (migrations)

Uma *migration* é um "passo de versão" da estrutura do banco. Depois de alterar uma classe em `Models/` (ou o `PdvDbContext`), gere uma nova:

```bash
dotnet tool restore
dotnet ef migrations add NomeDaMudanca --project src/NexusLS.Core
```

O programa aplica as migrations pendentes sozinho ao abrir, preservando os dados existentes.

## Fora do escopo desta versão

Login, nuvem, multiempresa, loja online, financeiro completo (contas a receber), fábrica, funcionários, impressora fiscal.
