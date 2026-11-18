using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NexusLS.Core.Atualizacao;
using NexusLS.Core.Data;
using NexusLS.Core.Diagnostico;
using NexusLS.Core.Services;

namespace NexusLS.Movel;

/// <summary>
/// Servidor local do Nexus Movel. Fica no computador da loja, escuta o Wi-Fi e serve a tela
/// do celular. Usa Kestrel (nao HttpListener) porque Kestrel abre a porta sem pedir administrador.
/// Nada e enviado para a internet: so le o banco local e responde na rede da loja.
/// </summary>
public static class ServidorMovel
{
    public const int PortaPadrao = 5123;

    /// <summary>Monta o servidor sem iniciar (usado pelos testes e pelo programa).</summary>
    public static WebApplication Criar(
        IDbContextFactory<PdvDbContext> fabrica,
        string pin,
        int porta = PortaPadrao,
        string? url = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = Array.Empty<string>(),
            ApplicationName = typeof(ServidorMovel).Assembly.GetName().Name,
            ContentRootPath = AppContext.BaseDirectory
        });

        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(o => o.AddServerHeader = false);
        builder.WebHost.UseUrls(url ?? $"http://0.0.0.0:{porta}");

        builder.Services.AddSingleton(fabrica);
        builder.Services.AddSingleton(new ProdutoService(fabrica));
        builder.Services.AddSingleton<ConsultaDeProduto>();
        builder.Services.AddSingleton(new AutenticacaoMovel(pin));

        var app = builder.Build();

        app.Use(async (contexto, proximo) =>
        {
            try
            {
                await proximo();
            }
            catch (OperationCanceledException) when (contexto.RequestAborted.IsCancellationRequested)
            {
                // O celular fechou a tela no meio: nao e erro.
            }
            catch (Exception ex)
            {
                RegistroDeLog.Erro("Nexus Movel: falha ao atender " + contexto.Request.Path, ex);
                if (!contexto.Response.HasStarted)
                {
                    contexto.Response.StatusCode = StatusCodes.Status500InternalServerError;
                    contexto.Response.ContentType = "application/json; charset=utf-8";
                    await contexto.Response.WriteAsync("{\"erro\":\"Falha ao consultar o banco da loja.\"}");
                }
            }
        });

        // Tela do celular (HTML, CSS, JS e icones) embutida no executavel.
        app.Use(async (contexto, proximo) =>
        {
            if (contexto.Request.Path.StartsWithSegments("/api"))
            {
                await proximo();
                return;
            }

            if (!HttpMethods.IsGet(contexto.Request.Method) && !HttpMethods.IsHead(contexto.Request.Method))
            {
                await proximo();
                return;
            }

            if (!RecursosWeb.TentarObter(contexto.Request.Path.Value ?? "/", out var arquivo))
            {
                await proximo();
                return;
            }

            contexto.Response.ContentType = arquivo.Tipo;
            contexto.Response.Headers.CacheControl = contexto.Request.Path == "/"
                ? "no-store"
                : "public, max-age=3600";

            contexto.Response.Headers["X-Content-Type-Options"] = "nosniff";
            contexto.Response.Headers["Referrer-Policy"] = "no-referrer";
            contexto.Response.Headers["Content-Security-Policy"] =
                "default-src 'self'; img-src 'self' data: blob:; media-src 'self' blob:; " +
                "script-src 'self'; style-src 'self'; base-uri 'none'; form-action 'none'";

            await contexto.Response.Body.WriteAsync(arquivo.Conteudo, contexto.RequestAborted);
        });

        // O PIN vale para tudo que e dado (/api). A tela em si nao tem dado nenhum.
        app.Use(async (contexto, proximo) =>
        {
            if (!contexto.Request.Path.StartsWithSegments("/api"))
            {
                await proximo();
                return;
            }

            var autenticacao = contexto.RequestServices.GetRequiredService<AutenticacaoMovel>();
            var origem = contexto.Connection.RemoteIpAddress?.ToString();
            var informado = contexto.Request.Headers["X-Pin"].FirstOrDefault()
                ?? contexto.Request.Query["pin"].FirstOrDefault();

            if (!autenticacao.Conferir(informado, origem))
            {
                contexto.Response.Headers.CacheControl = "no-store";
                contexto.Response.StatusCode = autenticacao.Bloqueado(origem)
                    ? StatusCodes.Status429TooManyRequests
                    : StatusCodes.Status401Unauthorized;

                contexto.Response.ContentType = "application/json; charset=utf-8";
                await contexto.Response.WriteAsync(
                    autenticacao.Bloqueado(origem)
                        ? "{\"erro\":\"Muitas tentativas. Espere um minuto.\"}"
                        : "{\"erro\":\"PIN incorreto.\"}");
                return;
            }

            contexto.Response.Headers.CacheControl = "no-store";
            await proximo();
        });

        // Somente leitura: o celular nunca cria nem altera nada no banco da loja.
        app.MapGet("/api/saude", async (IDbContextFactory<PdvDbContext> fabrica) =>
        {
            try
            {
                await using var db = await fabrica.CreateDbContextAsync();
                var config = await db.ConfiguracoesLoja.AsNoTracking().FirstOrDefaultAsync(c => c.Id == 1);

                return Results.Ok(new
                {
                    ok = true,
                    loja = string.IsNullOrWhiteSpace(config?.NomeLoja) ? "Nexus LS" : config!.NomeLoja,
                    versao = VersaoPrograma.Atual().ToString(),
                    computador = Environment.MachineName
                });
            }
            catch (Exception ex)
            {
                RegistroDeLog.Erro("Nexus Movel: falha ao ler a configuracao da loja.", ex);
                return Results.Ok(new
                {
                    ok = false,
                    loja = "Nexus",
                    versao = VersaoPrograma.Atual().ToString(),
                    erro = "O banco da loja nao respondeu."
                });
            }
        });

        app.MapGet("/api/produto/{codigo}", async (string codigo, ConsultaDeProduto consulta, CancellationToken ct) =>
            Results.Ok(await consulta.ConsultarAsync(codigo, ct)));

        app.MapGet("/api/busca", async (string? texto, ConsultaDeProduto consulta, CancellationToken ct) =>
            Results.Ok(new { itens = await consulta.BuscarAsync(texto, ct) }));

        app.MapGet("/api/foto/{id:int}", async (int id, ProdutoService produtos) =>
        {
            var produto = await produtos.ObterAsync(id);
            if (string.IsNullOrWhiteSpace(produto?.FotoArquivo))
            {
                return Results.NotFound();
            }

            // GetFileName impede que um nome estranho no banco saia da pasta de fotos.
            var arquivo = Path.Combine(FotoProdutoService.PastaPadrao(), Path.GetFileName(produto.FotoArquivo));
            return File.Exists(arquivo)
                ? Results.File(arquivo, "image/jpeg")
                : Results.NotFound();
        });

        return app;
    }

    /// <summary>Monta e ja comeca a escutar; devolve o endereco real (util com porta 0 nos testes).</summary>
    public static async Task<ServidorEmExecucao> IniciarAsync(
        IDbContextFactory<PdvDbContext> fabrica,
        string pin,
        int porta = PortaPadrao,
        string? url = null,
        CancellationToken ct = default)
    {
        var app = Criar(fabrica, pin, porta, url);
        await app.StartAsync(ct);

        var servidor = app.Services.GetRequiredService<IServer>();
        var endereco = servidor.Features.Get<IServerAddressesFeature>()?.Addresses.FirstOrDefault()
            ?? url
            ?? $"http://0.0.0.0:{porta}";

        RegistroDeLog.Info($"Nexus Movel escutando em {endereco}");

        return new ServidorEmExecucao(app, endereco);
    }
}

/// <summary>Servidor no ar; descarta para encerrar (os testes usam isso).</summary>
public sealed class ServidorEmExecucao : IAsyncDisposable
{
    private readonly WebApplication _app;

    internal ServidorEmExecucao(WebApplication app, string endereco)
    {
        _app = app;
        Endereco = endereco.Replace("0.0.0.0", "127.0.0.1");
    }

    public string Endereco { get; }

    public Task AguardarEncerramentoAsync() => _app.WaitForShutdownAsync();

    public async ValueTask DisposeAsync()
    {
        try
        {
            await _app.StopAsync(TimeSpan.FromSeconds(3));
        }
        catch (Exception ex)
        {
            RegistroDeLog.Aviso($"Nexus Movel: falha ao encerrar o servidor: {ex.Message}");
        }

        await _app.DisposeAsync();
    }
}
