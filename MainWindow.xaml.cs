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
        public MainWindow()
        {
            InitializeComponent(); //komentarz
            //komentarz2

            serialPort=new SerialPort("COM3",9600);
            serialPort.DataReceived += SerialPort_DataReceived;
            serialPort.Open();

        }

        private void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            string incomingData = serialPort.ReadExisting();
            Dispatcher.Invoke(() => {

                RecivedTextBox.AppendText(incomingData);
                RecivedTextBox.ScrollToEnd();

            
            });
        }

        private void SendCommandButton_Click(object sender, RoutedEventArgs e) {

            string comand = Box.Text;
            if(serialPort != null && serialPort.IsOpen)
            {
                serialPort.WriteLine(comand);
            }
        }

        private void ResetCommandButton_Click(object sender, RoutedEventArgs e)
        {

            string comand = Box.Text;
            if (serialPort != null && serialPort.IsOpen)
            {
                Box.Clear();
                RecivedTextBox.Clear();
            }
        }

        // KOMENDY

        private void YES_button_Click(object sender,RoutedEventArgs e)
        {
            Option.Visibility = Visibility.Visible;
            BitError.Visibility = Visibility.Collapsed;
            GaussianOptionsPanel.Visibility = Visibility.Collapsed;

            BitErrorGeneratorButton.Visibility = Visibility.Visible;
            GaussianNoiseButton.Visibility = Visibility.Visible;

            DensitySlider.Value = 0;
            
        }

        private void NO_button_Click(Object sender, RoutedEventArgs e)
        {
            Option.Visibility = Visibility.Collapsed;
            BitError.Visibility= Visibility.Collapsed;
           GaussianOptionsPanel.Visibility = Visibility.Collapsed;
            
            BitErrorGeneratorButton.Visibility = Visibility.Visible;
            GaussianNoiseButton.Visibility = Visibility.Visible;

            DensitySlider.Value = 0;
        }

        private void GaussianNoiseButton_Click(object sender, RoutedEventArgs e)
        {

            BitErrorGeneratorButton.Visibility = Visibility.Collapsed;
            GaussianOptionsPanel.Visibility = Visibility.Visible;   
          
        }

        
        private void BitErrorGeneratorButton_Click(object sender, RoutedEventArgs e)
        {
            BitError.Visibility = Visibility.Visible;
            GaussianOptionsPanel.Visibility = Visibility.Collapsed;

            BitErrorGeneratorButton.Visibility = Visibility.Visible;
            GaussianNoiseButton.Visibility = Visibility.Collapsed;



        }

        private void GenGenerateErrorsButton_Click(object sender, RoutedEventArgs e)
        {

            MessageBox.Show("Generate bit error base on choosen bits. ");
        }
        private void GenerateErrorsButton_Click(object sender, RoutedEventArgs e) 
        {
            MessageBox.Show("button erroor. ");
        }

    }
}