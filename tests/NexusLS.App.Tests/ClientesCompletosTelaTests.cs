using System.IO;
using System.Text;
using NexusLS.App.ViewModels;
using NexusLS.App.Views;
using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.App.Tests;

/// <summary>A tela Clientes completa: CPF/CNPJ obrigatório, crédito com histórico, exportar e importar CSV.</summary>
[Collection("OperadorAtual")]
public sealed class ClientesCompletosTelaTests : IDisposable
{
    private static readonly DateTime Agora = new(2026, 11, 5, 10, 0, 0);
    private const string Cpf = "529.982.247-25";

    private readonly BancoDeTeste _banco = new();
    private readonly ClienteService _clientes;
    private readonly ClientesViewModel _vm;

    public ClientesCompletosTelaTests()
    {
        OperadorAtual.Nome = "Ana";
        _clientes = new ClienteService(_banco.Fabrica, () => Agora) { ExigirDocumentoNaTela = true };
        _vm = new ClientesViewModel(_clientes, hoje: () => Agora);
    }

    public void Dispose()
    {
        OperadorAtual.Nome = null;
        _banco.Dispose();
    }

    private async Task CadastrarAsync(string nome, string documento, string razao = "", string email = "")
    {
        _vm.NovoCommand.Execute(null);
        _vm.Nome = nome;
        _vm.Documento = documento;
        _vm.RazaoSocial = razao;
        _vm.Email = email;
        _vm.Telefone = "(81) 99999-0000";
        await _vm.SalvarCommand.ExecuteAsync(null);
    }

    [Fact]
    public async Task Cadastro_completo_guarda_so_digitos_e_reabre_com_mascara()
    {
        await CadastrarAsync("Loja do Zé", "11222333000181", "Zé Comércio LTDA", "ze@loja.com.br");

        Assert.False(_vm.MensagemEhErro, _vm.Mensagem);
        var c = (await _clientes.BuscarAsync("Zé")).Single();
        Assert.Equal(("11222333000181", "Zé Comércio LTDA", "ze@loja.com.br"), (c.Documento, c.RazaoSocial, c.Email));
        Assert.Equal("11.222.333/0001-81", _vm.Documento);
        Assert.Equal("1 cliente", _vm.ResumoLista);
    }

    [Fact]
    public async Task Sem_documento_ou_com_documento_errado_a_tela_avisa_e_nao_grava()
    {
        await CadastrarAsync("Maria", "");
        Assert.True(_vm.MensagemEhErro);
        Assert.Contains("CPF ou o CNPJ", _vm.Mensagem);

        await CadastrarAsync("Maria", "123.456.789-00");
        Assert.True(_vm.MensagemEhErro);
        Assert.Contains("CPF inválido", _vm.Mensagem);

        Assert.Empty(await _clientes.BuscarAsync(null, incluirInativos: true));
    }

    [Fact]
    public async Task Documento_repetido_e_recusado_na_tela()
    {
        await CadastrarAsync("Maria", Cpf);
        await CadastrarAsync("Outra", "52998224725");

        Assert.True(_vm.MensagemEhErro);
        Assert.Contains("já está cadastrado para Maria", _vm.Mensagem);
    }

    [Fact]
    public void A_dica_do_documento_acompanha_a_digitacao()
    {
        Assert.Contains("Obrigatório", _vm.DocumentoDica);

        _vm.Documento = "529.982";
        Assert.False(_vm.DocumentoComErro);

        _vm.Documento = Cpf;
        Assert.Equal("CPF válido.", _vm.DocumentoDica);

        _vm.Documento = "529.982.247-24";
        Assert.True(_vm.DocumentoComErro);

        _vm.Documento = "11.222.333/0001-81";
        Assert.Equal("CNPJ válido.", _vm.DocumentoDica);
    }

