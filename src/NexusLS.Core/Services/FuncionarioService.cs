using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using NexusLS.Core.Data;
using NexusLS.Core.Models;

namespace NexusLS.Core.Services;

public record DadosFuncionario(
    string Nome,
    PerfilFuncionario Perfil,
    Permissao PermissoesPersonalizadas = Permissao.Nenhuma,
    string? Cargo = null,
    string? Telefone = null);

/// <summary>Quem está usando o programa agora. Os serviços gravam o nome nas vendas, no caixa e nos cancelamentos.</summary>
public static class OperadorAtual
{
    public static string? Nome { get; set; }
}

/// <summary>
/// Funcionários, PIN e permissões. O PIN (4 a 8 números) nunca é guardado: só o hash PBKDF2 com sal. Depois de 5 erros
/// seguidos o funcionário fica 30 segundos sem poder tentar. O sistema nunca fica sem administrador ativo, e o primeiro
/// administrador recebe um código de recuperação (mostrado uma única vez) para redefinir o PIN se esquecer.
/// </summary>
public class FuncionarioService
{
    public const int MaxTentativas = 5;
    public static readonly TimeSpan TempoDeBloqueio = TimeSpan.FromSeconds(30);

    private const int Iteracoes = 100_000;
    private readonly IDbContextFactory<PdvDbContext> _fabrica;
    private readonly Func<DateTime> _agora;
    private readonly Dictionary<int, (int Falhas, DateTime? BloqueadoAte)> _tentativas = new();
    private readonly object _trava = new();

    public FuncionarioService(IDbContextFactory<PdvDbContext> fabrica, Func<DateTime>? agora = null)
    {
        _fabrica = fabrica;
        _agora = agora ?? (() => DateTime.Now);
    }

