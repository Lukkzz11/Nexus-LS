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

public sealed class IntegracoesConfigTests : IDisposable
{
    private readonly BancoDeTeste _banco = new();
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "nexus_integ_" + Guid.NewGuid().ToString("N"));
    public void Dispose() { _banco.Dispose(); try { Directory.Delete(_pasta, true); } catch (IOException) { } }

    private ConfiguracoesViewModel Criar(Func<string?> pasta)
    {
        ConfiguracoesViewModel? vm = null;
        Capturador.Rodar(() =>
        {
            var config = new ConfiguracaoService(_banco.Fabrica);
            vm = new ConfiguracoesViewModel(config, new CupomService(_banco.Fabrica, config, new ImpressoraDeMentira()));
            vm.EscolherPastaContador = pasta;
            vm.UsarIntegracoes(new PacoteContador(_banco.Fabrica), () => new[] { ("Google", "Conectado."), ("ERP", "Ainda não configurado.") });
        });
        return vm!;
    }

    [Fact]
    public void Lista_a_situacao_de_cada_servico_e_sugere_o_mes_passado()
    {
        var vm = Criar(() => null);
        Assert.Equal(2, vm.Integracoes.Count);
        Assert.Equal("Conectado.", vm.Integracoes[0].Situacao);
        Assert.Equal(new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddMonths(-1), vm.MesContador!.Inicio);
    }

    [Fact]
    public async Task Exportar_grava_os_arquivos_na_pasta_escolhida()
    {
        var vm = Criar(() => _pasta);
        await vm.ExportarContadorCommand.ExecuteAsync(null);
        Assert.False(vm.MensagemIntegracoesEhErro);
        Assert.Contains("3 arquivos", vm.MensagemIntegracoes);
        Assert.Equal(3, Directory.GetFiles(_pasta, "*.csv").Length);
    }

    [Fact]
    public async Task Cancelar_a_escolha_da_pasta_nao_faz_nada()
    {
        var vm = Criar(() => null);
        await vm.ExportarContadorCommand.ExecuteAsync(null);
        Assert.Equal("", vm.MensagemIntegracoes);
    }
}

[Collection("Sessao")]
public sealed class ComunicacaoConfigTests : IDisposable
{
    private readonly BancoDeTeste _banco = new();
    private readonly string _arquivo = Path.Combine(Path.GetTempPath(), "nexus_com_" + Guid.NewGuid().ToString("N") + ".json");

    public void Dispose()
    {
        NexusLS.App.Seguranca.Alcada.Config = () => new();
        _banco.Dispose();
        try { File.Delete(_arquivo); } catch (IOException) { }
    }

    private ConfiguracoesViewModel CriarConfig()
    {
        ConfiguracoesViewModel? vm = null;
        Capturador.Rodar(() =>
        {
            var config = new ConfiguracaoService(_banco.Fabrica);
            vm = new ConfiguracoesViewModel(config, new CupomService(_banco.Fabrica, config, new ImpressoraDeMentira()));
            vm.UsarSeguranca(new ConfiguracaoSegurancaStore(_arquivo));
            vm.UsarComunicacao();
        });
        return vm!;
    }

    [Fact]
    public void Comeca_com_o_texto_padrao_e_a_previa_sem_campos_soltos()
    {
        var vm = CriarConfig();
        Assert.Equal(MensagensCliente.CobrancaPadrao, vm.MensagemCobrancaTexto);
        Assert.Contains("Maria", vm.PreviaCobranca);
        Assert.DoesNotContain("{", vm.PreviaCobranca);
    }

    [Fact]
    public void Salvar_texto_proprio_grava_e_o_padrao_nao_fica_gravado()
    {
        var vm = CriarConfig();
        vm.MensagemCobrancaTexto = "Oi {cliente}, pague {vencido}.";
        vm.SalvarComunicacaoCommand.Execute(null);
        Assert.Equal("Oi {cliente}, pague {vencido}.", new ConfiguracaoSegurancaStore(_arquivo).Carregar().MensagemCobranca);

        vm.RestaurarMensagemCobrancaCommand.Execute(null);
        vm.SalvarComunicacaoCommand.Execute(null);
        Assert.Null(new ConfiguracaoSegurancaStore(_arquivo).Carregar().MensagemCobranca);
    }

