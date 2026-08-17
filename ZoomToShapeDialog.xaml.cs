using System.Windows;

namespace DoodleSharp
{
    public partial class ZoomToShapeDialog : Window
    {
        public long? ShapeId { get; private set; }

        public ZoomToShapeDialog()
        {
            InitializeComponent();
            IdTextBox.Focus();
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (long.TryParse(IdTextBox.Text.Trim(), out long id))
            {
                ShapeId = id;
                DialogResult = true;
            }
            else
            {
                MessageBox.Show("Please enter a valid numeric ID.", "Invalid ID", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}
