using System.Security.Cryptography;
using System.Text;
using NexusLS.Core.Sincronizacao.Firebase;

namespace NexusLS.App.Seguranca;

/// <summary>
/// Guarda segredos com o DPAPI do Windows: só o MESMO usuário do Windows, neste computador, consegue ler.
/// Copiar o arquivo para outro computador (ou outro usuário) não revela a senha.
/// </summary>
public class ProtetorDpapi : IProtetorSegredo
{
    // "Sal" fixo do programa: impede que outro programa do mesmo usuário leia o segredo por engano.
    private static readonly byte[] Entropia = Encoding.UTF8.GetBytes("NexusLS.Firebase.v1");

    public string Proteger(string textoClaro)
    {
        var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(textoClaro), Entropia, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(bytes);
    }

    public string? Desproteger(string textoProtegido)
    {
        try
        {
            var bytes = ProtectedData.Unprotect(Convert.FromBase64String(textoProtegido), Entropia, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            return null; // arquivo vindo de outro computador/usuário: trata como "sem senha", e a tela pede de novo
        }
    }
}
