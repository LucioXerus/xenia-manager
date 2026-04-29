using System;
using System.Collections.Generic;
using System.Threading;
using Avalonia.Threading;
using SDL;
using XeniaManager.Core.Logging;

namespace XeniaManager.Services;

public unsafe class GamepadInputService : IDisposable
{
    private static volatile bool _isRunning;
    private static readonly object LockObject = new object();
    private readonly List<SDL_JoystickID> _gamepadIds = [];
    private Thread? _pollThread;
    private volatile bool _shouldStop;
    private int _selectedIndex = -1;
    private int _gridColumns = 1;
    private int _itemCount = 0;

    public event EventHandler<int>? SelectionChanged;
    public event EventHandler? LaunchPressed;
    public event EventHandler? QuitPressed;

    public static bool IsRunning => _isRunning;

    public void Start()
    {
        lock (LockObject)
        {
            if (_isRunning)
            {
                Logger.Warning<GamepadInputService>("GamepadInputService is already running");
                return;
            }

            try
            {
                if (!SDL3.SDL_Init(SDL_InitFlags.SDL_INIT_GAMEPAD))
                {
                    Logger.Error<GamepadInputService>($"SDL_Init failed: {SDL3.SDL_GetError()}");
                    return;
                }

                using var joysticks = SDL3.SDL_GetJoysticks();
                if (joysticks == null)
                {
                    Logger.Warning<GamepadInputService>("No joysticks found");
                    SDL3.SDL_Quit();
                    return;
                }

                Logger.Info<GamepadInputService>($"Found {joysticks.Count} joystick(s)");

                for (int i = 0; i < joysticks.Count; i++)
                {
                    SDL_JoystickID id = joysticks[i];
                    if (SDL3.SDL_IsGamepad(id))
                    {
                        _gamepadIds.Add(id);
                        Logger.Debug<GamepadInputService>($"Gamepad {id}: {SDL3.SDL_GetGamepadNameForID(id)}");
                    }
                }

                if (_gamepadIds.Count == 0)
                {
                    Logger.Warning<GamepadInputService>("No gamepads found");
                    SDL3.SDL_Quit();
                    return;
                }

                Logger.Info<GamepadInputService>($"Found {_gamepadIds.Count} gamepad(s)");

                _shouldStop = false;
                _pollThread = new Thread(PollGamepads)
                {
                    IsBackground = true,
                    Name = "GamepadPollThread"
                };
                _pollThread.Start();

                _isRunning = true;
                Logger.Info<GamepadInputService>("GamepadInputService started successfully");
            }
            catch (Exception ex)
            {
                Logger.Error<GamepadInputService>($"Failed to start GamepadInputService: {ex.Message}");
                Logger.LogExceptionDetails<GamepadInputService>(ex);
                Cleanup();
                throw;
            }
        }
    }

    public void Stop()
    {
        lock (LockObject)
        {
            if (!_isRunning)
            {
                Logger.Debug<GamepadInputService>("GamepadInputService is not running");
                return;
            }

            Cleanup();
            Logger.Info<GamepadInputService>("GamepadInputService stopped successfully");
        }
    }

    private void Cleanup()
    {
        _shouldStop = true;
        if (_pollThread != null)
        {
            _pollThread.Join(1000);
            _pollThread = null;
        }
        _gamepadIds.Clear();
        SDL3.SDL_Quit();
        _isRunning = false;
    }

    public void SetGridColumns(int columns)
    {
        _gridColumns = columns > 0 ? columns : 1;
    }

    public void SetItemCount(int count)
    {
        _itemCount = count;
        if (_selectedIndex >= _itemCount && _itemCount > 0)
        {
            _selectedIndex = _itemCount - 1;
        }
    }

    public void SetSelectedIndex(int index)
    {
        _selectedIndex = index;
    }

    public int GetSelectedIndex() => _selectedIndex;

