using System.Net.Http;
using System.IO;
using NexusLS.App.ViewModels;
using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Core.Sincronizacao.Firebase;
using NexusLS.Core.Sincronizacao.Nuvem;
using NexusLS.Tests.Infra;

namespace NexusLS.App.Tests;

/// <summary>
/// A aba Lojas com a nuvem: compartilhar, receber preços, comparativo e preço por loja. Usa um Firestore SIMULADO
/// (nunca o real) e banco em memória.
/// </summary>
[Collection("Sessao")]
public sealed class NuvemLojasTelaTests : IDisposable
{
    private readonly BancoDeTeste _banco = new();
    private readonly FirestoreNuvemFalso _nuvem = new();
    private readonly string _cacheArquivo = Path.Combine(Path.GetTempPath(), "nexus_nuvem_tela_" + Guid.NewGuid().ToString("N") + ".json");
    private readonly DateTime _agora = new(2026, 11, 10, 15, 0, 0, DateTimeKind.Utc);
    private readonly PrecoLojaService _precos;
    private bool _admin = true;
    private bool _financeiro = true;

    public NuvemLojasTelaTests()
    {
        _precos = new PrecoLojaService(_banco.Fabrica, () => _agora);
        new CacheNuvemLoja(_cacheArquivo).Salvar(new EstadoNuvemLoja { EmpresaId = "empresa1" });
    }

    public void Dispose()
    {
        Carrinho.PrecoDaLojaPadrao = null;
        _banco.Dispose();
        try { File.Delete(_cacheArquivo); } catch (IOException) { }
    }

    private ServicoNuvemLoja Servico(string lojaId = "loja-centro", string nome = "Loja Centro") =>
        new(_banco.Fabrica, () => new ClienteNuvemLoja(new HttpClient(_nuvem), "projeto-teste", "empresa1", new TokenDeTeste()),
            new CacheNuvemLoja(_cacheArquivo), () => (lojaId, nome), _precos, () => _admin, () => _financeiro, "1.0-teste", () => _agora, () => _agora.Date);

    private async Task<LojasViewModel> CriarTelaAsync(Func<bool>? autorizar = null, string lojaId = "loja-centro", string nome = "Loja Centro")
    {
        var vm = new LojasViewModel(new RelatorioFinanceiroService(_banco.Fabrica),
            () => new NexusLS.Core.Seguranca.ConfiguracaoSeguranca { LojaNome = nome, LojaId = lojaId }, () => _agora.Date, () => true);
        await vm.Carregamento;
        vm.UsarNuvem(Servico(lojaId, nome), new ProdutoService(_banco.Fabrica), () => _admin, () => _financeiro,
            _ => autorizar?.Invoke() ?? true, () => "Lucas");
        return vm;
    }

    private async Task<Produto> VenderAsync()
    {
        var produtos = new ProdutoService(_banco.Fabrica);
        var camiseta = await produtos.CadastrarAsync(new DadosProduto("Camiseta Básica", "7891234567895", 5990, 2000, 2, "CAM-100", 3990), 10);
        await new VendaService(_banco.Fabrica, () => _agora.Date.AddHours(10)).FinalizarAsync(
            new PedidoVenda(new[] { new ItemCarrinho(camiseta.Id, 2) }, 0, FormaPagamento.Pix));
        return camiseta;
    }

    [Fact]
    public async Task Compartilhar_envia_so_ao_clicar_e_mostra_o_resultado_em_portugues()
    {
        await VenderAsync();
        var vm = await CriarTelaAsync();
        Assert.Empty(_nuvem.Pedidos);                       // abrir a tela não envia nada
        Assert.Contains("ainda não foi enviada", vm.UltimoEnvioTexto);

        await vm.CompartilharCommand.ExecuteAsync(null);

        Assert.False(vm.MensagemEhErro);
        Assert.Contains("Enviado à nuvem", vm.Mensagem);
        Assert.Contains("Último envio:", vm.UltimoEnvioTexto);
        Assert.False(vm.OcupadoNuvem);
        Assert.True(_nuvem.Docs.ContainsKey("empresas/empresa1/lojas/loja-centro"));
    }

    [Fact]
    public async Task Sem_internet_mostra_erro_claro_e_nada_e_enviado()
    {
        await VenderAsync();
        var vm = await CriarTelaAsync();
        _nuvem.SemInternet = true;

        await vm.CompartilharCommand.ExecuteAsync(null);

        Assert.True(vm.MensagemEhErro);
        Assert.Contains("Sem conexão com a internet", vm.Mensagem);
        Assert.Contains("clicar de novo", vm.Mensagem);
        Assert.Empty(_nuvem.Docs);
        Assert.False(vm.OcupadoNuvem);
    }

    [Fact]
    public async Task Sem_a_permissao_financeiro_o_botao_recusa_e_nao_chama_a_nuvem()
    {
        var vm = await CriarTelaAsync();
        _financeiro = false;
        await vm.CompartilharCommand.ExecuteAsync(null);
        Assert.True(vm.MensagemEhErro);
        Assert.Contains("permissão", vm.Mensagem);
        Assert.Empty(_nuvem.Pedidos);
    }

