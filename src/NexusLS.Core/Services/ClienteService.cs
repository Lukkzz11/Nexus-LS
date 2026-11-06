using Microsoft.EntityFrameworkCore;
using NexusLS.Core.Data;
using NexusLS.Core.Models;

namespace NexusLS.Core.Services;

/// <summary>
/// Dados do cadastro. Com <paramref name="ExigirDocumento"/> (o cadastro da tela e a importação) o CPF/CNPJ é obrigatório, tem
/// de ter os dígitos conferidos, é guardado só com números e não pode repetir em outro cliente ativo. Sem ele (cadastro rápido
/// do caixa, só com o nome) o documento continua opcional e é guardado como veio.
/// </summary>
public record DadosCliente(string Nome, string? Documento = null, string? Telefone = null, string? Observacao = null, long LimiteCreditoCentavos = 0,
    string? RazaoSocial = null, string? Email = null, bool ExigirDocumento = false);

/// <summary>Um ajuste de crédito com tudo que o histórico mostra.</summary>
public record AjusteDeCredito(long SaldoAnteriorCentavos, long SaldoNovoCentavos, string Motivo, string? Operador, DateTime Quando);

/// <summary>
/// As compras de um cliente. <c>TotalAPrazoCentavos</c> é só o que foi vendido a prazo; o que ainda
/// está devendo (descontando os pagamentos) vem de <see cref="ClienteService.ContaAsync(int)"/>.
/// </summary>
/// <param name="Vendas">Da mais recente para a mais antiga, inclusive as canceladas.</param>
public record HistoricoCliente(
    IReadOnlyList<Venda> Vendas, int Compras, long TotalCompradoCentavos, long TotalAPrazoCentavos, DateTime? UltimaCompra);

public class ClienteService
{
    private readonly IDbContextFactory<PdvDbContext> _fabrica;
    private readonly Func<DateTime> _agora;

    /// <summary>
    /// Liga a regra de CPF/CNPJ obrigatório e conferido no cadastro feito pela tela de Clientes. O programa liga; o cadastro
    /// rápido do caixa (só o nome) não usa isso e continua como era.
    /// </summary>
    public bool ExigirDocumentoNaTela { get; init; }

    public ClienteService(IDbContextFactory<PdvDbContext> fabrica, Func<DateTime>? agora = null)
    {
        _fabrica = fabrica;
        _agora = agora ?? (() => DateTime.Now);
    }

    public async Task<Cliente> CadastrarAsync(DadosCliente dados)
    {
        Validar(dados);
        var cliente = new Cliente { DataCadastro = _agora(), Ativo = true };
        Preencher(cliente, dados);

        await using var db = await _fabrica.CreateDbContextAsync();
        await ExigirDocumentoLivreAsync(db, cliente.Documento, null, dados);
        db.Clientes.Add(cliente);
        await db.SaveChangesAsync();
        return cliente;
    }

    public async Task EditarAsync(int id, DadosCliente dados)
    {
        Validar(dados);
        await using var db = await _fabrica.CreateDbContextAsync();
        var cliente = await db.Clientes.FindAsync(id)
            ?? throw new RegraDeNegocioException("Cliente não encontrado.");
        Preencher(cliente, dados);
        if (cliente.Ativo) await ExigirDocumentoLivreAsync(db, cliente.Documento, id, dados);
        await db.SaveChangesAsync();
    }

    /// <summary>Desativar não apaga: o cliente some da busca, mas continua nas vendas antigas.</summary>
    public Task DesativarAsync(int id) => DefinirAtivoAsync(id, false);

    public Task ReativarAsync(int id) => DefinirAtivoAsync(id, true);

    /// <summary>Busca por parte do nome, razão social, e-mail, documento (com ou sem máscara) ou telefone. Sem texto, lista todos.</summary>
    public async Task<List<Cliente>> BuscarAsync(string? texto, bool incluirInativos = false)
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        IQueryable<Cliente> consulta = db.Clientes.AsNoTracking();
        if (!incluirInativos) consulta = consulta.Where(c => c.Ativo);

