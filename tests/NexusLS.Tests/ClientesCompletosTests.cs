using System.Text;
using Microsoft.EntityFrameworkCore;
using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.Tests;

/// <summary>CPF/CNPJ: dígitos verificadores, máscara e mensagens. Código puro, sem banco.</summary>
public class ValidadorDocumentoTests
{
    [Theory]
    [InlineData("529.982.247-25")]
    [InlineData("52998224725")]
    [InlineData("  529 982 247 25 ")]
    public void Cpf_valido_com_ou_sem_mascara(string cpf)
    {
        var r = ValidadorDocumento.Validar(cpf);
        Assert.True(r.Valido);
        Assert.Equal(TipoDocumento.Cpf, r.Tipo);
        Assert.Equal("52998224725", r.Digitos);
    }

    [Theory]
    [InlineData("11.222.333/0001-81")]
    [InlineData("11222333000181")]
    public void Cnpj_valido_com_ou_sem_mascara(string cnpj)
    {
        var r = ValidadorDocumento.Validar(cnpj);
        Assert.True(r.Valido);
        Assert.Equal(TipoDocumento.Cnpj, r.Tipo);
        Assert.Equal("11222333000181", r.Digitos);
    }

    [Theory]
    [InlineData("529.982.247-24")]   // dígito verificador errado
    [InlineData("111.111.111-11")]   // todos iguais passam na conta, mas não existem
    [InlineData("00000000000")]
    public void Cpf_invalido_e_recusado(string cpf)
    {
        var r = ValidadorDocumento.Validar(cpf);
        Assert.False(r.Valido);
        Assert.Contains("CPF inválido", r.Erro);
    }

    [Theory]
    [InlineData("11.222.333/0001-82")]
    [InlineData("11111111111111")]
    public void Cnpj_invalido_e_recusado(string cnpj)
    {
        var r = ValidadorDocumento.Validar(cnpj);
        Assert.False(r.Valido);
        Assert.Contains("CNPJ inválido", r.Erro);
    }

    [Theory]
    [InlineData("", "Informe")]
    [InlineData("   ", "Informe")]
    [InlineData("12345", "11 números")]
    [InlineData("123456789012", "11 números")]
    public void Vazio_ou_com_tamanho_errado_explica_o_motivo(string texto, string trecho)
    {
        var r = ValidadorDocumento.Validar(texto);
        Assert.False(r.Valido);
        Assert.Contains(trecho, r.Erro);
    }

    [Fact]
    public void Formata_cpf_e_cnpj_e_deixa_o_resto_como_veio()
    {
        Assert.Equal("529.982.247-25", ValidadorDocumento.Formatar("52998224725"));
        Assert.Equal("11.222.333/0001-81", ValidadorDocumento.Formatar("11222333000181"));
        Assert.Equal("123", ValidadorDocumento.Formatar("123"));
        Assert.Equal("", ValidadorDocumento.Formatar(null));
    }
}

/// <summary>Cadastro completo (documento obrigatório, único, razão social, e-mail) e crédito do cliente.</summary>
[Collection("OperadorAtual")]
public class ClientesCompletosTests : IDisposable
{
    private static readonly DateTime Agora = new(2026, 11, 5, 10, 0, 0);
    private const string Cpf = "529.982.247-25";
    private const string Cnpj = "11.222.333/0001-81";

    private readonly BancoDeTeste _banco = new();
    private readonly ClienteService _servico;

    public ClientesCompletosTests()
    {
        OperadorAtual.Nome = null;
        _servico = new ClienteService(_banco.Fabrica, () => Agora) { ExigirDocumentoNaTela = true };
    }

    public void Dispose()
    {
        OperadorAtual.Nome = null;
        _banco.Dispose();
    }

    private static DadosCliente Dados(string nome, string? doc = Cpf, string? razao = null, string? email = null) =>
        new(nome, doc, "(81) 99999-0000", null, 0, razao, email, ExigirDocumento: true);

    [Fact]
    public async Task Cadastra_guardando_so_os_digitos_e_os_novos_campos()
    {
        var c = await _servico.CadastrarAsync(Dados("Loja do Zé", Cnpj, "Zé Comércio LTDA", "ze@loja.com.br"));

        Assert.Equal("11222333000181", c.Documento);
        Assert.Equal("11.222.333/0001-81", c.DocumentoFormatado);
        Assert.Equal("Zé Comércio LTDA", c.RazaoSocial);
        Assert.Equal("ze@loja.com.br", c.Email);
        Assert.Equal(0, c.CreditoCentavos);
    }

