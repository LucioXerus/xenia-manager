using System;
using System.Collections.Generic;
using System.Threading;
using SDL2;
using XeniaManager.Core.Logging;
using Logger = XeniaManager.Core.Logging.Logger;

namespace XeniaManager.Services;

public class GamepadInputService : IDisposable
{
    private static volatile bool _isRunning;
    private static readonly Lock LockObject = new Lock();
    private List<int> _gamepadInstances = [];
    private Thread? _pollThread;
    private volatile bool _shouldStop;
    private int _selectedIndex = -1;
    private int _gridColumns = 1;
    private bool _isNavigatingWithController;

    public event EventHandler<int>? SelectionChanged;
    public event EventHandler? ButtonXPressed;
    public event EventHandler? ButtonPSPressed;

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
                if (SDL.SDL_Init(SDL.SDL_INIT_GAMECONTROLLER) < 0)
                {
                    Logger.Error<GamepadInputService>($"SDL_Init failed: {SDL.SDL_GetError()}");
                    return;
                }

                int numJoysticks = SDL.SDL_NumJoysticks();
                Logger.Info<GamepadInputService>($"Found {numJoysticks} joysticks/gamepads");

                for (int i = 0; i < numJoysticks; i++)
                {
                    if (SDL.SDL_IsGameController(i) == SDL.SDL_bool.SDL_TRUE)
                    {
                        _gamepadInstances.Add(i);
                        Logger.Debug<GamepadInputService>($"Gamepad {i}: {SDL.SDL_GameControllerNameForIndex(i)}");
                    }
                }

                if (_gamepadInstances.Count == 0)
                {
                    Logger.Warning<GamepadInputService>("No gamepads found");
                    SDL.SDL_Quit();
                    return;
                }

                Logger.Info<GamepadInputService>($"Found {_gamepadInstances.Count} gamepad(s)");

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
        _gamepadInstances.Clear();
        SDL.SDL_Quit();
        _isRunning = false;
    }

    public void SetGridColumns(int columns)
    {
        _gridColumns = columns > 0 ? columns : 1;
    }

    public void SetSelectedIndex(int index)
    {
        _selectedIndex = index;
    }

    public int GetSelectedIndex() => _selectedIndex;

    public void SetNavigatingWithController(bool navigating)
    {
        _isNavigatingWithController = navigating;
    }

    public bool IsNavigatingWithController() => _isNavigatingWithController;

    private void PollGamepads()
    {
        bool dpadLeftPressed = false;
        bool dpadRightPressed = false;
        bool dpadUpPressed = false;
        bool dpadDownPressed = false;
        bool buttonAPressed = false;
        bool buttonPSPressed = false;

        IntPtr[] gameControllers = new IntPtr[_gamepadInstances.Count];
        for (int i = 0; i < _gamepadInstances.Count; i++)
        {
            gameControllers[i] = SDL.SDL_GameControllerOpen(_gamepadInstances[i]);
        }

        while (!_shouldStop)
        {
            try
            {
                Thread.Sleep(16);

                SDL.SDL_PumpEvents();

                for (int i = 0; i < gameControllers.Length; i++)
                {
                    IntPtr controller = gameControllers[i];
                    if (controller == IntPtr.Zero) continue;

                    int axisX = SDL.SDL_GameControllerGetAxis(controller, SDL.SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_LEFTX);
                    int axisY = SDL.SDL_GameControllerGetAxis(controller, SDL.SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_LEFTY);

                    bool dpadLeft = axisX < -16000;
                    bool dpadRight = axisX > 16000;
                    bool dpadUp = axisY < -16000;
                    bool dpadDown = axisY > 16000;

                    if (dpadLeft && !dpadLeftPressed)
                    {
                        dpadLeftPressed = true;
                        OnDPadLeft();
                    }
                    else if (!dpadLeft)
                    {
                        dpadLeftPressed = false;
                    }

                    if (dpadRight && !dpadRightPressed)
                    {
                        dpadRightPressed = true;
                        OnDPadRight();
                    }
                    else if (!dpadRight)
                    {
                        dpadRightPressed = false;
                    }

                    if (dpadUp && !dpadUpPressed)
                    {
                        dpadUpPressed = true;
                        OnDPadUp();
                    }
                    else if (!dpadUp)
                    {
                        dpadUpPressed = false;
                    }

                    if (dpadDown && !dpadDownPressed)
                    {
                        dpadDownPressed = true;
                        OnDPadDown();
                    }
                    else if (!dpadDown)
                    {
                        dpadDownPressed = false;
                    }

                    byte buttonA = SDL.SDL_GameControllerGetButton(controller, SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_A);
                    if (buttonA == 1 && !buttonAPressed)
                    {
                        buttonAPressed = true;
                        OnButtonX();
                    }
                    else if (buttonA == 0)
                    {
                        buttonAPressed = false;
                    }

                    byte buttonGuide = SDL.SDL_GameControllerGetButton(controller, SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_GUIDE);
                    if (buttonGuide == 1 && !buttonPSPressed)
                    {
                        buttonPSPressed = true;
                        OnButtonPS();
                    }
                    else if (buttonGuide == 0)
                    {
                        buttonPSPressed = false;
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
            if (gameControllers[i] != IntPtr.Zero)
            {
                SDL.SDL_GameControllerClose(gameControllers[i]);
            }
        }
    }

    private void OnDPadLeft()
    {
        _isNavigatingWithController = true;
        int newIndex = _selectedIndex - 1;
        if (newIndex >= 0)
        {
            _selectedIndex = newIndex;
            SelectionChanged?.Invoke(this, _selectedIndex);
        }
    }

    private void OnDPadRight()
    {
        _isNavigatingWithController = true;
        int newIndex = _selectedIndex + 1;
        if (newIndex >= 0)
        {
            _selectedIndex = newIndex;
            SelectionChanged?.Invoke(this, _selectedIndex);
        }
    }

    private void OnDPadUp()
    {
        _isNavigatingWithController = true;
        int newIndex = _selectedIndex - _gridColumns;
        if (newIndex >= 0)
        {
            _selectedIndex = newIndex;
            SelectionChanged?.Invoke(this, _selectedIndex);
        }
    }

    private void OnDPadDown()
    {
        _isNavigatingWithController = true;
        int newIndex = _selectedIndex + _gridColumns;
        if (newIndex >= 0)
        {
            _selectedIndex = newIndex;
            SelectionChanged?.Invoke(this, _selectedIndex);
        }
    }

    private void OnButtonX()
    {
        _isNavigatingWithController = true;
        ButtonXPressed?.Invoke(this, EventArgs.Empty);
    }

    private void OnButtonPS()
    {
        _isNavigatingWithController = true;
        ButtonPSPressed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        Stop();
    }
}