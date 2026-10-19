# Nexus LS

PDV (frente de caixa) e controle de estoque para loja de roupas e acessórios, feito em C# com .NET e WPF, para Windows.
Funciona sem internet e sem login, com uma loja só. Todo o programa, o código e os comentários estão em português.

## Telas

O programa abre na tela **Início** (indicadores e atalhos); o menu lateral tem Início e mais três grupos:

| Grupo | Tela | Para que serve |
|---|---|---|
| **Início** | **Início** | Tela de abertura: vendas de hoje (com a comparação com ontem), ticket médio, situação do caixa, produtos abaixo do estoque mínimo (com os piores), quanto os clientes devem (e quanto já venceu), situação do backup e atalhos para todas as telas. Vendas canceladas nunca entram nos números. Em Configurações dá para escolher abrir direto no PDV. |
| **Vendas** | **PDV** | Frente de caixa, todo pelo teclado (veja o fluxo abaixo). |
| | **Caixa** | Vendas de um dia, resumo por forma de pagamento, detalhe, **cancelar** venda (pede o motivo, registra quem e quando cancelou, devolve o estoque se você quiser; a venda nunca é apagada), reimprimir cupom e exportar CSV. |
| | **Fechamento** | Abrir o caixa com troco inicial, registrar sangria, reforço, despesa e entrada avulsa, e fechar conferindo dinheiro, cartão e Pix contra o esperado. Diferença exige observação. Fechamentos nunca mudam: conferir de novo cria uma nova versão. |
| | **Mais vendidos** | Ranking de produtos num período, com opção de separar por tamanho, e exportar CSV. |
| **Estoque** | **Estoque da loja** | Lista de produtos, cadastro (preço de varejo e de atacado, custo, mínimo, referência, categoria, código de barras com botão **Gerar** para EAN-13 interno, código interno automático INT-000001 e calculadora de margem/preço), grade de tamanhos com estoque de cada um e ajuste de estoque com motivo (entrada, perda, correção). Itens abaixo do mínimo ficam em amarelo. Todo ajuste vira uma linha no histórico. |
| | **Etiquetas** | Etiquetas com nome, preço, tamanho e código de barras (Code 128; número só de dígitos usa o conjunto C, mais curto), em bobina ou folha A4, com prévia. |
| **Gestão** | **Clientes** | Cadastro e histórico de compras de cada cliente. |
| | **Relatórios** | Vendas por período: total, número de vendas, ticket médio, descontos, canceladas, por dia, por forma de pagamento, por categoria, **lucro bruto** (total vendido menos o custo das mercadorias; o custo fica guardado em cada venda, e vendas antigas usam o custo atual e avisam que é estimativa) e exportar CSV (vendas e itens). |
| | **Configurações** | Dados da loja e do cupom, impressora, largura do papel, impressão automática, com prévia do cupom, e **backup** (automático e manual, escolha da pasta, restauração). |

## Fluxo do PDV (tudo pelo teclado)

```
BUSCA ─Enter→ TAMANHO ─Enter→ QUANTIDADE ─Enter→ (volta à busca)
BUSCA vazia + carrinho cheio ─Enter→ DESCONTO ─Enter→ PAGAMENTO ─Enter→ SUCESSO ─Enter→ nova venda
```

- O foco volta sempre para a busca. A busca aceita nome, referência ou código de barras.
- **Cadastro de produto:** o código interno (INT-000001...) é automático e nunca repete; o botão Gerar cria um código de barras EAN-13 que começa com 2 (faixa de uso interno). Não se cadastra dois produtos ativos com o mesmo nome e a mesma referência. Margem = lucro sobre o preço; markup = lucro sobre o custo.
- **Vender sem baixa:** marque no produto (serviço, encomenda) para o PDV não conferir nem baixar o estoque dele; o cancelamento só devolve o que de fato saiu do estoque. Em Configurações, desligar "Perguntar o tamanho" faz o PDV ir direto à quantidade quando o produto só tem uma opção (modo rápido). Ligado é o padrão.
- **Enter na busca:** se você desceu com ↑ ↓, vale o item destacado; senão o código de barras exato tem prioridade (leitor); senão vale o primeiro resultado.
- **Tamanho:** sempre abre ao escolher o produto. Grade de 3 colunas, navegue com ← → ↑ ↓ ou digite a letra/número do tamanho (M, GG, 38). No **atacado** aparece também a opção "Sem tamanho".
- **Quantidade:** digite o número ou use ↑ soma / ↓ subtrai (mínimo 1); limitada ao estoque do tamanho.
- **Desconto:** ← escolhe R$, → escolhe %. Pode ficar vazio.
- **Pagamento:** dinheiro, Pix, cartão débito, cartão crédito (1 a 4 parcelas), cheque e a prazo (exige cliente). ← → trocam a forma, ↑ ↓ mudam as parcelas. No dinheiro o valor recebido já vem com o total, o troco é calculado e não deixa finalizar com valor menor. Na venda a prazo, digite o nome do cliente: se ele não existir, o primeiro Enter o cadastra.
- **Sucesso:** botões Reimprimir cupom e Nova venda (← → alternam, Enter confirma).
- **Carrinho:** quantidade, remover item, **editar o preço** direto na linha (por unidade ou pelo total da linha), esvaziar com confirmação. A venda em andamento é **salva sozinha** e volta se você fechar o programa.

