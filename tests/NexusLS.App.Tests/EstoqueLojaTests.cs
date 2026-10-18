using Microsoft.EntityFrameworkCore;
using NexusLS.App.ViewModels;
using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.App.Tests;

/// <summary>A tela "Estoque da loja": cadastro, grade de tamanhos e ajuste de estoque por tamanho.</summary>
public sealed class EstoqueLojaTests : IDisposable
{
    private readonly BancoDeTeste _banco = new();
    private readonly ProdutoService _produtos;
    private readonly EstoqueService _estoque;
    private readonly EstoqueLojaViewModel _vm;

    public EstoqueLojaTests()
    {
        _produtos = new ProdutoService(_banco.Fabrica);
        _estoque = new EstoqueService(_banco.Fabrica);
        _vm = new EstoqueLojaViewModel(_produtos, _estoque);
    }

    public void Dispose() => _banco.Dispose();

    // ---------- ajudantes ----------

    private async Task CadastrarPelaTelaAsync(string nome, string venda = "50,00", string atacado = "", string minimo = "2", string inicial = "0", string referencia = "", string codigo = "")
    {
        _vm.NovoCommand.Execute(null);
        _vm.Nome = nome;
        _vm.PrecoVenda = venda;
        _vm.PrecoAtacado = atacado;
        _vm.EstoqueMinimo = minimo;
        _vm.EstoqueInicial = inicial;
        _vm.Referencia = referencia;
        _vm.CodigoBarras = codigo;
        await _vm.SalvarCommand.ExecuteAsync(null);
    }

    private int IdDe(string nome)
    {
        using var db = _banco.CriarContexto();
        return db.Produtos.Single(p => p.Nome == nome).Id;
    }

    private int EstoqueDe(string nome, string tamanho)
    {
        using var db = _banco.CriarContexto();
        var id = db.Produtos.Single(p => p.Nome == nome).Id;
        return db.TamanhosProduto.Single(t => t.ProdutoId == id && t.Tamanho == tamanho).Estoque;
    }

    private async Task AdicionarTamanhoAsync(string tamanho)
    {
        _vm.NovoTamanho = tamanho;
        await _vm.AdicionarTamanhoCommand.ExecuteAsync(null);
    }

    private async Task AjustarAsync(string tipoTexto, string quantidade, string motivo = "")
    {
        _vm.TipoSelecionado = _vm.Opcoes.Single(o => o.Nome.StartsWith(tipoTexto));
        _vm.QuantidadeTexto = quantidade;
        _vm.Motivo = motivo;
        await _vm.AplicarAjusteCommand.ExecuteAsync(null);
    }

    // ---------- cadastro ----------

    [Fact]
    public async Task Cadastrar_produto_pela_tela_grava_com_precos_referencia_e_estoque_inicial()
    {
        await CadastrarPelaTelaAsync("Camiseta", venda: "59,90", atacado: "39,90", inicial: "4", referencia: "REF-1", codigo: "789");

        Assert.False(_vm.MensagemEhErro, _vm.Mensagem);
        using var db = _banco.CriarContexto();
        var p = db.Produtos.Single();
        Assert.Equal(("Camiseta", 5990L, 3990L, "REF-1", "789", 4), (p.Nome, p.PrecoVendaCentavos, p.PrecoAtacadoCentavos, p.Referencia, p.CodigoBarras, p.EstoqueAtual));
        Assert.True(_vm.EstaEditando);                         // o produto novo já fica aberto para cadastrar os tamanhos
        Assert.Equal("Camiseta", _vm.NomeEditando);
        Assert.Equal(Tamanhos.SemTamanho, Assert.Single(_vm.Grade).Tamanho);
    }

    [Fact]
    public async Task Dados_invalidos_mostram_o_motivo_e_nao_gravam()
    {
        await CadastrarPelaTelaAsync("Camiseta", venda: "abc");
        Assert.True(_vm.MensagemEhErro);
        Assert.Contains("Preço de venda", _vm.Mensagem);

        await CadastrarPelaTelaAsync("Outra", atacado: "x,y");
        Assert.Contains("atacado", _vm.Mensagem);

        using var db = _banco.CriarContexto();
        Assert.Empty(db.Produtos);
    }

    [Fact]
    public async Task Editar_muda_os_dados_sem_mexer_no_estoque()
    {
        await CadastrarPelaTelaAsync("Camiseta", inicial: "5");

        _vm.Nome = "Camiseta Nova";
        _vm.PrecoVenda = "70,00";
        await _vm.SalvarCommand.ExecuteAsync(null);

        using var db = _banco.CriarContexto();
        var p = db.Produtos.Single();
        Assert.Equal(("Camiseta Nova", 7000L, 5), (p.Nome, p.PrecoVendaCentavos, p.EstoqueAtual));
        Assert.Contains("atualizado", _vm.Mensagem);
    }

