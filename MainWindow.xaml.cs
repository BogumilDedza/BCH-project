using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.IO.Ports;
using System.IO;
using Renci.SshNet;

using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Drawing.Geometries;
using System.Collections.ObjectModel;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;
using LiveChartsCore.Measure;
using System.Windows.Threading;
using System.Diagnostics;
using System.Windows.Ink;
using LiveChartsCore.Defaults;

namespace BCH_PROJEKT
{
    public partial class MainWindow : Window
    {
        private CancellationTokenSource berCancellationToken;
        private CancellationTokenSource bitAddingCancellationToken;
        private bool BCHCodingEnable = false;
        private bool FastMode = true;
        private bool noiseGenerationEnabled = false;
        private ViewModel viewModel;
 
        private bool bitErrorEnabled = false;
        
        private bool IsFastModeEnabled = false;
        private bool IsNoiseGenerationEnabled = false;
        private DispatcherTimer sshPingTimer;
        private bool isSshConnected = false;
        private bool isPingRunning = false;

        private string sshHost = "";
        private string sshUser = "username";
        private string sshPassword = "password";
        private string bashScriptPath = "/home/username/script.sh";
        private SshClient sshClient;

        private int berDataPointIndex = 0;
        public MainWindow()
        {
            InitializeComponent();
            StartSshPing();
            viewModel = new ViewModel();
            DataContext = viewModel;
            

            SshHostTextBox.Text = "IP";
            SshUserTextBox.Text = "username";
            SshPasswordBox.Password = "password";
        }

        //Ustawienia do SSH, wpisujesz wszystkie dane do niego
        private async void ApplySshSettings_Click(object sender, RoutedEventArgs e)
        {
            sshHost = SshHostTextBox.Text.Trim();
            sshUser = SshUserTextBox.Text.Trim();
            sshPassword = SshPasswordBox.Password.Trim();

           

            if (string.IsNullOrEmpty(sshHost) || sshHost == "IP" ||
                string.IsNullOrEmpty(sshUser) || sshUser == "username" ||
                string.IsNullOrEmpty(sshPassword) || sshPassword == "password")
            {
                MessageBox.Show("Please enter valid SSH credentials.");
                return;
            }

            // Próba połączenia
            bool connected = await Task.Run(() =>
            {
                try
                {
                    sshClient?.Dispose();
                    sshClient = new SshClient(sshHost, sshUser, sshPassword);
                    sshClient.Connect();
                    bool ok = false;
                    if (sshClient.IsConnected)
                    {
                        using var cmd = sshClient.CreateCommand("echo ok");
                        var result = cmd.Execute().Trim();
                        ok = result == "ok";
                        
                    }

                    return ok;
                }
                catch
                {
                    return false;
                }
            });

            if (connected)
            {
                isSshConnected = true;
                UpdateConnectionStatus(true);
                MessageBox.Show("SSH connection successful.");
            }
            else
            {
                isSshConnected = false;
                UpdateConnectionStatus(false);
                MessageBox.Show("SSH connection failed. Check credentials.");
            }
        }

        private void ResetSshConnection()
        {
            try
            {
                if (sshClient != null)
                {
                    if (sshClient.IsConnected)
                        sshClient.Disconnect();

                    sshClient.Dispose();
                    sshClient = null;
                }

                isSshConnected = false;
                UpdateConnectionStatus(false);

                // Opcjonalne: wyczyść pola
                SshHostTextBox.Text = "";
                SshUserTextBox.Text = "";
                SshPasswordBox.Password = "";
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error resetting SSH connection: " + ex.Message);
            }
        }

        // tutaj flaga czyli BCH,FAST NOISE BIT ERROR
        private byte BuildFlagsByte(bool bch, bool fast, bool noise, bool bitError)
        {
            byte flags = 0;
            if (bch) flags |= 1 << 7;
            if (fast) flags |= 1 << 6;
            if (noise) flags |= 1 << 5;
            if (bitError) flags |= 1 << 4;
            return flags;
        }

