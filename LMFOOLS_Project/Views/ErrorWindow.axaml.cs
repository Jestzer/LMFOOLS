using Avalonia.Controls;
using Avalonia.Interactivity;
namespace LMFOOLS_Project.Views;

public partial class ErrorWindow : Window
{
    public ErrorWindow()
    {
        InitializeComponent();
        DpiScaling.Apply(this);
    }
    
    private void OKButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}