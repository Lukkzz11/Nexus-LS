using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using NexusLS.App.ViewModels;
using NexusLS.Core.Atualizacao;

namespace NexusLS.App.Tests;

public sealed class AtualizacaoViewModelTests : IDisposable
{
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "nexus-atualizacao-vm-" + Guid.NewGuid().ToString("N"));
    private readonly FonteFalsa _fonte = new();
    private readonly List<string> _aplicados = new();
    private bool _confirma = true;
    private bool _aplicarFalha;

    public void Dispose()
    {
        try { Directory.Delete(_pasta, true); } catch (IOException) { }
    }

    private sealed class FonteFalsa : IFonteAtualizacao
    {
        public InfoVersao? Ultima { get; set; }
        public Exception? Erro { get; set; }
        public byte[] Conteudo { get; set; } = Array.Empty<byte>();
        public int Downloads { get; private set; }

        public Task<InfoVersao?> UltimaAsync(CancellationToken ct = default) =>
            Erro is not null ? throw Erro : Task.FromResult(Ultima);

        public async Task BaixarAsync(InfoVersao versao, Stream destino, IProgress<long>? progresso, CancellationToken ct = default)
        {
            Downloads++;
            await destino.WriteAsync(Conteudo, ct);
            progresso?.Report(Conteudo.Length);
        }
    }

    private static byte[] Programa(byte semente = 7)
    {
        var b = new byte[4096];
        for (var i = 0; i < b.Length; i++) b[i] = (byte)(i * semente);
        b[0] = (byte)'M';
        b[1] = (byte)'Z';
        return b;
    }

    private static InfoVersao Info(string versao, byte[] conteudo, string notas = "- Contas e saldos") =>
        new(VersaoPrograma.Parse(versao), notas, new Uri("https://github.com/x/y/releases/download/v/NexusLS.exe"),
            conteudo.Length, Convert.ToHexString(SHA256.HashData(conteudo)).ToLowerInvariant(), null);

    private AtualizacaoViewModel Criar(string atual = "1.1.0")
    {
        var servico = new AtualizadorService(_fonte, Path.Combine(_pasta, "baixados"), VersaoPrograma.Parse(atual));
        return new AtualizacaoViewModel(servico, arquivo =>
        {
            if (_aplicarFalha) throw new InvalidOperationException("Sem permissão para trocar o programa.");
            _aplicados.Add(arquivo);
            return Task.CompletedTask;
        }) { Confirmar = _ => _confirma };
    }

    [Fact]
    public async Task Mostra_a_versao_do_programa()
    {
        Assert.Equal("Versão 1.1.0", Criar().VersaoAtualTexto);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Ao_abrir_com_versao_nova_aparece_o_aviso_sem_instalar_nada()
    {
        _fonte.Ultima = Info("1.2.0", Programa());
        var vm = Criar();

        await vm.VerificarAoAbrirAsync();

        Assert.True(vm.TemNovaVersao);
        Assert.Contains("1.2.0", vm.AvisoTexto);
        Assert.Contains("1.1.0", vm.AvisoTexto);
        Assert.Empty(_aplicados);
        Assert.Equal(0, _fonte.Downloads);
    }

    [Fact]
    public async Task Ao_abrir_sem_novidade_ou_sem_internet_nao_incomoda_ninguem()
    {
        var vm = Criar();

        _fonte.Ultima = Info("1.1.0", Programa());
        await vm.VerificarAoAbrirAsync();
        Assert.False(vm.TemNovaVersao);
        Assert.Equal("", vm.Mensagem);

        _fonte.Erro = new HttpRequestException("sem rede");
        await vm.VerificarAoAbrirAsync();
        Assert.False(vm.TemNovaVersao);
        Assert.Equal("", vm.Mensagem);
    }

    [Fact]
    public async Task Verificacao_manual_responde_em_qualquer_caso()
    {
        var vm = Criar();

        _fonte.Ultima = null;
        await vm.VerificarAgoraCommand.ExecuteAsync(null);
        Assert.False(vm.MensagemEhErro);
        Assert.Contains("mais nova", vm.Mensagem);

        _fonte.Erro = new HttpRequestException("sem rede");
        await vm.VerificarAgoraCommand.ExecuteAsync(null);
        Assert.True(vm.MensagemEhErro);
        Assert.Contains("internet", vm.Mensagem);

        _fonte.Erro = null;
        _fonte.Ultima = Info("1.3.0", Programa());
        await vm.VerificarAgoraCommand.ExecuteAsync(null);
        Assert.True(vm.TemNovaVersao);
    }

    [Fact]
    public async Task Depois_esconde_o_aviso()
    {
        _fonte.Ultima = Info("1.2.0", Programa());
        var vm = Criar();
        await vm.VerificarAoAbrirAsync();

        vm.DepoisCommand.Execute(null);

        Assert.False(vm.TemNovaVersao);
        Assert.Empty(_aplicados);
    }

    [Fact]
    public async Task O_que_mudou_mostra_as_notas_da_versao()
    {
        _fonte.Ultima = Info("1.2.0", Programa(), "- Contas e saldos\n- Atualizador");
        var vm = Criar();
        string? titulo = null, texto = null;
        vm.MostrarNotas = (t, x) => { titulo = t; texto = x; };
        await vm.VerificarAoAbrirAsync();

        vm.VerNovidadesCommand.Execute(null);

        Assert.Contains("1.2.0", titulo);
        Assert.Contains("Atualizador", texto);
    }

    [Fact]
    public async Task Atualizar_agora_baixa_confere_e_manda_aplicar_o_arquivo()
    {
        var programa = Programa();
        _fonte.Ultima = Info("1.2.0", programa);
        _fonte.Conteudo = programa;
        var vm = Criar();
        await vm.VerificarAoAbrirAsync();

        await vm.AtualizarAgoraCommand.ExecuteAsync(null);

        var aplicado = Assert.Single(_aplicados);
        Assert.EndsWith("NexusLS-1.2.0.exe", aplicado);
        Assert.Equal(programa, await File.ReadAllBytesAsync(aplicado));
        Assert.False(vm.MensagemEhErro);
    }

    [Fact]
    public async Task Se_o_usuario_nao_confirmar_nada_e_baixado()
    {
        _fonte.Ultima = Info("1.2.0", Programa());
        _fonte.Conteudo = Programa();
        _confirma = false;
        var vm = Criar();
        await vm.VerificarAoAbrirAsync();

        await vm.AtualizarAgoraCommand.ExecuteAsync(null);

        Assert.Equal(0, _fonte.Downloads);
        Assert.Empty(_aplicados);
        Assert.True(vm.TemNovaVersao); // o aviso continua para ele decidir depois
    }

    [Fact]
    public async Task Arquivo_adulterado_e_barrado_e_nada_e_instalado()
    {
        _fonte.Ultima = Info("1.2.0", Programa(7));   // a assinatura é do arquivo verdadeiro
        _fonte.Conteudo = Programa(9);                // mas veio outro
        var vm = Criar();
        await vm.VerificarAoAbrirAsync();

        await vm.AtualizarAgoraCommand.ExecuteAsync(null);

        Assert.True(vm.MensagemEhErro);
        Assert.Contains("não confere", vm.Mensagem);
        Assert.Empty(_aplicados);
        Assert.False(vm.Baixando);
    }

    [Fact]
    public async Task Se_a_instalacao_falhar_o_programa_continua_na_versao_atual_e_diz_o_motivo()
    {
        var programa = Programa();
        _fonte.Ultima = Info("1.2.0", programa);
        _fonte.Conteudo = programa;
        _aplicarFalha = true;
        var vm = Criar();
        await vm.VerificarAoAbrirAsync();

        await vm.AtualizarAgoraCommand.ExecuteAsync(null);

        Assert.True(vm.MensagemEhErro);
        Assert.Contains("continua na versão atual", vm.Mensagem);
        Assert.Contains("Sem permissão", vm.Mensagem);
        Assert.False(vm.Baixando);
    }

    [Fact]
    public async Task Sem_internet_durante_o_download_orienta_a_tentar_de_novo()
    {
        _fonte.Ultima = Info("1.2.0", Programa());
        var vm = Criar();
        await vm.VerificarAoAbrirAsync();
        _fonte.Conteudo = Programa();
        // a consulta funcionou, mas a conexão cai na hora de baixar
        var semRede = new FonteQueCai(_fonte.Ultima);
        var servico = new AtualizadorService(semRede, Path.Combine(_pasta, "baixados"), VersaoPrograma.Parse("1.1.0"));
        var vm2 = new AtualizacaoViewModel(servico, _ => Task.CompletedTask) { Confirmar = _ => true };
        await vm2.VerificarAoAbrirAsync();

        await vm2.AtualizarAgoraCommand.ExecuteAsync(null);

        Assert.True(vm2.MensagemEhErro);
        Assert.Contains("internet", vm2.Mensagem);
        Assert.False(vm2.Baixando);
    }

    private sealed class FonteQueCai : IFonteAtualizacao
    {
        private readonly InfoVersao _info;
        public FonteQueCai(InfoVersao info) => _info = info;
        public Task<InfoVersao?> UltimaAsync(CancellationToken ct = default) => Task.FromResult<InfoVersao?>(_info);
        public Task BaixarAsync(InfoVersao versao, Stream destino, IProgress<long>? progresso, CancellationToken ct = default) =>
            throw new HttpRequestException("conexão caiu");
    }
}