    [Fact]
    public async Task Ajuste_de_credito_pela_tela_atualiza_saldo_e_historico()
    {
        await CadastrarAsync("Maria", Cpf);

        _vm.NovoCreditoTexto = "50,00";
        _vm.CreditoMotivo = "oi"; // curto demais
        await _vm.SalvarCreditoCommand.ExecuteAsync(null);
        Assert.True(_vm.MensagemEhErro);
        Assert.Contains("motivo", _vm.Mensagem);

        _vm.CreditoMotivo = "Devolução de blusa";
        await _vm.SalvarCreditoCommand.ExecuteAsync(null);

        Assert.False(_vm.MensagemEhErro, _vm.Mensagem);
        Assert.Equal("R$ 50,00", _vm.CreditoAtualTexto.Replace(' ', ' '));
        var linha = Assert.Single(_vm.HistoricoCredito);
        Assert.Contains("Devolução de blusa", linha);
        Assert.Contains("Ana", linha);
        Assert.Contains("05/11/2026 10:00", linha);
        Assert.Equal(5000, _vm.Clientes.Single().CreditoCentavos);
    }

    [Fact]
    public async Task Saldo_de_credito_invalido_na_tela_nao_grava()
    {
        await CadastrarAsync("Maria", Cpf);
        _vm.NovoCreditoTexto = "abc";
        _vm.CreditoMotivo = "Teste de erro";
        await _vm.SalvarCreditoCommand.ExecuteAsync(null);

        Assert.True(_vm.MensagemEhErro);
        Assert.Empty(_vm.HistoricoCredito);
    }

    [Fact]
    public async Task Exportar_grava_o_csv_com_bom_e_avisa_quando_nao_ha_clientes()
    {
        await _vm.ExportarCommand.ExecuteAsync(null);
        Assert.True(_vm.MensagemEhErro);
        Assert.Contains("Não há clientes", _vm.Mensagem);

        await CadastrarAsync("Maria", Cpf, "", "maria@mail.com");
        var caminho = Path.Combine(Path.GetTempPath(), $"clientes-tela-{Guid.NewGuid():N}.csv");
        try
        {
            string? nomeSugerido = null;
            _vm.EscolherArquivoParaSalvar = nome => { nomeSugerido = nome; return caminho; };
            await _vm.ExportarCommand.ExecuteAsync(null);

            Assert.False(_vm.MensagemEhErro, _vm.Mensagem);
            Assert.Equal("clientes-2026-11-05.csv", nomeSugerido);
            var bytes = File.ReadAllBytes(caminho);
            Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
            var texto = new UTF8Encoding(false).GetString(bytes[3..]);
            Assert.StartsWith("CPF/CNPJ;Nome;Razão social;Telefone;E-mail;Crédito;Limite a prazo;Observação;Situação", texto);
            Assert.Contains("529.982.247-25;Maria;;(81) 99999-0000;maria@mail.com;0,00;0,00;;Ativo", texto);
        }
        finally { File.Delete(caminho); }
    }

    [Fact]
    public async Task Importar_mostra_a_previa_so_grava_ao_confirmar_e_cancelar_nao_grava()
    {
        await CadastrarAsync("Maria Antiga", Cpf);
        var arquivo = Path.Combine(Path.GetTempPath(), $"clientes-import-{Guid.NewGuid():N}.csv");
        File.WriteAllText(arquivo,
            "CPF/CNPJ;Nome;Crédito\r\n529.982.247-25;Maria Souza;10,00\r\n11.222.333/0001-81;Loja Nova;\r\n123;Ruim;\r\n",
            new UTF8Encoding(true));
        try
        {
            _vm.EscolherArquivoParaAbrir = () => arquivo;
            await _vm.ImportarCommand.ExecuteAsync(null);

            Assert.True(_vm.ImportacaoAberta);
            Assert.True(_vm.ImportacaoPodeConfirmar);
            Assert.Equal("1 novo(s) · 1 para atualizar · 0 sem mudança · 1 com erro", _vm.ResumoImportacao);
            Assert.Equal(3, _vm.LinhasImportacao.Count);
            Assert.Equal("Maria Antiga", (await _clientes.BuscarAsync(null)).Single().Nome); // nada gravado ainda

            _vm.CancelarImportacaoCommand.Execute(null);
            Assert.False(_vm.ImportacaoAberta);
            Assert.Single(await _clientes.BuscarAsync(null));

            await _vm.ImportarCommand.ExecuteAsync(null);
            await _vm.ConfirmarImportacaoCommand.ExecuteAsync(null);

            Assert.False(_vm.ImportacaoAberta);
            Assert.False(_vm.MensagemEhErro, _vm.Mensagem);
            Assert.Contains("1 cliente(s) novo(s), 1 atualizado(s)", _vm.Mensagem);
            Assert.Equal(new[] { "Loja Nova", "Maria Souza" }, _vm.Clientes.Select(c => c.Nome).OrderBy(n => n));
            Assert.Equal(1000, _vm.Clientes.Single(c => c.Nome == "Maria Souza").CreditoCentavos);
        }
        finally { File.Delete(arquivo); }
    }

