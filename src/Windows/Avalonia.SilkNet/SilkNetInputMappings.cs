using Avalonia.Input;
using Silk.NET.Input;
using AvaloniaKey = Avalonia.Input.Key;
using SilkKey = Silk.NET.Input.Key;
using SilkMouseButton = Silk.NET.Input.MouseButton;

namespace Avalonia.SilkNet
{
    internal readonly record struct SilkNetKeyMapping(AvaloniaKey Key, PhysicalKey PhysicalKey);

    internal static class SilkNetInputMappings
    {
        public static SilkNetKeyMapping MapKey(SilkKey key)
        {
            return new SilkNetKeyMapping(MapLogicalKey(key), MapPhysicalKey(key));
        }

        public static RawInputModifiers GetKeyboardModifiers(
            IKeyboard keyboard,
            SilkKey eventKey = SilkKey.Unknown,
            bool eventKeyIsDown = false)
        {
            var modifiers = RawInputModifiers.None;
            if (IsPressed(keyboard, SilkKey.AltLeft, eventKey, eventKeyIsDown) ||
                IsPressed(keyboard, SilkKey.AltRight, eventKey, eventKeyIsDown))
            {
                modifiers |= RawInputModifiers.Alt;
            }

            if (IsPressed(keyboard, SilkKey.ControlLeft, eventKey, eventKeyIsDown) ||
                IsPressed(keyboard, SilkKey.ControlRight, eventKey, eventKeyIsDown))
            {
                modifiers |= RawInputModifiers.Control;
            }

            if (IsPressed(keyboard, SilkKey.ShiftLeft, eventKey, eventKeyIsDown) ||
                IsPressed(keyboard, SilkKey.ShiftRight, eventKey, eventKeyIsDown))
            {
                modifiers |= RawInputModifiers.Shift;
            }

            if (IsPressed(keyboard, SilkKey.SuperLeft, eventKey, eventKeyIsDown) ||
                IsPressed(keyboard, SilkKey.SuperRight, eventKey, eventKeyIsDown))
            {
                modifiers |= RawInputModifiers.Meta;
            }

            return modifiers;
        }

        public static RawInputModifiers GetPointerModifiers(
            IInputContext? inputContext,
            IMouse mouse,
            SilkMouseButton eventButton = SilkMouseButton.Unknown,
            bool eventButtonIsDown = false)
        {
            var modifiers = RawInputModifiers.None;
            if (inputContext != null)
            {
                foreach (var keyboard in inputContext.Keyboards)
                {
                    modifiers |= GetKeyboardModifiers(keyboard);
                }
            }

            if (IsPressed(mouse, SilkMouseButton.Left, eventButton, eventButtonIsDown))
            {
                modifiers |= RawInputModifiers.LeftMouseButton;
            }
            if (IsPressed(mouse, SilkMouseButton.Right, eventButton, eventButtonIsDown))
            {
                modifiers |= RawInputModifiers.RightMouseButton;
            }
            if (IsPressed(mouse, SilkMouseButton.Middle, eventButton, eventButtonIsDown))
            {
                modifiers |= RawInputModifiers.MiddleMouseButton;
            }
            if (IsPressed(mouse, SilkMouseButton.Button4, eventButton, eventButtonIsDown))
            {
                modifiers |= RawInputModifiers.XButton1MouseButton;
            }
            if (IsPressed(mouse, SilkMouseButton.Button5, eventButton, eventButtonIsDown))
            {
                modifiers |= RawInputModifiers.XButton2MouseButton;
            }

            return modifiers;
        }

        private static bool IsPressed(
            IKeyboard keyboard,
            SilkKey key,
            SilkKey eventKey,
            bool eventKeyIsDown)
        {
            return keyboard.IsKeyPressed(key) || eventKeyIsDown && eventKey == key;
        }

        private static bool IsPressed(
            IMouse mouse,
            SilkMouseButton button,
            SilkMouseButton eventButton,
            bool eventButtonIsDown)
        {
            return mouse.IsButtonPressed(button) || eventButtonIsDown && eventButton == button;
        }