        //Wysyłanie i odbieranie danych 
        private async void SendCommandButton_Click(object sender, RoutedEventArgs e)
        {
            string userInput = Box.Text.Trim();

            if (string.IsNullOrEmpty(userInput) || userInput.Length > 8)
            {
                RecivedTextBox.Text = "Wrong Input: input must be 1-8 characters.";
                return;
            }

            // Wyczyść poprzednie dane
            viewModel.SentBits.Clear();
            viewModel.ReceivedBits.Clear();
            RecivedTextBox.Clear();

            // === WYKRES 1: DANE WYSŁANE ===
            foreach (char c in userInput)
            {
                string binary = Convert.ToString((byte)c, 2).PadLeft(8, '0');// Zamienia stringa na bytr i zmienia go na reprezentacje binarną i pod koniec uzupełnia zerami żeby zawsze było 8 znaków
                await AddBitsToSeries(viewModel.SentBits, binary, 400);
            }

            string receivedText;

            // === SPRAWDŹ CZY POŁĄCZONY Z SSH ===
            if (isSshConnected && sshClient != null && sshClient.IsConnected)
            {
                //Ustawienia byte 
                byte flags = BuildFlagsByte(BCHCodingEnable, FastMode, noiseGenerationEnabled, bitErrorEnabled);
                byte density = (byte)DensitySlider.Value;
                byte bitErrorValue = (byte)BitErrorSlider.Value;
                string commandArgs = $"{userInput} {flags} {density} {bitErrorValue}";

                receivedText = await Task.Run(() =>
                {
                    try
                    {
                        if (!sshClient.IsConnected)
                            sshClient.Connect();

                        using var cmd = sshClient.CreateCommand($"{bashScriptPath} {commandArgs}");
                        var result = cmd.Execute();

                        // Wyciągnij tylko tekst z wyniku SSH 
                        string[] lines = result.Split('\n');
                        return lines.FirstOrDefault(line => !string.IsNullOrWhiteSpace(line))?.Trim() ?? userInput;//zwraca tekst użytkownika jeśli coś pójdzie nie tak
                    }
                    catch (Exception ex)
                    {
                        Dispatcher.Invoke(() => {
                            MessageBox.Show("SSH command error: " + ex.Message);
                        });
                        return userInput; // Zwróć oryginalny tekst w przypadku błędu
                    }
                });
            }
            else
            {
                // SYMULACJA 
                await Task.Delay(300); // Symuluj opóźnienie
                receivedText = SimulateProcessing(userInput);
            }

            // === WYKRES 2: DANE OTRZYMANE ===
            foreach (char c in receivedText)
            {
                string binary = Convert.ToString((byte)c, 2).PadLeft(8, '0');// Zamienia stringa na bytr i zmienia go na reprezentacje binarną i pod koniec uzupełnia zerami żeby zawsze było 8 znaków
                await AddBitsToSeries(viewModel.ReceivedBits, binary, 400);
            }

            
            RecivedTextBox.AppendText($"{receivedText}\n");


            viewModel.BerValues.Clear();

            for (int i = 0; i < 30; i++) // Pętla do wykonania wykresu BER
            {
                List<int> sentBits = new List<int>();
                List<int> receivedBits = new List<int>();

                string received;
                // === SPRAWDŹ CZY POŁĄCZONY Z SSH ===
                if (isSshConnected && sshClient != null && sshClient.IsConnected)
                {   //Ustawienia byte 
                    byte flags = BuildFlagsByte(BCHCodingEnable, FastMode, noiseGenerationEnabled, bitErrorEnabled);
                    byte density = (byte)DensitySlider.Value;
                    byte bitErrorValue = (byte)BitErrorSlider.Value;
                    string commandArgs = $"{userInput} {flags} {density} {bitErrorValue}";

                    received = await Task.Run(() =>
                    {
                        using var cmd = sshClient.CreateCommand($"{bashScriptPath} {commandArgs}");
                        var result = cmd.Execute();
                        string[] lines = result.Split('\n');
                        return lines.FirstOrDefault(line => !string.IsNullOrWhiteSpace(line))?.Trim() ?? userInput; 
                    });
                }
                else
                {
                    await Task.Delay(100);
                    received = SimulateProcessing(userInput);
                }

                foreach (char c in userInput)
                {
                    string binary = Convert.ToString((byte)c, 2).PadLeft(8, '0');
                    sentBits.AddRange(binary.Select(b => b == '1' ? 1 : 0));//sprawdzanie znaku b w ciągu binarny jeśli 1 to dodaj do listy 1, jeśli 0 to dodaj do listy 0
                }

                foreach (char c in received)
                {
                    string binary = Convert.ToString((byte)c, 2).PadLeft(8, '0');
                    receivedBits.AddRange(binary.Select(b => b == '1' ? 1 : 0));//sprawdzanie znaku b w ciągu binarny jeśli 1 to dodaj do listy 1, jeśli 0 to dodaj do listy 0
                }

                double ber = CalculateBERFromBits(sentBits, receivedBits);

                await Dispatcher.InvokeAsync(() =>
                {
                    viewModel.BerValues.Add(ber);
                    if (viewModel.BerValues.Count > 100)
                        viewModel.BerValues.RemoveAt(0);
                });

                await Task.Delay(500);
            }

           
        }

