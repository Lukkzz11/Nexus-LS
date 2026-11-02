using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using NexusLS.Core.Services;
using NexusLS.Core.Sincronizacao;
using NexusLS.Core.Vitrine;

namespace NexusLS.App.Web;

/// <summary>
/// Recebe as mensagens da página (JSX) e responde. Cada mensagem é <c>{ id, metodo, args }</c>; a resposta é
/// <c>{ id, resultado }</c> ou <c>{ id, erro }</c> (texto em português para mostrar ao usuário).
/// </summary>
public class PonteCatalogo
{
    private readonly ServicoCatalogoWeb _servico;
    public PonteCatalogo(ServicoCatalogoWeb servico) => _servico = servico;

    public async Task<string> ResponderAsync(string json)
    {
        int id = 0;
        try
        {
            var msg = JsonNode.Parse(json)!.AsObject();
            id = msg["id"]?.GetValue<int>() ?? 0;
            var metodo = msg["metodo"]?.GetValue<string>() ?? "";
            var args = msg["args"] as JsonObject ?? new JsonObject();
            var resultado = await ExecutarAsync(metodo, args);
            return new JsonObject { ["id"] = id, ["resultado"] = resultado }.ToJsonString();
        }
        catch (Exception ex) when (ex is RegraDeNegocioException or FalhaDestinoException)
        {
            return Erro(id, ex.Message);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException or IOException)
        {
            return Erro(id, "Não foi possível concluir a operação: " + ex.Message);
        }
    }

    private static string Erro(int id, string texto) => new JsonObject { ["id"] = id, ["erro"] = texto }.ToJsonString();

    private async Task<JsonNode?> ExecutarAsync(string metodo, JsonObject args)
    {
        switch (metodo)
        {
            case "carregar":
            {
                var d = await _servico.CarregarAsync();
                return new JsonObject
                {
                    ["vitrine"] = d.VitrineJson is null ? null : JsonNode.Parse(d.VitrineJson),
                    ["empresa"] = d.Empresa,
                    ["produtos"] = d.Produtos
                };
            }
            case "salvarVitrine":
                _servico.SalvarLocal(Texto(args, "vitrine"));
                return new JsonObject();
            case "publicarVitrine":
            {
                var (json, quando) = await _servico.PublicarAsync(Texto(args, "vitrine"));
                return new JsonObject { ["vitrine"] = JsonNode.Parse(json), ["publicadoEm"] = quando.ToString("O") };
            }
            case "enviarImagem":
            {
                var (url, path) = _servico.SalvarImagem(args["dataUrl"]?.GetValue<string>() ?? "");
                return new JsonObject { ["url"] = url, ["path"] = path };
            }
            case "apagarImagem":
                _servico.ApagarImagem(args["path"]?.GetValue<string>() ?? "");
                return new JsonObject();
            default:
                throw new RegraDeNegocioException($"Operação desconhecida: {metodo}.");
        }
    }

    // A página manda o objeto da vitrine; o serviço guarda o texto JSON dele.
    private static string Texto(JsonObject args, string campo) =>
        args[campo]?.ToJsonString() ?? throw new RegraDeNegocioException("A vitrine não foi enviada pelo editor.");
}
