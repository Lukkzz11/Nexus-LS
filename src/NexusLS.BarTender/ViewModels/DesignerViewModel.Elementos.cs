using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexusLS.BarTender.Models;
using NexusLS.BarTender.Models.Designer;
using NexusLS.BarTender.Services;
using NexusLS.Core.Etiquetas;
using NexusLS.Core.Models;
using NexusLS.Core.Services;

namespace NexusLS.BarTender.ViewModels;

public partial class DesignerViewModel
{
    [RelayCommand]
    public void Agrupar()
    {
        var selecionados = Modelo.Elementos.Where(e => e.IsSelected).ToList();
        if (selecionados.Count < 2 && ElementoSelecionado != null)
        {
            selecionados = new List<ElementoDesign> { ElementoSelecionado };
        }
        if (selecionados.Count > 0)
        {
            RegistrarSnapshot();
            var novoGrupoId = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
            var nomeGrupo = $"Grupo {novoGrupoId}";
            foreach (var el in selecionados)
            {
                el.GrupoId = novoGrupoId;
                el.NomeGrupo = nomeGrupo;
            }
            NotificarCamadasAlteradas();
            AtualizarPreview();
        }
    }

    [RelayCommand]
    public void Desagrupar()
    {
        var selecionados = Modelo.Elementos.Where(e => e.IsSelected).ToList();
        if (selecionados.Count == 0 && ElementoSelecionado != null)
        {
            selecionados = new List<ElementoDesign> { ElementoSelecionado };
        }
        if (selecionados.Count > 0)
        {
            RegistrarSnapshot();
            var grupoIds = selecionados.Where(e => !string.IsNullOrEmpty(e.GrupoId)).Select(e => e.GrupoId).Distinct().ToList();
            foreach (var el in Modelo.Elementos)
            {
                if (grupoIds.Contains(el.GrupoId))
                {
                    el.GrupoId = null;
                    el.NomeGrupo = null;
                }
            }
            NotificarCamadasAlteradas();
            AtualizarPreview();
        }
    }

