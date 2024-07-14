using System;
using System.Diagnostics;
using System.Net.Mime;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Microsoft.CodeAnalysis;
using System.Text.RegularExpressions;
using PulsarUI.Models;
using System.Text.RegularExpressions;
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

    private void EnterPairLeftNum_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            string value = ((TextBox)sender).Text;
            PairModel.WriteQueueRaceNum(0,0,value);
            
            LeftIndexBox.Focus();
        }
    }

    private void EnterPairLeftIndex_OnKeyUp(object? sender, KeyEventArgs e)
    {
        TextBox tb = (TextBox)sender;
        int caretIndex = tb.CaretIndex;
        string originalText = tb.Text;

        if (e.Key == Key.Back)
        {
            // Handle Backspace
            if (caretIndex > 0)
            {
                HandleBackspace(tb, caretIndex, originalText);
            }
        }
        else if (e.Key == Key.Delete && caretIndex < tb.Text.Length)
        {
            HandleDelete(tb, caretIndex, originalText);
        }
    }
    
    private void HandleBackspace(TextBox tb, int caretIndex, string originalText)
    {
        // Simulate the removal of the character before the caret
        string newText = originalText.Remove(caretIndex - 1, 1);

        // Validate newText after deleting the character
        if (!IsIndexAllowed(newText))
        {
            // Re-insert the period if necessary
            newText = ReinsertPeriod(newText, caretIndex - 1);
        }

        tb.Text = newText;
        tb.CaretIndex = caretIndex - 1; // Move caret back
    }

    private void HandleDelete(TextBox tb, int caretIndex, string originalText)
    {
        // Simulate the removal of the character after the caret
        string newText = originalText.Remove(caretIndex, 1);

        // Validate newText after deleting the character
        if (!IsIndexAllowed(newText))
        {
            // Re-insert the period if necessary
            newText = ReinsertPeriod(newText, caretIndex);
        }

        tb.Text = newText;
        tb.CaretIndex = caretIndex; // Keep caret position
    }

    private void EnterPairRightIndex_OnKeyUp(object? sender, KeyEventArgs e)
    {
        TextBox tb = (TextBox)sender;
        int caretIndex = tb.CaretIndex;
        string originalText = tb.Text;

        if (e.Key == Key.Back)
        {
            // Handle Backspace
            if (caretIndex > 0)
            {
                HandleBackspace(tb, caretIndex, originalText);
            }
        }
        else if (e.Key == Key.Delete || caretIndex < tb.Text.Length)
        {
            HandleDelete(tb, caretIndex, originalText);
        }
    }
    
    private string ReinsertPeriod(string text, int caretIndex)
    {
        // Insert period if necessary at caretIndex
        if (caretIndex > 0 && caretIndex <= text.Length && !text.Contains("."))
        {
            text = text.Insert(caretIndex, ".");
        }
        return text;
    }
    private void EnterPairLeftIndex_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            //Final catch-all check. If good, pad, otherwise remove string
            if (IsIndexAllowed(((TextBox)sender).Text))
            {
                ((TextBox)sender).Text = PadIndex(((TextBox)sender).Text);
            }
            else
            {
                ((TextBox)sender).Text = "00.00";//This should eventually be the class index or category index (in that order)
            }

            RightRaceNumBox.Focus();
        }
        else if (e.KeySymbol != null)
        {
            string enteredChar = e.KeySymbol;
            string oldText = ((TextBox)sender).Text;
            string newText = "";
            int iCarat = ((TextBox)sender).CaretIndex;

            if (oldText == null || ((TextBox)sender).SelectedText == oldText) //If the string was empty, no processing needed
            {
                newText = enteredChar;
            }
            else //Insert the new character at the carat position
            {
                string preCaratText = iCarat > 0 ? oldText.Substring(0, iCarat) : string.Empty;
                string postCaratText = iCarat < oldText.Length ? oldText.Substring(iCarat) : string.Empty;
                newText = preCaratText + enteredChar + postCaratText;
            }

            if (!IsIndexAllowed(newText))
            {
                e.Handled = true;
            }
            //If a user has typed two digits, automatically fill in the decimal point if one does not exist
            else if (newText.Length == 2 && !newText.EndsWith('.') && !newText.Contains("."))
            {
                newText += ".";
                ((TextBox)sender).Text = newText;
                ((TextBox)sender).CaretIndex = ((TextBox)sender).Text.Length;
                e.Handled = true;
            }

            //The index is deemed to be complete when there are two digits after the decimal point.
            //Select all to allow the user to begin overtyping
            if (newText.Length - newText.IndexOf(".") == 3)
            {
                ((TextBox)sender).Text = PadIndex(newText);
                ((TextBox)sender).CaretIndex = ((TextBox)sender).Text.Length;
                ((TextBox)sender).SelectAll();
                e.Handled = true;
            }
        }
    }

    private void EnterPairRightNum_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            string value = ((TextBox)sender).Text;
            PairModel.WriteQueueRaceNum(0,1,value);
            
            RightIndexBox.Focus();
        }
    }
    
    private void EnterPairRightIndex_OnKeyDown(object? sender, KeyEventArgs e)
    { 
        if (e.Key == Key.Enter)
        {
            //Final catch-all check. If good, pad, otherwise remove string
            if (IsIndexAllowed(((TextBox)sender).Text))
            {
                ((TextBox)sender).Text = PadIndex(((TextBox)sender).Text);
            }
            else
            {
                ((TextBox)sender).Text = "00.00";//This should eventually be the class index or category index (in that order)
            }

            LeftRaceNumBox.Focus();
        }
        else if (e.KeySymbol != null)
        {
            string enteredChar = e.KeySymbol;
            string oldText = ((TextBox)sender).Text;
            string newText = "";
            int iCarat = ((TextBox)sender).CaretIndex;

            if (oldText == null || ((TextBox)sender).SelectedText == oldText) //If the string was empty, no processing needed
            {
                newText = enteredChar;
            }
            else //Insert the new character at the carat position
            {
                string preCaratText = iCarat > 0 ? oldText.Substring(0, iCarat) : string.Empty;
                string postCaratText = iCarat < oldText.Length ? oldText.Substring(iCarat) : string.Empty;
                newText = preCaratText + enteredChar + postCaratText;
            }

            if (!IsIndexAllowed(newText))
            {
                e.Handled = true;
            }
            //If a user has typed two digits, automatically fill in the decimal point if one does not exist
            else if (newText.Length == 2 && !newText.EndsWith('.') && !newText.Contains("."))
            {
                newText += ".";
                ((TextBox)sender).Text = newText;
                ((TextBox)sender).CaretIndex = ((TextBox)sender).Text.Length;
                e.Handled = true;
            }

            //The index is deemed to be complete when there are two digits after the decimal point.
            //Select all to allow the user to begin overtyping
            if (newText.Length - newText.IndexOf(".") == 3)
            {
                ((TextBox)sender).Text = PadIndex(newText);
                ((TextBox)sender).CaretIndex = ((TextBox)sender).Text.Length;
                ((TextBox)sender).SelectAll();
                e.Handled = true;
            }
        }
    }
    private static bool IsIndexAllowed(string text)
    {
        //The following patterns are valid: 0, 00, 00.0, 00.00, 0,00, 0.0, 0., 00., ., .00, .0 
        //Only numbers and decimal points allowed. No more than 2 digits before or after the decimal point, only one decimal point
        //Need to implement locale feature to account for commas as decimal points
        if (text == null)
        {
            return false;
        }
        string pattern = @"^(\d{1,2}(\.\d{0,2})?|\.?\d{0,2})$";
        Regex regex = new Regex(pattern);
        return regex.IsMatch(text);
    }

    private string PadIndex(string currIndex)
    {
        string newIndex = currIndex;
        StringBuilder bld;

        if (newIndex == null)
        {
            newIndex = "00.00";
        }
        
        if (!newIndex.Contains("."))
        {
            newIndex += ".";
        }

        bld = new StringBuilder(newIndex);
        while (bld.ToString().IndexOf(".") < 2)
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

    private void EnterPairLeftNum_OnFocus(object? sender, GotFocusEventArgs e)
    {
        LeftRaceNumBox.SelectAll();
    }

    private void EnterPairLeftIndex_OnFocus(object? sender, GotFocusEventArgs e)
    {
        LeftIndexBox.SelectAll();
    }
    
    private void EnterPairRightNum_OnFocus(object? sender, GotFocusEventArgs e)
    {
        RightRaceNumBox.SelectAll();
    }

    private void EnterPairRightIndex_OnFocus(object? sender, GotFocusEventArgs e)
    {
        RightIndexBox.SelectAll();
    }
}

