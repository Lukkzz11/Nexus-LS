using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using NexusLS.Core.Licenciamento;
using NexusLS.Core.Services;

namespace NexusLS.Setup;

public partial class JanelaSetup : Window
{
    private readonly OpcoesSetup _op;
    private readonly Instalador _instalador;
    private readonly string _hwid;
    private int _passoAtual = 1;
    private ResultadoLicencaNuvem? _licencaValidada;

    public JanelaSetup(OpcoesSetup op)
    {
        _op = op;
        _instalador = new Instalador(_op);
        _hwid = LicencaService.CodigoDaMaquina();

        InitializeComponent();

        TxtHwidRodape.Text = _hwid;
        TxtCaminhoPasta.Text = _op.Pasta;
        TxtTermosConteudo.Text = ObterTextoTermosDeUso();

        // Carrega chave salva localmente se houver
        if (!string.IsNullOrWhiteSpace(_op.ChaveLicenca))
        {
            TxtChave.Text = _op.ChaveLicenca;
        }
        else
        {
            var salva = _instalador.ServicoLicenca.AvaliarLocal();
            if (!string.IsNullOrWhiteSpace(salva.Chave))
            {
                TxtChave.Text = salva.Chave;
            }
        }

        AtualizarEspacoDisco();
        IrParaPasso(1);

        Loaded += JanelaSetup_Loaded;
    }

    private void JanelaSetup_Loaded(object sender, RoutedEventArgs e)
    {
        var args = Environment.GetCommandLineArgs();
        if (args.Any(a => a.Equals("--capturar-prints", StringComparison.OrdinalIgnoreCase)))
        {
            var idx = Array.FindIndex(args, a => a.Equals("--capturar-prints", StringComparison.OrdinalIgnoreCase));
            var pastaDestino = (idx >= 0 && idx < args.Length - 1 && !args[idx + 1].StartsWith("-"))
                ? args[idx + 1]
                : @"C:\Users\Lucas Emanuel\Desktop\Prints_Nexus";

            Dispatcher.InvokeAsync(async () =>
            {
                await Task.Delay(300);
                CapturarPrintsPassos(pastaDestino);
                var pastaArtefatos = @"C:\Users\Lucas Emanuel\.gemini\antigravity\brain\975d27ab-5c9a-4609-bb4c-9e018d33ea89\prints";
                CapturarPrintsPassos(pastaArtefatos);
                Application.Current.Shutdown(0);
            });
        }
    }

    private void IrParaPasso(int passo)
    {
        _passoAtual = passo;

        PanelPasso1.Visibility = passo == 1 ? Visibility.Visible : Visibility.Collapsed;
        PanelPasso2.Visibility = passo == 2 ? Visibility.Visible : Visibility.Collapsed;
        PanelPasso3.Visibility = passo == 3 ? Visibility.Visible : Visibility.Collapsed;
        PanelPasso4.Visibility = passo == 4 ? Visibility.Visible : Visibility.Collapsed;
        PanelPasso5.Visibility = passo == 5 ? Visibility.Visible : Visibility.Collapsed;
        PanelPasso6.Visibility = passo == 6 ? Visibility.Visible : Visibility.Collapsed;

        AtualizarStepper(passo);
        AtualizarEstadoBotoes();
    }

    private void AtualizarStepper(int passo)
    {
        AtualizarBadgeStep(BadgeStep1, TxtStep1, 1, passo);
        AtualizarBadgeStep(BadgeStep2, TxtStep2, 2, passo);
        AtualizarBadgeStep(BadgeStep3, TxtStep3, 3, passo);
        AtualizarBadgeStep(BadgeStep4, TxtStep4, 4, passo);
        AtualizarBadgeStep(BadgeStep5, TxtStep5, 5, passo);
    }

