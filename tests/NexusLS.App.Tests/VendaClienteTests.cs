using System.IO;
using NexusLS.App.ViewModels;
using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.App.Tests;

/// <summary>Venda Cliente: o mesmo PDV com o cliente escolhido (atacado, a prazo, desconto por peça).</summary>
public sealed class VendaClienteTests : IDisposable
{
    private readonly BancoDeTeste _banco = new();
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "nexusls-vc-" + Guid.NewGuid().ToString("N"));
    private readonly ProdutoService _produtos;
    private readonly EstoqueService _estoque;
    private readonly ClienteService _clientes;
    private readonly PdvViewModel _vm;

    public VendaClienteTests()
    {
        _produtos = new ProdutoService(_banco.Fabrica);
        _estoque = new EstoqueService(_banco.Fabrica);
        _clientes = new ClienteService(_banco.Fabrica);
        var configuracao = new ConfiguracaoService(_banco.Fabrica);
        var cupons = new CupomService(_banco.Fabrica, configuracao, new ImpressoraDeMentira());
        var rascunho = new RascunhoService(new RascunhoStore(Path.Combine(_pasta, "rascunho.json")), _banco.Fabrica);
        _vm = new PdvViewModel(_produtos, new VendaService(_banco.Fabrica), rascunho, _clientes, cupons, TimeSpan.Zero);
    }

    public void Dispose()
    {
        _banco.Dispose();
        if (Directory.Exists(_pasta)) Directory.Delete(_pasta, recursive: true);
    }

    private static async Task Ate(Func<bool> condicao, string quando)
    {
        for (var i = 0; i < 300 && !condicao(); i++) await Task.Delay(10);
        Assert.True(condicao(), "Tempo esgotado esperando: " + quando);
    }

    private void Tecla(params ComandoTecla[] teclas)
    {
        foreach (var t in teclas) _vm.ProcessarTecla(t);
    }

    /// <summary>Camiseta (varejo R$ 50, atacado R$ 35) e duas unidades dela no carrinho.</summary>
    private async Task CarrinhoComDuasCamisetasAsync()
    {
        var camiseta = await _produtos.CadastrarAsync(new DadosProduto("Camiseta", "789", 5000, 2000, 1, PrecoAtacadoCentavos: 3500), 10);
        _vm.Busca = "Camiseta";
        _vm.ProcessarTecla(ComandoTecla.Enter);
        await Ate(() => _vm.Etapa is EtapaPdv.Tamanho or EtapaPdv.Quantidade, "escolher a camiseta");
        if (_vm.Etapa == EtapaPdv.Tamanho) Tecla(ComandoTecla.Enter);
        Tecla(ComandoTecla.Cima, ComandoTecla.Enter); // 2 unidades
        Assert.Equal(EtapaPdv.Busca, _vm.Etapa);
        Assert.True(camiseta.Id > 0);
    }

    [Fact]
    public async Task No_modo_cliente_a_venda_sai_no_atacado_a_prazo_com_2_reais_por_peca_e_mostra_a_divida()
    {
        var maria = await _clientes.CadastrarAsync(new DadosCliente("Maria Atacado"));
        await _vm.EntrarModoClienteAsync(maria);
        Assert.True(_vm.EmModoCliente);
        Assert.Equal("Sem dívida", _vm.ClienteFixoDivida);

        await CarrinhoComDuasCamisetasAsync();
        Assert.True(_vm.EhAtacado);
        Assert.Equal("R$ 70,00", _vm.SubtotalTexto);

        Tecla(ComandoTecla.Enter);                       // fecha a venda → desconto
        Assert.Equal(EtapaPdv.Desconto, _vm.Etapa);
        Assert.Equal("4,00", _vm.DescontoTexto);         // 2 peças × R$ 2,00

        Tecla(ComandoTecla.Enter);                       // confirma o desconto → pagamento
        Assert.Equal(EtapaPdv.Pagamento, _vm.Etapa);
        Assert.True(_vm.EmAPrazo);
        Assert.Equal(maria.Id, _vm.ClienteDestacado?.Id);

        Tecla(ComandoTecla.Enter);                       // finaliza
        await Ate(() => _vm.Etapa == EtapaPdv.Sucesso, "venda finalizada");

        using var db = _banco.CriarContexto();
        var venda = db.Vendas.Single();
        Assert.Equal(FormaPagamento.APrazo, venda.FormaPagamento);
        Assert.Equal(maria.Id, venda.ClienteId);
        Assert.Equal(400, venda.DescontoCentavos);
        Assert.Equal(6600, venda.TotalCentavos);
        Assert.Contains("deve R$ 66,00", _vm.SucessoDetalhe);

        // a próxima venda do mesmo cliente continua no atacado e no nome dele
        Tecla(ComandoTecla.Enter);
        Assert.Equal(EtapaPdv.Busca, _vm.Etapa);
        Assert.True(_vm.EmModoCliente);
        Assert.True(_vm.EhAtacado);
    }

    [Fact]
    public async Task O_desconto_por_peca_pode_ser_mudado_e_nunca_passa_do_subtotal()
    {
        var maria = await _clientes.CadastrarAsync(new DadosCliente("Maria"));
        await _vm.EntrarModoClienteAsync(maria);
        await CarrinhoComDuasCamisetasAsync();

        Tecla(ComandoTecla.Enter);
        _vm.DescontoTexto = "10,00";
        Tecla(ComandoTecla.Enter);
        Tecla(ComandoTecla.Enter);
        await Ate(() => _vm.Etapa == EtapaPdv.Sucesso, "venda finalizada");

        using var db = _banco.CriarContexto();
        Assert.Equal(1000, db.Vendas.Single().DescontoCentavos);
    }

    [Fact]
    public async Task Sair_do_modo_cliente_volta_ao_PDV_comum_sem_desconto_sugerido()
    {
        var maria = await _clientes.CadastrarAsync(new DadosCliente("Maria"));
        await _vm.EntrarModoClienteAsync(maria);
        await CarrinhoComDuasCamisetasAsync();

        _vm.SairDoModoClienteCommand.Execute(null);
        Assert.False(_vm.EmModoCliente);

        Tecla(ComandoTecla.Enter);
        Assert.Equal(EtapaPdv.Desconto, _vm.Etapa);
        Assert.Equal("", _vm.DescontoTexto);
        Tecla(ComandoTecla.Enter);
        Assert.False(_vm.EmAPrazo);                      // volta a escolher a forma de pagamento
    }

    [Fact]
    public async Task A_tela_de_escolha_lista_o_cliente_com_a_divida_e_abre_o_pdv_para_ele()
    {
        var maria = await _clientes.CadastrarAsync(new DadosCliente("Maria Atacado", Telefone: "81 99999-0000"));
        await _clientes.CadastrarAsync(new DadosCliente("José Varejo"));

        Cliente? escolhido = null;
        var tela = new VendaClienteViewModel(_clientes, c => { escolhido = c; return Task.CompletedTask; });
        await Ate(() => tela.Clientes.Count == 2, "lista carregada");

        tela.Busca = "maria";
        await Ate(() => tela.Clientes.Count == 1, "busca filtrada");
        Assert.Equal("Sem dívida", tela.Clientes[0].Divida);
        Assert.Contains("99999", tela.Clientes[0].Contato);

        await tela.VenderCommand.ExecuteAsync(null);
        Assert.Equal(maria.Id, escolhido?.Id);
    }

    [Fact]
    public async Task Sem_cliente_escolhido_a_tela_avisa()
    {
        var tela = new VendaClienteViewModel(_clientes, _ => Task.CompletedTask);
        await tela.CarregarAsync();
        await tela.VenderCommand.ExecuteAsync(null);
        Assert.True(tela.MensagemEhErro);
        Assert.Contains("Escolha um cliente", tela.Mensagem);
    }
}
