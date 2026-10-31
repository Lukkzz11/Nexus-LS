# Guia do teste real do Nexus LS com o Firebase

Tudo o que o Nexus faz com a nuvem foi testado só com a rede simulada. Este guia valida cada parte com o Firebase de verdade,
do menos arriscado para o mais arriscado. **Pare no primeiro passo que falhar** e me mande o que apareceu (veja o item 6).

Antes de começar: abra o Nexus (`C:\NexusLS\publicar\NexusLS.exe`) e faça um **backup** pela tela de Backup. Se algo sair errado,
o banco local volta ao que era.

---

## 1. Importar do ERP (só leitura — comece por aqui)

Não grava nada no ERP, então não há risco para os dados dele.

**Onde pegar os dados do ERP**
- Id do projeto e chave da API: no `.env` do ERP (variáveis que começam com `VITE_FIREBASE_`) ou no Console do Firebase,
  em Configurações do projeto > Geral. A chave é a "Web API Key".
- Empresa: a que você usa no ERP, normalmente `lifesurf-principal`.
- Usuário: o mesmo e-mail e senha com que você entra no ERP.

**Passos**
1. Nexus > **Catálogo online** > seção **Importar do ERP**.
2. Preencha projeto, chave e empresa, clique em **Salvar**.
3. Clique em **Importar do ERP**. Na janela, entre com o e-mail e a senha do ERP.
4. Acompanhe o andamento na tela. No fim aparece quantos documentos foram lidos e o que veio de cada coleção.

**Conferir (de 5 a 10 itens de cada, pelo menos)**
- Produtos: nome, preço de varejo e atacado, grade de tamanhos e estoque iguais aos do ERP.
- Clientes: nome, telefone, limite de crédito.
- Vendas: data, total, forma de pagamento, itens, cliente. Uma venda cancelada no ERP deve aparecer cancelada.
- Dívida de 2 ou 3 clientes que devem a prazo: compare o saldo do Nexus com o `saldoDevedor` do ERP.
  Se o cliente tem "saldo anterior" no ERP, o Nexus mostrará menos (isso é esperado; veja Limites).
- O estoque dos produtos **não** pode ter diminuído por causa das vendas importadas.

**Teste de economia de leituras**
1. No Console do Firebase do ERP, abra Firestore > **Uso** e anote as leituras do dia.
2. Clique em **Importar do ERP** de novo, sem ter mudado nada no ERP.
3. Esperado: "Nada mudou no ERP desde a última importação" e 0 a poucas leituras a mais.
4. Altere um preço no ERP, importe de novo: só esse produto deve aparecer como atualizado.

A primeira importação lê cada documento uma vez (produtos + clientes + turnos + vendas + títulos). Se o ERP tem muitos
milhares de vendas, isso consome parte da cota gratuita do dia (50 mil leituras no plano Spark). Faça a primeira
importação num dia em que o ERP não está sendo muito usado.

**Se falhar**
- "Permissão negada": as regras do ERP não deixam esse usuário ler `empresas/{empresa}/...`. Use um usuário que consegue ver essas telas no ERP.
- "Chave da API inválida": copiou a chave de outro projeto.
- Nada importado em alguma coleção: me diga qual. Pode ser que os documentos de lá não tenham o campo `atualizadoEm`.

---

## 2. Nuvem operacional (projeto B) — vendas, caixa e financeiro

Este é um projeto Firebase **novo e privado**, separado do ERP e do catálogo.

**Criar o projeto B**
1. Console do Firebase > Adicionar projeto (ex.: `nexus-ls-minhaloja`).
2. Authentication > Sign-in method > ative **E-mail/senha** e crie um usuário para cada pessoa que usa o programa.
3. Firestore Database > crie o banco em **modo de produção**.
4. Firestore > Regras: cole o conteúdo de `C:\NexusLS\firebase\firestore.operacional.rules`, **troque os e-mails** pelos dos
   usuários do passo 2 e publique.
5. No Console, em Regras > **Simulador de regras** (as regras nunca rodaram num emulador):
   - leitura em `/lojas/minha-loja/vendas/x` autenticado como um e-mail da lista: deve **permitir**;
   - o mesmo com um e-mail fora da lista, ou sem login: deve **negar**;
   - exclusão de qualquer documento: deve **negar**.

**Configurar no Nexus**
1. Catálogo online > **Nuvem operacional**: id do projeto B, chave da API dele e um nome de loja (ex.: `minha-loja`). Salvar.
2. Menu lateral > **Entrar** com o usuário criado no passo 2.
3. O indicador do menu lateral mostra a fila. Clique em **Sincronizar agora**.

**Conferir**
- No Console, Firestore > Dados: deve existir `lojas/minha-loja/` com coleções `vendas`, `turnos`, `lancamentos` etc. (conforme o que você tem).
- Faça uma venda de teste no PDV. Em até 3 minutos (ou em Sincronizar agora) aparece em `vendas` com os itens dentro do documento.
- Cancele essa venda no Nexus: o **mesmo** documento muda (campos de cancelamento). Não pode surgir um segundo documento.
- Abra e feche um turno de caixa: aparece em `turnos`, e o fechamento atualiza o mesmo documento.

**A primeira sincronização envia todo o histórico** (vendas, caixa, financeiro). Se você importou muita coisa do ERP, isso
pode demorar e consumir escritas (20 mil por dia no plano gratuito). Deixe o programa aberto até a fila zerar.

---

## 3. Catálogo online (projeto A)

1. Crie outro projeto Firebase para o catálogo, ligue E-mail/senha, crie o banco e publique `C:\NexusLS\firebase\firestore.rules`
   com os e-mails trocados (mesmo cuidado de testar no Simulador: leitura pública de `produtos` e `meta`, escrita só dos e-mails da lista).
2. Catálogo online > bloco do catálogo: projeto, chave da API, e-mail e senha da licença. Salvar.
3. **Atualizar Firestore**: o programa mostra o plano (quantos produtos serão criados/alterados) e só envia depois do seu OK.
4. Confira os produtos no Console. Altere um preço no Nexus e atualize de novo: deve ser enviado só esse produto.

---

## 4. Teste de queda de internet e recuperação

1. Com tudo configurado e logado, **desligue a internet** do computador.
2. Faça 2 ou 3 vendas e altere um produto. O programa deve continuar normal; o indicador mostra itens pendentes.
3. Feche o programa no meio (pelo Gerenciador de Tarefas, para simular queda) e abra de novo: as vendas continuam lá.
4. **Ligue a internet**. A fila deve enviar sozinha em até 3 minutos.
5. Confira no Console: cada venda aparece **uma vez só** (o id do documento é o código da venda; reenviar nunca duplica).

---

## 5. Limites conhecidos (não são defeitos do teste)

- Dívida do cliente pode divergir do ERP quando ele tem "saldo anterior" (esse título não entra).
- Movimentos e fechamentos de caixa do ERP não são importados; só os turnos.
- Vendas do ERP com pagamento misto são ignoradas, com aviso.
- Fotos dos produtos não são importadas.
- Clientes, fornecedores, compras e pedidos do Nexus ainda não são enviados ao projeto B.
- As regras de segurança nunca rodaram num emulador (use o Simulador de regras do console).

---

## 6. Se algo falhar, me envie

- Em que passo parou e a mensagem exata que apareceu na tela.
- Para importação: o texto de resultado (inclui os "Avisos").
- Para envio: o texto do indicador no menu lateral e, em Catálogo online, o histórico.
- **Nunca** me envie senhas, a chave de API completa nem o conteúdo do `.env`. O id do projeto e a mensagem de erro bastam.