        // Dodaj tę funkcję do symulacji (gdy nie ma SSH):
        private string SimulateProcessing(string input)
        {
            Random random = new Random();
            char[] result = input.ToCharArray();

            // Oblicz prawdopodobieństwo błędu
            double errorChance = 0.0;

            if (noiseGenerationEnabled)
            {
                errorChance += DensitySlider.Value / 100.0 *2.0; 
            }

            if (bitErrorEnabled)
            {
                errorChance += BitErrorSlider.Value / 100.0 * 2.0; 
            }

            // Wprowadź błędy
            for (int i = 0; i < result.Length; i++)
            {
                if (random.NextDouble() < errorChance)
                {
                    // Zmień na losową literę lub cyfrę
                    if (random.NextDouble() < 0.5)
                        result[i] = (char)random.Next(97, 123); // a-z
                    else
                        result[i] = (char)random.Next(65, 91);  // A-Z
                }
            }

            // Jeśli BCH włączone - skoryguj część błędów
            if (BCHCodingEnable)
            {
                char[] original = input.ToCharArray();
                for (int i = 0; i < result.Length; i++)
                {
                    if (result[i] != original[i] && random.NextDouble() < 0.7) // 70% korekcji
                    {
                        result[i] = original[i]; // Przywróć oryginalny znak
                    }
                }
            }

            return new string(result);
        }





        //Rysowanie na wykresie 
        private async Task AddBitsToSeries(ObservableCollection<int> targetCollection, string binaryString, int delayMs)
        {
            bitAddingCancellationToken?.Cancel();
            bitAddingCancellationToken = new CancellationTokenSource();
            var token = bitAddingCancellationToken.Token;
            //tutaj rysuje bity an wykresie 
            foreach (char bit in binaryString)
            {
                if (token.IsCancellationRequested) break;
                int valueBit;
                if (bit == '1')
                {
                    valueBit = 1;
                }
                else
                {
                    valueBit = 0;
                }

                await Dispatcher.InvokeAsync(() =>
                {
                    if (targetCollection.Count > 100)
                        targetCollection.RemoveAt(0);
                    targetCollection.Add(valueBit);
                });

                await Task.Delay(delayMs);
            }
        }

        