    [Fact]
    public async Task Codigo_de_loja_repetido_pede_confirmacao_antes_de_enviar()
    {
        await VenderAsync();
        _nuvem.Docs["empresas/empresa1/lojas/loja-centro"] = SerializadorPacoteLoja.ParaCampos(
            MontadorPacoteLoja.Loja("loja-centro", "Loja do Shopping", "1.0", _agora.AddHours(-2)));
        var vm = await CriarTelaAsync();

        await vm.CompartilharCommand.ExecuteAsync(null);
        Assert.True(vm.PrecisaConfirmarEnvio);
        Assert.True(vm.MensagemEhErro);
        Assert.Contains("Loja do Shopping", vm.Mensagem);

        await vm.CompartilharMesmoAssimCommand.ExecuteAsync(null);
        Assert.False(vm.PrecisaConfirmarEnvio);
        Assert.False(vm.MensagemEhErro);
    }

    [Fact]
    public async Task Administrador_compara_as_lojas_e_o_usuario_de_loja_so_ve_a_propria()
    {
        await VenderAsync();
        var vm = await CriarTelaAsync();
        await vm.CompartilharCommand.ExecuteAsync(null);
        var dia = new ResumoDiaNuvem(_agora.Date, 90000, 3, 6, 0, 0, 0, 0, new Dictionary<string, long>(), Array.Empty<ItemDiaNuvem>(), _agora);
        _nuvem.Docs["empresas/empresa1/lojas/loja-praia"] = SerializadorPacoteLoja.ParaCampos(MontadorPacoteLoja.Loja("loja-praia", "Loja Praia", "1.0", _agora));
        _nuvem.Docs["empresas/empresa1/lojas/loja-praia/dias/2026-11-10"] = SerializadorPacoteLoja.ParaCampos(dia);

        await vm.LerDaNuvemCommand.ExecuteAsync(null);
        Assert.True(vm.EhAdministrador);
        Assert.Equal(2, vm.Comparativo.Count);
        var praia = vm.Comparativo.Single(c => c.Codigo == "loja-praia");
        Assert.Contains("900,00", praia.Faturamento);
        Assert.Contains("300,00", praia.TicketMedio);   // 900 / 3 vendas
        Assert.Equal("3", praia.Vendas);
        Assert.False(vm.ComparativoDoCache);

        _admin = false;
        var loja = await CriarTelaAsync();
        await loja.LerDaNuvemCommand.ExecuteAsync(null);
        Assert.False(loja.EhAdministrador);
        Assert.Single(loja.Comparativo);
        Assert.Equal("loja-centro", loja.Comparativo[0].Codigo);
    }

    [Fact]
    public async Task Sem_internet_o_comparativo_mostra_o_ultimo_dado_baixado_com_a_data()
    {
        await VenderAsync();
        var vm = await CriarTelaAsync();
        await vm.CompartilharCommand.ExecuteAsync(null);
        await vm.LerDaNuvemCommand.ExecuteAsync(null);
        var antes = vm.Comparativo[0].Faturamento;

        _nuvem.SemInternet = true;
        await vm.LerDaNuvemCommand.ExecuteAsync(null);

        Assert.True(vm.ComparativoDoCache);
        Assert.Equal(antes, vm.Comparativo[0].Faturamento);
        Assert.Contains("último dado baixado", vm.AvisoComparativo);
        Assert.Contains("10/11/2026", vm.AvisoComparativo);
    }

    [Fact]
    public async Task Administrador_define_o_preco_por_loja_e_a_loja_recebe_e_usa_no_pdv()
    {
        var camiseta = await VenderAsync();
        var admin = await CriarTelaAsync();
        await admin.CompartilharCommand.ExecuteAsync(null);
        await admin.LerDaNuvemCommand.ExecuteAsync(null);

        admin.BuscaProdutoPreco = "Camiseta";
        await admin.BuscarProdutoDoPrecoCommand.ExecuteAsync(null);
        Assert.Contains("Camiseta Básica", admin.ProdutoDoPrecoNome);
        var linha = Assert.Single(admin.PrecosPorLoja);
        linha.Varejo = "49,90";
        linha.Atacado = "35,00";
        await admin.SalvarPrecoDaLojaCommand.ExecuteAsync(linha);
        Assert.False(admin.MensagemPrecoEhErro);
        Assert.True(_nuvem.Docs.ContainsKey($"empresas/empresa1/precos/{camiseta.Guid:D}/lojas/loja-centro"));

        // a loja recebe (sem a permissão de Estoque, mas com autorização de gerente)
        var (texto, erro) = await NuvemApp.ReceberPrecosAsync(Servico(), _ => true, () => false);
        Assert.False(erro);
        Assert.Contains("1 aplicado", texto);

        Carrinho.PrecoDaLojaPadrao = _precos.PrecoDe;
        var vendaTela = await VenderPdvAsync(camiseta);
        Assert.Equal(4990, vendaTela);
        await using var db = _banco.CriarContexto();
        Assert.Equal(5990, db.Produtos.First(p => p.Id == camiseta.Id).PrecoVendaCentavos);   // o cadastro (ERP) não mudou
    }