    [Fact]
    public async Task Desativar_e_reativar()
    {
        await CadastrarPelaTelaAsync("Camiseta");
        await _vm.AlternarAtivoCommand.ExecuteAsync(null);

        using (var db = _banco.CriarContexto()) Assert.False(db.Produtos.Single().Ativo);
        Assert.True(_vm.EhNovo);                               // a tela volta para "novo produto"
        Assert.Empty(_vm.Produtos);                            // e o inativo some da lista

        _vm.MostrarInativos = true;
        await _vm.BuscarCommand.ExecuteAsync(null);
        _vm.Selecionado = Assert.Single(_vm.Produtos);
        await Task.Delay(100);
        Assert.Equal("Reativar", _vm.TextoBotaoAtivo);
        await _vm.AlternarAtivoCommand.ExecuteAsync(null);

        using (var db = _banco.CriarContexto()) Assert.True(db.Produtos.Single().Ativo);
    }

    // ---------- lista e filtros ----------

    [Fact]
    public async Task Busca_por_nome_referencia_e_codigo()
    {
        await CadastrarPelaTelaAsync("Camiseta", referencia: "REF-123", codigo: "111");
        await CadastrarPelaTelaAsync("Bermuda", referencia: "ZZ", codigo: "222");

        _vm.Busca = "ref-12";
        await _vm.BuscarCommand.ExecuteAsync(null);
        Assert.Equal("Camiseta", Assert.Single(_vm.Produtos).Nome);

        _vm.Busca = "222";
        await _vm.BuscarCommand.ExecuteAsync(null);
        Assert.Equal("Bermuda", Assert.Single(_vm.Produtos).Nome);
    }

    [Fact]
    public async Task Filtro_abaixo_do_minimo_e_o_aviso_de_quantidade()
    {
        await CadastrarPelaTelaAsync("Falta", minimo: "5", inicial: "1");
        await CadastrarPelaTelaAsync("Cheio", minimo: "2", inicial: "10");

        await _vm.BuscarCommand.ExecuteAsync(null);
        Assert.Equal(1, _vm.QuantidadeAbaixoDoMinimo);
        Assert.True(_vm.TemAlerta);

        _vm.SomenteAbaixoDoMinimo = true;
        await _vm.BuscarCommand.ExecuteAsync(null);
        Assert.Equal("Falta", Assert.Single(_vm.Produtos).Nome);
    }

    [Fact]
    public async Task Escolher_um_produto_na_lista_abre_cadastro_grade_e_historico()
    {
        await CadastrarPelaTelaAsync("Camiseta", inicial: "3", referencia: "R1");
        await CadastrarPelaTelaAsync("Bermuda");
        await _vm.BuscarCommand.ExecuteAsync(null);

        _vm.Selecionado = _vm.Produtos.Single(p => p.Nome == "Camiseta");
        for (var i = 0; i < 200 && _vm.NomeEditando != "Camiseta"; i++) await Task.Delay(10);
        await Task.Delay(50);

        Assert.Equal("R1", _vm.Referencia);
        Assert.Equal("Estoque total: 3", _vm.EstoqueTotalTexto);
        Assert.Single(_vm.Movimentacoes);                      // o estoque inicial
        Assert.Equal("Estoque inicial", _vm.Movimentacoes[0].Motivo);
    }

    // ---------- grade de tamanhos ----------

    [Fact]
    public async Task Cadastra_tamanhos_e_o_novo_ja_vem_selecionado()
    {
        await CadastrarPelaTelaAsync("Camiseta");

        await AdicionarTamanhoAsync("p");
        await AdicionarTamanhoAsync("M");

        Assert.Equal(new[] { Tamanhos.SemTamanho, "P", "M" }, _vm.Grade.Select(t => t.Tamanho));
        Assert.Equal("M", _vm.TamanhoSelecionado!.Tamanho);
        Assert.Equal("", _vm.NovoTamanho);
    }

    [Fact]
    public async Task Tamanho_repetido_ou_vazio_avisa()
    {
        await CadastrarPelaTelaAsync("Camiseta");
        await AdicionarTamanhoAsync("M");

        await AdicionarTamanhoAsync("m");
        Assert.True(_vm.MensagemEhErro);
        Assert.Contains("já tem", _vm.Mensagem);

        await AdicionarTamanhoAsync(" ");
        Assert.Contains("Informe o tamanho", _vm.Mensagem);
        Assert.Equal(2, _vm.Grade.Count);
    }

    [Fact]
    public async Task Sem_produto_aberto_nao_cadastra_tamanho()
    {
        _vm.NovoCommand.Execute(null);
        await AdicionarTamanhoAsync("M");
        Assert.True(_vm.MensagemEhErro);
        Assert.Contains("Salve o produto", _vm.Mensagem);
    }

