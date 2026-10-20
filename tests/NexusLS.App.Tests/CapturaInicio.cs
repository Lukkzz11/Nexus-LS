using System.IO;
using NexusLS.App.ViewModels;
using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.App.Tests;

/// <summary>
/// Desenha a tela Início num PNG para conferir o visual. Só roda quando a variável NEXUSLS_PRINTS aponta
/// para uma pasta; nos testes normais não faz nada.
/// </summary>
public sealed class CapturaInicio : IDisposable
{
    private readonly BancoDeTeste _banco = new();

    public void Dispose() => _banco.Dispose();

    [Fact]
    public async Task Gera_a_tela_inicial()
    {
        var saida = Environment.GetEnvironmentVariable("NEXUSLS_PRINTS");
        if (string.IsNullOrWhiteSpace(saida)) return;
        Directory.CreateDirectory(saida);

        var agora = new DateTime(2026, 10, 19, 14, 30, 0);
        var produtos = new ProdutoService(_banco.Fabrica);
        var camiseta = (await produtos.CadastrarAsync(new DadosProduto("Camiseta Básica Algodão", "789", 5990, 2000, 5), 8)).Id;
        var bone = (await produtos.CadastrarAsync(new DadosProduto("Boné Aba Reta", "555", 2990, 800, 3), 8)).Id;
        await produtos.CadastrarAsync(new DadosProduto("Camisa Polo Piquet", null, 8990, 3000, 4), 1);
        await produtos.CadastrarAsync(new DadosProduto("Meia Esportiva", null, 1500, 400, 6), 2);
        var maria = await new ClienteService(_banco.Fabrica).CadastrarAsync(new DadosCliente("Maria Souza"));

        await new CaixaService(_banco.Fabrica, () => agora.AddHours(-6)).AbrirAsync(10000);
        var vendas = new VendaService(_banco.Fabrica, () => agora);
        await vendas.FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(camiseta, 2) }, 0, FormaPagamento.Pix));
        await vendas.FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(bone, 1) }, 0, FormaPagamento.Pix));
        await new VendaService(_banco.Fabrica, () => agora.AddDays(-45)).FinalizarAsync(
            new PedidoVenda(new[] { new ItemCarrinho(bone, 3) }, 0, FormaPagamento.APrazo, ClienteId: maria.Id));

        var vm = new InicioViewModel(new InicioService(_banco.Fabrica, () => agora), null, _ => { }, () => agora);
        await vm.Carregamento;

        Capturador.SalvarInicio(vm, Path.Combine(saida, "inicio.png"));
    }
}