    [Fact]
    public async Task Arquivo_sem_as_colunas_certas_mostra_o_erro_e_nao_abre_a_previa()
    {
        var arquivo = Path.Combine(Path.GetTempPath(), $"clientes-ruim-{Guid.NewGuid():N}.csv");
        File.WriteAllText(arquivo, "Foo;Bar\r\n1;2\r\n");
        try
        {
            _vm.EscolherArquivoParaAbrir = () => arquivo;
            await _vm.ImportarCommand.ExecuteAsync(null);

            Assert.False(_vm.ImportacaoAberta);
            Assert.True(_vm.MensagemEhErro);
            Assert.Contains("CPF/CNPJ", _vm.Mensagem);
        }
        finally { File.Delete(arquivo); }
    }
}

/// <summary>
/// Desenha a tela Clientes nova em PNG (com dados e vazia, em 1900x1000 e 1280x760). Só roda com NEXUSLS_PRINTS apontando
/// para uma pasta e só usa o banco de teste. Rode UM teste por vez (--filter).
/// </summary>
[Collection("OperadorAtual")]
public sealed class CapturaClientesNovo : IDisposable
{
    private static readonly DateTime Agora = new(2026, 11, 5, 10, 0, 0);
    private readonly BancoDeTeste _banco = new();

    public CapturaClientesNovo() => OperadorAtual.Nome = "Ana";

    public void Dispose()
    {
        OperadorAtual.Nome = null;
        _banco.Dispose();
    }

    private static string? Saida()
    {
        var saida = Environment.GetEnvironmentVariable("NEXUSLS_PRINTS");
        if (string.IsNullOrWhiteSpace(saida)) return null;
        Directory.CreateDirectory(saida);
        return saida;
    }

    private static async Task AteAsync(Func<bool> condicao)
    {
        for (var i = 0; i < 400 && !condicao(); i++) await Task.Delay(10);
    }

    [Fact]
    public async Task Gera_a_tela_de_clientes_vazia()
    {
        var saida = Saida();
        if (saida is null) return;

        var clientes = new ClienteService(_banco.Fabrica, () => Agora) { ExigirDocumentoNaTela = true };
        ClientesViewModel? vm = null;
        Capturador.Rodar(() => vm = new ClientesViewModel(clientes, new TituloPrazoService(_banco.Fabrica, () => Agora), () => Agora));
        await Task.Delay(300);

        Capturador.Desenhar(() => new ClientesView { DataContext = vm }, Path.Combine(saida, "vazio-1900x1000.png"), 1900, 1000);
        Capturador.Desenhar(() => new ClientesView { DataContext = vm }, Path.Combine(saida, "vazio-1280x760.png"), 1280, 760);
    }

