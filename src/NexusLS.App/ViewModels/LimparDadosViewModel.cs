using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexusLS.Core.Services;

namespace NexusLS.App.ViewModels;

/// <summary>
/// Bloco "Apagar dados" de Configurações (o "Limpar tudo"). Só o administrador vê. Para apagar é preciso escolher o
/// que apagar, digitar APAGAR e confirmar numa pergunta final; o serviço ainda faz o backup antes. Ao terminar o
/// programa reinicia, porque telas abertas (e o carrinho do PDV) ficariam com dados que não existem mais.
/// </summary>
public partial class LimparDadosViewModel : ViewModelBase
{
    private readonly LimpezaService _limpeza;
    private readonly Func<bool> _podeApagar;
    private readonly Func<string, bool> _confirmar;
    private readonly Action _aoTerminar;

    public LimparDadosViewModel(LimpezaService limpeza, Func<bool> podeApagar, Func<string, bool> confirmar, Action aoTerminar)
    {
        _limpeza = limpeza;
        _podeApagar = podeApagar;
        _confirmar = confirmar;
        _aoTerminar = aoTerminar;
    }

    /// <summary>Quem não é administrador nem enxerga o bloco.</summary>
    public bool Visivel => _podeApagar();

    public string DescricaoMovimento => LimpezaService.Descricao(NivelLimpeza.Movimento);
    public string DescricaoTudo => LimpezaService.Descricao(NivelLimpeza.Tudo);

    [ObservableProperty] private bool _apagarTudo;
    [ObservableProperty] private string _confirmacao = "";

    public NivelLimpeza Nivel => ApagarTudo ? NivelLimpeza.Tudo : NivelLimpeza.Movimento;

    [RelayCommand]
    private Task ApagarAsync() => ExecutarAsync(async () =>
    {
        if (!_podeApagar())
            throw new RegraDeNegocioException("Só o administrador pode apagar os dados.");

        var oQue = ApagarTudo ? "TUDO da loja (movimento, produtos, clientes e fornecedores)" : "todo o MOVIMENTO (vendas, caixa, pedidos, compras e financeiro)";
        var pergunta = $"Isto vai apagar {oQue}. Não dá para desfazer pelo programa; só restaurando o backup que será feito agora.\n\nO programa será reiniciado. Continuar?";

        // Primeiro a regra do serviço (digitou APAGAR?), depois a pergunta: assim o erro de digitação aparece antes de qualquer janela.
        if (!string.Equals(Confirmacao.Trim(), LimpezaService.TextoDeConfirmacao, StringComparison.Ordinal))
            throw new RegraDeNegocioException($"Para apagar, digite {LimpezaService.TextoDeConfirmacao} (em maiúsculas).");
        if (!_confirmar(pergunta)) return;

        var r = await _limpeza.LimparAsync(Nivel, Confirmacao);
        Confirmacao = "";
        MostrarSucesso($"{r.Total} registro(s) apagado(s). Backup de segurança: {Path.GetFileName(r.ArquivoBackup)}");
        _aoTerminar();
    });
}
