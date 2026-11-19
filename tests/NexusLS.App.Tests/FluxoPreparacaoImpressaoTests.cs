using System.IO;
using Microsoft.EntityFrameworkCore;
using NexusLS.BarTender.Models;
using NexusLS.BarTender.Models.Designer;
using NexusLS.BarTender.Services;
using NexusLS.BarTender.ViewModels;
using NexusLS.Core.Data;
using NexusLS.Core.Etiquetas;
using NexusLS.Core.Models;

namespace NexusLS.App.Tests;

/// <summary>
/// Fluxo Catálogo → modal de quantidades → tela de preparação → envio.
/// Usa a Bermuda Cargo (ref. 1402) com 30 etiquetas 42 + 38 etiquetas 44 = 68.
/// </summary>
public class FluxoPreparacaoImpressaoTests : IDisposable
{
    private readonly string _tempDbPath;
    private readonly DialogosFalsos _dialogos = new();
    private readonly ImpressoraDeEtiquetasFalsa _impressora = new();

    public FluxoPreparacaoImpressaoTests()
    {
        _tempDbPath = Path.Combine(Path.GetTempPath(), $"nexus_preparacao_{Guid.NewGuid():N}.db");
        using var db = CriarContexto();
        db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(_tempDbPath)) File.Delete(_tempDbPath);
        }
        catch { }
    }

    private PdvDbContext CriarContexto()
    {
        var options = new DbContextOptionsBuilder<PdvDbContext>()
            .UseSqlite($"Data Source={_tempDbPath}")
            .Options;
        return new PdvDbContext(options);
    }

    private MainViewModel CriarMainViewModel() =>
        new(new BancoDadosService(_tempDbPath), _dialogos, iniciarAutomaticamente: false, _impressora);

    private static Produto BermudaCargo() => new()
    {
        Id = 45,
        Nome = "BERMUDA CARGO",
        Referencia = "1402",
        CodigoInterno = "INT-1402",
        PrecoVendaCentavos = 7000,
        Tamanhos = new List<ProdutoTamanho>
        {
            new() { Tamanho = "Sem tamanho", Ordem = 0, Estoque = 0 },
            new() { Tamanho = "42", Ordem = 1, Estoque = 6 },
            new() { Tamanho = "44", Ordem = 2, Estoque = 8 },
            new() { Tamanho = "46", Ordem = 3, Estoque = 8 },
            new() { Tamanho = "48", Ordem = 4, Estoque = 12 },
            new() { Tamanho = "50", Ordem = 5, Estoque = 2 }
        }
    };

    private static SelecaoImpressaoViewModel SelecaoBermuda(int qtd42 = 30, int qtd44 = 38)
    {
        var selecao = new SelecaoImpressaoViewModel(new ProdutoItemViewModel(BermudaCargo()));
        selecao.DefinirQuantidade("42", qtd42);
        selecao.DefinirQuantidade("44", qtd44);
        return selecao;
    }

    private static ModeloEtiquetaDesign Modelo50x30() => new()
    {
        Nome = "Etiqueta padrão 50 × 30",
        LarguraMm = 50,
        AlturaMm = 30,
        Elementos = new System.Collections.ObjectModel.ObservableCollection<ElementoDesign>
        {
            new()
            {
                Tipo = TipoElemento.Texto,
                X = 4, Y = 4, Largura = 42, Altura = 8,
                TamanhoFonte = 12,
                Variavel = VariavelEtiqueta.NomeProduto
            },
            new()
            {
                Tipo = TipoElemento.CodigoBarras,
                X = 4, Y = 16, Largura = 42, Altura = 10
            }
        }
    };

    // ---------------------------------------------------------- modal / seleção

    [Fact]
    public void Modal_da_bermuda_soma_68_e_mantem_todos_os_tamanhos_visiveis()
    {
        var selecao = SelecaoBermuda();

        Assert.Equal(6, selecao.Linhas.Count);
        Assert.Equal(68, selecao.Total);
        Assert.True(selecao.TemQuantidade);
        Assert.False(selecao.TemErro);
        Assert.True(selecao.ValidarParaImpressao());
        Assert.Equal("BERMUDA CARGO", selecao.Nome);
        Assert.Equal("1402", selecao.ReferenciaExibicao);
        Assert.Contains("42: 30", selecao.ResumoSelecao);
        Assert.Contains("44: 38", selecao.ResumoSelecao);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("abc")]
    [InlineData("1.5")]
    public void Quantidade_invalida_fica_marcada_e_impede_continuar(string texto)
    {
        var selecao = new SelecaoImpressaoViewModel(new ProdutoItemViewModel(BermudaCargo()));
        var linha = selecao.Linhas.First(l => l.Tamanho == "42");

        linha.QuantidadeTexto = texto;

        Assert.True(linha.QuantidadeInvalida);
        Assert.False(string.IsNullOrWhiteSpace(linha.MensagemErro));
        Assert.True(selecao.TemErro);
        Assert.False(selecao.ValidarParaImpressao());
        Assert.False(string.IsNullOrWhiteSpace(selecao.MensagemValidacao));
    }

    [Fact]
    public void Selecao_vazia_pede_pelo_menos_um_tamanho()
    {
        var selecao = new SelecaoImpressaoViewModel(new ProdutoItemViewModel(BermudaCargo()));

        Assert.Equal(0, selecao.Total);
        Assert.False(selecao.ValidarParaImpressao());
        Assert.Contains("pelo menos um", selecao.MensagemValidacao, StringComparison.OrdinalIgnoreCase);
    }

    // ---------------------------------------------------------- navegação

    [Fact]
    public void Ir_para_impressao_leva_a_selecao_para_a_preparacao()
    {
        var vm = CriarMainViewModel();
        vm.AbrirModalProduto = selecao =>
        {
            selecao.DefinirQuantidade("42", 30);
            selecao.DefinirQuantidade("44", 38);
            return AcaoProdutoCatalogo.Impressao;
        };

        vm.AbrirProdutoDoCatalogo(new ProdutoItemViewModel(BermudaCargo()));

        Assert.Equal("Preparacao", vm.TelaAtiva);
        Assert.NotNull(vm.PreparacaoVM.Selecao);
        Assert.Equal(68, vm.PreparacaoVM.TotalEtiquetas);
        Assert.Equal(2, vm.PreparacaoVM.Conferencia.Count);
        Assert.Equal("30 × 42 → 38 × 44", vm.PreparacaoVM.OrdemImpressao);
        Assert.Equal(68, vm.PreparacaoVM.Selecao!.Linhas.Where(l => l.TemQuantidade).Sum(l => l.Quantidade));
        Assert.Equal(2, vm.PreparacaoVM.PreviaItens.Count);
    }

    [Fact]
    public void Voltar_ao_catalogo_e_reabrir_o_modal_nao_perde_as_quantidades()
    {
        var vm = CriarMainViewModel();
        var item = new ProdutoItemViewModel(BermudaCargo());

        vm.AbrirModalProduto = selecao =>
        {
            selecao.DefinirQuantidade("42", 30);
            selecao.DefinirQuantidade("44", 38);
            return AcaoProdutoCatalogo.Impressao;
        };

        vm.AbrirProdutoDoCatalogo(item);
        vm.IrParaCatalogoCommand.Execute(null);
        Assert.Equal("Catalogo", vm.TelaAtiva);

        SelecaoImpressaoViewModel? reaberta = null;
        vm.AbrirModalProduto = selecao =>
        {
            reaberta = selecao;
            return AcaoProdutoCatalogo.Estudio;
        };

        vm.AbrirProdutoDoCatalogo(item);

        Assert.NotNull(reaberta);
        Assert.Equal(68, reaberta!.Total);
        Assert.Equal(30, reaberta.Linhas.First(l => l.Tamanho == "42").Quantidade);
        Assert.Equal("Estudio", vm.TelaAtiva);
        Assert.Equal("BERMUDA CARGO", vm.DesignerVM.ProdutoAtual?.Nome);
        Assert.Equal("42", vm.DesignerVM.TamanhoParaTeste);
    }

    // ---------------------------------------------------------- mídia e páginas

    [Fact]
    public void Preparacao_calcula_paginas_no_rolo_e_folhas_no_papel_a4()
    {
        var vm = new PreparacaoImpressaoViewModel(_dialogos, _impressora);
        vm.Carregar(SelecaoBermuda(), Modelo50x30(), "NEXUS LS");

        // Rolo padrão 50 × 30: uma etiqueta por página.
        Assert.Equal(PerfisMidia.Rolo50x30Gap.Tipo, vm.TipoMidia);
        Assert.Equal(1, vm.EtiquetasPorPagina);
        Assert.Equal(68, vm.PaginasNecessarias);
        Assert.Contains("rolo", vm.PaginasTexto, StringComparison.OrdinalIgnoreCase);

        // Folha A4 3 × 7: 21 por folha -> 4 folhas.
        vm.MidiaSelecionada = PerfisMidia.FolhaA4TresPorSete;

        Assert.Equal(21, vm.EtiquetasPorPagina);
        Assert.Equal(4, vm.PaginasNecessarias);
        Assert.Contains("folha", vm.PaginasTexto, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(21, vm.MapaPagina.Count);
        Assert.Contains("Primeira folha", vm.MapaResumo, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Preparacao_sugere_a_midia_com_as_medidas_do_modelo()
    {
        var modelo = Modelo50x30();
        modelo.LarguraMm = 100;
        modelo.AlturaMm = 50;

        var vm = new PreparacaoImpressaoViewModel(_dialogos, _impressora);
        vm.Carregar(SelecaoBermuda(), modelo, "NEXUS LS");

        Assert.Same(PerfisMidia.Rolo100x50Gap, vm.MidiaSelecionada);
        Assert.Equal(100, vm.LarguraEtiquetaMm);
        Assert.Equal(50, vm.AlturaEtiquetaMm);
    }

    [Fact]
    public void Modelo_fora_dos_perfis_vira_midia_personalizada_com_as_medidas_dele()
    {
        var modelo = Modelo50x30();
        modelo.LarguraMm = 45;
        modelo.AlturaMm = 22;

        var vm = new PreparacaoImpressaoViewModel(_dialogos, _impressora);
        vm.Carregar(SelecaoBermuda(), modelo, "NEXUS LS");

        Assert.Equal(45, vm.LarguraEtiquetaMm);
        Assert.Equal(22, vm.AlturaEtiquetaMm);
        Assert.Contains("Do modelo", vm.NomeMidia, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ajustado", vm.NomeMidiaAtual, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(vm.AvisosMidia);
    }

    [Fact]
    public void Reabrir_o_modal_na_preparacao_preserva_a_midia_escolhida()
    {
        var selecao = SelecaoBermuda();
        var vm = new PreparacaoImpressaoViewModel(_dialogos, _impressora);
        vm.Carregar(selecao, Modelo50x30(), "NEXUS LS");

        vm.MidiaSelecionada = PerfisMidia.FolhaA4TresPorSete;
        var antes = vm.MidiaAtual;

        // "Editar quantidades" reabre o modal e volta com a mesma seleção: não volta a mídia para o padrão.
        vm.Carregar(selecao, Modelo50x30(), "NEXUS LS");

        Assert.Equal(TipoMidiaEtiqueta.Folha, vm.TipoMidia);
        Assert.Equal(antes.LarguraEtiquetaMm, vm.LarguraEtiquetaMm);
        Assert.Equal(antes.Colunas, vm.Colunas);
        Assert.Equal(4, vm.PaginasNecessarias);
    }

    [Fact]
    public void Midia_incompativel_com_a_impressora_bloqueia_o_envio()
    {
        var vm = new PreparacaoImpressaoViewModel(_dialogos, _impressora);
        vm.Carregar(SelecaoBermuda(), Modelo50x30(), "NEXUS LS");
        vm.PerfilImpressoraSelecionado = PerfilImpressora.Perfis.First(p => p.Nome.StartsWith("Impressora de folhas", StringComparison.Ordinal));

        // Folha em perfil de rolo? Aqui a mídia é rolo e o perfil só aceita folha.
        Assert.True(vm.TemAvisoMidia);
        Assert.False(vm.PodeImprimir);
        Assert.False(vm.EnviarParaImpressora());
        Assert.Empty(_impressora.Envios);
        Assert.Single(_dialogos.Erros);
    }

    [Fact]
    public void Papel_mais_largo_que_a_impressora_informa_o_limite()
    {
        var vm = new PreparacaoImpressaoViewModel(_dialogos, _impressora);
        vm.Carregar(SelecaoBermuda(), Modelo50x30(), "NEXUS LS");
        vm.PerfilImpressoraSelecionado = PerfilImpressora.Perfis.First(p => p.Nome.StartsWith("Térmica compacta", StringComparison.Ordinal));
        vm.MidiaSelecionada = PerfisMidia.Rolo100x50Gap;

        Assert.True(vm.TemAvisoMidia);
        Assert.Contains("mais largo", vm.AvisoMidia, StringComparison.OrdinalIgnoreCase);
        Assert.False(vm.PodeImprimir);
    }

    // ---------------------------------------------------------- envio real (com impressora falsa)

    [Fact]
    public void Confirmar_imprime_68_etiquetas_respeitando_cada_tamanho()
    {
        var vm = new PreparacaoImpressaoViewModel(_dialogos, _impressora);
        vm.Carregar(SelecaoBermuda(), Modelo50x30(), "NEXUS LS");
        vm.DefinirImpressoras(new[] { "Impressora Térmica Teste" }, "Impressora Térmica Teste");

        var enviou = vm.EnviarParaImpressora();

        Assert.True(enviou);
        Assert.True(vm.EnvioConcluido);
        Assert.Empty(_dialogos.Erros);
        Assert.Single(_dialogos.Sucessos);

        var envio = Assert.Single(_impressora.Envios);
        Assert.Equal("Impressora Térmica Teste", envio.NomeImpressora);
        Assert.Equal("Rolo 50 × 30 mm (gap 3 mm)", envio.Midia.Nome);
        Assert.Equal(2, envio.Itens.Count);

        var t42 = envio.Itens.First(i => i.Tamanho == "42");
        var t44 = envio.Itens.First(i => i.Tamanho == "44");
        Assert.Equal(30, t42.Quantidade);
        Assert.Equal(38, t44.Quantidade);
        Assert.Equal(68, envio.Itens.Sum(i => i.Quantidade));
        Assert.All(envio.Itens, i => Assert.Equal("BERMUDA CARGO", i.Produto.Nome));
    }

    [Fact]
    public void Modelo_do_produto_nunca_e_alterado_e_a_copia_se_ajusta_a_folha()
    {
        var modelo = Modelo50x30();
        var vm = new PreparacaoImpressaoViewModel(_dialogos, _impressora);
        vm.Carregar(SelecaoBermuda(), modelo, "NEXUS LS");

        vm.MidiaSelecionada = PerfisMidia.FolhaA4TresPorSete;

        Assert.Equal(50, modelo.LarguraMm);
        Assert.Equal(30, modelo.AlturaMm);
        Assert.NotNull(vm.ModeloOriginal);
        Assert.Same(modelo, vm.ModeloOriginal);

        Assert.NotNull(vm.ModeloParaMidia);
        Assert.NotSame(modelo, vm.ModeloParaMidia);
        Assert.Equal(63.5, vm.ModeloParaMidia!.LarguraMm, 1);
        Assert.Equal(38.1, vm.ModeloParaMidia.AlturaMm, 1);
        Assert.True(vm.ModeloFoiAjustado);
        Assert.Contains("centralizado", vm.ModeloAjuste, StringComparison.OrdinalIgnoreCase);

        // A escala é única: a proporção largura/altura do desenho é mantida.
        var fatorX = vm.ModeloParaMidia.LarguraMm / 50;
        var fatorY = vm.ModeloParaMidia.AlturaMm / 30;
        Assert.Equal(fatorX, fatorY, 3);
    }

    [Fact]
    public void Falha_no_driver_e_informada_sem_derrubar_a_tela()
    {
        _impressora.Falha = new InvalidOperationException("driver ocupado");
        var vm = new PreparacaoImpressaoViewModel(_dialogos, _impressora);
        vm.Carregar(SelecaoBermuda(), Modelo50x30(), "NEXUS LS");

        var enviou = vm.EnviarParaImpressora();

        Assert.False(enviou);
        Assert.False(vm.EnvioConcluido);
        Assert.Contains("driver ocupado", vm.MensagemStatus, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(_dialogos.Erros, e => e.Contains("driver ocupado", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Cancelar_no_dialogo_do_windows_nao_marca_como_impresso()
    {
        _impressora.Cancelar = true;
        var vm = new PreparacaoImpressaoViewModel(_dialogos, _impressora);
        vm.Carregar(SelecaoBermuda(), Modelo50x30(), "NEXUS LS");

        Assert.False(vm.EnviarParaImpressora());
        Assert.False(vm.EnvioConcluido);
        Assert.Contains(_dialogos.Informes, i => i.Contains("cancelado", StringComparison.OrdinalIgnoreCase));
    }

    // ---------------------------------------------------------- auxiliares

    private sealed class ImpressoraDeEtiquetasFalsa : IImpressoraEtiquetas
    {
        public sealed record Envio(
            IReadOnlyList<ItemFila> Itens,
            ModeloEtiquetaDesign Modelo,
            PerfilMidia Midia,
            string? NomeImpressora);

        public List<Envio> Envios { get; } = new();
        public bool Cancelar { get; set; }
        public Exception? Falha { get; set; }

        public bool ImprimirPreparacao(
            IEnumerable<ItemFila> itens,
            ModeloEtiquetaDesign modelo,
            string nomeLoja,
            PerfilMidia midia,
            string? nomeImpressora,
            int primeiraPosicao = 1)
        {
            if (Falha is not null) throw Falha;
            if (Cancelar) return false;

            Envios.Add(new Envio(itens.ToList(), modelo, midia, nomeImpressora));
            return true;
        }
    }

    private sealed class DialogosFalsos : IDialogoService
    {
        public List<string> Erros { get; } = new();
        public List<string> Informes { get; } = new();
        public List<string> Sucessos { get; } = new();

        public bool Confirmar(string mensagem, string titulo = "Confirmação", string textoConfirmar = "Confirmar", string textoCancelar = "Cancelar") => true;

        public void Informar(string mensagem, string titulo = "Informação") => Informes.Add(mensagem);

        public void Erro(string mensagem, string titulo = "Erro") => Erros.Add(mensagem);

        public void Sucesso(string mensagem, string titulo = "Sucesso") => Sucessos.Add(mensagem);
    }
}