        private async Task RunBerTest(string userInput, int iterations = 30, int delayMs = 500)
        {
            berCancellationToken?.Cancel();
            berCancellationToken = new CancellationTokenSource();
            var token = berCancellationToken.Token;

            viewModel.BerValues.Clear();

            for (int i = 0; i < iterations; i++)
            {
                if (token.IsCancellationRequested) break;

                // Wyczyść poprzednie bity przed nowym testem
                viewModel.SentBits.Clear();
                viewModel.ReceivedBits.Clear();

                // Wysyłanie i odbieranie
                // --- wysyłanie
                foreach (char c in userInput)
                {
                    string binary = Convert.ToString((byte)c, 2).PadLeft(8, '0');
                    await AddBitsToSeries(viewModel.SentBits, binary, 10);
                }

                string receivedText;

                if (isSshConnected && sshClient != null && sshClient.IsConnected)
                {
                    byte flags = BuildFlagsByte(BCHCodingEnable, FastMode, noiseGenerationEnabled, bitErrorEnabled);
                    byte density = (byte)DensitySlider.Value;
                    byte bitErrorValue = (byte)BitErrorSlider.Value;
                    string commandArgs = $"{userInput} {flags} {density} {bitErrorValue}";

                    receivedText = await Task.Run(() =>
                    {
                        if (!sshClient.IsConnected)
                            sshClient.Connect();

                        using var cmd = sshClient.CreateCommand($"{bashScriptPath} {commandArgs}");
                        var result = cmd.Execute();
                        string[] lines = result.Split('\n');
                        return lines.FirstOrDefault(line => !string.IsNullOrWhiteSpace(line))?.Trim() ?? userInput;
                    });
                }
                else
                {
                    await Task.Delay(100);
                    receivedText = SimulateProcessing(userInput);
                }

                // --- odbieranie
                foreach (char c in receivedText)
                {
                    string binary = Convert.ToString((byte)c, 2).PadLeft(8, '0');
                    await AddBitsToSeries(viewModel.ReceivedBits, binary, 10);
                }

                // Oblicz BER dla tej iteracji
                double ber = CalculateBERFromBits(viewModel.SentBits, viewModel.ReceivedBits);

                // Dodaj BER do kolekcji na UI
                await Dispatcher.InvokeAsync(() =>
                {
                    viewModel.BerValues.Add(ber);
                    if (viewModel.BerValues.Count > 100)
                        viewModel.BerValues.RemoveAt(0);
                });

                await Task.Delay(delayMs);
            }
        }
        private double CalculateBERFromBits(IList<int> sentBits, IList<int> receivedBits)
        {
            int length = Math.Min(sentBits.Count, receivedBits.Count);
            if (length == 0) return 0;

            int errorCount = 0;

            for (int i = 0; i < length; i++)
            {
                if (sentBits[i] != receivedBits[i])
                    errorCount++;
            }

            return (double)errorCount / length;
        }

        //Tutaj mamy funkcje ping sprawdzajaca connectivity 
        private void StartSshPing()
        {
            sshPingTimer = new DispatcherTimer();
            sshPingTimer.Interval = TimeSpan.FromSeconds(4);
            sshPingTimer.Tick += SshPingTimer_Tick;
            sshPingTimer.Start();
        }


        private void SshPingTimer_Tick(object? sender, EventArgs e)
        {
            if (isPingRunning) return;
            isPingRunning = true;
            Task.Run(() =>
            {
                bool isConnected = false;

                try
                {
                    if (sshClient != null && sshClient.IsConnected)
                    {
                        using var cmd = sshClient.CreateCommand("echo ok");
                        var result = cmd.Execute();
                        isConnected = result.Trim() == "ok";
                    }
                }
                catch
                {
                    isConnected = false;
                }

                Dispatcher.Invoke(() =>
                {
                    UpdateConnectionStatus(isConnected);
                    isPingRunning = false;
                });
            });
        }

        // Informacja wizualna o connectivity
        private void UpdateConnectionStatus(bool isConnected)
        {
            if (ConnectionStatusDot != null)
            {
                ConnectionStatusDot.Fill = isConnected ? Brushes.Green : Brushes.Red;
            }
        }

        //Reset Button
        private void ResetCommandButton_Click(object sender, RoutedEventArgs e)
        {
            bitAddingCancellationToken?.Cancel();
            Box.Clear();
            RecivedTextBox.Clear();
            viewModel.SentBits.Clear();
            viewModel.ReceivedBits.Clear();
           
        }

        //Wszystkie funkcje obsługujące Buttony
        private void BchYesButton_Click(object sender, RoutedEventArgs e)
        {
            BCHCodingEnable = true;
            SetBchButtons(true);
        }

       
    
    private void BchNoButton_Click(object sender, RoutedEventArgs e)
        {
            BCHCodingEnable = false;
            SetBchButtons(false);
        }

