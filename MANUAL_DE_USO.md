# Nexus LS — Manual de uso

Este manual explica o dia a dia do programa. Ele descreve o que o programa faz hoje; o que ainda não foi testado numa loja de verdade está marcado com **(a confirmar)**.

## 1. Primeiros passos

1. **Instale e abra** o `NexusLS.exe`. Os dados ficam em `%LOCALAPPDATA%\NexusLS` (banco `nexus.db`). O programa funciona **sem internet**.
2. Em **Configurações**, preencha os **Dados da loja** (nome, CNPJ, endereço, logo). Eles saem no cupom e na nota em PDF.
   As Configurações têm cinco abas: **Loja e cupom** (nome fantasia, razão social, CNPJ, inscrição estadual, endereço, cidade, e-mail, chave Pix, o que aparece no cupom, linhas em branco no fim para o corte, impressora e prévia), **Vendas e aparência** (tamanho das telas e desconto por peça da Venda Cliente), **Parâmetros** (troco inicial padrão na abertura do caixa, estoque mínimo e margem sugeridos no cadastro de produto), **Backup** e **Sistema** (pasta dos dados, tamanho do banco, licença, atualizações, atalhos do teclado e apagar dados).
3. Escolha a impressora do cupom (80 mm) e teste uma impressão.
4. Se quiser que o programa abra direto no PDV, ligue "Abrir no PDV" em Configurações.
5. **Funcionários e permissões:** em Funcionários, cadastre quem usa o programa. Com pelo menos um funcionário com login cadastrado, o programa passa a pedir entrada. Cada perfil enxerga só os menus permitidos.
6. **Backup:** em Configurações > Backup, escolha uma pasta (de preferência em outro disco ou no Drive) e deixe o backup automático ligado.

### Tela Início
É a primeira tela ao entrar. Mostra a saudação do dia, as vendas de hoje (com a comparação com ontem), o ticket médio, a situação do caixa, o que precisa de atenção (estoque abaixo do mínimo, clientes devendo, backup), os compromissos de hoje que ainda faltam e atalhos para as telas que a sua função permite. O botão **Atualizar** recarrega os números.

## 2. Vender (PDV)

| Tecla | O que faz |
| :--- | :--- |
| **F2** | Vai para a busca de produto |
| **Enter** (na busca) | Escolhe o produto. Se tiver tamanhos, pergunta o tamanho; depois a quantidade |
| **← →** | Troca tamanho, forma de pagamento ou tipo de desconto |
| **↑ ↓** | Muda a quantidade, as parcelas do crédito ou a linha de opções |
| **Espaço** (busca vazia) | Troca a tabela entre **Varejo** e **Atacado** (o botão fica laranja no atacado) |
| **Enter** (busca vazia) ou **F10** | Fecha a venda: desconto, depois pagamento |
| **F6** (no pagamento) | Usa um **vale-troca**: digite ou leia o código. F6 de novo tira o vale |
| **Del** | Remove o item selecionado do carrinho |
| **Esc** | Fecha a janela aberta (não esvazia o carrinho) |

- **Formas de pagamento:** dinheiro (com troco), Pix, débito, crédito (1 a 4x, com acréscimo de 5%, 7%, 9% ou 10%), cheque e **a prazo** (exige cliente; pode cadastrar o cliente na hora digitando o nome e dando Enter).
- **Vale-troca no pagamento (F6):** o saldo do vale abate do total; o resto vai pela forma escolhida (dinheiro, Pix ou cartão; não pode ser a prazo nem cheque). Se o vale paga tudo, o que sobra continua no vale para a próxima compra.
- **Preço e total da linha** podem ser editados no carrinho. Vazio devolve o preço de tabela.
- **Venda em andamento:** se o programa fechar com itens no carrinho, ao abrir a venda volta, conferida com o estoque.
- **Tela de sucesso:** Enter já segue para a próxima venda; use ← para "Reimprimir cupom".
- **Câmera/leitor:** o leitor de código de barras digita na busca e termina com Enter. Câmera: **(a confirmar)**.

### Venda Cliente (atacado e a prazo)
Menu **Vendas > Venda Cliente**: escolha o cliente (a lista mostra quanto cada um já deve). O PDV abre no **atacado**, com a venda **a prazo no nome dele** e desconto sugerido de **R$ 2,00 por peça** (você pode mudar). Uma faixa no painel direito mostra o cliente e a dívida, com **Trocar cliente** e **Sair**. Depois da venda a tela mostra quanto ele passou a dever.

## 3. Caixa

