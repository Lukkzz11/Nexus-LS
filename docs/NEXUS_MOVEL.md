# Nexus Móvel — consulta de produtos pelo celular

Fase 1 do "Nexus móvel": o celular da loja consulta o mesmo banco do PDV, pelo Wi-Fi,
sem internet. Serve para responder rápido no salão: *quanto custa, tem em estoque, em quais
tamanhos?*

## Como funciona

São duas peças, e nenhuma delas é um aplicativo para instalar na loja de aplicativos:

1. **`NexusMovel.exe`** — um servidor local que roda no computador da loja (Kestrel, porta
   `5123` por padrão). Ele lê o banco SQLite do Nexus e responde na rede local. **Não grava
   nada**: é somente leitura.
2. **A tela do celular** — HTML/CSS/JS embutido dentro do próprio executável e aberto no
   navegador do aparelho (`http://<ip-do-pc>:5123`). Dá para "Adicionar à tela de início" e
   usar como um aplicativo.

A tela vai embutida no executável (recursos do assembly), então não existe pasta de site
para perder, copiar errado ou ficar desatualizada. Nada sai para a internet: o celular fala
direto com o computador da loja.

## Como usar

No computador da loja (o mesmo que roda o Nexus):

```
NexusMovel.exe
```

A janela mostra os endereços e o PIN:

```
  NEXUS MOVEL  1.2.8
  ---------------------------------------------------------
  Banco: C:\Users\...\AppData\Local\NexusLS\nexus.db

  No celular (na mesma rede Wi-Fi), abra:
    http://192.168.0.4:5123

  PIN: 481902
```

No celular, digite o endereço no navegador e informe o PIN uma vez. O aparelho guarda o PIN
e entra sozinho nas próximas vezes. O botão **Sair** apaga o PIN do celular.

### Opções

| Opção | Para que serve |
|---|---|
| `--banco <arquivo>` | Usa outro banco (padrão: `%LOCALAPPDATA%\NexusLS\nexus.db`). |
| `--porta <número>` | Muda a porta (padrão `5123`). |
| `--pin <6 dígitos>` | Define um PIN fixo para o celular. |
| `--novo-pin` | Gera um PIN novo; os celulares já conectados pedem de novo. |
| `--ajuda` | Mostra a lista de opções. |

### Firewall

Na primeira vez, o Windows pode perguntar se libera o `NexusMovel.exe`. Se o celular não
abrir a página, libere a porta (PowerShell **como administrador**):

```powershell
netsh advfirewall firewall add rule name="Nexus Movel" dir=in action=allow protocol=TCP localport=5123
```

Confira também se o celular está no **mesmo Wi-Fi** do computador (rede de convidados
costuma isolar os aparelhos).

## O que dá para consultar

A ordem é a mesma do PDV — o celular nunca discorda do caixa:

1. **Código do tamanho** (referência + número): `10192` abre a camisa 1019 já no tamanho M.
2. **Referência** exata: `1402` abre a bermuda com a grade inteira.
3. **Código de barras / código interno** exato (leitor USB/Bluetooth ou digitado).
4. **Nome (ou parte dele)**: um resultado abre o produto; vários viram sugestões para tocar.

Na tela aparecem foto (quando existe), preço de varejo, atacado (quando diferente), estoque
total, selo de estoque baixo/esgotado e a grade de tamanhos com o estoque de cada um. Tocar
num tamanho mostra o código que a etiqueta imprime, com botão **Copiar**.

Há também um histórico curto das últimas consultas **no próprio celular** (não no banco).

## Câmera

A câmera tem dois caminhos, porque navegador não entrega câmera ao vivo em página HTTP fora
de `localhost`:

- **Leitura ao vivo** (`BarcodeDetector` do navegador): só funciona quando a página é
  considerada segura — acesso por `localhost` ou por HTTPS.
- **Foto** (o caminho normal no Wi-Fi, por HTTP): o botão abre a câmera do sistema pelo
  `<input capture>`, tira a foto e o código é decodificado no próprio aparelho com o
  **ZXing** (biblioteca incluída em `wwwroot/vendor/`, Apache-2.0, licença em
  `LICENSE-zxing.txt`).

Para leitura ao vivo no celular seria preciso HTTPS com certificado aceito pelo aparelho —
fica para uma fase seguinte. Enquanto isso, leitor Bluetooth/USB no campo de código
funciona sempre, e é o caminho mais rápido no balcão.

## Segurança

- **Somente leitura**: o servidor não cria, não altera e não apaga nada. Nem a configuração
  padrão da loja ele cria (lê a tabela direto).
- **PIN de 6 dígitos**: comparado em tempo constante. Depois de 8 erros do mesmo aparelho,
  aquele IP fica bloqueado por 60 segundos.
- O PIN fica em `%LOCALAPPDATA%\NexusLS\movel.json` (junto com a porta). Nada de senha em
  texto no repositório.
- A API não é guardada em cache (`no-store`) e a tela usa CSP restritiva; o servidor não
  anuncia a versão do Kestrel no cabeçalho.
- O caminho da foto é resolvido com `Path.GetFileName`, então um nome estranho no banco não
  consegue apontar para fora da pasta de fotos.

O PIN não substitui a segurança da rede: ele evita que qualquer pessoa na mesma rede veja
preços e estoque, mas quem tem o PIN vê o que o celular vê. Para uso na loja é o suficiente.

## Testes

```
dotnet test tests\NexusLS.Movel.Tests\NexusLS.Movel.Tests.csproj -c Release
```

A suíte cobre a regra de consulta (código de tamanho, referência, código de barras, nome,
produto desativado, preço de atacado) e o servidor HTTP de verdade (401 sem PIN, PIN errado,
saúde com o nome da loja, produto com tamanho marcado, sugestões, tela e assets embutidos,
foto ausente). Roda no CI junto com as outras suítes.

## Publicação

O `NexusMovel.exe` entra no `publicar-tudo.ps1` junto com o PDV, o atualizador, o BarTender e
o instalador: a release passa a trazer o executável e o `.sha256` dele. Para gerar sozinho,
na raiz:

```powershell
dotnet publish src/NexusLS.Movel -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publicar_movel
```

O executável sai em `publicar_movel\NexusMovel.exe` e não precisa do .NET instalado no
computador da loja. A versão do projeto fica no `NexusLS.Movel.csproj`; no release, o
`publicar-tudo.ps1` grava a versão pedida dentro do executável.

## Limitações conhecidas (fase 1)

- Só consulta. Não dá baixa, não reserva, não vende.
- A tela abre no navegador; não é um app nativo. "Adicionar à tela de início" resolve o
  visual, mas o ícone só fica completo com HTTPS (o manifest é lido, o service worker não
  registra em HTTP).
- Leitura ao vivo da câmera só em `localhost`/HTTPS (veja acima).
- O servidor precisa estar aberto no computador da loja. Se ele fecha, o celular mostra
  "sem resposta".

## Próximas fases (ideias, não compromisso)

1. **HTTPS local** com certificado confiável no celular, para câmera ao vivo e instalação
   completa como PWA.
2. **Consultar preço e etiqueta**: reimprimir etiqueta do tamanho pelo celular.
3. **Conferência de estoque**: contar peças e comparar com o sistema (aí sim gravando, com
   trilha de atividade e permissão própria).
4. **Embutir o servidor no Nexus**: abrir e fechar junto com o PDV, sem segunda janela.
