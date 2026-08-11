using System.Windows;
using System.Windows.Controls;

namespace GrevHome.Views;

public partial class ControllerQwertyKeyboard : UserControl
{
    private readonly List<Button> _keyButtons = new();
    private bool _upperCase = true;
    private int _maxLength = 50;

    public event Action<string>? Completed;
    public event EventHandler? Cancelled;
    public event EventHandler? Opened;
    public event EventHandler? Closed;

    public bool IsOpen => Visibility == Visibility.Visible;
    public string Value { get; private set; } = string.Empty;

    public ControllerQwertyKeyboard()
    {
        InitializeComponent();
        BuildKeyboard();
    }

    public void Open(string title, string? initialValue, int maxLength)
    {
        _maxLength = Math.Max(1, maxLength);
        var safeInitialValue = initialValue ?? string.Empty;
        Value = safeInitialValue.Length > _maxLength
            ? safeInitialValue[.._maxLength]
            : safeInitialValue;
        _upperCase = true;
        TitleText.Text = title;
        Visibility = Visibility.Visible;
        UpdatePresentation();
        Opened?.Invoke(this, EventArgs.Empty);
        Dispatcher.BeginInvoke(new Action(() => _keyButtons.FirstOrDefault()?.Focus()));
    }

    public void Cancel()
    {
        if (!IsOpen)
        {
            return;
        }

        Close();
        Cancelled?.Invoke(this, EventArgs.Empty);
    }

    private void BuildKeyboard()
    {
        AddRow("1234567890", 70);
        AddRow("QWERTYUIOP", 76);
        AddRow("ASDFGHJKL", 76);
        AddRow("ZXCVBNM", 76);
    }

    private void AddRow(string keys, double width)
    {
        var row = new WrapPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 7)
        };

        foreach (var key in keys)
        {
            var button = new Button
            {
                Tag = key,
                Width = width,
                Height = 58,
                Margin = new Thickness(4),
                FontSize = 18
            };
            button.Click += Key_Click;
            _keyButtons.Add(button);
            row.Children.Add(button);
        }

        KeyboardRows.Children.Add(row);
    }

    private void Key_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: char key } || Value.Length >= _maxLength)
        {
            return;
        }

        var value = char.IsLetter(key)
            ? (_upperCase ? char.ToUpperInvariant(key) : char.ToLowerInvariant(key))
            : key;
        Value += value;
        UpdatePresentation();
    }

    private void Shift_Click(object sender, RoutedEventArgs e)
    {
        _upperCase = !_upperCase;
        UpdatePresentation();
    }

    private void Space_Click(object sender, RoutedEventArgs e)
    {
        if (Value.Length < _maxLength)
        {
            Value += " ";
            UpdatePresentation();
        }
    }

    private void Backspace_Click(object sender, RoutedEventArgs e)
    {
        if (Value.Length == 0)
        {
            return;
        }

        Value = Value[..^1];
        UpdatePresentation();
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        Value = string.Empty;
        UpdatePresentation();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Cancel();

    private void Done_Click(object sender, RoutedEventArgs e)
    {
        var result = Value;
        Close();
        Completed?.Invoke(result);
    }

    private void Close()
    {
        Visibility = Visibility.Collapsed;
        Closed?.Invoke(this, EventArgs.Empty);
    }

    private void UpdatePresentation()
    {
        ValueText.Text = string.IsNullOrEmpty(Value) ? " " : Value;
        ShiftButton.Content = _upperCase ? "Shift • ABC" : "Shift • abc";

        foreach (var button in _keyButtons)
        {
            if (button.Tag is not char key)
            {
                continue;
            }

            button.Content = char.IsLetter(key)
                ? (_upperCase ? char.ToUpperInvariant(key) : char.ToLowerInvariant(key)).ToString()
                : key.ToString();
        }
    }
}
