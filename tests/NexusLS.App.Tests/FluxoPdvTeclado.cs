using System.IO;
using Microsoft.EntityFrameworkCore;
using NexusLS.App.ViewModels;
using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.App.Tests;

/// <summary>
/// O fluxo do PDV pelo teclado, tecla por tecla, como o operador usa:
/// BUSCA → TAMANHO → QUANTIDADE → BUSCA ... BUSCA vazia → DESCONTO → PAGAMENTO → SUCESSO → nova venda.
/// </summary>
public sealed class FluxoPdvTeclado : IDisposable
{
    private readonly BancoDeTeste _banco = new();
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "nexusls-ui-" + Guid.NewGuid().ToString("N"));
    private readonly ProdutoService _produtos;
    private readonly EstoqueService _estoque;
    private readonly ClienteService _clientes;
    private readonly RascunhoService _rascunho;
    private readonly ConfiguracaoService _configuracao;
    private readonly ImpressoraDeMentira _impressora = new();
    private readonly CupomService _cupons;
    private readonly PdvViewModel _vm;

    private int _camisetaId;
    private int _boneId;

    public FluxoPdvTeclado()
    {
        _produtos = new ProdutoService(_banco.Fabrica);
        _estoque = new EstoqueService(_banco.Fabrica);
        _clientes = new ClienteService(_banco.Fabrica);
        _configuracao = new ConfiguracaoService(_banco.Fabrica);
        _cupons = new CupomService(_banco.Fabrica, _configuracao, _impressora);
        _rascunho = new RascunhoService(new RascunhoStore(Path.Combine(_pasta, "rascunho.json")), _banco.Fabrica);
        _vm = NovoVm();
    }

    private PdvViewModel NovoVm(TimeSpan? tempoMinimoSucesso = null) =>
        new(_produtos, new VendaService(_banco.Fabrica), _rascunho, _clientes, _cupons, tempoMinimoSucesso ?? TimeSpan.Zero);

    public void Dispose()
    {
        _banco.Dispose();
        if (Directory.Exists(_pasta)) Directory.Delete(_pasta, recursive: true);
    }

    /// <summary>Camiseta (varejo R$ 50, atacado R$ 35): P=3, M=5, G=0, Sem tamanho=2. Boné R$ 20, estoque 10.</summary>
    private async Task PrepararAsync()
    {
        _camisetaId = (await _produtos.CadastrarAsync(
            new DadosProduto("Camiseta", "789", 5000, 2000, 1, PrecoAtacadoCentavos: 3500), 2)).Id;
        foreach (var t in new[] { "P", "M", "G" }) await _produtos.AdicionarTamanhoAsync(_camisetaId, t);
        await _estoque.AjustarAsync(_camisetaId, TipoMovimentacao.Entrada, 3, null, "P");
        await _estoque.AjustarAsync(_camisetaId, TipoMovimentacao.Entrada, 5, null, "M");
        _boneId = (await _produtos.CadastrarAsync(new DadosProduto("Boné", "555", 2000, 800, 1), 10)).Id;
    }

    // ---------- ajudantes ----------

    private async Task AteAsync(Func<bool> condicao, string quando)
    {
        for (var i = 0; i < 300 && !condicao(); i++) await Task.Delay(10);
        Assert.True(condicao(), $"Tempo esgotado esperando: {quando}. Etapa={_vm.Etapa}, mensagem=\"{_vm.Mensagem}\"");
    }

    /// <summary>Digita na busca e tecla Enter; espera o fluxo chegar à etapa esperada.</summary>
    private async Task BuscarEEscolherAsync(string texto, EtapaPdv esperada = EtapaPdv.Tamanho)
    {
        _vm.Busca = texto;
        Assert.True(_vm.ProcessarTecla(ComandoTecla.Enter));
        await AteAsync(() => _vm.Etapa == esperada, $"etapa {esperada} depois de buscar \"{texto}\"");
    }

    private void Tecla(params ComandoTecla[] teclas)
    {
        foreach (var t in teclas) _vm.ProcessarTecla(t);
    }

    /// <summary>Adiciona produto/tamanho/quantidade pelo teclado, voltando à busca.</summary>
    private async Task AdicionarAsync(string busca, int cliquesNoTamanho, int maisQuantidade)
    {
        await BuscarEEscolherAsync(busca);
        for (var i = 0; i < cliquesNoTamanho; i++) Tecla(ComandoTecla.Direita);
        Tecla(ComandoTecla.Enter);
        Assert.Equal(EtapaPdv.Quantidade, _vm.Etapa);
        for (var i = 0; i < maisQuantidade; i++) Tecla(ComandoTecla.Cima);
        Tecla(ComandoTecla.Enter);
        Assert.Equal(EtapaPdv.Busca, _vm.Etapa);
    }

    private int EstoqueDe(int produtoId, string tamanho)
    {
        using var db = _banco.CriarContexto();
        return db.TamanhosProduto.Single(t => t.ProdutoId == produtoId && t.Tamanho == tamanho).Estoque;
    }

    private List<Venda> Vendas()
    {
        using var db = _banco.CriarContexto();
        return db.Vendas.Include(v => v.Itens).OrderBy(v => v.Id).ToList();
    }

    // ---------- o fluxo feliz ----------

    [Fact]
    public async Task Venda_completa_so_com_o_teclado()
    {
        await PrepararAsync();

        // BUSCA → TAMANHO: o varejo mostra só P, M, G (sem "Sem tamanho") e já começa no primeiro com estoque.
        await BuscarEEscolherAsync("Camiseta");
        Assert.Equal(new[] { "P", "M", "G" }, _vm.OpcoesTamanho.Select(o => o.Texto));
        Assert.True(_vm.OpcoesTamanho[0].Selecionada);

        // TAMANHO → QUANTIDADE (→ escolhe M)
        Tecla(ComandoTecla.Direita, ComandoTecla.Enter);
        Assert.Equal(EtapaPdv.Quantidade, _vm.Etapa);
        Assert.Equal(1, _vm.QuantidadeValor);

        // QUANTIDADE: ↑ ↑ = 3, Enter volta à busca com o item no carrinho
        Tecla(ComandoTecla.Cima, ComandoTecla.Cima, ComandoTecla.Enter);
        Assert.Equal(EtapaPdv.Busca, _vm.Etapa);
        var linha = Assert.Single(_vm.Itens);
        Assert.Equal(("Camiseta", 3), (linha.Nome, linha.Quantidade));
        Assert.Equal("  ·  tam. M", linha.TamanhoTexto);
        Assert.Equal("", _vm.Busca);

        // BUSCA vazia + carrinho cheio: Enter → DESCONTO; Enter → PAGAMENTO
        Tecla(ComandoTecla.Enter);
        Assert.Equal(EtapaPdv.Desconto, _vm.Etapa);
        Tecla(ComandoTecla.Enter);
        Assert.Equal(EtapaPdv.Pagamento, _vm.Etapa);
        Assert.Equal("R$ 150,00", _vm.TotalFinalTexto.Replace(' ', ' '));
        Assert.Equal("150,00", _vm.ValorRecebidoTexto); // já vem com o total

        // PAGAMENTO: Enter finaliza → SUCESSO
        Tecla(ComandoTecla.Enter);
        await AteAsync(() => _vm.Etapa == EtapaPdv.Sucesso, "tela de sucesso");
        Assert.Contains("#1", _vm.SucessoTitulo);

        var venda = Assert.Single(Vendas());
        Assert.Equal((15000L, FormaPagamento.Dinheiro), (venda.TotalCentavos, venda.FormaPagamento));
        Assert.Equal("M", venda.Itens.Single().Tamanho);
        Assert.Equal(2, EstoqueDe(_camisetaId, "M")); // 5 - 3
        Assert.Empty(_vm.Itens);

        // SUCESSO: Enter → nova venda (já nasce em "Nova venda")
        Tecla(ComandoTecla.Enter);
        Assert.Equal(EtapaPdv.Busca, _vm.Etapa);
    }

    [Fact]
    public async Task Esc_fecha_o_modal_e_volta_a_busca_sem_esvaziar_o_carrinho()
    {
        await PrepararAsync();
        await AdicionarAsync("Boné", 0, 0);

        Tecla(ComandoTecla.Enter);                     // desconto
        Assert.Equal(EtapaPdv.Desconto, _vm.Etapa);
        Tecla(ComandoTecla.Esc);
        Assert.Equal(EtapaPdv.Busca, _vm.Etapa);
        Assert.Single(_vm.Itens);

        await BuscarEEscolherAsync("Camiseta");        // tamanho
        Tecla(ComandoTecla.Esc);
        Assert.Equal(EtapaPdv.Busca, _vm.Etapa);

        await BuscarEEscolherAsync("Camiseta");
        Tecla(ComandoTecla.Enter);                     // quantidade
        Tecla(ComandoTecla.Esc);
        Assert.Equal(EtapaPdv.Busca, _vm.Etapa);
        Assert.Single(_vm.Itens);                      // nada foi adicionado nem perdido
    }

    [Fact]
    public async Task Enter_com_busca_vazia_e_carrinho_vazio_nao_abre_nada()
    {
        await PrepararAsync();

        Tecla(ComandoTecla.Enter);

        Assert.Equal(EtapaPdv.Busca, _vm.Etapa);
        Assert.True(_vm.MensagemEhErro);
    }

    [Fact]
    public async Task Produto_inexistente_avisa_e_fica_na_busca()
    {
        await PrepararAsync();
        _vm.Busca = "zzzz";
        Tecla(ComandoTecla.Enter);
        await AteAsync(() => _vm.MensagemEhErro, "aviso de produto não encontrado");
        Assert.Equal(EtapaPdv.Busca, _vm.Etapa);
        Assert.Contains("zzzz", _vm.Mensagem);
    }

    // ---------- busca ----------

    [Fact]
    public async Task Codigo_de_barras_exato_do_leitor_abre_o_tamanho_do_produto_certo()
    {
        await PrepararAsync();
        await BuscarEEscolherAsync("555");
        Assert.Contains("Boné", _vm.ProdutoModalTexto);
    }

    [Fact]
    public async Task Sem_descer_com_as_setas_o_Enter_usa_o_primeiro_e_descendo_usa_o_destacado()
    {
        await PrepararAsync();
        await _produtos.CadastrarAsync(new DadosProduto("Camisa Polo", null, 8000, 3000, 1), 4);

        // sem descer: o primeiro resultado em ordem alfabética ("Camisa Polo" vem antes de "Camiseta")
        _vm.Busca = "Cami";
        await AteAsync(() => _vm.Resultados.Count == 2, "resultados ao vivo");
        Tecla(ComandoTecla.Enter);
        await AteAsync(() => _vm.Etapa == EtapaPdv.Tamanho, "tamanho");
        Assert.Contains("Camisa Polo", _vm.ProdutoModalTexto);
        Tecla(ComandoTecla.Esc);

        // descendo uma vez: o segundo
        _vm.Busca = "";
        _vm.Busca = "Cami";
        await AteAsync(() => _vm.Resultados.Count == 2, "resultados ao vivo");
        Tecla(ComandoTecla.Baixo, ComandoTecla.Enter);
        await AteAsync(() => _vm.Etapa == EtapaPdv.Tamanho, "tamanho");
        Assert.Contains("Camiseta", _vm.ProdutoModalTexto);
    }

    [Fact]
    public async Task Setas_na_busca_sem_resultados_nao_sao_consumidas()
    {
        await PrepararAsync();
        Assert.False(_vm.ProcessarTecla(ComandoTecla.Baixo));
        Assert.False(_vm.ProcessarTecla(ComandoTecla.Esquerda)); // fica para o cursor do campo
    }

    // ---------- tamanho e quantidade ----------

    [Fact]
    public async Task Tamanho_esgotado_nao_confirma()
    {
        await PrepararAsync();
        await BuscarEEscolherAsync("Camiseta");

        Tecla(ComandoTecla.Direita, ComandoTecla.Direita);   // G, que está com 0
        Tecla(ComandoTecla.Enter);

        Assert.Equal(EtapaPdv.Tamanho, _vm.Etapa);
        Assert.Contains("esgotado", _vm.Mensagem);
    }

    [Fact]
    public async Task Quantidade_nao_passa_do_estoque_nem_fica_abaixo_de_1()
    {
        await PrepararAsync();
        await BuscarEEscolherAsync("Camiseta");              // começa no P (3 unidades)
        Tecla(ComandoTecla.Enter);

        Tecla(ComandoTecla.Baixo);
        Assert.Equal(1, _vm.QuantidadeValor);
        Tecla(ComandoTecla.Cima, ComandoTecla.Cima, ComandoTecla.Cima, ComandoTecla.Cima);
        Assert.Equal(3, _vm.QuantidadeValor);
        Assert.True(_vm.MensagemEhErro);
    }

    [Fact]
    public async Task O_tamanho_mostra_o_estoque_livre_descontando_o_que_ja_esta_no_carrinho()
    {
        await PrepararAsync();
        await AdicionarAsync("Camiseta", 0, 1);              // 2 do P (restam 1)

        await BuscarEEscolherAsync("Camiseta");

        Assert.Equal("1 em estoque", _vm.OpcoesTamanho[0].Detalhe);
    }

    // ---------- tabela de preço ----------

    [Fact]
    public async Task F8_alterna_o_atacado_que_oferece_Sem_tamanho_e_usa_o_preco_de_atacado()
    {
        await PrepararAsync();
        Assert.Equal("VAREJO", _vm.TabelaTexto);

        _vm.AlternarTabelaCommand.Execute(null);
        Assert.Equal("ATACADO", _vm.TabelaTexto);

        await BuscarEEscolherAsync("Camiseta");
        Assert.Equal(new[] { "P", "M", "G", Tamanhos.SemTamanho }, _vm.OpcoesTamanho.Select(o => o.Texto));
        Assert.Contains("35,00", _vm.ProdutoModalTexto);

        Tecla(ComandoTecla.Esc);
        _vm.AlternarTabelaCommand.Execute(null);
        Assert.Equal("VAREJO", _vm.TabelaTexto);
    }

    [Fact]
    public async Task F8_com_modal_aberto_e_ignorado()
    {
        await PrepararAsync();
        await BuscarEEscolherAsync("Camiseta");

        _vm.AlternarTabelaCommand.Execute(null);

        Assert.Equal("VAREJO", _vm.TabelaTexto);
    }

    // ---------- desconto ----------

    [Fact]
    public async Task Desconto_em_percentual_aparece_no_total_do_pagamento()
    {
        await PrepararAsync();
        await AdicionarAsync("Boné", 0, 4);                  // 5 × R$ 20 = R$ 100

        Tecla(ComandoTecla.Enter);
        Tecla(ComandoTecla.Direita);                         // %
        _vm.DescontoTexto = "10";
        Assert.Contains("90,00", _vm.DescontoResumo.Replace(' ', ' '));
        Tecla(ComandoTecla.Enter);

        Assert.Equal(EtapaPdv.Pagamento, _vm.Etapa);
        Assert.Equal("R$ 90,00", _vm.TotalFinalTexto.Replace(' ', ' '));
    }

    [Fact]
    public async Task Desconto_invalido_nao_avanca()
    {
        await PrepararAsync();
        await AdicionarAsync("Boné", 0, 0);
        Tecla(ComandoTecla.Enter);

        _vm.DescontoTexto = "999";                           // em R$, maior que o subtotal
        Tecla(ComandoTecla.Enter);

        Assert.Equal(EtapaPdv.Desconto, _vm.Etapa);
        Assert.True(_vm.MensagemEhErro);
    }

    // ---------- pagamento ----------

    [Fact]
    public async Task Credito_em_3_parcelas_pelas_setas()
    {
        await PrepararAsync();
        await AdicionarAsync("Boné", 0, 0);
        Tecla(ComandoTecla.Enter, ComandoTecla.Enter);       // desconto, pagamento
        Assert.Equal(EtapaPdv.Pagamento, _vm.Etapa);

        Tecla(ComandoTecla.Direita, ComandoTecla.Direita, ComandoTecla.Direita); // Pix, Débito, Crédito
        Assert.True(_vm.EmCredito);
        Tecla(ComandoTecla.Cima, ComandoTecla.Cima);         // 3x
        Tecla(ComandoTecla.Enter);
        await AteAsync(() => _vm.Etapa == EtapaPdv.Sucesso, "sucesso");

        var venda = Assert.Single(Vendas());
        Assert.Equal((FormaPagamento.CartaoCredito, 3), (venda.FormaPagamento, venda.Parcelas));
        Assert.Contains("3x", _vm.SucessoDetalhe);
    }

    [Fact]
    public async Task Dinheiro_com_valor_menor_que_o_total_bloqueia()
    {
        await PrepararAsync();
        await AdicionarAsync("Boné", 0, 0);                  // R$ 20,00
        Tecla(ComandoTecla.Enter, ComandoTecla.Enter);

        _vm.ValorRecebidoTexto = "10";
        Assert.Contains("Faltam", _vm.TrocoTexto);
        Tecla(ComandoTecla.Enter);
        await Task.Delay(100);

        Assert.Equal(EtapaPdv.Pagamento, _vm.Etapa);
        Assert.Contains("faltam", _vm.Mensagem);
        Assert.Empty(Vendas());
        Assert.Equal(10, EstoqueDe(_boneId, Tamanhos.SemTamanho));
    }

    [Fact]
    public async Task Dinheiro_com_valor_maior_calcula_o_troco_e_mostra_no_sucesso()
    {
        await PrepararAsync();
        await AdicionarAsync("Boné", 0, 0);
        Tecla(ComandoTecla.Enter, ComandoTecla.Enter);

        _vm.ValorRecebidoTexto = "50";
        Assert.Contains("30,00", _vm.TrocoTexto);
        Tecla(ComandoTecla.Enter);
        await AteAsync(() => _vm.Etapa == EtapaPdv.Sucesso, "sucesso");

        Assert.Contains("Troco", _vm.SucessoDetalhe);
        Assert.Equal(3000, Vendas().Single().TrocoCentavos);
    }

    [Fact]
    public async Task A_prazo_exige_cliente_e_aceita_o_da_lista()
    {
        await PrepararAsync();
        var maria = await _clientes.CadastrarAsync(new DadosCliente("Maria"));
        await AdicionarAsync("Boné", 0, 0);
        Tecla(ComandoTecla.Enter, ComandoTecla.Enter);

        Tecla(ComandoTecla.Esquerda);                        // dá a volta: A prazo
        Assert.True(_vm.EmAPrazo);
        await AteAsync(() => _vm.ClientesResultado.Count == 1, "lista de clientes");

        // com a lista aberta o primeiro cliente já vem destacado: o Enter finaliza com ele
        Tecla(ComandoTecla.Enter);
        await AteAsync(() => _vm.Etapa == EtapaPdv.Sucesso, "sucesso");

        var venda = Assert.Single(Vendas());
        Assert.Equal((FormaPagamento.APrazo, (int?)maria.Id), (venda.FormaPagamento, venda.ClienteId));
    }

    [Fact]
    public async Task A_prazo_sem_nenhum_cliente_bloqueia_e_nome_novo_cadastra_no_primeiro_Enter()
    {
        await PrepararAsync();
        await AdicionarAsync("Boné", 0, 0);
        Tecla(ComandoTecla.Enter, ComandoTecla.Enter);
        Tecla(ComandoTecla.Esquerda);

        Tecla(ComandoTecla.Enter);                           // nenhum cliente, nada digitado
        await Task.Delay(100);
        Assert.Equal(EtapaPdv.Pagamento, _vm.Etapa);
        Assert.Contains("cliente", _vm.Mensagem);
        Assert.Empty(Vendas());

        _vm.ClienteBusca = "João Novo";
        await AteAsync(() => _vm.ClientesResultado.Count == 0 && _vm.ClienteDestacado is null, "lista vazia");
        Tecla(ComandoTecla.Enter);                           // 1º Enter: cadastra
        await AteAsync(() => _vm.ClienteDestacado is not null, "cliente cadastrado");
        Assert.Equal(EtapaPdv.Pagamento, _vm.Etapa);
        Assert.Empty(Vendas());

        Tecla(ComandoTecla.Enter);                           // 2º Enter: finaliza
        await AteAsync(() => _vm.Etapa == EtapaPdv.Sucesso, "sucesso");
        Assert.Equal("João Novo", (await _clientes.BuscarAsync("João")).Single().Nome);
    }

    // ---------- proteções contra venda duplicada ----------

    [Fact]
    public async Task Enter_repetido_no_pagamento_cria_uma_unica_venda_e_nao_pula_a_tela_de_sucesso()
    {
        await PrepararAsync();
        var vm = NovoVm(TimeSpan.FromMilliseconds(300)); // sucesso ignora Enter por 300 ms
        vm.Busca = "Boné";
        vm.ProcessarTecla(ComandoTecla.Enter);
        await AteAsync(() => vm.Etapa == EtapaPdv.Tamanho, "tamanho");
        vm.ProcessarTecla(ComandoTecla.Enter);
        vm.ProcessarTecla(ComandoTecla.Cima);
        vm.ProcessarTecla(ComandoTecla.Enter);          // 2 unidades no carrinho
        vm.ProcessarTecla(ComandoTecla.Enter);          // desconto
        vm.ProcessarTecla(ComandoTecla.Enter);          // pagamento
        Assert.Equal(EtapaPdv.Pagamento, vm.Etapa);

        // vários Enters seguidos (duplo clique / tecla segurada) sem tempo entre eles
        var tratados = new[]
        {
            vm.ProcessarTecla(ComandoTecla.Enter),
            vm.ProcessarTecla(ComandoTecla.Enter),
            vm.ProcessarTecla(ComandoTecla.Enter)
        };

        Assert.All(tratados, Assert.True);              // todos consumidos pelo fluxo
        Assert.Equal(EtapaPdv.Sucesso, vm.Etapa);       // os extras NÃO pularam a tela de sucesso
        Assert.Single(Vendas());
        Assert.Equal(8, EstoqueDe(_boneId, Tamanhos.SemTamanho)); // baixou uma vez só (10 - 2)

        await Task.Delay(400);
        vm.ProcessarTecla(ComandoTecla.Enter);          // agora sim: nova venda
        Assert.Equal(EtapaPdv.Busca, vm.Etapa);
        Assert.Single(Vendas());
    }
    [Fact]
    public async Task Enter_com_modal_aberto_nunca_dispara_busca()
    {
        await PrepararAsync();
        await BuscarEEscolherAsync("Camiseta");              // modal de tamanho

        _vm.Busca = "Boné";                                  // texto sobrando no campo de busca
        Tecla(ComandoTecla.Enter);                           // confirma o TAMANHO, não busca "Boné"

        Assert.Equal(EtapaPdv.Quantidade, _vm.Etapa);
        Assert.Contains("Camiseta", _vm.ProdutoModalTexto);
    }

    [Fact]
    public async Task Duplo_Enter_na_tela_de_sucesso_nao_reenvia_a_venda()
    {
        await PrepararAsync();
        await AdicionarAsync("Boné", 0, 0);
        Tecla(ComandoTecla.Enter, ComandoTecla.Enter, ComandoTecla.Enter);
        await AteAsync(() => _vm.Etapa == EtapaPdv.Sucesso, "sucesso");

        Tecla(ComandoTecla.Enter);                           // nova venda
        Tecla(ComandoTecla.Enter);                           // busca vazia + carrinho vazio: só avisa

        Assert.Equal(EtapaPdv.Busca, _vm.Etapa);
        Assert.Single(Vendas());
    }

    // ---------- sucesso ----------

    [Fact]
    public async Task Sucesso_setas_alternam_e_Esc_e_nova_venda()
    {
        await PrepararAsync();
        await AdicionarAsync("Boné", 0, 0);
        Tecla(ComandoTecla.Enter, ComandoTecla.Enter, ComandoTecla.Enter);
        await AteAsync(() => _vm.Etapa == EtapaPdv.Sucesso, "sucesso");

        Assert.True(_vm.OpcoesSucesso[1].Selecionada);       // "Nova venda"
        Tecla(ComandoTecla.Esquerda);
        Assert.True(_vm.OpcoesSucesso[0].Selecionada);       // "Reimprimir cupom"
        Tecla(ComandoTecla.Enter);
        Assert.Equal(EtapaPdv.Sucesso, _vm.Etapa);           // reimprimir não sai da tela

        Tecla(ComandoTecla.Esc);
        Assert.Equal(EtapaPdv.Busca, _vm.Etapa);
    }

    // ---------- rascunho ----------

    [Fact]
    public async Task O_carrinho_e_guardado_sozinho_e_volta_ao_reabrir()
    {
        await PrepararAsync();
        await AdicionarAsync("Camiseta", 1, 1);              // 2 × M

        var novo = NovoVm();                                 // como se o programa tivesse sido reaberto
        await novo.RestaurarRascunhoAsync();

        var linha = Assert.Single(novo.Itens);
        Assert.Equal(("Camiseta", 2), (linha.Nome, linha.Quantidade));
        Assert.Equal("  ·  tam. M", linha.TamanhoTexto);
        Assert.Contains("restaurada", novo.Mensagem);
    }

    [Fact]
    public async Task Depois_de_finalizar_nao_sobra_rascunho()
    {
        await PrepararAsync();
        await AdicionarAsync("Boné", 0, 0);
        Tecla(ComandoTecla.Enter, ComandoTecla.Enter, ComandoTecla.Enter);
        await AteAsync(() => _vm.Etapa == EtapaPdv.Sucesso, "sucesso");

        var novo = NovoVm();
        await novo.RestaurarRascunhoAsync();

        Assert.Empty(novo.Itens);
    }

    // ---------- cupom ----------

    private async Task FinalizarVendaDeUmBoneAsync()
    {
        await AdicionarAsync("Boné", 0, 0);
        Tecla(ComandoTecla.Enter, ComandoTecla.Enter, ComandoTecla.Enter);
        await AteAsync(() => _vm.Etapa == EtapaPdv.Sucesso, "sucesso");
    }

    [Fact]
    public async Task Ao_finalizar_o_cupom_sai_com_o_numero_da_venda_e_os_itens()
    {
        await PrepararAsync();

        await FinalizarVendaDeUmBoneAsync();

        var cupom = Assert.Single(_impressora.Impressos);
        Assert.Contains("CUPOM NÃO FISCAL", cupom);
        Assert.Contains("Venda nº 1", cupom);
        Assert.Contains("Boné", cupom);
        Assert.Contains("Cupom enviado", _vm.Mensagem);
    }

    [Fact]
    public async Task Falha_da_impressora_nao_desfaz_a_venda_e_avisa_na_tela_de_sucesso()
    {
        await PrepararAsync();
        _impressora.Falha = new InvalidOperationException("Impressora sem papel.");

        await FinalizarVendaDeUmBoneAsync();

        Assert.Equal(EtapaPdv.Sucesso, _vm.Etapa);
        Assert.Single(Vendas());                              // a venda foi gravada
        Assert.Equal(9, EstoqueDe(_boneId, Tamanhos.SemTamanho));
        Assert.True(_vm.MensagemEhErro);
        Assert.Contains("não foi impresso", _vm.Mensagem);
        Assert.Contains("sem papel", _vm.Mensagem);
    }

    [Fact]
    public async Task Reimprimir_cupom_na_tela_de_sucesso_imprime_de_novo_e_nao_sai_da_tela()
    {
        await PrepararAsync();
        await FinalizarVendaDeUmBoneAsync();

        Tecla(ComandoTecla.Esquerda, ComandoTecla.Enter);     // [Reimprimir cupom]
        await AteAsync(() => _impressora.Impressos.Count == 2, "segunda impressão");

        Assert.Equal(EtapaPdv.Sucesso, _vm.Etapa);
        Assert.Equal(_impressora.Impressos[0], _impressora.Impressos[1]); // é o mesmo cupom
        Assert.Single(Vendas());
    }

    [Fact]
    public async Task Com_a_impressao_automatica_desligada_nao_imprime_mas_reimprimir_funciona()
    {
        await PrepararAsync();
        await _configuracao.SalvarAsync(new DadosLoja("Loja", ImprimirCupomAoFinalizar: false));

        await FinalizarVendaDeUmBoneAsync();
        Assert.Empty(_impressora.Impressos);

        Tecla(ComandoTecla.Esquerda, ComandoTecla.Enter);
        await AteAsync(() => _impressora.Impressos.Count == 1, "reimpressão");
    }

    [Fact]
    public async Task O_cupom_leva_os_dados_da_loja_salvos_em_Configuracoes()
    {
        await PrepararAsync();
        await _configuracao.SalvarAsync(new DadosLoja("Casa Nexus", "12.345.678/0001-90", MensagemCupom: "Volte sempre!"));

        await FinalizarVendaDeUmBoneAsync();

        var cupom = Assert.Single(_impressora.Impressos);
        Assert.Contains("Casa Nexus", cupom);
        Assert.Contains("12.345.678/0001-90", cupom);
        Assert.Contains("Volte sempre!", cupom);
    }
}
