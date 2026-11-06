# Licenças no Firebase (projeto `nexus-ls`)

O **Nexus License Manager** (site em `license-manager/`, aberto também pelo app `src/NexusLS.LicenseManager`) cadastra clientes e gera licenças no **Firestore**. O **Atualizador** valida a chave do cliente e o **Nexus** mostra a situação.

## Fluxo
1. No License Manager (login de administrador): cadastrar cliente → gerar licença (`NEXUS-XXXX-XXXX-XXXX`, plano, validade, limite de computadores).
2. O cliente abre o **Nexus Atualizador** → aba Licença → digita a chave → "Ativar / verificar licença".
3. O Atualizador tenta primeiro a Cloud Function `validateLicense` (região southamerica-east1: confere status, validade e limite de computadores, registra o dispositivo). **Se a função não estiver publicada ou estiver fora do ar, lê o documento direto no Firestore** (`FonteLicencaComReserva`). A chave é aceita em minúsculas.
4. Resultado salvo em `%LOCALAPPDATA%\NexusLS\licenca-nuvem.json`. Ao abrir, o Atualizador reconfere online; **sem internet vale a última verificação por até 7 dias**; aviso com ≤ 15 dias.
5. O Nexus mostra "Licença de {cliente}, válida até {data}" no menu lateral (lê o cache; não bloqueia o uso). Depois de ativar, reabra o Nexus.

## Documento `licencas/{chave}` (ID = chave)
Campos que o site grava e o app lê: `cliente`, `validade` (`yyyy-MM-dd`), `ativa`, `maquina` (`""` = livre; o app grava o código do computador na 1ª ativação; `"*"` = qualquer), `plano`. O site grava também `status` (PENDENTE/ATIVA/BLOQUEADA/CANCELADA/VENCIDA), `dataVencimento`, `limiteDispositivos`, `clienteId`, `hashChave` etc. Bloquear no site = `ativa=false`; renovar = nova `validade`.

## Regras e administradores
As regras ficam em `license-manager/firestore.rules` e **precisam ser publicadas** (Console do Firebase → Firestore → Regras, ou `firebase deploy --only firestore:rules --project nexus-ls` com `firebase login` de uma conta com permissão no projeto). A lista de administradores está dentro das regras (`ehAdmin`); hoje: `nexuslsatulizador@gmail.com` e `jarbasantonio201@gmail.com`. O app do cliente não faz login: usa só a Web API Key (pública) e as regras permitem apenas `get` da licença e preencher `maquina` quando vazio.

## Pendente
- Publicar a Cloud Function: `firebase deploy --only functions` (plano Blaze). Sem ela, o limite de computadores não é aplicado e `dispositivos` não é preenchido.
- Nunca coloque service account no Atualizador nem no Nexus.
