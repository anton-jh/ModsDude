using ModsDude.WindowsClient.Model.Models.ValueTypes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;

namespace ModsDude.WindowsClient.Wpf.Pages.GameAdapters
{
    /// <summary>
    /// Interaction logic for FarmingSimulatorV1BaseConfigPage.xaml
    /// </summary>
    public partial class FarmingSimulatorV1InstanceConfigPage : Page
    {
        public FarmingSimulatorV1InstanceConfigPage()
        {
            InitializeComponent();
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            var folderPath = _gameDataFolderTextBox.Text;

            using var dialog = new FolderBrowserDialog { SelectedPath = folderPath };
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                var newPath = new DirectoryPath(dialog.SelectedPath);
                _gameDataFolderTextBox.Text = newPath.ToString();
            }
        }
    }
}
