// Gera o arquivo de licença de um cliente. SÓ para o fornecedor: usa a chave PRIVADA, que nunca vai no programa.
//
// Uso (na pasta ferramentas):
//   dotnet fsi gerar-licenca.fsx "Loja Surf" 2027-12-31
//   dotnet fsi gerar-licenca.fsx "Loja Surf" 2027-12-31 A1B2C3D4E5F6     (presa a um computador; o código aparece em Configurações > Licença, no balão do botão)
//
// A chave privada fica em %USERPROFILE%\NexusLS_chaves\licenca-privada.txt. Guarde uma cópia em lugar seguro:
// sem ela não dá para emitir novas licenças, e quem a tiver emite licenças como se fosse você.
open System
open System.IO
open System.Security.Cryptography
open System.Text
open System.Text.Json

let args = fsi.CommandLineArgs |> Array.skip 1
if args.Length < 2 then
    printfn "Uso: dotnet fsi gerar-licenca.fsx \"Nome do cliente\" aaaa-MM-dd [codigo-do-computador]"
    exit 1

let cliente = args.[0].Trim()
let validade = DateTime.ParseExact(args.[1], "yyyy-MM-dd", Globalization.CultureInfo.InvariantCulture).ToString("yyyy-MM-dd")
let maquina = if args.Length > 2 && args.[2].Trim() <> "" then args.[2].Trim() else "*"

let caminhoChave = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "NexusLS_chaves", "licenca-privada.txt")
let privada = File.ReadAllText(caminhoChave).Trim()

use ec = ECDsa.Create()
ec.ImportPkcs8PrivateKey(Convert.FromBase64String privada, ref 0)

// O texto assinado tem de ser IGUAL ao de ArquivoLicenca.TextoAssinado() no programa.
let texto = sprintf "NEXUSLS|%s|%s|%s" cliente validade maquina
let assinatura = Convert.ToBase64String(ec.SignData(Encoding.UTF8.GetBytes texto, HashAlgorithmName.SHA256))

let json =
    JsonSerializer.Serialize(
        dict [ "Cliente", cliente; "Validade", validade; "Maquina", maquina; "Assinatura", assinatura ],
        JsonSerializerOptions(WriteIndented = true))

let nomeArquivo = sprintf "licenca-%s-%s.json" (String(cliente |> Seq.filter Char.IsLetterOrDigit |> Seq.toArray)) validade
File.WriteAllText(nomeArquivo, json)
printfn "Licença criada: %s (cliente %s, válida até %s, computador %s)" (Path.GetFullPath nomeArquivo) cliente validade maquina