/// <summary>Desenha a tela Catálogo online (com a confirmação do plano aberta) num PNG. Só roda com NEXUSLS_PRINTS.</summary>
public sealed class CapturaCatalogoOnline : IDisposable
{
    private readonly BancoDeTeste _banco = new();
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "nexusls-capco-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        _banco.Dispose();
        try { Directory.Delete(_pasta, true); } catch (IOException) { }
    }

    private sealed class DestinoMudo : NexusLS.Core.Sincronizacao.IDestinoCatalogo
    {
        public Task VerificarAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task EnviarLoteAsync(IReadOnlyList<NexusLS.Core.Sincronizacao.OperacaoCatalogo> lote, CancellationToken ct = default) => Task.CompletedTask;
        public Task RegistrarAtualizacaoAsync(DateTime agora, int total, CancellationToken ct = default) => Task.CompletedTask;
    }

    [Fact]
    public async Task Gera_a_tela_catalogo_online()
    {
        var saida = Environment.GetEnvironmentVariable("NEXUSLS_PRINTS");
        if (string.IsNullOrWhiteSpace(saida)) return;
        Directory.CreateDirectory(saida);
        Directory.CreateDirectory(_pasta);

        var produtos = new ProdutoService(_banco.Fabrica);
        await produtos.CadastrarAsync(new DadosProduto("Camiseta Básica", "789", 5990, 2000, 5), 8);
        await produtos.CadastrarAsync(new DadosProduto("Boné Aba Reta", "555", 2990, 800, 3), 8);
        await produtos.CadastrarAsync(new DadosProduto("Camisa Polo", null, 8990, 3000, 4), 1);
        using (var db = _banco.CriarContexto())
        {
            db.Produtos.Add(new Produto { Nome = "Meia sem preço certo", PrecoVendaCentavos = -5 });
            db.SaveChanges();
        }

        var repo = new NexusLS.Core.Sincronizacao.SincronizacaoRepositorio(_banco.Fabrica);
        var destino = new DestinoMudo();
        var sincronizador = new NexusLS.Core.Sincronizacao.SincronizadorCatalogo(repo, destino);
        await repo.GravarLogAsync(new SincronizacaoLog
        {
            IniciadaEm = new DateTime(2026, 10, 18, 17, 40, 0), ConcluidaEm = new DateTime(2026, 10, 18, 17, 40, 4),
            Resultado = ResultadoSincronizacao.Sucesso, Criados = 12, Mensagem = "12 produto(s) enviado(s): 12 novo(s), 0 alterado(s), 0 desativado(s)."
        });
        await repo.GravarLogAsync(new SincronizacaoLog
        {
            IniciadaEm = new DateTime(2026, 10, 17, 9, 5, 0), Resultado = ResultadoSincronizacao.Falhou, Erros = 1, Mensagem = "Sem conexão com a internet."
        });

        var store = new NexusLS.Core.Sincronizacao.Firebase.ConfiguracaoFirebaseStore(
            Path.Combine(_pasta, "firebase.json"), new NexusLS.App.Seguranca.ProtetorDpapi());
        store.Salvar(new NexusLS.Core.Sincronizacao.Firebase.ConfiguracaoFirebase
            { ProjectId = "minha-loja-1a2b3", ApiKey = "AIzaSyExemplo", Email = "loja@exemplo.com" }, "senha");

        var vm = new CatalogoOnlineViewModel(store, sincronizador, repo, destino, new ConfiguracaoService(_banco.Fabrica));
        await vm.Carregamento;
        await vm.AtualizarFirestoreCommand.ExecuteAsync(null);

        Capturador.SalvarCatalogoOnline(vm, Path.Combine(saida, "catalogo-online.png"));
    }
}

/// <summary>Desenha a tela Financeiro com dados de exemplo num PNG. Só roda com NEXUSLS_PRINTS.</summary>
public sealed class CapturaFinanceiro : IDisposable
{
    private readonly BancoDeTeste _banco = new();

    public void Dispose() => _banco.Dispose();

    [Fact]
    public async Task Gera_a_tela_financeiro()
    {
        var saida = Environment.GetEnvironmentVariable("NEXUSLS_PRINTS");
        if (string.IsNullOrWhiteSpace(saida)) return;
        Directory.CreateDirectory(saida);

        var agora = DateTime.Now;
        var produtos = new ProdutoService(_banco.Fabrica);
        var bone = (await produtos.CadastrarAsync(new DadosProduto("Boné Aba Reta", "555", 2990, 800, 3), 20)).Id;
        await new VendaService(_banco.Fabrica).FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(bone, 5) }, 0, FormaPagamento.Pix));

        var fin = new FinanceiroService(_banco.Fabrica, () => agora);
        var hoje = agora.Date;
        await fin.CriarAsync(new DadosLancamento(TipoLancamento.Despesa, "Aluguel da loja", 150000, new DateTime(hoje.Year, hoje.Month, 5), "Aluguel", "Imobiliária Central", JaPago: true, Forma: FormaPagamento.Pix));
        await fin.CriarAsync(new DadosLancamento(TipoLancamento.Despesa, "Conta de luz", 38000, hoje.AddDays(-2), "Energia", "Neoenergia"));
        await fin.CriarAsync(new DadosLancamento(TipoLancamento.Despesa, "Compra de camisetas", 120000, hoje.AddDays(6), "Fornecedores", "Malharia Sol", Parcelas: 3));
        await fin.CriarAsync(new DadosLancamento(TipoLancamento.Receita, "Conserto de máquina", 25000, hoje.AddDays(3), "Serviços"));

        var vm = new FinanceiroViewModel(fin, () => agora);
        await vm.Carregamento;
        vm.Selecionada = vm.Linhas.First(l => l.Atrasado);

        Capturador.SalvarFinanceiro(vm, Path.Combine(saida, "financeiro.png"));
    }
}

