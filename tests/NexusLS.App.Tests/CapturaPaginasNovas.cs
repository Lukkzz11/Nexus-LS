using System.IO;
using NexusLS.App.ViewModels;
using NexusLS.Core.Seguranca;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.App.Tests;

/// <summary>Só gera os PNGs quando NEXUSLS_PRINTS está definido (conferência visual das páginas novas de Configurações).</summary>
[Collection("Sessao")]
public sealed class CapturaPaginasNovas : IDisposable
{
    private readonly BancoDeTeste _banco = new();
    private readonly string _arquivo = Path.Combine(Path.GetTempPath(), "nexus_capnovas_" + Guid.NewGuid().ToString("N") + ".json");

    public void Dispose()
    {
        NexusLS.App.Seguranca.Alcada.Config = () => new();
        _banco.Dispose();
        try { File.Delete(_arquivo); } catch (IOException) { }
    }

    [Fact]
    public async Task Gera_as_paginas_novas()
    {
        var saida = Environment.GetEnvironmentVariable("NEXUSLS_PRINTS");
        if (string.IsNullOrWhiteSpace(saida)) return;
        Directory.CreateDirectory(saida);

        var config = new ConfiguracaoService(_banco.Fabrica);
        await config.SalvarAsync(new DadosLoja("Casa Nexus", "11.222.333/0001-81", "Rua das Flores, 123", "(11) 3333-4444", "Volte sempre."));
        ConfiguracoesViewModel? vm = null;
        Capturador.Rodar(() =>
        {
            vm = new ConfiguracoesViewModel(config, new CupomService(_banco.Fabrica, config, new ImpressoraDeMentira()));
            vm.UsarSeguranca(new ConfiguracaoSegurancaStore(_arquivo));
            vm.UsarNotificacoes(); vm.UsarComunicacao(); vm.UsarFiscal(); vm.UsarResumoCelular(new InicioService(_banco.Fabrica));
            vm.UsarIntegracoes(new PacoteContador(_banco.Fabrica), () => new[] { ("Google Drive", "Conectado"), ("Firebase", "Não configurado") });
        });
        for (var i = 0; i < 300 && vm!.NomeLoja != "Casa Nexus"; i++) await Task.Delay(10);
        vm!.AtualizarChecklistFiscal();

        foreach (var chave in new[] { "fiscal", "integracoes", "notificacoes", "comunicacao" })
            Capturador.SalvarConfiguracoesPagina(vm, chave, Path.Combine(saida, $"cfg-{chave}.png"), 1400, 1000);
    }
}