        private void FastButton_Click(object sender, RoutedEventArgs e)
        {
            FastMode = true;
            IsFastModeEnabled = true;
            SetCodingTypeButtons(true);
        }

        private void SlowButton_Click(object sender, RoutedEventArgs e)
        {
            FastMode = false;
            IsFastModeEnabled = false;
            SetCodingTypeButtons(false);
        }

        private void GaussianNoiseButton_Click(object sender, RoutedEventArgs e)
        {
            noiseGenerationEnabled = true;
            IsNoiseGenerationEnabled = true;

            GaussianNoiseButton.Visibility = Visibility.Visible;
            BitErrorGeneratorButton.Visibility = Visibility.Visible;

            DensitySlider.Visibility = Visibility.Visible;
            BitErrorSlider.Visibility = Visibility.Collapsed;

            GaussianOptionsPanel.Visibility = Visibility.Visible;
            BitErrorOptionsPanel.Visibility = Visibility.Collapsed;

            SetNoiseButtons(true);
        }

        private void BitErrorGeneratorButton_Click(object sender, RoutedEventArgs e)
        {
            noiseGenerationEnabled = true;
            IsNoiseGenerationEnabled = true;

            GaussianOptionsPanel.Visibility = Visibility.Collapsed;
            BitErrorOptionsPanel.Visibility = Visibility.Visible;

            DensitySlider.Visibility = Visibility.Collapsed;
            BitErrorSlider.Visibility = Visibility.Visible;

            SetNoiseButtons(true);
        }

        private void NoiseYesButton_Click(object sender, RoutedEventArgs e)
        {
            noiseGenerationEnabled = true;
            IsNoiseGenerationEnabled = true;
            SetNoiseButtons(true);
        }

        private void NoiseNoButton_Click(object sender, RoutedEventArgs e)
        {
            noiseGenerationEnabled = false;
            IsNoiseGenerationEnabled = false;
            SetNoiseButtons(false);

            GaussianOptionsPanel.Visibility = Visibility.Collapsed;
            BitErrorOptionsPanel.Visibility = Visibility.Collapsed;

            DensitySlider.Value = 0;
            BitErrorSlider.Value = 0;
        }

        private void ResetSshSettings_Click(object sender, RoutedEventArgs e)
        {
            ResetSshConnection();
           
        }

        //Funkcje obsługujace zmiane koloru po wybraniu buttonu 
        private void SetBchButtons(bool bchEnable)
        {
            if (BchYesButton != null && BchNoButton != null)
            {
                if (bchEnable)
                {
                    BchYesButton.Background = Brushes.Green;
                    BchNoButton.ClearValue(Button.BackgroundProperty);
                }
                else
                {
                    BchYesButton.ClearValue(Button.BackgroundProperty);
                    BchNoButton.Background = Brushes.Green;
                }
            }
        }

        private void SetCodingTypeButtons(bool fastEnable)
        {
            if (FastButton != null && SlowButton != null)
            {
                if (fastEnable)
                {
                    FastButton.Background = Brushes.Green;
                    SlowButton.ClearValue(Button.BackgroundProperty);
                }
                else
                {
                    FastButton.ClearValue(Button.BackgroundProperty);
                    SlowButton.Background = Brushes.Green;
                }
            }
        }

        private void SetNoiseButtons(bool noiseEnable)
        {
            if (NoiseYesButton != null && NoiseNoButton != null)
            {
                if (noiseEnable)
                {
                    NoiseYesButton.Background = Brushes.Green;
                    NoiseNoButton.ClearValue(Button.BackgroundProperty);
                }
                else
                {
                    NoiseYesButton.ClearValue(Button.BackgroundProperty);
                    NoiseNoButton.Background = Brushes.Green;
                }
            }
        }

        

        //Tutaj wszystkie dane potrzebne do wykresu
        public class ViewModel
        {

           