    [RelayCommand]
    public void AlternarVisibilidadeCamada(ElementoDesign? el)
    {
        if (el == null) return;
        RegistrarSnapshot();
        el.Visivel = !el.Visivel;
        if (!el.Visivel && el.IsSelected)
        {
            DesmarcarSelecao();
        }
        NotificarCamadasAlteradas();
        AtualizarPreview();
        ElementoPropriedadeAlterada?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    public void AlternarBloqueioCamada(ElementoDesign? el)
    {
        if (el == null) return;
        RegistrarSnapshot();
        el.Bloqueado = !el.Bloqueado;
        if (el.Bloqueado && el.IsSelected)
        {
            DesmarcarSelecao();
        }
        NotificarCamadasAlteradas();
        AtualizarPreview();
        ElementoPropriedadeAlterada?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    public void SubirCamada(ElementoDesign? el)
    {
        el ??= ElementoSelecionado;
        if (el == null) return;
        var idx = Modelo.Elementos.IndexOf(el);
        if (idx >= 0 && idx < Modelo.Elementos.Count - 1)
        {
            RegistrarSnapshot();
            Modelo.Elementos.Move(idx, idx + 1);
            NotificarCamadasAlteradas();
            AtualizarPreview();
            ElementoPropriedadeAlterada?.Invoke(this, EventArgs.Empty);
        }
    }

    [RelayCommand]
    public void DescerCamada(ElementoDesign? el)
    {
        el ??= ElementoSelecionado;
        if (el == null) return;
        var idx = Modelo.Elementos.IndexOf(el);
        if (idx > 0)
        {
            RegistrarSnapshot();
            Modelo.Elementos.Move(idx, idx - 1);
            NotificarCamadasAlteradas();
            AtualizarPreview();
            ElementoPropriedadeAlterada?.Invoke(this, EventArgs.Empty);
        }
    }

    [RelayCommand]
    public void AdicionarTexto()
    {
        RegistrarSnapshot();
        var el = new ElementoDesign
        {
            Nome = $"Texto {Modelo.Elementos.Count(e => e.Tipo == TipoElemento.Texto) + 1}",
            Tipo = TipoElemento.Texto,
            Texto = "Novo Texto",
            X = 2.0,
            Y = Math.Min(Modelo.AlturaMm - 6, 2.0),
            Largura = Math.Min(Modelo.LarguraMm - 4, 30.0),
            Altura = 5.0,
            TamanhoFonte = 9.0
        };
        Modelo.Elementos.Add(el);
        NotificarCamadasAlteradas();
        SelecionarElemento(el);
    }

    [RelayCommand]
    public void AdicionarTamanho()
    {
        RegistrarSnapshot();
        var larg = Math.Max(10.0, Modelo.LarguraMm - 4.0);
        var alt = Math.Min(15.0, Math.Max(6.0, Modelo.AlturaMm * 0.25));
        var el = new ElementoDesign
        {
            Nome = "Tamanho",
            Tipo = TipoElemento.Texto,
            Texto = "{Tamanho}",
            Variavel = VariavelEtiqueta.Tamanho,
            X = 2.0,
            Y = Math.Max(2.0, Math.Round((Modelo.AlturaMm * 0.22) * 2) / 2.0),
            Largura = larg,
            Altura = alt,
            TamanhoFonte = 28.0,
            Negrito = true,
            Alinhamento = TextAlignment.Center
        };
        Modelo.Elementos.Add(el);
        NotificarCamadasAlteradas();
        SelecionarElemento(el);
        AtualizarPreview();
    }

    [RelayCommand]
    public void AdicionarImagem()
    {
        var ofd = new OpenFileDialog
        {
            Title = "Selecionar Imagem / Logotipo para Etiqueta",
            Filter = "Imagens (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|Todos os Arquivos (*.*)|*.*"
        };

        if (ofd.ShowDialog() == true)
        {
            try
            {
                RegistrarSnapshot();
                var bytes = File.ReadAllBytes(ofd.FileName);
                var base64 = Convert.ToBase64String(bytes);

                var el = new ElementoDesign
                {
                    Nome = $"Imagem {Modelo.Elementos.Count(e => e.Tipo == TipoElemento.Imagem) + 1}",
                    Tipo = TipoElemento.Imagem,
                    CaminhoImagem = ofd.FileName,
                    ImagemBase64 = base64,
                    X = 2.0,
                    Y = 2.0,
                    Largura = 12.0,
                    Altura = 12.0,
                    ManterProporcao = true
                };

                Modelo.Elementos.Add(el);
                NotificarCamadasAlteradas();
                SelecionarElemento(el);
            }
            catch (Exception ex)
            {
                _dialogo.Erro($"Erro ao carregar a imagem: {ex.Message}");
            }
        }
    }

    [RelayCommand]
    public void TrocarImagemElemento()
    {
        if (ElementoSelecionado == null || ElementoSelecionado.Tipo != TipoElemento.Imagem) return;

        var ofd = new OpenFileDialog
        {
            Title = "Trocar Imagem / Logotipo",
            Filter = "Imagens (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp"
        };

        if (ofd.ShowDialog() == true)
        {
            try
            {
                RegistrarSnapshot();
                var bytes = File.ReadAllBytes(ofd.FileName);
                ElementoSelecionado.CaminhoImagem = ofd.FileName;
                ElementoSelecionado.ImagemBase64 = Convert.ToBase64String(bytes);
                AtualizarPreview();
            }
            catch (Exception ex)
            {
                _dialogo.Erro($"Erro ao carregar a imagem: {ex.Message}");
            }
        }
    }

    [RelayCommand]
    public void AdicionarCodigoBarras()
    {
        AbrirModalCodigo("1D");
    }

    [RelayCommand]
    public void AdicionarQrCode()
    {
        AbrirModalCodigo("2D");
    }

    [RelayCommand]
    public void AbrirModalCodigo(string? tipo = null)
    {
        bool is2D = string.Equals(tipo, "2D", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(tipo, "QR", StringComparison.OrdinalIgnoreCase);

        if (is2D)
        {
            ModalCodigoTitulo = "Gerador de QR Code (2D)";
            ModalCodigoTipo = TipoSimboloCodigo.QrCode;
            ModalCodigoEhQrCode = true;
            ModalCodigoMostrarLegivel = false;
            ModalCodigoLarguraMm = 15.0;
            ModalCodigoAlturaMm = 15.0;
            ModalCodigoOrigemProduto = true;
            ModalCodigoVariavel = VariavelEtiqueta.CodigoBarras;
            if (string.IsNullOrWhiteSpace(ModalCodigoValorFixo) || ModalCodigoValorFixo == "7891234567890")
            {
                ModalCodigoValorFixo = "https://loja.com.br";
            }
        }
        else
        {
            ModalCodigoTitulo = "Gerador de Código de Barras (1D)";
            ModalCodigoTipo = TipoSimboloCodigo.Code128;
            ModalCodigoEhQrCode = false;
            ModalCodigoMostrarLegivel = true;
            ModalCodigoLarguraMm = Math.Min(38.0, Math.Max(20.0, Modelo.LarguraMm - 4.0));
            ModalCodigoAlturaMm = 12.0;
            ModalCodigoOrigemProduto = true;
            ModalCodigoVariavel = VariavelEtiqueta.CodigoBarras;
            if (string.IsNullOrWhiteSpace(ModalCodigoValorFixo) || ModalCodigoValorFixo.StartsWith("http"))
            {
                ModalCodigoValorFixo = "7891234567890";
            }
        }

        AtualizarModalCodigoPreview();
        ModalCodigoAberto = true;
    }

    [RelayCommand]
    public void FecharModalCodigo()
    {
        ModalCodigoAberto = false;
    }

    [RelayCommand]
    public void ConfirmarInserirCodigo()
    {
        RegistrarSnapshot();

        var ehQr = ModalCodigoTipo == TipoSimboloCodigo.QrCode;
        int totalCodigos = Modelo.Elementos.Count(e => e.Tipo == TipoElemento.CodigoBarras) + 1;

        var el = new ElementoDesign
        {
            Nome = ehQr ? $"QR Code {totalCodigos}" : $"Código 1D {totalCodigos}",
            Tipo = TipoElemento.CodigoBarras,
            TipoCodigo = ModalCodigoTipo,
            Variavel = ModalCodigoOrigemProduto ? ModalCodigoVariavel : VariavelEtiqueta.Nenhuma,
            ValorCodigo = !ModalCodigoOrigemProduto ? ModalCodigoValorFixo : null,
            MostrarTextoLegivel = !ehQr && ModalCodigoMostrarLegivel,
            Largura = Math.Max(5.0, ModalCodigoLarguraMm),
            Altura = Math.Max(5.0, ModalCodigoAlturaMm),
            X = Math.Max(1.0, Math.Round((Modelo.LarguraMm - ModalCodigoLarguraMm) / 2.0, 1)),
            Y = Math.Max(1.0, Math.Round((Modelo.AlturaMm - ModalCodigoAlturaMm) / 2.0, 1))
        };

        Modelo.Elementos.Add(el);
        NotificarCamadasAlteradas();
        SelecionarElemento(el);
        AtualizarPreview();

        ModalCodigoAberto = false;
    }

    [RelayCommand]
    public void DefinirOrigemCodigo(string origem)
    {
        ModalCodigoOrigemProduto = string.Equals(origem, "Produto", StringComparison.OrdinalIgnoreCase);
        AtualizarModalCodigoPreview();
    }

    [RelayCommand]
    public void DefinirTipoSimbolo(string tipo)
    {
        if (Enum.TryParse<TipoSimboloCodigo>(tipo, true, out var t))
        {
            ModalCodigoTipo = t;
        }
    }

    [RelayCommand]
    public void GerarEan13Nexus()
    {
        ModalCodigoTipo = TipoSimboloCodigo.Ean13;
        ModalCodigoEhQrCode = false;
        ModalCodigoOrigemProduto = false;

        long seq = 1;
        if (ListaProdutosCatalog.Count > 0)
        {
            var codigos = ListaProdutosCatalog
                .Select(p => p.Produto.CodigoBarras)
                .Where(c => c != null && c.StartsWith("2") && c.Length == 13)
                .Select(CodigoBarrasInterno.SequenciaDe)
                .Where(s => s != null)
                .Select(s => s!.Value);

            if (codigos.Any())
            {
                seq = codigos.Max() + 1;
            }
            else
            {
                seq = ListaProdutosCatalog.Count + 1;
            }
        }

        ModalCodigoValorFixo = CodigoBarrasInterno.Montar(seq);
        AtualizarModalCodigoPreview();
    }

    public void AtualizarModalCodigoPreview()
    {
        try
        {
            ModalCodigoEhQrCode = ModalCodigoTipo == TipoSimboloCodigo.QrCode;

            string conteudo;
            if (ModalCodigoOrigemProduto)
            {
                var tamModal = !string.IsNullOrWhiteSpace(TamanhoParaTeste) && TamanhoParaTeste != "Sem tamanho"
                    ? TamanhoParaTeste
                    : null;

                conteudo = ModalCodigoVariavel switch
                {
                    VariavelEtiqueta.CodigoBarras => CodigoBarrasTamanho.Resolver(ProdutoAtual, tamModal) ?? ProdutoAtual?.CodigoBarras ?? "7891234567890",
                    VariavelEtiqueta.CodigoInterno => ProdutoAtual?.CodigoInterno ?? "INT-0001",
                    VariavelEtiqueta.Referencia => CodigoBarrasTamanho.ResolverReferencia(ProdutoAtual, tamModal) ?? ProdutoAtual?.Referencia ?? "REF-8606",
                    VariavelEtiqueta.NomeProduto => ProdutoAtual?.Nome ?? "CAMISA DRYFIT",
                    VariavelEtiqueta.PrecoVarejo => ProdutoAtual != null ? (ProdutoAtual.PrecoVendaCentavos / 100.0).ToString("F2") : "49.90",
                    VariavelEtiqueta.NomeLoja => !string.IsNullOrWhiteSpace(NomeLoja) ? NomeLoja : "NEXUS LS",
                    _ => CodigoBarrasTamanho.Resolver(ProdutoAtual, tamModal) ?? ProdutoAtual?.CodigoBarras ?? "7891234567890"
                };

                if (string.IsNullOrWhiteSpace(conteudo))
                {
                    conteudo = "7891234567890";
                }
            }
            else
            {
                conteudo = !string.IsNullOrWhiteSpace(ModalCodigoValorFixo)
                    ? ModalCodigoValorFixo.Trim()
                    : (ModalCodigoEhQrCode ? "https://nexusls.com.br" : "7891234567890");
            }

            ModalCodigoValorResolvido = conteudo;

            // Validações e avisos contextuais baseados nas regras do Nexus
            if (ModalCodigoTipo == TipoSimboloCodigo.Ean13)
            {
                var digitos = new string(conteudo.Where(char.IsDigit).ToArray());
                if (digitos.Length == 13 && digitos.StartsWith("2") && CodigoBarrasInterno.EhEan13Valido(digitos))
                {
                    ModalCodigoAviso = "Código EAN-13 Interno Nexus válido (prefixo 2 oficial da casa com Dígito Verificador Módulo 10). Aceito pelo leitor e PDV.";
                }
                else if (digitos.Length < 12)
                {
                    ModalCodigoAviso = "Dica EAN-13: Requer 12 ou 13 dígitos numéricos. Faltando dígitos, serão completados com zeros à esquerda e cálculo de checksum.";
                }
                else if (digitos.Length > 13)
                {
                    ModalCodigoAviso = "Dica EAN-13: Apenas os primeiros 12 dígitos mais o dígito verificador serão codificados.";
                }
                else
                {
                    ModalCodigoAviso = "Padrão EAN-13 válido (13 dígitos com checksum automático).";
                }
            }
            else if (ModalCodigoOrigemProduto && ModalCodigoVariavel == VariavelEtiqueta.Referencia)
            {
                ModalCodigoAviso = $"Regra Nexus para Referência '{conteudo}': em etiquetas com grade de tamanho, o código receberá o sufixo numérico do tamanho (P→{conteudo}1, M→{conteudo}2, G→{conteudo}3).";
            }
            else if (ModalCodigoTipo == TipoSimboloCodigo.Code39)
            {
                ModalCodigoAviso = "Padrão Code 39 aceita letras maiúsculas (A-Z), números (0-9) e símbolos (- . $ / + % espaço).";
            }
            else if (ModalCodigoTipo == TipoSimboloCodigo.QrCode)
            {
                ModalCodigoAviso = "Padrão QR Code 2D (Suporta URLs, chaves Pix, textos alfanuméricos e identificadores).";
            }
            else
            {
                ModalCodigoAviso = "Padrão Code 128 (Alta densidade, suporta caracteres alfanuméricos completos). Aceita referências e códigos de produto.";
            }

            int larguraPx = (int)Math.Max(80, Math.Min(600, ModalCodigoLarguraMm * 10.0));
            int alturaPx = (int)Math.Max(40, Math.Min(600, ModalCodigoAlturaMm * 10.0));

            ModalCodigoPreview = BarcodeGeneratorService.Gerar(conteudo, ModalCodigoTipo, larguraPx, alturaPx, margem: 1);
        }
        catch (Exception ex)
        {
            ModalCodigoAviso = $"Aviso de geração: {ex.Message}";
            ModalCodigoPreview = null;
        }
    }

    [RelayCommand]
    public void AdicionarRetangulo()
    {
        RegistrarSnapshot();
        var el = new ElementoDesign
        {
            Nome = $"Retângulo {Modelo.Elementos.Count(e => e.Tipo == TipoElemento.Retangulo) + 1}",
            Tipo = TipoElemento.Retangulo,
            X = 2.0,
            Y = 2.0,
            Largura = Modelo.LarguraMm - 4.0,
            Altura = Modelo.AlturaMm - 4.0,
            EspessuraBorda = 1.0,
            Preenchido = false
        };
        Modelo.Elementos.Add(el);
        NotificarCamadasAlteradas();
        SelecionarElemento(el);
    }

    [RelayCommand]
    public void AdicionarLinha()
    {
        RegistrarSnapshot();
        var el = new ElementoDesign
        {
            Nome = $"Linha {Modelo.Elementos.Count(e => e.Tipo == TipoElemento.Linha) + 1}",
            Tipo = TipoElemento.Linha,
            X = 2.0,
            Y = 10.0,
            Largura = Modelo.LarguraMm - 4.0,
            Altura = 1.0,
            EspessuraBorda = 0.8
        };
        Modelo.Elementos.Add(el);
        NotificarCamadasAlteradas();
        SelecionarElemento(el);
    }

    [RelayCommand]
    public void ExcluirSelecionado()
    {
        if (ElementoSelecionado == null) return;
        RegistrarSnapshot();
        Modelo.Elementos.Remove(ElementoSelecionado);
        ElementoSelecionado = null;
        NotificarCamadasAlteradas();
        AtualizarPreview();
    }

    [RelayCommand]
    public void DuplicarSelecionado()
    {
        if (ElementoSelecionado == null) return;
        RegistrarSnapshot();
        var clone = ElementoSelecionado.Clonar();
        clone.X = Math.Min(Modelo.LarguraMm - clone.Largura, clone.X + 2.0);
        clone.Y = Math.Min(Modelo.AlturaMm - clone.Altura, clone.Y + 2.0);
        Modelo.Elementos.Add(clone);
        NotificarCamadasAlteradas();
        SelecionarElemento(clone);
    }

    // =========================================================================
    // COMANDOS DE ALINHAMENTO (ESTILO CORELDRAW / VECTORCRAFT)
    // =========================================================================
    [RelayCommand]
    public void CentralizarNaPagina()
    {
        if (ElementoSelecionado == null) return;
        RegistrarSnapshot();
        ElementoSelecionado.X = Math.Max(0, Math.Round((Modelo.LarguraMm - ElementoSelecionado.Largura) / 2.0, 1));
        ElementoSelecionado.Y = Math.Max(0, Math.Round((Modelo.AlturaMm - ElementoSelecionado.Altura) / 2.0, 1));
        AtualizarPreview();
    }

    [RelayCommand]
    public void CentralizarHorizontalmente()
    {
        if (ElementoSelecionado == null) return;
        RegistrarSnapshot();
        ElementoSelecionado.X = Math.Max(0, Math.Round((Modelo.LarguraMm - ElementoSelecionado.Largura) / 2.0, 1));
        AtualizarPreview();
    }

    [RelayCommand]
    public void CentralizarVerticalmente()
    {
        if (ElementoSelecionado == null) return;
        RegistrarSnapshot();
        ElementoSelecionado.Y = Math.Max(0, Math.Round((Modelo.AlturaMm - ElementoSelecionado.Altura) / 2.0, 1));
        AtualizarPreview();
    }

    [RelayCommand]
    public void AlinharEsquerda()
    {
        if (ElementoSelecionado == null) return;
        RegistrarSnapshot();
        ElementoSelecionado.X = 2.0;
        AtualizarPreview();
    }

    [RelayCommand]
    public void AlinharDireita()
    {
        if (ElementoSelecionado == null) return;
        RegistrarSnapshot();
        ElementoSelecionado.X = Math.Max(0, Modelo.LarguraMm - ElementoSelecionado.Largura - 2.0);
        AtualizarPreview();
    }

    [RelayCommand]
    public void AlinharTopo()
    {
        if (ElementoSelecionado == null) return;
        RegistrarSnapshot();
        ElementoSelecionado.Y = 2.0;
        AtualizarPreview();
    }

    [RelayCommand]
    public void AlinharBase()
    {
        if (ElementoSelecionado == null) return;
        RegistrarSnapshot();
        ElementoSelecionado.Y = Math.Max(0, Modelo.AlturaMm - ElementoSelecionado.Altura - 2.0);
        AtualizarPreview();
    }

    [RelayCommand]
    public void AvancarCamada()
    {
        if (ElementoSelecionado == null) return;
        var idx = Modelo.Elementos.IndexOf(ElementoSelecionado);
        if (idx >= 0 && idx < Modelo.Elementos.Count - 1)
        {
            RegistrarSnapshot();
            Modelo.Elementos.Move(idx, idx + 1);
            NotificarCamadasAlteradas();
            AtualizarPreview();
        }
    }

    [RelayCommand]
    public void RecuarCamada()
    {
        if (ElementoSelecionado == null) return;
        var idx = Modelo.Elementos.IndexOf(ElementoSelecionado);
        if (idx > 0)
        {
            RegistrarSnapshot();
            Modelo.Elementos.Move(idx, idx - 1);
            NotificarCamadasAlteradas();
            AtualizarPreview();
        }
    }

    [ObservableProperty]
    private double _mouseXMm;

    [ObservableProperty]
    private double _mouseYMm;

    public void AtualizarCoordenadasMouse(double xMm, double yMm)
    {
        MouseXMm = Math.Round(Math.Clamp(xMm, 0, Modelo.LarguraMm), 1);
        MouseYMm = Math.Round(Math.Clamp(yMm, 0, Modelo.AlturaMm), 1);
    }

    [RelayCommand]
    public void TrazerParaFrente()
    {
        if (ElementoSelecionado == null) return;
        var idx = Modelo.Elementos.IndexOf(ElementoSelecionado);
        if (idx >= 0 && idx < Modelo.Elementos.Count - 1)
        {
            RegistrarSnapshot();
            Modelo.Elementos.Move(idx, Modelo.Elementos.Count - 1);
            NotificarCamadasAlteradas();
            AtualizarPreview();
        }
    }

    [RelayCommand]
    public void EnviarParaTras()
    {
        if (ElementoSelecionado == null) return;
        var idx = Modelo.Elementos.IndexOf(ElementoSelecionado);
        if (idx > 0)
        {
            RegistrarSnapshot();
            Modelo.Elementos.Move(idx, 0);
            NotificarCamadasAlteradas();
            AtualizarPreview();
        }
    }

    [RelayCommand]
    public void InserirVariavel(string nomeVariavel)
    {
        if (!Enum.TryParse<VariavelEtiqueta>(nomeVariavel, out var variavel))
        {
            variavel = VariavelEtiqueta.NomeProduto;
        }

        if (variavel == VariavelEtiqueta.Tamanho)
        {
            AdicionarTamanho();
            return;
        }

        var (textoPadrao, largPadrao, altPadrao, fontePadrao, negrito) = variavel switch
        {
            VariavelEtiqueta.NomeProduto => ("{Nome}", Math.Min(Modelo.LarguraMm - 4, 46.0), 7.0, 9.5, true),
            VariavelEtiqueta.PrecoVarejo => ("R$ {Preco}", 28.0, 6.0, 13.0, true),
            VariavelEtiqueta.PrecoAtacado => ("ATACADO: R$ {PrecoAtacado}", 30.0, 4.0, 7.5, false),
            VariavelEtiqueta.Referencia => ("REF: {Ref}", 25.0, 3.5, 7.5, false),
            VariavelEtiqueta.Tamanho => ("TAM: {Tamanho}", 15.0, 4.5, 8.5, true),
            VariavelEtiqueta.CodigoBarras => ("{Codigo}", Math.Min(Modelo.LarguraMm - 4, 40.0), 8.0, 8.0, false),
            VariavelEtiqueta.CodigoInterno => ("CÓD: {CodigoInterno}", 25.0, 3.5, 7.5, false),
            VariavelEtiqueta.NomeLoja => ("NEXUS LS", Math.Min(Modelo.LarguraMm - 4, 46.0), 4.5, 8.5, true),
            _ => ("Novo Campo", 25.0, 5.0, 9.0, false)
        };

        var el = new ElementoDesign
        {
            Nome = $"{nomeVariavel} {Modelo.Elementos.Count + 1}",
            Tipo = variavel == VariavelEtiqueta.CodigoBarras ? TipoElemento.CodigoBarras : TipoElemento.Texto,
            Texto = textoPadrao,
            Variavel = variavel,
            X = 2.0,
            Y = Math.Max(2.0, Math.Round((Modelo.AlturaMm / 2 - altPadrao / 2) * 2) / 2.0),
            Largura = largPadrao,
            Altura = altPadrao,
            TamanhoFonte = fontePadrao,
            Negrito = negrito
        };

        Modelo.Elementos.Add(el);
        NotificarCamadasAlteradas();
        SelecionarElemento(el);
    }

    // =========================================================================
    // HUB DE NOVO DOCUMENTO (ESTILO ILLUSTRATOR / CORELDRAW)
    // =========================================================================
    [RelayCommand]
    public void AbrirNovoDocumento()
    {
        NovoDocCategoriaFiltro = "Todos";
        OnPropertyChanged(nameof(ModelosFiltradosNovoDoc));

        if (NovoDocPredefinicaoSelecionada == null && CatalogoModelos.Count > 0)
        {
            SelecionarPresetNovoDoc(CatalogoModelos[0]);
        }
        else if (NovoDocPredefinicaoSelecionada != null)
        {
            SelecionarPresetNovoDoc(NovoDocPredefinicaoSelecionada);
        }

        NovoDocumentoAberto = true;
    }

    [RelayCommand]
    public void FecharNovoDocumento()
    {
        NovoDocumentoAberto = false;
    }

    [RelayCommand]
    public void FiltrarCategoriaNovoDoc(string categoria)
    {
        NovoDocCategoriaFiltro = categoria;
        OnPropertyChanged(nameof(ModelosFiltradosNovoDoc));
    }

    [RelayCommand]
    public void SelecionarPresetNovoDoc(PredefinicaoEtiqueta preset)
    {
        if (preset == null) return;
        NovoDocPredefinicaoSelecionada = preset;
        NovoDocNome = preset.Nome;
        NovoDocLargura = preset.LarguraMm;
        NovoDocAltura = preset.AlturaMm;
        NovoDocIsPaisagem = preset.LarguraMm >= preset.AlturaMm;
        NovoDocTipo = preset.TipoSugerido;
    }

    [RelayCommand]
    public void AlternarOrientacaoNovoDoc(string modo)
    {
        var querPaisagem = string.Equals(modo, "Paisagem", StringComparison.OrdinalIgnoreCase);

        if (querPaisagem && NovoDocLargura < NovoDocAltura)
        {
            var temp = NovoDocLargura;
            NovoDocLargura = NovoDocAltura;
            NovoDocAltura = temp;
        }
        else if (!querPaisagem && NovoDocAltura < NovoDocLargura)
        {
            var temp = NovoDocLargura;
            NovoDocLargura = NovoDocAltura;
            NovoDocAltura = temp;
        }

        NovoDocIsPaisagem = querPaisagem;
    }

    [RelayCommand]
    public void AlternarTipoNovoDoc(string tipo)
    {
        NovoDocTipo = tipo;
    }

    [RelayCommand]
    public void ConfirmarCriarNovoDocumento()
    {
        try
        {
            if (NovoDocLargura <= 0) NovoDocLargura = 50.0;
            if (NovoDocAltura <= 0) NovoDocAltura = 30.0;
            if (string.IsNullOrWhiteSpace(NovoDocNome)) NovoDocNome = "Nova Etiqueta";

            ModeloEtiquetaDesign novoModelo;

            if (NovoDocTipo == "EmBranco")
            {
                novoModelo = ModeloEtiquetaDesign.CriarEmBranco(NovoDocLargura, NovoDocAltura, NovoDocNome);
            }
            else if (NovoDocTipo == "Composicao")
            {
                if (NovoDocPredefinicaoSelecionada != null &&
                    NovoDocPredefinicaoSelecionada.TipoSugerido == "Composicao" &&
                    Math.Abs(NovoDocPredefinicaoSelecionada.LarguraMm - NovoDocLargura) < 0.1 &&
                    Math.Abs(NovoDocPredefinicaoSelecionada.AlturaMm - NovoDocAltura) < 0.1)
                {
                    novoModelo = NovoDocPredefinicaoSelecionada.Fabrica();
                    novoModelo.Nome = NovoDocNome;
                }
                else
                {
                    novoModelo = ModeloEtiquetaDesign.CriarEtiquetaComposicaoGenerica(NovoDocLargura, NovoDocAltura, NovoDocNome);
                }
            }
            else
            {
                // Normal
                if (NovoDocPredefinicaoSelecionada != null &&
                    NovoDocPredefinicaoSelecionada.TipoSugerido == "Normal" &&
                    Math.Abs(NovoDocPredefinicaoSelecionada.LarguraMm - NovoDocLargura) < 0.1 &&
                    Math.Abs(NovoDocPredefinicaoSelecionada.AlturaMm - NovoDocAltura) < 0.1)
                {
                    novoModelo = NovoDocPredefinicaoSelecionada.Fabrica();
                    novoModelo.Nome = NovoDocNome;
                }
                else
                {
                    novoModelo = ModeloEtiquetaDesign.CriarEtiquetaNormalGenerica(NovoDocLargura, NovoDocAltura, NovoDocNome);
                }
            }

            Modelo = novoModelo;
            PresetSelecionado = Modelo.Nome;
            InscreverEventosModelo();
            ElementoSelecionado = null;
            AtualizarPreview();

            NovoDocumentoAberto = false;
        }
        catch (Exception ex)
        {
            _dialogo.Erro($"Erro ao criar novo documento: {ex.Message}");
        }
    }
}
