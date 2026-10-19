using System.IO;
using NexusLS.App.Seguranca;
using NexusLS.App.ViewModels;
using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Core.Sincronizacao;
using NexusLS.Core.Sincronizacao.Firebase;
using NexusLS.Tests.Infra;

namespace NexusLS.App.Tests;

public sealed class CatalogoOnlineViewModelTests : IDisposable
{
    private sealed class DestinoDeMentira : IDestinoCatalogo
    {
        public FalhaDestinoException? FalhaNaVerificacao;
        public FalhaDestinoException? FalhaNoEnvio;
        public int Verificacoes;
        public List<OperacaoCatalogo> Enviadas { get; } = new();

        public Task VerificarAsync(CancellationToken ct = default)
        {
            Verificacoes++;
            return FalhaNaVerificacao is null ? Task.CompletedTask : Task.FromException(FalhaNaVerificacao);
        }

        public Task EnviarLoteAsync(IReadOnlyList<OperacaoCatalogo> lote, CancellationToken ct = default)
        {
            if (FalhaNoEnvio is not null) return Task.FromException(FalhaNoEnvio);
            Enviadas.AddRange(lote);
            return Task.CompletedTask;
        }

        public Task RegistrarAtualizacaoAsync(DateTime agora, int total, CancellationToken ct = default) => Task.CompletedTask;
    }

