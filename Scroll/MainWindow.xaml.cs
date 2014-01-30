﻿using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TETCSharpClient;
using TETCSharpClient.Data;
using MessageBox = System.Windows.MessageBox;

namespace Scroll
{
	public partial class MainWindow : IGazeListener
	{
		#region Variables

		private const double SPEED_BOOST = 20.0;
		private const double ACTIVE_SCROLL_AREA = 0.25; // 25% top and bottom
		private const int MAX_IMAGE_WIDTH = 1600;
		private ImageButton latestSelection;
		private readonly double dpiScale;
		private Matrix transfrm;
		private readonly Timer scrollTimer;
		private double scrollLevel;
		private bool canScroll;
		enum Direction { Up = -1, Down = 1 }

		#endregion

		#region Constructor

		public MainWindow()
		{
			var connectedOk = true;
			GazeManager.Instance.Activate(GazeManager.ApiVersion.VERSION_1_0, GazeManager.ClientMode.Push);
			GazeManager.Instance.AddGazeListener(this);
			if (!GazeManager.Instance.IsConnected)
			{
				Dispatcher.BeginInvoke(new Action(() => MessageBox.Show("EyeTribe Server has not been started")));
				connectedOk = false;
			}
			else if (!GazeManager.Instance.IsCalibrated)
			{
				Dispatcher.BeginInvoke(new Action(() => MessageBox.Show("User is not calibrated")));
				connectedOk = false;
			}
			if (!connectedOk)
			{
				Close();
				return;
			}

			InitializeComponent();
			dpiScale = CalcDpiScale();

			// Hide all from start
			PanelsVisibility(Visibility.Collapsed);

			// Register for mouse clicks (eg. taps) to show/hide panels and enable selection
			PreviewMouseDown += TapDown;
			PreviewMouseUp += TapUp;

			// Listen for keys
			KeyDown += ScrollWindowKeyDown;

			scrollTimer = new Timer {Interval = 30, Enabled = true};
			scrollTimer.Tick += ScrollTimerTick;

			Loaded += (sender, args) =>
				{
					if (Screen.PrimaryScreen.Bounds.Width > MAX_IMAGE_WIDTH)
						WebImage.Width = MAX_IMAGE_WIDTH*dpiScale;
					else
						WebImage.Width = Screen.PrimaryScreen.Bounds.Width*dpiScale;

					// Transformation matrix that accomodate for the DPI settings
					var presentationSource = PresentationSource.FromVisual(this);
					transfrm = presentationSource.CompositionTarget.TransformFromDevice;

					ExecuteSelectedButton("newyorktimes");
				};
		}

		#endregion

		#region Public methods

		public void OnGazeUpdate(GazeData gazeData)
		{
			var x = (int) Math.Round(gazeData.SmoothedCoordinates.X, 0);
			var y = (int) Math.Round(gazeData.SmoothedCoordinates.Y, 0);
			if (x == 0 & y == 0) return;
			// Invoke thread
			Dispatcher.BeginInvoke(new Action(() => UpdateUI(x, y)));
		}

		#endregion

		#region Private methods

		private void ScrollTimerTick(object sender, EventArgs e)
		{
			if (!canScroll) return;
			WebImageScroll.ScrollToVerticalOffset(WebImageScroll.VerticalOffset - scrollLevel);	
			canScroll = false;
		}

		private void TapDown(object sender, MouseButtonEventArgs e)
		{
			PanelsVisibility(Visibility.Visible);
		}

		private void TapUp(object sender, MouseButtonEventArgs e)
		{
			// Hide panlel and exe button click if needed
			PanelsVisibility(Visibility.Collapsed);
			var selectedButton = latestSelection;
			if (selectedButton != null)
			{
				ExecuteSelectedButton(selectedButton.Name);
			}
		}

		private void ScrollWindowKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
		{
			if (e.Key == Key.Escape)
				Close();
		}

		private void UpdateUI(int x, int y)
		{
			// Unhide the GazePointer if you want to see your gaze point
			if (GazePointer.Visibility == Visibility.Visible)
			{
				var relativePt = new Point(x, y);
				relativePt = transfrm.Transform(relativePt);
				Canvas.SetLeft(GazePointer, relativePt.X - GazePointer.Width / 2);
				Canvas.SetTop(GazePointer, relativePt.Y - GazePointer.Height / 2);
			}

			if (GridTop.Visibility == Visibility.Collapsed)
			{
				DoScroll(x, y);
			}
			else
			{
				DoButtonCheck(x, y);
			}
		}