    /// <summary>Se existe algum funcionário cadastrado, o programa pede o PIN ao abrir. Sem nenhum, abre direto (como sempre foi).</summary>
    public async Task<bool> ExigeLoginAsync()
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        return await db.Funcionarios.AnyAsync();
    }

    /// <summary>Cria o primeiro administrador (só quando não há ninguém cadastrado). Devolve também o código de recuperação.</summary>
    public async Task<(Funcionario Funcionario, string CodigoRecuperacao)> CriarPrimeiroAdministradorAsync(string nome, string pin)
    {
        ValidarNome(nome);
        ValidarPin(pin);
        await using var db = await _fabrica.CreateDbContextAsync();
        if (await db.Funcionarios.AnyAsync())
            throw new RegraDeNegocioException("Já existem funcionários cadastrados.");

        var codigo = NovoCodigoDeRecuperacao();
        var f = new Funcionario
        {
            Nome = nome.Trim(),
            Perfil = PerfilFuncionario.Administrador,
            PinHash = Hash(pin),
            RecuperacaoHash = Hash(NormalizarCodigo(codigo)),
            Ativo = true,
            DataCadastro = _agora()
        };
        db.Funcionarios.Add(f);
        await db.SaveChangesAsync();
        return (f, codigo);
    }

    public async Task<Funcionario> CadastrarAsync(DadosFuncionario dados, string pin)
    {
        ValidarNome(dados.Nome);
        ValidarPin(pin);
        await using var db = await _fabrica.CreateDbContextAsync();
        await GarantirNomeLivreAsync(db, dados.Nome, null);

        var f = new Funcionario { Ativo = true, DataCadastro = _agora(), PinHash = Hash(pin) };
        Preencher(f, dados);
        db.Funcionarios.Add(f);
        AtividadeService.Registrar(db, TipoAtividade.FuncionarioCadastrado,
            $"Cadastrou o funcionário {f.Nome} ({f.PerfilTexto})", $"Funcionário {f.Nome}", agora: f.DataCadastro);
        await db.SaveChangesAsync();
        return f;
    }

    public async Task EditarAsync(int id, DadosFuncionario dados)
    {
        ValidarNome(dados.Nome);
        await using var db = await _fabrica.CreateDbContextAsync();
        var f = await ObterAsync(db, id);
        await GarantirNomeLivreAsync(db, dados.Nome, id);

        var deixaDeSerAdmin = f.Ativo && f.Perfil == PerfilFuncionario.Administrador && dados.Perfil != PerfilFuncionario.Administrador;
        if (deixaDeSerAdmin) await GarantirOutroAdministradorAsync(db, id);

        Preencher(f, dados);
        AtividadeService.Registrar(db, TipoAtividade.FuncionarioEditado,
            $"Editou o funcionário {f.Nome} (perfil {f.PerfilTexto})", $"Funcionário {f.Nome}", agora: _agora());
        await db.SaveChangesAsync();
    }

    public async Task DefinirPinAsync(int id, string novoPin)
    {
        ValidarPin(novoPin);
        await using var db = await _fabrica.CreateDbContextAsync();
        var f = await ObterAsync(db, id);
        f.PinHash = Hash(novoPin);
        AtividadeService.Registrar(db, TipoAtividade.PinRedefinido,
            $"Redefiniu o PIN de {f.Nome}", $"Funcionário {f.Nome}", agora: _agora());
        await db.SaveChangesAsync();
        lock (_trava) _tentativas.Remove(id);
    }

    public async Task DesativarAsync(int id)
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        var f = await ObterAsync(db, id);
        if (f.Ativo && f.Perfil == PerfilFuncionario.Administrador) await GarantirOutroAdministradorAsync(db, id);
        f.Ativo = false;
        AtividadeService.Registrar(db, TipoAtividade.FuncionarioDesativado,
            $"Desativou o funcionário {f.Nome}", $"Funcionário {f.Nome}", agora: _agora());
        await db.SaveChangesAsync();
    }

    public async Task ReativarAsync(int id)
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        var f = await ObterAsync(db, id);
        f.Ativo = true;
        AtividadeService.Registrar(db, TipoAtividade.FuncionarioReativado,
            $"Reativou o funcionário {f.Nome}", $"Funcionário {f.Nome}", agora: _agora());
        await db.SaveChangesAsync();
    }

    public async Task<List<Funcionario>> ListarAsync(bool incluirInativos = false)
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        var lista = await db.Funcionarios.AsNoTracking().Where(f => incluirInativos || f.Ativo).ToListAsync();
        return lista.OrderBy(f => f.Nome, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>Confere o PIN. Erra com mensagem clara e bloqueia por 30 segundos depois de 5 erros seguidos.</summary>
    public async Task<Funcionario> AutenticarAsync(int id, string pin)
    {
        var agora = _agora();
        lock (_trava)
        {
            if (_tentativas.TryGetValue(id, out var t) && t.BloqueadoAte is DateTime ate && ate > agora)
                throw new RegraDeNegocioException($"Muitas tentativas erradas. Aguarde {(int)Math.Ceiling((ate - agora).TotalSeconds)} segundo(s).");
        }

        await using var db = await _fabrica.CreateDbContextAsync();
        var f = await db.Funcionarios.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.Ativo)
            ?? throw new RegraDeNegocioException("Funcionário não encontrado ou desativado.");

        if (!Confere(pin ?? "", f.PinHash))
        {
            lock (_trava)
            {
                var falhas = (_tentativas.TryGetValue(id, out var t) ? t.Falhas : 0) + 1;
                _tentativas[id] = falhas >= MaxTentativas ? (0, agora + TempoDeBloqueio) : (falhas, null);
            }
            throw new RegraDeNegocioException("PIN incorreto.");
        }

        lock (_trava) _tentativas.Remove(id);
        return f;
    }

    /// <summary>Redefine o PIN de um administrador com o código de recuperação. O código usado deixa de valer; devolve o novo.</summary>
    public async Task<(Funcionario Funcionario, string NovoCodigo)> RedefinirPinComCodigoAsync(string codigo, string novoPin)
    {
        ValidarPin(novoPin);
        var normalizado = NormalizarCodigo(codigo ?? "");
        await using var db = await _fabrica.CreateDbContextAsync();
        var admins = await db.Funcionarios.Where(f => f.Ativo && f.Perfil == PerfilFuncionario.Administrador && f.RecuperacaoHash != null).ToListAsync();
        var dono = admins.FirstOrDefault(a => Confere(normalizado, a.RecuperacaoHash!))
            ?? throw new RegraDeNegocioException("Código de recuperação inválido.");

        var novo = NovoCodigoDeRecuperacao();
        dono.PinHash = Hash(novoPin);
        dono.RecuperacaoHash = Hash(NormalizarCodigo(novo));
        await db.SaveChangesAsync();
        lock (_trava) _tentativas.Remove(dono.Id);
        return (dono, novo);
    }

    /// <summary>Gera um código de recuperação novo para um administrador (o antigo deixa de valer).</summary>
    public async Task<string> NovoCodigoDeRecuperacaoAsync(int administradorId)
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        var f = await ObterAsync(db, administradorId);
        if (f.Perfil != PerfilFuncionario.Administrador) throw new RegraDeNegocioException("Só administradores têm código de recuperação.");
        var codigo = NovoCodigoDeRecuperacao();
        f.RecuperacaoHash = Hash(NormalizarCodigo(codigo));
        await db.SaveChangesAsync();
        return codigo;
    }

    // ---------- regras e hash ----------

    private static void ValidarNome(string nome)
    {
        if (string.IsNullOrWhiteSpace(nome)) throw new RegraDeNegocioException("Informe o nome do funcionário.");
        if (nome.Trim().Length > 80) throw new RegraDeNegocioException("O nome pode ter no máximo 80 caracteres.");
    }

    public static void ValidarPin(string pin)
    {
        if (string.IsNullOrEmpty(pin) || pin.Length is < 4 or > 8 || !pin.All(char.IsAsciiDigit))
            throw new RegraDeNegocioException("O PIN deve ter de 4 a 8 números.");
    }

    private static void Preencher(Funcionario f, DadosFuncionario d)
    {
        f.Nome = d.Nome.Trim();
        f.Perfil = d.Perfil;
        f.PermissoesPersonalizadas = d.Perfil == PerfilFuncionario.Personalizado ? d.PermissoesPersonalizadas & Permissoes.Todas : Permissao.Nenhuma;
        f.Cargo = string.IsNullOrWhiteSpace(d.Cargo) ? null : d.Cargo.Trim();
        f.Telefone = string.IsNullOrWhiteSpace(d.Telefone) ? null : d.Telefone.Trim();
    }

    private static async Task<Funcionario> ObterAsync(PdvDbContext db, int id) =>
        await db.Funcionarios.FirstOrDefaultAsync(f => f.Id == id) ?? throw new RegraDeNegocioException("Funcionário não encontrado.");

    private static async Task GarantirNomeLivreAsync(PdvDbContext db, string nome, int? idIgnorado)
    {
        var minusculo = nome.Trim().ToLower();
        if (await db.Funcionarios.AnyAsync(f => f.Ativo && f.Id != idIgnorado && f.Nome.ToLower() == minusculo))
            throw new RegraDeNegocioException("Já existe um funcionário ativo com esse nome.");
    }

    private static async Task GarantirOutroAdministradorAsync(PdvDbContext db, int id)
    {
        if (!await db.Funcionarios.AnyAsync(f => f.Id != id && f.Ativo && f.Perfil == PerfilFuncionario.Administrador))
            throw new RegraDeNegocioException("O sistema precisa de pelo menos um administrador ativo. Cadastre outro antes.");
    }

    private static string Hash(string segredo)
    {
        var sal = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(segredo, sal, Iteracoes, HashAlgorithmName.SHA256, 32);
        return Convert.ToBase64String(sal) + ":" + Convert.ToBase64String(hash);
    }

    private static bool Confere(string segredo, string guardado)
    {
        var partes = guardado.Split(':');
        if (partes.Length != 2) return false;
        try
        {
            var sal = Convert.FromBase64String(partes[0]);
            var esperado = Convert.FromBase64String(partes[1]);
            var calculado = Rfc2898DeriveBytes.Pbkdf2(segredo, sal, Iteracoes, HashAlgorithmName.SHA256, esperado.Length);
            return CryptographicOperations.FixedTimeEquals(calculado, esperado);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>Código de 12 letras e números sem os confusos (0/O, 1/I), em grupos de 4: ABCD-EFGH-JKLM.</summary>
    private static string NovoCodigoDeRecuperacao()
    {
        const string alfabeto = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var letras = new char[12];
        for (var i = 0; i < letras.Length; i++) letras[i] = alfabeto[RandomNumberGenerator.GetInt32(alfabeto.Length)];
        var s = new string(letras);
        return $"{s[..4]}-{s[4..8]}-{s[8..]}";
    }

    private static string NormalizarCodigo(string codigo) =>
        new string(codigo.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
}
