using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using PulsarUI.Interfaces;
using PulsarUI.Models;
using PulsarUI.Services;

namespace PulsarUI.ViewModels
{
    public partial class MainWindowViewModel : ViewModelBase
    {
        private readonly IDatabaseService _databaseService;

        [GeneratedRegex(@"^(\d{0,2}(\.\d{0,2})?|\.?\d{0,2})?$")]
    
        private static partial Regex IndexPatternRegex();

        [ObservableProperty] private int _enterPairCategIndex;
        [ObservableProperty] private string _enterPairCategName;
        [ObservableProperty] private int _enterPairFinishIndex;
        [ObservableProperty] private string _enterPairFinishName;
        [ObservableProperty] private string _leftEnterPairRaceNum;
        [ObservableProperty] private string _rightEnterPairRaceNum;
        [ObservableProperty] private string _leftEnterPairIndex;
        [ObservableProperty] private string _rightEnterPairIndex;
        [ObservableProperty] private string _leftEnterPairClass;
        [ObservableProperty] private string _rightEnterPairClass;
        [ObservableProperty] private string _leftEnterPairName;
        [ObservableProperty] private string _rightEnterPairName;
        [ObservableProperty] private string _leftEnterPairVehicle;
        [ObservableProperty] private string _rightEnterPairVehicle;

        public MainWindowViewModel()
        {
            _databaseService = new DatabaseService("Data Source=/home/david/PulsarDB.db");
            EnterPairCategIndex = 1;
            EnterPairCategName = "Sportsman ET";
            EnterPairFinishIndex = 1;
            EnterPairFinishName = "402.336m";
        }

       public async Task EnterPairRaceNum_OnKeyDown(object? sender, KeyEventArgs e)
        {
            if (sender is not TextBox textBox) { return; }
            
            var currText = textBox.Text == null ? "" : textBox.Text.ToUpper();

            if (e.Key == Key.Enter)
            {
                if (!currText.All(char.IsAsciiLetterOrDigit)) // Catch-all in case of pasting
                {
                    currText = "";
                }

                if (textBox.Text != currText)
                {
                    textBox.Text = currText;
                }

                var raceEntry = new RaceEntry
                {
                    QueueIndex = 0
                };
                switch (textBox.Name)
                {
                    case "LeftRaceNumBox":
                        raceEntry.Lane = 0;
                        LeftEnterPairRaceNum = currText;
                        break;
                    case "RightRaceNumBox":
                        raceEntry.Lane = 1;
                        RightEnterPairRaceNum = currText;
                        break;
                }
                raceEntry.RaceNumber = currText;
                raceEntry.HandicapIndex = await GetVehIndexAsync(raceEntry.Lane);
                raceEntry.Tree = 0; //This needs implementing properly
                await _databaseService.WriteQueueAsync(raceEntry);
                await GetEntryDetailsAsync(raceEntry.Lane);
            }
            else if (e.KeySymbol != null && char.IsAsciiLetterOrDigit(e.KeySymbol[0]))
            {
                ProcessRaceNumKeyDown(sender, e);
            }
            else
            {
                e.Handled = true;
            }
        }

        private async Task<string> GetVehIndexAsync(int lane)
        {
            var entry = new RaceEntry
            {
                Category = EnterPairCategIndex,
                Finish = EnterPairFinishIndex,
                RaceNumber = lane switch
                {
                    0 => LeftEnterPairRaceNum,
                    1 => RightEnterPairRaceNum,
                    _ => null
                }
            };

            var indexList = await _databaseService.GetIndexListAsync(entry);

            var indexes = new[]
            {
                indexList.EventIndex,
                indexList.PersonalIndex,
                indexList.ClassIndex,
                indexList.CategoryIndex
            };

            switch (lane)
            {
                case 0:
                    LeftEnterPairIndex = indexes.FirstOrDefault(index => index != "") ?? "00.00";
                    break;
                case 1:
                    RightEnterPairIndex = indexes.FirstOrDefault(index => index != "") ?? "00.00";
                    break;
            }

            return indexes.FirstOrDefault(index => index != "") ?? "00.00";
        }
        