		private void DoScroll(int x, int y)
		{
			WebImage.Focus();
			ClampMouse(ref x, ref y);

			// Scrolling based on distance to either top or bottom
			var h = Screen.PrimaryScreen.Bounds.Height;
			var xExp = 0.0;
			var newVar = h*ACTIVE_SCROLL_AREA;
			var doScroll = false;
			var direction = Direction.Up;
			if (y > h - newVar)
			{
				direction = Direction.Up;
				doScroll = true;
				xExp = 1 - ((h - y)/newVar);
			}
			else if (y < newVar)
			{
				direction = Direction.Down;
				doScroll = true;
				xExp = 1 - (y/newVar);
			}
			if (!doScroll) return;

			// Sigmoid function multiplied with the scroll direction and a constant SPEED_BOOST
			scrollLevel = (1 / (1 + Math.Exp((-10 * xExp) + 6))) * ((int)direction * SPEED_BOOST);
			canScroll = true;
		}

		private static void ClampMouse(ref int x, ref int y)
		{
			var w = Screen.PrimaryScreen.Bounds.Width;
			var h = Screen.PrimaryScreen.Bounds.Height;

			if (x >= w)
				x = w;
			else if (x <= 0)
				x = 0;

			if (y >= h)
				y = h;
			else if (y <= 0)
				y = 0;
		}

		private void DoButtonCheck(int x, int y)
		{
			var pt = new Point(x, y);
			pt = transfrm.Transform(pt);
			var foundCandidate = false;
			foreach (var child in GridButtons.Children.Cast<ImageButton>())
			{
				var isChecked = HitTest(child, pt);
				child.IsChecked = isChecked;
				if (!isChecked) continue;
				foundCandidate = true;
				latestSelection = child;
			}
			if (!foundCandidate)
			{
				latestSelection = null;
			}
		}

		private bool HitTest(ImageButton control, Point gazePt)
		{
			var gridPt = transfrm.Transform(control.PointToScreen(new Point(0, 0)));
			return gazePt.X > gridPt.X && gazePt.X < gridPt.X + control.ActualWidth &&
				gazePt.Y > gridPt.Y && gazePt.Y < gridPt.Y + control.ActualHeight;
		}

		private void ExecuteSelectedButton(string selectedButtonName)
		{
			if (selectedButtonName == null) return;

			WebImageScroll.ScrollToVerticalOffset(0); // reset scroll
			switch (selectedButtonName)
			{
				case "newyorktimes":
					WebImage.Source = new BitmapImage(new Uri("Graphics/newyorktimes.jpg", UriKind.RelativeOrAbsolute));
					break;
				case "techcrunch":
					WebImage.Source = new BitmapImage(new Uri("Graphics/techcrunch.jpg", UriKind.RelativeOrAbsolute));
					break;
				case "tumblr":
					WebImage.Source = new BitmapImage(new Uri("Graphics/tumblr.jpg", UriKind.RelativeOrAbsolute));
					break;
				case "note":
					WebImage.Source = new BitmapImage(new Uri("Graphics/note.jpg", UriKind.RelativeOrAbsolute));
					break;
				case "exit":
					Close();
					break;
			}
		}

		private void PanelsVisibility(Visibility visibility)
		{
			GridTop.Visibility = visibility;
		}

		private static void CleanUp()
		{
			GazeManager.Instance.Deactivate();
		}

		protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
		{
			CleanUp();
			base.OnClosing(e);
		}

		private static double CalcDpiScale()
		{
			return 96.0/GetSystemDpi().X;
		}

		#endregion

		#region Native methods

		public static Point GetSystemDpi()
		{
			var result = new Point();
			var hDc = GetDC(IntPtr.Zero);
			result.X = GetDeviceCaps(hDc, 88);
			result.Y = GetDeviceCaps(hDc, 90);
			ReleaseDC(IntPtr.Zero, hDc);
			return result;
		}

		[DllImport("gdi32.dll")]
		private static extern int GetDeviceCaps(IntPtr hdc, int nIndex);

		[DllImport("user32.dll")]
		private static extern IntPtr GetDC(IntPtr hWnd);

		[DllImport("user32.dll")]
		private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDc);

		#endregion
	}
}