- **Caixa:** lista as vendas do dia, imprime/reimprime cupom, gera **Nota em PDF** (da venda) e **Relatório do dia em PDF** (totais, formas de pagamento e a lista das vendas), exporta CSV e **cancela venda** (sempre com motivo; pode devolver os itens ao estoque). Nada é apagado: cancelar deixa o registro.
- **Fechamento de caixa:** confira dinheiro, cartão e Pix contra o esperado. Se houver diferença, a observação é obrigatória. Para corrigir, faça uma nova versão: a anterior continua no histórico.
- **Movimentos:** sangria, reforço, despesa paga pela gaveta e entrada avulsa ficam ligados ao turno.
- **Trocas (menu Vendas > Trocas):** digite o número da venda (está no cupom) e Enter, escreva na coluna **Voltam** quantas peças de cada linha o cliente trouxe, informe o motivo e clique **Registrar troca e gerar vale**. As peças voltam ao estoque do tamanho e o **vale-troca** sai na impressora do cupom (dá para reimprimir na lista de vales). O vale vale o que o cliente pagou pelas peças (com o desconto da venda repartido; o acréscimo do cartão não volta) e **não vira dinheiro**: é usado no PDV com **F6**. Venda a prazo só troca depois de quitada. Errou? **Cancelar troca** (pede motivo) desfaz, se o vale ainda não foi usado.

## 4. Estoque, compras e fábrica

- **Estoque da loja:** cadastro com tamanhos, preço de varejo/atacado, custo, categoria, código de barras e código interno. Importação por planilha. Ajustes de estoque gravam histórico (Kardex). Filtro **Sobras** mostra o que parou.
- **Foto do produto:** no cadastro (produto já salvo), **Escolher foto...**. O programa reduz a imagem (máx. 1200 px, JPEG) e guarda no computador; funciona sem internet. Ao **Atualizar Firestore** no Catálogo online as fotos novas sobem para o Firebase Storage e o endereço vai junto do produto. Trocar ou remover a foto vale na próxima atualização. Se o Storage não estiver ativo no projeto, o aviso aparece na lista de problemas e o catálogo segue sem a foto. **(a confirmar com um Firebase real)**
- **Compras e fornecedores:** a compra soma ao estoque, atualiza o custo e pode gerar conta a pagar. Compra cancelada desfaz.
- **Etiquetas:** editor com modelos salvos; o código de barras é obrigatório no modelo.
- **Fábrica:** matéria-prima (com alerta de mínimo), **ordens de produção** com grade por tamanho e etapas (planejamento, corte, costura, estampa, acabamento), **perdas** (baixam a matéria) e **transferência para a loja**, que soma ao produto e conclui a ordem de uma vez só.
- **Calculadora de preços:** preço pela margem, promoção, comparador de formas de pagamento, quantidades e custo de lote. Dá para **carregar o custo de um produto**, **aplicar o preço calculado** nele (só o preço muda; o programa pergunta antes) e **guardar os parâmetros da loja**.

## 5. Gestão

- **Pedidos:** atacado, WhatsApp, catálogo e encomenda, com etapas e histórico.
- **Clientes:** ficha, histórico, limite, **dívida a prazo** com pagamento parcial (o mais antigo é quitado primeiro) e estorno.
- **Financeiro e Contas e saldos:** contas a pagar/receber, parcelas, cartões a liquidar, cheques, transferências e saldos. Os saldos usam uma fórmula única em todas as telas.
- **Relatórios, Mais vendidos e Desempenho:** período contra o anterior, ranking, quebra por categoria, pagamento e vendedor, com **filtros** e **exportar CSV**. O lucro só aparece quando o custo é conhecido na maior parte do que foi vendido.

- **Agenda:** calendário com os compromissos do dia (entrega, cobrança, reunião), horário ou dia inteiro, marcar como feito e remover. Fica só neste computador.

## 6. Catálogo online e importação do ERP

- Em **Catálogo online** você liga sua conta Firebase. Os dados vão para a nuvem por uma **fila**: se a internet cair, nada se perde e o envio continua depois. O indicador no menu lateral mostra o estado da fila.
- **Importar do ERP:** traz produtos (com grade), clientes, turnos, vendas, dívidas, pedidos, movimentos e gastos do caixa e a fábrica. Importar de novo atualiza sem duplicar. Fica de fora: fechamentos de caixa (o ERP fecha por período, aqui é por turno), saldo anterior sem venda de origem e fotos.
- **(a confirmar)** A sincronização e a importação ainda não foram testadas com um Firebase real. Siga o `GUIA_TESTE_REAL.md`.

