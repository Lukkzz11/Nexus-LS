using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.Tests;

public class GradesEEnderecoTests
{
    [Fact]
    public void As_tres_grades_tem_os_tamanhos_pedidos()
    {
        Assert.Equal("PP P M G GG G1 G2 G3 G4 G5 G6 G7 G8 G9 G10", string.Join(" ", GradesDeTamanho.Letras.Tamanhos));
        Assert.Equal("34 36 38 40 42 44 46 48 50 52 54 56 58 60 62 64 66 68 70", string.Join(" ", GradesDeTamanho.CalcaBermuda.Tamanhos));
        Assert.Equal("1 2 3 4 5 6 7 8 9 10", string.Join(" ", GradesDeTamanho.CamisaBotao.Tamanhos));
    }

    [Fact]
    public async Task Grade_pronta_cria_so_os_tamanhos_que_faltam_e_cores_sao_guardadas()
    {
        using var banco = new BancoDeTeste();
        var produtos = new ProdutoService(banco.Fabrica);
        var p = await produtos.CadastrarAsync(new DadosProduto("Bermuda", null, 9000, 4000, 1, Cores: " preto, Branco ,PRETO "));
        await produtos.AdicionarTamanhoAsync(p.Id, "40");

        var criados = await produtos.AdicionarGradeAsync(p.Id, GradesDeTamanho.CalcaBermuda.Tamanhos);

        Assert.Equal(18, criados.Count); // 19 tamanhos, o 40 já existia
        var tamanhos = await produtos.ListarTamanhosAsync(p.Id);
        Assert.Equal(1, tamanhos.Count(t => t.Tamanho == "40"));
        Assert.Equal("preto, Branco", (await produtos.ObterAsync(p.Id))!.Cores);
    }

    [Fact]
    public async Task Cliente_guarda_cidade_estado_e_endereco()
    {
        using var banco = new BancoDeTeste();
        var clientes = new ClienteService(banco.Fabrica);
        var c = await clientes.CadastrarAsync(new DadosCliente("Maria", Cidade: "Fortaleza", Estado: "ce", Endereco: "Rua A", Numero: "10", Bairro: "Centro", Cep: "60000-000"));

        var salvo = (await clientes.BuscarAsync("Maria")).Single();
        Assert.Equal(("Fortaleza", "CE"), (salvo.Cidade, salvo.Estado));
        Assert.Equal("Rua A, 10 - Centro - Fortaleza/CE - 60000-000", salvo.EnderecoCompleto);
        Assert.Equal(c.Id, salvo.Id);
    }
}