/// <summary>Desenha a tela Compras com uma compra em montagem e histórico. Só roda com NEXUSLS_PRINTS.</summary>
public sealed class CapturaCompras : IDisposable
{
    private readonly BancoDeTeste _banco = new();

    public void Dispose() => _banco.Dispose();

    [Fact]
    public async Task Gera_a_tela_compras()
    {
        var saida = Environment.GetEnvironmentVariable("NEXUSLS_PRINTS");
        if (string.IsNullOrWhiteSpace(saida)) return;
        Directory.CreateDirectory(saida);

        var produtos = new ProdutoService(_banco.Fabrica);
        var fornecedores = new FornecedorService(_banco.Fabrica);
        var compras = new CompraService(_banco.Fabrica);
        var camiseta = (await produtos.CadastrarAsync(new DadosProduto("Camiseta Básica", "789", 5990, 2200, 5), 0)).Id;
        foreach (var t in new[] { "P", "M", "G" }) await produtos.AdicionarTamanhoAsync(camiseta, t);
        var bone = (await produtos.CadastrarAsync(new DadosProduto("Boné Aba Reta", "555", 2990, 800, 3), 0)).Id;
        var sol = await fornecedores.CadastrarAsync(new DadosFornecedor("Malharia Sol"));
        await compras.RegistrarAsync(new DadosCompra(new[] { new ItemCompraDados(camiseta, 10, 2200, "M"), new ItemCompraDados(bone, 6, 800) }, sol.Id, "NF 1234",
            Pagamento: new PagamentoCompra(2, DateTime.Today.AddDays(30))));

        var vm = new ComprasViewModel(compras, produtos, fornecedores);
        await vm.Carregamento;
        vm.Fornecedor = vm.Fornecedores.First();
        vm.BuscaProduto = "camiseta";
        await vm.AdicionarProdutoCommand.ExecuteAsync(null);
        vm.Itens[0].QuantidadeTexto = "20";
        vm.BuscaProduto = "boné";
        await vm.AdicionarProdutoCommand.ExecuteAsync(null);
        vm.Itens[1].QuantidadeTexto = "12";
        vm.Selecionada = vm.Historico.FirstOrDefault();

        Capturador.Desenhar(() => new NexusLS.App.Views.ComprasView { DataContext = vm }, Path.Combine(saida, "compras.png"), 1500, 820);
    }
}

/// <summary>Desenha a tela Funcionários (login ligado, com um funcionário personalizado escolhido). Só roda com NEXUSLS_PRINTS.</summary>
[Collection("Sessao")]
public sealed class CapturaFuncionarios : IDisposable
{
    private readonly BancoDeTeste _banco = new();

    public void Dispose()
    {
        NexusLS.App.Seguranca.Sessao.Sair();
        NexusLS.App.Seguranca.Sessao.DefinirLoginAtivo(false);
        _banco.Dispose();
    }

    [Fact]
    public async Task Gera_a_tela_funcionarios()
    {
        var saida = Environment.GetEnvironmentVariable("NEXUSLS_PRINTS");
        if (string.IsNullOrWhiteSpace(saida)) return;
        Directory.CreateDirectory(saida);

        var servico = new FuncionarioService(_banco.Fabrica);
        var (admin, _) = await servico.CriarPrimeiroAdministradorAsync("Dona Maria", "1234");
        NexusLS.App.Seguranca.Sessao.Entrar(admin);
        await servico.CadastrarAsync(new DadosFuncionario("Ana Paula", PerfilFuncionario.OperadorDeCaixa, Cargo: "Caixa"), "4321");
        await servico.CadastrarAsync(new DadosFuncionario("Rui Costa", PerfilFuncionario.Personalizado, Permissao.Estoque | Permissao.Compras, "Estoquista"), "5555");

        var vm = new FuncionariosViewModel(servico);
        await vm.Carregamento;
        vm.Selecionado = vm.Funcionarios.First(f => f.Nome == "Rui Costa");

        Capturador.Desenhar(() => new NexusLS.App.Views.FuncionariosView { DataContext = vm }, Path.Combine(saida, "funcionarios.png"), 1500, 820);
    }
}