    private async Task<long> VenderPdvAsync(Produto produto)
    {
        var configuracao = new ConfiguracaoService(_banco.Fabrica);
        await configuracao.SalvarAsync(new DadosLoja("Loja", PerguntarTamanho: false));
        var pasta = Path.Combine(Path.GetTempPath(), "nexusls-nuvem-pdv-" + Guid.NewGuid().ToString("N"));
        var pdv = new PdvViewModel(new ProdutoService(_banco.Fabrica), new VendaService(_banco.Fabrica),
            new RascunhoService(new RascunhoStore(Path.Combine(pasta, "rascunho.json")), _banco.Fabrica), new ClienteService(_banco.Fabrica),
            new CupomService(_banco.Fabrica, configuracao, new ImpressoraDeMentira()), TimeSpan.Zero, configuracao);
        pdv.Busca = produto.Nome;
        pdv.ProcessarTecla(ComandoTecla.Enter);
        for (var i = 0; i < 300 && pdv.Etapa != EtapaPdv.Quantidade; i++) await Task.Delay(10);
        Assert.Equal(EtapaPdv.Quantidade, pdv.Etapa);
        Assert.Contains("49,90", pdv.ProdutoModalTexto);   // o preço mostrado já é o da loja
        pdv.ProcessarTecla(ComandoTecla.Enter);
        Assert.Single(pdv.Itens);
        var total = pdv.Itens[0].Linha.TotalCentavos;
        try { Directory.Delete(pasta, true); } catch (IOException) { }
        return total;
    }

    [Fact]
    public async Task Usuario_de_loja_nao_busca_nem_edita_preco_por_loja()
    {
        await VenderAsync();
        _admin = false;
        var vm = await CriarTelaAsync();
        vm.BuscaProdutoPreco = "Camiseta";
        await vm.BuscarProdutoDoPrecoCommand.ExecuteAsync(null);
        Assert.True(vm.MensagemPrecoEhErro);
        Assert.Empty(vm.PrecosPorLoja);
        Assert.False(vm.EhAdministrador);
    }

    [Fact]
    public async Task Receber_precos_sem_permissao_de_estoque_e_sem_autorizacao_nao_aplica_nada()
    {
        var camiseta = await VenderAsync();
        await Servico().DefinirPrecoAsync(camiseta.Guid, "loja-centro", 4990, null, "Lucas");
        var (texto, erro) = await NuvemApp.ReceberPrecosAsync(Servico(), _ => false, () => false);
        Assert.True(erro);
        Assert.Contains("permissão de Estoque", texto);
        Assert.Empty(await _precos.ListarAsync());
    }

    [Fact]
    public async Task Receber_precos_sem_internet_explica_e_mantem_os_precos_de_hoje()
    {
        var camiseta = await VenderAsync();
        await Servico().DefinirPrecoAsync(camiseta.Guid, "loja-centro", 4990, null, "Lucas");
        await NuvemApp.ReceberPrecosAsync(Servico(), _ => true, () => true);
        _nuvem.SemInternet = true;
        var (texto, erro) = await NuvemApp.ReceberPrecosAsync(Servico(), _ => true, () => true);
        Assert.True(erro);
        Assert.Contains("continuam como estavam", texto);
        Assert.Single(await _precos.ListarAsync());
    }

    [Fact]
    public async Task Configuracoes_recebe_precos_da_nuvem()
    {
        var camiseta = await VenderAsync();
        await Servico().DefinirPrecoAsync(camiseta.Guid, "loja-centro", 4990, null, "Lucas");
        var config = new ConfiguracaoService(_banco.Fabrica);
        var vm = new ConfiguracoesViewModel(config, new CupomService(_banco.Fabrica, config, new ImpressoraDeMentira()));
        Assert.False(vm.TemNuvem);
        vm.UsarNuvem(Servico(), _ => true, () => true);
        Assert.True(vm.TemNuvem);

        await vm.ReceberPrecosNuvemCommand.ExecuteAsync(null);

        Assert.False(vm.MensagemPrecosNuvemEhErro);
        Assert.Contains("Preços recebidos", vm.MensagemPrecosNuvem);
        Assert.Single(await _precos.ListarAsync());
    }

    [Fact]
    public void Codigo_da_empresa_invalido_e_recusado_e_valido_e_salvo()
    {
        var vm = new LojasViewModel(new RelatorioFinanceiroService(_banco.Fabrica), () => new NexusLS.Core.Seguranca.ConfiguracaoSeguranca { LojaId = "loja-centro" }, () => _agora.Date, () => true);
        vm.UsarNuvem(Servico());
        vm.EmpresaNuvem = "../erp";
        vm.SalvarEmpresaNuvemCommand.Execute(null);
        Assert.True(vm.MensagemEhErro);
        vm.EmpresaNuvem = "minha-empresa";
        vm.SalvarEmpresaNuvemCommand.Execute(null);
        Assert.False(vm.MensagemEhErro);
        Assert.Equal("minha-empresa", new CacheNuvemLoja(_cacheArquivo).Carregar().EmpresaId);
    }
}
