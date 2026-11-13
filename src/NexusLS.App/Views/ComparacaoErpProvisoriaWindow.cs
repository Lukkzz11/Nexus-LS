using System.Text;
using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;
using NexusLS.App.ViewModels;
using NexusLS.Core.Services;
using NexusLS.Core.Sincronizacao;
using NexusLS.Core.Sincronizacao.Firebase;

namespace NexusLS.App.Views;

/// <summary>
/// PROVISÓRIO (teste com os dados do ERP Life): importa tudo do ERP (só leitura) e mostra um relatório comparando
/// com o que já havia no Nexus: produtos novos/atualizados, preços de atacado que mudaram ou parecem errados e itens repetidos.
/// Para remover: apagar este arquivo e o botão "Importar ERP e comparar (provisório)" em ConfiguracoesView.xaml / .xaml.cs.
/// </summary>
public sealed class ComparacaoErpProvisoriaWindow : Window
{
    private sealed record Linha(Guid Guid, string Nome, string? Referencia, string? Codigo, long Varejo, long Atacado);

    private readonly TextBox _relatorio = new()
    {
        IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        FontFamily = new System.Windows.Media.FontFamily("Consolas"), FontSize = 12, MinHeight = 380, Margin = new Thickness(0, 10, 0, 0)
    };
    private readonly Button _importar = new() { Content = "Importar do ERP e comparar", Padding = new Thickness(16, 8, 16, 8), Margin = new Thickness(0, 0, 8, 0) };
    private readonly Button _copiar = new() { Content = "Copiar relatório", Padding = new Thickness(16, 8, 16, 8), IsEnabled = false };

