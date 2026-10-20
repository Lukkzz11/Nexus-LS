namespace NexusLS.Core.Models;

/// <summary>O que o funcionário pode abrir ou fazer. Os valores ficam gravados no banco: nunca reaproveitar nem mudar um número.</summary>
[Flags]
public enum Permissao
{
    Nenhuma = 0,
    Vender = 1,            // PDV
    Caixa = 2,             // Caixa, Fechamento (abrir, sangria, fechar)
    CancelarVendas = 4,    // cancelar uma venda já feita
    Estoque = 8,           // Estoque da loja, etiquetas, importar CSV
    Compras = 16,          // Compras e Fornecedores
    Clientes = 32,         // Clientes e dívidas a prazo
    Financeiro = 64,       // contas a pagar e a receber
    Relatorios = 128,      // Relatórios e Mais vendidos (mostram custo e lucro)
    Configuracoes = 256,   // dados da loja, impressora, backup, catálogo online
    Funcionarios = 512     // cadastrar funcionários e mudar permissões
}

/// <summary>Os números ficam gravados no banco: nunca reaproveitar nem mudar um número.</summary>
public enum PerfilFuncionario
{
    Administrador = 1,
    Gerente = 2,
    OperadorDeCaixa = 3,
    Vendedor = 4,
    Personalizado = 5
}

public static class Permissoes
{
    public const Permissao Todas =
        Permissao.Vender | Permissao.Caixa | Permissao.CancelarVendas | Permissao.Estoque | Permissao.Compras |
        Permissao.Clientes | Permissao.Financeiro | Permissao.Relatorios | Permissao.Configuracoes | Permissao.Funcionarios;

    /// <summary>O que cada perfil pronto pode. "Personalizado" usa as permissões marcadas à mão.</summary>
    public static Permissao DoPerfil(PerfilFuncionario perfil) => perfil switch
    {
        PerfilFuncionario.Administrador => Todas,
        PerfilFuncionario.Gerente => Todas & ~(Permissao.Configuracoes | Permissao.Funcionarios),
        PerfilFuncionario.OperadorDeCaixa => Permissao.Vender | Permissao.Caixa | Permissao.Clientes,
        PerfilFuncionario.Vendedor => Permissao.Vender | Permissao.Clientes,
        _ => Permissao.Nenhuma
    };

    public static string Nome(PerfilFuncionario perfil) => perfil switch
    {
        PerfilFuncionario.Administrador => "Administrador",
        PerfilFuncionario.Gerente => "Gerente",
        PerfilFuncionario.OperadorDeCaixa => "Operador de caixa",
        PerfilFuncionario.Vendedor => "Vendedor",
        _ => "Personalizado"
    };

    public static string Nome(Permissao p) => p switch
    {
        Permissao.Vender => "Vender (PDV)",
        Permissao.Caixa => "Caixa e fechamento",
        Permissao.CancelarVendas => "Cancelar vendas",
        Permissao.Estoque => "Estoque e etiquetas",
        Permissao.Compras => "Compras e fornecedores",
        Permissao.Clientes => "Clientes e dívidas",
        Permissao.Financeiro => "Financeiro",
        Permissao.Relatorios => "Relatórios",
        Permissao.Configuracoes => "Configurações e backup",
        Permissao.Funcionarios => "Funcionários",
        _ => ""
    };

    public static readonly IReadOnlyList<Permissao> Lista = new[]
    {
        Permissao.Vender, Permissao.Caixa, Permissao.CancelarVendas, Permissao.Estoque, Permissao.Compras,
        Permissao.Clientes, Permissao.Financeiro, Permissao.Relatorios, Permissao.Configuracoes, Permissao.Funcionarios
    };
}

public class Funcionario
{
    public int Id { get; set; }
    public string Nome { get; set; } = string.Empty;
    public string? Cargo { get; set; }
    public string? Telefone { get; set; }
    public PerfilFuncionario Perfil { get; set; }

    /// <summary>Só vale para o perfil Personalizado; nos outros vem do perfil.</summary>
    public Permissao PermissoesPersonalizadas { get; set; }

    /// <summary>Senha numérica (PIN) guardada só como hash com sal. Nunca o PIN em si.</summary>
    public string PinHash { get; set; } = string.Empty;

    /// <summary>Código de recuperação do administrador (hash). Nulo nos demais.</summary>
    public string? RecuperacaoHash { get; set; }

    public bool Ativo { get; set; } = true;
    public DateTime DataCadastro { get; set; }

    public Permissao PermissoesEfetivas => Perfil == PerfilFuncionario.Personalizado ? PermissoesPersonalizadas : Permissoes.DoPerfil(Perfil);

    public bool Pode(Permissao p) => (PermissoesEfetivas & p) == p;
}
