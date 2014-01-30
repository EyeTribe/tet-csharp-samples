using System;
using System.Windows;
using System.Windows.Forms;
using TETControls.Calibration;
using TETCSharpClient.Data;
using TETControls;
using System.Windows.Interop;
using TETCSharpClient;
using MessageBox = System.Windows.MessageBox;


namespace Calibration
{
	public partial class MainWindow
	{
		public MainWindow()
		{
			// Create a client for the eye tracker
			GazeManager.Instance.Activate(GazeManager.ApiVersion.VERSION_1_0, GazeManager.ClientMode.Push);
			InitializeComponent();

			if (!GazeManager.Instance.IsConnected)
			{
				MessageBox.Show("EyeTribe Server has not been started");
				Close();
			}
			else if (GazeManager.Instance.IsCalibrated)
			{
				// Get the latest successful calibration from the EyeTribe server
				RatingText.Text = RatingFunction(GazeManager.Instance.LastCalibrationResult);
				btnCalibrate.Content = "Re-Calibrate";
			}
		}

		private void CalibrateClicked(object sender, RoutedEventArgs e)
		{
			btnCalibrate.Content = "Re-Calibrate";
			Calibrate();
		}

		private void Calibrate()
		{
			//Run the calibration on 'this' monitor
			var ActiveScreen = Screen.FromHandle(new WindowInteropHelper(this).Handle);
			
			// Initialize and start the calibration
			CalibrationRunner calRunner = new CalibrationRunner(ActiveScreen, ActiveScreen.Bounds.Size, 9);

			var isCalibrated = calRunner.Start();
			if (!isCalibrated) return;

			// Show the rating of last accepted current calibration
			RatingText.Text = RatingFunction(GazeManager.Instance.LastCalibrationResult);
		}

		private void WindowClosed(object sender, EventArgs e)
		{
			GazeManager.Instance.Deactivate();
		}

		public string RatingFunction(CalibrationResult result)
		{
			var accuracy = result.AverageErrorDegree;

			if (accuracy < 0.5)
			{
				return "Calibration Quality: PERFECT";
			}
			if (accuracy < 0.7)
			{
				return "Calibration Quality: GOOD";
			}
			if (accuracy < 1)
			{
				return "Calibration Quality: MODERATE";
			}
			if (accuracy < 1.5)
			{
				return "Calibration Quality: POOR";
			}
			return "Calibration Quality: REDO";
		}
	}
}
