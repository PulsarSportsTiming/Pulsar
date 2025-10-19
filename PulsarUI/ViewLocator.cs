using System;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using PulsarUI.ViewModels;

namespace PulsarUI;

public class ViewLocator : IDataTemplate
{
    // Use nullable-aware signatures to match Avalonia interfaces and avoid null dereferences
    public Control? Build(object? data)
    {
        if (data == null)
            return new TextBlock { Text = "Not Found: (null)" };

        // Use a safe fallback if FullName is null (rare), avoid null-forgiving operator
        var fullName = data.GetType().FullName ?? data.GetType().Name ?? string.Empty;
        var name = fullName.Replace("ViewModel", "View");
        var type = Type.GetType(name);

        if (type != null)
        {
            return (Control?)Activator.CreateInstance(type);
        }

        return new TextBlock { Text = "Not Found: " + name };
    }

    public bool Match(object? data)
    {
        return data is ViewModelBase;
    }
}