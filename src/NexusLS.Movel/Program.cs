using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using Microsoft.EntityFrameworkCore;
using NexusLS.Core.Atualizacao;
using NexusLS.Core.Data;
using NexusLS.Core.Diagnostico;
using NexusLS.Movel;

Console.OutputEncoding = Encoding.UTF8;

var banco = (string?)null;
var pinInformado = (string?)null;
var porta = 0;
var novoPin = false;

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--banco":
            banco = i + 1 < args.Length ? args[++i] : null;
            break;
        case "--porta":
            _ = int.TryParse(i + 1 < args.Length ? args[++i] : "", out porta);
            break;
        case "--pin":
            pinInformado = i + 1 < args.Length ? args[++i] : null;
            break;
        case "--novo-pin":
            novoPin = true;
            break;
        case "--ajuda":
        case "-h":
        case "/?":
            Ajuda();
            return 0;
    }
}

banco ??= CaminhoBanco.Padrao();

if (!File.Exists(banco))
{
    Console.WriteLine("Nao encontrei o banco do Nexus.");
    Console.WriteLine($"  Procurado em: {banco}");
    Console.WriteLine();
    Console.WriteLine("Abra o Nexus LS pelo menos uma vez neste computador e rode de novo,");
    Console.WriteLine("ou informe o arquivo com:  NexusMovel.exe --banco \"C:\\caminho\\nexus.db\"");
    return 2;
}

// Ajustes do SQLite (WAL, espera e chaves estrangeiras) antes de qualquer leitura.
try
{
    PreparadorBanco.Aplicar(banco);
}
catch (Exception ex)
{
    RegistroDeLog.Aviso($"Nexus Movel: nao foi possivel preparar o banco ({ex.Message}). Seguindo so para leitura.");
}

var preferencias = PinMovel.CarregarOuCriar();

if (novoPin)
{
    preferencias.Pin = PinMovel.Gerar();
    PinMovel.Salvar(preferencias);
}

if (PinMovel.PinValido(pinInformado))
{
    preferencias.Pin = pinInformado!;
    PinMovel.Salvar(preferencias);
}

if (porta is <= 0 or > 65535)
{
    porta = preferencias.Porta is > 0 and <= 65535 ? preferencias.Porta : ServidorMovel.PortaPadrao;
}

var opcoes = new DbContextOptionsBuilder<PdvDbContext>()
    .UseSqlite(CaminhoBanco.StringConexao(banco))
    .Options;

var fabrica = new FabricaContexto(opcoes);

await using var servidor = await ServidorMovel.IniciarAsync(fabrica, preferencias.Pin, porta);

var enderecos = EnderecosLocais().ToList();

Console.WriteLine();
Console.WriteLine($"  NEXUS MOVEL  {VersaoPrograma.Atual()}");
Console.WriteLine("  ---------------------------------------------------------");
Console.WriteLine($"  Banco: {banco}");
Console.WriteLine();
Console.WriteLine("  No celular (na mesma rede Wi-Fi), abra:");
if (enderecos.Count == 0)
{
    Console.WriteLine($"    http://localhost:{porta}");
    Console.WriteLine("    (nenhum endereco de rede encontrado; confira o Wi-Fi)");
}
else
{
    foreach (var ip in enderecos)
    {
        Console.WriteLine($"    http://{ip}:{porta}");
    }
}
Console.WriteLine();
Console.WriteLine($"  PIN: {preferencias.Pin}");
Console.WriteLine();
Console.WriteLine("  A tela nao fica na internet: o celular fala direto com este computador.");
Console.WriteLine("  Se o celular nao abrir, libere a porta no Firewall do Windows:");
Console.WriteLine($"    netsh advfirewall firewall add rule name=\"Nexus Movel\" dir=in action=allow protocol=TCP localport={porta}");
Console.WriteLine();
Console.WriteLine("  Ctrl+C encerra.");
Console.WriteLine();

RegistroDeLog.Info($"Nexus Movel iniciado: banco {banco}, porta {porta}.");

await servidor.AguardarEncerramentoAsync();
RegistroDeLog.Info("Nexus Movel encerrado.");
return 0;

static IEnumerable<string> EnderecosLocais()
{
    foreach (var rede in NetworkInterface.GetAllNetworkInterfaces())
    {
        if (rede.OperationalStatus != OperationalStatus.Up) continue;
        if (rede.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;

        foreach (var endereco in rede.GetIPProperties().UnicastAddresses)
        {
            if (endereco.Address.AddressFamily != AddressFamily.InterNetwork) continue;
            if (IPAddress.IsLoopback(endereco.Address)) continue;

            var ip = endereco.Address.ToString();
            if (ip.StartsWith("169.254.", StringComparison.Ordinal)) continue; // APIPA: sem rede de verdade

            yield return ip;
        }
    }
}

static void Ajuda()
{
    Console.WriteLine("Nexus Movel - consulta de produtos no celular pelo Wi-Fi da loja.");
    Console.WriteLine();
    Console.WriteLine("  NexusMovel.exe [opcoes]");
    Console.WriteLine();
    Console.WriteLine("  --banco <arquivo>   banco do Nexus (padrao: %LOCALAPPDATA%\\NexusLS\\nexus.db)");
    Console.WriteLine("  --porta <numero>    porta do servidor (padrao: 5123)");
    Console.WriteLine("  --pin <6 digitos>   define o PIN de acesso do celular");
    Console.WriteLine("  --novo-pin          gera um PIN novo (o celular pergunta de novo)");
    Console.WriteLine("  --ajuda             mostra esta ajuda");
}