/// <summary>Desenha a janela de login. Só roda com NEXUSLS_PRINTS.</summary>
public sealed class CapturaLogin : IDisposable
{
    private readonly BancoDeTeste _banco = new();

    public void Dispose() => _banco.Dispose();

    [Fact]
    public async Task Gera_a_janela_de_login()
    {
        var saida = Environment.GetEnvironmentVariable("NEXUSLS_PRINTS");
        if (string.IsNullOrWhiteSpace(saida)) return;
        Directory.CreateDirectory(saida);

        var servico = new FuncionarioService(_banco.Fabrica);
        await servico.CriarPrimeiroAdministradorAsync("Dona Maria", "1234");
        await servico.CadastrarAsync(new DadosFuncionario("Ana Paula", PerfilFuncionario.OperadorDeCaixa), "4321");

        Capturador.SalvarJanela(() => new NexusLS.App.Views.LoginWindow(servico, podeCancelar: false), Path.Combine(saida, "login.png"));
    }
}

/// <summary>Desenha o quadro de Pedidos com dados de exemplo. Só roda com NEXUSLS_PRINTS.</summary>
public sealed class CapturaPedidos : IDisposable
{
    private readonly BancoDeTeste _banco = new();

    public void Dispose() => _banco.Dispose();

    [Fact]
    public async Task Gera_a_tela_pedidos()
    {
        var saida = Environment.GetEnvironmentVariable("NEXUSLS_PRINTS");
        if (string.IsNullOrWhiteSpace(saida)) return;
        Directory.CreateDirectory(saida);

        var produtos = new ProdutoService(_banco.Fabrica);
        var pedidos = new PedidoService(_banco.Fabrica);
        var camiseta = (await produtos.CadastrarAsync(new DadosProduto("Camiseta Básica", "789", 5990, 2200, 5), 0)).Id;
        Pedido Novo(string cliente, OrigemPedido o, params ItemPedidoDados[] itens) => pedidos.CriarAsync(new DadosPedido(o, itens, ClienteNome: cliente)).GetAwaiter().GetResult();

        var a = Novo("Maria Souza", OrigemPedido.WhatsApp, new ItemPedidoDados(camiseta, 3));
        var b = Novo("João Lima", OrigemPedido.Catalogo, new ItemPedidoDados(camiseta, 1), new ItemPedidoDados(null, 2, "Boné", 3000));
        var c = Novo("Ana Paula", OrigemPedido.Encomenda, new ItemPedidoDados(camiseta, 10));
        var d = Novo("Rui Costa", OrigemPedido.Balcao, new ItemPedidoDados(camiseta, 2));
        Novo("Dona Zefa", OrigemPedido.WhatsApp, new ItemPedidoDados(camiseta, 1));
        await pedidos.AvancarAsync(b.Id);
        await pedidos.AvancarAsync(c.Id); await pedidos.AvancarAsync(c.Id);
        for (var i = 0; i < 3; i++) await pedidos.AvancarAsync(d.Id);

        var vm = new PedidosViewModel(pedidos, produtos, new ClienteService(_banco.Fabrica));
        await vm.Carregamento;
        vm.Selecionado = vm.Colunas[1].Cartoes.First();
        await Task.Delay(300);

        Capturador.Desenhar(() => new NexusLS.App.Views.PedidosView { DataContext = vm }, Path.Combine(saida, "pedidos.png"), 1500, 820);
    }
}
