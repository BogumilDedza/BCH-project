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

namespace BCH_PROJEKT
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private bool BCHCodingEnable = false;
        private bool FastMode = true;
        private bool noiseGenerationEnabled = false;
        public MainWindow()
        {
            InitializeComponent(); //komentarz
            //komentarz2
        }

        // KOMENDY

        private void YES_button_Click(object sender,RoutedEventArgs e)
        {
            Option.Visibility = Visibility.Visible;
            BitError.Visibility = Visibility.Collapsed;
        }

        private void NO_button_Click(Object sender, RoutedEventArgs e)
        {
            Option.Visibility = Visibility.Collapsed;
            BitError.Visibility= Visibility.Collapsed;
        }

        private void GaussianNoiseButton_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("You choose gaussian noise ");
        
        }

        private void BitErrorGeneratorButton_Click(object sender, RoutedEventArgs e)
        {
            BitError.Visibility = Visibility.Visible;

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