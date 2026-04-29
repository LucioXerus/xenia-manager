using System;
using System.Diagnostics;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.DependencyInjection;
using XeniaManager.Core.Logging;
using XeniaManager.Core.Manage;
using XeniaManager.Core.Utilities;
using XeniaManager.Services;
using XeniaManager.ViewModels.Items;
using XeniaManager.ViewModels.Pages;

namespace XeniaManager.Views.Pages;

public partial class LibraryPage : UserControl
{
    private LibraryPageViewModel _viewModel { get; set; }
    private GamepadInputService _gamepadService { get; set; }
    private GameItemViewModel? _lastSelectedGame;
    private ItemsRepeater? _itemsRepeater;
    private ScrollViewer? _scrollViewer;

    public LibraryPage()
    {
        InitializeComponent();
        _viewModel = App.Services.GetRequiredService<LibraryPageViewModel>();
        _gamepadService = App.Services.GetRequiredService<GamepadInputService>();
        DataContext = _viewModel;

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object? sender, EventArgs e)
    {
        _scrollViewer = this.FindControl<ScrollViewer>("GamesScrollViewer");
        _itemsRepeater = this.FindControl<ItemsRepeater>("GamesItemsRepeater");

        _viewModel.Games.CollectionChanged += (_, _) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                UpdateGridColumns();
                _gamepadService.SetItemCount(_viewModel.Games.Count);
                if (_viewModel.Games.Count > 0 && _gamepadService.GetSelectedIndex() < 0)
                {
                    SetControllerSelection(0);
                }
            });
        };

        _gamepadService.SetItemCount(_viewModel.Games.Count);
        if (_viewModel.Games.Count > 0)
        {
            SetControllerSelection(0);
        }

        _gamepadService.SelectionChanged += OnControllerSelectionChanged;
        _gamepadService.LaunchPressed += OnControllerLaunchPressed;
        _gamepadService.QuitPressed += OnControllerQuitPressed;

        if (!GamepadInputService.IsRunning)
        {
            _gamepadService.Start();
        }

        if (_itemsRepeater != null)
        {
            _itemsRepeater.SizeChanged += (_, _) => UpdateGridColumns();
        }

        UpdateGridColumns();
    }

    private void OnUnloaded(object? sender, EventArgs e)
    {
        _gamepadService.SelectionChanged -= OnControllerSelectionChanged;
        _gamepadService.LaunchPressed -= OnControllerLaunchPressed;
        _gamepadService.QuitPressed -= OnControllerQuitPressed;
    }

    private void UpdateGridColumns()
    {
        if (_itemsRepeater == null || _viewModel.Games.Count == 0) return;

        double availableWidth = _itemsRepeater.Bounds.Width;
        if (availableWidth <= 0) return;

        double minItemWidth = _viewModel.MinItemWidth + _viewModel.ItemSpacing;
        int columns = Math.Max(1, (int)(availableWidth / minItemWidth));
        _gamepadService.SetGridColumns(columns);
    }

    private void OnControllerSelectionChanged(object? sender, int index)
    {
        Dispatcher.UIThread.Post(() =>
        {
            SetControllerSelection(index);
        });
    }

    private void SetControllerSelection(int index)
    {
        if (_viewModel.Games.Count == 0) return;
        index = Math.Clamp(index, 0, _viewModel.Games.Count - 1);

        foreach (GameItemViewModel game in _viewModel.Games)
        {
            game.IsControllerSelected = false;
        }

        if (index >= 0 && index < _viewModel.Games.Count)
        {
            _viewModel.Games[index].IsControllerSelected = true;
            _gamepadService.SetSelectedIndex(index);
            ScrollToItem(index);
        }
    }

    private void ScrollToItem(int index)
    {
        if (_itemsRepeater == null || _scrollViewer == null) return;

        var container = _itemsRepeater.TryGetElement(index);
        if (container != null)
        {
            double offset = container.Bounds.Y;
            double height = container.Bounds.Height;
            double viewportHeight = _scrollViewer.Viewport.Height;
            double currentOffset = _scrollViewer.Offset.Y;

            if (offset < currentOffset)
            {
                _scrollViewer.Offset = new Avalonia.Vector(_scrollViewer.Offset.X, offset);
            }
            else if (offset + height > currentOffset + viewportHeight)
            {
                _scrollViewer.Offset = new Avalonia.Vector(_scrollViewer.Offset.X, offset + height - viewportHeight);
            }
        }
    }

    private void OnControllerLaunchPressed(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            int index = _gamepadService.GetSelectedIndex();
            if (index < 0 || index >= _viewModel.Games.Count) return;

            if (IsGameRunning())
            {
                Logger.Info<LibraryPage>("Game is already running, ignoring controller launch");
                return;
            }

            GameItemViewModel game = _viewModel.Games[index];
            if (game.LaunchCommand.CanExecute(null))
            {
                game.LaunchCommand.Execute(null);
            }
        });
    }

    private void OnControllerQuitPressed(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                int currentId = Process.GetCurrentProcess().Id;
                Process[] xeniaProcesses = Process.GetProcesses()
                    .Where(p => p.Id != currentId && p.ProcessName.StartsWith("xenia", StringComparison.OrdinalIgnoreCase))
                    .ToArray();

                foreach (Process process in xeniaProcesses)
                {
                    try
                    {
                        Logger.Info<LibraryPage>($"Killing Xenia process: {process.ProcessName} (PID: {process.Id})");
                        process.Kill();
                        process.WaitForExit(5000);
                    }
                    catch (Exception ex)
                    {
                        Logger.Error<LibraryPage>($"Failed to kill Xenia process: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error<LibraryPage>($"Error in controller quit: {ex.Message}");
            }
        });
    }

    private static bool IsGameRunning()
    {
        try
        {
            int currentId = Process.GetCurrentProcess().Id;
            return Process.GetProcesses()
                .Any(p => p.Id != currentId && p.ProcessName.StartsWith("xenia", StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
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
        var games = _viewModel.Games.ToList();
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
