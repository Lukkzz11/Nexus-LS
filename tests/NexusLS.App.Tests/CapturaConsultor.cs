using System.IO;
using NexusLS.App.Aparencia;
using NexusLS.App.ViewModels;
using NexusLS.Core.Consultor;
using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.App.Tests;

/// <summary>
/// Desenha a tela Início com os cartões do Consultor Nexus (tema claro e escuro, tela larga, notebook e janela pequena) e o
/// estado vazio. Só roda quando NEXUSLS_PRINTS aponta para uma pasta.
/// </summary>
public sealed class CapturaConsultor : IDisposable
{
    private static readonly (int Largura, int Altura)[] Tamanhos = { (1900, 1000), (1280, 760), (1100, 700) };

    private readonly BancoDeTeste _banco = new();
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "nexusls-capconsultor-" + Guid.NewGuid().ToString("N"));
    private DateTime _agora = new(2026, 11, 19, 14, 30, 0);

    public void Dispose()
    {
        Capturador.Rodar(() => GerenciadorDeTema.Aplicar(false));
        _banco.Dispose();
        try { Directory.Delete(_pasta, true); } catch (IOException) { }
    }

    private async Task VenderAsync(VendaService vendas, int produtoId, int quantidade, int diasAtras, FormaPagamento forma = FormaPagamento.Pix)
    {
        var real = _agora;
        _agora = real.Date.AddDays(-diasAtras).AddHours(11);
        try { await vendas.FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(produtoId, quantidade) }, 0, forma)); }
        finally { _agora = real; }
    }

    private async Task<InicioViewModel> InicioAsync(bool comProblemas)
    {
        Directory.CreateDirectory(_pasta);
        var vendas = new VendaService(_banco.Fabrica, () => _agora);
        if (comProblemas)
        {
            var produtos = new ProdutoService(_banco.Fabrica);
            async Task<int> Produto(string nome, long preco, long custo, int estoque) =>
                (await produtos.CadastrarAsync(new DadosProduto(nome, null, preco, custo, 2), estoque)).Id;
            var casaco = await Produto("Casaco de Lã Inverno", 25990, 12000, 12);
            var jaqueta = await Produto("Jaqueta Jeans", 21990, 9500, 9);
            var camiseta = await Produto("Camiseta Básica Algodão", 5990, 2000, 14);
            var bone = await Produto("Boné Aba Reta", 2990, 800, 10);
            await VenderAsync(vendas, casaco, 1, 80);
            await VenderAsync(vendas, jaqueta, 1, 75);
            for (var dia = 1; dia <= 12; dia++) await VenderAsync(vendas, camiseta, 1, dia);   // sobram 2 de 14: risco
            for (var dia = 1; dia <= 10; dia++) await VenderAsync(vendas, bone, 1, dia, FormaPagamento.CartaoDebito);   // sobra 0: acabou

            using var db = _banco.CriarContexto();
            db.Lancamentos.Add(new Lancamento { Tipo = TipoLancamento.Despesa, Descricao = "Aluguel da loja", Contraparte = "Imobiliária Central",
                ValorCentavos = 250_000, Vencimento = _agora.Date.AddDays(-4), CriadoEm = _agora });
            db.Lancamentos.Add(new Lancamento { Tipo = TipoLancamento.Despesa, Descricao = "Compra de camisetas", Contraparte = "Malharia Sol",
                ValorCentavos = 80_000, Vencimento = _agora.Date.AddDays(3), CriadoEm = _agora });
            db.LancamentosEmpresa.Add(new LancamentoEmpresa { Data = new DateTime(2026, 11, 8), Tipo = TipoMovimentoEmpresa.Saida,
                Categoria = "Fornecedor", Descricao = "Mercadoria", ValorCentavos = 600_000, CriadoEm = _agora, AlteradoEm = _agora });
            db.Clientes.Add(new Cliente { Nome = "Maria Souza", DataNascimento = new DateTime(1990, 11, 21), Ativo = true });
            db.Clientes.Add(new Cliente { Nome = "João Lima", DataNascimento = new DateTime(1985, 11, 24), Ativo = true });
            db.SaveChanges();
        }

        var consultor = new ConsultorNexus(_banco.Fabrica, new DispensasConsultorStore(Path.Combine(_pasta, "consultor.json")), () => _agora);
        var cartoes = new ConsultorViewModel(consultor, _ => { }, _ => true, () => _agora);
        var inicio = new InicioViewModel(new InicioService(_banco.Fabrica, () => _agora), null, _ => { }, () => _agora, consultor: cartoes);
        await inicio.Carregamento;
        await cartoes.Carregamento;
        return inicio;
    }

    private static void Salvar(InicioViewModel vm, string saida, string nome)
    {
        foreach (var escuro in new[] { false, true })
        {
            Capturador.Rodar(() => GerenciadorDeTema.Aplicar(escuro));
            foreach (var (largura, altura) in Tamanhos)
                Capturador.SalvarInicio(vm, Path.Combine(saida, $"{nome}-{(escuro ? "escuro" : "claro")}-{largura}x{altura}.png"), largura, altura);
        }
    }

    [Fact]
    public async Task Gera_o_Inicio_com_os_cartoes_do_consultor()
    {
        var saida = Environment.GetEnvironmentVariable("NEXUSLS_PRINTS");
        if (string.IsNullOrWhiteSpace(saida)) return;
        Directory.CreateDirectory(saida);

        var vm = await InicioAsync(comProblemas: true);
        Assert.NotEmpty(vm.Consultor!.Cartoes);
        Salvar(vm, saida, "consultor");
    }

    [Fact]
    public async Task Gera_o_Inicio_com_o_consultor_vazio()
    {
        var saida = Environment.GetEnvironmentVariable("NEXUSLS_PRINTS");
        if (string.IsNullOrWhiteSpace(saida)) return;
        Directory.CreateDirectory(saida);

        var vm = await InicioAsync(comProblemas: false);
        Assert.True(vm.Consultor!.Vazio);
        Salvar(vm, saida, "consultor-vazio");
    }
}