### Editor do catálogo
Menu **Gestão > Editor do catálogo**: modelo e fonte, cores (formato #RRGGBB, com aviso quando a leitura fica difícil), textos do topo, dados da loja (WhatsApp, Instagram, endereço...), mínimo do atacado, quantidade de novidades e os dois banners. **Salvar** guarda neste computador; **Publicar no Firebase** grava o documento `meta/vitrine`. É um subconjunto do editor visual do ERP: ainda não há arrastar blocos, fotos de banner, logo, seleção de produtos, desfazer/refazer nem catálogo em PDF. **(a confirmar)** A vitrine pública precisa ler `meta/vitrine` para usar estas escolhas.

### Backup no Google Drive
Configurações > Backup > **Google Drive** envia uma cópia do backup para a pasta "Nexus LS Backups" do seu Drive (o programa só enxerga o que ele mesmo criou). Passo a passo, uma vez só:
1. Em console.cloud.google.com crie um projeto e ative a **Google Drive API**.
2. Em "Tela de permissão OAuth" escolha tipo Externo, preencha o nome do app e adicione o seu e-mail como usuário de teste.
3. Em "Credenciais" crie um **ID do cliente OAuth** do tipo **App para computador** e copie o Client ID e o Client Secret.
4. No Nexus LS cole os dois, clique em **Salvar credenciais** e depois em **Conectar ao Google**; autorize no navegador.
5. Faça um backup e clique em **Enviar o backup ... ao Drive**.
O segredo e a permissão ficam protegidos pelo Windows neste computador. **(a confirmar)** Foi testado só com um Google simulado; a primeira conexão real depende da sua conta e do projeto que você criar. 

### Google Agenda
Na tela **Agenda**, o botão **Enviar o dia ao Google Agenda** cria no seu Google Agenda principal os compromissos do dia escolhido; enviar de novo atualiza os mesmos eventos (sem duplicar) e remover um compromisso apaga o evento também. Para isso, no Google Cloud ative também a **Google Calendar API**. Quem já tinha conectado o Google antes desta versão precisa **desconectar e conectar de novo** (a permissão da agenda é nova). Gmail ainda não tem integração.

## 6.1 Imprimir etiquetas (Nexus BarTender)

O **Nexus BarTender** é o programa onde as etiquetas são **criadas e impressas**: o **Estúdio de Etiquetas** desenha o modelo (nome, preço, código de barras, logo, QR code, cores) e a **Fila de Impressão** manda para a impressora. Ele lê os produtos do mesmo banco do Nexus; se o Nexus vender ou mudar estoque com o BarTender aberto, clique em **Atualizar** (rodapé do menu lateral) para ver os números novos.

**Passo a passo para imprimir**

1. Abra o **Catálogo ERP** e ache o produto (busca por nome, referência ou código; há filtro por categoria e por "com/sem código").
2. Na coluna **Tamanho** escolha o tamanho. **Todos os tamanhos** cria uma etiqueta para cada tamanho da grade.
3. Defina a quantidade com **−** e **+** e clique em **Fila**. Para puxar a grade pelo estoque da loja, use **Puxar estoque da loja**.
4. Abra **Fila de Impressão** e confira: miniatura da etiqueta, tamanho, preço e o total de etiquetas. Ajuste as quantidades se precisar.
5. Escolha a **impressora**. Em folha A4, informe em **Posição inicial** o número da etiqueta em que começar (assim você reaproveita uma folha já usada).
6. Clique em **IMPRIMIR ETIQUETAS NO WINDOWS** e confirme no diálogo do Windows. Deixe marcada a opção de usar o modelo do Estúdio se quiser sair com o seu desenho (logo, QR code, cores).

O botão **Como imprimir** (topo da Fila) mostra esses passos de novo. Ele aparece sozinho só na primeira vez.

**Pelo Nexus:** na tela **Etiquetas** do Nexus, o botão **Imprimir no BarTender** abre o BarTender com a fila já montada, direto na Fila de Impressão. Criar e editar modelos é só no BarTender.

**Bobina térmica ou folha A4**

- **Bobina térmica** (50x30, 40x25, 60x40...): escolha o formato em **Configurações** do BarTender e a impressora térmica na Fila. Cada etiqueta sai numa página do tamanho da bobina.
- **Folha A4** (várias etiquetas por folha): escolha o formato A4 e use a **Posição inicial** para não desperdiçar etiquetas da folha.

**Código de barras:** produto sem código aparece como "Sem Código". Use **Gerar Código EAN-13** (um produto) ou **Gerar Códigos para Todos sem Código** (em lote; o programa faz uma cópia de segurança do banco antes).

**Se a impressora não aparece ou não imprime**

- Confira se ela está instalada no Windows (Configurações do Windows > Impressoras) e clique em **Atualizar** no BarTender.
- Se o papel sair com tamanho errado, ajuste o tamanho do papel nas propriedades da impressora no diálogo do Windows (alguns drivers de impressora térmica não aceitam o ajuste automático).
- Teste com uma etiqueta só antes de imprimir um lote grande.

## 7. Se algo der errado

- **Mensagem vermelha:** leia o texto, ele diz o que corrigir.
- **Restaurar backup:** Configurações > Backup > Restaurar.
- **Voltar a versão anterior:** na pasta do programa há o `NexusLS.anterior.exe`.
- **Impressora não imprime:** a venda já foi gravada; use **Reimprimir cupom**.

## 8. Licença

No menu lateral, em **Licença**, aparece o estado. Para ativar, clique em **Ativar licença** e escolha o arquivo `.json` que o fornecedor enviou. Passe ao fornecedor o **código deste computador** (aparece ao parar o mouse sobre o botão) se a licença for presa a ele. Por enquanto o programa funciona mesmo sem licença.