    [Fact]
    public async Task Documento_e_obrigatorio_e_precisa_estar_certo()
    {
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _servico.CadastrarAsync(Dados("Sem doc", null)));
        var ex = await Assert.ThrowsAsync<RegraDeNegocioException>(() => _servico.CadastrarAsync(Dados("Doc ruim", "123.456.789-00")));
        Assert.Contains("CPF inválido", ex.Message);
        Assert.Empty(await _servico.BuscarAsync(null, incluirInativos: true));
    }

    [Fact]
    public async Task Email_invalido_e_recusado()
    {
        var ex = await Assert.ThrowsAsync<RegraDeNegocioException>(() => _servico.CadastrarAsync(Dados("Maria", Cpf, null, "isso-nao-e-email")));
        Assert.Contains("E-mail", ex.Message);
    }

    [Fact]
    public async Task Documento_nao_repete_entre_clientes_ativos_mesmo_com_mascara_diferente()
    {
        await _servico.CadastrarAsync(Dados("Maria"));

        var ex = await Assert.ThrowsAsync<RegraDeNegocioException>(() => _servico.CadastrarAsync(Dados("Outra Maria", "52998224725")));
        Assert.Contains("já está cadastrado para Maria", ex.Message);
    }

    [Fact]
    public async Task Editar_o_proprio_cliente_nao_conta_como_duplicado()
    {
        var maria = await _servico.CadastrarAsync(Dados("Maria"));
        await _servico.EditarAsync(maria.Id, Dados("Maria Souza"));
        Assert.Equal("Maria Souza", (await _servico.BuscarAsync("Souza")).Single().Nome);
    }

    [Fact]
    public async Task Cliente_desativado_libera_o_documento_e_reativar_confere_de_novo()
    {
        var antiga = await _servico.CadastrarAsync(Dados("Maria Antiga"));
        await _servico.DesativarAsync(antiga.Id);

        var nova = await _servico.CadastrarAsync(Dados("Maria Nova")); // pode: a outra está desativada
        Assert.NotEqual(antiga.Id, nova.Id);

        var ex = await Assert.ThrowsAsync<RegraDeNegocioException>(() => _servico.ReativarAsync(antiga.Id));
        Assert.Contains("já está cadastrado para Maria Nova", ex.Message);
    }

    [Fact]
    public async Task Busca_acha_pelo_documento_com_ou_sem_pontuacao_e_pelo_email()
    {
        await _servico.CadastrarAsync(Dados("Maria", Cpf, null, "maria@mail.com"));
        await _servico.CadastrarAsync(Dados("Loja", Cnpj, "Razão Azul Ltda"));

        Assert.Equal("Maria", (await _servico.BuscarAsync("529.982")).Single().Nome);
        Assert.Equal("Maria", (await _servico.BuscarAsync("52998224725")).Single().Nome);
        Assert.Equal("Maria", (await _servico.BuscarAsync("maria@mail")).Single().Nome);
        Assert.Equal("Loja", (await _servico.BuscarAsync("Azul")).Single().Nome);
        Assert.Equal("Loja", (await _servico.BuscarAsync("11.222.333/0001")).Single().Nome);
    }

    [Fact]
    public async Task Cadastro_rapido_do_caixa_continua_sem_documento()
    {
        var c = await _servico.CadastrarAsync(new DadosCliente("Cliente do balcão"));
        Assert.Null(c.Documento);
    }

    // ---------- crédito ----------

    [Fact]
    public async Task Ajuste_de_credito_muda_o_saldo_grava_o_historico_e_a_atividade_juntos()
    {
        OperadorAtual.Nome = "Ana";
        var maria = await _servico.CadastrarAsync(Dados("Maria"));

        var ajuste = await _servico.AjustarCreditoAsync(maria.Id, 5000, "  Devolução de blusa  ");

        Assert.Equal((0L, 5000L, "Devolução de blusa", "Ana", Agora),
            (ajuste.SaldoAnteriorCentavos, ajuste.SaldoNovoCentavos, ajuste.Motivo, ajuste.Operador, ajuste.CriadoEm));
        await using var db = _banco.CriarContexto();
        Assert.Equal(5000, (await db.Clientes.SingleAsync()).CreditoCentavos);
        var atividade = await db.Atividades.SingleAsync();
        Assert.Equal(TipoAtividade.CreditoClienteAjustado, atividade.Tipo);
        Assert.Equal("Ana", atividade.Operador);
        Assert.Contains("Maria", atividade.Descricao);
        Assert.Contains("Devolução de blusa", atividade.Descricao);
        Assert.Equal(Agora, atividade.DataHora);
    }

    [Fact]
    public async Task Historico_do_credito_lista_do_mais_recente_para_o_mais_antigo()
    {
        var maria = await _servico.CadastrarAsync(Dados("Maria"));
        await _servico.AjustarCreditoAsync(maria.Id, 5000, "Crédito inicial");
        await _servico.AjustarCreditoAsync(maria.Id, 3000, "Usou na compra");

        var historico = await _servico.HistoricoCreditoAsync(maria.Id);

        Assert.Equal(new[] { "Usou na compra", "Crédito inicial" }, historico.Select(h => h.Motivo));
        Assert.Equal((5000L, 3000L), (historico[0].SaldoAnteriorCentavos, historico[0].SaldoNovoCentavos));
    }

    [Theory]
    [InlineData("")]
    [InlineData("ab")]
    [InlineData("   ")]
    public async Task Ajuste_sem_motivo_de_3_letras_e_recusado_e_nada_muda(string motivo)
    {
        var maria = await _servico.CadastrarAsync(Dados("Maria"));

        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _servico.AjustarCreditoAsync(maria.Id, 1000, motivo));

        await using var db = _banco.CriarContexto();
        Assert.Equal(0, (await db.Clientes.SingleAsync()).CreditoCentavos);
        Assert.Empty(db.AjustesCreditoCliente);
        Assert.Empty(db.Atividades);
    }

    [Fact]
    public async Task Credito_nao_fica_negativo_e_ajuste_igual_ao_saldo_e_recusado()
    {
        var maria = await _servico.CadastrarAsync(Dados("Maria"));
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _servico.AjustarCreditoAsync(maria.Id, -1, "Teste"));
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _servico.AjustarCreditoAsync(maria.Id, 0, "Teste"));
    }

    [Fact]
    public async Task Cliente_inexistente_no_ajuste_de_credito()
    {
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _servico.AjustarCreditoAsync(999, 100, "Teste"));
    }
}