    [Fact]
    public async Task Gera_a_tela_de_clientes_com_dados_credito_e_importacao()
    {
        var saida = Saida();
        if (saida is null) return;

        var clientes = new ClienteService(_banco.Fabrica, () => Agora) { ExigirDocumentoNaTela = true };
        var titulos = new TituloPrazoService(_banco.Fabrica, () => Agora);
        var vendas = new VendaService(_banco.Fabrica, () => Agora.AddDays(-12));
        var produtos = new ProdutoService(_banco.Fabrica);
        var camiseta = await produtos.CadastrarAsync(new DadosProduto("Camiseta Básica Algodão", null, 5990, 2000, 2), 50);

        DadosCliente Dc(string nome, string doc, string tel, string? razao = null, string? email = null, long limite = 0) =>
            new(nome, doc, tel, null, limite, razao, email, ExigirDocumento: true);

        var maria = await clientes.CadastrarAsync(Dc("Maria Souza", "52998224725", "(81) 99999-0000", null, "maria.souza@email.com", 150000));
        var loja = await clientes.CadastrarAsync(Dc("Loja do Zé", "11222333000181", "(81) 3333-0000", "Zé Comércio de Confecções LTDA", "compras@lojadoze.com.br", 500000));
        var joao = await clientes.CadastrarAsync(Dc("João Pereira", "39053344705", "(81) 98888-7777", null, "joao.pereira@email.com"));
        await clientes.CadastrarAsync(Dc("Ana Beatriz Lima", "11144477735", "(81) 97777-1234"));
        var antigo = await clientes.CadastrarAsync(Dc("Carlos Antigo", "12345678909", "(81) 96666-0000"));
        await clientes.DesativarAsync(antigo.Id);
        await clientes.AjustarCreditoAsync(maria.Id, 12550, "Devolução de blusa (troca por crédito)");
        await clientes.AjustarCreditoAsync(maria.Id, 8050, "Usou parte do crédito na compra #14");
        await clientes.AjustarCreditoAsync(loja.Id, 50000, "Adiantamento para a próxima coleção");
        await clientes.AjustarCreditoAsync(joao.Id, 3000, "Bônus de aniversário");
        await vendas.FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(camiseta.Id, 3) }, 0, FormaPagamento.APrazo, ClienteId: maria.Id));
        await vendas.FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(camiseta.Id, 1) }, 0, FormaPagamento.Pix, ClienteId: maria.Id));

        ClientesViewModel? vm = null;
        Capturador.Rodar(() => vm = new ClientesViewModel(clientes, titulos, () => Agora));
        await AteAsync(() => vm!.Clientes.Count == 4);
        Capturador.Rodar(() => vm!.Selecionado = vm.Clientes.First(c => c.Nome.StartsWith("Maria")));
        await AteAsync(() => vm!.Compras.Count == 2 && vm.HistoricoCredito.Count == 2);

        Capturador.Desenhar(() => new ClientesView { DataContext = vm }, Path.Combine(saida, "dados-1900x1000.png"), 1900, 1000);
        Capturador.Desenhar(() => new ClientesView { DataContext = vm }, Path.Combine(saida, "dados-1280x760.png"), 1280, 760);

        // pré-visualização da importação: um novo, um que atualiza, um sem mudança e erros com o motivo
        var csv = ExportadorClientes.Csv(await clientes.ListarParaExportacaoAsync()).Replace("(81) 99999-0000", "(81) 95555-4321")
                  + "52998224725;Maria repetida;;;;;;;\r\n"
                  + "11.222.333/0001-82;Padaria Estrela;Estrela Alimentos ME;(81) 3222-1010;contato@estrela.com;0,00;;;\r\n"
                  + "390.533.447-06;Pedro Novo;;;pedro@@email;;;;\r\n"
                  + "11.598.236/0001-98;Mercadinho Central;Central Comércio LTDA;(81) 3111-2222;central@mercado.com;20,00;;;\r\n";
        Capturador.Rodar(() => _ = vm!.PreverAsync(csv, "clientes-novembro.csv"));
        await AteAsync(() => vm!.ImportacaoAberta);
        await Task.Delay(200);
        Capturador.Desenhar(() => new ClientesView { DataContext = vm }, Path.Combine(saida, "importacao-1900x1000.png"), 1900, 1000);
        Capturador.Desenhar(() => new ClientesView { DataContext = vm }, Path.Combine(saida, "importacao-1280x760.png"), 1280, 760);
    }
}
