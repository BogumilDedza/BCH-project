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

        private void SendComandButton_Click(object sender, RoutedEventArgs e)
        {
            string command=Box.Text;
            if (string.IsNullOrEmpty(command))
            {
                MessageBox.Show("Proszę wpisz komendę","Pusta komenda",MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            
        }


    }
}