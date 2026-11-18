using System.IO;
using NexusLS.Core.Data;
using NexusLS.App.ViewModels;
using NexusLS.Core.Models;
using NexusLS.Core.Seguranca;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.App.Tests;

/// <summary>Página "Estoque e produtos": aviso de estoque baixo (gravar a preferência e listar o que está abaixo do mínimo).</summary>
public sealed class EstoqueConfigTests : IDisposable
{
    private readonly BancoDeTeste _banco = new();
    private readonly string _arquivo = Path.Combine(Path.GetTempPath(), "nexus_estoquecfg_" + Guid.NewGuid().ToString("N") + ".json");

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
            vm.UsarEstoque(new EstoqueService(_banco.Fabrica));
        });
        return vm!;
    }

    [Fact]
    public void O_aviso_vem_ligado_por_padrao_e_a_pagina_aparece()
    {
        var vm = Criar();
        Assert.True(vm.TemEstoqueConfig);
        Assert.True(vm.AvisarEstoqueBaixo);
    }

    [Fact]
    public void Desligar_o_aviso_grava_e_nao_apaga_as_outras_regras()
    {
        var store = new ConfiguracaoSegurancaStore(_arquivo);
        var antes = store.Carregar();
        antes.MinutosInatividade = 7;
        store.Salvar(antes);

        var vm = Criar();
        vm.AvisarEstoqueBaixo = false;
        vm.SalvarEstoqueCommand.Execute(null);

        Assert.False(vm.MensagemEstoqueEhErro);
        Assert.Contains("desligado", vm.MensagemEstoque);
        var lida = new ConfiguracaoSegurancaStore(_arquivo).Carregar();
        Assert.False(lida.AvisarEstoqueBaixoAoAbrir);
        Assert.Equal(7, lida.MinutosInatividade);
    }

    [Fact]
    public async Task Lista_so_os_produtos_abaixo_do_minimo()
    {
        using (var db = _banco.Fabrica.CreateDbContext())
        {
            db.Produtos.Add(new Produto { Nome = "Camiseta baixa", PrecoVendaCentavos = 1000, EstoqueAtual = 1, EstoqueMinimo = 5, Ativo = true });
            db.Produtos.Add(new Produto { Nome = "Calça cheia", PrecoVendaCentavos = 1000, EstoqueAtual = 20, EstoqueMinimo = 5, Ativo = true });
            await db.SaveChangesAsync();
        }

        var vm = Criar();
        await vm.AtualizarEstoqueBaixoCommand.ExecuteAsync(null);

        Assert.Single(vm.ListaEstoqueBaixo);
        Assert.Contains("Camiseta baixa", vm.ListaEstoqueBaixo[0]);
        Assert.Contains("1 produto(s)", vm.ResumoEstoqueBaixo);
    }

    [Fact]
    public async Task Sem_nada_abaixo_do_minimo_diz_isso()
    {
        var vm = Criar();
        await vm.AtualizarEstoqueBaixoCommand.ExecuteAsync(null);
        Assert.Empty(vm.ListaEstoqueBaixo);
        Assert.Equal("Nenhum produto abaixo do mínimo.", vm.ResumoEstoqueBaixo);
    }
}

public sealed class NotificacoesConfigTests : IDisposable
{
    private readonly BancoDeTeste _banco = new();
    private readonly string _arquivo = Path.Combine(Path.GetTempPath(), "nexus_notif_" + Guid.NewGuid().ToString("N") + ".json");

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
            vm.UsarNotificacoes();
        });
        return vm!;
    }

    [Fact]
    public void Salvar_grava_os_dias_e_mantem_o_aviso_de_estoque()
    {
        var store = new ConfiguracaoSegurancaStore(_arquivo);
        var c = store.Carregar(); c.AvisarEstoqueBaixoAoAbrir = false; store.Salvar(c);

        var vm = Criar();
        vm.DiasContasTexto = "7"; vm.AvisarBackupAtrasado = false; vm.DiasBackupTexto = "10";
        vm.SalvarNotificacoesCommand.Execute(null);

        Assert.False(vm.MensagemNotificacoesEhErro);
        var lida = new ConfiguracaoSegurancaStore(_arquivo).Carregar();
        Assert.Equal(7, lida.DiasAvisoContas);
        Assert.False(lida.AvisarBackupAtrasado);
        Assert.Equal(10, lida.DiasBackupAtrasado);
        Assert.False(lida.AvisarEstoqueBaixoAoAbrir);
    }

    [Fact]
    public void Valor_invalido_mostra_erro_e_nao_grava()
    {
        var vm = Criar();
        vm.DiasBackupTexto = "0";
        vm.SalvarNotificacoesCommand.Execute(null);
        Assert.True(vm.MensagemNotificacoesEhErro);
        Assert.Equal(3, new ConfiguracaoSegurancaStore(_arquivo).Carregar().DiasBackupAtrasado);

        vm.DiasBackupTexto = "abc";
        vm.SalvarNotificacoesCommand.Execute(null);
        Assert.True(vm.MensagemNotificacoesEhErro);
    }
}

