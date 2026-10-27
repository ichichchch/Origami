using CommunityToolkit.Mvvm.ComponentModel;
using Origami.Mobile.ViewModels;

namespace Origami.Mobile.Navigation;

/// <summary>供桌面與行動裝置生命週期共用的最小 ViewModel 導覽堆疊。</summary>
public sealed class Navigator : ObservableObject
{
    private readonly Stack<ViewModelBase> stack = [];
    private ViewModelBase current = null!;

    public ViewModelBase Current
    {
        get => current;
        private set => SetProperty(ref current, value);
    }

    public bool CanBack => stack.Count > 1;

    public void Push(ViewModelBase viewModel)
    {
        stack.Push(viewModel);
        Current = viewModel;
        OnPropertyChanged(nameof(CanBack));
    }

    public void Back()
    {
        if (stack.Count <= 1) return;
        stack.Pop();
        Current = stack.Peek();
        OnPropertyChanged(nameof(CanBack));
    }
}