        texto = texto?.Trim();
        if (!string.IsNullOrEmpty(texto))
        {
            var padrao = $"%{texto}%";
            var digitos = ValidadorDocumento.SoDigitos(texto);
            var padraoDigitos = $"%{digitos}%";
            var buscaNumero = digitos.Length >= 3;
            consulta = consulta.Where(c =>
                EF.Functions.Like(c.Nome, padrao) ||
                (c.RazaoSocial != null && EF.Functions.Like(c.RazaoSocial, padrao)) ||
                (c.Email != null && EF.Functions.Like(c.Email, padrao)) ||
                (c.Documento != null && (EF.Functions.Like(c.Documento, padrao) || (buscaNumero && EF.Functions.Like(c.Documento, padraoDigitos)))) ||
                (c.Telefone != null && (EF.Functions.Like(c.Telefone, padrao) || (buscaNumero && EF.Functions.Like(c.Telefone, padraoDigitos)))));
        }
        return await consulta.OrderBy(c => c.Nome).ToListAsync();
    }

    /// <summary>Vendas do cliente com os totais (as canceladas aparecem na lista mas não somam).</summary>
    public async Task<HistoricoCliente> HistoricoAsync(int clienteId)
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        var vendas = await db.Vendas.AsNoTracking().Include(v => v.Itens)
            .Where(v => v.ClienteId == clienteId)
            .OrderByDescending(v => v.DataHora).ThenByDescending(v => v.Id)
            .ToListAsync();

        var validas = vendas.Where(v => !v.Cancelada).ToList();
        return new HistoricoCliente(
            vendas,
            validas.Count,
            validas.Sum(v => v.TotalCentavos),
            validas.Where(v => v.FormaPagamento == FormaPagamento.APrazo).Sum(v => v.TotalCentavos),
            validas.Count == 0 ? null : validas.Max(v => v.DataHora));
    }

    /// <summary>A dívida a prazo do cliente: títulos (vencimento em 30 dias) já abatidos pelos pagamentos, do mais antigo ao mais novo.</summary>
    public async Task<ContaCliente> ContaAsync(int clienteId)
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        return await ContaAsync(db, clienteId);
    }

    internal static async Task<ContaCliente> ContaAsync(PdvDbContext db, int clienteId)
    {
        var vendas = await db.Vendas.AsNoTracking()
            .Where(v => v.ClienteId == clienteId && v.FormaPagamento == FormaPagamento.APrazo).ToListAsync();
        var pagamentos = await db.PagamentosCliente.AsNoTracking().Where(p => p.ClienteId == clienteId).ToListAsync();
        var ids = vendas.Select(v => v.Id).ToList();
        var ajustes = await db.AjustesTitulosPrazo.AsNoTracking().Where(a => ids.Contains(a.VendaId)).ToListAsync();
        return ContaCorrente.Calcular(vendas, pagamentos, ajustes);
    }

    /// <summary>Pagamentos recebidos do cliente, do mais recente para o mais antigo.</summary>
    public async Task<IReadOnlyList<PagamentoCliente>> PagamentosAsync(int clienteId)
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        var lista = await db.PagamentosCliente.AsNoTracking().Where(p => p.ClienteId == clienteId).ToListAsync();
        return lista.OrderByDescending(p => p.DataHora).ThenByDescending(p => p.Id).ToList();
    }

    /// <summary>Registra que o cliente pagou parte (ou toda) a dívida. Não pode passar do que ele deve.</summary>
    public async Task<PagamentoCliente> RegistrarPagamentoAsync(int clienteId, long valorCentavos, FormaPagamento forma, string? observacao = null)
    {
        if (valorCentavos <= 0)
            throw new RegraDeNegocioException("Informe um valor maior que zero.");
        if (forma == FormaPagamento.APrazo)
            throw new RegraDeNegocioException("Escolha como o cliente pagou: dinheiro, Pix, cartão ou cheque.");

        await using var db = await _fabrica.CreateDbContextAsync();
        if (!await db.Clientes.AnyAsync(c => c.Id == clienteId))
            throw new RegraDeNegocioException("Cliente não encontrado.");

        var conta = await ContaAsync(db, clienteId);
        if (valorCentavos > conta.SaldoDevedorCentavos)
            throw new RegraDeNegocioException(
                $"O cliente deve {Dinheiro.Formatar(conta.SaldoDevedorCentavos)}; o pagamento não pode ser maior.");

        var operador = await db.ConfiguracoesLoja.AsNoTracking().Where(c => c.Id == 1).Select(c => c.NomeOperador).FirstOrDefaultAsync();
        var pagamento = new PagamentoCliente
        {
            ClienteId = clienteId,
            DataHora = _agora(),
            ValorCentavos = valorCentavos,
            Forma = forma,
            Observacao = Limpar(observacao),
            Operador = !string.IsNullOrWhiteSpace(OperadorAtual.Nome) ? OperadorAtual.Nome : string.IsNullOrWhiteSpace(operador) ? "Caixa" : operador
        };
        db.PagamentosCliente.Add(pagamento);
        await db.SaveChangesAsync();

        // Pagou com cheque: o dinheiro só entra na conta quando o cheque for compensado.
        if (forma == FormaPagamento.Cheque)
        {
            var nome = await db.Clientes.AsNoTracking().Where(c => c.Id == clienteId).Select(c => c.Nome).FirstOrDefaultAsync();
            db.ChequesRecebidos.Add(ChequeService.NovoPendente(valorCentavos, pagamento.DataHora, nome, null, pagamento.Id));
            await db.SaveChangesAsync();
        }
        return pagamento;
    }

    private async Task DefinirAtivoAsync(int id, bool ativo)
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        var cliente = await db.Clientes.FindAsync(id)
            ?? throw new RegraDeNegocioException("Cliente não encontrado.");
        if (ativo && !cliente.Ativo && ValidadorDocumento.SoDigitos(cliente.Documento).Length > 0)
            await ExigirDocumentoLivreAsync(db, cliente.Documento, id, new DadosCliente(cliente.Nome, ExigirDocumento: true));
        cliente.Ativo = ativo;
        await db.SaveChangesAsync();
    }

    // ---------- crédito do cliente (saldo a favor) ----------

    /// <summary>
    /// Muda o crédito (saldo a favor) do cliente para o valor informado. Exige motivo (pelo menos 3 letras), grava o ajuste no
    /// histórico (de → para, quem, quando) e na trilha de atividades, tudo na mesma transação.
    /// </summary>
    public async Task<AjusteCreditoCliente> AjustarCreditoAsync(int clienteId, long novoSaldoCentavos, string motivo)
    {
        var texto = (motivo ?? "").Trim();
        if (texto.Length < 3) throw new RegraDeNegocioException("Informe o motivo do ajuste (pelo menos 3 letras).");
        if (texto.Length > 200) throw new RegraDeNegocioException("O motivo pode ter no máximo 200 caracteres.");
        if (novoSaldoCentavos < 0) throw new RegraDeNegocioException("O crédito do cliente não pode ficar negativo.");

        await using var db = await _fabrica.CreateDbContextAsync();
        await using var transacao = await db.Database.BeginTransactionAsync();
        var cliente = await db.Clientes.FindAsync(clienteId)
            ?? throw new RegraDeNegocioException("Cliente não encontrado.");
        if (cliente.CreditoCentavos == novoSaldoCentavos)
            throw new RegraDeNegocioException("Nada mudou: informe um saldo diferente do atual.");

        var ajuste = RegistrarAjusteDeCredito(db, cliente, novoSaldoCentavos, texto, _agora());
        await db.SaveChangesAsync();
        await transacao.CommitAsync();
        return ajuste;
    }

    /// <summary>Histórico do crédito do cliente, do mais recente para o mais antigo.</summary>
    public async Task<IReadOnlyList<AjusteDeCredito>> HistoricoCreditoAsync(int clienteId)
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        var lista = await db.AjustesCreditoCliente.AsNoTracking().Where(a => a.ClienteId == clienteId).ToListAsync();
        return lista.OrderByDescending(a => a.CriadoEm).ThenByDescending(a => a.Id)
            .Select(a => new AjusteDeCredito(a.SaldoAnteriorCentavos, a.SaldoNovoCentavos, a.Motivo, a.Operador, a.CriadoEm)).ToList();
    }

    /// <summary>Muda o saldo e cria o registro do ajuste e a atividade no contexto; quem chama grava (mesma transação).</summary>
    private static AjusteCreditoCliente RegistrarAjusteDeCredito(PdvDbContext db, Cliente cliente, long novoSaldo, string motivo, DateTime agora)
    {
        var ajuste = new AjusteCreditoCliente
        {
            Cliente = cliente,
            SaldoAnteriorCentavos = cliente.CreditoCentavos,
            SaldoNovoCentavos = novoSaldo,
            Motivo = motivo,
            Operador = string.IsNullOrWhiteSpace(OperadorAtual.Nome) ? null : OperadorAtual.Nome.Trim(),
            CriadoEm = agora
        };
        db.AjustesCreditoCliente.Add(ajuste);
        AtividadeService.Registrar(db, TipoAtividade.CreditoClienteAjustado,
            $"Ajustou o crédito de {cliente.Nome} ({Dinheiro.Formatar(cliente.CreditoCentavos)} → {Dinheiro.Formatar(novoSaldo)}): {motivo}",
            $"Cliente {cliente.Nome}", null, agora);
        cliente.CreditoCentavos = novoSaldo;
        return ajuste;
    }

    // ---------- importação e exportação ----------

    /// <summary>
    /// Lê o CSV de clientes como o banco está agora e devolve a pré-visualização (novos, atualizados, erros por linha).
    /// Não grava nada.
    /// </summary>
    public async Task<PreviaImportacaoClientes> PreverImportacaoAsync(string csv)
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        var existentes = await db.Clientes.AsNoTracking().ToListAsync();
        return ImportadorClientes.Analisar(csv, existentes);
    }

    /// <summary>
    /// Grava o CSV: linhas novas viram clientes, linhas que casam por CPF/CNPJ atualizam o cadastro, linhas com erro são
    /// ignoradas. Nunca apaga nem desativa ninguém. Tudo numa transação, com os ajustes de crédito no histórico e uma atividade.
    /// </summary>
    public async Task<PreviaImportacaoClientes> ImportarAsync(string csv)
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        await using var transacao = await db.Database.BeginTransactionAsync();
        var existentes = await db.Clientes.ToListAsync(); // rastreados: as atualizações saem no SaveChanges
        var previa = ImportadorClientes.Analisar(csv, existentes);
        if (previa.Erro is not null) throw new RegraDeNegocioException(previa.Erro);
        if (previa.Novos + previa.Atualizados == 0)
            throw new RegraDeNegocioException("Não há nada para gravar: nenhuma linha nova ou alterada.");

        var agora = _agora();
        var porId = existentes.ToDictionary(c => c.Id);
        foreach (var linha in previa.Linhas)
        {
            if (linha.Situacao is not (SituacaoLinhaImportacao.Novo or SituacaoLinhaImportacao.Atualizado)) continue;
            var d = linha.Dados!;
            Cliente cliente;
            if (linha.Situacao == SituacaoLinhaImportacao.Novo)
            {
                cliente = new Cliente { DataCadastro = agora, Ativo = true };
                db.Clientes.Add(cliente);
            }
            else cliente = porId[linha.ClienteId!.Value];

            cliente.Nome = d.Nome;
            cliente.Documento = d.Documento;
            if (d.RazaoSocial is not null) cliente.RazaoSocial = d.RazaoSocial;
            if (d.Telefone is not null) cliente.Telefone = d.Telefone;
            if (d.Email is not null) cliente.Email = d.Email;
            if (d.Observacao is not null) cliente.Observacao = d.Observacao;
            if (d.LimiteCreditoCentavos is long limite) cliente.LimiteCreditoCentavos = limite;
            if (d.CreditoCentavos is long credito && credito != cliente.CreditoCentavos)
                RegistrarAjusteDeCredito(db, cliente, credito, "Importação de clientes (planilha)", agora);
        }

        AtividadeService.Registrar(db, TipoAtividade.ClientesImportados,
            $"Importou clientes de uma planilha: {previa.Novos} novo(s), {previa.Atualizados} atualizado(s), {previa.Erros} linha(s) com erro ignorada(s)",
            "Clientes", null, agora);
        await db.SaveChangesAsync();
        await transacao.CommitAsync();
        return previa;
    }

    /// <summary>Todos os clientes (inclusive os desativados) para o CSV, em ordem de nome.</summary>
    public async Task<List<Cliente>> ListarParaExportacaoAsync()
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        return await db.Clientes.AsNoTracking().OrderBy(c => c.Nome).ThenBy(c => c.Id).ToListAsync();
    }

    // ---------- regras do cadastro ----------

    private static async Task ExigirDocumentoLivreAsync(PdvDbContext db, string? documento, int? ignorarId, DadosCliente dados)
    {
        if (!dados.ExigirDocumento) return;
        var digitos = ValidadorDocumento.SoDigitos(documento);
        var candidatos = await db.Clientes.AsNoTracking()
            .Where(c => c.Ativo && c.Documento != null && (ignorarId == null || c.Id != ignorarId))
            .Select(c => new { c.Nome, c.Documento }).ToListAsync();
        var dono = candidatos.FirstOrDefault(c => ValidadorDocumento.SoDigitos(c.Documento) == digitos);
        if (dono is not null)
            throw new RegraDeNegocioException(
                $"O {(digitos.Length == 14 ? "CNPJ" : "CPF")} {ValidadorDocumento.Formatar(digitos)} já está cadastrado para {dono.Nome}.");
    }

    private static void Validar(DadosCliente dados)
    {
        if (string.IsNullOrWhiteSpace(dados.Nome))
            throw new RegraDeNegocioException("Informe o nome do cliente.");
        if (dados.LimiteCreditoCentavos < 0)
            throw new RegraDeNegocioException("O limite de crédito não pode ser negativo.");
        if (dados.ExigirDocumento)
        {
            var doc = ValidadorDocumento.Validar(dados.Documento);
            if (!doc.Valido) throw new RegraDeNegocioException(doc.Erro!);
        }
        if (!string.IsNullOrWhiteSpace(dados.Email) && !ImportadorClientes.EmailValido(dados.Email))
            throw new RegraDeNegocioException("E-mail inválido. Digite algo como nome@empresa.com.br.");
    }

    private static void Preencher(Cliente cliente, DadosCliente dados)
    {
        cliente.Nome = dados.Nome.Trim();
        cliente.RazaoSocial = Limpar(dados.RazaoSocial);
        cliente.Documento = dados.ExigirDocumento ? ValidadorDocumento.SoDigitos(dados.Documento) : Limpar(dados.Documento);
        cliente.Telefone = Limpar(dados.Telefone);
        cliente.Email = Limpar(dados.Email);
        cliente.Observacao = Limpar(dados.Observacao);
        cliente.LimiteCreditoCentavos = dados.LimiteCreditoCentavos;
    }

    private static string? Limpar(string? texto) => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();
}