    private readonly BancoDeTeste _banco = new();
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "nexusls-co-" + Guid.NewGuid().ToString("N"));
    private readonly DestinoDeMentira _destino = new();
    private readonly SincronizacaoRepositorio _repo;
    private readonly ConfiguracaoFirebaseStore _store;
    private readonly ProdutoService _produtos;

    public CatalogoOnlineViewModelTests()
    {
        Directory.CreateDirectory(_pasta);
        _repo = new SincronizacaoRepositorio(_banco.Fabrica);
        _store = new ConfiguracaoFirebaseStore(Path.Combine(_pasta, "firebase.json"), new ProtetorDpapi());
        _produtos = new ProdutoService(_banco.Fabrica);
    }

    public void Dispose()
    {
        _banco.Dispose();
        try { Directory.Delete(_pasta, true); } catch (IOException) { }
    }

    private async Task<CatalogoOnlineViewModel> CriarAsync(bool configurado = true)
    {
        if (configurado)
            _store.Salvar(new ConfiguracaoFirebase { ProjectId = "loja-1a2b3", ApiKey = "AIzaChave", Email = "loja@exemplo.com" }, "senha-secreta");
        var vm = new CatalogoOnlineViewModel(_store, new SincronizadorCatalogo(_repo, _destino), _repo, _destino, new ConfiguracaoService(_banco.Fabrica));
        await vm.Carregamento;
        return vm;
    }

    private Task CadastrarAsync(int quantidade) => Task.Run(async () =>
    {
        for (var i = 1; i <= quantidade; i++)
            await _produtos.CadastrarAsync(new DadosProduto($"Produto {i}", $"C{i}", 1000, 400, 1), 5);
    });

    // ---------- configuração ----------

    [Fact]
    public async Task Sem_configuracao_o_botao_fica_desligado_e_explica_o_que_falta()
    {
        var vm = await CriarAsync(configurado: false);

        Assert.False(vm.PodeAtualizar);
        Assert.Contains("Ainda não configurado", vm.SituacaoConfiguracao);
    }

    [Fact]
    public async Task Salvar_grava_a_configuracao_limpa_a_senha_da_tela_e_liga_o_botao()
    {
        var vm = await CriarAsync(configurado: false);
        vm.ProjectId = "loja-1a2b3";
        vm.ApiKey = "AIzaChave";
        vm.Email = "loja@exemplo.com";
        vm.SenhaNova = "senha-secreta";

        await vm.SalvarConfiguracaoCommand.ExecuteAsync(null);

        Assert.Equal("Configuração salva.", vm.Mensagem);
        Assert.Equal("", vm.SenhaNova);                 // a senha nunca fica na tela
        Assert.True(vm.PodeAtualizar);
        Assert.Equal("senha-secreta", _store.ObterSenha());
        Assert.DoesNotContain("senha-secreta", File.ReadAllText(Path.Combine(_pasta, "firebase.json")));
    }

    [Fact]
    public async Task Configuracao_invalida_mostra_o_erro_e_nao_grava()
    {
        var vm = await CriarAsync(configurado: false);
        vm.ProjectId = "Nome Inválido";
        vm.ApiKey = "x";
        vm.Email = "a@b.com";
        vm.SenhaNova = "s";

        await vm.SalvarConfiguracaoCommand.ExecuteAsync(null);

        Assert.True(vm.MensagemEhErro);
        Assert.Contains("id do projeto", vm.Mensagem);
        Assert.False(File.Exists(Path.Combine(_pasta, "firebase.json")));
    }

    [Fact]
    public async Task Testar_conexao_mostra_sucesso_ou_o_motivo_da_falha()
    {
        var vm = await CriarAsync();

        await vm.TestarConexaoCommand.ExecuteAsync(null);
        Assert.False(vm.MensagemEhErro);
        Assert.Contains("Conexão ok", vm.Mensagem);

        _destino.FalhaNaVerificacao = new FalhaDestinoException("Sem permissão para gravar no Firestore.");
        await vm.TestarConexaoCommand.ExecuteAsync(null);
        Assert.True(vm.MensagemEhErro);
        Assert.Equal("Sem permissão para gravar no Firestore.", vm.Mensagem);
        Assert.False(vm.Ocupado);
    }

    // ---------- Atualizar Firestore ----------

    [Fact]
    public async Task Clicar_mostra_o_plano_e_NAO_envia_nada_antes_da_confirmacao()
    {
        await CadastrarAsync(3);
        var vm = await CriarAsync();

        await vm.AtualizarFirestoreCommand.ExecuteAsync(null);

        Assert.True(vm.AguardandoConfirmacao);
        Assert.Equal("Vai enviar 3 novo(s). Nada será apagado na nuvem.", vm.TextoConfirmacao);
        Assert.Empty(_destino.Enviadas);
        Assert.False(vm.PodeAtualizar); // não deixa clicar de novo enquanto espera a resposta
        Assert.Empty(await _repo.ObterEstadoAsync());
    }

    [Fact]
    public async Task Confirmar_envia_mostra_o_resultado_e_atualiza_o_historico()
    {
        await CadastrarAsync(3);
        var vm = await CriarAsync();
        await vm.AtualizarFirestoreCommand.ExecuteAsync(null);

        await vm.ConfirmarEnvioCommand.ExecuteAsync(null);

        Assert.Equal(3, _destino.Enviadas.Count);
        Assert.False(vm.AguardandoConfirmacao);
        Assert.False(vm.ResultadoEhErro);
        Assert.Contains("3 produto(s) enviado(s)", vm.ResultadoTexto);
        var linha = Assert.Single(vm.Historico);
        Assert.Contains("Sucesso", linha.Texto);
        Assert.True(vm.PodeAtualizar);
        Assert.Equal(3, (await _repo.ObterEstadoAsync()).Count);
    }

    [Fact]
    public async Task Cancelar_a_previa_nao_envia_nada_e_libera_o_botao()
    {
        await CadastrarAsync(2);
        var vm = await CriarAsync();
        await vm.AtualizarFirestoreCommand.ExecuteAsync(null);

        vm.CancelarPreviaCommand.Execute(null);

        Assert.False(vm.AguardandoConfirmacao);
        Assert.Empty(_destino.Enviadas);
        Assert.Equal("Nada foi enviado.", vm.ResultadoTexto);
        Assert.True(vm.PodeAtualizar);
    }

    [Fact]
    public async Task Sem_mudancas_nao_pede_confirmacao_e_registra_no_historico()
    {
        await CadastrarAsync(2);
        var vm = await CriarAsync();
        await vm.AtualizarFirestoreCommand.ExecuteAsync(null);
        await vm.ConfirmarEnvioCommand.ExecuteAsync(null);

        await vm.AtualizarFirestoreCommand.ExecuteAsync(null);

        Assert.False(vm.AguardandoConfirmacao);
        Assert.Contains("Nada a enviar", vm.ResultadoTexto);
        Assert.Equal(2, vm.Historico.Count);
        Assert.Contains("Sem mudanças", vm.Historico[0].Texto);
    }

    [Fact]
    public async Task Sem_conexao_mostra_o_erro_registra_a_tentativa_e_nao_pede_confirmacao()
    {
        await CadastrarAsync(1);
        _destino.FalhaNaVerificacao = new FalhaDestinoException("Sem conexão com a internet.");
        var vm = await CriarAsync();

        await vm.AtualizarFirestoreCommand.ExecuteAsync(null);

        Assert.True(vm.ResultadoEhErro);
        Assert.Equal("Sem conexão com a internet.", vm.ResultadoTexto);
        Assert.False(vm.AguardandoConfirmacao);
        Assert.Contains("Falhou", Assert.Single(vm.Historico).Texto);
        Assert.False(vm.Ocupado);
    }

    [Fact]
    public async Task Falha_no_envio_vira_resultado_com_erro_na_tela_e_o_botao_volta()
    {
        await CadastrarAsync(2);
        var vm = await CriarAsync();
        await vm.AtualizarFirestoreCommand.ExecuteAsync(null);
        _destino.FalhaNoEnvio = new FalhaDestinoException("A conexão caiu.");

        await vm.ConfirmarEnvioCommand.ExecuteAsync(null);

        Assert.True(vm.ResultadoEhErro);
        Assert.Contains("A conexão caiu.", vm.ResultadoTexto);
        Assert.Contains("Falhou", Assert.Single(vm.Historico).Texto);
        Assert.True(vm.PodeAtualizar);
        Assert.Empty(await _repo.ObterEstadoAsync());
    }

    [Fact]
    public async Task Produto_invalido_aparece_na_lista_de_problemas_e_o_plano_diz_que_ficou_de_fora()
    {
        await CadastrarAsync(1);
        await using (var db = _banco.CriarContexto())
        {
            db.Produtos.Add(new Produto { Nome = "Estoque ruim", PrecoVendaCentavos = 100, Tamanhos = { new ProdutoTamanho { Tamanho = "Sem tamanho", Estoque = -1 } } });
            await db.SaveChangesAsync();
        }
        var vm = await CriarAsync();

        await vm.AtualizarFirestoreCommand.ExecuteAsync(null);

        Assert.Contains("1 com dados inválidos ficam de fora", vm.TextoConfirmacao);
        Assert.Contains("Estoque ruim: estoque negativo no tamanho Sem tamanho", Assert.Single(vm.Problemas));
    }

    [Fact]
    public async Task Confirmar_sem_ter_clicado_antes_nao_faz_nada()
    {
        await CadastrarAsync(1);
        var vm = await CriarAsync();

        await vm.ConfirmarEnvioCommand.ExecuteAsync(null);

        Assert.Empty(_destino.Enviadas);
        Assert.Empty(vm.Historico);
    }
}

public class ProtetorDpapiTests
{
    [Fact]
    public void Protege_e_devolve_a_mesma_senha_e_o_texto_protegido_nao_a_revela()
    {
        var protetor = new ProtetorDpapi();

        var protegido = protetor.Proteger("minha-senha-123");

        Assert.DoesNotContain("minha-senha-123", protegido);
        Assert.Equal("minha-senha-123", protetor.Desproteger(protegido));
    }

    [Fact]
    public void Dado_de_outro_computador_ou_estragado_vira_nulo_em_vez_de_quebrar()
    {
        var protetor = new ProtetorDpapi();

        Assert.Null(protetor.Desproteger("isto-nao-e-base64-valido!!"));
        Assert.Null(protetor.Desproteger(Convert.ToBase64String(new byte[] { 1, 2, 3, 4, 5 })));
    }
}
