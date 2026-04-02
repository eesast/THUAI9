using Avalonia.Controls;
using Avalonia.Controls.Templates;
using System;
using System.Collections.Generic;
using THUAI9_Avalonia.ViewModels;

namespace THUAI9_Avalonia
{
    public class ViewLocator : IDataTemplate
    {
        private readonly Dictionary<Type, Func<Control>> _factories = new();

        public Control Build(object? data)
        {
            if (data == null)
                return new TextBlock { Text = "null" };

            var name = data.GetType().FullName!.Replace("ViewModel", "View", StringComparison.Ordinal);
            var type = Type.GetType(name);

            if (type != null)
            {
                var control = (Control)Activator.CreateInstance(type)!;
                control.DataContext = data;
                return control;
            }

            return new TextBlock { Text = "Not Found: " + name };
        }

        public bool Match(object? data)
        {
            return data is ViewModelBase;
        }
    }
}
