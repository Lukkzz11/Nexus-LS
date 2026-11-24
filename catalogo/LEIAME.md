# Catálogo de apps do Nexus Hub

`apps.json` é a lista da página **Mais apps** do Hub. O `publicar-tudo.ps1` envia o arquivo na release como
`catalogo-apps.json`, junto com os zips citados nele. App novo **não precisa de mudança no código do Hub**.

## Adicionar um app

1. Compacte a pasta do programa em um `.zip` com o `.exe` **na raiz** e coloque em `catalogo/pacotes/` (fora do git).
2. Acrescente em `apps.json`:

```json
{
  "apps": [
    {
      "id": "meu-app",
      "nome": "Meu App",
      "subtitulo": "Uma linha",
      "descricao": "Duas ou três linhas sobre o que ele faz.",
      "cor": "#0067B8",
      "versao": "1.0.0",
      "pacote": "MeuApp.zip",
      "exe": "Meu App.exe"
    },
    { "id": "meu-sistema-web", "nome": "Meu Sistema", "url": "https://..." }
  ]
}
```

3. Publique uma release. Para atualizar um app, troque o zip e suba a `versao`.

Cuidados: o repositório das releases é **público** (qualquer pessoa pode baixar o zip, então nunca ponha senha, chave
ou dado de cliente dentro dele). Toda release precisa levar todos os zips do catálogo, porque o Hub olha só a mais
recente.
