using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using PulsarUI.ViewModels;
using System.Windows.Input;

namespace PulsarUI.Views
{
    public partial class MainWindow : Window
    {
        // Provide a design-time view model in the parameterless constructor to satisfy non-nullable usages in XAML
        private MainWindowViewModel? _viewModel;

        // Simple ICommand implementation for code-behind keybinding
        private sealed class RelayCommand : ICommand
        {
            private readonly Action<object?> _execute;
            private readonly Func<object?, bool>? _canExecute;
            public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
            {
                _execute = execute ?? throw new ArgumentNullException(nameof(execute));
                _canExecute = canExecute;
            }
            public event EventHandler? CanExecuteChanged;
            public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;
            public void Execute(object? parameter) => _execute(parameter);
        }

        public MainWindow()
        {
            _viewModel = new MainWindowViewModel();
            InitializeComponent();
            DataContext = _viewModel;

            // Register global key handlers so plus/minus work regardless of focus
            this.AddHandler(KeyDownEvent, OnWindowKeyDown, handledEventsToo: true);
            // Fallback: also subscribe to KeyDown event
            this.KeyDown += OnWindowKeyDown;
            // Add explicit KeyBinding for F12 to ensure it triggers ToggleCategoryPopup
            this.KeyBindings.Add(new KeyBinding
            {
                Gesture = new KeyGesture(Key.F12),
                Command = new RelayCommand(_ => ToggleCategoryPopup())
            });

            // Add F8 keybinding to invoke AbortRun (if available) so keyboard F8 triggers the command
            this.KeyBindings.Add(new KeyBinding
            {
                Gesture = new KeyGesture(Key.F8),
                Command = new RelayCommand(_ => { try { var vm = _viewModel; if (vm != null) ((dynamic)vm).ExecuteAbortRun(); } catch { } })
            });

            // Build category grid after InitializeComponent to ensure CategoryGrid is available
            this.Opened += (_, _) => BuildCategoryGrid();

            // Wire popup opened/closed to manage focus and buffer reliably (handles light-dismiss)
            try
            {
                if (CategoryPopup != null)
                {
                    CategoryPopup.Opened += (_, _) =>
                    {
                        _categoryTyped = string.Empty;
                        UpdateCategoryTypedDisplay();
                        BuildCategoryGrid();
                        try { CategoryCaptureBox?.Focus(); } catch (Exception) { /* Focus may fail during light-dismiss; safe to ignore */ }
                    };

                    CategoryPopup.Closed += (_, _) =>
                    {
                        try { LeftRaceNumBox?.Focus(); } catch (Exception) { /* best-effort */ }
                        _categoryTyped = string.Empty;
                        UpdateCategoryTypedDisplay();
                    };
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("MainWindow ctor (popup wiring) error: " + ex.Message);
            }

            // Subscribe to ViewModel changes for test message popup
            try
            {
                if (_viewModel != null)
                {
                    _viewModel.PropertyChanged += (s, e) =>
                    {
                        try
                        {
                            if (e.PropertyName == nameof(MainWindowViewModel.ShowTestMessagePopup))
                            {
                                var show = _viewModel.ShowTestMessagePopup;
                                if (TestMessagePopup != null)
                                {
                                    TestMessagePopup.IsOpen = show;
                                    if (show)
                                    {
                                        // Optionally set focus to the popup's close button
                                        try { /* no-op: focus may be set if needed */ } catch (Exception ex2) { Console.Error.WriteLine("MainWindow: focus set failed: " + ex2.Message); }
                                    }
                                }
                            }
                        }
                        catch (Exception ex3) { Console.Error.WriteLine("MainWindow.PropertyChanged handler error: " + ex3.Message); }
                    };
                }
            }
            catch (Exception ex4) { Console.Error.WriteLine("MainWindow ctor (property wiring) error: " + ex4.Message); }
        }
        public MainWindow(MainWindowViewModel viewModel)
        {
            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            InitializeComponent();
            DataContext = _viewModel;

            // Register global key handlers so plus/minus work regardless of focus
            this.AddHandler(KeyDownEvent, OnWindowKeyDown, handledEventsToo: true);
            // Fallback: also subscribe to KeyDown event
            this.KeyDown += OnWindowKeyDown;
            // Add explicit KeyBinding for F12 in the other constructor as well
            this.KeyBindings.Add(new KeyBinding
            {
                Gesture = new KeyGesture(Key.F12),
                Command = new RelayCommand(_ => ToggleCategoryPopup())
            });

            // Add F8 keybinding to invoke AbortRun (if available) so keyboard F8 triggers the command
            this.KeyBindings.Add(new KeyBinding
            {
                Gesture = new KeyGesture(Key.F8),
                Command = new RelayCommand(_ => { try { var vm = _viewModel; if (vm != null) ((dynamic)vm).ExecuteAbortRun(); } catch { } })
            });

            // Build category grid after InitializeComponent to ensure CategoryGrid is available
            this.Opened += (_, _) => BuildCategoryGrid();

            // Wire popup opened/closed to manage focus and buffer reliably (handles light-dismiss)
            try
            {
                if (CategoryPopup != null)
                {
                    CategoryPopup.Opened += (_, _) =>
                    {
                        _categoryTyped = string.Empty;
                        UpdateCategoryTypedDisplay();
                        BuildCategoryGrid();
                        try { CategoryCaptureBox?.Focus(); } catch (Exception) { /* Focus may fail during UI transitions; ignore */ }
                    };

                    CategoryPopup.Closed += (_, _) =>
                    {
                        try { LeftRaceNumBox?.Focus(); } catch (Exception) { /* best-effort focus */ }
                        _categoryTyped = string.Empty;
                        UpdateCategoryTypedDisplay();
                    };
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("MainWindow ctor (popup wiring) error: " + ex.Message);
            }

            // Subscribe to ViewModel changes for test message popup
            try
            {
                if (_viewModel != null)
                {
                    _viewModel.PropertyChanged += (s, e) =>
                    {
                        try
                        {
                            if (e.PropertyName == nameof(MainWindowViewModel.ShowTestMessagePopup))
                            {
                                var show = _viewModel.ShowTestMessagePopup;
                                if (TestMessagePopup != null)
                                {
                                    TestMessagePopup.IsOpen = show;
                                }
                            }
                        }
                        catch (Exception ex) { Console.Error.WriteLine("MainWindow.PropertyChanged handler error: " + ex.Message); }
                    };
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("MainWindow ctor (property wiring) error: " + ex.Message);
            }
        }

        // typed-digit buffer for category popup
        private string _categoryTyped = string.Empty;

        // helper to update the visible typed display in the popup
        private void UpdateCategoryTypedDisplay()
        {
            try
            {
                if (CategoryTypedDisplay != null)
                {
                    // show typed buffer (pad with underscore for missing digits)
                    if (_categoryTyped.Length == 0)
                        CategoryTypedDisplay.Text = "__";
                    else if (_categoryTyped.Length == 1)
                        CategoryTypedDisplay.Text = _categoryTyped + "_";
                    else
                        CategoryTypedDisplay.Text = _categoryTyped;
                }
            }
            catch (Exception ex) { Console.Error.WriteLine("UpdateCategoryTypedDisplay error: " + ex.Message); }
        }

        // highlight matching button for current typed buffer (if any)
        private void HighlightMatchingButton()
        {
            try
            {
                if (CategoryGrid == null) return;
                // iterate over buttons and set Background/Opacity to indicate selection
                foreach (var child in CategoryGrid.Children)
                {
                    if (child is Button btn && btn.Content is string content)
                    {
                        // assume items are like "01 - Name" so compare first two chars
                        var id = content.Length >= 2 ? content.Substring(0, 2) : string.Empty;
                        if (!string.IsNullOrEmpty(id) && id == _categoryTyped)
                        {
                            // high-contrast orange highlight + thicker border
                            btn.Background = new SolidColorBrush(Color.Parse("#FF8C00")); // orange
                            btn.Foreground = Brushes.Black;
                            try { btn.BorderBrush = new SolidColorBrush(Color.Parse("#FFA500")); btn.BorderThickness = new Avalonia.Thickness(2); } catch (Exception ex) { Console.Error.WriteLine("HighlightMatchingButton (border) error: " + ex.Message); }
                        }
                        else
                        {
                            // reset style for non-selected buttons
                            btn.Background = new SolidColorBrush(Color.Parse("#2D2D30"));
                            btn.Foreground = Brushes.White;
                            try { btn.BorderBrush = new SolidColorBrush(Color.Parse("#444444")); btn.BorderThickness = new Avalonia.Thickness(1); } catch (Exception ex) { Console.Error.WriteLine("HighlightMatchingButton (border reset) error: " + ex.Message); }
                        }
                    }
                }
            }
            catch (Exception ex) { Console.Error.WriteLine("HighlightMatchingButton error: " + ex.Message); }
        }

        // when two digits are typed (or enter pressed) select matching category if found
        private void TrySelectCategoryFromTyped()
        {
            try
            {
                if (string.IsNullOrEmpty(_categoryTyped) || CategoryGrid == null) return;

                foreach (var child in CategoryGrid.Children)
                {
                    if (child is Button btn && btn.Content is string content && content.Length >= 2)
                    {
                        if (content.Substring(0, 2) == _categoryTyped)
                        {
                            // select this category
                            if (_viewModel != null)
                            {
                                _viewModel.EnterPairSelectedCategComboText = content;
                            }

                            // close popup
                            if (CategoryPopup != null) CategoryPopup.IsOpen = false;
                            // clear typed buffer
                            _categoryTyped = string.Empty;
                            UpdateCategoryTypedDisplay();
                            return;
                        }
                    }
                }
            }
            catch (Exception ex) { Console.Error.WriteLine("TrySelectCategoryFromTyped error: " + ex.Message); }
        }

        private void OnWindowKeyDown(object? sender, KeyEventArgs e)
        {
            // Toggle CategoryPopup with F12 (regardless of SetupActive state for now)
            if (e.Key == Key.F12)
            {
                ToggleCategoryPopup();
                e.Handled = true;
                return;
            }

            // If the category popup is open, handle digit input for selecting categories
            if (CategoryPopup != null && CategoryPopup.IsOpen)
            {
                // handle Escape to close and Backspace to remove
                if (e.Key == Key.Escape)
                {
                    _categoryTyped = string.Empty;
                    UpdateCategoryTypedDisplay();
                    if (CategoryPopup != null) CategoryPopup.IsOpen = false;
                    e.Handled = true;
                    return;
                }

                if (e.Key == Key.Back)
                {
                    if (_categoryTyped.Length > 0) _categoryTyped = _categoryTyped.Substring(0, _categoryTyped.Length - 1);
                    UpdateCategoryTypedDisplay();
                    HighlightMatchingButton();
                    e.Handled = true;
                    return;
                }

                // accept Enter as commit when we have two digits
                if (e.Key == Key.Enter || e.Key == Key.Return)
                {
                    if (_categoryTyped.Length == 2)
                    {
                        TrySelectCategoryFromTyped();
                        e.Handled = true;
                        return;
                    }
                }

                // handle digit keys
                var sym = e.KeySymbol ?? string.Empty;
                if (sym.Length == 1 && char.IsDigit(sym[0]))
                {
                    if (_categoryTyped.Length < 2)
                    {
                        // append until we have two digits
                        _categoryTyped += sym;
                    }
                    else
                    {
                        // already had two digits -> start a new entry with the newly typed digit
                        _categoryTyped = sym;
                    }

                    // update UI and highlight matching button, but DO NOT auto-select; wait for Enter
                    UpdateCategoryTypedDisplay();
                    HighlightMatchingButton();

                    e.Handled = true;
                    return;
                }

                // consume other keys while popup open
                e.Handled = true;
                return;
            }

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
                var sym2 = e.KeySymbol ?? string.Empty;
                if (sym2 == "+")
                {
                    handled = true;
                    AdjustRound(1);
                }
                else if (sym2 == "-")
                {
                    handled = true;
                    AdjustRound(-1);
                }
                else if (sym2 == "=" && (e.KeyModifiers & KeyModifiers.Shift) != 0)
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
            catch (Exception ex)
            {
                Console.Error.WriteLine("AdjustRound error: " + ex.Message);
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
            catch (Exception ex)
            {
                Console.Error.WriteLine("AdjustMode error: " + ex.Message);
            }
        }

        [GeneratedRegex(@"^\.\d{2}$")]
        private static partial Regex DotDigDig();
        [GeneratedRegex(@"^\d{1}\.\d{2}$")]
        private static partial Regex DigDotDigDig();

        // Programmatic popup/grid helpers
        public void ToggleCategoryPopup()
        {
            try
            {
                // Only allow popup when setup is active
                if (_viewModel == null || !_viewModel.SetupActive)
                {
                    return;
                }

                if (CategoryPopup == null) return;

                // When opening, ensure placement is centered on the window
                if (!CategoryPopup.IsOpen)
                {
                    try
                    {
                        CategoryPopup.PlacementTarget = this;
                    }
                    catch (Exception ex) { Console.Error.WriteLine("ToggleCategoryPopup (placement) error: " + ex.Message); }
                }

                CategoryPopup.IsOpen = !CategoryPopup.IsOpen;

                if (CategoryPopup.IsOpen)
                {
                    // clear typed buffer and update visible display when opening
                    _categoryTyped = string.Empty;
                    UpdateCategoryTypedDisplay();

                    BuildCategoryGrid();

                    // Attempt to set keyboard focus to the invisible capture box so keys go to the popup
                    try
                    {
                        CategoryCaptureBox?.Focus();
                    }
                    catch (Exception ex) { Console.Error.WriteLine("ToggleCategoryPopup (focus) error: " + ex.Message); }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("ToggleCategoryPopup error: " + ex.Message);
            }
        }

        private void BuildCategoryGrid()
        {
            try
            {
                if (CategoryGrid == null || _viewModel == null) return;

                // Clear existing children and definitions
                CategoryGrid.Children.Clear();
                CategoryGrid.RowDefinitions.Clear();
                CategoryGrid.ColumnDefinitions.Clear();

                int rows = 12;
                int cols = 3;

                for (int r = 0; r < rows; r++)
                    CategoryGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                for (int c = 0; c < cols; c++)
                    CategoryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                // Items are expected to be strings like "01 - Name" — use the ViewModel list
                var items = _viewModel.CategComboBoxItems ?? new List<string>();

                // Ensure we have at least rows*cols items; unfilled cells will be hidden
                for (int col = 0; col < cols; col++)
                {
                    for (int row = 0; row < rows; row++)
                    {
                        int index = col * rows + row; // column-major ordering
                        string content = index < items.Count ? items[index] : string.Empty;

                        var btn = new Button
                        {
                            Content = content,
                            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch,
                            Margin = new Avalonia.Thickness(4),
                            MinHeight = 40
                        };

                        // Optional: disable empty buttons
                        if (string.IsNullOrEmpty(content))
                        {
                            btn.IsVisible = false;
                            btn.IsEnabled = false;
                        }
                        else
                        {
                            // Improve contrast: dark button background, white text, left-aligned text, padding and larger font
                            try
                            {
                                btn.Background = new SolidColorBrush(Color.Parse("#2D2D30"));
                                btn.Foreground = Brushes.White;
                                btn.FontSize = 16;
                                btn.HorizontalContentAlignment = HorizontalAlignment.Left;
                                btn.VerticalContentAlignment = VerticalAlignment.Center;
                                btn.Padding = new Avalonia.Thickness(10,6);
                                btn.BorderBrush = new SolidColorBrush(Color.Parse("#444444"));
                                btn.BorderThickness = new Avalonia.Thickness(1);
                            }
                            catch (Exception ex) { Console.Error.WriteLine("BuildCategoryGrid (button style) error: " + ex.Message); }

                            // Wire click to select category: set the ViewModel's selected combo text and close popup
                            btn.Click += (s, ev) =>
                            {
                                try
                                {
                                    if (_viewModel != null)
                                    {
                                        _viewModel.EnterPairSelectedCategComboText = content;
                                    }
                                    // Close the popup if present
                                    try { if (CategoryPopup != null) CategoryPopup.IsOpen = false; } catch (Exception) { /* best-effort close */ }
                                }
                                catch (Exception ex) { Console.Error.WriteLine("BuildCategoryGrid (button click) error: " + ex.Message); }
                            };
                        }

                         Grid.SetRow(btn, row);
                         Grid.SetColumn(btn, col);
                         CategoryGrid.Children.Add(btn);
                     }
                 }
             }
             catch (Exception ex)
             {
                 Console.Error.WriteLine("BuildCategoryGrid error: " + ex.Message);
             }
         }

        private void F12Button_Click(object? sender, RoutedEventArgs e)
        {
             ToggleCategoryPopup();
        }

        // Helper to format Category properties into a Panel for the InfoPopup
        private Avalonia.Controls.Control BuildCategoryInfoContent(PulsarUI.Models.Category? cat)
        {
            // StackPanel doesn't have Padding in Avalonia; use Margin here and let the caller (Border) provide padding
            var panel = new StackPanel { Orientation = Orientation.Vertical, Background = new SolidColorBrush(Color.Parse("#1E1E1E")), Margin = new Avalonia.Thickness(6) };
            if (cat == null)
            {
                panel.Children.Add(new TextBlock { Text = "No category selected.", Foreground = Brushes.White });
                return panel;
            }

            // Title
            panel.Children.Add(new TextBlock { Text = cat.Name ?? $"Category #{cat.Id}", FontWeight = FontWeight.Bold, Foreground = Brushes.White, Margin = new Avalonia.Thickness(0, 0, 0, 6) });

            // Reflect properties of Category and display them sorted by name
            var props = typeof(PulsarUI.Models.Category).GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            foreach (var p in props.OrderBy(p => p.Name))
            {
                try
                {
                    var val = p.GetValue(cat);
                    var tb = new TextBlock { Text = $"{p.Name}: {val}", Foreground = Brushes.White, FontSize = 12, Margin = new Avalonia.Thickness(0, 2, 0, 2) };
                    panel.Children.Add(tb);
                }
                catch (Exception ex) { Console.Error.WriteLine("BuildCategoryInfoContent (property) error: " + ex.Message); }
            }

            return panel;
        }

        private void ShowInfoPopup(Control anchor, PulsarUI.Models.Category? category)
        {
            try
            {
                // If CategoryDetails wasn't available when the popup was requested (async DB load),
                // try to resolve the Category from the ViewModel's data so the popup still shows useful info.
                var resolved = category;
                try
                {
                    if (resolved == null && _viewModel != null)
                    {
                        // Determine which queue item to inspect based on the anchor control (which info button was clicked)
                        PulsarUI.Models.CategQueueItem? q = null;
                        try
                        {
                            if (anchor == InfoEngageButton)
                                q = _viewModel.EngagePairQueueCategory;
                            else if (anchor == InfoQueueButton)
                                q = _viewModel.QueuePairQueueCategory;
                            else
                                q = _viewModel.EnterPairQueueCategory;
                        }
                        catch (Exception ex) { Console.Error.WriteLine("ShowInfoPopup (queue item determination) error: " + ex.Message); }

                        // Prefer explicit CategoryDetails on the chosen queue item
                        if (q != null)
                        {
                            resolved = q.CategoryDetails;
                            // If still null, try to find by numeric Category id (only if non-zero)
                            if ((resolved == null || resolved.Id == 0) && q.Category != 0 && _viewModel.Categories != null)
                                resolved = _viewModel.Categories.FirstOrDefault(c => c != null && c.Id == q.Category);
                        }

                        // If still unresolved and the anchor is the Enter info button, allow falling back to the EnterPair display
                        if (resolved == null && anchor == InfoEnterButton && _viewModel != null && !string.IsNullOrWhiteSpace(_viewModel.EnterPairCategText) && _viewModel.Categories != null)
                        {
                            var name = _viewModel.EnterPairCategText.Trim();
                            resolved = _viewModel.Categories.FirstOrDefault(c => c != null && string.Equals(c.Name?.Trim(), name, StringComparison.OrdinalIgnoreCase));
                        }

                        // Also allow the Enter combo-text fallback only for Enter info
                        if (resolved == null && anchor == InfoEnterButton && _viewModel != null && !string.IsNullOrWhiteSpace(_viewModel.EnterPairSelectedCategComboText) && _viewModel.Categories != null)
                        {
                            var combo = _viewModel.EnterPairSelectedCategComboText.Trim();
                            var parts = combo.Split(new[] { '-' }, 2);
                            if (parts.Length == 2)
                            {
                                var after = parts[1].Trim();
                                if (!string.IsNullOrEmpty(after))
                                    resolved = _viewModel.Categories.FirstOrDefault(c => c != null && string.Equals(c.Name?.Trim(), after, StringComparison.OrdinalIgnoreCase));
                            }
                        }
                    }
                }
                catch (Exception ex) { Console.Error.WriteLine("ShowInfoPopup (category resolution) error: " + ex.Message); }

                // Treat a resolved category with Id==0 as unresolved (placeholder); try to match by display labels
                if (resolved != null && resolved.Id == 0)
                    resolved = null;

                // If still null, try matching by the textual label associated with the clicked info button
                if (resolved == null && _viewModel != null && _viewModel.Categories != null)
                {
                    try
                    {
                        string? display = null;
                        if (anchor == InfoEnterButton) display = _viewModel.EnterPairCategText ?? _viewModel.EnterPairSelectedCategComboText;
                        else if (anchor == InfoQueueButton) display = _viewModel.QueuePairCategText ?? _viewModel.EnterPairSelectedCategComboText;
                        else if (anchor == InfoEngageButton) display = _viewModel.EngagePairCategText ?? _viewModel.EnterPairSelectedCategComboText;

                        if (!string.IsNullOrWhiteSpace(display))
                        {
                            var d = display.Trim();
                            // If it looks like "NN - Name", prefer the name part
                            var parts = d.Split(new[] { '-' }, 2);
                            if (parts.Length == 2) d = parts[1].Trim();
                            var matchByName = _viewModel.Categories.FirstOrDefault(c => c != null && string.Equals(c.Name?.Trim(), d, StringComparison.OrdinalIgnoreCase));
                            if (matchByName != null) resolved = matchByName;
                        }
                    }
                    catch (Exception ex) { Console.Error.WriteLine("ShowInfoPopup (match by display label) error: " + ex.Message); }
                }

                Avalonia.Controls.Control content;
                if (resolved == null)
                {
                    // If categories haven't loaded yet, show a helpful message and the textual label if available
                    var msgPanel = new StackPanel { Orientation = Orientation.Vertical };
                    var label = new TextBlock { Text = "Category details not available yet.", Foreground = Brushes.White, FontWeight = FontWeight.Bold };
                    msgPanel.Children.Add(label);
                    try
                    {
                        var name = anchor == InfoEnterButton ? (_viewModel?.EnterPairCategText ?? _viewModel?.EnterPairSelectedCategComboText)
                                  : anchor == InfoQueueButton ? (_viewModel?.QueuePairCategText ?? _viewModel?.EnterPairSelectedCategComboText)
                                  : (_viewModel?.EngagePairCategText ?? _viewModel?.EnterPairSelectedCategComboText);
                        msgPanel.Children.Add(new TextBlock { Text = $"Selected: {name}", Foreground = Brushes.White, Margin = new Avalonia.Thickness(0,6,0,0) });
                    }
                    catch (Exception ex) { Console.Error.WriteLine("ShowInfoPopup (message panel) error: " + ex.Message); }
                    content = msgPanel;
                }
                else
                {
                    content = BuildCategoryInfoContent(resolved);
                }

                InfoPopup.Child = new Border
                {
                    Background = new SolidColorBrush(Color.Parse("#2B2B2B")),
                    BorderBrush = new SolidColorBrush(Color.Parse("#444444")),
                    BorderThickness = new Avalonia.Thickness(1),
                    Padding = new Avalonia.Thickness(8),
                    CornerRadius = new Avalonia.CornerRadius(6),
                    Child = new ScrollViewer { Content = content, Width = 320, Height = 280 }
                };

                if (InfoPopup.PlacementTarget != anchor)
                    InfoPopup.PlacementTarget = anchor;
                InfoPopup.IsOpen = true;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("ShowInfoPopup error: " + ex.Message);
            }
        }

        // Click handlers for the three info buttons
        private void InfoEngageButton_Click(object? sender, RoutedEventArgs e)
        {
            var cat = _viewModel?.EngagePairQueueCategory?.CategoryDetails;
            ShowInfoPopup(InfoEngageButton, cat);
        }

        private void InfoQueueButton_Click(object? sender, RoutedEventArgs e)
        {
            var cat = _viewModel?.QueuePairQueueCategory?.CategoryDetails;
            ShowInfoPopup(InfoQueueButton, cat);
        }

        private void InfoEnterButton_Click(object? sender, RoutedEventArgs e)
        {
            var cat = _viewModel?.EnterPairQueueCategory?.CategoryDetails;
            ShowInfoPopup(InfoEnterButton, cat);
        }

        private void OnCloseTestMessagePopup(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            try
            {
                if (_viewModel != null)
                    _viewModel.ShowTestMessagePopup = false;
                else if (TestMessagePopup != null)
                    TestMessagePopup.IsOpen = false;
            }
            catch (Exception ex) { Console.Error.WriteLine("OnCloseTestMessagePopup error: " + ex.Message); }
        }
    }
}
