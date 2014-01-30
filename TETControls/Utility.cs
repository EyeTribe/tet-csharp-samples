using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using TETCSharpClient.Data;


namespace TETControls
{
    public class Utility
    {
        #region Variabels 

        private static Utility _instance;
        private Point sysDpi;
        private float scaleDpi;
        private const int Logpixelsx = 88; // Used for GetDeviceCaps().
        private const int Logpixelsy = 90; // Used for GetDeviceCaps().

        #endregion

        #region Constructor

        private Utility()
        {
            sysDpi = GetSystemDpi();
            ScaleDpi = 96f / sysDpi.X;
        }

        #endregion

        #region Get/Set

        public static Utility Instance
        {
            get { return _instance ?? (_instance = new Utility()); }
        }

        public Point SysDpi
        {
            get { return sysDpi; }
            set { sysDpi = value; }
        }

        public float ScaleDpi
        {
            get { return scaleDpi; }
            set { scaleDpi = value; }
        }

        #endregion

        #region Public methods

        public static Point GetSystemDpi()
        {
            Point result = new Point();
            IntPtr hDc = GetDC(IntPtr.Zero);

            result.X = GetDeviceCaps(hDc, Logpixelsx);
            result.Y = GetDeviceCaps(hDc, Logpixelsy);

            ReleaseDC(IntPtr.Zero, hDc);

            return result;
        }

        #endregion

        #region Private methods (DLL Imports)

        [DllImport("gdi32.dll")]
        private static extern int GetDeviceCaps(IntPtr hdc, int nIndex);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDc);

        #endregion
    }
}
