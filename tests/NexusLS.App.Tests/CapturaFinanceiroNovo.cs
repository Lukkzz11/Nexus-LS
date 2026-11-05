using System.IO;
using System.Windows;
using System.Windows.Controls;
using NexusLS.App.ViewModels;
using NexusLS.App.Views;
using NexusLS.Core.Financeiro;
using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.App.Tests;

/// <summary>
/// Desenha em PNG as telas do Financeiro novo (classificações, corrigir/retirar, DRE, ajuste de título, cheques emitidos).
/// Só roda quando NEXUSLS_PRINTS aponta para uma pasta; nos testes normais não faz nada. Rode um teste por vez.
/// </summary>
[Collection("OperadorAtual")]
public sealed class CapturaFinanceiroNovo : IDisposable
{
    private static readonly DateTime Agora = new(2026, 10, 20, 15, 0, 0);
    private static DateTime Dia(int d) => new(2026, 10, d);

    private readonly BancoDeTeste _banco = new();

    public CapturaFinanceiroNovo() => OperadorAtual.Nome = "Ana";

    public void Dispose()
    {
        OperadorAtual.Nome = null;
        _banco.Dispose();
    }

    private static string? Pasta()
    {
        var saida = Environment.GetEnvironmentVariable("NEXUSLS_PRINTS");
        if (string.IsNullOrWhiteSpace(saida)) return null;
        Directory.CreateDirectory(saida);
        return saida;
    }

    private static async Task AteAsync(Func<bool> condicao)
    {
        for (var i = 0; i < 300 && !condicao(); i++) await Task.Delay(10);
    }

    private static void SelecionarAba(FrameworkElement tela, int aba)
    {
        TabControl? Achar(DependencyObject o)
        {
            foreach (var f in LogicalTreeHelper.GetChildren(o).OfType<DependencyObject>())
            {
                if (f is TabControl t) return t;
                var r = Achar(f);
                if (r is not null) return r;
            }
            return null;
        }
        var tab = Achar(tela);
        if (tab is not null) tab.SelectedIndex = aba;
    }

