# O que preservar no Nexus LS

Registro feito em 21/10/2026, na versão 1.1.0 (893 testes verdes), **antes** de refazer o visual. É a lista do que está bom e não pode se perder em nenhuma mudança. Regra de ouro: **o visual pode mudar à vontade (pasta `Views/`, `Resources/`), o `Core` e os testes não**. Se uma mudança deixar um teste vermelho, ela está errada, não o teste.

## Como mexer no visual sem estragar o resto

- Mude só `src/NexusLS.App/Views/*.xaml`, `Resources/Estilos.xaml` e `MainWindow.xaml`. Os ViewModels guardam a lógica da tela e são testados tecla por tecla; ao mover um botão, mantenha o nome do comando (`SalvarCommand`, `FinalizarCommand`...).
- Depois de cada ajuste: `dotnet build` e `dotnet test`. Tudo verde antes de seguir.
- **Design system** (`src/NexusLS.App/Resources/`, reunido por `Estilos.xaml`): `Tokens.xaml` (cores, fontes `Tam*`, espaços `Esp*`, cantos `Raio*`), `Tipografia.xaml`, `Botoes.xaml` (`BotaoPrimario`, `BotaoSecundario`, `BotaoPerigo`, `BotaoFantasma`, `BotaoIcone`), `Campos.xaml` (`Painel`, `FaixaAviso`, campos), `Tabelas.xaml` (DataGrid implícito: toda tabela herda cabeçalho, hover e seleção; linhas coloridas por regra usam `BasedOn="{StaticResource {x:Type DataGridRow}}"`), `Menu.xaml`. Cada dicionário inclui o `Tokens.xaml` (sem isso, templates não acham as cores). Um símbolo de ícone inexistente só falha ao abrir a tela, não ao compilar: gere as capturas (`NEXUSLS_PRINTS`) depois de mexer em XAML, **um teste por vez** (`--filter`), porque rodar todos juntos falha.
- Cores e tamanhos: usar os estilos de `Estilos.xaml` (`Painel`, `BotaoPrimario`, `CorPositivo`, `CorLinhaGrade`...) em vez de escrever cor na tela (as cores soltas das telas já foram trocadas por pincéis nomeados em 21/10/2026).
- Serviços: o `MainViewModel` recebe o `ContainerDeServicos` (`App.Servicos`) pelo construtor; novos ViewModels devem receber o serviço pelo construtor, não buscar em lugar global.

## Regras de negócio que não podem mudar