/// <summary>Importar e exportar clientes em CSV.</summary>
[Collection("OperadorAtual")]
public class ImportacaoExportacaoClientesTests : IDisposable
{
    private static readonly DateTime Agora = new(2026, 11, 5, 10, 0, 0);
    private const string Cpf1 = "529.982.247-25";
    private const string Cnpj1 = "11.222.333/0001-81";
    private const string Cabecalho = "CPF/CNPJ;Nome;Razão social;Telefone;E-mail;Crédito;Limite a prazo;Observação;Situação";

    private readonly BancoDeTeste _banco = new();
    private readonly ClienteService _servico;

    public ImportacaoExportacaoClientesTests()
    {
        OperadorAtual.Nome = null;
        _servico = new ClienteService(_banco.Fabrica, () => Agora) { ExigirDocumentoNaTela = true };
    }

    public void Dispose()
    {
        OperadorAtual.Nome = null;
        _banco.Dispose();
    }

    private static string Csv(params string[] linhas) => string.Join("\r\n", new[] { Cabecalho }.Concat(linhas)) + "\r\n";

    // ---------- exportação ----------

    [Fact]
    public async Task Exporta_todas_as_colunas_com_bom_ponto_e_virgula_e_valores_com_virgula()
    {
        var maria = await _servico.CadastrarAsync(new DadosCliente("Maria; \"a Grande\"", Cpf1, "(81) 99999-0000", "Obs", 50000, null, "maria@mail.com", true));
        await _servico.AjustarCreditoAsync(maria.Id, 12550, "Crédito inicial");
        await _servico.CadastrarAsync(new DadosCliente("Zé Loja", Cnpj1, "+55 81 3333-0000", null, 0, "Zé Comércio LTDA", null, true));
        var inativo = await _servico.CadastrarAsync(new DadosCliente("Antigo", "39053344705", null, null, 0, null, null, true));
        await _servico.DesativarAsync(inativo.Id);

        var csv = ExportadorClientes.Csv(await _servico.ListarParaExportacaoAsync());
        var linhas = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(Cabecalho, linhas[0]);
        Assert.Equal(4, linhas.Length);
        Assert.Equal("390.533.447-05;Antigo;;;;0,00;0,00;;Inativo", linhas[1]);
        Assert.Equal("529.982.247-25;\"Maria; \"\"a Grande\"\"\";;(81) 99999-0000;maria@mail.com;125,50;500,00;Obs;Ativo", linhas[2]);
        Assert.StartsWith("11.222.333/0001-81;Zé Loja;Zé Comércio LTDA;'+55 81 3333-0000;;0,00;0,00;;Ativo", linhas[3]);
    }

