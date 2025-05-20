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

namespace BCH_PROJEKT
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private SerialPort serialPort;
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

        //connecting to port function and data
        private void ConnectToPort()
        {
            string[] ports = SerialPort.GetPortNames();
            foreach (string port in ports)
            {
                try
                {
                    serialPort = new SerialPort(port, 9600);
                    serialPort.DataReceived += SerialPort_DataReceived;
                    serialPort.Open();
                    UpdateConnectionStatus(true);
                    return;
                }
                catch
                {
                    
                }
            }

            serialPort = null;
            UpdateConnectionStatus(false);
           ;
        }

        //fucntion for data that we recive 
        private void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            string incomingData = serialPort.ReadLine(); 
           
            Dispatcher.Invoke(() =>
            {
                RecivedTextBox.AppendText(incomingData + "\n");
                RecivedTextBox.ScrollToEnd();

                viewModel.ReceivedBits.Clear();

                foreach (char bit in incomingData.Trim())
                {
                    if (bit == '0' || bit == '1')
                    {
                        if (bit == '1')
                        {
                            viewModel.ReceivedBits.Add(1);
                        }
                        else
                        {
                            viewModel.ReceivedBits.Add(0);
                        }
                    }
                }
            });
        }

        // fucntion for sending data 
        private void SendCommandButton_Click(object sender, RoutedEventArgs e) {

            string UserInput = Box.Text.Trim();
           
            if (UserInput.Length != 7 || !UserInput.All( c=> c =='0' || c=='1' ))
            {
                RecivedTextBox.Text = "WRONG INPUT";

                return;
            }
           
            string command = "0" + UserInput;

            viewModel.SentBits.Clear();
            //sending to chart
            foreach (char bit in command)
            {
                if (bit == '1')
                {
                    viewModel.SentBits.Add(1);
                }
                else
                {
                    viewModel.SentBits.Add(0);
                }
            }

            if (serialPort != null && serialPort.IsOpen)
            {
                try
                {
                    serialPort.WriteLine(command);
                }
                catch
                {
                    MessageBox.Show("Device disconnected. Attempting to reconnect...");
                    ConnectToPort(); 
                }
            }
            else
            {
                RecivedTextBox.Text="no connection"; 
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