### Atalhos

| Tecla | Ação |
|---|---|
| `F1` | Abre o PDV, de qualquer tela |
| `F2` | Foco na busca (e seleciona o texto) |
| `Espaço` | Com a busca vazia, alterna a tabela de preço: varejo / atacado |
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
- **Nunca se apaga uma venda**: só se cancela, com motivo obrigatório (mínimo 3 letras), guardando quem cancelou e quando. O estoque volta ao mesmo tamanho, a menos que se escolha não devolver (peça danificada). O nome do operador fica em Configurações. Venda cancelada não entra em totais.
- **Caixa por turno**: só um caixa aberto por vez; as vendas pertencem ao turno pelo horário, então turnos não se cruzam. Cancelar venda de um caixa já fechado avisa para refazer a conferência.
- **Todo movimento de estoque deixa registro** (estoque inicial, ajustes, vendas e cancelamentos), com o tamanho.
- Cada produto tem **dois preços**: varejo e atacado. Atacado vazio vale o de varejo.
- O item da venda guarda o nome, o tamanho e o preço **da hora da venda**.
- Todo produto tem a opção "Sem tamanho" (onde fica o estoque de quem não usa grade).
- **Venda a prazo** exige cliente e vira uma dívida (vence em 30 dias). Em Clientes você recebe pagamentos parciais: cada pagamento abate primeiro a venda mais antiga. O limite de crédito do cliente (zero = sem limite) bloqueia vendas a prazo que o ultrapassem, e uma venda a prazo que já recebeu pagamento não pode ser cancelada. Pagamentos recebidos entram no esperado do caixa aberto. Não há cobrança, juros nem carnê.

## Cupom e etiquetas

- **Cupom não fiscal** impresso pela impressora do Windows (bobina térmica ou comum), sem abrir caixa de diálogo, na impressora escolhida em Configurações (ou na padrão do Windows). Ele sai sozinho ao finalizar a venda (dá para desligar em Configurações), e o botão **Reimprimir** existe no PDV e no Caixa. Se a impressora falhar, a venda continua gravada e o programa avisa.
- **Etiquetas** usam o código de barras do produto em **Code 128** (letras, números e símbolos sem acento). Produto sem código, ou com acento no código, sai sem barras. Formatos: bobina 50 × 30 mm, bobina 40 × 25 mm e folha A4 3 × 7 (63,5 × 38,1 mm); na folha dá para começar numa etiqueta que não seja a primeira, para aproveitar uma folha já usada em parte. A impressora é escolhida na caixa de impressão do Windows. **Legibilidade:** se o código de barras ficaria com barras mais finas que 0,25 mm no papel escolhido (código comprido em etiqueta estreita), a tela avisa e a impressão é bloqueada até você trocar de papel ou remover o item. **Meus modelos de bobina:** salve bobinas de tamanho próprio (nome, largura e altura em mm); ficam em `%LOCALAPPDATA%\NexusLS\etiquetas.json`.
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
    Backup/              Backup e restauração do banco, nomes, retenção e preferências
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

- **Backup (Configurações > Backup dos dados):** o programa faz cópias do banco sozinho e sob demanda, **com o programa aberto**.
  - **Automático:** ao abrir e a cada 15 minutos ele confere se já passou o intervalo escolhido (desligado, 1, 6, 12 horas, todo dia ou toda semana; padrão: todo dia). Se falhar, aparece um aviso na tela.
  - **Manual:** botão "Fazer backup agora".
  - **Onde:** você escolhe a pasta (padrão `Documentos\Nexus LS\Backups`). Se a pasta ficar no mesmo disco do banco, a tela avisa: prefira pen drive, HD externo ou pasta sincronizada na nuvem.
  - **Nome:** `nexus_AAAA-MM-DD_HHMMSS_motivo.db` (motivo: manual, auto, pre-atualizacao, pre-restauracao).
  - **Segurança da cópia:** usa a API de backup do SQLite (copiar o `.db` à mão pode sair incompleto por causa do arquivo `-wal`), grava num arquivo temporário, confere a integridade (`PRAGMA integrity_check`) e só então dá o nome final. Cópia que não passa na conferência é descartada.
  - **Quantos guardar:** você define (de 3 a 200). Os mais antigos, manuais e automáticos, são apagados; só arquivos com o nome padrão acima são tocados. Os de segurança (antes de atualizar ou de restaurar) nunca são apagados sozinhos.
  - **Antes de atualizar o banco:** quando uma versão nova muda a estrutura das tabelas, o programa faz um backup antes. Se a mudança falhar, o banco volta a esse backup.
  - As preferências ficam em `%LOCALAPPDATA%\NexusLS\backup.json`, fora do banco.
- **Restaurar:** em Configurações, escolha o backup na lista (ou nenhum, para procurar um arquivo) e clique em "Restaurar". O programa confere o arquivo, **guarda uma cópia dos dados atuais** (`pre-restauracao`), troca o banco e reinicia. Se o banco estiver em uso ou algo falhar, nada é alterado. Restaurar à mão continua possível: com o programa fechado, substitua o `nexus.db` (apague os `-wal` e `-shm` ao lado).
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

Login, nuvem, multiempresa, loja online, financeiro completo (cobrança, juros, carnê), fábrica, funcionários, impressora fiscal.
