using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace ClickerApp
{
    public partial class MainWindow : Window
    {
        // ── Win32 ─────────────────────────────────────────────────────────────────
        [DllImport("user32.dll")] static extern uint SendInput(uint n, INPUT[] i, int cb);
        [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr h, int id, uint mod, uint vk);
        [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr h, int id);

        [StructLayout(LayoutKind.Sequential)]
        struct INPUT { public uint type; public UNION u; }
        [StructLayout(LayoutKind.Explicit)]
        struct UNION { [FieldOffset(0)] public MI mi; }
        [StructLayout(LayoutKind.Sequential)]
        struct MI { public int dx, dy; public uint data, flags, time; public IntPtr extra; }

        const uint T_MOUSE = 0, LDN = 2, LUP = 4, RDN = 8, RUP = 16;

        // ── Стан ──────────────────────────────────────────────────────────────────
        bool    running;
        Thread? thread;
        Random  rng = new();
        uint    hotMod = 0x0002, hotVk = 0x5A; // Ctrl + Z
        bool    recording;
        const int HK = 9000;
        static readonly CultureInfo IC = CultureInfo.InvariantCulture;

        // ── Лог помилок на Робочий стіл ───────────────────────────────────────────
        static void Log(Exception ex)
        {
            try
            {
                File.WriteAllText(
                    Path.Combine(Environment.GetFolderPath(
                        Environment.SpecialFolder.Desktop), "clicker_error.txt"),
                    ex.ToString());
            }
            catch { }
        }

        // ── Конструктор ───────────────────────────────────────────────────────────
        public MainWindow()
        {
            try
            {
                InitializeComponent();
                LoadConfig();
                ComponentDispatcher.ThreadFilterMessage += OnMsg;
            }
            catch (Exception ex) { Log(ex); throw; }
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            try { base.OnSourceInitialized(e); RegHK(); }
            catch (Exception ex) { Log(ex); throw; }
        }

        // ── Цикл клікера ──────────────────────────────────────────────────────────
        void Loop()
        {
            // Зчитуємо параметри UI один раз на старті потоку
            bool isLmb = true;
            int  cps   = 100;
            bool anti  = false;
            double ji  = 0, bu = 0, ho = 0;

            Dispatcher.Invoke(() =>
            {
                isLmb = BtnLmb.IsChecked == true;
                if (!int.TryParse(TxtCps.Text, out cps) || cps < 1) cps = 100;
                anti = ChkAntiDetect.IsChecked == true;
                ji   = SldJitter.Value / 100.0;
                bu   = SldBurstChance.Value;
                ho   = SldHold.Value;
            });

            uint dn = isLmb ? LDN : RDN;
            uint up = isLmb ? LUP : RUP;
            var  sw = new Stopwatch();

            while (running)
            {
                sw.Restart();
                double iv = 1.0 / cps;
                if (anti) iv = Math.Max(0.0001, iv + (rng.NextDouble() * 2 - 1) * ji * iv);

                Click(dn);
                if (anti && ho > 0) Sleep(rng.NextDouble() * ho / 1000.0);
                Click(up);
                if (anti && rng.Next(1, 101) <= bu) Sleep(rng.Next(50, 501) / 1000.0);

                double left = iv - sw.Elapsed.TotalSeconds;
                if (left > 0) Sleep(left);
            }
        }

        static void Sleep(double s)
        {
            if (s <= 0) return;
            long t = (long)(s * Stopwatch.Frequency);
            long n = Stopwatch.GetTimestamp();
            if (s > 0.002) Thread.Sleep((int)((s - 0.002) * 1000));
            while (Stopwatch.GetTimestamp() - n < t) { }
        }

        static void Click(uint f)
        {
            var inp = new INPUT[1];
            inp[0].type = T_MOUSE;
            inp[0].u.mi.flags = f;
            SendInput(1, inp, Marshal.SizeOf<INPUT>());
        }

        // ── Старт / Стоп ──────────────────────────────────────────────────────────
        void Start()
        {
            if (running) return;
            running = true;
            SetUi(true);
            thread = new Thread(Loop) { IsBackground = true, Priority = ThreadPriority.Highest };
            thread.Start();
        }

        void Stop()
        {
            if (!running) return;
            running = false;
            SetUi(false);
        }

        void Toggle() { if (running) Stop(); else Start(); }

        void SetUi(bool on)
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(() => SetUi(on)); return; }
            var green = new SolidColorBrush(Color.FromRgb(0, 255, 136));
            var grey  = new SolidColorBrush(Color.FromRgb(85, 85, 85));
            StatusDot.Fill        = on ? green : grey;
            StatusText.Text       = on ? "ACTIVE" : "IDLE";
            StatusText.Foreground = StatusDot.Fill;
            BtnStart.Background   = on ? grey : green;
        }

        // ── Хоткей ────────────────────────────────────────────────────────────────
        void RegHK()
        {
            var h = new WindowInteropHelper(this).Handle;
            UnregisterHotKey(h, HK);
            RegisterHotKey(h, HK, hotMod, hotVk);
        }

        void OnMsg(ref MSG msg, ref bool handled)
        {
            if (msg.message == 0x0312 && msg.wParam.ToInt32() == HK && !recording)
            { Toggle(); handled = true; }
        }

        void BtnRecord_Click(object s, RoutedEventArgs e)
        {
            if (recording) return;
            recording = true;
            BtnRecord.Content = "...";
            TxtHotkey.Text    = "Нажмите клавишу";
            KeyDown += OnKey;
        }

        void OnKey(object s, KeyEventArgs e)
        {
            KeyDown -= OnKey;
            uint m = 0; string ms = "";
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) { m |= 0x0002; ms += "Ctrl+"; }
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))   { m |= 0x0004; ms += "Shift+"; }
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))     { m |= 0x0001; ms += "Alt+"; }
            Key k = e.Key == Key.System ? e.SystemKey : e.Key;
            if (k == Key.LeftCtrl  || k == Key.RightCtrl  ||
                k == Key.LeftShift || k == Key.RightShift ||
                k == Key.LeftAlt   || k == Key.RightAlt) k = Key.Z;
            hotMod = m; hotVk = (uint)KeyInterop.VirtualKeyFromKey(k);
            TxtHotkey.Text    = ms + k;
            BtnRecord.Content = "ЗАПИСЬ";
            recording = false;
            RegHK(); Save(); e.Handled = true;
        }

        // ── Конфіг через змінні середовища (без файлів) ───────────────────────────
        void Save()
        {
            Env("CPS",  TxtCps.Text);
            Env("MOD",  hotMod.ToString(IC));
            Env("VK",   hotVk.ToString(IC));
            Env("LMB",  (BtnLmb.IsChecked == true).ToString());
            Env("ANTI", (ChkAntiDetect.IsChecked == true).ToString());
            Env("JIT",  SldJitter.Value.ToString(IC));
            Env("BRST", SldBurstChance.Value.ToString(IC));
            Env("HOLD", SldHold.Value.ToString(IC));
        }

        void LoadConfig()
        {
            try
            {
                var cps = Env("CPS"); if (cps != null) TxtCps.Text = cps;

                if (Env("MOD") is string ms && uint.TryParse(ms, out uint mv)) hotMod = mv;
                if (Env("VK")  is string vs && uint.TryParse(vs, out uint vv)) hotVk  = vv;

                if (Env("LMB") is string ls && bool.TryParse(ls, out bool lv))
                { BtnLmb.IsChecked = lv; BtnRmb.IsChecked = !lv; }

                if (Env("ANTI") is string @as && bool.TryParse(@as, out bool av))
                    ChkAntiDetect.IsChecked = av;

                if (Env("JIT") is string js && double.TryParse(js, NumberStyles.Any, IC, out double jv))
                    SldJitter.Value = jv;
                if (Env("BRST") is string bs && double.TryParse(bs, NumberStyles.Any, IC, out double bv))
                    SldBurstChance.Value = bv;
                if (Env("HOLD") is string hs && double.TryParse(hs, NumberStyles.Any, IC, out double hv))
                    SldHold.Value = hv;

                // Текст хоткею
                string hk = "";
                if ((hotMod & 0x0002) != 0) hk += "Ctrl+";
                if ((hotMod & 0x0004) != 0) hk += "Shift+";
                if ((hotMod & 0x0001) != 0) hk += "Alt+";
                hk += KeyInterop.KeyFromVirtualKey((int)hotVk);
                TxtHotkey.Text = hk;
            }
            catch { /* перший запуск — залишаємо дефолти */ }
        }

        static void   Env(string k, string v) =>
            Environment.SetEnvironmentVariable("KM_"+k, v, EnvironmentVariableTarget.User);
        static string? Env(string k) =>
            Environment.GetEnvironmentVariable("KM_"+k, EnvironmentVariableTarget.User);

        // ── UI-події ──────────────────────────────────────────────────────────────
        void Window_MouseLeftButtonDown(object s, MouseButtonEventArgs e)
        { try { DragMove(); } catch { } }

        void MouseMode_Changed(object s, RoutedEventArgs e)
        {
            if (BtnLmb == null || BtnRmb == null) return;
            var green = new SolidColorBrush(Color.FromRgb(0, 255, 136));
            var dark  = new SolidColorBrush(Color.FromRgb(26, 26, 26));
            bool l = BtnLmb.IsChecked == true;
            BtnLmb.Background = l ? green : dark;
            BtnLmb.Foreground = l ? Brushes.Black : Brushes.Gray;
            BtnRmb.Background = l ? dark  : green;
            BtnRmb.Foreground = l ? Brushes.Gray  : Brushes.Black;
            Save();
        }

        void TxtCps_LostFocus(object s, RoutedEventArgs e)
        { if (!int.TryParse(TxtCps.Text, out int c) || c < 1) TxtCps.Text = "100"; Save(); }

        void AntiDetect_Toggle(object s, RoutedEventArgs e) => Save();
        void BtnStart_Click(object s, RoutedEventArgs e)    => Start();
        void BtnStop_Click(object s, RoutedEventArgs e)     => Stop();
        void Minimize_Click(object s, RoutedEventArgs e)    => WindowState = WindowState.Minimized;
        void Close_Click(object s, RoutedEventArgs e)       { Stop(); Save(); Close(); }
    }
}
