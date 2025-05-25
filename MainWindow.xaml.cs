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

namespace BCH_PROJEKT
{
    public partial class MainWindow : Window
    {
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
            if (!isSshConnected || sshClient == null || !sshClient.IsConnected)
            {
                MessageBox.Show("Not connected via SSH. Please apply valid SSH settings first.");
                return;
            }

            string userInput = Box.Text.Trim();

            if (string.IsNullOrEmpty(userInput) || userInput.Length > 8)
            {
                RecivedTextBox.Text = "Wrong Input: input must be 1-8 characters.";
                return;
            }

            viewModel.SentBits.Clear();
            viewModel.ReceivedBits.Clear();

            foreach (char c in userInput)
            {
                string binary = Convert.ToString((byte)c, 2).PadLeft(8, '0');
                await AddBitsToSeries(viewModel.SentBits, binary, 400);
            }

            byte flags = BuildFlagsByte(BCHCodingEnable, FastMode, noiseGenerationEnabled, bitErrorEnabled);
            byte density = (byte)DensitySlider.Value;
            byte bitErrorValue = (byte)BitErrorSlider.Value;

            string commandArgs = $"{userInput} {flags} {density} {bitErrorValue}";

            await Task.Run(() =>
            {
                try
                {
                    if (!sshClient.IsConnected)
                        sshClient.Connect();

                    using var cmd = sshClient.CreateCommand($"{bashScriptPath} {commandArgs}");
                    var result = cmd.Execute();

                    Dispatcher.Invoke(() =>
                    {
                        RecivedTextBox.Clear();
                        RecivedTextBox.AppendText(result);
                        RecivedTextBox.ScrollToEnd();
                    });

                    string cleanBinary = new string(result.Where(c => c == '0' || c == '1').ToArray());
                    string sentBinary = string.Concat(userInput.Select(c => Convert.ToString((byte)c, 2).PadLeft(8, '0')));

                    if (!string.IsNullOrEmpty(cleanBinary))
                    {
                        Dispatcher.Invoke(async () =>
                        {
                            await AddBitsToSeries(viewModel.ReceivedBits, cleanBinary, 500);
                            CalculateAndPlotBer(sentBinary, cleanBinary);
                        });
                    }
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() =>
                    {
                        MessageBox.Show("SSH command error: " + ex.Message);
                    });
                }
            });
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

        //Tutaj oblicza oraz daje na wykres Ber dane 
        private void CalculateAndPlotBer(string sentBits, string receivedBits)
        {
            if (string.IsNullOrEmpty(sentBits) || string.IsNullOrEmpty(receivedBits)) return;

            int errors = 0;
            int length = Math.Min(sentBits.Length, receivedBits.Length);

            for (int i = 0; i < length; i++)
            {
                if (sentBits[i] != receivedBits[i])
                    errors++;
            }

            double ber;
            if (length > 0)
            {
                ber = (double)errors / length;
            }
            else
            {
                ber = 0.0;
            }

            Dispatcher.Invoke(() =>
            {
                if (viewModel.BERValues.Count > 100)
                    viewModel.BERValues.RemoveAt(0);

                viewModel.BERValues.Add(ber);
            });
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
            viewModel.BERValues.Clear();
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
            public ObservableCollection<double> BERValues { get; set; } = new ObservableCollection<double>();
            public ObservableCollection<int> SentBits { get; set; } = new ObservableCollection<int>();
            public ObservableCollection<int> ReceivedBits { get; set; } = new ObservableCollection<int>();

            public ISeries[] Series { get; set; }
            public ISeries[] BERSeries { get; set; }

            public Axis[] YAxes { get; set; } = new Axis[]
            {
                new Axis
                {
                    MinLimit = -0.1,
                    MaxLimit = 1.2,
                    MinStep = 1,
                    Labeler = value => value.ToString("0"),
                }
            };

            public Axis[] XAxes { get; set; } = new Axis[]
            {
                new Axis
                {
                    MinLimit = -0.5,
                    MaxLimit = 65,
                    MinStep = 1,
                    UnitWidth = 1,
                    Labeler = value => value.ToString("0"),
                    LabelsRotation = 0
                }
            };

            public Axis[] AdditionalYAxes { get; set; } = new Axis[]
            {
                new Axis
                {
                    MinLimit = 0,
                    MaxLimit = 1,
                    Position = AxisPosition.End,
                    Labeler = value => value.ToString("0.00"),
                    Name = "BER"
                }
            };

            public ViewModel()
            {
                Series = new ISeries[]
                {
                    new LineSeries<int>
                    {
                        Values = SentBits,
                        Name = "Send",
                        Fill = null,
                        GeometrySize = 7.5,
                        Stroke = new SolidColorPaint(SKColors.Green) { StrokeThickness = 2 }
                    },
                    new LineSeries<int>
                    {
                        Values = ReceivedBits,
                        Name = "Received",
                        Fill = null,
                        GeometrySize = 7.5,
                        Stroke = new SolidColorPaint(SKColors.Red) { StrokeThickness = 2 }
                    }
                };

                BERSeries = new ISeries[]
                {
                    new LineSeries<double>
                    {
                        Values = BERValues,
                        Name = "BER",
                        Fill = null,
                        GeometrySize = 7.5,
                        Stroke = new SolidColorPaint(SKColors.Blue) { StrokeThickness = 2 },
                        ScalesYAt = 0
                    }
                };
            }
        }
    }
}