    [Fact]
    public void Arquivo_exportado_e_gravado_em_utf8_com_bom()
    {
        var caminho = Path.Combine(Path.GetTempPath(), $"clientes-{Guid.NewGuid():N}.csv");
        try
        {
            ExportadorClientes.Salvar(caminho, new[] { new Cliente { Nome = "João Çedilha", Documento = "52998224725" } });
            var bytes = File.ReadAllBytes(caminho);
            Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
            Assert.Contains("João Çedilha", new UTF8Encoding(false).GetString(bytes[3..]));
        }
        finally { File.Delete(caminho); }
    }

    [Fact]
    public async Task O_que_foi_exportado_volta_na_importacao_sem_mudar_nada()
    {
        var maria = await _servico.CadastrarAsync(new DadosCliente("Maria", Cpf1, "+55 81 9999-0000", "Obs", 1000, "Maria ME", "m@mail.com", true));
        await _servico.AjustarCreditoAsync(maria.Id, 2500, "Crédito inicial");
        var csv = ExportadorClientes.Csv(await _servico.ListarParaExportacaoAsync());

        var previa = await _servico.PreverImportacaoAsync(csv);

        Assert.Null(previa.Erro);
        Assert.Equal((0, 0, 1, 0), (previa.Novos, previa.Atualizados, previa.SemMudanca, previa.Erros));
        Assert.False(previa.PodeImportar);
    }

    // ---------- importação ----------

    [Fact]
    public async Task Previa_conta_novos_atualizados_e_erros_com_o_motivo_de_cada_linha_e_nao_grava()
    {
        await _servico.CadastrarAsync(Dados("Maria Antiga", Cpf1));

        var previa = await _servico.PreverImportacaoAsync(Csv(
            $"{Cpf1};Maria Souza;;(81) 98888-0000;;;;;Ativo",       // atualiza
            $"{Cnpj1};Loja Nova;Loja Nova LTDA;;loja@x.com;10,00;;;",  // novo
            "123.456.789-00;Doc Ruim;;;;;;;",                         // erro: CPF inválido
            $"11222333000181;;;;;;;;",                                // erro: sem nome
            "11144477735;Email Ruim;;;sem-arroba;;;;",                // erro: e-mail
            "39053344705;Credito Ruim;;;;abc;;;"));                   // erro: crédito

        Assert.Equal((1, 1, 4), (previa.Novos, previa.Atualizados, previa.Erros));
        Assert.Equal(6, previa.Linhas.Count);
        Assert.Equal(new[] { 2, 3, 4, 5, 6, 7 }, previa.Linhas.Select(l => l.Numero));
        Assert.Contains("telefone", previa.Linhas[0].Motivo);
        Assert.Contains("CPF inválido", previa.Linhas[2].Motivo);
        Assert.Contains("Nome em branco", previa.Linhas[3].Motivo);
        Assert.Contains("E-mail inválido", previa.Linhas[4].Motivo);
        Assert.Contains("Crédito inválido", previa.Linhas[5].Motivo);
        Assert.True(previa.PodeImportar);

        // pré-visualizar não grava nada
        var todos = await _servico.BuscarAsync(null, incluirInativos: true);
        Assert.Equal("Maria Antiga", Assert.Single(todos).Nome);
        await using var db = _banco.CriarContexto();
        Assert.Empty(db.Atividades);
    }

