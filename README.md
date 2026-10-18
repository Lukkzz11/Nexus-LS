# PDV Simples

PDV (frente de caixa) e controle de estoque simples para Windows, feito em C# com .NET e WPF.
Projeto de aprendizado: sem login, sem internet, uma loja só.

## O que o programa faz

| Tela | Para que serve |
|---|---|
| **Caixa (PDV)** | Vender: buscar produto, montar o carrinho, desconto, forma de pagamento, troco e finalizar. |
| **Vendas** | Ver as vendas de um dia (padrão: hoje), o total, o detalhe de cada uma e **cancelar** (devolve o estoque; a venda nunca é apagada, só marcada como cancelada). |
| **Produtos** | Cadastrar, editar, desativar/reativar e buscar por nome ou código de barras. |
| **Estoque** | Ver o estoque (itens abaixo do mínimo ficam em amarelo), fazer ajustes (entrada, perda, correção) com motivo e ver o histórico de movimentações do produto. |

### Atalhos do Caixa

| Tecla | Ação |
|---|---|
| `F2` | Vai para o campo de busca |
| `Enter` | Adiciona o produto (código de barras exato ou nome) |
| `3*789` + `Enter` | Adiciona 3 unidades do código/nome `789` |
| `↑` `↓` | Escolhe na lista quando a busca por nome acha vários produtos |
| `F6` / `F7` / `F8` | Dinheiro / Pix / Cartão |
| `Del` | Remove o item selecionado do carrinho |
| `F10` | Finaliza a venda |
| `Esc` | Fecha a lista de resultados; sem lista, cancela a venda (pede confirmação) |

Na tela Produtos: `Ctrl+N` (novo) e `Ctrl+S` (salvar). Na tela Estoque: `Enter` aplica o ajuste.

## Regras importantes

- **Dinheiro é guardado em centavos** (número inteiro, `long`), nunca em `double`. A tela converte para "R$ 12,50".
- **Venda e baixa de estoque acontecem na mesma transação** do banco: ou grava tudo, ou nada.
- **Estoque nunca fica negativo.** O operador é avisado (carrinho, finalização e ajustes).
- **Todo movimento de estoque deixa registro** (cadastro com estoque inicial, ajustes, vendas e cancelamentos).
- Produto desativado some do caixa, mas continua no histórico de vendas.
- O item da venda guarda o nome e o preço **da hora da venda**; mudar o produto depois não altera o histórico.

## Estrutura do projeto

```
PdvSimples.slnx
src/
  PdvSimples.Core/       Regra de negócio, sem nenhuma referência à interface
    Models/              Produto, Venda, ItemVenda, MovimentacaoEstoque e enums
    Data/                PdvDbContext (acesso ao banco)
    Migrations/          Histórico de versões da estrutura do banco
    Services/            ProdutoService, EstoqueService, VendaService, Dinheiro
  PdvSimples.App/        Interface WPF (MVVM com CommunityToolkit.Mvvm)
    Views/               Telas (XAML)
    ViewModels/          Lógica de cada tela
    Converters/          Conversores de exibição (centavos -> "R$ 12,50" etc.)
    Resources/           Cores e estilos
tests/
  PdvSimples.Tests/      Testes xUnit (SQLite em memória)
```

A regra de negócio fica no `Core`, que **não conhece o WPF**. Por isso ela é testada sem abrir janela.

## Requisitos

- Windows 10 ou 11
- [SDK do .NET](https://dotnet.microsoft.com/download) 10 (confira com `dotnet --version`)

## Como rodar

Na pasta do projeto:

```bash
dotnet build
dotnet test
dotnet run --project src/PdvSimples.App
```

Na primeira execução o banco é criado sozinho (`Database.Migrate()` ao abrir o programa).

## Onde fica o banco e como fazer backup

Todos os dados ficam em **um único arquivo** SQLite:

```
%LOCALAPPDATA%\PdvSimples\pdv.db
```

Normalmente `C:\Users\<seu usuário>\AppData\Local\PdvSimples\pdv.db`. Para abrir a pasta, cole `%LOCALAPPDATA%\PdvSimples` na barra de endereço do Explorer.

- **Backup:** feche o programa e copie o `pdv.db` para um pendrive ou serviço de nuvem. Se houver arquivos `pdv.db-wal` e `pdv.db-shm` ao lado, copie junto.
- **Restaurar:** com o programa fechado, substitua o `pdv.db` pela cópia.
- **Começar do zero:** com o programa fechado, apague o `pdv.db`; um novo, vazio, é criado na próxima abertura.

## Gerar o executável

Gera um único `.exe` que roda em qualquer Windows 64 bits, **sem precisar instalar o .NET**:

```bash
dotnet publish src/PdvSimples.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publicar
```

O resultado é `publicar\PdvSimples.App.exe` (cerca de 140 MB, porque leva o .NET dentro). Copie esse arquivo para o computador do caixa e crie um atalho na Área de Trabalho. Os dados ficam no `%LOCALAPPDATA%` do computador onde o programa roda.

Um instalador com assistente (`.msi`) não faz parte desta versão. Para um arquivo menor, troque `--self-contained true` por `false`; aí o computador de destino precisa ter o [.NET Desktop Runtime](https://dotnet.microsoft.com/download) 10.

## Mexendo nos models (migrations)

Uma *migration* é um "passo de versão" da estrutura do banco. Depois de alterar uma classe em `Models/` (ou o `PdvDbContext`), gere uma nova:

```bash
dotnet tool restore
dotnet ef migrations add NomeDaMudanca --project src/PdvSimples.Core
```

O programa aplica as migrations pendentes sozinho ao abrir.

## Fora do escopo desta versão

Login, nuvem, multiempresa, impressora fiscal/cupom, relatórios avançados, financeiro e clientes.
