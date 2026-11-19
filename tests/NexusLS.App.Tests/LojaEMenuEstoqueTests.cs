using System.IO;
using System.Text.RegularExpressions;
using NexusLS.App.ViewModels;
using NexusLS.Core.Seguranca;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.App.Tests;

/// <summary>Configurações > Loja (nome desta loja) e o menu Estoque reduzido a três telas.</summary>
public sealed class LojaEMenuEstoqueTests : IDisposable
{
    private readonly BancoDeTeste _banco = new();
    private readonly string _arquivo = Path.Combine(Path.GetTempPath(), "nexus_lojamenu_" + Guid.NewGuid().ToString("N") + ".json");

    public void Dispose()
    {
        _banco.Dispose();
        try { File.Delete(_arquivo); } catch (IOException) { }
    }

    private ConfiguracoesViewModel Criar()
    {
        ConfiguracoesViewModel? vm = null;
        Capturador.Rodar(() =>
        {
            var config = new ConfiguracaoService(_banco.Fabrica);
            vm = new ConfiguracoesViewModel(config, new CupomService(_banco.Fabrica, config, new ImpressoraDeMentira()));
            vm.UsarSeguranca(new ConfiguracaoSegurancaStore(_arquivo));
        });
        return vm!;
    }

    [Fact]
    public void A_pagina_mostra_o_nome_padrao_e_o_id()
    {
        var vm = Criar();
        Assert.Equal("Loja 1", vm.LojaNomeTexto);
        Assert.False(string.IsNullOrWhiteSpace(vm.LojaIdTexto));
    }

    [Fact]
    public void Salvar_o_nome_grava_sem_mexer_no_id()
    {
        var vm = Criar();
        var id = vm.LojaIdTexto;
        vm.LojaNomeTexto = "Loja 2";
        vm.SalvarLojaIdCommand.Execute(null);
        Assert.False(vm.MensagemLojaIdEhErro);
        var lida = new ConfiguracaoSegurancaStore(_arquivo).Carregar();
        Assert.Equal("Loja 2", lida.LojaNome);
        Assert.Equal(id, lida.LojaId);
    }

    [Fact]
    public void Nome_vazio_mostra_erro_e_nao_grava()
    {
        var vm = Criar();
        vm.LojaNomeTexto = "  ";
        vm.SalvarLojaIdCommand.Execute(null);
        Assert.True(vm.MensagemLojaIdEhErro);
        Assert.Equal("Loja 1", new ConfiguracaoSegurancaStore(_arquivo).Carregar().LojaNome);
    }

    [Fact]
    public void O_menu_Estoque_tem_so_tres_telas_e_o_resto_continua_acessivel()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "NexusLS.slnx"))) dir = dir.Parent;
        var xaml = File.ReadAllText(Path.Combine(dir!.FullName, "src", "NexusLS.App", "MainWindow.xaml"));
        var ini = xaml.IndexOf("Text=\"ESTOQUE\"", StringComparison.Ordinal);
        var fim = xaml.IndexOf("Text=\"GESTÃO\"", StringComparison.Ordinal);
        Assert.True(ini > 0 && fim > ini);
        var secao = xaml[ini..fim];
        var tags = Regex.Matches(secao, "<Button Tag=\"(\\w+)\"").Select(m => m.Groups[1].Value).ToArray();
        Assert.Equal(new[] { "ResumoEstoque", "EstoqueDaLoja", "Fabrica" }, tags);
        Assert.Contains("AutomationProperties.Name=\"Resumo\"", secao);
        Assert.Contains("AutomationProperties.Name=\"Estoque da loja\"", secao);
        Assert.Contains("AutomationProperties.Name=\"Estoque fábrica\"", secao);
        // Compras, Fornecedores e Calculadora de preços saíram do balcão e vivem no menu da Gestão
        var gestao = File.ReadAllText(Path.Combine(dir!.FullName, "src", "NexusLS.App", "Views", "GestaoWindow.xaml"));
        foreach (var tag in new[] { "CalculadoraPrecos", "Compras", "Fornecedores" })
        {
            Assert.DoesNotContain($"<Button Tag=\"{tag}\"", xaml);
            Assert.Contains($"<Button Tag=\"{tag}\"", gestao);
        }
    }
}
