using System;
using System.Linq;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using CommunityToolkit.Mvvm.Input;
using PulsarUI.ViewModels;

namespace PulsarUI.Views
{
    public partial class MainWindow : Window
    {
        // Provide a design-time view model in the parameterless constructor to satisfy non-nullable usages in XAML
        private MainWindowViewModel _viewModel;

        public MainWindow()
        {
            _viewModel = new MainWindowViewModel();
            InitializeComponent();
            DataContext = _viewModel;

            // Register global key handlers so plus/minus work regardless of focus
            this.AddHandler(KeyDownEvent, OnWindowKeyDown, handledEventsToo: true);
        }
        public MainWindow(MainWindowViewModel viewModel)
        {
            _viewModel = viewModel;
            InitializeComponent();
            DataContext = _viewModel;

            // Register global key handlers so plus/minus work regardless of focus
            this.AddHandler(KeyDownEvent, OnWindowKeyDown, handledEventsToo: true);
        }

        private void OnWindowKeyDown(object? sender, KeyEventArgs e)
        {
            // Only respond when setup is active
            if (_viewModel == null || !_viewModel.SetupActive) return;

            if (RoundNumericUpDown == null) return;

            // Determine plus/minus keys: handle main keyboard (+ via OemPlus with Shift or '='), and numpad Add/Subtract
            bool handled = false;

            // Numpad keys
            if (e.Key == Key.Add || e.Key == Key.Subtract)
            {
                handled = true;
                if (e.Key == Key.Add)
                    AdjustRound(1);
                else
                    AdjustRound(-1);
            }
            else
            {
                // Fallback: inspect KeySymbol (char) which is more consistent across platforms for +/ -
                var sym = e.KeySymbol ?? string.Empty;
                if (sym == "+")
                {
                    handled = true;
                    AdjustRound(1);
                }
                else if (sym == "-")
                {
                    handled = true;
                    AdjustRound(-1);
                }
                else if (sym == "=" && (e.KeyModifiers & KeyModifiers.Shift) != 0)
                {
                    // Shift+ = often produces + on some keyboards
                    handled = true;
                    AdjustRound(1);
                }
            }

            // Page Up / Page Down change ModeComboBox selection when setup is active
            if (!handled)
            {
                if (e.Key == Key.PageUp)
                {
                    AdjustMode(-1);
                    handled = true;
                }
                else if (e.Key == Key.PageDown)
                {
                    AdjustMode(1);
                    handled = true;
                }
            }

            if (handled)
            {
                e.Handled = true;
            }
        }

        private void AdjustRound(int delta)
        {
            try
            {
                // NumericUpDown uses decimal? for Value/Min/Max in Avalonia. Use decimal arithmetic.
                decimal current = RoundNumericUpDown.Value ?? 0m;
                decimal min = RoundNumericUpDown.Minimum;
                decimal max = RoundNumericUpDown.Maximum;

                decimal newVal = current + delta;
                if (newVal < min) newVal = min;
                if (newVal > max) newVal = max;

                RoundNumericUpDown.Value = newVal;
            }
            catch
            {
                // Ignore any runtime issues
            }
        }

        private void RaceNumBox_OnKeyDown(object? sender, KeyEventArgs e)
        {
            if (sender is not TextBox textBox) return;

            if (e.Key is Key.Enter or Key.Tab)
            {
                string text = textBox.Text ?? "";

                // Trigger MQTT command manually for the correct box
                if (textBox.Name == "LeftRaceNumBox")
                {
                    if (_viewModel.LeftRaceNumConfirmedCommand.CanExecute(text))
                    {
                        _viewModel.LeftRaceNumConfirmedCommand.Execute(null);
                    }
                }
                else if (textBox.Name == "RightRaceNumBox" && _viewModel.RightRaceNumConfirmedCommand.CanExecute(text))
                {
                    _viewModel.RightRaceNumConfirmedCommand.Execute(null);
                }

                // Lane logic
                switch (textBox.Name)
                {
                    case "LeftRaceNumBox":
                        if (RightRaceNumBox.Text == text) RightRaceNumBox.Text = string.Empty;
                        LeftIndexBox.Focus();
                        break;
                    case "RightRaceNumBox":
                        if (LeftRaceNumBox.Text == text) LeftRaceNumBox.Text = string.Empty;
                        RightIndexBox.Focus();
                        break;
                }

                e.Handled = true;
                return;
            }

            // Block invalid characters
            var inputChar = e.KeySymbol ?? "";
            if (inputChar.Length != 0 && !char.IsLetterOrDigit(inputChar[0]))
                e.Handled = true;
        }

