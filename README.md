# PDV Simples

PDV (frente de caixa) e controle de estoque simples para Windows, feito em C# / .NET com WPF.
Projeto de aprendizado. Sem login, sem internet, uma loja só.

> **Status:** em construção. Etapas prontas: esqueleto da solution, models e banco (SQLite + migrations).
> Telas e regras de venda ainda serão adicionadas. Este README é atualizado a cada etapa.

## Estrutura

```
src/PdvSimples.Core   regra de negócio, sem interface (Models, Data, Services)
src/PdvSimples.App    interface WPF (Views, ViewModels) — MVVM com CommunityToolkit.Mvvm
tests/PdvSimples.Tests testes xUnit (SQLite em memória)
```

## Requisitos

- Windows 10/11
- [SDK do .NET](https://dotnet.microsoft.com/download) 10 (`dotnet --version`)

## Como rodar

```bash
dotnet build
dotnet test
dotnet run --project src/PdvSimples.App
```

Na primeira execução o banco é criado e atualizado automaticamente (`Database.Migrate()`).

## Banco de dados e backup

O banco é um único arquivo SQLite:

```
%LOCALAPPDATA%\PdvSimples\pdv.db
```

(normalmente `C:\Users\<seu usuário>\AppData\Local\PdvSimples\pdv.db`)

**Backup:** feche o programa e copie o arquivo `pdv.db` para um pendrive ou nuvem.
**Restaurar:** com o programa fechado, substitua o `pdv.db` pela cópia.

## Gerar o executável / instalador

Gera uma pasta com o programa pronto, que roda sem instalar o .NET na máquina:

```bash
dotnet publish src/PdvSimples.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publicar
```

O arquivo `publicar\PdvSimples.App.exe` pode ser copiado para qualquer PC com Windows 64 bits.
(Um instalador `.msi`/`.exe` com atalhos não faz parte desta versão.)

## Migrations (para quem for mexer nos models)

Após alterar uma classe em `Models`, gere uma migration:

```bash
dotnet tool restore
dotnet ef migrations add NomeDaMudanca --project src/PdvSimples.Core
```

## Regras importantes

- Todo dinheiro é guardado em **centavos** (`long`), nunca em `double`.
- Venda e baixa de estoque acontecem na mesma transação; estoque não fica negativo.
- Venda cancelada nunca é apagada, só marcada como cancelada.
