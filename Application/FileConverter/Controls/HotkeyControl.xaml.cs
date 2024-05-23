using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace FileConverter.Controls
{
    /// <summary>
    /// Interaction logic for HotkeyControl.xaml
    /// </summary>
    public partial class HotkeyControl : UserControl
    {
        public static readonly DependencyProperty HotkeyProperty =
        DependencyProperty.Register("Hotkey", typeof(string), typeof(HotkeyControl), new PropertyMetadata("", OnHotkeyPropertyChanged));

        private static void OnHotkeyPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            HotkeyControl control = (HotkeyControl)d;
            control.HotkeyTextBox.Text = (string)e.NewValue;
        }

        public string Hotkey
        {
            get => (string)GetValue(HotkeyProperty);
            set => SetValue(HotkeyProperty, value);
        }

        public HotkeyControl()
        {
            InitializeComponent();
        }

        private void HotkeyTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            e.Handled = true;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            var modifiers = Keyboard.Modifiers;

            Hotkey = $"{modifiers}+{key}";
            HotkeyTextBox.Text = Hotkey;
        }

        private void HotkeyTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            // Optionally, validate the hotkey or reset if invalid
        }
    }
}