    /// <summary>Loja de demonstração: vendas, dívida de cliente com ajuste, contas com todas as classificações e cheques emitidos.</summary>
    private async Task SemearAsync()
    {
        var produtos = new ProdutoService(_banco.Fabrica);
        var camiseta = (await produtos.CadastrarAsync(new DadosProduto("Camiseta Básica Algodão", "789", 5990, 2000, 5), 80)).Id;
        var bone = (await produtos.CadastrarAsync(new DadosProduto("Boné Aba Reta", "555", 2990, 800, 3), 80)).Id;
        var clientes = new ClienteService(_banco.Fabrica, () => Agora);
        var maria = await clientes.CadastrarAsync(new DadosCliente("Maria Souza", LimiteCreditoCentavos: 500000));
        var joao = await clientes.CadastrarAsync(new DadosCliente("João Pereira"));

        VendaService Vendas(DateTime quando) => new(_banco.Fabrica, () => quando);
        await Vendas(Dia(3)).FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(camiseta, 12) }, 0, FormaPagamento.Pix));
        await Vendas(Dia(6)).FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(bone, 8) }, 0, FormaPagamento.Dinheiro, ValorRecebidoCentavos: 30000));
        await Vendas(Dia(9)).FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(camiseta, 4) }, 0, FormaPagamento.CartaoCredito));
        await Vendas(Dia(10)).FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(camiseta, 3) }, 0, FormaPagamento.APrazo, ClienteId: maria.Id));
        await Vendas(Dia(12)).FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(bone, 10) }, 0, FormaPagamento.APrazo, ClienteId: maria.Id));
        await Vendas(Dia(14)).FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(bone, 4) }, 0, FormaPagamento.APrazo, ClienteId: joao.Id));
        await new ClienteService(_banco.Fabrica, () => Dia(15)).RegistrarPagamentoAsync(maria.Id, 10000, FormaPagamento.Pix);

        // título a prazo da Maria: desconto combinado
        var titulos = new TituloPrazoService(_banco.Fabrica, () => Agora);
        var contaMaria = await clientes.ContaAsync(maria.Id);
        await titulos.EditarAsync(contaMaria.Titulos[1].VendaId, contaMaria.Titulos[1].TotalCentavos - 3000, Dia(28), "desconto combinado por telefone");

        // contas e saldos
        var contas = new ContaFinanceiraService(_banco.Fabrica, () => Agora);
        var banco = await contas.CriarAsync(new DadosConta("Banco Itaú", TipoContaFin.Banco, 800000, Dia(1), Padrao: true));
        await contas.CriarAsync(new DadosConta("Cofre da loja", TipoContaFin.Caixa, 50000, Dia(1)));

        // financeiro com todas as classificações
        var cat = new CategoriaFinanceiraService(_banco.Fabrica, () => Agora);
        await cat.GarantirPadraoAsync();
        await cat.CriarAsync(TipoLancamento.Despesa, "Brindes para clientes");
        var fin = new FinanceiroService(_banco.Fabrica, () => Agora);
        await fin.CriarAsync(new DadosLancamento(TipoLancamento.Despesa, "Aluguel da loja", 150000, Dia(5), "Aluguel", "Imobiliária Central", JaPago: true, Forma: FormaPagamento.Pix));
        await fin.CriarAsync(new DadosLancamento(TipoLancamento.Despesa, "Conta de luz", 38000, Dia(18), "Energia", "Neoenergia"));
        await fin.CriarAsync(new DadosLancamento(TipoLancamento.Despesa, "Anúncios Instagram", 22000, Dia(8), "Marketing", null, JaPago: true, Forma: FormaPagamento.CartaoCredito));
        await fin.CriarAsync(new DadosLancamento(TipoLancamento.Despesa, "Retirada do dono", 100000, Dia(10), null, "Lucas", JaPago: true, Forma: FormaPagamento.Pix, Classificacao: ClassificacaoLancamento.RetiradaProprietario));
        await fin.CriarAsync(new DadosLancamento(TipoLancamento.Receita, "Aporte para estoque", 300000, Dia(2), null, "Lucas", JaPago: true, Forma: FormaPagamento.Pix, Classificacao: ClassificacaoLancamento.Aporte));
        await fin.CriarAsync(new DadosLancamento(TipoLancamento.Receita, "Reembolso do fornecedor", 12000, Dia(11), null, "Malharia Sol", JaPago: true, Forma: FormaPagamento.Pix, Classificacao: ClassificacaoLancamento.OutraEntrada));
        await fin.CriarAsync(new DadosLancamento(TipoLancamento.Receita, "Conserto de máquina", 25000, Dia(22), "Serviços"));
        var errada = (await fin.CriarAsync(new DadosLancamento(TipoLancamento.Despesa, "Embalagens", 45000, Dia(7), "Embalagens", "Papelaria", JaPago: true, Forma: FormaPagamento.Dinheiro)))[0];
        await fin.CorrigirAsync(errada.Id, new DadosCorrecaoLancamento("Embalagens", 4500, Dia(7), "Embalagens", "Papelaria"), "digitei um zero a mais");
        var teste = (await fin.CriarAsync(new DadosLancamento(TipoLancamento.Despesa, "Lançamento de teste", 9900, Dia(9), "Outros", null, JaPago: true, Forma: FormaPagamento.Dinheiro)))[0];
        await fin.RetirarAsync(teste.Id, "lançamento de teste");

        // cheques emitidos: pendente, compensado, cancelado
        var emitidos = new ChequeEmitidoService(_banco.Fabrica, () => Agora);
        var tecidos = (await fin.CriarAsync(new DadosLancamento(TipoLancamento.Despesa, "Tecidos — Malharia Sol", 180000, Dia(28), "Fornecedores", "Malharia Sol")))[0];
        var linhas = (await fin.CriarAsync(new DadosLancamento(TipoLancamento.Despesa, "Linhas e aviamentos", 32000, Dia(16), "Fornecedores", "Armarinho Central")))[0];
        var etiquetas = (await fin.CriarAsync(new DadosLancamento(TipoLancamento.Despesa, "Etiquetas bordadas", 15000, Dia(25), "Fornecedores", "Bordados Ltda")))[0];
        await emitidos.EmitirAsync(new DadosChequeEmitido(tecidos.Id, banco.Id, Dia(28), "000231", "Itaú", null));
        var c2 = await emitidos.EmitirAsync(new DadosChequeEmitido(linhas.Id, banco.Id, Dia(16), "000229", "Itaú", null));
        await emitidos.CompensarAsync(c2.Id, Dia(20));
        var c3 = await emitidos.EmitirAsync(new DadosChequeEmitido(etiquetas.Id, banco.Id, Dia(25), "000230", "Itaú", null));
        await emitidos.CancelarAsync(c3.Id, "cheque sustado: erro no valor");
        await emitidos.EmitirAsync(new DadosChequeEmitido(etiquetas.Id, banco.Id, Dia(30), "000232", "Itaú", null));
    }

    [Fact]
    public async Task Gera_financeiro()
    {
        var saida = Pasta();
        if (saida is null) return;
        await SemearAsync();

        var fin = new FinanceiroService(_banco.Fabrica, () => Agora);
        var vm = new FinanceiroViewModel(fin, () => Agora, new CategoriaFinanceiraService(_banco.Fabrica, () => Agora));
        await vm.Carregamento;
        vm.PedirMotivo = _ => null;

        // 1) lista com corrigido/cancelado e um lançamento pago selecionado (botões Corrigir/Retirar ligados)
        vm.Selecionada = vm.Linhas.First(l => l.Descricao == "Aluguel da loja");
        Capturador.SalvarFinanceiro(vm, Path.Combine(saida, "financeiro-lista.png"), 1500, 1100);

        // 2) formulário em modo "Corrigir lançamento pago"
        vm.CorrigirSelecionadaCommand.Execute(null);
        Capturador.SalvarFinanceiro(vm, Path.Combine(saida, "financeiro-corrigir.png"), 1500, 1100);

        // 3) formulário com classificação diferente do padrão
        vm.NovoCommand.Execute(null);
        vm.FormDescricao = "Retirada para despesas pessoais";
        vm.FormValor = "800,00";
        var retirada = vm.Classificacoes.FirstOrDefault(c => c.Valor == ClassificacaoLancamento.RetiradaProprietario);
        if (retirada is not null) vm.FormClassificacao = retirada;
        Capturador.SalvarFinanceiro(vm, Path.Combine(saida, "financeiro-classificacao.png"), 1500, 1100);

        // 4) gerenciador de categorias aberto
        Capturador.Desenhar(() => new FinanceiroView { DataContext = vm }, Path.Combine(saida, "financeiro-categorias.png"), 1500, 1250, tela =>
        {
            Expander? Achar(DependencyObject o)
            {
                foreach (var f in LogicalTreeHelper.GetChildren(o).OfType<DependencyObject>())
                {
                    if (f is Expander e) return e;
                    var r = Achar(f);
                    if (r is not null) return r;
                }
                return null;
            }
            var exp = Achar(tela);
            if (exp is not null) exp.IsExpanded = true;
        });
    }

    [Fact]
    public async Task Gera_relatorios_dre()
    {
        var saida = Pasta();
        if (saida is null) return;
        await SemearAsync();

        var vm = new RelatoriosViewModel(new RelatorioService(_banco.Fabrica), Agora.Date,
            new DemonstrativoService(_banco.Fabrica, () => Agora));
        await AteAsync(() => vm.Dre.Count > 0);
        Capturador.Desenhar(() => new RelatoriosView { DataContext = vm }, Path.Combine(saida, "relatorios-dre.png"), 1400, 1100,
            tela => SelecionarAba(tela, 1));
    }

    [Fact]
    public async Task Gera_clientes_titulos()
    {
        var saida = Pasta();
        if (saida is null) return;
        await SemearAsync();

        var servico = new ClienteService(_banco.Fabrica, () => Agora);
        var vm = new ClientesViewModel(servico, new TituloPrazoService(_banco.Fabrica, () => Agora));
        await AteAsync(() => vm.Clientes.Count == 2);
        vm.Selecionado = vm.Clientes.First(c => c.Nome.StartsWith("Maria"));
        await AteAsync(() => vm.Titulos.Count > 0 && vm.Ajustes.Count > 0);
        vm.TituloSelecionado = vm.Titulos.First();
        Capturador.Desenhar(() => new ClientesView { DataContext = vm }, Path.Combine(saida, "clientes-titulos.png"), 1500, 1300);
    }

    [Fact]
    public async Task Gera_contas_e_saldos_cheques_emitidos()
    {
        var saida = Pasta();
        if (saida is null) return;
        await SemearAsync();

        var vm = new ContasESaldosViewModel(new PosicaoFinanceiraService(_banco.Fabrica, () => Agora),
            new ContaFinanceiraService(_banco.Fabrica, () => Agora), new LiquidacaoCartaoService(_banco.Fabrica, () => Agora),
            new ChequeService(_banco.Fabrica, () => Agora), () => Agora, new ChequeEmitidoService(_banco.Fabrica, () => Agora));
        await vm.Saldos.Carregamento;
        await vm.ChequesEmitidos!.Carregamento;
        vm.ChequesEmitidos.MostrarTodos = true;
        await AteAsync(() => vm.ChequesEmitidos.Cheques.Count >= 4);
        Capturador.Desenhar(() => new ContasESaldosView { DataContext = vm }, Path.Combine(saida, "contas-visao-geral.png"), 1500, 1100);

        vm.ChequesEmitidos.Selecionado = vm.ChequesEmitidos.Cheques.First(c => c.Situacao.StartsWith("Pen") || c.Situacao.StartsWith("Pode"));
        vm.AbaAtual = 4;   // a aba é ligada ao ViewModel (SelectedIndex), então é por aqui que se escolhe
        Capturador.Desenhar(() => new ContasESaldosView { DataContext = vm }, Path.Combine(saida, "contas-cheques-emitidos.png"), 1500, 1100);
    }
}