        private static AvaloniaKey MapLogicalKey(SilkKey key)
        {
            return key switch
            {
                SilkKey.Space => AvaloniaKey.Space,
                SilkKey.Apostrophe => AvaloniaKey.OemQuotes,
                SilkKey.Comma => AvaloniaKey.OemComma,
                SilkKey.Minus => AvaloniaKey.OemMinus,
                SilkKey.Period => AvaloniaKey.OemPeriod,
                SilkKey.Slash => AvaloniaKey.OemQuestion,
                SilkKey.Number0 => AvaloniaKey.D0,
                SilkKey.Number1 => AvaloniaKey.D1,
                SilkKey.Number2 => AvaloniaKey.D2,
                SilkKey.Number3 => AvaloniaKey.D3,
                SilkKey.Number4 => AvaloniaKey.D4,
                SilkKey.Number5 => AvaloniaKey.D5,
                SilkKey.Number6 => AvaloniaKey.D6,
                SilkKey.Number7 => AvaloniaKey.D7,
                SilkKey.Number8 => AvaloniaKey.D8,
                SilkKey.Number9 => AvaloniaKey.D9,
                SilkKey.Semicolon => AvaloniaKey.OemSemicolon,
                SilkKey.Equal => AvaloniaKey.OemPlus,
                >= SilkKey.A and <= SilkKey.Z => AvaloniaKey.A + (key - SilkKey.A),
                SilkKey.LeftBracket => AvaloniaKey.OemOpenBrackets,
                SilkKey.BackSlash => AvaloniaKey.OemPipe,
                SilkKey.RightBracket => AvaloniaKey.OemCloseBrackets,
                SilkKey.GraveAccent => AvaloniaKey.OemTilde,
                SilkKey.Escape => AvaloniaKey.Escape,
                SilkKey.Enter => AvaloniaKey.Enter,
                SilkKey.Tab => AvaloniaKey.Tab,
                SilkKey.Backspace => AvaloniaKey.Back,
                SilkKey.Insert => AvaloniaKey.Insert,
                SilkKey.Delete => AvaloniaKey.Delete,
                SilkKey.Right => AvaloniaKey.Right,
                SilkKey.Left => AvaloniaKey.Left,
                SilkKey.Down => AvaloniaKey.Down,
                SilkKey.Up => AvaloniaKey.Up,
                SilkKey.PageUp => AvaloniaKey.PageUp,
                SilkKey.PageDown => AvaloniaKey.PageDown,
                SilkKey.Home => AvaloniaKey.Home,
                SilkKey.End => AvaloniaKey.End,
                SilkKey.CapsLock => AvaloniaKey.CapsLock,
                SilkKey.ScrollLock => AvaloniaKey.Scroll,
                SilkKey.NumLock => AvaloniaKey.NumLock,
                SilkKey.PrintScreen => AvaloniaKey.PrintScreen,
                SilkKey.Pause => AvaloniaKey.Pause,
                >= SilkKey.F1 and <= SilkKey.F24 => AvaloniaKey.F1 + (key - SilkKey.F1),
                SilkKey.Keypad0 => AvaloniaKey.NumPad0,
                SilkKey.Keypad1 => AvaloniaKey.NumPad1,
                SilkKey.Keypad2 => AvaloniaKey.NumPad2,
                SilkKey.Keypad3 => AvaloniaKey.NumPad3,
                SilkKey.Keypad4 => AvaloniaKey.NumPad4,
                SilkKey.Keypad5 => AvaloniaKey.NumPad5,
                SilkKey.Keypad6 => AvaloniaKey.NumPad6,
                SilkKey.Keypad7 => AvaloniaKey.NumPad7,
                SilkKey.Keypad8 => AvaloniaKey.NumPad8,
                SilkKey.Keypad9 => AvaloniaKey.NumPad9,
                SilkKey.KeypadDecimal => AvaloniaKey.Decimal,
                SilkKey.KeypadDivide => AvaloniaKey.Divide,
                SilkKey.KeypadMultiply => AvaloniaKey.Multiply,
                SilkKey.KeypadSubtract => AvaloniaKey.Subtract,
                SilkKey.KeypadAdd => AvaloniaKey.Add,
                SilkKey.KeypadEnter => AvaloniaKey.Enter,
                SilkKey.KeypadEqual => AvaloniaKey.OemPlus,
                SilkKey.ShiftLeft => AvaloniaKey.LeftShift,
                SilkKey.ControlLeft => AvaloniaKey.LeftCtrl,
                SilkKey.AltLeft => AvaloniaKey.LeftAlt,
                SilkKey.SuperLeft => AvaloniaKey.LWin,
                SilkKey.ShiftRight => AvaloniaKey.RightShift,
                SilkKey.ControlRight => AvaloniaKey.RightCtrl,
                SilkKey.AltRight => AvaloniaKey.RightAlt,
                SilkKey.SuperRight => AvaloniaKey.RWin,
                SilkKey.Menu => AvaloniaKey.Apps,
                _ => AvaloniaKey.None
            };
        }

