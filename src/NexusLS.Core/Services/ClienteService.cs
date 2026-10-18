using Microsoft.EntityFrameworkCore;
using NexusLS.Core.Data;
using NexusLS.Core.Models;

namespace NexusLS.Core.Services;

public record DadosCliente(string Nome, string? Documento = null, string? Telefone = null, string? Observacao = null);

public class ClienteService
{
    private readonly IDbContextFactory<PdvDbContext> _fabrica;
    private readonly Func<DateTime> _agora;

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
        await db.SaveChangesAsync();
    }

    /// <summary>Desativar não apaga: o cliente some da busca, mas continua nas vendas antigas.</summary>
    public Task DesativarAsync(int id) => DefinirAtivoAsync(id, false);

    public Task ReativarAsync(int id) => DefinirAtivoAsync(id, true);

    /// <summary>Busca por parte do nome, documento ou telefone. Sem texto, lista todos.</summary>
    public async Task<List<Cliente>> BuscarAsync(string? texto, bool incluirInativos = false)
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        IQueryable<Cliente> consulta = db.Clientes.AsNoTracking();
        if (!incluirInativos) consulta = consulta.Where(c => c.Ativo);

        texto = texto?.Trim();
        if (!string.IsNullOrEmpty(texto))
        {
            var padrao = $"%{texto}%";
            consulta = consulta.Where(c =>
                EF.Functions.Like(c.Nome, padrao) ||
                (c.Documento != null && EF.Functions.Like(c.Documento, padrao)) ||
                (c.Telefone != null && EF.Functions.Like(c.Telefone, padrao)));
        }
        return await consulta.OrderBy(c => c.Nome).ToListAsync();
    }

    private async Task DefinirAtivoAsync(int id, bool ativo)
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        var cliente = await db.Clientes.FindAsync(id)
            ?? throw new RegraDeNegocioException("Cliente não encontrado.");
        cliente.Ativo = ativo;
        await db.SaveChangesAsync();
    }

    private static void Validar(DadosCliente dados)
    {
        if (string.IsNullOrWhiteSpace(dados.Nome))
            throw new RegraDeNegocioException("Informe o nome do cliente.");
    }

    private static void Preencher(Cliente cliente, DadosCliente dados)
    {
        cliente.Nome = dados.Nome.Trim();
        cliente.Documento = Limpar(dados.Documento);
        cliente.Telefone = Limpar(dados.Telefone);
        cliente.Observacao = Limpar(dados.Observacao);
    }

    private static string? Limpar(string? texto) => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();
}
