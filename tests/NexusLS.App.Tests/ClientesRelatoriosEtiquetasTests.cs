using System.IO;
using System.Text;
using NexusLS.App.ViewModels;
using NexusLS.Core.Etiquetas;
using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.App.Tests;

internal static class Espera
{
    public static async Task AteAsync(Func<bool> condicao, string quando)
    {
        for (var i = 0; i < 300 && !condicao(); i++) await Task.Delay(10);
        Assert.True(condicao(), "Tempo esgotado esperando: " + quando);
    }
}

/// <summary>A tela Clientes: cadastro e histórico de compras.</summary>
public sealed class ClientesTelaTests : IDisposable
{
    private readonly BancoDeTeste _banco = new();
    private readonly ClienteService _clientes;
    private readonly ClientesViewModel _vm;

    public ClientesTelaTests()
    {
        _clientes = new ClienteService(_banco.Fabrica);
        _vm = new ClientesViewModel(_clientes);
    }

    public void Dispose() => _banco.Dispose();

    private async Task CadastrarAsync(string nome, string documento = "", string telefone = "", string observacao = "")
    {
        _vm.NovoCommand.Execute(null);
        _vm.Nome = nome;
        _vm.Documento = documento;
        _vm.Telefone = telefone;
        _vm.Observacao = observacao;
        await _vm.SalvarCommand.ExecuteAsync(null);
    }

    [Fact]
    public async Task Cadastra_um_cliente_e_ele_fica_aberto_para_editar()
    {
        await CadastrarAsync("  Maria Souza ", "111.222.333-44", "(11) 99999-0000", "Prefere avisar por WhatsApp");

        Assert.False(_vm.MensagemEhErro, _vm.Mensagem);
        var c = (await _clientes.BuscarAsync("Maria")).Single();
        Assert.Equal(("Maria Souza", "111.222.333-44", "(11) 99999-0000", "Prefere avisar por WhatsApp"),
            (c.Nome, c.Documento, c.Telefone, c.Observacao));
        Assert.True(_vm.EstaEditando);
        Assert.Equal("Editar cliente", _vm.TituloFormulario);
        Assert.Equal("Ainda não comprou.", _vm.ResumoCompras);
    }

    [Fact]
    public async Task Nome_vazio_avisa_e_nao_grava()
    {
        await CadastrarAsync("   ");

        Assert.True(_vm.MensagemEhErro);
        Assert.Contains("nome", _vm.Mensagem, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await _clientes.BuscarAsync(null, incluirInativos: true));
    }

    [Fact]
    public async Task Editar_muda_os_dados()
    {
        await CadastrarAsync("Maria");

        _vm.Nome = "Maria Souza";
        _vm.Telefone = "1234";
        await _vm.SalvarCommand.ExecuteAsync(null);

        var c = (await _clientes.BuscarAsync("Maria")).Single();
        Assert.Equal(("Maria Souza", "1234"), (c.Nome, c.Telefone));
        Assert.Contains("atualizado", _vm.Mensagem);
    }

    [Fact]
    public async Task Busca_na_lista_por_nome_documento_e_telefone()
    {
        await CadastrarAsync("Maria", "111", "9999");
        await CadastrarAsync("João", "222", "8888");

        _vm.Busca = "222";
        await _vm.BuscarCommand.ExecuteAsync(null);
        Assert.Equal("João", Assert.Single(_vm.Clientes).Nome);

        _vm.Busca = "9999";
        await _vm.BuscarCommand.ExecuteAsync(null);
        Assert.Equal("Maria", Assert.Single(_vm.Clientes).Nome);
    }

