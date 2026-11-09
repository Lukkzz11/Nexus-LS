using System.IO;
using System.Net;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using NexusLS.App.Views;
using NexusLS.Core.Importacao.Erp;
using NexusLS.Core.Sincronizacao.Firebase;
using NexusLS.Core.Sincronizacao.Operacional;

namespace NexusLS.App.Tests;

/// <summary>A janela "Conexão com a nuvem" vem preenchida com o projeto do ERP (erp.json) sem mostrar a chave nem a senha.</summary>
public sealed class ConexaoBancoWindowTests : IDisposable
{
    private const string ChaveSecreta = "CHAVE-QUE-NAO-PODE-APARECER";
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "nexusls-conexao-" + Guid.NewGuid().ToString("N"));

    public ConexaoBancoWindowTests() => Directory.CreateDirectory(_pasta);
    public void Dispose() { try { Directory.Delete(_pasta, true); } catch (IOException) { } }

    private sealed class Protetor : IProtetorSegredo
    {
        public string Proteger(string textoClaro) => "P:" + textoClaro;
        public string? Desproteger(string protegido) => protegido[2..];
    }

    private sealed class SemRede : HttpMessageHandler
    {
        public int Pedidos { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Pedidos++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        }
    }

    private static IEnumerable<T> Todos<T>(DependencyObject raiz) where T : DependencyObject
    {
        foreach (var filho in LogicalTreeHelper.GetChildren(raiz).OfType<DependencyObject>())
        {
            if (filho is T t) yield return t;
            foreach (var neto in Todos<T>(filho)) yield return neto;
        }
    }

    [Fact]
    public void Vem_preenchida_com_o_projeto_do_erp_e_nao_mostra_a_chave()
    {
        var erp = new ConfiguracaoErpStore(Path.Combine(_pasta, "erp.json"));
        erp.Salvar(new ConfiguracaoErp { ProjectId = "pdv---lifesurf", ApiKey = ChaveSecreta, EmpresaId = "lifesurf-principal" });
        var rede = new SemRede();
        var conta = new ContaErp(erp, new HttpClient(rede), new Protetor(), Path.Combine(_pasta, "sessao_erp.json"));

        string? combo = null; var textos = new List<string>(); var senhas = new List<string>(); string? estadoChave = null;
        Capturador.Rodar(() =>
        {
            var janela = new ConexaoBancoWindow(
                new ConfiguracaoFirebaseStore(Path.Combine(_pasta, "firebase.json"), new Protetor()),
                new ConfiguracaoOperacionalStore(Path.Combine(_pasta, "operacional.json")), erp, conta);
            combo = ((ComboBox)Todos<ComboBox>(janela).First()).SelectedItem as string;
            textos.AddRange(Todos<TextBox>(janela).Select(t => t.Text));
            senhas.AddRange(Todos<PasswordBox>(janela).Select(p => p.Password));
            estadoChave = Todos<TextBlock>(janela).Select(t => t.Text).FirstOrDefault(t => t.StartsWith("Chave da API salva"));
        });

        Assert.StartsWith("Importar do ERP PDV Life", combo);
        Assert.Contains("pdv---lifesurf", textos);
        Assert.Contains("lifesurf-principal", textos);
        Assert.DoesNotContain(textos, t => t.Contains(ChaveSecreta));
        Assert.All(senhas, s => Assert.Equal("", s));
        Assert.NotNull(estadoChave);
        Assert.Equal(0, rede.Pedidos); // abrir a janela não fala com a internet
    }

    [Fact]
    public void Sem_erp_configurado_o_catalogo_continua_sendo_a_conta_escolhida_quando_ja_ha_projeto()
    {
        var firebase = new ConfiguracaoFirebaseStore(Path.Combine(_pasta, "firebase.json"), new Protetor());
        firebase.Salvar(new ConfiguracaoFirebase { ProjectId = "minha-loja-1a2b3", ApiKey = "k", Email = "a@b.com" }, "s");
        var erp = new ConfiguracaoErpStore(Path.Combine(_pasta, "erp.json"));

        int indice = -1;
        Capturador.Rodar(() =>
        {
            var janela = new ConexaoBancoWindow(firebase, new ConfiguracaoOperacionalStore(Path.Combine(_pasta, "operacional.json")), erp,
                new ContaErp(erp, new HttpClient(new SemRede()), new Protetor(), Path.Combine(_pasta, "sessao_erp.json")));
            indice = Todos<ComboBox>(janela).First().SelectedIndex;
        });
        Assert.Equal(1, indice);
    }
}
