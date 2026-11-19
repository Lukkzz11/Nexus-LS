using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using NexusLS.Core.Sincronizacao.Firebase;

namespace NexusLS.Tests.Infra;

/// <summary>Firestore de mentira, na memória. NENHUM teste fala com o Firestore real. Guarda cada documento gravado e cada pedido.</summary>
internal sealed class FirestoreNuvemFalso : HttpMessageHandler
{
    private const string Marca = "/documents/";
    public Dictionary<string, JsonObject> Docs { get; } = new();
    public List<string> Pedidos { get; } = new();
    public List<string> CaminhosGravados { get; } = new();
    public List<int> TamanhosDeCommit { get; } = new();
    public bool SemInternet { get; set; }
    public HttpStatusCode? ForcarStatus { get; set; }
    /// <summary>Número (a partir de 1) do pedido de commit que deve falhar com 503; 0 = nenhum.</summary>
    public int FalharNoCommit { get; set; }
    private int _commits;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (SemInternet) throw new HttpRequestException("sem internet (teste)");
        var url = request.RequestUri!.ToString();
        var metodo = request.Method.Method;
        Pedidos.Add($"{metodo} {url}");
        var corpo = request.Content is null ? "" : request.Content.ReadAsStringAsync(ct).GetAwaiter().GetResult();
        if (ForcarStatus is HttpStatusCode forcado) return Task.FromResult(Resp(forcado, "{\"error\":{\"message\":\"forçado\"}}"));

        var depois = url[(url.IndexOf("/documents", StringComparison.Ordinal) + "/documents".Length)..].TrimStart('/');
        var semConsulta = depois.Split('?')[0];

        if (metodo == "POST" && semConsulta == ":commit")
        {
            _commits++;
            var escritas = JsonNode.Parse(corpo)!["writes"]!.AsArray();
            TamanhosDeCommit.Add(escritas.Count);
            if (FalharNoCommit == _commits) return Task.FromResult(Resp(HttpStatusCode.ServiceUnavailable, "{}"));
            foreach (var w in escritas)
            {
                var nome = w!["update"]!["name"]!.GetValue<string>();
                var caminho = nome[(nome.IndexOf(Marca, StringComparison.Ordinal) + Marca.Length)..];
                CaminhosGravados.Add(caminho);
                Docs[caminho] = (JsonObject)w["update"]!["fields"]!.DeepClone();   // sem máscara: substitui o documento inteiro
            }
            return Task.FromResult(Resp(HttpStatusCode.OK, "{\"writeResults\":[]}"));
        }
        if (metodo == "POST" && semConsulta == ":batchGet")
        {
            var itens = new JsonArray();
            foreach (var n in JsonNode.Parse(corpo)!["documents"]!.AsArray())
            {
                var nome = n!.GetValue<string>();
                var caminho = nome[(nome.IndexOf(Marca, StringComparison.Ordinal) + Marca.Length)..];
                itens.Add(Docs.TryGetValue(caminho, out var f)
                    ? new JsonObject { ["found"] = Documento(caminho, f) }
                    : new JsonObject { ["missing"] = nome });
            }
            return Task.FromResult(Resp(HttpStatusCode.OK, itens.ToJsonString()));
        }
        if (metodo == "POST" && semConsulta.EndsWith(":runQuery", StringComparison.Ordinal))
        {
            var pai = semConsulta[..^":runQuery".Length];
            var filtros = JsonNode.Parse(corpo)!["structuredQuery"]!["where"]!["compositeFilter"]!["filters"]!.AsArray();
            string De() => filtros[0]!["fieldFilter"]!["value"]!["stringValue"]!.GetValue<string>();
            string Ate() => filtros[1]!["fieldFilter"]!["value"]!["stringValue"]!.GetValue<string>();
            var itens = new JsonArray();
            foreach (var (caminho, f) in Docs.Where(d => d.Key.StartsWith(pai + "/dias/", StringComparison.Ordinal)).OrderBy(d => d.Key, StringComparer.Ordinal))
            {
                var dia = f["dia"]!["stringValue"]!.GetValue<string>();
                if (string.CompareOrdinal(dia, De()) >= 0 && string.CompareOrdinal(dia, Ate()) <= 0)
                    itens.Add(new JsonObject { ["document"] = Documento(caminho, f) });
            }
            if (itens.Count == 0) itens.Add(new JsonObject { ["readTime"] = "2026-01-01T00:00:00Z" });
            return Task.FromResult(Resp(HttpStatusCode.OK, itens.ToJsonString()));
        }
        if (metodo == "GET")
        {
            if (semConsulta.Contains("/lojas", StringComparison.Ordinal) && semConsulta.Split('/').Length == 3 && url.Contains("pageSize"))
            {
                var filhos = Docs.Where(d => d.Key.StartsWith(semConsulta + "/", StringComparison.Ordinal) && !d.Key[(semConsulta.Length + 1)..].Contains('/'));
                var docs = new JsonArray(filhos.Select(d => (JsonNode)Documento(d.Key, d.Value)).ToArray());
                return Task.FromResult(Resp(HttpStatusCode.OK, new JsonObject { ["documents"] = docs }.ToJsonString()));
            }
            return Task.FromResult(Docs.TryGetValue(semConsulta, out var um)
                ? Resp(HttpStatusCode.OK, Documento(semConsulta, um).ToJsonString())
                : Resp(HttpStatusCode.NotFound, "{\"error\":{\"message\":\"Document not found\"}}"));
        }
        throw new InvalidOperationException("Pedido não previsto no teste: " + metodo + " " + url);
    }

    private static JsonObject Documento(string caminho, JsonObject campos) => new()
    {
        ["name"] = "projects/projeto-teste/databases/(default)/documents/" + caminho, ["fields"] = campos.DeepClone()
    };

    private static HttpResponseMessage Resp(HttpStatusCode s, string corpo) => new(s) { Content = new StringContent(corpo, Encoding.UTF8, "application/json") };
}

internal sealed class TokenDeTeste : IProvedorToken
{
    public Task<string> ObterAsync(CancellationToken ct = default) => Task.FromResult("token-de-teste");
    public void Invalidar() { }
}