    [Fact]
    public async Task Desativar_tira_da_lista_e_reativar_traz_de_volta()
    {
        await CadastrarAsync("Pedro");

        await _vm.AlternarAtivoCommand.ExecuteAsync(null);
        Assert.Empty(_vm.Clientes);
        Assert.False(_vm.EstaEditando);

        _vm.MostrarInativos = true;
        await _vm.BuscarCommand.ExecuteAsync(null);
        _vm.Selecionado = Assert.Single(_vm.Clientes);
        await Espera.AteAsync(() => _vm.EstaEditando, "cliente aberto");
        Assert.Equal("Reativar", _vm.TextoBotaoAtivo);

        await _vm.AlternarAtivoCommand.ExecuteAsync(null);
        Assert.Single(await _clientes.BuscarAsync("Pedro"));
    }

    [Fact]
    public async Task Escolher_o_cliente_mostra_o_historico_com_total_e_a_prazo()
    {
        await CadastrarAsync("Maria");
        var maria = (await _clientes.BuscarAsync("Maria")).Single();
        var produtos = new ProdutoService(_banco.Fabrica);
        var bone = await produtos.CadastrarAsync(new DadosProduto("Boné", null, 2000, 800, 1), 50);
        var vendas = new VendaService(_banco.Fabrica);
        await vendas.FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(bone.Id, 1) }, 0, FormaPagamento.Pix, ClienteId: maria.Id));
        await vendas.FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(bone.Id, 3) }, 0, FormaPagamento.APrazo, ClienteId: maria.Id));
        await CadastrarAsync("Outro");

        await _vm.BuscarCommand.ExecuteAsync(null);
        _vm.Selecionado = _vm.Clientes.Single(c => c.Nome == "Maria");
        await Espera.AteAsync(() => _vm.Compras.Count == 2, "histórico");

        var resumo = _vm.ResumoCompras.Replace(' ', ' ');
        Assert.Contains("2 compra(s)", resumo);
        Assert.Contains("R$ 80,00", resumo);
        Assert.Contains("R$ 60,00", _vm.ResumoAPrazo.Replace(' ', ' '));
        Assert.Contains("não controla", _vm.ResumoAPrazo);
    }

    [Fact]
    public async Task Novo_limpa_o_formulario_e_o_historico()
    {
        await CadastrarAsync("Maria", "111");

        _vm.NovoCommand.Execute(null);

        Assert.Equal(("", "", false, "Novo cliente"), (_vm.Nome, _vm.Documento, _vm.EstaEditando, _vm.TituloFormulario));
        Assert.Empty(_vm.Compras);
    }
}