        private void RaceNumBox_OnTextChanged(object? sender, TextChangedEventArgs e)
        {
            if (sender is not TextBox textBox) return;
            if (textBox.Text == null) return;

            // Remove non-alphanumeric characters
            var filteredText = new string(textBox.Text.Where(char.IsLetterOrDigit).ToArray());

            // Make uppercase
            filteredText = filteredText.ToUpper();

            if (textBox.Text != filteredText)
            {
                var caretIndex = textBox.CaretIndex;
                textBox.Text = filteredText;
                textBox.CaretIndex = Math.Min(caretIndex, filteredText.Length);
            }

            // Optional: limit length
            if (textBox.Text.Length >= 16)
            {
                textBox.SelectAll();
            }
        }

        private void IndexBox_OnKeyDown(object? sender, KeyEventArgs e)
        {
            if (sender is not TextBox textBox) return; 
            
            if (e.Key is Key.Enter or Key.Tab)
            {
                if (textBox.Text != null) textBox.Text = PadIndexFormat(textBox.Text);
                e.Handled = true;
                switch (textBox?.Name)
                {
                    case "LeftIndexBox":
                        if (_viewModel.LeftIndexConfirmedCommand.CanExecute(null))
                        {
                            _viewModel.LeftIndexConfirmedCommand.Execute(null);
                        }
                        RightRaceNumBox.Focus();
                        break;
                    case "RightIndexBox":
                        if (_viewModel.RightIndexConfirmedCommand.CanExecute(null))
                        {
                            _viewModel.RightIndexConfirmedCommand.Execute(null);
                        }
                        LeftRaceNumBox.Focus();
                        break;
                }
                return;
            }

            var inputChar = e.KeySymbol ?? string.Empty;
            textBox.Text ??= string.Empty;
            if (inputChar.Length > 0 && (char.IsDigit(inputChar[0]) || (inputChar == "." && !textBox.Text.Contains('.'))))
            {
                // Get the current caret position
                var caretIndex = textBox.CaretIndex;

                // Insert the new character at the caret position
                var currentText = textBox.Text ?? string.Empty;
                var newText = currentText.Length == 5 ? inputChar : currentText.Insert(caretIndex, inputChar);

                if (!IsValidIndexFormat(newText))
                {
                    e.Handled = true;
                    return;
                }
                
                if (newText.Length == 2 && !newText.Contains('.'))
                {
                    newText = newText.Insert(2, ".");  // Insert a decimal point after the two digits
                }
                
                if (DotDigDig().IsMatch(newText) || DigDotDigDig().IsMatch(newText))
                {
                    newText = PadIndexFormat(newText);
                }
                // Update the text and set the caret position after the inserted character
                textBox.Text = newText;
                
                e.Handled = true;
                
                if (textBox.Text.Length == 5)
                {
                    textBox.SelectAll(); // Select all to start typing at the beginning if the string is complete
                }
                else
                {
                    textBox.CaretIndex = textBox.Text.Length; // Move caret to the next position
                }
                
                return;
            }

            // Block invalid character
            e.Handled = true;
        }

        private static bool IsValidIndexFormat(string input)
        {
            return Regex.IsMatch(input, @"^\d{0,2}(\.\d{0,2})?$");
        }
        
        private static string PadIndexFormat(string input)
        {
            var match = Regex.Match(input, @"^(?<whole>\d{0,2})?(?:\.(?<frac>\d{0,2}))?$");

            if (!match.Success)
                return "00.00"; // Optional fallback or return input

            string whole = match.Groups["whole"].Success ? match.Groups["whole"].Value.PadLeft(2, '0') : "00";
            string frac = match.Groups["frac"].Success ? match.Groups["frac"].Value.PadRight(2, '0') : "00";

            return $"{whole}.{frac}";
        }

        private void EnterPairTextBox_OnGotFocus(object? sender, GotFocusEventArgs e)
        {
            if (sender is not TextBox textBox) { return; }
            
            textBox.SelectAll();
        }

        private void AdjustMode(int delta)
        {
            try
            {
                if (ModeComboBox == null) return;

                var items = ModeComboBox.Items;
                int count = items == null ? 0 : items.Cast<object>().Count();

                if (count == 0)
                    return;

                int current = ModeComboBox.SelectedIndex;
                if (current < 0)
                    current = 0; // pick first if none selected

                int newIndex = Math.Clamp(current + delta, 0, count - 1);
                ModeComboBox.SelectedIndex = newIndex;
            }
            catch
            {
                // ignore any runtime issues
            }
        }

        [GeneratedRegex(@"^\.\d{2}$")]
        private static partial Regex DotDigDig();
        [GeneratedRegex(@"^\d{1}\.\d{2}$")]
        private static partial Regex DigDotDigDig();
    }
}