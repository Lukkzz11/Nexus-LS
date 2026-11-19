using System.IO;
using System.Net.Http;
using NexusLS.App.Aparencia;
using NexusLS.App.ViewModels;
using NexusLS.App.Views;
using NexusLS.Core.Models;
using NexusLS.Core.Seguranca;
using NexusLS.Core.Services;
using NexusLS.Core.Sincronizacao.Nuvem;
using NexusLS.Tests.Infra;

namespace NexusLS.App.Tests;

/// <summary>
/// Desenha a aba Lojas com a nuvem (compartilhar, comparativo, preço por loja) nos temas claro e escuro. Só roda com
/// NEXUSLS_PRINTS apontando para uma pasta; usa Firestore simulado e banco de teste, nunca o real.
/// </summary>
[Collection("Sessao")]
public sealed class CapturaNuvemLojas : IDisposable
{
    private readonly BancoDeTeste _banco = new();
    private readonly string _cache = Path.Combine(Path.GetTempPath(), "nexus_print_nuvem_" + Guid.NewGuid().ToString("N") + ".json");

    public void Dispose()
    {
        Capturador.Rodar(() => GerenciadorDeTema.Aplicar(false));
        _banco.Dispose();
        try { File.Delete(_cache); } catch (IOException) { }
    }

    [Fact]
    public async Task Gera_a_aba_lojas_com_a_nuvem_nos_dois_temas()
    {
        var saida = Environment.GetEnvironmentVariable("NEXUSLS_PRINTS");
        if (string.IsNullOrWhiteSpace(saida)) return;
        Directory.CreateDirectory(saida);

        var agora = DateTime.Today.AddHours(15).ToUniversalTime();
        var nuvem = new FirestoreNuvemFalso();
        new CacheNuvemLoja(_cache).Salvar(new EstadoNuvemLoja { EmpresaId = "minha-empresa" });
        var produtos = new ProdutoService(_banco.Fabrica);
        var camiseta = await produtos.CadastrarAsync(new DadosProduto("Camiseta Básica Algodão", "7891234567895", 5990, 2000, 2, "CAM-100", 3990), 20);
        await new VendaService(_banco.Fabrica, () => DateTime.Today.AddHours(10)).FinalizarAsync(
            new PedidoVenda(new[] { new ItemCarrinho(camiseta.Id, 3) }, 0, FormaPagamento.Pix));
        var servico = new ServicoNuvemLoja(_banco.Fabrica,
            () => new ClienteNuvemLoja(new HttpClient(nuvem), "projeto-teste", "minha-empresa", new TokenDeTeste()), new CacheNuvemLoja(_cache),
            () => ("loja-centro", "Loja Centro"), new PrecoLojaService(_banco.Fabrica), () => true, () => true, "1.0", () => agora, () => DateTime.Today);

        nuvem.Docs["empresas/minha-empresa/lojas/loja-praia"] = SerializadorPacoteLoja.ParaCampos(MontadorPacoteLoja.Loja("loja-praia", "Loja Praia", "1.0", agora.AddHours(-5)));
        nuvem.Docs[$"empresas/minha-empresa/lojas/loja-praia/dias/{DateTime.Today:yyyy-MM-dd}"] = SerializadorPacoteLoja.ParaCampos(
            new ResumoDiaNuvem(DateTime.Today, 154000, 7, 12, 0, 0, 0, 0, new Dictionary<string, long>(), Array.Empty<ItemDiaNuvem>(), agora));

        GestaoEmpresaViewModel? empresa = null;
        Capturador.Rodar(() =>
        {
            var lojas = new LojasViewModel(new RelatorioFinanceiroService(_banco.Fabrica),
                () => new ConfiguracaoSeguranca { LojaNome = "Loja Centro", LojaId = "loja-centro" });
            lojas.UsarNuvem(servico, produtos, () => true, () => true);
            empresa = new GestaoEmpresaViewModel(lojas, null);
        });
        await empresa!.Lojas.Carregamento;
        await Capturador.RodarAsync(() => empresa.Lojas.CompartilharCommand.ExecuteAsync(null));
        await Capturador.RodarAsync(() => empresa.Lojas.LerDaNuvemCommand.ExecuteAsync(null));
        empresa.Lojas.BuscaProdutoPreco = "Camiseta";
        await Capturador.RodarAsync(() => empresa.Lojas.BuscarProdutoDoPrecoCommand.ExecuteAsync(null));
        if (empresa.Lojas.PrecosPorLoja.Count > 0) { empresa.Lojas.PrecosPorLoja[0].Varejo = "49,90"; empresa.Lojas.PrecosPorLoja[0].Atacado = "35,00"; }

        foreach (var (escuro, nome) in new[] { (false, "claro"), (true, "escuro") })
        {
            Capturador.Rodar(() => GerenciadorDeTema.Aplicar(escuro));
            Capturador.Desenhar(() => new GestaoEmpresaView { DataContext = empresa }, Path.Combine(saida, $"nuvem-lojas-{nome}.png"), 1400, 1500);
        }

        // sem internet: o aviso com a data do último dado baixado
        nuvem.SemInternet = true;
        await Capturador.RodarAsync(() => empresa.Lojas.LerDaNuvemCommand.ExecuteAsync(null));
        Capturador.Rodar(() => GerenciadorDeTema.Aplicar(false));
        Capturador.Desenhar(() => new GestaoEmpresaView { DataContext = empresa }, Path.Combine(saida, "nuvem-lojas-sem-internet.png"), 1400, 1500);
    }
}
