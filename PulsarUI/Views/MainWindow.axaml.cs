using Avalonia.Controls;
using Avalonia.Input;
using PulsarUI.ViewModels;

namespace PulsarUI.Views
{
    public partial class MainWindow : Window
    {
        private readonly MainWindowViewModel _viewModel;

        public MainWindow()
        {
            InitializeComponent();
        }
        public MainWindow(MainWindowViewModel viewModel)
        {
            _viewModel = viewModel;
            InitializeComponent();
            DataContext = _viewModel;
        }
        
        private void LeftRaceNumBox_OnKeyDown(object? sender, KeyEventArgs e)
        {
            _viewModel.EnterPairRaceNum_OnKeyDown(sender, e);
            if (e.Key == Key.Enter)
            {
                LeftIndexBox.Focus();
            }
        }

        private void LeftIndexBox_OnKeyDown(object? sender, KeyEventArgs e)
        {
            _viewModel.EnterPairIndex_OnKeyDown(sender, e);
            if (e.Key == Key.Enter)
            {
                RightRaceNumBox.Focus();
            }
        }

        private void RightRaceNumBox_OnKeyDown(object? sender, KeyEventArgs e)
        {
            _viewModel.EnterPairRaceNum_OnKeyDown(sender, e);
            if (e.Key == Key.Enter)
            {
                RightIndexBox.Focus();
            }
        }

        private void RightIndexBox_OnKeyDown(object? sender, KeyEventArgs e)
        {
            _viewModel.EnterPairIndex_OnKeyDown(sender, e);
            if (e.Key == Key.Enter)
            {
                LeftRaceNumBox.Focus();
            }
        }

        private void EnterPairTextBox_OnGotFocus(object? sender, GotFocusEventArgs e)
        {
            if (sender is not TextBox textBox) { return; }
            
            textBox.SelectAll();
        }

        private void LeftIndexBox_OnKeyUp(object? sender, KeyEventArgs e)
        {
            _viewModel.EnterPairIndex_OnKeyUp(sender, e);
        }

        private void RightRaceNumBox_OnKeyUp(object? sender, KeyEventArgs e)
        {
            _viewModel.EnterPairIndex_OnKeyUp(sender, e);
        }
    }
}