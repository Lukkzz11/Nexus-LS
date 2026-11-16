# Prompt para o Antigravity – Melhorias do Nexus BarTender

Você vai melhorar o app "Nexus BarTender" (WPF, .NET, biblioteca WPF-UI), em C:\NexusLS\src\NexusLS.BarTender. Ele faz parte da solução NexusLS.slnx e usa NexusLS.Core. Leia o código antes de mexer e mantenha o comportamento atual: o objetivo é melhorar o visual e a organização, sem quebrar nada.

## REGRAS GERAIS
- Idioma da interface: português do Brasil. Nomes de código seguem o padrão já existente (português).
- Não crie cores, fontes ou espaçamentos soltos no XAML. Use ou crie tokens em Resources/Tokens.xaml.
- Depois de cada etapa, rode `dotnet build NexusLS.slnx` e os testes em tests/. Só passe para a etapa seguinte se compilar e os testes passarem.
- Faça um commit pequeno por etapa, com mensagem em português no estilo "feat(ui): ..." ou "refactor: ...".
- Não apague funcionalidades. Não mexa em src/NexusLS.App, Core, Setup ou Atualizador, exceto se for necessário para compartilhar algo (nesse caso, avise).

## ETAPA 1 – Diálogos modernos e avisos discretos
- Hoje há cerca de 30 chamadas de MessageBox.Show em DesignerViewModel.cs, MainViewModel.cs, App.xaml.cs e outros.
- Crie uma interface IDialogoService (Confirmar, Informar, Erro) com uma implementação em janela customizada no estilo Fluent do app (cantos RaioGrande, botão primário azul, ícone por tipo). Injete-a nos ViewModels em vez de chamar MessageBox direto.
- Mensagens de sucesso ("Projeto salvo com sucesso!", "Modelo carregado com sucesso!", "Impressão enviada com sucesso!") devem virar um toast discreto que some sozinho em cerca de 3 s (use o Snackbar do WPF-UI ou um componente próprio), sem exigir clique.
- Erro e confirmação continuam como diálogo modal.

