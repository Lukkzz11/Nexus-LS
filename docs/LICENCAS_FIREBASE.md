# Licenças no Firebase (projeto `nexus-ls`)

O site de licenças (feito no Antigravity) grava no **Firestore**; o **Atualizador** do Nexus lê a licença do cliente e o **Nexus** mostra a situação.

## Coleção `licencas`, um documento por cliente
O **ID do documento é a chave da licença** que o cliente digita no Atualizador. Use uma chave longa e aleatória (ex.: `NX-7K2M-93QD-XP4L`; só letras, números e hífens, 6 a 64 caracteres), porque quem tem a chave consegue ler a licença.

| Campo | Tipo | Exemplo | Obrigatório |
|---|---|---|---|
| `cliente` | string | `Loja Sol Ltda` | sim (nome mostrado no Nexus) |
| `validade` | string `yyyy-MM-dd` (ou timestamp) | `2027-05-01` | sim |
| `ativa` | boolean | `true` | sim (`false` bloqueia na hora) |
| `maquina` | string | `""` | deixe **vazio**: o Atualizador grava o código do computador na primeira ativação. `"*"` = qualquer computador |
| `plano` | string | `anual` | não |
| `criadaEm` | timestamp | | não |

Para **trocar de computador**: no site, apague o valor de `maquina` (deixe `""`). Para **renovar**: mude `validade`. Para **bloquear**: `ativa = false`.

## Como o Atualizador/Nexus decide
1. Cliente digita a chave → leitura de `licencas/{chave}`.
2. Bloqueada (`ativa=false`) → não ativa. Outra máquina → recusa. Vencida → recusa. Vencendo (≤15 dias) → ativa com aviso.
3. Primeira vez: grava `maquina` (campo único que o app consegue escrever).
4. Guarda o resultado em `%LOCALAPPDATA%\NexusLS\licenca-nuvem.json`. Ao abrir, o Atualizador reconfere online; **sem internet vale a última verificação por até 7 dias**.
5. O Nexus mostra "Licença de {cliente}, válida até {data}" no menu lateral (ainda sem bloquear o uso).

## Regras de segurança do Firestore (cole em Firebase → Firestore → Regras)
O app não faz login: usa só a Web API Key pública. Por isso as regras é que protegem.

```
rules_version = '2';
service cloud.firestore {
  match /databases/{database}/documents {
    match /licencas/{chave} {
      // ler um documento pela chave: sim; listar todos: nunca
      allow get: if true;
      allow list: if false;
      // o app só pode preencher "maquina" quando ainda estiver vazio
      allow update: if request.resource.data.diff(resource.data).affectedKeys().hasOnly(['maquina'])
                    && (!('maquina' in resource.data) || resource.data.maquina == '');
      // criar/editar/apagar: só o administrador logado no site
      allow create, delete: if request.auth != null && request.auth.token.admin == true;
    }
    match /{outro=**} { allow read, write: if false; }
  }
}
```
Se o seu site de administração usa outra forma de login, ajuste a condição de `create`/`delete` e também permita `update` completo para o administrador (`|| request.auth.token.admin == true`).

## Chave de administrador
Nunca coloque service account no Atualizador nem no Nexus. A Web API Key (`AIzaSy…`) é pública por natureza.