    private void PollGamepads()
    {
        ControllerState[] states = new ControllerState[_gamepadIds.Count];
        for (int i = 0; i < states.Length; i++)
        {
            states[i] = new ControllerState();
        }

        SDL_Gamepad*[] gameControllers = new SDL_Gamepad*[_gamepadIds.Count];
        for (int i = 0; i < _gamepadIds.Count; i++)
        {
            gameControllers[i] = SDL3.SDL_OpenGamepad(_gamepadIds[i]);
        }

        DateTime lastNavTime = DateTime.MinValue;
        const int navCooldownMs = 150;

        while (!_shouldStop)
        {
            try
            {
                Thread.Sleep(16);
                SDL3.SDL_PumpEvents();

                bool anyNavigated = false;

                for (int i = 0; i < gameControllers.Length; i++)
                {
                    SDL_Gamepad* controller = gameControllers[i];
                    if (controller == null) continue;

                    ControllerState state = states[i];

                    bool dpadLeft = SDL3.SDL_GetGamepadButton(controller, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_LEFT);
                    bool dpadRight = SDL3.SDL_GetGamepadButton(controller, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_RIGHT);
                    bool dpadUp = SDL3.SDL_GetGamepadButton(controller, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_UP);
                    bool dpadDown = SDL3.SDL_GetGamepadButton(controller, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_DOWN);

                    short axisX = SDL3.SDL_GetGamepadAxis(controller, SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFTX);
                    short axisY = SDL3.SDL_GetGamepadAxis(controller, SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFTY);

                    bool stickLeft = axisX < -16000;
                    bool stickRight = axisX > 16000;
                    bool stickUp = axisY < -16000;
                    bool stickDown = axisY > 16000;

                    bool[] dpadCurrent = [dpadLeft, dpadRight, dpadUp, dpadDown];
                    bool[] stickCurrent = [stickLeft, stickRight, stickUp, stickDown];

                    for (int dir = 0; dir < 4; dir++)
                    {
                        bool dpadPressed = dpadCurrent[dir];
                        bool stickPressed = stickCurrent[dir];

                        bool wasPressed = state.DpadPressed[dir] || state.StickPressed[dir];
                        bool isPressed = dpadPressed || stickPressed;

                        if (isPressed && !wasPressed && !anyNavigated)
                        {
                            double elapsed = (DateTime.Now - lastNavTime).TotalMilliseconds;
                            if (elapsed >= navCooldownMs)
                            {
                                anyNavigated = true;
                                lastNavTime = DateTime.Now;
                                OnNavigate(dir);
                            }
                        }

                        state.DpadPressed[dir] = dpadPressed;
                        state.StickPressed[dir] = stickPressed;
                    }

                    bool buttonSouth = SDL3.SDL_GetGamepadButton(controller, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_SOUTH);
                    if (buttonSouth && !state.ButtonSouthPressed)
                    {
                        state.ButtonSouthPressed = true;
                        OnLaunchPressed();
                    }
                    else if (!buttonSouth)
                    {
                        state.ButtonSouthPressed = false;
                    }

                    bool buttonGuide = SDL3.SDL_GetGamepadButton(controller, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_GUIDE);
                    if (buttonGuide && !state.ButtonGuidePressed)
                    {
                        state.ButtonGuidePressed = true;
                        OnQuitPressed();
                    }
                    else if (!buttonGuide)
                    {
                        state.ButtonGuidePressed = false;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error<GamepadInputService>($"Error in gamepad poll loop: {ex.Message}");
            }
        }

        for (int i = 0; i < gameControllers.Length; i++)
        {
            if (gameControllers[i] != null)
            {
                SDL3.SDL_CloseGamepad(gameControllers[i]);
            }
        }
    }

    private class ControllerState
    {
        public bool[] DpadPressed = new bool[4];
        public bool[] StickPressed = new bool[4];
        public bool ButtonSouthPressed;
        public bool ButtonGuidePressed;
    }

    private void OnNavigate(int direction)
    {
        int newIndex = direction switch
        {
            0 => _selectedIndex - 1,
            1 => _selectedIndex + 1,
            2 => _selectedIndex - _gridColumns,
            3 => _selectedIndex + _gridColumns,
            _ => _selectedIndex
        };

        if (_itemCount > 0)
        {
            newIndex = Math.Clamp(newIndex, 0, _itemCount - 1);
        }
        else
        {
            newIndex = -1;
        }

        if (newIndex != _selectedIndex)
        {
            _selectedIndex = newIndex;
            Dispatcher.UIThread.Post(() => SelectionChanged?.Invoke(this, _selectedIndex));
        }
    }

    private void OnLaunchPressed()
    {
        Dispatcher.UIThread.Post(() => LaunchPressed?.Invoke(this, EventArgs.Empty));
    }

    private void OnQuitPressed()
    {
        Dispatcher.UIThread.Post(() => QuitPressed?.Invoke(this, EventArgs.Empty));
    }

    public void Dispose()
    {
        Stop();
    }
}