## ETAPA 2 – Tokens de cor no lugar de cores fixas
- Existem cerca de 50 cores fixas (#F3E8FF, #DCFCE7, #FEF3C7, #EFF6FF, #15803D, #F8FAFC, #F1F5F9, etc.), principalmente em Views/Designer/DesignerView.xaml e Views/MainWindow.xaml.
- Crie tokens semânticos (por exemplo CorSucessoFundo, CorSucessoBorda, CorSucessoTexto, CorAvisoFundo, CorAvisoBorda, CorDestaqueFundo, CorSuperficieSuave...) em Tokens.xaml e troque todas as cores fixas por eles.
- Padronize o FontSize de BotaoOpcao (hoje 17) com a escala existente (TamCorpo/TamRotulo).
- Deixe a estrutura pronta para um tema escuro futuro, sem implementá-lo agora.

## ETAPA 3 – Menu lateral e Catálogo
- No menu lateral, troque os `Grid Width="170"` por colunas * e Auto para que o texto não quebre. Crie um estilo reutilizável para os badges de contagem.
- Catálogo: encurte os textos dos botões do rodapé ("Gerar EAN-13 (selecionado)", "Gerar códigos faltantes", "Puxar estoque da loja"). Na coluna "Ações" da tabela, deixe só ícones com tooltip e AutomationProperties.Name (Abrir no Estúdio, − qtd +, Adicionar à fila).
- Adicione debounce de cerca de 250 ms na busca por texto e confirme que o DataGrid usa virtualização de linhas.
- Adicione AutomationProperties.Name em todos os botões que têm só ícone.

## ETAPA 4 – Fila de impressão e Configurações
- Fila: adicione uma miniatura da etiqueta em cada item (reutilize o serviço de desenho que já existe). Crie um estado vazio com ícone, texto e botão "Ir ao Catálogo". Reorganize o rodapé: botão IMPRIMIR sempre visível, e opções de impressora e posição inicial agrupadas.
- Configurações: separe em seções "Loja", "Impressão" e "Avançado". O ZPL II fica numa seção recolhível (Expander) em "Avançado". Use a largura da tela de forma melhor, sem deixar tudo numa coluna estreita à esquerda.

## ETAPA 5 – Impressão sem código duplicado e sem erros engolidos
- Em Services/ImpressaoService.cs, Imprimir e ImprimirDesign repetem a lógica de escolher impressora e definir papel (A4 ou bobina). Extraia um método privado compartilhado.
- Troque os `catch { }` vazios por log (use o log que o app já tiver, ou crie um simples em arquivo) e avise o usuário quando a impressora escolhida não for encontrada, informando qual impressora será usada no lugar.

## ETAPA 6 – Higiene do repositório
- Remova do Git as pastas bin/ e obj/ que estão versionadas, em especial tests/**/bin/** (incluindo NexusBarTender.exe e DLLs). Use `git rm -r --cached` e reforce o .gitignore.
- Não apague nada do disco que seja necessário para publicar (pasta publicar/ e publicar_release/ ficam como estão).

## ETAPA 7 – Dividir arquivos grandes (faça por último, com cuidado)
- DesignerView.xaml (~2.087 linhas): extraia partes em UserControls (RibbonDesigner, ToolboxDesigner, InspectorDesigner, GaleriaModelos), mantendo os bindings.
- DesignerViewModel.cs (~1.824 linhas) e MainViewModel.cs (~866): divida em arquivos `partial` por responsabilidade (Arquivo, Impressão, Elementos, Produto, Desfazer/Refazer).
- Não mude o comportamento nesta etapa: só mova código.

## ETAPA 8 – Testes
- Adicione testes em tests/NexusLS.App.Tests para: dígito verificador do EAN-13, cálculo de páginas e posição inicial na folha A4, salvar e carregar o modelo de etiqueta (ida e volta) e geração de ZPL de um modelo simples.

## ETAPA 9 – Conexão com o Nexus (o BarTender cria e imprime; o Nexus só imprime)
- Banco: no BarTender, use `CaminhoBanco.StringConexao` e `PreparadorBanco` do NexusLS.Core em vez da string montada à mão em Services/BancoDadosService.cs. Passe o usuário ao `FilaSincronizacaoInterceptor` quando houver sessão, em vez de `() => null`.
- Etiquetas por produto: hoje ficam em JSON em %LOCALAPPDATA%\NexusLS\EtiquetasProdutos\produto_<id>.json (Services/GerenciadorEtiquetasProdutoService.cs), fora do backup e da sincronização. Guarde os modelos no banco (ou no `ModeloEtiquetaStore` do Core) para entrarem no backup e na nuvem. Migre os JSON existentes na primeira abertura, sem apagar os arquivos antigos. Pare de engolir erros ao salvar: avise o usuário.
- Atualização dos dados: o catálogo só recarrega no botão "Atualizar". Recarregue sozinho (FileSystemWatcher no .db/-wal ou a cada 60 s) ou mostre "Dados de X minutos atrás" na barra lateral.
- No Nexus (src/NexusLS.App), na tela "Etiquetas" e na lista de produtos, adicione o botão "Imprimir etiqueta" que abre o NexusBarTender.exe passando o produto e a quantidade por argumento (ex.: `--produto 123 --qtd 5 --imprimir`). O BarTender deve abrir direto na Fila com o item já adicionado. Se o BarTender não estiver instalado, avise e ofereça abrir o instalador.
- O Nexus NÃO ganha editor de etiquetas: criar e editar modelos é só no BarTender. No Nexus fica só imprimir.
- Meta futura (só documente, não implemente agora): mover o desenho da etiqueta para uma biblioteca compartilhada, para o Nexus imprimir os modelos do BarTender sem abrir o outro app.

## ETAPA 10 – Passo a passo de impressão no BarTender (hoje não existe)
- Não existe guia de impressão no app nem no MANUAL_DE_USO.md (o manual nem cita o BarTender).
- Dentro do app: adicione um botão "Como imprimir" no topo da Fila de Impressão. Ele abre um painel curto com 5 passos: 1) escolher produtos no Catálogo, 2) escolher o tamanho e a quantidade, 3) conferir a Fila, 4) escolher a impressora e a posição inicial, 5) clicar em IMPRIMIR e conferir no diálogo do Windows. Mostre o painel sozinho na primeira vez e depois só pelo botão.
- Na Fila vazia, mostre uma versão resumida do passo a passo.
- No MANUAL_DE_USO.md, crie a seção "Imprimir etiquetas (BarTender)" com os mesmos passos, mais: como configurar bobina térmica (50x30, 40x25) e folha A4, como reaproveitar folha parcialmente usada (posição inicial), e o que fazer quando a impressora não aparece.

## ENTREGA
- Ao final, mostre um resumo por etapa com os arquivos alterados e o resultado do build e dos testes.
- Não publique release nem faça push sem eu pedir.