| # | Regra | Onde está | O que a protege |
|---|---|---|---|
| 1 | **Dinheiro em centavos inteiros** (`long`), nunca decimal/double | `Core/Services/Dinheiro.cs`, todos os modelos | `DinheiroTests`, `LucroBrutoTests` |
| 2 | **Venda + baixa de estoque na mesma transação**: grava tudo ou nada; estoque nunca fica negativo | `VendaService.FinalizarAsync` | `VendaServiceTests`, `GradeTamanhosTests` |
| 3 | **Nada é apagado**: venda, compra, lançamento, transferência, cheque só se **cancelam, com motivo** (mín. 3 letras), guardando quem e quando | `VendaService.CancelarAsync`, `FinanceiroService`, `CompraService`, `ContaFinanceiraService` | `VendaServiceTests`, `FinanceiroTests`, `ComprasTests`, `ContasCartaoChequeTests` |
| 4 | **Trilha de atividades** gravada na mesma transação da ação (se a ação falha, o registro não existe); nunca alterada nem apagada | `AtividadeService.Registrar` | `AtividadeTests` |
| 5 | **Backup antes de qualquer risco**: antes de migrar o banco, antes de restaurar e antes de "Apagar dados" (se o backup falha, nada acontece). Backups de segurança nunca são apagados sozinhos | `BackupService`, `LimpezaService`, `App.xaml.cs` | `BackupTests`, `LimpezaTests` |
| 6 | **Um só motor financeiro** para todo saldo, a receber, a pagar, projeção e resultado. Só o *realizado* mexe no saldo; cartão/cheque pendente é "em compensação" | `Core/Financeiro/MotorFinanceiro.cs`, `PosicaoFinanceiraService` | `MotorFinanceiroTests`, `PosicaoFinanceiraServiceTests` (loja inteira conferida à mão) |
| 7 | **Compra de mercadoria não é despesa** (o custo entra quando a peça é vendida); recebimento de dívida não é receita de novo; sangria/reforço/transferência não são receita nem despesa | `MotorFinanceiro`, `FinanceiroService.ResumoAsync` | `MotorFinanceiroTests`, `FinanceiroTests` |
| 8 | **Travas de cancelamento**: venda de cartão já liquidada ou cheque já compensado não cancela sem estornar antes; venda a prazo com pagamento não cancela | `VendaService.CancelarAsync` | `ContasCartaoChequeTests`, `DividaClienteTests` |
| 9 | **Dívida de cliente por FIFO**: cada pagamento abate a venda a prazo mais antiga; o saldo é sempre recalculado, nunca guardado | `ContaCorrente.cs` | `ContaClienteTests`, `DividaClienteTests` |
| 10 | **Fechamento do caixa imutável**: conferir de novo cria uma nova versão; diferença exige observação | `CaixaService.FecharAsync` | `FechamentoTests`, `CaixaServiceTests` |
| 11 | **Funcionários**: PIN guardado só como hash PBKDF2 com sal; 5 erros bloqueiam 30 s; o sistema nunca fica sem administrador; código de recuperação mostrado uma vez | `FuncionarioService` | `FuncionariosTests` |
| 12 | **Custo da venda guardado no item** (lucro bruto histórico não muda quando o custo do produto muda) | `ItemVenda.CustoUnitarioCentavos` | `LucroBrutoTests`, `RelatorioLucroTests` |
| 13 | **Apagar dados** só administrador, exige digitar `APAGAR`, uma transação, reinicia; contas, funcionários, configurações e atividades ficam | `LimpezaService`, `LimparDadosViewModel` | `LimpezaTests`, `LimparDadosViewModelTests` |
| 14 | **Catálogo online só por clique**, só campos públicos, nunca apaga na nuvem, custo/atacado/clientes nunca saem do computador | `Core/Sincronizacao/*` | `SincronizadorTests`, `FirebaseRestTests` |

## O que o PDV tem de bom (o jeito de usar é parte do produto)

- **Fluxo todo pelo teclado**: busca → tamanho → quantidade → desconto → pagamento → sucesso, sempre devolvendo o foco para a busca. Atalhos `F1`, `F2`, `F10`, `Espaço`, `Del`, `Esc`. Testado tecla por tecla (`FluxoPdvTests`, `ModoRapidoPdvTests`).
- **Proteção contra venda duplicada**: Enter repetido ignorado, venda em andamento bloqueia segundo envio, tela de sucesso ignora o primeiro Enter. Não remover essas guardas.
- **Venda em andamento salva sozinha** e volta se o programa fechar (`RascunhoTests`). Importante também para o atualizador.
- **Busca por nome, referência ou código de barras**, com leitor como prioridade e setas na lista.
- **Cadastro de produto**: código interno automático, EAN-13 interno, calculadora de margem/markup, grade de tamanhos, "vender sem baixa".

## Arquitetura que vale manter

- `Core` sem nenhuma referência ao WPF (regras testáveis sem abrir janela); `App` só com telas e ViewModels.
- Cada serviço recebe o banco e o relógio (`Func<DateTime>`) no construtor: testes determinísticos. Novos serviços devem fazer igual.
- Mudança de dado = migration; o programa aplica sozinho ao abrir, preservando os dados.
- Código, comentários, telas e commits em **português (BR)**.
- Atualizador: só HTTPS, só GitHub, confere SHA-256, nunca instala sem o clique, nenhum token no programa.

## Dívidas conhecidas (para não esquecer)

Fiscal (NFC-e) a confirmar com o contador · banco sem criptografia e sem backup na nuvem · um só computador · `.exe` sem assinatura digital e sem volta de versão · `OperadorAtual` global · 25 usos de `DateTime.Now` no `Core` · cálculo financeiro lê tudo a cada atualização · telas novas ainda não vistas rodando · gerador de capturas (`CapturaDeTelas`) instável (já falhava antes) · instância única e espera do banco ainda não testadas com o programa aberto · Firestore e atualizador nunca testados contra o serviço real.
