using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace MobControlUI.Core.UI
{
    public class HotkeyBox : TextBox
    {
        public static readonly DependencyProperty ValueProperty =
            DependencyProperty.Register(nameof(Value), typeof(string), typeof(HotkeyBox),
                new FrameworkPropertyMetadata(default(string),
                    FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                    (d, e) => ((HotkeyBox)d).Text = e.NewValue as string ?? ""));

        public string? Value
        {
            get => (string?)GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }

        public bool AllowKeyboard { get; set; } = true;
        public bool AllowMouseButtons { get; set; } = true;
        public bool AllowMouseWheel { get; set; } = true;

        // NEW: suppress first input used purely to focus the control
        private bool _suppressNextMouse;
        private bool _suppressNextWheel;

        public HotkeyBox()
        {
            IsReadOnly = true;
            Cursor = Cursors.IBeam;
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            base.OnPreviewKeyDown(e);

            if (e.Key == Key.Tab) return;                 // let focus move
            if (e.Key is Key.Escape) { e.Handled = true; return; }
            if (e.Key is Key.Back or Key.Delete) { Value = null; e.Handled = true; return; }

            if (!AllowKeyboard) return;

            var mods = Keyboard.Modifiers;
            var parts = new[]
            {
                mods.HasFlag(ModifierKeys.Control) ? "Ctrl" : null,
                mods.HasFlag(ModifierKeys.Shift)   ? "Shift" : null,
                mods.HasFlag(ModifierKeys.Alt)     ? "Alt" : null,
                mods.HasFlag(ModifierKeys.Windows) ? "Win" : null
            }.Where(p => p != null).Cast<string>().ToList();

            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
                or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin)
            { e.Handled = true; return; }

            var keyName = new KeyConverter().ConvertToString(key) ?? key.ToString();
            parts.Add(keyName);
            Value = string.Join("+", parts);
            e.Handled = true;
        }

        protected override void OnPreviewTextInput(TextCompositionEventArgs e)
        {
            e.Handled = true; // block raw chars
            base.OnPreviewTextInput(e);
        }

        protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
        {
            base.OnPreviewMouseDown(e);

            // Click to focus → don’t capture that click
            if (!IsKeyboardFocusWithin)
            {
                Focus();
                _suppressNextMouse = true;
                e.Handled = true;       // stop this click from becoming a binding
                return;
            }

            if (_suppressNextMouse)
            {
                _suppressNextMouse = false;
                e.Handled = true;       // ignore the first post-focus click
                return;
            }

            if (!AllowMouseButtons) return;

            Value = e.ChangedButton switch
            {
                MouseButton.Left => "MouseLeft",
                MouseButton.Right => "MouseRight",
                MouseButton.Middle => "MouseMiddle",
                MouseButton.XButton1 => "MouseX1",
                MouseButton.XButton2 => "MouseX2",
                _ => Value
            };
            e.Handled = true;
        }

        protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
        {
            base.OnPreviewMouseWheel(e);

            // Scroll to focus → don’t capture that first scroll
            if (!IsKeyboardFocusWithin)
            {
                Focus();
                _suppressNextWheel = true;
                e.Handled = true;
                return;
            }

            if (_suppressNextWheel)
            {
                _suppressNextWheel = false;
                e.Handled = true;
                return;
            }

            if (!AllowMouseWheel) return;

            Value = e.Delta > 0 ? "WheelUp" : "WheelDown";
            e.Handled = true;
        }
    }
}
