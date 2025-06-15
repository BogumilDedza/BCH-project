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

        private string sshHost = "169.254.97.151";
        private string sshUser = "root";
        private string sshPassword = "fpgai0t";
        private string bashScriptPath = "/home/root/script";
        private SshClient sshClient;
        private bool lastDataFromSsh = false;
        private int berDataPointIndex = 0;
        public MainWindow()
        {
            InitializeComponent();
            StartSshPing();
            viewModel = new ViewModel();
            DataContext = viewModel;

            SshHostTextBox.Text = sshHost;
            SshUserTextBox.Text = sshUser;
            SshPasswordBox.Password = sshPassword;

        }

        //Ustawienia do SSH sprawdzenie 
        private async void ApplySshSettings_Click(object sender, RoutedEventArgs e)
        {
            sshHost = SshHostTextBox.Text.Trim();
            sshUser = SshUserTextBox.Text.Trim();
            sshPassword = SshPasswordBox.Password;
         

            // Połączenie z SSH
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

            isSshConnected = connected;
            UpdateConnectionStatus(connected);

            if (connected)
            {
                
                MessageBox.Show("SSH connection successful.");
            }
            else
            {
               
                MessageBox.Show("SSH connection failed.");
            }
        }

        //Reset Połączenie z SSH
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

        //  Flaga czyli BCH,FAST NOISE BIT ERROR
        private string BuildCommand(byte dataIn, bool bch, bool fs, bool gauss, bool ber, byte density, byte berGen)
        {
            
            string executablePath = "/home/root/script";  

            return $"{executablePath} --input 0x{dataIn:X2} --bch {(bch ? 1 : 0)} --fs {(fs ? 1 : 0)} --gauss {(gauss ? 1 : 0)} --ber {(ber ? 1 : 0)} --density 0x{density:X2} --bergen 0x{berGen:X2} 2>&1";
        }

        private async Task<bool> ResetHardwareState()
        {
            return await Task.Run(() =>
            {
                try
                {
                    if (!sshClient.IsConnected)
                    {
                        sshClient.Connect();
                    }

                    using var cmd = sshClient.CreateCommand($"{bashScriptPath} --input 0x00 --bch 0 --fs 0 --gauss 0 --ber 0 --density 0x00 --bergen 0x00");
                    cmd.CommandTimeout = TimeSpan.FromSeconds(3);
                    var result = cmd.Execute();

                    return cmd.ExitStatus == 0;
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() => {
                        RecivedTextBox.AppendText($"Reset Error: {ex.Message}\n");
                    });
                    return false;
                }
            });
        }

        //Wysyłanie i odbieranie danych 
        private async void SendCommandButton_Click(object sender, RoutedEventArgs e)
        {
            string userInput = Box.Text.Trim();

            if (string.IsNullOrEmpty(userInput) || userInput.Length > 8)
            {
                RecivedTextBox.Text = "Wrong Input - Maximum 8 characters allowed";
                return;
            }

            // czyść dane
            viewModel.SentBits.Clear();
            viewModel.ReceivedBits.Clear();
            RecivedTextBox.Clear();

            //CHART 1: SENT DATA 
            foreach (char c in userInput)
            {
                string binary = Convert.ToString((byte)c, 2).PadLeft(8, '0');
                await AddBitsToSeries(viewModel.SentBits, binary, 400);
            }

            string receivedText = "";

             
            if (isSshConnected && sshClient != null && sshClient.IsConnected)
            {
                await ResetHardwareState();
                await Task.Delay(1000);

                foreach (char c in userInput)
                {
                    byte dataIn = (byte)c;
                    byte density = (byte)DensitySlider.Value;
                    byte bitErrorValue = (byte)BitErrorSlider.Value;

                    string command = BuildCommand(
                        dataIn,
                        BCHCodingEnable,
                        FastMode,
                        noiseGenerationEnabled,
                        bitErrorEnabled,
                        density,
                        bitErrorValue
                    );

                    byte receivedByte = await ExecuteSshCommand(command, dataIn);
                    receivedText += (char)receivedByte;

                    int delayMs = BCHCodingEnable ? 2000 : 1000;
                    await Task.Delay(delayMs);
                }
            }
            else
            {
                lastDataFromSsh = false;
                RecivedTextBox.AppendText("[SIMULATION MODE] Using local simulation...\n");
                // SIMULATION MODE
                await Task.Delay(300);
                receivedText = SimulateProcessing(userInput);
            }

           
            foreach (char c in receivedText)
            {
                string binary = Convert.ToString((byte)c, 2).PadLeft(8, '0');
                await AddBitsToSeries(viewModel.ReceivedBits, binary, 400);
            }

            string sourceIndicator = lastDataFromSsh ? "[FROM SSH]" : "[FROM SIMULATION]";
            RecivedTextBox.AppendText($"{sourceIndicator} Final result: {receivedText}\n");
            // Run BER test
            viewModel.BerValues.Clear();
            await RunBerTest(userInput);
        }

     
        private async Task<byte> ExecuteSshCommand(string command, byte fallbackValue)
        {
            return await Task.Run(() =>
            {
                try
                {
                    if (!sshClient.IsConnected)
                    {
                        sshClient.Connect();
                    }

                    using var cmd = sshClient.CreateCommand(command);
                    cmd.CommandTimeout = TimeSpan.FromSeconds(2); 

                    var result = cmd.Execute().Trim();

                   /* Dispatcher.Invoke(() => {
                        RecivedTextBox.AppendText($"INPUT: {fallbackValue} ('{(char)fallbackValue}') -> ");
                        RecivedTextBox.AppendText($"COMMAND: {command}\n");
                        RecivedTextBox.AppendText($"RAW RESULT: '{result}' -> ");
                    });
                   Decoding*/

                    if (cmd.ExitStatus != 0 || !string.IsNullOrEmpty(cmd.Error))
                    {
                        Dispatcher.Invoke(() => {
                            RecivedTextBox.AppendText($"Command failed with exit code {cmd.ExitStatus}\n");
                            if (!string.IsNullOrEmpty(cmd.Error))
                                RecivedTextBox.AppendText($"Error: {cmd.Error}\n");
                        });
                        lastDataFromSsh = false;
                        return fallbackValue;
                    }

                   
                    if (result.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                    {
                        if (byte.TryParse(result.Substring(2), System.Globalization.NumberStyles.HexNumber, null, out byte hexByte))
                        {
                            lastDataFromSsh = true;
                            return hexByte;
                        }
                    }

                    if (byte.TryParse(result, out byte parsedByte))
                    {
                        lastDataFromSsh = true;
                        return parsedByte;
                    }

                    
                    Dispatcher.Invoke(() => {
                        RecivedTextBox.AppendText($"Could not parse result: '{result}'\n");
                    });

                    lastDataFromSsh = false;
                    return fallbackValue;
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() => {
                        RecivedTextBox.AppendText($"SSH Error: {ex.Message}\n");
                    });
                    lastDataFromSsh = false;
                    return fallbackValue;
                }
            });
        }

        // Funkcja do symulacji w stanie braku połączenia z ssh
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

                // Przygotuj dane do porównania
                List<int> originalBits = new List<int>();
                List<int> processedBits = new List<int>();

                // Konwertuj oryginalne dane na bity
                foreach (char c in userInput)
                {
                    string binary = Convert.ToString((byte)c, 2).PadLeft(8, '0');
                    foreach (char bit in binary)
                    {
                        originalBits.Add(bit == '1' ? 1 : 0);
                    }
                }

                string receivedText = "";

                // Przetwarzanie przez SSH lub symulację
                if (isSshConnected && sshClient != null && sshClient.IsConnected)
                {
                    foreach (char c in userInput)
                    {
                        byte dataIn = (byte)c;
                        byte density = (byte)DensitySlider.Value;
                        byte bitErrorValue = (byte)BitErrorSlider.Value;

                        string command = BuildCommand(dataIn, BCHCodingEnable, FastMode,
                            noiseGenerationEnabled, bitErrorEnabled, density, bitErrorValue);

                        byte receivedByte = await ExecuteSshCommand(command, dataIn);
                        receivedText += (char)receivedByte;
                    }
                }
                else
                {
                    await Task.Delay(100);
                    receivedText = SimulateProcessing(userInput);
                }

                // Konwertuj otrzymane dane na bity
                foreach (char c in receivedText)
                {
                    string binary = Convert.ToString((byte)c, 2).PadLeft(8, '0');
                    foreach (char bit in binary)
                    {
                        processedBits.Add(bit == '1' ? 1 : 0);
                    }
                }

                // Oblicz BER dla kompletnych danych
                double ber = CalculateBER(originalBits, processedBits);

                // Aktualizuj wykresy (tylko dla ostatniej iteracji lub co kilka iteracji)
                if (i == iterations - 1 || i % 5 == 0)
                {
                    viewModel.SentBits.Clear();
                    viewModel.ReceivedBits.Clear();

                    foreach (int bit in originalBits)
                    {
                        viewModel.SentBits.Add(bit);
                    }
                    foreach (int bit in processedBits)
                    {
                        viewModel.ReceivedBits.Add(bit);
                    }
                }

                // Dodaj BER do wykresu
                await Dispatcher.InvokeAsync(() =>
                {
                    viewModel.BerValues.Add(ber);
                    if (viewModel.BerValues.Count > 100)
                        viewModel.BerValues.RemoveAt(0);
                });

                await Task.Delay(delayMs);
            }
        }

        private double CalculateBER(List<int> originalBits, List<int> receivedBits)
        {
            int length = Math.Min(originalBits.Count, receivedBits.Count);
            if (length == 0) return 0;

            int errorCount = 0;
            for (int i = 0; i < length; i++)
            {
                if (originalBits[i] != receivedBits[i])
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

            bitErrorEnabled = false;
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
            bitErrorEnabled = true;
            noiseGenerationEnabled = false;
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
            bitErrorEnabled = false;  // Add this line!
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