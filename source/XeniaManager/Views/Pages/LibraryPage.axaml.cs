using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using XeniaManager.ViewModels.Items;
using XeniaManager.ViewModels.Pages;

namespace XeniaManager.Views.Pages;

public partial class LibraryPage : UserControl
{
    private LibraryPageViewModel _viewModel { get; set; }
    private GameItemViewModel? _lastSelectedGame;
    private bool _isGamepadNavigating = false;

    public LibraryPage()
    {
        InitializeComponent();
        _viewModel = App.Services.GetRequiredService<LibraryPageViewModel>();
        DataContext = _viewModel;

        _viewModel.QuitRequested += OnQuitRequested;
    }

    private async void OnQuitRequested(object? sender, EventArgs e)
    {
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            if (App.MainWindow != null)
            {
                App.MainWindow.Close();
            }
        });
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        Dispatcher.UIThread.Post(() =>
        {
            _viewModel.StartGamepadInput();
            UpdateGridColumnsForGamepad();
        });
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _viewModel.StopGamepadInput();
    }

    private void UpdateGridColumnsForGamepad()
    {
        if (Bounds.Width > 0 && _viewModel.MinItemWidth > 0)
        {
            int columns = Math.Max(1, (int)(Bounds.Width / (_viewModel.MinItemWidth + _viewModel.ItemSpacing)));
            _viewModel.UpdateGamepadGridColumns(columns);
        }
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        UpdateGridColumnsForGamepad();
    }

    private void OnGameButtonTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Button { DataContext: GameItemViewModel vm })
        {
            if (IsMultiselectModifierPressed(e))
            {
                HandleGameSelection(vm, e);
            }
            else if (!_viewModel.DoubleClickLaunch && !_viewModel.HasSelectedGames)
            {
                if (_isGamepadNavigating)
                {
                    _isGamepadNavigating = false;
                    return;
                }

                if (vm.LaunchCommand.CanExecute(null))
                {
                    vm.LaunchCommand.Execute(null);
                }
            }
            else if (_viewModel.HasSelectedGames)
            {
                HandleGameSelection(vm, e);
            }
        }
    }

    private void OnGameButtonDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_viewModel.DoubleClickLaunch && sender is Button { DataContext: GameItemViewModel vm })
        {
            if (!IsMultiselectModifierPressed(e) && !_viewModel.HasSelectedGames)
            {
                if (_isGamepadNavigating)
                {
                    _isGamepadNavigating = false;
                    return;
                }

                if (vm.LaunchCommand.CanExecute(null))
                {
                    vm.LaunchCommand.Execute(null);
                }
            }
        }
    }

    private bool IsMultiselectModifierPressed(TappedEventArgs e)
    {
        return e.KeyModifiers.HasFlag(KeyModifiers.Control) ||
               e.KeyModifiers.HasFlag(KeyModifiers.Shift);
    }

    private void HandleGameSelection(GameItemViewModel clickedGame, TappedEventArgs e)
    {
        List<GameItemViewModel> games = _viewModel.Games.ToList();
        int clickedIndex = games.IndexOf(clickedGame);
        if (clickedIndex < 0) return;

        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift) && _lastSelectedGame != null)
        {
            int lastIndex = games.IndexOf(_lastSelectedGame);
            if (lastIndex >= 0)
            {
                int start = Math.Min(lastIndex, clickedIndex);
                int end = Math.Max(lastIndex, clickedIndex);
                for (int i = start; i <= end; i++)
                {
                    games[i].IsSelected = true;
                }
            }
        }
        else if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            clickedGame.IsSelected = !clickedGame.IsSelected;
            _lastSelectedGame = clickedGame;
        }
        else
        {
            foreach (GameItemViewModel game in games)
            {
                game.IsSelected = false;
            }
            clickedGame.IsSelected = true;
            _lastSelectedGame = clickedGame;
        }
    }

    private void OnScrollViewerPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton == MouseButton.Left)
        {
            foreach (GameItemViewModel game in _viewModel.Games)
            {
                game.IsSelected = false;
            }
            _lastSelectedGame = null;
        }
    }
}