        private async Task GetEntryDetailsAsync(int lane)
        {
            var entry = new RaceEntry();

            entry.Category = EnterPairCategIndex;

            entry.RaceNumber = lane switch
            {
                0 => LeftEnterPairRaceNum,
                1 => RightEnterPairRaceNum,
                _ => entry.RaceNumber
            };
            
            var racerDetails = await _databaseService.GetRacerDetailsAsync(entry);

            switch (lane)
            {
                case 0:
                    LeftEnterPairClass = racerDetails.Class;
                    LeftEnterPairName = racerDetails.Name;
                    LeftEnterPairVehicle = racerDetails.Vehicle;
                    break;
                case 1:
                    RightEnterPairClass = racerDetails.Class;
                    RightEnterPairName = racerDetails.Name;
                    RightEnterPairVehicle = racerDetails.Vehicle;
                    break;
            }
        }

        private static void ProcessRaceNumKeyDown(object? sender, KeyEventArgs e)
        {
            var enteredChar = e.KeySymbol!.ToUpper();
            var oldText = ((TextBox)sender!).Text == null ? "" : ((TextBox)sender).Text!.ToUpper();
            var iCarat = ((TextBox)sender).CaretIndex;
            var newText = enteredChar;
        
            if (oldText.Length > 0 && oldText != ((TextBox)sender).SelectedText)
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

        public void EnterPairIndex_OnKeyDown(object? sender, KeyEventArgs e)
        {
            if (sender is not TextBox textBox) { return; }
            
            var currText = textBox.Text == null ? "" : textBox.Text.ToUpper();
            
            if (e.Key == Key.Enter)
            {
                currText = PadIndex(currText);
                
                if (textBox.Text != currText)
                {
                    textBox.Text = currText;
                }

                var raceEntry = new RaceEntry();
                raceEntry.QueueIndex = 0;
                switch (textBox.Name)
                {
                    case "LeftIndexBox":
                        raceEntry.Lane = 0;
                        raceEntry.RaceNumber = LeftEnterPairRaceNum;
                        break;
                    case "RightIndexBox":
                        raceEntry.Lane = 1;
                        raceEntry.RaceNumber = RightEnterPairRaceNum;
                        break;
                    default: break;
                }
                raceEntry.HandicapIndex = currText;
                raceEntry.Tree = 0; //This needs implementing properly
                _databaseService.WriteQueueAsync(raceEntry);
            }
            else if (e.KeySymbol != null)
            {
                ProcessIndexKeyDown(sender, e);
            }
        }
        
        public void EnterPairIndex_OnKeyUp(object? sender, KeyEventArgs e)
        {
            if (sender is not TextBox textBox) { return; }
            
            var currText = textBox.Text == null ? "" : textBox.Text.ToUpper();
            var caretIndex = textBox.CaretIndex;
        
            if (e.Key == Key.Back)
            {
                // Handle Backspace
                if (caretIndex > 0)
                {
                    HandleBackspace(textBox, caretIndex, currText);
                }
            }
            else if (e.Key == Key.Delete && caretIndex < textBox.Text!.Length)
            {
                HandleDelete(textBox, caretIndex, currText);
            }
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
        
        private static string ReinsertPeriod(string text, int caretIndex)
        {
            // Insert period if necessary at caretIndex
            if (caretIndex > 0 && caretIndex <= text.Length && !text.Contains('.'))
            {
                text = text.Insert(caretIndex, ".");
            }
            return text;
        }
        
        private static void ProcessIndexKeyDown(object? sender, KeyEventArgs e)
        {
            var enteredChar = e.KeySymbol!;
            var oldText = ((TextBox)sender!).Text == null ? "" : ((TextBox)sender).Text!.ToUpper();
            var iCarat = ((TextBox)sender).CaretIndex;
            var newText = enteredChar;
        
            if (oldText.Length > 0 && oldText != ((TextBox)sender).SelectedText)
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
        
        private static bool IsIndexAllowed(string text)
        {
            //The following patterns are valid: 0, 00, 00.0, 00.00, 0,00, 0.0, 0., 00., ., .00, .0 
            //Only numbers and decimal points allowed. No more than 2 digits before or after the decimal point, only one decimal point
            //Need to implement locale feature to account for commas as decimal points
            if (text == "")
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
    
}
