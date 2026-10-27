using CommunityToolkit.Mvvm.ComponentModel;
using Origami.Mobile.ViewModels;

namespace Origami.Mobile.Navigation;

/// <summary>Minimal view-model navigation stack shared by desktop and mobile lifetimes.</summary>
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
