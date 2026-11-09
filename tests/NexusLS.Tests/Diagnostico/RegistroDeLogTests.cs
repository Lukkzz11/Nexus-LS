using NexusLS.Core.Diagnostico;

namespace NexusLS.Tests.Diagnostico;

public sealed class RegistroDeLogTests : IDisposable
{
    private readonly string _pasta;
    private readonly DateTime _agora = new(2026, 11, 9, 14, 30, 0);

    public RegistroDeLogTests()
    {
        _pasta = Path.Combine(Path.GetTempPath(), "nexus-log-testes-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_pasta);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_pasta)) Directory.Delete(_pasta, true);
        }
        catch (Exception)
        {
            // pasta temporária presa: o Windows limpa depois.
        }
    }

    [Fact]
    public void Grava_info_aviso_e_erro_no_arquivo_do_dia()
    {
        var log = new RegistroDeLogEmArquivo(_pasta, () => _agora);

        log.Info("Venda finalizada com sucesso.");
        log.Aviso("Backup automático atrasado.");
        log.Erro("Falha ao abrir o caixa.", new InvalidOperationException("caixa ocupado"));

        var arquivo = Path.Combine(_pasta, "nexus-2026-11-09.log");
        Assert.True(File.Exists(arquivo));

        var texto = File.ReadAllText(arquivo);
        Assert.Contains("[INFO ]", texto);
        Assert.Contains("[AVISO]", texto);
        Assert.Contains("[ERRO ]", texto);
        Assert.Contains("Venda finalizada com sucesso.", texto);
        Assert.Contains("Falha ao abrir o caixa.", texto);
        Assert.Contains("caixa ocupado", texto);
        Assert.Contains("thread ", texto);
        Assert.Contains("2026-11-09T14:30:00", texto);
    }

    [Fact]
    public void Caminho_invalido_nao_lanca()
    {
        // Um arquivo comum no lugar da pasta: Directory.CreateDirectory falha com IOException.
        var arquivoNoLugarDaPasta = Path.Combine(_pasta, "nao-e-uma-pasta");
        File.WriteAllText(arquivoNoLugarDaPasta, "ocupado");
        var log = new RegistroDeLogEmArquivo(arquivoNoLugarDaPasta, () => _agora);

        var excecao = Record.Exception(() =>
        {
            log.Info("não deve gravar");
            log.Aviso("também não");
            log.Erro("nem este", new Exception("teste"));
        });

        Assert.Null(excecao);
    }

    [Fact]
    public void Retencao_mantem_os_14_mais_recentes_e_so_limpa_uma_vez_por_dia()
    {
        for (var dia = 1; dia <= 20; dia++)
            File.WriteAllText(Path.Combine(_pasta, $"nexus-2026-10-{dia:00}.log"), "antigo");

        var log = new RegistroDeLogEmArquivo(_pasta, () => _agora);
        log.Info("primeiro do dia");

        Assert.Equal(RegistroDeLogEmArquivo.ArquivosMantidos, Directory.GetFiles(_pasta, "nexus-*.log").Length);
        Assert.False(File.Exists(Path.Combine(_pasta, "nexus-2026-10-01.log")));
        Assert.True(File.Exists(Path.Combine(_pasta, "nexus-2026-10-20.log")));
        Assert.True(File.Exists(Path.Combine(_pasta, "nexus-2026-11-09.log")));

        // A limpeza é no máximo uma vez por dia: um arquivo velho criado depois fica para amanhã.
        File.WriteAllText(Path.Combine(_pasta, "nexus-2026-09-30.log"), "antigo");
        log.Info("segundo do dia");

        Assert.True(File.Exists(Path.Combine(_pasta, "nexus-2026-09-30.log")));
        Assert.Equal(RegistroDeLogEmArquivo.ArquivosMantidos + 1, Directory.GetFiles(_pasta, "nexus-*.log").Length);
    }
}
