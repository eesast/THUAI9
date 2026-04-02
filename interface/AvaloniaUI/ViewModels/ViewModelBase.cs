using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace THUAI9_Avalonia.ViewModels
{
    /// <summary>
    /// ViewModel 基类
    /// </summary>
    public class ViewModelBase : ObservableObject, IDisposable
    {
        public virtual void Dispose()
        {
            // 清理资源
        }
    }
}
