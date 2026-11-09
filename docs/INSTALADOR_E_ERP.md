# Instalador do Nexus (Nexus-Setup.exe) e ligação com o ERP Life

## O que é
`Nexus-Setup.exe` é o instalador de um arquivo só. Ele **baixa a última versão publicada** (NexusLS.exe, atualizador.exe e NexusBarTender.exe), confere o SHA-256 de cada um, instala em `%LOCALAPPDATA%\Programs\Nexus` (não pede administrador), cria os atalhos (Área de Trabalho e menu Iniciar), grava a conexão com o ERP (`%LOCALAPPDATA%\NexusLS\erp.json`, só se ainda não existir) e registra a desinstalação em *Configurações > Aplicativos*.

Link fixo para baixar sempre a última versão:

    https://github.com/Lukkzz11/Nexus-LS---Update/releases/latest/download/Nexus-Setup.exe

## Parâmetros
| Parâmetro | Efeito |
|---|---|
| `/S` | instala em silêncio (sem janela) |
| `--empresa=ID` | empresa do ERP gravada no erp.json (padrão `lifesurf-principal`) |
| `--pasta=CAMINHO` | pasta de instalação |
| `--desinstalar` | remove os programas (os dados em `%LOCALAPPDATA%\NexusLS` ficam) |

Exemplo: `Nexus-Setup.exe /S --empresa=lifesurf-principal`

## Ligar à tela "Trocar empresa" do ERP Life
O ERP é um site: o instalador não pode ser "executado" por ele, mas a tela pode oferecer o **download** (o navegador baixa o .exe). Como a troca de empresa já sabe o id da empresa escolhida, o botão pode apontar para o link fixo acima. Trecho para colocar na tela (React):

```jsx
const SETUP_NEXUS = 'https://github.com/Lukkzz11/Nexus-LS---Update/releases/latest/download/Nexus-Setup.exe';

<a href={SETUP_NEXUS} className="..." download>
  Instalar o Nexus neste computador
</a>
```

O ERP é só leitura para o Nexus (regra do projeto), por isso esse trecho **não foi aplicado lá**: cole-o na tela de troca de empresa quando quiser.
Se o computador for de outra empresa, depois de instalar rode o `Nexus-Setup.exe --empresa=ID` (ou edite o `EmpresaId` no erp.json).

## Gerar e publicar
`publicar-tudo.ps1 -Versao X.Y.Z -Notas "..."` compila o PDV, o Atualizador, o BarTender e o instalador, e publica tudo no release (o License Manager não entra).