    [Fact]
    public async Task Importar_cria_os_novos_atualiza_pelo_documento_e_ignora_os_erros()
    {
        var maria = await _servico.CadastrarAsync(Dados("Maria Antiga", Cpf1));

        var resultado = await _servico.ImportarAsync(Csv(
            $"529.982.247-25;Maria Souza;;(81) 98888-0000;maria@mail.com;;;;",
            $"{Cnpj1};Loja Nova;Loja Nova LTDA;;;;300,00;Cliente de atacado;",
            "000.000.000-00;Falso;;;;;;;"));

        Assert.Equal((1, 1, 1), (resultado.Novos, resultado.Atualizados, resultado.Erros));
        var todos = await _servico.BuscarAsync(null, incluirInativos: true);
        Assert.Equal(2, todos.Count);

        var atualizada = todos.Single(c => c.Id == maria.Id);
        Assert.Equal(("Maria Souza", "(81) 98888-0000", "maria@mail.com", "52998224725"),
            (atualizada.Nome, atualizada.Telefone, atualizada.Email, atualizada.Documento));

        var nova = todos.Single(c => c.Id != maria.Id);
        Assert.Equal(("Loja Nova", "11222333000181", "Loja Nova LTDA", 30000L, "Cliente de atacado", true, Agora),
            (nova.Nome, nova.Documento, nova.RazaoSocial, nova.LimiteCreditoCentavos, nova.Observacao, nova.Ativo, nova.DataCadastro));

        await using var db = _banco.CriarContexto();
        var atividade = await db.Atividades.SingleAsync();
        Assert.Equal(TipoAtividade.ClientesImportados, atividade.Tipo);
        Assert.Contains("1 novo(s), 1 atualizado(s), 1 linha(s) com erro", atividade.Descricao);
    }

    [Fact]
    public async Task Importar_nunca_apaga_nem_desativa_quem_nao_esta_no_arquivo()
    {
        await _servico.CadastrarAsync(Dados("Maria", Cpf1));
        var inativo = await _servico.CadastrarAsync(Dados("Antigo", "39053344705"));
        await _servico.DesativarAsync(inativo.Id);

        await _servico.ImportarAsync(Csv($"{Cnpj1};Loja Nova;;;;;;;"));

        var todos = await _servico.BuscarAsync(null, incluirInativos: true);
        Assert.Equal(new[] { "Antigo", "Loja Nova", "Maria" }, todos.Select(c => c.Nome).OrderBy(n => n));
        Assert.False(todos.Single(c => c.Nome == "Antigo").Ativo);
        Assert.True(todos.Single(c => c.Nome == "Maria").Ativo);
    }

    [Fact]
    public async Task Atualizar_o_credito_pela_planilha_gera_historico_e_atividade()
    {
        var maria = await _servico.CadastrarAsync(Dados("Maria", Cpf1));

        await _servico.ImportarAsync(Csv($"{Cpf1};Maria;;;;45,90;;;"));

        await using var db = _banco.CriarContexto();
        Assert.Equal(4590, (await db.Clientes.SingleAsync()).CreditoCentavos);
        var ajuste = await db.AjustesCreditoCliente.SingleAsync();
        Assert.Equal((maria.Id, 0L, 4590L), (ajuste.ClienteId, ajuste.SaldoAnteriorCentavos, ajuste.SaldoNovoCentavos));
        Assert.Contains("Importação", ajuste.Motivo);
        Assert.Equal(2, await db.Atividades.CountAsync()); // o ajuste de crédito e o resumo da importação
    }

    [Fact]
    public async Task Cliente_novo_com_credito_na_planilha_ja_nasce_com_historico()
    {
        await _servico.ImportarAsync(Csv($"{Cpf1};Maria;;;;20,00;;;"));

        await using var db = _banco.CriarContexto();
        var cliente = await db.Clientes.SingleAsync();
        Assert.Equal(2000, cliente.CreditoCentavos);
        Assert.Equal(cliente.Id, (await db.AjustesCreditoCliente.SingleAsync()).ClienteId);
    }