public sealed class EtiquetasConfigTests : IDisposable
{
    private readonly BancoDeTeste _banco = new();
    private readonly string _arquivo = Path.Combine(Path.GetTempPath(), "nexus_etq_" + Guid.NewGuid().ToString("N") + ".json");
    private readonly string _modelos = Path.Combine(Path.GetTempPath(), "nexus_modelos_" + Guid.NewGuid().ToString("N") + ".json");

    public void Dispose()
    {
        _banco.Dispose();
        foreach (var f in new[] { _arquivo, _modelos }) { try { File.Delete(f); } catch (IOException) { } }
    }

    private ConfiguracoesViewModel Criar()
    {
        ConfiguracoesViewModel? vm = null;
        Capturador.Rodar(() =>
        {
            var config = new ConfiguracaoService(_banco.Fabrica);
            vm = new ConfiguracoesViewModel(config, new CupomService(_banco.Fabrica, config, new ImpressoraDeMentira()));
            vm.UsarSeguranca(new ConfiguracaoSegurancaStore(_arquivo));
            vm.UsarEtiquetas(new NexusLS.Core.Etiquetas.ModeloEtiquetaStore(_modelos));
        });
        return vm!;
    }

    [Fact]
    public void Sem_escolha_salva_a_pagina_mostra_a_bobina_50x30()
    {
        var vm = Criar();
        Assert.Equal(NexusLS.Core.Etiquetas.FormatoEtiqueta.Bobina50x30.Nome, vm.FormatoEtiquetaPadrao);
        Assert.Contains(NexusLS.Core.Etiquetas.FormatoEtiqueta.FolhaA4Tres_por_Sete.Nome, vm.FormatosEtiqueta);
    }

    [Fact]
    public void Salvar_grava_formato_e_preco_e_a_tela_de_etiquetas_abre_assim()
    {
        var vm = Criar();
        vm.FormatoEtiquetaPadrao = NexusLS.Core.Etiquetas.FormatoEtiqueta.FolhaA4Tres_por_Sete.Nome;
        vm.EtiquetaPrecoAtacado = true;
        vm.SalvarEtiquetasCommand.Execute(null);

        var lida = new ConfiguracaoSegurancaStore(_arquivo).Carregar();
        Assert.Equal(NexusLS.Core.Etiquetas.FormatoEtiqueta.FolhaA4Tres_por_Sete.Nome, lida.EtiquetaFormatoPadrao);
        Assert.True(lida.EtiquetaPrecoAtacadoPadrao);

        EtiquetasViewModel? tela = null;
        Capturador.Rodar(() =>
        {
            tela = new EtiquetasViewModel(new ProdutoService(_banco.Fabrica));
            tela.AplicarPadroes(lida.EtiquetaFormatoPadrao, lida.EtiquetaPrecoAtacadoPadrao);
        });
        Assert.Equal(NexusLS.Core.Etiquetas.FormatoEtiqueta.FolhaA4Tres_por_Sete, tela!.FormatoSelecionado);
        Assert.True(tela.PrecoAtacado);
    }

    [Fact]
    public void Formato_que_nao_existe_mais_e_ignorado()
    {
        EtiquetasViewModel? tela = null;
        Capturador.Rodar(() =>
        {
            tela = new EtiquetasViewModel(new ProdutoService(_banco.Fabrica));
            tela.AplicarPadroes("Modelo apagado", false);
        });
        Assert.Equal(NexusLS.Core.Etiquetas.FormatoEtiqueta.Bobina50x30, tela!.FormatoSelecionado);
    }
}