    [Fact]
    public void Texto_grande_demais_da_erro_e_nao_grava()
    {
        var vm = CriarConfig();
        vm.MensagemCobrancaTexto = new string('a', 601);
        vm.SalvarComunicacaoCommand.Execute(null);
        Assert.True(vm.MensagemComunicacaoEhErro);
        Assert.Null(new ConfiguracaoSegurancaStore(_arquivo).Carregar().MensagemCobranca);
    }

    [Fact]
    public void Botao_de_cobranca_abre_o_link_com_a_mensagem_e_avisa_telefone_invalido()
    {
        Capturador.Rodar(() => { });   // inicia a interface antes: ela pode reiniciar a configuração estática
        NexusLS.App.Seguranca.Alcada.Config = () => new ConfiguracaoSeguranca { MensagemCobranca = "Oi {cliente} da {loja}" };
        ClientesViewModel? vm = null;
        var aberto = "";
        Capturador.Rodar(() =>
        {
            vm = new ClientesViewModel(new ClienteService(_banco.Fabrica)) { NomeLoja = "Loja Y", AbrirLink = u => aberto = u };
            vm.Nome = "Ana Paula";
            vm.Telefone = "(11) 98888-7777";
        });
        vm!.CobrarWhatsAppCommand.Execute(null);
        Assert.Equal("https://wa.me/5511988887777?text=Oi%20Ana%20da%20Loja%20Y", aberto);

        aberto = "";
        vm.Telefone = "123";
        vm.CobrarWhatsAppCommand.Execute(null);
        Assert.Equal("", aberto);
        Assert.Contains("telefone válido", vm.Mensagem);
    }
}

public sealed class FiscalConfigTests : IDisposable
{
    private readonly BancoDeTeste _banco = new();
    private readonly string _arquivo = Path.Combine(Path.GetTempPath(), "nexus_fis_" + Guid.NewGuid().ToString("N") + ".json");

    public void Dispose()
    {
        NexusLS.App.Seguranca.Alcada.Config = () => new();
        _banco.Dispose();
        try { File.Delete(_arquivo); } catch (IOException) { }
    }

    private ConfiguracoesViewModel CriarConfig()
    {
        ConfiguracoesViewModel? vm = null;
        Capturador.Rodar(() =>
        {
            var config = new ConfiguracaoService(_banco.Fabrica);
            vm = new ConfiguracoesViewModel(config, new CupomService(_banco.Fabrica, config, new ImpressoraDeMentira()));
            vm.UsarSeguranca(new ConfiguracaoSegurancaStore(_arquivo));
            vm.UsarFiscal();
        });
        return vm!;
    }

    [Fact]
    public void Comeca_em_homologacao_serie_1_e_com_pendencias()
    {
        var vm = CriarConfig();
        Assert.Equal("Homologação (teste)", vm.AmbienteFiscal);
        Assert.Equal("1", vm.SerieFiscal);
        Assert.Contains(vm.ChecklistFiscal, l => l.StartsWith("✖") && l.Contains("Regime"));
    }

    [Fact]
    public void Salvar_grava_e_o_regime_completa_o_item_da_lista()
    {
        var vm = CriarConfig();
        vm.RegimeFiscal = "Simples Nacional";
        vm.SerieFiscal = "2";
        vm.CscIdFiscal = "1";
        vm.NcmPadraoFiscal = "22021000";
        vm.SalvarFiscalCommand.Execute(null);
        var c = new ConfiguracaoSegurancaStore(_arquivo).Carregar();
        Assert.Equal("SimplesNacional", c.FiscalRegime);
        Assert.Equal(2, c.FiscalSerie);
        Assert.Equal("22021000", c.FiscalNcmPadrao);
        Assert.Contains(vm.ChecklistFiscal, l => l.StartsWith("✔") && l.Contains("Regime"));
    }

    [Fact]
    public void Ncm_invalido_nao_salva()
    {
        var vm = CriarConfig();
        vm.NcmPadraoFiscal = "123";
        vm.SalvarFiscalCommand.Execute(null);
        Assert.True(vm.MensagemFiscalEhErro);
        Assert.Null(new ConfiguracaoSegurancaStore(_arquivo).Carregar().FiscalNcmPadrao);
    }
}
