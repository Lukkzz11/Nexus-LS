using System.IO;
using NexusLS.App.ViewModels;
using NexusLS.Core.Sincronizacao;
using NexusLS.Core.Vitrine;

namespace NexusLS.App.Tests;

public sealed class EditorCatalogoTests : IDisposable
{
    private readonly string _arquivo = Path.Combine(Path.GetTempPath(), "vitrine-app-" + Guid.NewGuid().ToString("N") + ".json");
    public void Dispose() { if (File.Exists(_arquivo)) File.Delete(_arquivo); }

    private sealed class PublicadorFalso : IPublicadorVitrine
    {
        public List<ConfiguracaoVitrine> Enviados { get; } = new();
        public Exception? Erro { get; set; }
        public Task PublicarAsync(ConfiguracaoVitrine config, CancellationToken ct = default)
        {
            if (Erro is not null) throw Erro;
            Enviados.Add(config);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Salvar_limpa_guarda_e_recarrega_e_cor_invalida_mostra_erro()
    {
        var vm = new EditorCatalogoViewModel(new VitrineStore(_arquivo));
        vm.Modelo = "elegante";
        vm.LojaNome = "  Life   Surf ";
        vm.Cores.First(c => c.Id == "botao").Valor = "ABC";
        await vm.SalvarCommand.ExecuteAsync(null);
        Assert.False(vm.MensagemEhErro, vm.Mensagem);
        Assert.Equal("Life Surf", vm.LojaNome);
        Assert.Equal("#aabbcc", vm.Cores.First(c => c.Id == "botao").Valor);

        var outro = new EditorCatalogoViewModel(new VitrineStore(_arquivo));       // reabrir mostra o que foi salvo
        Assert.Equal("elegante", outro.Modelo);

        outro.Cores.First(c => c.Id == "fundo").Valor = "azulzao";
        await outro.SalvarCommand.ExecuteAsync(null);
        Assert.True(outro.MensagemEhErro);
        Assert.Contains("Fundo geral", outro.Mensagem);
    }

    [Fact]
    public async Task Publicar_salva_envia_e_mostra_a_falha_do_firebase_em_portugues()
    {
        var pub = new PublicadorFalso();
        var vm = new EditorCatalogoViewModel(new VitrineStore(_arquivo), pub);
        Assert.True(vm.PodePublicar);
        vm.AtacadoMinPecas = "15";
        await vm.PublicarCommand.ExecuteAsync(null);
        Assert.False(vm.MensagemEhErro, vm.Mensagem);
        Assert.Equal(15, Assert.Single(pub.Enviados).AtacadoMinPecas);
        Assert.Equal(15, new VitrineStore(_arquivo).Carregar().AtacadoMinPecas);

        pub.Erro = new FalhaDestinoException("Sem conexão com a internet.");
        await vm.PublicarCommand.ExecuteAsync(null);
        Assert.True(vm.MensagemEhErro);
        Assert.Equal("Sem conexão com a internet.", vm.Mensagem);

        vm.AtacadoMinPecas = "abc";
        await vm.PublicarCommand.ExecuteAsync(null);
        Assert.Contains("número inteiro", vm.Mensagem);
    }

    [Fact]
    public void Contraste_ruim_aparece_como_aviso_e_voltar_ao_padrao_limpa_as_cores()
    {
        var vm = new EditorCatalogoViewModel(new VitrineStore(_arquivo));
        vm.Cores.First(c => c.Id == "titulo").Valor = "#cccccc";
        vm.Cores.First(c => c.Id == "fundo").Valor = "#ffffff";
        Assert.Single(vm.Avisos);
        vm.RestaurarCommand.Execute(null);
        Assert.Empty(vm.Avisos);
        Assert.All(vm.Cores, c => Assert.Equal("", c.Valor));
    }

    [Fact]
    public async Task A_tela_do_editor_abre_com_os_estilos_do_programa()
    {
        var vm = new EditorCatalogoViewModel(new VitrineStore(_arquivo), new PublicadorFalso());
        vm.Cores.First(c => c.Id == "botao").Valor = "#1e40af";
        vm.Cores.First(c => c.Id == "titulo").Valor = "#cccccc";
        vm.Cores.First(c => c.Id == "fundo").Valor = "#ffffff";
        var pasta = Environment.GetEnvironmentVariable("NEXUSLS_PRINTS") ?? Path.GetTempPath();
        Directory.CreateDirectory(pasta);
        var png = Path.Combine(pasta, "editor-catalogo.png");
        Capturador.SalvarEditorCatalogo(vm, png);
        Assert.True(new FileInfo(png).Length > 5000);
        if (Environment.GetEnvironmentVariable("NEXUSLS_PRINTS") is null) File.Delete(png);
        await Task.CompletedTask;
    }
}