    private static void AtualizarBadgeStep(System.Windows.Controls.Border badge, System.Windows.Controls.TextBlock txt, int numeroStep, int passoAtual)
    {
        if (numeroStep == passoAtual)
        {
            badge.Background = new SolidColorBrush(Color.FromRgb(0xEF, 0xF6, 0xFF)); // Blue 50
            badge.BorderBrush = new SolidColorBrush(Color.FromRgb(0x93, 0xC5, 0xFD)); // Blue 300
            txt.Foreground = new SolidColorBrush(Color.FromRgb(0x1D, 0x4E, 0xD8)); // Blue 700
            txt.FontWeight = FontWeights.Bold;
        }
        else if (numeroStep < passoAtual)
        {
            badge.Background = new SolidColorBrush(Color.FromRgb(0xF0, 0xFD, 0xF4)); // Green 50
            badge.BorderBrush = new SolidColorBrush(Color.FromRgb(0xBB, 0xF7, 0xD0)); // Green 200
            txt.Foreground = new SolidColorBrush(Color.FromRgb(0x15, 0x80, 0x3D)); // Green 700
            txt.FontWeight = FontWeights.SemiBold;
        }
        else
        {
            badge.Background = new SolidColorBrush(Color.FromRgb(0xF1, 0xF5, 0xF9)); // Slate 100
            badge.BorderBrush = new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0)); // Slate 200
            txt.Foreground = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B)); // Slate 500
            txt.FontWeight = FontWeights.Normal;
        }
    }

    private void AtualizarEstadoBotoes()
    {
        if (BtnAvancar == null || BtnVoltar == null || BtnCancelar == null) return;

        BtnVoltar.Visibility = (_passoAtual > 1 && _passoAtual < 5) ? Visibility.Visible : Visibility.Collapsed;
        BtnCancelar.Visibility = _passoAtual < 5 ? Visibility.Visible : Visibility.Collapsed;

        if (_passoAtual == 5)
        {
            BtnAvancar.Visibility = Visibility.Collapsed;
            BtnVoltar.Visibility = Visibility.Collapsed;
            BtnCancelar.Visibility = Visibility.Collapsed;
            return;
        }

        if (_passoAtual == 6)
        {
            BtnAvancar.Visibility = Visibility.Visible;
            BtnAvancar.Content = "Concluir";
            BtnAvancar.Background = new SolidColorBrush(Color.FromRgb(0x16, 0xA3, 0x4A));
            BtnAvancar.BorderBrush = new SolidColorBrush(Color.FromRgb(0x16, 0xA3, 0x4A));
            BtnAvancar.IsEnabled = true;
            BtnVoltar.Visibility = Visibility.Collapsed;
            BtnCancelar.Visibility = Visibility.Collapsed;
            return;
        }

        BtnAvancar.Visibility = Visibility.Visible;

        switch (_passoAtual)
        {
            case 1:
                BtnAvancar.Content = "Avançar >";
                BtnAvancar.Background = new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB));
                BtnAvancar.BorderBrush = new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB));
                BtnAvancar.IsEnabled = _licencaValidada != null && _licencaValidada.Ativa;
                break;

            case 2:
                BtnAvancar.Content = "Avançar >";
                BtnAvancar.Background = new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB));
                BtnAvancar.BorderBrush = new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB));
                BtnAvancar.IsEnabled = ChkAceitoTermos.IsChecked == true;
                break;

            case 3:
                BtnAvancar.Content = "Avançar >";
                BtnAvancar.Background = new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB));
                BtnAvancar.BorderBrush = new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB));
                BtnAvancar.IsEnabled = !string.IsNullOrWhiteSpace(TxtCaminhoPasta.Text);
                break;

            case 4:
                BtnAvancar.Content = "Instalar Agora";
                BtnAvancar.Background = new SolidColorBrush(Color.FromRgb(0x16, 0xA3, 0x4A));
                BtnAvancar.BorderBrush = new SolidColorBrush(Color.FromRgb(0x16, 0xA3, 0x4A));
                BtnAvancar.IsEnabled = ChkNexus.IsChecked == true || ChkBarTender.IsChecked == true || ChkAtualizador.IsChecked == true;
                break;
        }
    }

    // ==================== EVENTOS DO PASSO 1 (LICENÇA) ====================

    private void TxtChave_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        _licencaValidada = null;
        AtualizarEstadoBotoes();
    }

    private async void BtnColarChave_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (Clipboard.ContainsText())
            {
                var texto = Clipboard.GetText()?.Trim();
                if (!string.IsNullOrWhiteSpace(texto))
                {
                    TxtChave.Text = texto.ToUpperInvariant();
                    await ValidarChaveAsync(mostrarAlerta: false);
                }
            }
        }
        catch { }
    }

    private async void BtnValidarChave_Click(object sender, RoutedEventArgs e)
    {
        await ValidarChaveAsync(mostrarAlerta: true);
    }

    private async Task<ResultadoLicencaNuvem?> ValidarChaveAsync(bool mostrarAlerta = true)
    {
        var chave = TxtChave.Text.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(chave))
        {
            TxtStatusLicenca.Text = "Digite ou cole a sua chave de ativação para verificar a permissão.";
            TxtStatusLicenca.Foreground = new SolidColorBrush(Color.FromRgb(0xB4, 0x53, 0x09));
            CardStatusLicenca.Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xFB, 0xEB));
            CardStatusLicenca.BorderBrush = new SolidColorBrush(Color.FromRgb(0xFD, 0xE6, 0x8A));
            TxtDetalhesLicenca.Visibility = Visibility.Collapsed;
            _licencaValidada = null;
            AtualizarEstadoBotoes();
            return null;
        }

        BtnValidarChave.IsEnabled = false;
        TxtStatusLicenca.Text = "Consultando servidor oficial Nexus para validar chave e vagas contratadas...";
        TxtStatusLicenca.Foreground = new SolidColorBrush(Color.FromRgb(0x1D, 0x4E, 0xD8));
        CardStatusLicenca.Background = new SolidColorBrush(Color.FromRgb(0xEF, 0xF6, 0xFF));
        CardStatusLicenca.BorderBrush = new SolidColorBrush(Color.FromRgb(0xBF, 0xDB, 0xFE));
        TxtDetalhesLicenca.Visibility = Visibility.Collapsed;

        try
        {
            var res = await _instalador.ValidarEAtivarLicencaAsync(chave);
            if (res.Ativa)
            {
                _licencaValidada = res;
                _op.ChaveLicenca = chave;
                if (!string.IsNullOrWhiteSpace(res.Empresa)) _op.Empresa = res.Empresa;

                CardStatusLicenca.Background = new SolidColorBrush(Color.FromRgb(0xF0, 0xFD, 0xF4));
                CardStatusLicenca.BorderBrush = new SolidColorBrush(Color.FromRgb(0xBB, 0xF7, 0xD0));
                TxtStatusLicenca.Foreground = new SolidColorBrush(Color.FromRgb(0x15, 0x80, 0x3D));
                TxtStatusLicenca.Text = $"Licença Autorizada! Cliente: {res.Cliente}" + (string.IsNullOrWhiteSpace(res.Empresa) ? "" : $" ({res.Empresa})");

                var validadeStr = res.Validade?.ToString("dd/MM/yyyy") ?? "Vitalícia";
                var vagasStr = (res.LimiteDispositivos ?? 0) > 0 ? $"{res.DispositivosUsados ?? 1} de {res.LimiteDispositivos} computadores vinculados" : "Dispositivos Ilimitados";
                TxtDetalhesLicenca.Text = $"Plano: {res.Plano ?? "Comercial"} • Validade: {validadeStr} • {vagasStr}\nComputador autorizado com sucesso. Clique em 'Avançar >' para prosseguir.";
                TxtDetalhesLicenca.Visibility = Visibility.Visible;
            }
            else
            {
                _licencaValidada = null;
                CardStatusLicenca.Background = new SolidColorBrush(Color.FromRgb(0xFE, 0xF2, 0xF2));
                CardStatusLicenca.BorderBrush = new SolidColorBrush(Color.FromRgb(0xFE, 0xCA, 0xCA));
                TxtStatusLicenca.Foreground = new SolidColorBrush(Color.FromRgb(0x99, 0x1B, 0x1B));
                TxtStatusLicenca.Text = $"Licença Não Autorizada: {res.Mensagem}";
                TxtDetalhesLicenca.Text = $"Situação: {res.Estado} • HWID deste computador: {_hwid}";
                TxtDetalhesLicenca.Visibility = Visibility.Visible;

                if (mostrarAlerta)
                {
                    MessageBox.Show(this, $"A chave de licença não pôde ser ativada:\n\n{res.Mensagem}\n\nCódigo HWID deste PC: {_hwid}", "Licença Recusada", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }

            AtualizarEstadoBotoes();
            return res;
        }
        catch (Exception ex)
        {
            _licencaValidada = null;
            CardStatusLicenca.Background = new SolidColorBrush(Color.FromRgb(0xFE, 0xF2, 0xF2));
            CardStatusLicenca.BorderBrush = new SolidColorBrush(Color.FromRgb(0xFE, 0xCA, 0xCA));
            TxtStatusLicenca.Foreground = new SolidColorBrush(Color.FromRgb(0x99, 0x1B, 0x1B));
            TxtStatusLicenca.Text = $"Falha de Conexão: {ex.Message}";
            TxtDetalhesLicenca.Visibility = Visibility.Collapsed;

            if (mostrarAlerta)
            {
                MessageBox.Show(this, $"Erro ao conectar no servidor de licenças:\n\n{ex.Message}", "Erro de Conexão", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            AtualizarEstadoBotoes();
            return null;
        }
        finally
        {
            BtnValidarChave.IsEnabled = true;
        }
    }

    // ==================== EVENTOS DO PASSO 2 (TERMOS) ====================

    private void ChkAceitoTermos_CheckedChanged(object sender, RoutedEventArgs e)
    {
        AtualizarEstadoBotoes();
    }

    // ==================== EVENTOS DO PASSO 3 (LOCAL) ====================

    private void TxtCaminhoPasta_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        AtualizarEspacoDisco();
        AtualizarEstadoBotoes();
    }

    private void BtnProcurarPasta_Click(object sender, RoutedEventArgs e)
    {
        var ofd = new OpenFolderDialog
        {
            Title = "Selecionar Pasta de Instalação do Nexus",
            InitialDirectory = Directory.Exists(TxtCaminhoPasta.Text) ? TxtCaminhoPasta.Text : OpcoesSetup.PastaPadrao
        };

        if (ofd.ShowDialog(this) == true && !string.IsNullOrWhiteSpace(ofd.FolderName))
        {
            TxtCaminhoPasta.Text = ofd.FolderName;
        }
    }

    private void AtualizarEspacoDisco()
    {
        try
        {
            var pasta = string.IsNullOrWhiteSpace(TxtCaminhoPasta?.Text) ? OpcoesSetup.PastaPadrao : TxtCaminhoPasta.Text;
            var raiz = Path.GetPathRoot(Path.GetFullPath(pasta));
            if (!string.IsNullOrWhiteSpace(raiz) && Directory.Exists(raiz))
            {
                var drive = new DriveInfo(raiz);
                var gb = drive.AvailableFreeSpace / (1024.0 * 1024.0 * 1024.0);
                if (TxtInfoDisco != null)
                {
                    TxtInfoDisco.Text = $"Espaço livre na unidade ({drive.Name}): {gb:F1} GB";
                }
            }
        }
        catch { }
    }

    // ==================== EVENTOS DO PASSO 4 (APPS) ====================

    private void ChkApp_Changed(object sender, RoutedEventArgs e)
    {
        AtualizarEstadoBotoes();
    }

    // ==================== BOTÕES DE NAVEGAÇÃO ====================

    private void BtnVoltar_Click(object sender, RoutedEventArgs e)
    {
        if (_passoAtual > 1)
        {
            IrParaPasso(_passoAtual - 1);
        }
    }

    private async void BtnAvancar_Click(object sender, RoutedEventArgs e)
    {
        if (_passoAtual == 1)
        {
            if (_licencaValidada == null || !_licencaValidada.Ativa)
            {
                var res = await ValidarChaveAsync(mostrarAlerta: true);
                if (res == null || !res.Ativa) return;
            }
            IrParaPasso(2);
        }
        else if (_passoAtual == 2)
        {
            if (ChkAceitoTermos.IsChecked != true) return;
            IrParaPasso(3);
        }
        else if (_passoAtual == 3)
        {
            _op.Pasta = TxtCaminhoPasta.Text.Trim();
            if (string.IsNullOrWhiteSpace(_op.Pasta)) return;
            IrParaPasso(4);
        }
        else if (_passoAtual == 4)
        {
            _op.InstalarNexusLS = ChkNexus.IsChecked == true;
            _op.InstalarBarTender = ChkBarTender.IsChecked == true;
            _op.InstalarAtualizador = ChkAtualizador.IsChecked == true;
            _op.CriarAtalhosDesktop = ChkAtalhos.IsChecked == true;

            if (!_op.InstalarNexusLS && !_op.InstalarBarTender && !_op.InstalarAtualizador)
            {
                MessageBox.Show(this, "Selecione pelo menos um módulo para instalar.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_instalador.HaProgramaAberto())
            {
                MessageBox.Show(this, "Feche todos os programas do Nexus que estiverem abertos antes de continuar com a instalação.", "Programas em Execução", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            IrParaPasso(5);
            await ExecutarInstalacaoAsync();
        }
        else if (_passoAtual == 6)
        {
            Close();
        }
    }

    private void BtnCancelar_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "Deseja realmente cancelar a instalação do Nexus?", "Cancelar Instalação", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            Close();
        }
    }

    private async Task ExecutarInstalacaoAsync()
    {
        var progresso = new Progress<(double Percentual, string Texto)>(p =>
        {
            BarraProgresso.Value = p.Percentual;
            TxtPercentualProgresso.Text = $"{p.Percentual:F0}%";
            TxtMensagemProgresso.Text = p.Texto;
            TxtDetalhesProgresso.Text = $"Concluído: {p.Percentual:F0}% • Aguarde a finalização dos pacotes...";
        });

        try
        {
            await _instalador.InstalarAsync(progresso);

            BarraProgresso.Value = 100;
            TxtPercentualProgresso.Text = "100%";
            TxtMensagemProgresso.Text = "Instalação concluída com sucesso!";

            // Abre o Nexus Hub automaticamente ao terminar
            var caminhoAtualizador = Path.Combine(_op.Pasta, "atualizador.exe");
            if (File.Exists(caminhoAtualizador))
            {
                try
                {
                    Process.Start(new ProcessStartInfo(caminhoAtualizador) { UseShellExecute = true });
                    TxtConclusaoInfo.Text = "Os programas foram instalados com êxito e o Nexus Hub foi aberto automaticamente como sua central de programas.";
                }
                catch
                {
                    TxtConclusaoInfo.Text = "Os programas foram instalados com êxito! Clique abaixo para iniciar o Nexus Hub.";
                }
            }
            else
            {
                TxtConclusaoInfo.Text = "Os programas foram instalados e ativados com êxito neste computador.";
            }

            IrParaPasso(6);
        }
        catch (Exception ex)
        {
            BarraProgresso.Foreground = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));
            TxtMensagemProgresso.Text = $"Erro durante a instalação: {ex.Message}";
            TxtMensagemProgresso.Foreground = new SolidColorBrush(Color.FromRgb(0x99, 0x1B, 0x1B));

            MessageBox.Show(this, $"Ocorreu uma falha durante a instalação:\n\n{ex.Message}", "Falha na Instalação", MessageBoxButton.OK, MessageBoxImage.Error);
            IrParaPasso(4);
        }
    }

    private void BtnConcluirAbrirHub_Click(object sender, RoutedEventArgs e)
    {
        var caminhoAtualizador = Path.Combine(_op.Pasta, "atualizador.exe");
        if (File.Exists(caminhoAtualizador))
        {
            try
            {
                Process.Start(new ProcessStartInfo(caminhoAtualizador) { UseShellExecute = true });
            }
            catch { }
        }
        Close();
    }

    private void BtnCopiarHwidRodape_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(_hwid);
            BtnCopiarHwidRodape.Content = "Copiado!";
        }
        catch { }
    }

    private static string ObterTextoTermosDeUso()
    {
        return
            "CONTRATO DE LICENÇA DE USO DE SOFTWARE — NEXUS\n\n" +
            "1. CONCESSÃO DA LICENÇA: A Nexus concede ao cliente uma licença comercial, intransferível e não exclusiva para utilizar o software Nexus LS e seus módulos correspondentes (Frente de Caixa PDV, BarTender e Hub Atualizador) de acordo com o plano contratado.\n\n" +
            "2. CONTROLE DE DISPOSITIVOS E HWID: O uso é vinculado ao identificador de hardware (HWID) de cada computador autorizado. O número de terminais operando simultaneamente não poderá exceder o limite estabelecido no plano da licença.\n\n" +
            "3. PROTEÇÃO DE DADOS: O software processa e armazena os dados de operações e estoque com criptografia e segurança, atendendo às diretrizes da LGPD (Lei Geral de Proteção de Dados).\n\n" +
            "4. ATUALIZAÇÕES E SUPORTE: O cliente tem direito a atualizações de segurança e correções durante o período de vigência da licença comercial ativa.\n\n" +
            "5. RESPONSABILIDADE: O usuário é responsável pela guarda de suas senhas, cópias de segurança (backups) e pela regularidade fiscal de suas operações comerciais.";
    }

    public void CapturarPrintsPassos(string pastaDestino)
    {
        try
        {
            Directory.CreateDirectory(pastaDestino);

            Width = 860;
            Height = 640;
            Show();
            UpdateLayout();

            for (int passo = 1; passo <= 6; passo++)
            {
                IrParaPasso(passo);

                if (passo == 1)
                {
                    TxtChave.Text = "NEXUS-QP7V-577T-8MFL";
                    CardStatusLicenca.Visibility = Visibility.Visible;
                    CardStatusLicenca.Background = new SolidColorBrush(Color.FromRgb(0xF0, 0xFD, 0xF4));
                    CardStatusLicenca.BorderBrush = new SolidColorBrush(Color.FromRgb(0xBB, 0xF7, 0xD0));
                    TxtStatusLicenca.Text = "Licença Comercial Válida e Ativa";
                    TxtStatusLicenca.Foreground = new SolidColorBrush(Color.FromRgb(0x15, 0x80, 0x3D));
                    TxtDetalhesLicenca.Text = "Cliente: Life Surf (Jarbas) | Plano Anual | Válida até 06/11/2027";
                    TxtDetalhesLicenca.Foreground = new SolidColorBrush(Color.FromRgb(0x16, 0x65, 0x34));
                }
                else if (passo == 2)
                {
                    ChkAceitoTermos.IsChecked = true;
                }
                else if (passo == 3)
                {
                    TxtCaminhoPasta.Text = @"C:\Users\Lucas Emanuel\AppData\Local\Programs\Nexus";
                }
                else if (passo == 4)
                {
                    ChkNexus.IsChecked = true;
                    ChkBarTender.IsChecked = true;
                    ChkAtualizador.IsChecked = true;
                    ChkAtalhos.IsChecked = true;
                }
                else if (passo == 5)
                {
                    BarraProgresso.Value = 68;
                    TxtPercentualProgresso.Text = "68%";
                    TxtMensagemProgresso.Text = "Descompactando pacotes do sistema Nexus LS...";
                    TxtDetalhesProgresso.Text = "Extraindo módulos e configurando atalhos na Área de Trabalho...";
                }

                UpdateLayout();
                Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);

                int w = (int)RootGrid.ActualWidth;
                int h = (int)RootGrid.ActualHeight;
                if (w <= 0) w = 844;
                if (h <= 0) h = 601;

                var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(RootGrid);

                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));

                var nomeArquivo = passo switch
                {
                    1 => "Nexus_Setup_01_Licenca.png",
                    2 => "Nexus_Setup_02_Termos.png",
                    3 => "Nexus_Setup_03_Pasta.png",
                    4 => "Nexus_Setup_04_Modulos.png",
                    5 => "Nexus_Setup_05_Instalacao.png",
                    _ => "Nexus_Setup_06_Conclusao.png"
                };

                var caminho = Path.Combine(pastaDestino, nomeArquivo);
                using var fs = File.Create(caminho);
                encoder.Save(fs);
            }

            Close();
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(pastaDestino, "erro_captura_setup.txt"), ex.ToString());
        }
    }
}
