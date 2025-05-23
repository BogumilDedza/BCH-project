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
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private CancellationTokenSource bitAddingCancellationToken;
        private SerialPort serialPort;//+
        private Dispatcher connectionChecker;//+
        private bool BCHCodingEnable = false;
        private bool FastMode = true;
        private bool noiseGenerationEnabled = false;
         private ViewModel viewModel;
        private DispatcherTimer pingTimer;
        private bool bitErrorEnabled = false;
        private bool IsBchCodingEnabled = false;
        private bool IsFastModeEnabled = false;
        private bool IsNoiseGenerationEnabled = false;
        private bool isConnectedDotGreen = false; //+
        private string selectedPortName = "";
        public MainWindow()
        {
            
            InitializeComponent(); //komentarz
            pingTimer = new DispatcherTimer();
            pingTimer.Interval = TimeSpan.FromSeconds(1);
            pingTimer.Tick += PingTimer_Tick;
            pingTimer.Start();
            viewModel = new ViewModel();
            DataContext = viewModel;
            RefreshPortsList();

            DispatcherTimer comRefreshTimer = new DispatcherTimer();
            comRefreshTimer.Interval = TimeSpan.FromSeconds(1);
            comRefreshTimer.Tick += (s, e) => RefreshPortsList();
            comRefreshTimer.Start();
        }

        private void RefreshPortsList()
        {
            var availablePorts = SerialPort.GetPortNames();
           
            PortsComboBox.ItemsSource = null;
            PortsComboBox.ItemsSource = availablePorts;

            
            if (!string.IsNullOrEmpty(selectedPortName) && availablePorts.Contains(selectedPortName))
            {
                PortsComboBox.SelectedItem = selectedPortName;
            }
            else if (availablePorts.Length > 0)
            {
                PortsComboBox.SelectedIndex = 0;
                selectedPortName = PortsComboBox.SelectedItem.ToString();
            }
        }

        private void PortsComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PortsComboBox.SelectedItem != null)
            {
                selectedPortName = PortsComboBox.SelectedItem.ToString();
            }
        }

        private void RefreshPortsButton_Click(object sender, RoutedEventArgs e)
        {
            RefreshPortsList();
        }
        //COnectivity
        private void ConnectToPort(string portName)
        {
            DisposeSerialPort();

            try
            {
                serialPort = new SerialPort
                {
                    PortName = portName,
                    BaudRate = 9600,
                    DataBits = 8,
                    Handshake = Handshake.None,
                    Encoding = Encoding.ASCII,
                    ReadTimeout = 1000,
                    WriteTimeout = 1000
                };

                serialPort.DataReceived += SerialPort_DataReceived;
                serialPort.Open();

                if (serialPort.IsOpen)
                {
                    UpdateConnectionStatus(true);
                }
                else
                {
                    UpdateConnectionStatus(false);
                }
            }
            catch
            {
                DisposeSerialPort();
                UpdateConnectionStatus(false);
            }
        }

        private void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            if (PortsComboBox.SelectedItem != null)
            {
                string selectedPort = PortsComboBox.SelectedItem.ToString();
                ConnectToPort(selectedPort);
            }
            else
            {
                RecivedTextBox.Text="Choose COM.";
            }
        }
        private void ConnectToPort()
        {
            if (!string.IsNullOrEmpty(selectedPortName))
            {
                ConnectToPort(selectedPortName);
            }
        }
        private bool IsPortConnected()
        {
            if (serialPort != null)
            {
                return serialPort.IsOpen;
            }
            else
            {
                return false;
            }
        }

       
        private void DisposeSerialPort()
        {
            try
            {
                if (serialPort != null)
                {
                    if (serialPort.IsOpen)
                        serialPort.DataReceived -= SerialPort_DataReceived;
                    serialPort.Close();
                    serialPort.Dispose();
                }
            }
            catch { }
            serialPort = null;
        }

        List<byte> buffer = new List<byte>();

        private byte BuildFlagsByte(bool bch, bool fast, bool noise, bool bitError)
        {
            byte flags = 0;
            if (bch) flags |= 1 << 7;
            if (fast) flags |= 1 << 6;
            if (noise) flags |= 1 << 5;
            if (bitError) flags |= 1 << 4;
            return flags;
        }

        private async void SendCommandButton_Click(object sender, RoutedEventArgs e)
        {
            if (!IsPortConnected())
            {
                RecivedTextBox.Text = "Waiting ...";
                ConnectToPort();

                if (!IsPortConnected())
                {
                    RecivedTextBox.Text = "Please connect your device.";
                    return;
                }
            }

            string userInput = Box.Text.Trim();

            if (string.IsNullOrEmpty(userInput) || userInput.Length >8 )
            {
                RecivedTextBox.Text = "WRONG INPUT";
                return;
            }

            viewModel.SentBits.Clear();

            foreach(char c in userInput) 
            {
                string binary = Convert.ToString((byte)c,2).PadLeft(8,'0');
                await AddBitsToSeries(viewModel.SentBits, binary, 400);
                
            }
            // Flagi
            byte flags = BuildFlagsByte(BCHCodingEnable, FastMode, noiseGenerationEnabled, bitErrorEnabled);
            byte density = (byte)DensitySlider.Value;
            byte bitError = (byte)BitErrorSlider.Value;

            try
            {
               foreach(char c in userInput)
                {
                    byte dataToSend =(byte)c;

                    byte[] message = new byte[]
                        {
                        0x02,           // START: 0000_0010
                        dataToSend,     // dane użytkownika
                        flags,          // 1 bajt z 4 flagami
                        density,        // gęstość szumu
                        bitError,       // bit error
                        0xF0            // STOP: 1111_0000
                    };

                    serialPort.Write(message, 0, message.Length);
                }
                
            }
            catch (Exception)
            {
                UpdateConnectionStatus(false);
                MessageBox.Show("Device disconnected. Attempting to reconnect...");
                ConnectToPort();
            }
        }

        private async void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                while (serialPort.BytesToRead > 0)
                {
                    byte Byte = (byte)serialPort.ReadByte();
                    buffer.Add(Byte);


                    while (buffer.Count >= 3)
                    {
                        if (buffer[0] == 0x03 && buffer[2] == 0xF1)
                        {
                            byte dataByte = buffer[1];
                            string binaryString = Convert.ToString(dataByte, 2).PadLeft(8, '0');


                            await Dispatcher.InvokeAsync(() =>
                            {
                                RecivedTextBox.Clear();
                                RecivedTextBox.AppendText($"Odebrano dane: {binaryString}\n");
                                RecivedTextBox.ScrollToEnd();
                            });


                            await AddBitsToSeries(viewModel.ReceivedBits, binaryString, 500);
                           

                            buffer.RemoveRange(0, 3);
                        }
                        else
                        {

                            buffer.RemoveAt(0);
                        }
                    }
                }
            }
            catch (TimeoutException)
            {

            }
        }
        public class ViewModel {

            public ObservableCollection<double> BERValues { get; set; } = new ObservableCollection<double>();

            public ObservableCollection<int> SentBits { get; set; } = new ObservableCollection<int>();
             public ObservableCollection<int> ReceivedBits { get; set; } = new ObservableCollection<int>();

             public ISeries[] Series { get; set; }
             public ISeries[] BERSeries { get; set; }
            public Axis[] YAxes { get; set; } = new Axis[] //setting Axis Y
             {
            
                 new Axis
                {
                    MinLimit = -0.1,
                    MaxLimit = 1.2,
                    MinStep = 1,
                    Labeler = value => value.ToString("0"),
                    
                 }
             };

            public Axis[] XAxes { get; set; } = new Axis[] //setting Axis Y
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
             Position = LiveChartsCore.Measure.AxisPosition.End,
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
                    GeometrySize=7.5,
                    Stroke = new SolidColorPaint(SKColors.Green) { StrokeThickness = 2 } // color od the line in chart and thickness

            },
            new LineSeries<int>
            {
                    Values = ReceivedBits,
                    Name = "Recived",
                    Fill = null,
                    GeometrySize=7.5,
                    Stroke = new SolidColorPaint(SKColors.Red) { StrokeThickness = 2 } // color od the line in chart and thickness
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
                    ScalesYAt = 0  // zakładam osobna oś Y
                }
            };

            }
        }

        
        private async Task AddBitsToSeries(ObservableCollection<int> targetCollection, string binaryString, int delayMs)
        {
            bitAddingCancellationToken?.Cancel(); 
            bitAddingCancellationToken = new CancellationTokenSource();
            var token = bitAddingCancellationToken.Token;

            foreach (char bit in binaryString)
            {
                if (token.IsCancellationRequested) { break; }
                  
                int ValueBit;
                if (bit == '1')
                {
                    ValueBit = 1;
                }
                else
                {
                    ValueBit = 0;
                }

                    await Dispatcher.InvokeAsync(() =>
                {
                    if (targetCollection.Count > 100)
                        targetCollection.RemoveAt(0);

                    targetCollection.Add(ValueBit);
                });

                await Task.Delay(delayMs);
            }
        }

        //Ping
        private void PingTimer_Tick(object? sender, EventArgs e)
        {
            if (serialPort == null || !serialPort.IsOpen)
            {
                UpdateConnectionStatus(false);
                ConnectToPort();
                return;
            }

            try
            {
                // Wysyłamy ping: [argument=0x01, stop=0xF0]
                byte[] pingMessage = new byte[] { 0x01, 0xF0 };
                serialPort.Write(pingMessage, 0, pingMessage.Length);

                UpdateConnectionStatus(true);
            }
            catch
            {
                UpdateConnectionStatus(false);
                ConnectToPort();
            }
        }

        private void UpdateConnectionStatus(bool isConnected)
        {
            if (ConnectionStatusDot != null)
            {
                ConnectionStatusDot.Fill = isConnected ? Brushes.Green : Brushes.Red;
            }
        }

        //function to reset everything in 1 section (sending data and reciving it ) and chart in app
        private void ResetCommandButton_Click(object sender, RoutedEventArgs e)
        {
            bitAddingCancellationToken?.Cancel();

            Box.Clear();
            RecivedTextBox.Clear();

            viewModel.SentBits.Clear();
            viewModel.ReceivedBits.Clear();

            viewModel.BERValues.Clear();
        }

             
        //function to button 
        private void BchYesButton_Click(object sender,RoutedEventArgs e)
        {
            BCHCodingEnable = true;
            IsBchCodingEnabled = true;

            SetBchButtons(true);

        }

        //function to button 
        private void BchNoButton_Click(Object sender, RoutedEventArgs e)
        {
            BCHCodingEnable = false;
            IsBchCodingEnabled = false;
            SetBchButtons(false);

        }

        private void FastButton_Click(Object sender, RoutedEventArgs e)
        {
            FastMode = true;
            IsFastModeEnabled = true;

           
            SetCodingTypeButtons(true);
        }


        private void SlowButton_Click(Object sender, RoutedEventArgs e)
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

            //DensitySlider = 0;
        }

        //function to button 
        private void BitErrorGeneratorButton_Click(object sender, RoutedEventArgs e)
        {
            noiseGenerationEnabled = true;
            IsNoiseGenerationEnabled = true;

            GaussianNoiseButton.Visibility = Visibility.Visible;
            BitErrorGeneratorButton.Visibility = Visibility.Visible;

            GaussianOptionsPanel.Visibility = Visibility.Collapsed;
            BitErrorOptionsPanel.Visibility = Visibility.Visible;

            DensitySlider.Visibility = Visibility.Collapsed;
            BitErrorSlider.Visibility = Visibility.Visible;

            SetNoiseButtons(true);

            //BitErrorSlider = 0;
        }
        
        private void NoiseYesButton_Click(object sender,RoutedEventArgs e)
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

        private void SetCodingTypeButtons(bool bchEnable)
        {
            if (FastButton != null && SlowButton != null)
            {
                if (FastMode)
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
                if (noiseGenerationEnabled)
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




    }
}