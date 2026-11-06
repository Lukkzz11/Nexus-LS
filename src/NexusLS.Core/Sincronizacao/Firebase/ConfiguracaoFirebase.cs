using System.Text.Json;
using System.Text.RegularExpressions;
using NexusLS.Core.Services;

namespace NexusLS.Core.Sincronizacao.Firebase;

/// <summary>
/// Protege um segredo (a senha) para guardar em disco. No Windows a implementação usa o DPAPI: só o MESMO usuário
/// do Windows, neste computador, consegue ler. Fica atrás de uma interface para o Core não depender do Windows
/// e para os testes usarem um protetor simples.
/// </summary>
public interface IProtetorSegredo
{
    string Proteger(string textoClaro);
    string? Desproteger(string textoProtegido);
}

/// <summary>
/// Dados do Firebase desta instalação: cada cliente tem o seu próprio projeto. Nenhuma chave de administrador
/// (service account) entra aqui nem no instalador: só a chave pública da API web, o e-mail e a senha do login
/// da licença, e quem manda no que esse login pode fazer são as regras de segurança do Firestore.
/// </summary>
public class ConfiguracaoFirebase
{
    private static readonly Regex PadraoProjeto = new("^[a-z][a-z0-9\\-]{4,29}$", RegexOptions.Compiled);

    /// <summary>O id do projeto no Firebase (ex.: loja-do-joao-1a2b3).</summary>
    public string ProjectId { get; set; } = "";

    /// <summary>A "Web API Key" do projeto. Não é segredo (aparece em qualquer site Firebase), mas só vale para este projeto.</summary>
    public string ApiKey { get; set; } = "";

    /// <summary>Bucket do Firebase Storage (ex.: meu-projeto.firebasestorage.app). Vazio = o padrão do projeto.</summary>
    public string Bucket { get; set; } = "";

    public string BucketEfetivo => string.IsNullOrWhiteSpace(Bucket) ? $"{ProjectId}.firebasestorage.app" : Bucket.Trim();

    /// <summary>E-mail do usuário do Firebase Authentication que representa esta licença.</summary>
    public string Email { get; set; } = "";

    /// <summary>A senha, já protegida (nunca em texto claro no arquivo).</summary>
    public string? SenhaProtegida { get; set; }

    public bool EstaCompleta =>
        !string.IsNullOrWhiteSpace(ProjectId) && !string.IsNullOrWhiteSpace(ApiKey)
        && !string.IsNullOrWhiteSpace(Email) && !string.IsNullOrWhiteSpace(SenhaProtegida);

    public void Validar()
    {
        NexusLS.Core.Importacao.Erp.GuardaErp.ExigirNaoSerOErp(ProjectId);
        if (!PadraoProjeto.IsMatch(ProjectId.Trim()))
            throw new RegraDeNegocioException("O id do projeto Firebase deve ter de 5 a 30 letras minúsculas, números ou hífens (ex.: minha-loja-1a2b3).");
        if (string.IsNullOrWhiteSpace(ApiKey))
            throw new RegraDeNegocioException("Informe a chave da API (Web API Key) do projeto.");
        if (!Email.Contains('@') || Email.Trim().Length < 5)
            throw new RegraDeNegocioException("Informe o e-mail do login da licença.");
    }
}

/// <summary>Lê e grava a <see cref="ConfiguracaoFirebase"/> num arquivo desta instalação (fora do banco de dados).</summary>
public class ConfiguracaoFirebaseStore
{
    private readonly string _caminho;
    private readonly IProtetorSegredo _protetor;

    public ConfiguracaoFirebaseStore(string caminho, IProtetorSegredo protetor)
    {
        _caminho = caminho;
        _protetor = protetor;
    }

    public static string CaminhoPadrao() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NexusLS", "firebase.json");

    /// <summary>A configuração salva. Arquivo ausente ou estragado vira uma configuração vazia.</summary>
    public ConfiguracaoFirebase Carregar()
    {
        try
        {
            if (!File.Exists(_caminho)) return new();
            return JsonSerializer.Deserialize<ConfiguracaoFirebase>(File.ReadAllText(_caminho)) ?? new();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new();
        }
    }

    /// <summary>
    /// Grava. A senha só é trocada se uma nova for informada (<paramref name="senhaNova"/> diferente de vazio);
    /// deixar em branco mantém a que já estava salva.
    /// </summary>
    public void Salvar(ConfiguracaoFirebase configuracao, string? senhaNova)
    {
        configuracao.ProjectId = configuracao.ProjectId.Trim();
        configuracao.ApiKey = configuracao.ApiKey.Trim();
        configuracao.Email = configuracao.Email.Trim();
        configuracao.Validar();

        if (!string.IsNullOrEmpty(senhaNova)) configuracao.SenhaProtegida = _protetor.Proteger(senhaNova);
        else configuracao.SenhaProtegida = Carregar().SenhaProtegida;

        Directory.CreateDirectory(Path.GetDirectoryName(_caminho)!);
        var tmp = _caminho + ".tmp"; // grava e troca: uma queda no meio não deixa o arquivo pela metade
        File.WriteAllText(tmp, JsonSerializer.Serialize(configuracao, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tmp, _caminho, overwrite: true);
    }

    /// <summary>A senha em texto claro, só na hora de usar. Nulo se não há senha salva ou se não foi possível ler.</summary>
    public string? ObterSenha()
    {
        var protegida = Carregar().SenhaProtegida;
        return string.IsNullOrEmpty(protegida) ? null : _protetor.Desproteger(protegida);
    }

    /// <summary>Apaga o login salvo (para trocar de projeto ou desligar a sincronização).</summary>
    public void Limpar()
    {
        if (File.Exists(_caminho)) File.Delete(_caminho);
    }
}