    [Fact]
    public async Task Celula_vazia_mantem_o_valor_que_ja_estava_no_cadastro()
    {
        await _servico.CadastrarAsync(new DadosCliente("Maria", Cpf1, "(81) 99999-0000", "Obs antiga", 70000, "Maria ME", "m@mail.com", true));

        await _servico.ImportarAsync(Csv($"{Cpf1};Maria Souza;;;;;;;"));

        var c = (await _servico.BuscarAsync(null)).Single();
        Assert.Equal(("Maria Souza", "(81) 99999-0000", "m@mail.com", "Maria ME", "Obs antiga", 70000L),
            (c.Nome, c.Telefone, c.Email, c.RazaoSocial, c.Observacao, c.LimiteCreditoCentavos));
    }

    [Fact]
    public async Task Documento_repetido_no_proprio_arquivo_vira_erro_na_segunda_linha()
    {
        var previa = await _servico.PreverImportacaoAsync(Csv($"{Cpf1};Maria;;;;;;;", $"52998224725;Maria de novo;;;;;;;"));

        Assert.Equal((1, 1), (previa.Novos, previa.Erros));
        Assert.Contains("repetido", previa.Linhas[1].Motivo);
        Assert.Contains("linha 2", previa.Linhas[1].Motivo);
    }

    [Fact]
    public async Task Aceita_colunas_em_outra_ordem_apelidos_e_virgula_como_separador()
    {
        var previa = await _servico.PreverImportacaoAsync($"Cliente,CPF,Celular,Saldo\r\nMaria,{Cpf1},81 99999-0000,\"1.250,00\"\r\n");

        Assert.Null(previa.Erro);
        var linha = Assert.Single(previa.Linhas);
        Assert.Equal(SituacaoLinhaImportacao.Novo, linha.Situacao);
        Assert.Equal(("Maria", "52998224725", "81 99999-0000", 125000L),
            (linha.Dados!.Nome, linha.Dados.Documento, linha.Dados.Telefone, linha.Dados.CreditoCentavos));
    }

    [Theory]
    [InlineData("", "vazio")]
    [InlineData("Nome;Telefone\r\nMaria;1\r\n", "CPF/CNPJ")]
    [InlineData("CPF/CNPJ;Telefone\r\n529.982.247-25;1\r\n", "Nome")]
    [InlineData("CPF/CNPJ;Nome\r\n", "nenhum cliente")]
    public async Task Arquivo_sem_as_colunas_certas_ou_sem_linhas_da_erro_claro(string csv, string trecho)
    {
        var previa = await _servico.PreverImportacaoAsync(csv);

        Assert.Contains(trecho, previa.Erro);
        Assert.False(previa.PodeImportar);
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _servico.ImportarAsync(csv));
    }

    [Fact]
    public async Task Importar_sem_nenhuma_linha_valida_nao_grava_nada()
    {
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _servico.ImportarAsync(Csv("000.000.000-00;Falso;;;;;;;")));

        Assert.Empty(await _servico.BuscarAsync(null, incluirInativos: true));
        await using var db = _banco.CriarContexto();
        Assert.Empty(db.Atividades);
    }

    [Fact]
    public async Task Cliente_ativo_duplicado_no_banco_vira_erro_em_vez_de_escolher_um()
    {
        // dois cadastros antigos (sem a regra) com o mesmo documento
        var antigo = new ClienteService(_banco.Fabrica, () => Agora);
        await antigo.CadastrarAsync(new DadosCliente("Maria 1", "529.982.247-25"));
        await antigo.CadastrarAsync(new DadosCliente("Maria 2", "52998224725"));

        var previa = await _servico.PreverImportacaoAsync(Csv($"{Cpf1};Maria;;;;;;;"));

        Assert.Equal(1, previa.Erros);
        Assert.Contains("2 clientes ativos", previa.Linhas[0].Motivo);
    }

    [Fact]
    public async Task Planilha_do_excel_em_windows_1252_e_lida()
    {
        var bytes = Encoding.Latin1.GetBytes(Csv($"{Cpf1};João Conceição;;;;;;;"));
        var texto = ImportadorProdutos.Decodificar(bytes);

        var previa = await _servico.PreverImportacaoAsync(texto);

        Assert.Equal("João Conceição", previa.Linhas.Single().Nome);
    }

    private static DadosCliente Dados(string nome, string doc) => new(nome, doc, null, null, 0, null, null, true);
}