    [Fact]
    public async Task Remover_tamanho_so_zerado_e_nunca_o_Sem_tamanho()
    {
        await CadastrarPelaTelaAsync("Camiseta");
        await AdicionarTamanhoAsync("M");
        await AjustarAsync("Entrada", "3");

        await _vm.RemoverTamanhoCommand.ExecuteAsync(null);
        Assert.True(_vm.MensagemEhErro);
        Assert.Contains("ainda tem 3", _vm.Mensagem);

        await AjustarAsync("Perda", "3", "Zerar");
        await _vm.RemoverTamanhoCommand.ExecuteAsync(null);
        Assert.False(_vm.MensagemEhErro, _vm.Mensagem);
        Assert.DoesNotContain(_vm.Grade, t => t.Tamanho == "M");

        _vm.TamanhoSelecionado = _vm.Grade.Single(t => t.EhSemTamanho);
        Assert.False(_vm.PodeRemoverTamanho);
    }

    // ---------- ajuste por tamanho ----------

    [Fact]
    public async Task Entrada_no_tamanho_soma_nele_no_total_e_aparece_no_historico_com_o_tamanho()
    {
        await CadastrarPelaTelaAsync("Camiseta", inicial: "2");
        await AdicionarTamanhoAsync("M");

        await AjustarAsync("Entrada", "5", "Compra");

        Assert.False(_vm.MensagemEhErro, _vm.Mensagem);
        Assert.Equal(5, EstoqueDe("Camiseta", "M"));
        Assert.Equal(2, EstoqueDe("Camiseta", Tamanhos.SemTamanho));
        Assert.Equal("Estoque total: 7", _vm.EstoqueTotalTexto);
        var mov = _vm.Movimentacoes[0];
        Assert.Equal(("M", 5, "Compra"), (mov.Tamanho, mov.Quantidade, mov.Motivo));
        Assert.Contains("tamanho M", _vm.Mensagem);
        Assert.Equal("M", _vm.TamanhoSelecionado!.Tamanho);    // continua no mesmo tamanho para o próximo ajuste
        Assert.Equal("", _vm.QuantidadeTexto);
    }

    [Fact]
    public async Task Perda_maior_que_o_tamanho_e_bloqueada_mesmo_com_estoque_em_outros_tamanhos()
    {
        await CadastrarPelaTelaAsync("Camiseta", inicial: "10");
        await AdicionarTamanhoAsync("M");
        await AjustarAsync("Entrada", "2");

        await AjustarAsync("Perda", "3", "Rasgou");

        Assert.True(_vm.MensagemEhErro);
        Assert.Contains("tamanho M", _vm.Mensagem);
        Assert.Equal(2, EstoqueDe("Camiseta", "M"));
        Assert.Equal(10, EstoqueDe("Camiseta", Tamanhos.SemTamanho));
    }

    [Fact]
    public async Task Correcao_pede_a_contagem_e_o_sistema_calcula_a_diferenca()
    {
        await CadastrarPelaTelaAsync("Camiseta");
        await AdicionarTamanhoAsync("M");
        await AjustarAsync("Entrada", "5");

        await AjustarAsync("Correção", "3", "Contagem");

        Assert.Equal(3, EstoqueDe("Camiseta", "M"));
        Assert.Equal(-2, _vm.Movimentacoes[0].Quantidade);

        await AjustarAsync("Correção", "3", "Contagem");       // mesma contagem: nada a corrigir
        Assert.True(_vm.MensagemEhErro);
        Assert.Contains("Nada a corrigir", _vm.Mensagem);
    }

    [Fact]
    public async Task Perda_e_correcao_exigem_motivo_e_a_quantidade_precisa_ser_numero()
    {
        await CadastrarPelaTelaAsync("Camiseta", inicial: "5");

        await AjustarAsync("Perda", "1", "");
        Assert.Contains("motivo", _vm.Mensagem, StringComparison.OrdinalIgnoreCase);

        await AjustarAsync("Entrada", "abc");
        Assert.Contains("Quantidade inválida", _vm.Mensagem);

        await AjustarAsync("Entrada", "-3");
        Assert.Contains("Quantidade inválida", _vm.Mensagem);
        Assert.Equal(5, EstoqueDe("Camiseta", Tamanhos.SemTamanho));
    }

    [Fact]
    public async Task O_rotulo_da_quantidade_muda_na_correcao()
    {
        await CadastrarPelaTelaAsync("Camiseta");
        _vm.TipoSelecionado = _vm.Opcoes.Single(o => o.Tipo == TipoMovimentacao.Correcao);
        Assert.Contains("contada", _vm.RotuloQuantidade);
        _vm.TipoSelecionado = _vm.Opcoes.Single(o => o.Tipo == TipoMovimentacao.Entrada);
        Assert.Equal("Quantidade", _vm.RotuloQuantidade);
    }

    [Fact]
    public async Task Sem_produto_ou_sem_tamanho_escolhido_o_ajuste_avisa()
    {
        await AjustarAsync("Entrada", "1");
        Assert.Contains("Escolha um produto", _vm.Mensagem);
    }
}
