using System.Security.Cryptography;
using System.Text;
using NexusLS.Core.Sincronizacao.Firebase;

namespace NexusLS.LojaOnline;

/// <summary>
/// Lê os segredos do Firebase guardados pelo PDV com o DPAPI. Usa o mesmo "sal" do PDV de propósito:
/// o arquivo é o mesmo e só o MESMO usuário do Windows, neste computador, consegue abri-lo.
/// </summary>
public class ProtetorDpapi : IProtetorSegredo
{
    private static readonly byte[] Entropia = Encoding.UTF8.GetBytes("NexusLS.Firebase.v1");

    public string Proteger(string textoClaro) =>
        Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(textoClaro), Entropia, DataProtectionScope.CurrentUser));

    public string? Desproteger(string textoProtegido)
    {
        try
        {
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(textoProtegido), Entropia, DataProtectionScope.CurrentUser));
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            return null;
        }
    }
}
