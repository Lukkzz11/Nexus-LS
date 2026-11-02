using System.IO;
using NexusLS.App.ViewModels;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.App.Tests;

[Collection("OperadorAtual")]
public sealed class ConfiguracoesAbasTests : IDisposable
{
    private sealed class ProtetorFalso : NexusLS.Core.Sincronizacao.Firebase.IProtetorSegredo
    {
        public string Proteger(string t) => t;
        public string? Desproteger(string t) => t;
    }

    private readonly BancoDeTeste _banco = new();
    private readonly string _arquivo = Path.Combine(Path.GetTempPath(), "pref-app-" + Guid.NewGuid().ToString("N") + ".json");

    public void Dispose()
    {
        _banco.Dispose();
        if (File.Exists(_arquivo)) File.Delete(_arquivo);
    }

    private ConfiguracoesViewModel Criar()
    {
        var config = new ConfiguracaoService(_banco.Fabrica);
        return new ConfiguracoesViewModel(config, new CupomService(_banco.Fabrica, config, new ImpressoraDeMentira()));
    }

    [Fact]
    public void Zoom_vale_na_hora_e_fica_guardado_e_o_desconto_so_com_salvar()
    {
        var vm = Criar();
        double? zoom = null;
        var store = new PreferenciasStore(_arquivo);
        vm.UsarPreferencias(store, z => zoom = z, () => "Licença válida", Path.GetTempPath());
        Assert.Equal(100, vm.ZoomSelecionado!.Percentual);
        Assert.Equal("2,00", vm.DescontoPorPeca);
        Assert.Equal("Licença válida", vm.LicencaTexto);

        vm.ZoomSelecionado = vm.Zooms.First(z => z.Percentual == 125);
        Assert.Equal(1.25, zoom);
        Assert.Equal(125, store.Carregar().ZoomPercentual);

        vm.DescontoPorPeca = "3,50";
        Assert.Equal(200, store.Carregar().DescontoPorPecaCentavos);             // ainda não salvou
        vm.SalvarPreferenciasCommand.Execute(null);
        Assert.False(vm.MensagemPreferenciasEhErro, vm.MensagemPreferencias);
        Assert.Equal(350, store.Carregar().DescontoPorPecaCentavos);

        vm.DescontoPorPeca = "abc";
        vm.SalvarPreferenciasCommand.Execute(null);
        Assert.True(vm.MensagemPreferenciasEhErro);
        Assert.Equal(350, store.Carregar().DescontoPorPecaCentavos);
    }

    [Fact]
    public void Abrir_pasta_usa_a_pasta_informada_e_o_formato_do_tamanho_e_legivel()
    {
        var vm = Criar();
        string? aberta = null;
        vm.AbrirPasta = p => aberta = p;
        vm.UsarPreferencias(new PreferenciasStore(_arquivo), _ => { }, null, @"C:\dados");
        vm.AbrirPastaDeDadosCommand.Execute(null);
        Assert.Equal(@"C:\dados", aberta);
        Assert.Equal("1 KB", ConfiguracoesViewModel.FormatarTamanho(10));
        Assert.Contains("MB", ConfiguracoesViewModel.FormatarTamanho((long)(2.5 * 1024 * 1024)));
    }

    [Fact]
    public void As_cinco_abas_abrem_com_os_estilos_do_programa()
    {
        var vm = Criar();
        vm.UsarPreferencias(new PreferenciasStore(_arquivo), _ => { }, () => "Licença válida até 31/12/2027", Path.GetTempPath());
        var gs = new NexusLS.Core.Google.GoogleStore(Path.Combine(Path.GetTempPath(), "g-" + Guid.NewGuid().ToString("N") + ".json"), new ProtetorFalso());
        var ga = new NexusLS.Core.Google.GoogleOAuth(new System.Net.Http.HttpClient(), gs);
        vm.UsarGoogle(new NexusLS.Core.Google.GoogleDriveBackup(gs, ga, new NexusLS.Core.Google.DriveRest(new System.Net.Http.HttpClient(), ga)), _ => { });
        var pasta = Environment.GetEnvironmentVariable("NEXUSLS_PRINTS") ?? Path.GetTempPath();
        Directory.CreateDirectory(pasta);
        for (var aba = 0; aba < 5; aba++)
        {
            var png = Path.Combine(pasta, $"configuracoes-aba{aba}.png");
            Capturador.SalvarConfiguracoesAba(vm, aba, png);
            Assert.True(new FileInfo(png).Length > 5000);
            if (Environment.GetEnvironmentVariable("NEXUSLS_PRINTS") is null) File.Delete(png);
        }
    }
}
