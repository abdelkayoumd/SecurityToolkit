using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SecurityToolkit.UI
{
    /// <summary>The grey hint inside an empty text box ("Search all columns", "Find a screen").</summary>
    internal static class TextCue
    {
        private const int EmSetCueBanner = 0x1501;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        public static void Set(TextBox box, string cue)
        {
            void Apply() => SendMessage(box.Handle, EmSetCueBanner, (IntPtr)1, cue);
            if (box.IsHandleCreated)
            {
                Apply();
            }
            else
            {
                box.HandleCreated += (s, e) => Apply();
            }
        }
    }
}
