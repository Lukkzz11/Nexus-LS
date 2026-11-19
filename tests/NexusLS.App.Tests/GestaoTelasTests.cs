using System.IO;
using System.Text;
using NexusLS.App.Seguranca;
using NexusLS.App.ViewModels;
using NexusLS.Core.Models;
using NexusLS.Core.Seguranca;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.App.Tests;

/// <summary>
/// As telas novas da Gestão (etapa 6): relatório financeiro, relatório de produtos, entradas e saídas com anexo, lojas, e as
/// permissões do menu. Usa banco em memória e pastas de teste; nada toca no banco, na pasta ou na nuvem de verdade.
/// </summary>
[Collection("Sessao")]
public sealed class GestaoTelasTests : IDisposable
{
    private readonly BancoDeTeste _banco = new();
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "nexusls-gestao-" + Guid.NewGuid().ToString("N"));
    private readonly AnexoService _anexos;
    private readonly LancamentoEmpresaService _lancamentos;
    private static readonly Func<DateTime> Hoje = () => DateTime.Today;

    public GestaoTelasTests()
    {
        Limpar();
        Directory.CreateDirectory(_pasta);
        _anexos = new AnexoService(_banco.Fabrica, Path.Combine(_pasta, "anexos"));
        _lancamentos = new LancamentoEmpresaService(_banco.Fabrica, _anexos);
    }

    public void Dispose()
    {
        Limpar();
        _banco.Dispose();
        try { Directory.Delete(_pasta, true); } catch (IOException) { }
    }

    private static void Limpar() { Sessao.Sair(); Sessao.DefinirLoginAtivo(false); }

    private static string N(string? texto) => (texto ?? "").Replace(' ', ' ');

    private string CriarPng(string nome = "nota.png")
    {
        var caminho = Path.Combine(_pasta, nome);
        File.WriteAllBytes(caminho, new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4, 5, 6 });
        return caminho;
    }

    private async Task<(Produto camiseta, Produto bone, Produto polo)> VenderAsync()
    {
        var produtos = new ProdutoService(_banco.Fabrica);
        var estoque = new EstoqueService(_banco.Fabrica);
        var vendas = new VendaService(_banco.Fabrica);
        var camiseta = await produtos.CadastrarAsync(new DadosProduto("Camiseta Básica", "7891234567895", 5990, 2000, 2, "CAM-100", 3990), 0);
        foreach (var t in new[] { "P", "M" })
        {
            await produtos.AdicionarTamanhoAsync(camiseta.Id, t);
            await estoque.AjustarAsync(camiseta.Id, TipoMovimentacao.Entrada, 20, null, t);
        }
        var bone = await produtos.CadastrarAsync(new DadosProduto("Boné Aba Reta", "7890000000555", 2990, 800, 1, "BON-020"), 30);
        var polo = await produtos.CadastrarAsync(new DadosProduto("Camisa Polo", null, 8990, 3000, 1, "POL-300"), 5);
        await vendas.FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(camiseta.Id, 2, "M"), new ItemCarrinho(bone.Id, 1) }, 0, FormaPagamento.Pix));
        await vendas.FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(bone.Id, 4) }, 0, FormaPagamento.Dinheiro, 20000));
        return (camiseta, bone, polo);
    }

    // ===================== Relatório financeiro =====================

    [Fact]
    public async Task Relatorio_financeiro_mostra_totais_formas_e_dias_do_periodo()
    {
        await VenderAsync();
        await _lancamentos.CriarAsync(new DadosLancamentoEmpresa(DateTime.Today, TipoMovimentoEmpresa.Saida, "Aluguel", "Aluguel do mês", 150000, FormaPagamento.Pix));
        await _lancamentos.CriarAsync(new DadosLancamentoEmpresa(DateTime.Today, TipoMovimentoEmpresa.Entrada, "Aporte do dono", "Reforço de caixa", 100000));

        var vm = new RelatorioFinanceiroViewModel(new RelatorioFinanceiroService(_banco.Fabrica), Hoje);
        await vm.Carregamento;

        Assert.False(vm.MensagemEhErro, vm.Mensagem);
        Assert.True(vm.TemMovimento);
        // vendas 269,30 + entrada 1.000,00 = 1.269,30; saídas 1.500,00; saldo -230,70
        Assert.Equal(new[] { "Entradas", "Saídas", "Saldo do período", "Vendas (faturamento)" }, vm.Resumo.Select(c => c.Titulo));
        Assert.Contains("1.269,30", N(vm.Resumo[0].Valor));
        Assert.Contains("1.500,00", N(vm.Resumo[1].Valor));
        Assert.Contains("230,70", N(vm.Resumo[2].Valor));
        Assert.True(vm.Resumo[2].Negativo);
        Assert.Contains("269,30", N(vm.Resumo[3].Valor));
        Assert.Contains(vm.PorForma, f => f.Forma == "Pix");
        Assert.Contains(vm.PorForma, f => f.Forma == "Dinheiro");
        Assert.Equal((int)(DateTime.Today - new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1)).TotalDays + 1, vm.PorDia.Count);
        Assert.Equal("Aluguel", Assert.Single(vm.SaidasPorCategoria).Categoria);
    }

    [Fact]
    public async Task Relatorio_financeiro_troca_o_periodo_pelos_atalhos()
    {
        var vm = new RelatorioFinanceiroViewModel(new RelatorioFinanceiroService(_banco.Fabrica), () => new DateTime(2026, 10, 20));
        await vm.Carregamento;
        Assert.Equal(new DateTime(2026, 10, 1), vm.De);

        vm.MesAnteriorCommand.Execute(null);
        await vm.Carregamento;
        Assert.Equal((new DateTime(2026, 9, 1), new DateTime(2026, 9, 30)), (vm.De, vm.Ate));
        Assert.Equal(30, vm.PorDia.Count);

        vm.UltimosSeteDiasCommand.Execute(null);
        await vm.Carregamento;
        Assert.Equal((new DateTime(2026, 10, 14), new DateTime(2026, 10, 20)), (vm.De, vm.Ate));
        Assert.Equal(7, vm.PorDia.Count);
        Assert.False(vm.TemMovimento);
    }

    [Fact]
    public async Task Relatorio_financeiro_exporta_csv_e_so_com_movimento()
    {
        var vm = new RelatorioFinanceiroViewModel(new RelatorioFinanceiroService(_banco.Fabrica), Hoje);
        await vm.Carregamento;
        var arquivo = Path.Combine(_pasta, "fin.csv");
        vm.EscolherArquivoParaSalvar = _ => arquivo;

        await vm.ExportarCommand.ExecuteAsync(null);
        Assert.True(vm.MensagemEhErro);
        Assert.False(File.Exists(arquivo));

        await _lancamentos.CriarAsync(new DadosLancamentoEmpresa(DateTime.Today, TipoMovimentoEmpresa.Saida, "Energia", "Conta de luz", 25000));
        await vm.AtualizarCommand.ExecuteAsync(null);
        await vm.ExportarCommand.ExecuteAsync(null);

        Assert.False(vm.MensagemEhErro, vm.Mensagem);
        var bytes = File.ReadAllBytes(arquivo);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]); // UTF-8 com BOM, como o Excel pede
        Assert.Contains("Saídas por categoria", Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public async Task Relatorio_financeiro_sem_permissao_nao_mostra_valores()
    {
        await _lancamentos.CriarAsync(new DadosLancamentoEmpresa(DateTime.Today, TipoMovimentoEmpresa.Saida, "Energia", "Conta de luz", 25000));
        var vm = new RelatorioFinanceiroViewModel(new RelatorioFinanceiroService(_banco.Fabrica), Hoje, () => false);
        await vm.Carregamento;

        Assert.True(vm.SemPermissao);
        Assert.True(vm.MensagemEhErro);
        Assert.Empty(vm.Resumo);
        Assert.Empty(vm.PorDia);
        vm.EscolherArquivoParaSalvar = _ => Path.Combine(_pasta, "x.csv");
        await vm.ExportarCommand.ExecuteAsync(null);
        Assert.False(File.Exists(Path.Combine(_pasta, "x.csv")));
    }

    [Fact]
    public async Task Relatorio_financeiro_usa_a_permissao_da_sessao_por_padrao()
    {
        Sessao.DefinirLoginAtivo(true);
        Sessao.Entrar(new Funcionario { Id = 7, Nome = "Vendedor", Perfil = PerfilFuncionario.Vendedor, Ativo = true });
        var vm = new RelatorioFinanceiroViewModel(new RelatorioFinanceiroService(_banco.Fabrica), Hoje);
        await vm.Carregamento;
        Assert.True(vm.SemPermissao);

        Sessao.Entrar(new Funcionario { Id = 8, Nome = "Gerente", Perfil = PerfilFuncionario.Gerente, Ativo = true });
        await vm.AtualizarCommand.ExecuteAsync(null);
        Assert.False(vm.SemPermissao);
        Assert.NotEmpty(vm.Resumo);
    }

    // ===================== Relatório de produtos =====================

    [Fact]
    public async Task Relatorio_de_produtos_mostra_abc_categoria_tamanho_e_parados()
    {
        await VenderAsync();
        var vm = new RelatorioProdutosViewModel(new RelatorioProdutosService(_banco.Fabrica), Hoje);
        await vm.Carregamento;

        Assert.False(vm.MensagemEhErro, vm.Mensagem);
        Assert.True(vm.TemVendas);
        Assert.Equal(2, vm.MaisVendidos.Count);
        Assert.Equal("1", vm.MaisVendidos[0].Posicao);
        Assert.Equal("Boné Aba Reta", vm.MaisVendidos[0].Nome); // 5 peças contra 2
        Assert.Equal("A", vm.MaisVendidos[0].Classe);
        Assert.NotEmpty(vm.PorCategoria);
        Assert.Contains(vm.PorTamanho, t => t.Tamanho == "M" && t.Pecas == "2");
        // a polo nunca vendeu e tem estoque: está parada
        var parado = Assert.Single(vm.Parados);
        Assert.Equal("Camisa Polo", parado.Nome);
        Assert.Equal("nunca vendeu", parado.UltimaVenda);
        Assert.Contains("150,00", N(parado.CustoParado)); // 5 x R$ 30,00
        Assert.Contains("Estoque parado", vm.Resumo.Select(c => c.Titulo));
        Assert.Contains("Curva ABC", vm.AvisoAbc);
    }

    [Fact]
    public async Task Relatorio_de_produtos_dias_parado_invalido_explica_e_valido_recarrega()
    {
        await VenderAsync();
        var vm = new RelatorioProdutosViewModel(new RelatorioProdutosService(_banco.Fabrica), Hoje);
        await vm.Carregamento;

        vm.DiasParadoTexto = "abc";
        await vm.Carregamento;
        Assert.True(vm.MensagemEhErro);
        Assert.Contains("número", vm.Mensagem);

        vm.DiasParadoTexto = "30";
        await vm.Carregamento;
        Assert.False(vm.MensagemEhErro, vm.Mensagem);
        Assert.Contains("30 dia", vm.ResumoParados);
    }

    [Fact]
    public async Task Relatorio_de_produtos_exporta_csv_de_cada_parte()
    {
        await VenderAsync();
        var vm = new RelatorioProdutosViewModel(new RelatorioProdutosService(_banco.Fabrica), Hoje);
        await vm.Carregamento;
        var gravados = new List<string>();
        vm.EscolherArquivoParaSalvar = nome => { var c = Path.Combine(_pasta, nome); gravados.Add(c); return c; };

        await vm.ExportarMaisVendidosCommand.ExecuteAsync(null);
        await vm.ExportarCategoriasCommand.ExecuteAsync(null);
        await vm.ExportarTamanhosCommand.ExecuteAsync(null);
        await vm.ExportarParadosCommand.ExecuteAsync(null);

        Assert.False(vm.MensagemEhErro, vm.Mensagem);
        Assert.Equal(4, gravados.Count);
        Assert.All(gravados, g => Assert.True(File.Exists(g), g));
        Assert.Contains("Classe ABC", File.ReadAllText(gravados[0]));
        Assert.Contains("Camisa Polo", File.ReadAllText(gravados[3]));
    }

    [Fact]
    public async Task Relatorio_de_produtos_sem_permissao_esconde_tudo()
    {
        await VenderAsync();
        var vm = new RelatorioProdutosViewModel(new RelatorioProdutosService(_banco.Fabrica), Hoje, () => false);
        await vm.Carregamento;
        Assert.True(vm.SemPermissao);
        Assert.Empty(vm.MaisVendidos);
        Assert.Empty(vm.Parados);
        Assert.Empty(vm.Resumo);
    }

    // ===================== Entradas e saídas =====================

    private EntradasESaidasViewModel CriarEntradas(Func<bool>? podeVer = null) => new(_lancamentos, _anexos, Hoje, podeVer);

    private static async Task Lancar(EntradasESaidasViewModel vm, bool entrada, string valor, string categoria, string descricao)
    {
        vm.NovoCommand.Execute(null);
        vm.FormEhEntrada = entrada;
        vm.FormValorTexto = valor;
        vm.FormCategoria = categoria;
        vm.FormDescricao = descricao;
        await vm.SalvarCommand.ExecuteAsync(null);
    }

    [Fact]
    public async Task Salvar_cria_o_lancamento_atualiza_totais_e_libera_o_anexo()
    {
        var vm = CriarEntradas();
        await vm.Carregamento;
        Assert.False(vm.PodeAnexar);

        await Lancar(vm, false, "150,00", "Energia", "Conta de luz");

        Assert.False(vm.MensagemEhErro, vm.Mensagem);
        var linha = Assert.Single(vm.Linhas);
        Assert.Equal(("Saída", "Conta de luz", "Energia"), (linha.Tipo, linha.Descricao, linha.Categoria));
        Assert.Contains("150,00", N(linha.Valor));
        Assert.Contains("150,00", N(vm.TotalSaidas));
        Assert.True(vm.SaldoNegativo);
        Assert.True(vm.PodeAnexar);
        Assert.NotNull(vm.EditandoId);
        Assert.Contains("Energia", vm.CategoriasDoFormulario);
        Assert.Equal("1 lançamento", vm.Contagem);
    }

    [Fact]
    public async Task Valor_ou_descricao_invalidos_explicam_o_erro_e_nao_gravam()
    {
        var vm = CriarEntradas();
        await vm.Carregamento;

        await Lancar(vm, false, "abc", "Energia", "Conta de luz");
        Assert.True(vm.MensagemEhErro);
        Assert.Contains("valor", vm.Mensagem, StringComparison.OrdinalIgnoreCase);

        await Lancar(vm, false, "10,00", "Energia", "ab");
        Assert.True(vm.MensagemEhErro);
        Assert.Contains("mínimo 3 letras", vm.Mensagem);
        Assert.Empty(vm.Linhas);
    }

    [Fact]
    public async Task Editar_um_lancamento_pela_lista_altera_o_registro()
    {
        var vm = CriarEntradas();
        await vm.Carregamento;
        await Lancar(vm, false, "150,00", "Energia", "Conta de luz");
        var id = vm.EditandoId!.Value;

        vm.NovoCommand.Execute(null);
        Assert.Null(vm.EditandoId);
        await vm.AbrirLancamentoAsync(id);
        Assert.Equal("Conta de luz", vm.FormDescricao);
        Assert.Equal("150,00", vm.FormValorTexto);

        vm.FormDescricao = "Conta de luz de outubro";
        vm.FormValorTexto = "180,50";
        await vm.SalvarCommand.ExecuteAsync(null);

        Assert.False(vm.MensagemEhErro, vm.Mensagem);
        var linha = Assert.Single(vm.Linhas);
        Assert.Equal("Conta de luz de outubro", linha.Descricao);
        Assert.Contains("180,50", N(linha.Valor));
    }

    [Fact]
    public async Task Filtros_por_tipo_categoria_texto_e_anexo()
    {
        var vm = CriarEntradas();
        await vm.Carregamento;
        await Lancar(vm, false, "150,00", "Energia", "Conta de luz");
        await Lancar(vm, true, "1.000,00", "Aporte do dono", "Reforço de caixa");

        Assert.Equal(2, vm.Linhas.Count);
        Assert.Contains("1.000,00", N(vm.TotalEntradas));

        vm.FiltroTipo = vm.OpcoesTipo[1]; // só entradas
        await vm.Carregamento;
        Assert.Equal("Reforço de caixa", Assert.Single(vm.Linhas).Descricao);

        vm.FiltroTipo = vm.OpcoesTipo[0];
        await vm.Carregamento;
        vm.FiltroTexto = "luz";
        await vm.Carregamento;
        Assert.Equal("Conta de luz", Assert.Single(vm.Linhas).Descricao);

        vm.LimparFiltrosCommand.Execute(null);
        await vm.Carregamento;
        Assert.Equal(2, vm.Linhas.Count);

        vm.FiltroAnexo = vm.OpcoesAnexo[1]; // só com anexo
        await vm.Carregamento;
        Assert.Empty(vm.Linhas);
        Assert.True(vm.Vazio);
    }

    [Fact]
    public async Task Anexar_abrir_e_remover_arquivo()
    {
        var vm = CriarEntradas();
        await vm.Carregamento;
        await Lancar(vm, false, "150,00", "Energia", "Conta de luz");

        vm.EscolherArquivoAnexo = () => CriarPng();
        await vm.AnexarArquivoCommand.ExecuteAsync(null);

        Assert.False(vm.MensagemEhErro, vm.Mensagem);
        var anexo = Assert.Single(vm.AnexosDoLancamento);
        Assert.Equal("nota.png", anexo.Nome);
        Assert.True(anexo.Existe);
        Assert.Equal("1 anexo", Assert.Single(vm.Linhas).Anexos);

        string? aberto = null;
        vm.AbrirArquivo = c => aberto = c;
        vm.AbrirAnexoCommand.Execute(anexo);
        Assert.NotNull(aberto);
        Assert.True(File.Exists(aberto));
        Assert.StartsWith(_anexos.Pasta, aberto, StringComparison.OrdinalIgnoreCase);

        vm.Confirmar = _ => false;
        await vm.RemoverAnexoCommand.ExecuteAsync(anexo);
        Assert.Single(vm.AnexosDoLancamento);

        vm.Confirmar = _ => true;
        await vm.RemoverAnexoCommand.ExecuteAsync(anexo);
        Assert.Empty(vm.AnexosDoLancamento);
        Assert.False(File.Exists(aberto));
        Assert.Equal("", Assert.Single(vm.Linhas).Anexos);
    }

    [Fact]
    public async Task Anexar_recusa_tipo_nao_permitido_e_exige_lancamento_salvo()
    {
        var vm = CriarEntradas();
        await vm.Carregamento;
        vm.EscolherArquivoAnexo = () => CriarPng();
        await vm.AnexarArquivoCommand.ExecuteAsync(null);
        Assert.True(vm.MensagemEhErro);
        Assert.Contains("Salve o lançamento", vm.Mensagem);

        await Lancar(vm, false, "10,00", "Outras saídas", "Material de limpeza");
        var perigoso = Path.Combine(_pasta, "programa.exe");
        File.WriteAllBytes(perigoso, new byte[] { 1, 2, 3 });
        vm.EscolherArquivoAnexo = () => perigoso;
        await vm.AnexarArquivoCommand.ExecuteAsync(null);
        Assert.True(vm.MensagemEhErro);
        Assert.Contains("não permitido", vm.Mensagem);
        Assert.Empty(vm.AnexosDoLancamento);
    }

    [Fact]
    public async Task Cancelar_o_seletor_de_arquivo_nao_faz_nada()
    {
        var vm = CriarEntradas();
        await vm.Carregamento;
        await Lancar(vm, false, "10,00", "Outras saídas", "Material de limpeza");
        vm.EscolherArquivoAnexo = () => null;
        await vm.AnexarArquivoCommand.ExecuteAsync(null);
        Assert.Empty(vm.AnexosDoLancamento);
        Assert.False(vm.MensagemEhErro, vm.Mensagem);
    }

    [Fact]
    public async Task Excluir_com_anexo_pede_confirmacao_e_apaga_os_arquivos()
    {
        var vm = CriarEntradas();
        await vm.Carregamento;
        await Lancar(vm, false, "150,00", "Energia", "Conta de luz");
        vm.EscolherArquivoAnexo = () => CriarPng();
        await vm.AnexarArquivoCommand.ExecuteAsync(null);
        var arquivo = _anexos.CaminhoCompleto(vm.AnexosDoLancamento[0].CaminhoRelativo);
        Assert.True(File.Exists(arquivo));

        string? pergunta = null;
        vm.Confirmar = p => { pergunta = p; return false; };
        await vm.ExcluirCommand.ExecuteAsync(null);
        Assert.Contains("1 arquivo", pergunta);
        Assert.Single(vm.Linhas);
        Assert.True(File.Exists(arquivo));

        vm.Confirmar = _ => true;
        await vm.ExcluirCommand.ExecuteAsync(null);
        Assert.False(vm.MensagemEhErro, vm.Mensagem);
        Assert.Empty(vm.Linhas);
        Assert.False(File.Exists(arquivo));
        Assert.Null(vm.EditandoId);
    }

    [Fact]
    public async Task Excluir_sem_anexo_tambem_pergunta()
    {
        var vm = CriarEntradas();
        await vm.Carregamento;
        await Lancar(vm, true, "50,00", "Outras entradas", "Venda avulsa de sacola");
        vm.Confirmar = _ => false;
        await vm.ExcluirCommand.ExecuteAsync(null);
        Assert.Single(vm.Linhas);
        vm.Confirmar = _ => true;
        await vm.ExcluirCommand.ExecuteAsync(null);
        Assert.Empty(vm.Linhas);
    }

    [Fact]
    public async Task Excluir_pede_autorizacao_de_gerente_pela_alcada()
    {
        var vm = CriarEntradas();
        await vm.Carregamento;
        await Lancar(vm, true, "50,00", "Outras entradas", "Venda avulsa de sacola");
        var motivos = new List<string>();
        var anterior = Alcada.Solicitar;
        try
        {
            Alcada.Solicitar = m => { motivos.Add(m); return false; }; // o gerente recusou
            vm.Confirmar = _ => true;
            await vm.ExcluirCommand.ExecuteAsync(null);
            Assert.Single(motivos);
            Assert.Single(vm.Linhas);
        }
        finally { Alcada.Solicitar = anterior; }
    }

    [Fact]
    public async Task Anexo_sumido_da_pasta_e_avisado_e_nao_abre()
    {
        var vm = CriarEntradas();
        await vm.Carregamento;
        await Lancar(vm, false, "150,00", "Energia", "Conta de luz");
        vm.EscolherArquivoAnexo = () => CriarPng();
        await vm.AnexarArquivoCommand.ExecuteAsync(null);
        File.Delete(_anexos.CaminhoCompleto(vm.AnexosDoLancamento[0].CaminhoRelativo));

        await vm.AbrirLancamentoAsync(vm.EditandoId!.Value);
        var anexo = Assert.Single(vm.AnexosDoLancamento);
        Assert.False(anexo.Existe);
        Assert.NotEqual("", anexo.Aviso);

        string? aberto = null;
        vm.AbrirArquivo = c => aberto = c;
        vm.AbrirAnexoCommand.Execute(anexo);
        Assert.Null(aberto);
        Assert.True(vm.MensagemEhErro);
    }

    [Fact]
    public async Task Entradas_e_saidas_sem_permissao_financeira_nao_mostra_nem_grava()
    {
        await _lancamentos.CriarAsync(new DadosLancamentoEmpresa(DateTime.Today, TipoMovimentoEmpresa.Saida, "Energia", "Conta de luz", 25000));
        var vm = CriarEntradas(() => false);
        await vm.Carregamento;
        Assert.True(vm.SemPermissao);
        Assert.Empty(vm.Linhas);
        Assert.Equal("", vm.TotalSaidas);

        vm.FormValorTexto = "10,00"; vm.FormCategoria = "Outras saídas"; vm.FormDescricao = "Tentativa sem permissão";
        await vm.SalvarCommand.ExecuteAsync(null);
        Assert.True(vm.MensagemEhErro);
        Assert.Single(await _lancamentos.ListarAsync());
    }

    // ===================== Lojas e Gestão da empresa =====================

    private LojasViewModel CriarLojas(Func<bool>? podeVer = null) => new(new RelatorioFinanceiroService(_banco.Fabrica),
        () => new ConfiguracaoSeguranca { LojaNome = "Loja Centro", LojaId = "abc123def4567890abc123def4567890" }, Hoje, podeVer);

    [Fact]
    public async Task Lojas_mostra_esta_loja_com_o_desempenho_do_periodo()
    {
        await VenderAsync();
        var vm = CriarLojas();
        await vm.Carregamento;

        Assert.Equal("Loja Centro", vm.LojaNome);
        var loja = Assert.Single(vm.Lojas);
        Assert.Equal("Loja Centro", loja.Nome);
        Assert.Equal("2", loja.Vendas);
        Assert.Contains("269,30", N(loja.Faturamento));
        Assert.Contains("134,65", N(loja.TicketMedio));
        Assert.Equal("Este computador", loja.Situacao);
        Assert.Equal("Faturamento", vm.Desempenho[0].Titulo);
    }

    [Fact]
    public async Task Adicionar_loja_explica_o_passo_a_passo_e_mostra_o_codigo_desta_loja()
    {
        var vm = CriarLojas();
        await vm.Carregamento;
        Assert.False(vm.MostrandoComoAdicionar);

        vm.AdicionarLojaCommand.Execute(null);

        Assert.True(vm.MostrandoComoAdicionar);
        Assert.Equal(4, vm.Passos.Count);
        Assert.Contains("outro computador", string.Join(" ", vm.Passos.Select(p => p.Titulo + " " + p.Texto)));
        Assert.Contains("nada é enviado", string.Join(" ", vm.Passos.Select(p => p.Texto)), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("abc123def4567890abc123def4567890", vm.LojaId);

        string? copiado = null;
        vm.CopiarTexto = t => copiado = t;
        vm.CopiarCodigoCommand.Execute(null);
        Assert.Equal(vm.LojaId, copiado);

        vm.AdicionarLojaCommand.Execute(null);
        Assert.False(vm.MostrandoComoAdicionar);
    }

    [Fact]
    public async Task Lojas_sem_permissao_financeira_esconde_os_valores()
    {
        await VenderAsync();
        var vm = CriarLojas(() => false);
        await vm.Carregamento;
        Assert.True(vm.SemPermissao);
        Assert.Empty(vm.Desempenho);
        var loja = Assert.Single(vm.Lojas);
        Assert.Equal("—", loja.Faturamento);
        Assert.Equal("Loja Centro", loja.Nome);
    }

    // ===================== Menu e permissões =====================

    private static Funcionario Func(PerfilFuncionario perfil, Permissao? extras = null) => new()
    {
        Id = 1, Nome = "Teste", Perfil = perfil, Ativo = true, PermissoesPersonalizadas = extras ?? Permissao.Nenhuma
    };

    [Fact]
    public void Sem_login_tudo_da_gestao_esta_liberado_inclusive_as_telas_novas()
    {
        foreach (var item in NovosItens) Assert.True(GestaoViewModel.Permitido(item), item.ToString());
    }

    private static readonly ItemMenu[] NovosItens =
    {
        ItemMenu.Compras, ItemMenu.Fornecedores, ItemMenu.CalculadoraPrecos, ItemMenu.RelatorioFinanceiro, ItemMenu.RelatorioProdutos,
        ItemMenu.GestaoEmpresa, ItemMenu.EntradasESaidas
    };

    [Fact]
    public void Administrador_ve_tudo_e_gerente_ve_tudo_menos_a_gestao_da_empresa()
    {
        Sessao.DefinirLoginAtivo(true);
        Sessao.Entrar(Func(PerfilFuncionario.Administrador));
        foreach (var item in NovosItens) Assert.True(GestaoViewModel.Permitido(item), "admin: " + item);

        Sessao.Entrar(Func(PerfilFuncionario.Gerente));
        foreach (var item in NovosItens.Where(i => i != ItemMenu.GestaoEmpresa)) Assert.True(GestaoViewModel.Permitido(item), "gerente: " + item);
        Assert.False(GestaoViewModel.Permitido(ItemMenu.GestaoEmpresa));
    }

    [Fact]
    public void Usuario_comum_nao_ve_nenhuma_tela_nova()
    {
        Sessao.DefinirLoginAtivo(true);
        Sessao.Entrar(Func(PerfilFuncionario.Vendedor));
        foreach (var item in NovosItens) Assert.False(GestaoViewModel.Permitido(item), item.ToString());
    }

    [Fact]
    public void Cada_tela_respeita_a_permissao_dela()
    {
        Sessao.DefinirLoginAtivo(true);
        Sessao.Entrar(Func(PerfilFuncionario.Personalizado, Permissao.Financeiro));
        Assert.True(GestaoViewModel.Permitido(ItemMenu.RelatorioFinanceiro));
        Assert.True(GestaoViewModel.Permitido(ItemMenu.EntradasESaidas));
        Assert.False(GestaoViewModel.Permitido(ItemMenu.RelatorioProdutos));
        Assert.False(GestaoViewModel.Permitido(ItemMenu.Compras));
        Assert.False(GestaoViewModel.Permitido(ItemMenu.GestaoEmpresa));

        Sessao.Entrar(Func(PerfilFuncionario.Personalizado, Permissao.Relatorios));
        Assert.True(GestaoViewModel.Permitido(ItemMenu.RelatorioProdutos));
        Assert.False(GestaoViewModel.Permitido(ItemMenu.RelatorioFinanceiro));

        Sessao.Entrar(Func(PerfilFuncionario.Personalizado, Permissao.Compras));
        Assert.True(GestaoViewModel.Permitido(ItemMenu.Compras));
        Assert.True(GestaoViewModel.Permitido(ItemMenu.Fornecedores));
        Assert.False(GestaoViewModel.Permitido(ItemMenu.CalculadoraPrecos));

        Sessao.Entrar(Func(PerfilFuncionario.Personalizado, Permissao.Estoque));
        Assert.True(GestaoViewModel.Permitido(ItemMenu.CalculadoraPrecos));
    }

    [Fact]
    public void Quem_so_tem_compras_ou_estoque_ainda_entra_na_gestao_para_usar_as_telas_que_mudaram()
    {
        Assert.True(Func(PerfilFuncionario.Personalizado, Permissao.Compras).PodeAlguma(Permissoes.Gestao));
        Assert.True(Func(PerfilFuncionario.Personalizado, Permissao.Estoque).PodeAlguma(Permissoes.Gestao));
        Assert.False(Func(PerfilFuncionario.Vendedor).PodeAlguma(Permissoes.Gestao));
    }

    [Fact]
    public void O_editor_do_catalogo_nao_e_mais_item_da_gestao_nem_do_menu_do_balcao()
    {
        Assert.False(GestaoViewModel.EhDaGestao(ItemMenu.EditorCatalogo));
        Assert.False(GestaoViewModel.Permitido(ItemMenu.EditorCatalogo));
        // os atalhos que antes abriam telas do balcão agora abrem na Gestão
        foreach (var item in new[] { ItemMenu.Compras, ItemMenu.Fornecedores, ItemMenu.CalculadoraPrecos })
            Assert.True(GestaoViewModel.EhDaGestao(item), item.ToString());
    }

    [Fact]
    public void Os_menus_nao_trazem_mais_os_itens_que_mudaram_de_lugar()
    {
        var raiz = RaizDoRepositorio();
        var principal = File.ReadAllText(Path.Combine(raiz, "src", "NexusLS.App", "MainWindow.xaml"));
        Assert.DoesNotContain("COMPRAS E PREÇOS", principal);
        Assert.DoesNotContain("IrParaComprasCommand", principal);
        Assert.DoesNotContain("IrParaFornecedoresCommand", principal);
        Assert.DoesNotContain("IrParaCalculadoraPrecosCommand", principal);

        var gestao = File.ReadAllText(Path.Combine(raiz, "src", "NexusLS.App", "Views", "GestaoWindow.xaml"));
        Assert.DoesNotContain("Editor do catálogo", gestao);
        Assert.DoesNotContain("IrParaEditorCatalogoCommand", gestao);
        foreach (var comando in new[] { "IrParaComprasCommand", "IrParaFornecedoresCommand", "IrParaCalculadoraPrecosCommand", "IrParaRelatorioFinanceiroCommand",
                     "IrParaRelatorioProdutosCommand", "IrParaGestaoEmpresaCommand", "IrParaEntradasESaidasCommand" })
            Assert.Contains(comando, gestao);
    }

    private static string RaizDoRepositorio()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "NexusLS.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Não achei a raiz do repositório.");
    }
}
