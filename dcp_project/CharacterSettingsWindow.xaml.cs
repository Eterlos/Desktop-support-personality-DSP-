using System.Windows;

namespace dcp_project;

/// <summary>
/// Окно выбора png-персонажа (base_normal, maid_normal, gyro_normal).
/// Файлы с суффиксом sost не используются.
/// </summary>
public partial class CharacterSettingsWindow : Window
{
    public string SelectedCharacter { get; private set; } = "base_normal";

    public CharacterSettingsWindow(string currentCharacter)
    {
        InitializeComponent();
        SelectedCharacter = currentCharacter;

        switch (currentCharacter)
        {
            case "maid_normal":
                RadioMaid.IsChecked = true;
                break;
            case "gyro_normal":
                RadioGyro.IsChecked = true;
                break;
            default:
                RadioBase.IsChecked = true;
                break;
        }
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        SelectedCharacter = RadioBase.IsChecked == true ? "base_normal"
            : RadioMaid.IsChecked == true ? "maid_normal"
            : "gyro_normal";
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}