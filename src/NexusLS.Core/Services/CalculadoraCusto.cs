using System.Globalization;

namespace NexusLS.Core.Services;

/// <summary>Um acréscimo (juros, frete...) somado ao preço calculado da compra.</summary>
public sealed record AcrescimoCusto(string Descricao, long Centavos);

/// <summary>
/// Calculadora de custo: uma calculadora de bancada (visor, + − × ÷, =) para chegar ao preço da compra,
/// mais uma lista de acréscimos (juros % sobre o valor atual e valores avulsos como frete).
/// Lógica pura, sem tela, para poder ser testada. Contas em decimal; o resultado final vai para centavos.
/// </summary>
public sealed class CalculadoraCusto
{
    private static readonly CultureInfo PtBr = new("pt-BR");
    private const int MaxDigitos = 12;

    private decimal? _acumulado;
    private char? _operador;
    private string _entrada = "0";
    private bool _novaEntrada = true;
    private bool _erro;
    private readonly List<AcrescimoCusto> _acrescimos = new();

    public IReadOnlyList<AcrescimoCusto> Acrescimos => _acrescimos;

    /// <summary>Texto mostrado no visor (vírgula decimal).</summary>
    public string Visor => _erro ? "Erro" : _entrada.Replace('.', ',');

    private decimal ValorEntrada => decimal.Parse(_entrada, CultureInfo.InvariantCulture);

    public void Digito(int d)
    {
        if (d < 0 || d > 9) return;
        if (_erro) Limpar();
        if (_novaEntrada) { _entrada = "0"; _novaEntrada = false; }
        if (_entrada.Replace(".", "").Length >= MaxDigitos) return;
        _entrada = _entrada == "0" ? d.ToString(CultureInfo.InvariantCulture) : _entrada + d.ToString(CultureInfo.InvariantCulture);
    }

    public void Virgula()
    {
        if (_erro) Limpar();
        if (_novaEntrada) { _entrada = "0"; _novaEntrada = false; }
        if (!_entrada.Contains('.')) _entrada += ".";
    }

    /// <summary>Operador: '+', '-', '*' ou '/'. Encadeia como a calculadora do Windows (1+2+ já mostra 3).</summary>
    public void Operador(char op)
    {
        if (op is not ('+' or '-' or '*' or '/') || _erro) return;
        if (_operador is not null && !_novaEntrada)
        {
            if (!Aplicar()) return;
        }
        else if (_operador is null)
        {
            _acumulado = ValorEntrada;
        }
        _operador = op;
        _novaEntrada = true;
    }

    public void Igual()
    {
        if (_erro) return;
        if (_operador is not null && _acumulado is not null && !Aplicar()) return;
        _operador = null; _acumulado = null; _novaEntrada = true;
    }

    public void Limpar()
    {
        _acumulado = null; _operador = null; _entrada = "0"; _novaEntrada = true; _erro = false;
    }

    public void Apagar()
    {
        if (_erro) { Limpar(); return; }
        if (_novaEntrada) return;
        _entrada = _entrada.Length <= 1 ? "0" : _entrada[..^1];
        if (_entrada is "" or "-") _entrada = "0";
    }

    private bool Aplicar()
    {
        var a = _acumulado ?? 0m; var b = ValorEntrada;
        decimal r;
        try
        {
            switch (_operador)
            {
                case '+': r = a + b; break;
                case '-': r = a - b; break;
                case '*': r = a * b; break;
                case '/':
                    if (b == 0m) { _erro = true; return false; }
                    r = a / b; break;
                default: r = b; break;
            }
        }
        catch (OverflowException) { _erro = true; return false; }
        r = Math.Round(r, 6, MidpointRounding.AwayFromZero);
        _acumulado = r;
        _entrada = r.ToString("0.######", CultureInfo.InvariantCulture);
        _novaEntrada = true;
        return true;
    }

    /// <summary>Valor do visor em centavos (arredondado; negativo ou erro vira zero).</summary>
    public long PrecoCentavos
    {
        get
        {
            if (_erro) return 0;
            var v = ValorEntrada;
            return v <= 0 ? 0 : (long)Math.Round(v * 100m, MidpointRounding.AwayFromZero);
        }
    }

    /// <summary>Juros em % sobre o valor atual (preço + acréscimos já lançados), como item da lista.</summary>
    public AcrescimoCusto AdicionarJuros(decimal percentual)
    {
        if (percentual <= 0 || percentual > 1000)
            throw new RegraDeNegocioException("Informe juros maiores que 0% e até 1000%.");
        var valor = (long)Math.Round(TotalCentavos * percentual / 100m, MidpointRounding.AwayFromZero);
        var item = new AcrescimoCusto($"Juros {percentual.ToString("0.##", PtBr)}%", valor);
        _acrescimos.Add(item);
        return item;
    }

    /// <summary>Valor avulso (frete, taxa...) em centavos.</summary>
    public AcrescimoCusto AdicionarValor(string descricao, long centavos)
    {
        if (centavos <= 0) throw new RegraDeNegocioException("Informe um valor maior que zero.");
        var item = new AcrescimoCusto(string.IsNullOrWhiteSpace(descricao) ? "Acréscimo" : descricao.Trim(), centavos);
        _acrescimos.Add(item);
        return item;
    }

    public void RemoverAcrescimo(int indice)
    {
        if (indice >= 0 && indice < _acrescimos.Count) _acrescimos.RemoveAt(indice);
    }

    public long TotalAcrescimosCentavos => _acrescimos.Sum(a => a.Centavos);

    public long TotalCentavos => PrecoCentavos + TotalAcrescimosCentavos;
}
