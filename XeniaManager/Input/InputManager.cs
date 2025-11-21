using Silk.NET.SDL;
using System;

namespace XeniaManager.Input
{
    public static unsafe class InputManager
    {
        private static Sdl? _sdl;
        private const int MaxControllers = 4;
        private static GameController*[] _controllers = new GameController*[MaxControllers];
        private static bool _initialized = false;

        private static void Init()
        {
            if (_initialized) return;
            try 
            {
                _sdl = Sdl.GetApi();
                if (_sdl.Init(Sdl.InitGamecontroller) < 0)
                {
                    return;
                }
                _initialized = true;
            }
            catch { }
        }

        public static bool IsConnected
        {
            get
            {
                if (!_initialized) Init();
                if (_sdl == null) return false;

                _sdl.PumpEvents();

                bool anyConnected = false;

                // 1. Check existing controllers
                for (int i = 0; i < MaxControllers; i++)
                {
                    if (_controllers[i] != null)
                    {
                        if (_sdl.GameControllerGetAttached(_controllers[i]) == SdlBool.False)
                        {
                            _sdl.GameControllerClose(_controllers[i]);
                            _controllers[i] = null;
                        }
                        else
                        {
                            anyConnected = true;
                        }
                    }
                }

                // 2. Try to add new controllers if we have space
                int numJoysticks = _sdl.NumJoysticks();
                for (int deviceIndex = 0; deviceIndex < numJoysticks; deviceIndex++)
                {
                    if (_sdl.IsGameController(deviceIndex) == SdlBool.True)
                    {
                        // Get Instance ID to check if already open
                        int instanceId = _sdl.JoystickGetDeviceInstanceID(deviceIndex);
                        
                        bool isAlreadyOpen = false;
                        for (int i = 0; i < MaxControllers; i++)
                        {
                            if (_controllers[i] != null)
                            {
                                var joystick = _sdl.GameControllerGetJoystick(_controllers[i]);
                                if (_sdl.JoystickInstanceID(joystick) == instanceId)
                                {
                                    isAlreadyOpen = true;
                                    break;
                                }
                            }
                        }

                        if (!isAlreadyOpen)
                        {
                            // Find empty slot
                            for (int i = 0; i < MaxControllers; i++)
                            {
                                if (_controllers[i] == null)
                                {
                                    _controllers[i] = _sdl.GameControllerOpen(deviceIndex);
                                    if (_controllers[i] != null) anyConnected = true;
                                    break;
                                }
                            }
                        }
                    }
                }

                return anyConnected;
            }
        }

        // Helper for checking buttons across all controllers
        private static bool IsButtonPressed(GameControllerButton button)
        {
            if (!_initialized || _sdl == null) return false;
            for (int i = 0; i < MaxControllers; i++)
            {
                if (_controllers[i] != null)
                {
                    if (_sdl.GameControllerGetButton(_controllers[i], button) == 1) return true;
                }
            }
            return false;
        }

        public static bool IsGuideButtonPressed()
        {
            if (IsButtonPressed(GameControllerButton.Guide)) return true;
            
            // Fallback: Back + Start on ANY controller (must be same controller)
            for (int i = 0; i < MaxControllers; i++)
            {
                if (_controllers[i] != null)
                {
                    bool back = _sdl.GameControllerGetButton(_controllers[i], GameControllerButton.Back) == 1;
                    bool start = _sdl.GameControllerGetButton(_controllers[i], GameControllerButton.Start) == 1;
                    if (back && start) return true;
                }
            }
            return false;
        }

        public static bool IsAPressed() => IsButtonPressed(GameControllerButton.A);
        public static bool IsDpadUpPressed() => IsButtonPressed(GameControllerButton.DpadUp);
        public static bool IsDpadDownPressed() => IsButtonPressed(GameControllerButton.DpadDown);
        public static bool IsDpadLeftPressed() => IsButtonPressed(GameControllerButton.DpadLeft);
        public static bool IsDpadRightPressed() => IsButtonPressed(GameControllerButton.DpadRight);
    }
}
