using CommunityToolkit.Mvvm.Input;
using NexusLS.BarTender.Services;
using NexusLS.Core.Models;

namespace NexusLS.BarTender.ViewModels;

public partial class SelecaoImpressaoViewModel
{
    /// <summary>Tamanhos com quantidade que o produto ainda não tem no Nexus: sem cadastro, não têm código próprio.</summary>
    private List<string> TamanhosSemCadastro() => Linhas
        .Where(l => !l.EhSemTamanho && l.TemQuantidade)
        .Select(l => l.Tamanho)
        .Where(t => Produto.Tamanhos?.Any(c => string.Equals(c.Tamanho, t, StringComparison.OrdinalIgnoreCase)) != true)
        .ToList();

    public bool TemTamanhoSemCadastro => TamanhosSemCadastro().Count > 0;

    public string AvisoCadastro =>
        $"Ainda não cadastrados no Nexus: {string.Join(", ", TamanhosSemCadastro())}. Cadastre para cada um ter o seu código.";

    public string MensagemCadastro { get; private set; } = "";

    [RelayCommand]
    private async Task CadastrarTamanhosAsync()
    {
        var banco = GerenciadorEtiquetasProdutoService.Banco;
        var novos = TamanhosSemCadastro();
        if (banco is null || novos.Count == 0) return;

        try
        {
            var grade = await banco.CadastrarTamanhosAsync(Produto.Id, novos);
            Produto.Tamanhos = grade;
            MensagemCadastro = "";
        }
        catch (Exception ex)
        {
            MensagemCadastro = $"Não foi possível cadastrar: {ex.Message}";
        }

        OnPropertyChanged(nameof(MensagemCadastro));
        AtualizarTotais();
    }
}
