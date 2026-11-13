using System.Windows;
using System.Windows.Controls;
using NexusLS.Core.Services;

namespace NexusLS.App.Views;

/// <summary>Lista os produtos repetidos (mesmo nome). "Mesclar" soma o estoque no primeiro da lista (o de maior estoque) e desativa os outros.</summary>
public sealed class MesclarDuplicadosWindow : Window
{
    private readonly MesclaProdutos _servico;
    private readonly ListBox _lista = new() { Height = 260, Margin = new Thickness(0, 8, 0, 8) };
    private readonly TextBlock _msg = new() { TextWrapping = TextWrapping.Wrap };
    private IReadOnlyList<GrupoDuplicado> _grupos = Array.Empty<GrupoDuplicado>();
    private readonly TaskCompletionSource _fim = new();

    public MesclarDuplicadosWindow(MesclaProdutos servico)
    {
        _servico = servico;
        Title = "Mesclar produtos duplicados";
        Width = 560;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        var mesclar = new Button { Content = "Mesclar o grupo selecionado", Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 0, 8, 0) };
        mesclar.Click += async (_, _) => await MesclarAsync();
        var fechar = new Button { Content = "Fechar", Padding = new Thickness(14, 6, 14, 6), IsCancel = true };
        var linha = new StackPanel { Orientation = Orientation.Horizontal };
        linha.Children.Add(mesclar);
        linha.Children.Add(fechar);
        var p = new StackPanel { Margin = new Thickness(20) };
        p.Children.Add(new TextBlock { Text = "Produtos com o mesmo nome", FontSize = 18, FontWeight = FontWeights.Bold });
        p.Children.Add(new TextBlock { Text = "O estoque do repetido vai para o que tem mais estoque; o repetido fica desativado e as vendas antigas são mantidas.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
        p.Children.Add(_lista);
        p.Children.Add(linha);
        p.Children.Add(_msg);
        Content = p;
        Closed += (_, _) => _fim.TrySetResult();
        Loaded += async (_, _) => await CarregarAsync();
    }

    public Task MostrarAsync() { Show(); return _fim.Task; }

    private async Task CarregarAsync()
    {
        _grupos = await _servico.ListarAsync();
        _lista.Items.Clear();
        foreach (var g in _grupos)
            _lista.Items.Add($"{g.Nome}  ·  {g.Produtos.Count} cadastros  ·  estoque: {string.Join(" + ", g.Produtos.Select(x => x.EstoqueAtual))}");
        _msg.Text = _grupos.Count == 0 ? "Nenhum produto repetido." : $"{_grupos.Count} grupo(s) encontrado(s).";
    }

    private async Task MesclarAsync()
    {
        if (_lista.SelectedIndex < 0) { _msg.Text = "Selecione um grupo."; return; }
        var g = _grupos[_lista.SelectedIndex];
        if (MessageBox.Show(this, $"Juntar {g.Produtos.Count} cadastros de \"{g.Nome}\" em um só?\nO estoque será somado e os repetidos ficam desativados.", "Mesclar", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try
        {
            await NexusLS.Core.Backup.CopiaAntesDeImportar.ExecutarAsync();
            foreach (var extra in g.Produtos.Skip(1)) await _servico.MesclarAsync(g.Produtos[0].Id, extra.Id);
            await CarregarAsync();
            _msg.Text = "Mesclado. " + _msg.Text;
        }
        catch (RegraDeNegocioException e) { _msg.Text = e.Message; }
    }
}
