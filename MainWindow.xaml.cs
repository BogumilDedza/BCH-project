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
        
        private SerialPort serialPort;
        private Dispatcher connectionChecker;
        private bool BCHCodingEnable = false;
        private bool FastMode = true;
        private bool noiseGenerationEnabled = false;
         private ViewModel viewModel;
        public MainWindow()
        {
            
            InitializeComponent(); //komentarz
            ConnectToPort();
            viewModel = new ViewModel();
            DataContext = viewModel;


        }

        public class ViewModel {

           
            public ObservableCollection<int> SentBits { get; set; } = new ObservableCollection<int>();
             public ObservableCollection<int> ReceivedBits { get; set; } = new ObservableCollection<int>();

             public ISeries[] Series { get; set; }

            public Axis[] YAxes { get; set; } = new Axis[] //setting Axis Y
             {
            
                 new Axis
                {
                    MinLimit = -0.1,
                    MaxLimit = 1.1,
                    MinStep = 1,
                    Labeler = value => value.ToString("0"),
                    
                 }
             };

            public Axis[] XAxes { get; set; } = new Axis[] //setting Axis Y
            {
                new Axis
                 {
                    MinLimit = -0.5,
                    MaxLimit = 7.5,
                    MinStep = 1,
                    UnitWidth = 1,
                    Labeler = value => value.ToString("0"),
                    LabelsRotation = 0
                    
                     
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
        //connecting to port function and data
        private void ConnectToPort()
        {
            DisposeSerialPort();

            string[] ports = SerialPort.GetPortNames();

            if (ports.Length == 0)
            {
                UpdateConnectionStatus(false);
                return;
            }

            foreach (string port in ports)
            {
                try
                {
                    serialPort = new SerialPort
                    {
                        PortName = port,
                        BaudRate = 9600,
                        DataBits = 7,
                        Parity = Parity.Even,
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
                        return;
                    }
                }
                catch
                {
                    DisposeSerialPort();
                }
            }

            UpdateConnectionStatus(false);
        }

        private async Task AddBitsToSeries(ObservableCollection<int> targetCollection, string binaryString, int delayMs)
        {
            foreach (char bit in binaryString)
            {
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




        //fucntion for data that we recive 
        private async void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                int incomingData = serialPort.ReadByte();
                string binaryString = Convert.ToString(incomingData, 2).PadLeft(7, '0');

                await Dispatcher.InvokeAsync(() =>
                {
                    RecivedTextBox.Clear();
                    RecivedTextBox.AppendText(binaryString + "\n");
                    RecivedTextBox.ScrollToEnd();
                });

                await AddBitsToSeries(viewModel.ReceivedBits, binaryString, 500);
            }
            catch (TimeoutException)
            {
                
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
        
        // fucntion for sending data 
        private async void SendCommandButton_Click(object sender, RoutedEventArgs e)
        {
            if (!IsPortConnected())
            {
                RecivedTextBox.Text = "Waiting ...";
                ConnectToPort();

                if (!IsPortConnected())
                {
                    RecivedTextBox.Text = "Can't connect";
                    return;
                }
            }

            string userInput = Box.Text.Trim();

            if (userInput.Length != 7 || !userInput.All(c => c == '0' || c == '1'))
            {
                RecivedTextBox.Text = "WRONG INPUT";
                return;
            }

            viewModel.SentBits.Clear();

            await AddBitsToSeries(viewModel.SentBits, userInput, 400);

            byte dataToSend = Convert.ToByte(userInput, 2);

            try
            {
                serialPort.Write(new byte[] { dataToSend }, 0, 1);
            }
            catch (Exception)
            {
                UpdateConnectionStatus(false);
                MessageBox.Show("Device disconnected. Attempting to reconnect...");
                ConnectToPort();
            }
        }




        //function to reset everything in 1 section (sending data and reciving it ) and chart in app
        private void ResetCommandButton_Click(object sender, RoutedEventArgs e)
        {

            Box.Clear();
            RecivedTextBox.Clear();

            viewModel.SentBits.Clear();
            viewModel.ReceivedBits.Clear();
        }

             
        //function to button 
        private void YES_button_Click(object sender,RoutedEventArgs e)
        {
            Option.Visibility = Visibility.Visible;
            BitErrorOptionsPanel.Visibility = Visibility.Collapsed;
            GaussianOptionsPanel.Visibility = Visibility.Collapsed;

            BitErrorGeneratorButton.Visibility = Visibility.Visible;
            GaussianNoiseButton.Visibility = Visibility.Visible;

            DensitySlider.Value = 0;
            BitErrorSlider.Value = 0;

        }

        //function to button 
        private void NO_button_Click(Object sender, RoutedEventArgs e)
        {
            Option.Visibility = Visibility.Collapsed;
            BitErrorOptionsPanel.Visibility= Visibility.Collapsed;
           GaussianOptionsPanel.Visibility = Visibility.Collapsed;
            
            BitErrorGeneratorButton.Visibility = Visibility.Visible;
            GaussianNoiseButton.Visibility = Visibility.Visible;


            DensitySlider.Value = 0;
            BitErrorSlider.Value = 0;
        }

        //function to button 
        private void GaussianNoiseButton_Click(object sender, RoutedEventArgs e)
        {

            BitErrorGeneratorButton.Visibility = Visibility.Collapsed;
            GaussianOptionsPanel.Visibility = Visibility.Visible;   
          
        }

        //function to button 
        private void BitErrorGeneratorButton_Click(object sender, RoutedEventArgs e)
        {
            BitErrorOptionsPanel.Visibility = Visibility.Visible;
            GaussianOptionsPanel.Visibility = Visibility.Collapsed;

            BitErrorGeneratorButton.Visibility = Visibility.Visible;
            GaussianNoiseButton.Visibility = Visibility.Collapsed;


        }
        //function update connection status
        private void UpdateConnectionStatus(bool isConnected)
        {
            if (ConnectionStatusDot != null)
            {
                ConnectionStatusDot.Fill = isConnected ? Brushes.Green : Brushes.Red;
            }
        }

       

    }
}