    public ComparacaoErpProvisoriaWindow()
    {
        Title = "Importar ERP e comparar (provisório)";
        Width = 720;
        Height = 680;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _importar.Click += async (_, _) => await ExecutarAsync();
        _copiar.Click += (_, _) => { try { Clipboard.SetText(_relatorio.Text); } catch { /* área de transferência ocupada */ } };

        var topo = new StackPanel { Orientation = Orientation.Horizontal };
        topo.Children.Add(_importar);
        topo.Children.Add(_copiar);

        var p = new StackPanel { Margin = new Thickness(20) };
        p.Children.Add(new TextBlock
        {
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10),
            Text = "Traz os produtos, clientes e vendas do ERP Life para este Nexus (somente leitura: nada é gravado no ERP) e mostra o que mudou. " +
                   "Pede o seu login do ERP se ainda não entrou. Produtos que já existem aqui são ligados aos do ERP, sem duplicar."
        });
        p.Children.Add(topo);
        p.Children.Add(_relatorio);
        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = p };
    }

    private async Task<List<Linha>> LerAsync()
    {
        await using var db = await App.Servicos.FabricaBanco.CreateDbContextAsync();
        return await db.Produtos.AsNoTracking()
            .Select(x => new Linha(x.Guid, x.Nome, x.Referencia, x.CodigoBarras, x.PrecoVendaCentavos, x.PrecoAtacadoCentavos)).ToListAsync();
    }

    private async Task ExecutarAsync()
    {
        _importar.IsEnabled = false;
        _copiar.IsEnabled = false;
        try
        {
            var antes = (await LerAsync()).ToDictionary(x => x.Guid);

            var conta = App.Servicos.ContaErp;
            if (!App.Servicos.ErpConfig.Carregar().EstaCompleta)
            {
                _relatorio.Text = "O ERP ainda não está configurado. Em Configurações → Conectar banco de dados → \"Importar do ERP PDV Life\", informe o projeto, a empresa e a chave da API.";
                return;
            }
            if (!await conta.RestaurarAsync() && LoginFirebaseWindow.PedirErp(conta, this) is null)
            {
                _relatorio.Text = "Sem entrar no ERP não dá para importar. Clique em \"Importar do ERP e comparar\" de novo e informe o usuário do ERP.";
                return;
            }

            _relatorio.Text = "Lendo o ERP...";
            var r = await App.Servicos.CriarImportadorErp().ImportarAsync(default, new Progress<string>(m => _relatorio.Text = m));

            var depois = await LerAsync();
            string ErpIds;
            HashSet<Guid> ligados;
            await using (var db = await App.Servicos.FabricaBanco.CreateDbContextAsync())
            {
                ligados = (await db.ImportacaoErp.AsNoTracking().Where(x => x.Colecao == "produtos").Select(x => x.EntidadeGuid).ToListAsync()).ToHashSet();
                ErpIds = ligados.Count.ToString();
            }
            _relatorio.Text = Montar(r, antes, depois, ligados, ErpIds);
            _copiar.IsEnabled = true;
        }
        catch (Exception ex) when (ex is FalhaDestinoException or FalhaLoginException or RegraDeNegocioException)
        {
            _relatorio.Text = "Não foi possível importar: " + ex.Message;
        }
        finally { _importar.IsEnabled = true; }
    }

    private static string Montar(NexusLS.Core.Importacao.Erp.ResultadoImportacaoErp r, Dictionary<Guid, Linha> antes, List<Linha> depois, HashSet<Guid> ligados, string produtosDoErp)
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== O QUE FOI LIDO DO ERP ===");
        sb.AppendLine(ImportacaoErpViewModel.Resumir(r));
        sb.AppendLine();
        sb.AppendLine($"Produtos no Nexus antes: {antes.Count}   depois: {depois.Count}   ligados ao ERP: {produtosDoErp}");

        var mudouAtacado = depois.Where(d => antes.TryGetValue(d.Guid, out var a) && a.Atacado != d.Atacado).ToList();
        sb.AppendLine();
        sb.AppendLine($"=== ATACADO QUE MUDOU COM O ERP: {mudouAtacado.Count} ===");
        foreach (var d in mudouAtacado.Take(30))
            sb.AppendLine($"{d.Nome}: {Dinheiro.Formatar(antes[d.Guid].Atacado)} -> {Dinheiro.Formatar(d.Atacado)}  (varejo {Dinheiro.Formatar(d.Varejo)})");

        var semAtacado = depois.Where(d => d.Atacado <= 0).ToList();
        var atacadoAlto = depois.Where(d => d.Atacado > 0 && d.Atacado >= d.Varejo).ToList();
        sb.AppendLine();
        sb.AppendLine($"=== SEM PREÇO DE ATACADO (vale o varejo): {semAtacado.Count} de {depois.Count} ===");
        foreach (var d in semAtacado.Take(15)) sb.AppendLine($"{d.Nome}  (varejo {Dinheiro.Formatar(d.Varejo)})");
        sb.AppendLine();
        sb.AppendLine($"=== ATACADO IGUAL OU MAIOR QUE O VAREJO (suspeito): {atacadoAlto.Count} ===");
        foreach (var d in atacadoAlto.Take(20)) sb.AppendLine($"{d.Nome}: varejo {Dinheiro.Formatar(d.Varejo)}, atacado {Dinheiro.Formatar(d.Atacado)}");

        var repetidosCodigo = depois.Where(d => !string.IsNullOrWhiteSpace(d.Codigo)).GroupBy(d => d.Codigo!).Where(g => g.Count() > 1).ToList();
        var repetidosNome = depois.GroupBy(d => (d.Nome.Trim().ToLowerInvariant(), (d.Referencia ?? "").Trim().ToLowerInvariant())).Where(g => g.Count() > 1).ToList();
        sb.AppendLine();
        sb.AppendLine($"=== ITENS REPETIDOS: {repetidosNome.Count} por nome+referência, {repetidosCodigo.Count} por código de barras ===");
        foreach (var g in repetidosNome.Take(20)) sb.AppendLine($"{g.First().Nome} (ref. {g.Key.Item2}) x{g.Count()}");
        foreach (var g in repetidosCodigo.Take(10)) sb.AppendLine($"código {g.Key}: {string.Join(" | ", g.Select(x => x.Nome))}");

        var soAqui = depois.Where(d => !ligados.Contains(d.Guid)).ToList();
        sb.AppendLine();
        sb.AppendLine($"=== SÓ EXISTEM NO NEXUS (não estão no ERP): {soAqui.Count} ===");
        foreach (var d in soAqui.Take(25)) sb.AppendLine($"{d.Nome}  ref. {d.Referencia}  varejo {Dinheiro.Formatar(d.Varejo)}  atacado {Dinheiro.Formatar(d.Atacado)}");
        return sb.ToString();
    }
}