            public ObservableCollection<int> SentBits { get; set; } = new ObservableCollection<int>();
            public ObservableCollection<int> ReceivedBits { get; set; } = new ObservableCollection<int>();
            public ObservableCollection<double> BerValues{ get; set; } = new ObservableCollection<double>();
            public ISeries[] SentSeries { get; set; }
            public ISeries[] RecivedtSeries { get; set; }
            
            public ISeries[] BerSeries { get; set; }
            public Axis[] X1 { get; set; }
            public Axis[] X2 { get; set; }
            public Axis[] Y1 { get; set; }
            public Axis[] Y2 { get; set; }

            public Axis[] BerX { get; set; }
            public Axis[] BerY { get; set; }


            public Margin DrawMargin { get; set; }


            public ViewModel()
            {
                SentSeries = new ISeries[]
                {
                new StepLineSeries<int>
                {
                Values = SentBits,
                    Name = "Sent",
                    Fill = null,
                   
                    GeometrySize = 10,
                    Stroke = new SolidColorPaint(SKColors.Green) { StrokeThickness = 2 },
                    GeometryStroke= new SolidColorPaint(SKColors.Green){ StrokeThickness = 2 }
                }
                };

                RecivedtSeries = new ISeries[]
                {
                new StepLineSeries<int>
                {
                Values = ReceivedBits,
                    Name = "Recived",
                    
                    Fill = null,
                    GeometrySize = 10,
                    Stroke = new SolidColorPaint(SKColors.Red) { StrokeThickness = 2 },
                    GeometryStroke= new SolidColorPaint(SKColors.Red){ StrokeThickness = 2 }
                } };

                BerSeries = new ISeries[]
        {
            new LineSeries<double>
            {
                Values = BerValues,
                Name = "BER",
                Stroke = new SolidColorPaint(SKColors.Blue) { StrokeThickness = 2 },
                GeometrySize = 6,
                Fill = null,
                GeometryStroke = new SolidColorPaint(SKColors.Blue) { StrokeThickness = 2 }
                }
                };

                X1 = new Axis[]
            {
                new Axis
                {
                    MinLimit = -0.5,
                    MaxLimit = 65,
                    MinStep = 1,
                    Labeler = value => value.ToString("0"),
                    Name ="Sample",
                    SeparatorsPaint = new SolidColorPaint(SKColors.LightGray) {StrokeThickness=1},

                }
            };

                X2 = new Axis[]
                {
                new Axis
                {
                    MinLimit = -0.5,
                    MaxLimit = 65,
                    MinStep = 1,
                    UnitWidth = 1,
                    Labeler = value => value.ToString("0"),
                    LabelsRotation = 0,
                    Name= "Sample",
                    SeparatorsPaint = new SolidColorPaint(SKColors.LightGray) {StrokeThickness=1}
                }
                };

                SharedAxes.Set(X1[0], X2[0]);

                Y1 = new Axis[]
                    {
                new Axis
                    {
                        MinLimit = -0.1,
                        MaxLimit = 1.1,
                        Name = "Bit",
                        SeparatorsPaint = new SolidColorPaint(SKColors.LightGray) {StrokeThickness=1},
                        MinStep = 0.10,
                    }
                    };

                Y2 = new Axis[]
                    {
                new Axis
                    {
                        MinLimit = -0.1,
                        MaxLimit = 1.1,
                        Name = "Bit",
                        SeparatorsPaint = new SolidColorPaint(SKColors.LightGray) {StrokeThickness=1},
                        MinStep = 0.10
                    }
                    };

                BerX = new Axis[]
                {
                     new Axis
                     {
                        Name = "Iteration",
                        MinLimit=0,
                        Labeler = value => value.ToString("0"),
                        SeparatorsPaint = new SolidColorPaint(SKColors.LightGray) {StrokeThickness=1},
                        
                     }
                };
                BerY = new Axis[]
                {
                    new Axis
                    {
                        Name = "BER",
                         MinLimit = -0.1,
                        MaxLimit = 1,
                        Labeler = value => value.ToString("P2"), // procenty
                        SeparatorsPaint = new SolidColorPaint(SKColors.LightGray) {StrokeThickness=1},
                        
                    }
                };
            }
        }

        

    }
}