/// <summary>A tela Relatórios (vendas por período).</summary>
public sealed class RelatoriosTelaTests : IDisposable
{
    private readonly BancoDeTeste _banco = new();
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "nexusls-rel-" + Guid.NewGuid().ToString("N"));
    private readonly VendaService _vendas;
    private readonly RelatorioService _relatorio;
    private int _boneId;

    public RelatoriosTelaTests()
    {
        Directory.CreateDirectory(_pasta);
        _vendas = new VendaService(_banco.Fabrica);
        _relatorio = new RelatorioService(_banco.Fabrica);
    }

    public void Dispose()
    {
        _banco.Dispose();
        if (Directory.Exists(_pasta)) Directory.Delete(_pasta, recursive: true);
    }

    private async Task PrepararAsync() =>
        _boneId = (await new ProdutoService(_banco.Fabrica).CadastrarAsync(new DadosProduto("Boné", null, 2000, 800, 1), 500)).Id;

    private Task VenderAsync(int quantidade, FormaPagamento forma = FormaPagamento.Pix) =>
        _vendas.FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(_boneId, quantidade) }, 0, forma,
            forma == FormaPagamento.Dinheiro ? 1_000_000 : null));

    [Fact]
    public async Task Mostra_os_numeros_do_mes_ate_hoje()
    {
        await PrepararAsync();
        await VenderAsync(2);                                   // 40,00
        await VenderAsync(3, FormaPagamento.Dinheiro);          // 60,00
        var cancelada = await _vendas.FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(_boneId, 9) }, 0, FormaPagamento.Pix));
        await _vendas.CancelarAsync(cancelada.Id, "Teste");

        var vm = new RelatoriosViewModel(_relatorio);
        await Espera.AteAsync(() => vm.Vendas.Count == 3, "relatório");

        Assert.Equal("R$ 100,00", vm.TotalTexto.Replace(' ', ' '));
        Assert.Equal("2", vm.QuantidadeTexto);
        Assert.Equal("R$ 50,00", vm.TicketMedioTexto.Replace(' ', ' '));
        Assert.Equal("1", vm.CanceladasTexto);
        Assert.Equal(new[] { FormaPagamento.Dinheiro, FormaPagamento.Pix }, vm.PorForma.Select(f => f.Forma));
        var dia = Assert.Single(vm.PorDia);
        Assert.Equal((DateTime.Today.ToString("dd/MM"), 2, 100.0), (dia.Dia, dia.Vendas, dia.Percentual)); // a barra do maior dia é 100%
    }

    [Fact]
    public async Task A_lista_mostra_as_mais_recentes_primeiro()
    {
        await PrepararAsync();
        await VenderAsync(1); await VenderAsync(1); await VenderAsync(1);

        var vm = new RelatoriosViewModel(_relatorio);
        await Espera.AteAsync(() => vm.Vendas.Count == 3, "relatório");

        Assert.Equal(new[] { 3, 2, 1 }, vm.Vendas.Select(v => v.Id));
    }

    [Fact]
    public async Task Atalhos_de_periodo_acertam_as_datas_inclusive_o_mes_anterior()
    {
        var vm = new RelatoriosViewModel(_relatorio);
        await Task.Delay(100);

        vm.UltimosSeteDiasCommand.Execute(null);
        Assert.Equal((DateTime.Today.AddDays(-6), DateTime.Today), (vm.De!.Value, vm.Ate!.Value));

        vm.MesAnteriorCommand.Execute(null);
        var primeiro = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddMonths(-1);
        Assert.Equal((primeiro, primeiro.AddMonths(1).AddDays(-1)), (vm.De!.Value, vm.Ate!.Value));

        vm.HojeCommand.Execute(null);
        Assert.Equal((DateTime.Today, DateTime.Today), (vm.De!.Value, vm.Ate!.Value));
    }

    [Fact]
    public async Task Data_final_antes_da_inicial_avisa_e_zera_a_tela()
    {
        await PrepararAsync();
        await VenderAsync(1);
        var vm = new RelatoriosViewModel(_relatorio);
        await Espera.AteAsync(() => vm.Vendas.Count == 1, "relatório");

        vm.De = DateTime.Today;
        vm.Ate = DateTime.Today.AddDays(-2);
        await Espera.AteAsync(() => vm.MensagemEhErro, "aviso");

        Assert.Contains("data final", vm.Mensagem);
        Assert.Empty(vm.Vendas);
        Assert.Equal("", vm.TotalTexto);
    }

    [Fact]
    public async Task Exporta_vendas_e_itens_do_periodo_em_csv()
    {
        await PrepararAsync();
        await VenderAsync(2);
        await VenderAsync(1, FormaPagamento.Dinheiro);
        var vm = new RelatoriosViewModel(_relatorio);
        await Espera.AteAsync(() => vm.Vendas.Count == 2, "relatório");
        var vendasCsv = Path.Combine(_pasta, "vendas.csv");
        var itensCsv = Path.Combine(_pasta, "itens.csv");
        var nomes = new List<string>();

        vm.EscolherArquivoParaSalvar = nome => { nomes.Add(nome); return nomes.Count == 1 ? vendasCsv : itensCsv; };
        await vm.ExportarVendasCommand.ExecuteAsync(null);
        await vm.ExportarItensCommand.ExecuteAsync(null);

        Assert.StartsWith("vendas-", nomes[0]);
        Assert.StartsWith("itens-", nomes[1]);
        Assert.Equal(3, File.ReadAllText(vendasCsv, Encoding.UTF8).Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.Equal(3, File.ReadAllText(itensCsv, Encoding.UTF8).Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Fact]
    public async Task Periodo_sem_vendas_nao_exporta()
    {
        var vm = new RelatoriosViewModel(_relatorio);
        await Task.Delay(100);
        vm.EscolherArquivoParaSalvar = _ => Path.Combine(_pasta, "x.csv");

        await vm.ExportarVendasCommand.ExecuteAsync(null);

        Assert.True(vm.MensagemEhErro);
        Assert.Contains("Não há vendas", vm.Mensagem);
    }
}

/// <summary>A tela Etiquetas: fila, formato, posição inicial e impressão (a impressora é simulada).</summary>
public sealed class EtiquetasTelaTests : IDisposable
{
    private readonly BancoDeTeste _banco = new();
    private readonly ProdutoService _produtos;
    private readonly EtiquetasViewModel _vm;
    private int _camisetaId;

    public EtiquetasTelaTests()
    {
        _produtos = new ProdutoService(_banco.Fabrica);
        _vm = new EtiquetasViewModel(_produtos);
    }

    public void Dispose() => _banco.Dispose();

    private async Task PrepararAsync()
    {
        _camisetaId = (await _produtos.CadastrarAsync(
            new DadosProduto("Camiseta", "7891234567895", 5990, 2000, 1, PrecoAtacadoCentavos: 3990), 0)).Id;
        foreach (var t in new[] { "P", "M", "G" }) await _produtos.AdicionarTamanhoAsync(_camisetaId, t);
        await _produtos.CadastrarAsync(new DadosProduto("Boné", null, 2990, 800, 1), 5);
        await _produtos.CadastrarAsync(new DadosProduto("Cinto Ação", "AÇÃO-1", 8990, 3000, 1), 5); // código com acento
    }

    private async Task EscolherAsync(string busca)
    {
        _vm.Busca = busca;
        await _vm.EscolherPrimeiroCommand.ExecuteAsync(null);
    }

    [Fact]
    public async Task Escolher_o_produto_lista_os_tamanhos_com_o_primeiro_de_verdade_marcado()
    {
        await PrepararAsync();

        await EscolherAsync("Camiseta");

        Assert.Equal(new[] { "P", "M", "G", Tamanhos.SemTamanho }, _vm.OpcoesDeTamanho);
        Assert.Equal("P", _vm.TamanhoEscolhido);
        Assert.True(_vm.TemTamanhosReais);
    }

    [Fact]
    public async Task Produto_sem_grade_so_tem_Sem_tamanho()
    {
        await PrepararAsync();

        await EscolherAsync("Boné");

        Assert.Equal(new[] { Tamanhos.SemTamanho }, _vm.OpcoesDeTamanho);
        Assert.False(_vm.TemTamanhosReais);
    }

    [Fact]
    public async Task Produto_inexistente_avisa()
    {
        await PrepararAsync();
        await EscolherAsync("zzz");
        Assert.True(_vm.MensagemEhErro);
        Assert.Contains("zzz", _vm.Mensagem);
    }

    [Fact]
    public async Task Busca_ao_vivo_mostra_os_resultados_e_escolher_pela_lista_carrega_os_tamanhos()
    {
        await PrepararAsync();

        _vm.Busca = "Cam";
        await Espera.AteAsync(() => _vm.Resultados.Count == 1, "resultados");
        _vm.ResultadoSelecionado = _vm.Resultados[0];
        await Espera.AteAsync(() => _vm.OpcoesDeTamanho.Count == 4, "tamanhos");

        Assert.Equal("Camiseta", _vm.ResultadoSelecionado.Nome);
    }

    [Fact]
    public async Task Adicionar_cria_a_linha_e_a_mesma_etiqueta_soma_as_copias()
    {
        await PrepararAsync();
        await EscolherAsync("Camiseta");

        _vm.QuantidadeTexto = "3";
        _vm.AdicionarCommand.Execute(null);
        _vm.QuantidadeTexto = "2";
        _vm.AdicionarCommand.Execute(null);

        var linha = Assert.Single(_vm.Fila);
        Assert.Equal(("Camiseta", "P", 5), (linha.Nome, linha.Tamanho, linha.Quantidade));
        Assert.Equal("5 etiqueta(s)", _vm.Resumo);
        Assert.Equal("1", _vm.QuantidadeTexto);       // volta ao padrão para a próxima
    }

    [Fact]
    public async Task Todos_os_tamanhos_adiciona_uma_linha_por_tamanho_sem_o_Sem_tamanho()
    {
        await PrepararAsync();
        await EscolherAsync("Camiseta");

        _vm.QuantidadeTexto = "2";
        _vm.AdicionarTodosOsTamanhosCommand.Execute(null);

        Assert.Equal(new[] { "P", "M", "G" }, _vm.Fila.Select(l => l.Tamanho));
        Assert.All(_vm.Fila, l => Assert.Equal(2, l.Quantidade));
        Assert.Equal("6 etiqueta(s)", _vm.Resumo);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("abc")]
    [InlineData("")]
    public async Task Quantidade_invalida_nao_adiciona(string texto)
    {
        await PrepararAsync();
        await EscolherAsync("Boné");

        _vm.QuantidadeTexto = texto;
        _vm.AdicionarCommand.Execute(null);

        Assert.Empty(_vm.Fila);
        Assert.True(_vm.MensagemEhErro);
    }

    [Fact]
    public async Task Adicionar_sem_escolher_produto_avisa()
    {
        await PrepararAsync();
        _vm.AdicionarCommand.Execute(null);
        Assert.True(_vm.MensagemEhErro);
        Assert.Contains("Escolha um produto", _vm.Mensagem);
    }

    [Fact]
    public async Task Editar_as_copias_atualiza_o_total_e_nunca_deixa_abaixo_de_1()
    {
        await PrepararAsync();
        await EscolherAsync("Boné");
        _vm.AdicionarCommand.Execute(null);

        _vm.Fila[0].Quantidade = 10;
        Assert.Equal("10 etiqueta(s)", _vm.Resumo);

        _vm.Fila[0].Quantidade = 0;
        Assert.Equal(1, _vm.Fila[0].Quantidade);
    }

    [Fact]
    public async Task Remover_e_limpar_esvaziam_a_fila()
    {
        await PrepararAsync();
        await EscolherAsync("Camiseta");
        _vm.AdicionarTodosOsTamanhosCommand.Execute(null);

        _vm.RemoverCommand.Execute(_vm.Fila[0]);
        Assert.Equal(2, _vm.Fila.Count);
        _vm.LimparCommand.Execute(null);

        Assert.Empty(_vm.Fila);
        Assert.True(_vm.FilaVazia);
        Assert.Equal("Nenhuma etiqueta na fila.", _vm.Resumo);
    }

    [Fact]
    public async Task Os_itens_para_imprimir_levam_tamanho_preco_e_codigo_e_o_atacado_cai_no_varejo_se_nao_houver()
    {
        await PrepararAsync();
        await EscolherAsync("Camiseta"); _vm.AdicionarCommand.Execute(null);
        await EscolherAsync("Boné"); _vm.AdicionarCommand.Execute(null);

        var varejo = _vm.ItensParaImprimir();
        Assert.Equal(("Camiseta", "P", 5990L, "7891234567895"), (varejo[0].Nome, varejo[0].Tamanho, varejo[0].PrecoCentavos, varejo[0].CodigoBarras));
        Assert.Equal(("Boné", "", 2990L), (varejo[1].Nome, varejo[1].Tamanho, varejo[1].PrecoCentavos)); // "Sem tamanho" vira vazio

        _vm.PrecoAtacado = true;
        var atacado = _vm.ItensParaImprimir();
        Assert.Equal(3990, atacado[0].PrecoCentavos);
        Assert.Equal(2990, atacado[1].PrecoCentavos);          // boné não tem preço de atacado
    }

    [Fact]
    public async Task Folha_a4_mostra_a_posicao_inicial_e_conta_as_folhas()
    {
        await PrepararAsync();
        await EscolherAsync("Boné");
        _vm.QuantidadeTexto = "20";
        _vm.AdicionarCommand.Execute(null);
        Assert.False(_vm.MostrarPosicao);                      // bobina: não tem posição

        _vm.FormatoSelecionado = FormatoEtiqueta.FolhaA4Tres_por_Sete;
        Assert.True(_vm.MostrarPosicao);
        Assert.Equal("20 etiqueta(s) em 1 folha(s)", _vm.Resumo);

        _vm.PrimeiraPosicaoTexto = "5";                        // 4 posições já usadas: 4 + 20 = 24 > 21
        Assert.Equal("20 etiqueta(s) em 2 folha(s)", _vm.Resumo);
        Assert.Equal(5, _vm.PrimeiraPosicaoValida);

        _vm.PrimeiraPosicaoTexto = "99";
        Assert.Equal(21, _vm.PrimeiraPosicaoValida);           // ajustada para caber
    }

    [Fact]
    public async Task Imprimir_entrega_os_itens_o_formato_e_a_posicao_para_a_impressora()
    {
        await PrepararAsync();
        await EscolherAsync("Boné");
        _vm.QuantidadeTexto = "4";
        _vm.AdicionarCommand.Execute(null);
        _vm.FormatoSelecionado = FormatoEtiqueta.FolhaA4Tres_por_Sete;
        _vm.PrimeiraPosicaoTexto = "8";
        IReadOnlyList<ItemEtiqueta>? recebidos = null;
        FormatoEtiqueta? formato = null;
        var posicao = 0;
        _vm.ImprimirEtiquetas = (itens, f, p) => { recebidos = itens; formato = f; posicao = p; return true; };

        await _vm.ImprimirCommand.ExecuteAsync(null);

        Assert.False(_vm.MensagemEhErro, _vm.Mensagem);
        Assert.Equal(FormatoEtiqueta.FolhaA4Tres_por_Sete, formato);
        Assert.Equal(8, posicao);
        Assert.Equal(4, recebidos!.Single().Quantidade);
        Assert.Contains("4 etiqueta(s) enviada(s)", _vm.Mensagem);
    }

    [Fact]
    public async Task Cancelar_a_caixa_de_impressao_nao_mostra_sucesso_nem_erro()
    {
        await PrepararAsync();
        await EscolherAsync("Boné");
        _vm.AdicionarCommand.Execute(null);
        _vm.ImprimirEtiquetas = (_, _, _) => false;

        await _vm.ImprimirCommand.ExecuteAsync(null);

        Assert.False(_vm.MensagemEhErro);
        Assert.DoesNotContain("enviada", _vm.Mensagem);
    }

    [Fact]
    public async Task Fila_vazia_e_posicao_fora_da_folha_nao_imprimem()
    {
        await PrepararAsync();
        var chamadas = 0;
        _vm.ImprimirEtiquetas = (_, _, _) => { chamadas++; return true; };

        await _vm.ImprimirCommand.ExecuteAsync(null);
        Assert.Contains("fila está vazia", _vm.Mensagem);

        await EscolherAsync("Boné");
        _vm.AdicionarCommand.Execute(null);
        _vm.FormatoSelecionado = FormatoEtiqueta.FolhaA4Tres_por_Sete;
        _vm.PrimeiraPosicaoTexto = "22";
        await _vm.ImprimirCommand.ExecuteAsync(null);

        Assert.Contains("entre 1 e 21", _vm.Mensagem);
        Assert.Equal(0, chamadas);
    }

    [Fact]
    public async Task Codigo_de_barras_com_acento_avisa_que_a_etiqueta_sai_sem_codigo()
    {
        await PrepararAsync();
        await EscolherAsync("Cinto");

        _vm.AdicionarCommand.Execute(null);

        Assert.True(_vm.MensagemEhErro);
        Assert.Contains("Code 128", _vm.Mensagem);
        Assert.False(_vm.Fila.Single().TemCodigo);
    }
}
