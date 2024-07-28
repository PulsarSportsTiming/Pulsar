using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using System.Text.RegularExpressions;
using PulsarUI.Models;
using System.Text;

namespace PulsarUI.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        #if DEBUG    
            this.AttachDevTools();
        #endif

    }

    // Use GeneratedRegexAttribute for compile-time regex generation
    [GeneratedRegex(@"^(\d{0,2}(\.\d{0,2})?|\.?\d{0,2})?$")]
    private static partial Regex IndexPatternRegex();
    
    private void FunctionButton1_Clicked(object? sender, RoutedEventArgs e)
    {
        SystemSetupWindow systemSetupWindow = new SystemSetupWindow();
        systemSetupWindow.Show();
    }
    
    private void FunctionButton2_Clicked(object? sender, RoutedEventArgs e)
    {
        throw new NotImplementedException();
    }
    
    private void FunctionButton3_Clicked(object? sender, RoutedEventArgs e)
    {
        throw new NotImplementedException();
    }
    
    private void FunctionButton4_Clicked(object? sender, RoutedEventArgs e)
    {
        throw new NotImplementedException();
    }
    
    private void FunctionButton5_Clicked(object? sender, RoutedEventArgs e)
    {
        throw new NotImplementedException();
    }
    
    private void FunctionButton6_Clicked(object? sender, RoutedEventArgs e)
    {
        throw new NotImplementedException();
    }
    
    private void FunctionButton7_Clicked(object? sender, RoutedEventArgs e)
    {
        RunSetupWindow runSetupWindow = new RunSetupWindow();
        runSetupWindow.Show();
    }
    
    private void FunctionButton8_Clicked(object? sender, RoutedEventArgs e)
    {
        throw new NotImplementedException();
    }
    
    private void FunctionButton9_Clicked(object? sender, RoutedEventArgs e)
    {
        throw new NotImplementedException();
    }
    
    private void FunctionButton10_Clicked(object? sender, RoutedEventArgs e)
    {
        throw new NotImplementedException();
    }
    
    private void FunctionButton11_Clicked(object? sender, RoutedEventArgs e)
    {
        throw new NotImplementedException();
    }
    
    private void FunctionButton12_Clicked(object? sender, RoutedEventArgs e)
    {
        SwiftSetEnterPairTitle.IsVisible = StandardEnterPairTitle.IsVisible;
        StandardEnterPairTitle.IsVisible = !StandardEnterPairTitle.IsVisible;

        SwiftSetEnterPairConfig.IsVisible = SwiftSetEnterPairTitle.IsVisible;
        StandardEnterPairConfig.IsVisible = StandardEnterPairTitle.IsVisible;

        F12Button.Classes.Clear();
        F12Button.Classes.Add(!SwiftSetEnterPairTitle.IsVisible ? "FKey" : "F12Highlight");
    }

    private void EnterPairLeftNum_OnFocus(object? sender, GotFocusEventArgs e)
    {
        LeftRaceNumBox.SelectAll();
    }
    
    private void EnterPairLeftNum_OnKeyDown(object? sender, KeyEventArgs e)
    {
        var currText = ((TextBox)sender!).Text == null ? "" : ((TextBox)sender).Text!.ToUpper();
        
        if (e.Key == Key.Enter)
        {
            if (!currText.All(char.IsAsciiLetterOrDigit)) //Catch-all in case of pasting
            {
                currText = "";
            }
            ((TextBox)sender).Text = currText;
            PairModel.WriteQueueRaceNum(0,0,currText);
            
            LeftIndexBox.Focus();
        }
        else if (e.KeySymbol != null && Char.IsAsciiLetterOrDigit(e.KeySymbol[0]))
        {
            ProcessRaceNumKeyDown(sender, e);
        }
        else
        {
            e.Handled = true;
        }
    }
    
    private void EnterPairLeftIndex_OnFocus(object? sender, GotFocusEventArgs e)
    {
        LeftIndexBox.SelectAll();
    }
    
    private void EnterPairLeftIndex_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            //Final catch-all check. If good, pad, otherwise remove string
            ((TextBox)sender!).Text =
                IsIndexAllowed(((TextBox)sender).Text!) ? PadIndex(((TextBox)sender).Text!) : "00.00";  //To do: replace 00.00 with personal/class/category index
                                                                                                        //null if non-index category

            RightRaceNumBox.Focus();
        }
        else if (e.KeySymbol != null)
        {
            ProcessIndexKeyDown(sender, e);
        }
    }

    private void EnterPairRightNum_OnFocus(object? sender, GotFocusEventArgs e)
    {
        RightRaceNumBox.SelectAll();
    }

    private void EnterPairRightNum_OnKeyDown(object? sender, KeyEventArgs e)
    {
        var currText = ((TextBox)sender!).Text == null ? "" : ((TextBox)sender).Text!.ToUpper();
        
        if (e.Key == Key.Enter)
        {
            if (!currText.All(char.IsAsciiLetterOrDigit)) //Catch-all in case of pasting
            {
                currText = "";
            }
            ((TextBox)sender).Text = currText;
            PairModel.WriteQueueRaceNum(0,0,currText);
            
            RightIndexBox.Focus();
        }
        else if (e.KeySymbol != null && Char.IsAsciiLetterOrDigit(e.KeySymbol[0]))
        {
            ProcessRaceNumKeyDown(sender, e);
        }
        else
        {
            e.Handled = true;
        }
    }
    
    private void EnterPairRightIndex_OnFocus(object? sender, GotFocusEventArgs e)
    {
        RightIndexBox.SelectAll();
    }
    
    private void EnterPairRightIndex_OnKeyDown(object? sender, KeyEventArgs e)
    { 
        if (e.Key == Key.Enter)
        {
            //Final catch-all check. If good, pad, otherwise remove string
            ((TextBox)sender!).Text =
                IsIndexAllowed(((TextBox)sender).Text!) ? PadIndex(((TextBox)sender).Text!) : "00.00";  //To do: replace 00.00 with personal/class/category index
                                                                                                        //null if non-index category

            LeftRaceNumBox.Focus();
        }
        else if (e.KeySymbol != null)
        {
            ProcessIndexKeyDown(sender, e);
        }
    }

    private static void ProcessRaceNumKeyDown(object? sender, KeyEventArgs e)
    {
        var enteredChar = e.KeySymbol!.ToUpper();
        var oldText = ((TextBox)sender!).Text == null ? "" : ((TextBox)sender).Text!.ToUpper();
        var iCarat = ((TextBox)sender).CaretIndex;
        var newText = enteredChar;
        
        if (oldText.Length > 0 && oldText != ((TextBox)sender!).SelectedText)
        {
            var preCaratText = iCarat > 0 ? oldText[..iCarat] : string.Empty;
            var postCaratText = iCarat < oldText.Length ? oldText[iCarat..] : string.Empty;
            newText = preCaratText + enteredChar + postCaratText;
        }
            
        ((TextBox)sender).Text = newText;
        ((TextBox)sender).CaretIndex = ((TextBox)sender).Text!.Length;
        switch (newText.Length)
        {
            case 16:
                ((TextBox)sender).SelectAll();
                break;
            case > 16:
                ((TextBox)sender).Text = e.KeySymbol;
                ((TextBox)sender).CaretIndex = ((TextBox)sender).Text!.Length;
                break;
        }
        e.Handled = true;
    }
    private static void ProcessIndexKeyDown(object? sender, KeyEventArgs e)
    {
        var enteredChar = e.KeySymbol!;
        var oldText = ((TextBox)sender!).Text == null ? "" : ((TextBox)sender).Text!.ToUpper();
        var iCarat = ((TextBox)sender).CaretIndex;
        var newText = enteredChar;
        
        if (oldText.Length > 0 && oldText != ((TextBox)sender!).SelectedText)
        {
            var preCaratText = iCarat > 0 ? oldText[..iCarat] : string.Empty;
            var postCaratText = iCarat < oldText.Length ? oldText[iCarat..] : string.Empty;
            newText = preCaratText + enteredChar + postCaratText;
        }
        
        if (!IsIndexAllowed(newText))
        {
            e.Handled = true;
            return;
        }
        //If a user has typed two digits, automatically fill in the decimal point if one does not exist
        else if (newText.Length == 2 && !newText.EndsWith('.') && !newText.Contains('.'))
        {
            newText += ".";
            ((TextBox)sender).Text = newText;
            ((TextBox)sender).CaretIndex = ((TextBox)sender).Text!.Length;
            e.Handled = true;
        }

        // Handle when the index is not complete
        if (newText.Length - newText.IndexOf('.', StringComparison.Ordinal) != 3)
        {
            // Handle cases where the index is not complete here if needed
            return;
        }

        // The index is deemed to be complete when there are two digits after the decimal point.
        // Select all to allow the user to begin over-typing
        ((TextBox)sender).Text = PadIndex(newText);
        ((TextBox)sender).CaretIndex = ((TextBox)sender).Text!.Length;
        ((TextBox)sender).SelectAll();
        e.Handled = true;

    }
    
    private void EnterPairIndex_OnKeyUp(object? sender, KeyEventArgs e)
    {
        var tb = (TextBox)sender!;
        var caretIndex = tb.CaretIndex;
        string originalText;

        if (tb.Text != null)
        {
            originalText = tb.Text;
        }
        else
        {
            return;
        }
        
        if (e.Key == Key.Back)
        {
            // Handle Backspace
            if (caretIndex > 0)
            {
                HandleBackspace(tb, caretIndex, originalText);
            }
        }
        else if (e.Key == Key.Delete && caretIndex < tb.Text!.Length)
        {
            HandleDelete(tb, caretIndex, originalText);
        }
    }

    private static void HandleBackspace(TextBox tb, int caretIndex, string originalText)
    {
        if (IsIndexAllowed(originalText))
        {
            tb.Text = originalText;
            tb.CaretIndex = caretIndex;
            return; // Exit early
        }
        // Simulate the removal of the character before the caret
        var newText = originalText.Remove(caretIndex - 1, 1);

        // Validate newText after deleting the character
        if (IsIndexAllowed(newText))
        {
            tb.Text = newText;
            tb.CaretIndex = caretIndex;
            return; // Exit early
        }

        // Handle the case where the text is not allowed
        newText = ReinsertPeriod(newText, caretIndex - 1);
        tb.Text = newText;
        tb.CaretIndex = caretIndex - 1; // Move caret back
    }

    private static void HandleDelete(TextBox tb, int caretIndex, string originalText)
    {
        // Simulate the removal of the character after the caret
        var newText = originalText.Remove(caretIndex, 1);

        // Validate newText after deleting the character
        if (!IsIndexAllowed(newText))
        {
            // Re-insert the period if necessary
            newText = ReinsertPeriod(newText, caretIndex);
        }

        tb.Text = newText;
        tb.CaretIndex = caretIndex; // Keep caret position
    }
    
    private static string ReinsertPeriod(string text, int caretIndex)
    {
        // Insert period if necessary at caretIndex
        if (caretIndex > 0 && caretIndex <= text.Length && !text.Contains('.'))
        {
            text = text.Insert(caretIndex, ".");
        }
        return text;
    }
    
    private static bool IsIndexAllowed(string text)
    {
        //The following patterns are valid: 0, 00, 00.0, 00.00, 0,00, 0.0, 0., 00., ., .00, .0 
        //Only numbers and decimal points allowed. No more than 2 digits before or after the decimal point, only one decimal point
        //Need to implement locale feature to account for commas as decimal points
        if (text == "" || text == null)
        {
            return false;
        }

        var regex = IndexPatternRegex();
        return regex.IsMatch(text);
    }

    private static string PadIndex(string currIndex)
    {
        var newIndex = currIndex;

        if (!newIndex.Contains("."))
        {
            newIndex += ".";
        }

        var bld = new StringBuilder(newIndex);
        while (bld.ToString().IndexOf(".", StringComparison.Ordinal) < 2)
        {
            bld.Insert(0, "0");
        }

        newIndex = bld.ToString();

        bld = new StringBuilder(newIndex);
        while (bld.Length < 5)
        {
            bld.Append("0");
        }

        newIndex = bld.ToString();

        return newIndex;
    }
}