        private static PhysicalKey MapPhysicalKey(SilkKey key)
        {
            return key switch
            {
                SilkKey.Space => PhysicalKey.Space,
                SilkKey.Apostrophe => PhysicalKey.Quote,
                SilkKey.Comma => PhysicalKey.Comma,
                SilkKey.Minus => PhysicalKey.Minus,
                SilkKey.Period => PhysicalKey.Period,
                SilkKey.Slash => PhysicalKey.Slash,
                SilkKey.Number0 => PhysicalKey.Digit0,
                SilkKey.Number1 => PhysicalKey.Digit1,
                SilkKey.Number2 => PhysicalKey.Digit2,
                SilkKey.Number3 => PhysicalKey.Digit3,
                SilkKey.Number4 => PhysicalKey.Digit4,
                SilkKey.Number5 => PhysicalKey.Digit5,
                SilkKey.Number6 => PhysicalKey.Digit6,
                SilkKey.Number7 => PhysicalKey.Digit7,
                SilkKey.Number8 => PhysicalKey.Digit8,
                SilkKey.Number9 => PhysicalKey.Digit9,
                SilkKey.Semicolon => PhysicalKey.Semicolon,
                SilkKey.Equal => PhysicalKey.Equal,
                >= SilkKey.A and <= SilkKey.Z => PhysicalKey.A + (key - SilkKey.A),
                SilkKey.LeftBracket => PhysicalKey.BracketLeft,
                SilkKey.BackSlash => PhysicalKey.Backslash,
                SilkKey.RightBracket => PhysicalKey.BracketRight,
                SilkKey.GraveAccent => PhysicalKey.Backquote,
                SilkKey.World1 => PhysicalKey.IntlBackslash,
                SilkKey.World2 => PhysicalKey.IntlRo,
                SilkKey.Escape => PhysicalKey.Escape,
                SilkKey.Enter => PhysicalKey.Enter,
                SilkKey.Tab => PhysicalKey.Tab,
                SilkKey.Backspace => PhysicalKey.Backspace,
                SilkKey.Insert => PhysicalKey.Insert,
                SilkKey.Delete => PhysicalKey.Delete,
                SilkKey.Right => PhysicalKey.ArrowRight,
                SilkKey.Left => PhysicalKey.ArrowLeft,
                SilkKey.Down => PhysicalKey.ArrowDown,
                SilkKey.Up => PhysicalKey.ArrowUp,
                SilkKey.PageUp => PhysicalKey.PageUp,
                SilkKey.PageDown => PhysicalKey.PageDown,
                SilkKey.Home => PhysicalKey.Home,
                SilkKey.End => PhysicalKey.End,
                SilkKey.CapsLock => PhysicalKey.CapsLock,
                SilkKey.ScrollLock => PhysicalKey.ScrollLock,
                SilkKey.NumLock => PhysicalKey.NumLock,
                SilkKey.PrintScreen => PhysicalKey.PrintScreen,
                SilkKey.Pause => PhysicalKey.Pause,
                >= SilkKey.F1 and <= SilkKey.F24 => PhysicalKey.F1 + (key - SilkKey.F1),
                SilkKey.Keypad0 => PhysicalKey.NumPad0,
                SilkKey.Keypad1 => PhysicalKey.NumPad1,
                SilkKey.Keypad2 => PhysicalKey.NumPad2,
                SilkKey.Keypad3 => PhysicalKey.NumPad3,
                SilkKey.Keypad4 => PhysicalKey.NumPad4,
                SilkKey.Keypad5 => PhysicalKey.NumPad5,
                SilkKey.Keypad6 => PhysicalKey.NumPad6,
                SilkKey.Keypad7 => PhysicalKey.NumPad7,
                SilkKey.Keypad8 => PhysicalKey.NumPad8,
                SilkKey.Keypad9 => PhysicalKey.NumPad9,
                SilkKey.KeypadDecimal => PhysicalKey.NumPadDecimal,
                SilkKey.KeypadDivide => PhysicalKey.NumPadDivide,
                SilkKey.KeypadMultiply => PhysicalKey.NumPadMultiply,
                SilkKey.KeypadSubtract => PhysicalKey.NumPadSubtract,
                SilkKey.KeypadAdd => PhysicalKey.NumPadAdd,
                SilkKey.KeypadEnter => PhysicalKey.NumPadEnter,
                SilkKey.KeypadEqual => PhysicalKey.NumPadEqual,
                SilkKey.ShiftLeft => PhysicalKey.ShiftLeft,
                SilkKey.ControlLeft => PhysicalKey.ControlLeft,
                SilkKey.AltLeft => PhysicalKey.AltLeft,
                SilkKey.SuperLeft => PhysicalKey.MetaLeft,
                SilkKey.ShiftRight => PhysicalKey.ShiftRight,
                SilkKey.ControlRight => PhysicalKey.ControlRight,
                SilkKey.AltRight => PhysicalKey.AltRight,
                SilkKey.SuperRight => PhysicalKey.MetaRight,
                SilkKey.Menu => PhysicalKey.ContextMenu,
                _ => PhysicalKey.None
            };
        }
    }
}
