using System;
using System.Windows;
using Calibration;
using TETCSharpClient.Data;
using TETWinControls;
using TETWinControls.Calibration;
using Screen = System.Windows.Forms.Screen;
using System.Windows.Interop;
using TETCSharpClient;


namespace TETBasicSample
{
	public partial class MainWindow
	{
		private GazeDot _gazeDot;

		public MainWindow()
		{
			// Create a client for the eye tracker
			GazeManager.Instance.Activate(1, GazeManager.ClientMode.Push);
			InitializeComponent();

			if (!GazeManager.Instance.IsConnected)
			{
				MessageBox.Show("Tracker is not started");
				Close();
			}
			else if (GazeManager.Instance.IsCalibrated)
			{
				ShowGaze();
			}
		}

		private void CalibrateClicked(object sender, RoutedEventArgs e)
		{
			btnCalibrate.Content = "Re-Calibrate";
			Calibrate();
		}

		private void Calibrate()
		{
			if (_gazeDot != null)
			{
				_gazeDot.Hide();
			}

			//Run the calibration on 'this' monitor
			Utility.Instance.RecordingScreen = Screen.FromHandle(new WindowInteropHelper(this).Handle);

			//Initialize and start calibration
			CalibrationRunner calRunner = new CalibrationRunner();
			var isCalibrated = calRunner.Start();

			if (!isCalibrated)
			{
				return;
			}

			MagicRatingFunction(calRunner.GetLatestCalibrationResult());

			ShowGaze();
		}

		private void ShowGaze()
		{
			if (_gazeDot == null)
			{
				_gazeDot = new GazeDot();
			}
			_gazeDot.Show();
		}

		private void WindowClosed(object sender, EventArgs e)
		{
			if (_gazeDot != null)
			{
				_gazeDot.Close();
			}
			GazeManager.Instance.Deactivate();
		}

		private void MagicRatingFunction(CalibrationResult calibrationResult)
		{
			if (calibrationResult == null)
			{
				return;
			}
			var str = "";
			var accuracy = calibrationResult.AverageErrorDegree;
			if (accuracy < 0.5)
			{
				str = "PERFECT";
			}
			else if (accuracy < 0.7)
			{
				str = "GOOD";
			}
			else if (accuracy < 1)
			{
				str = "MODERATE";
			}
			else if (accuracy < 1.5)
			{
				str = "POOR";
			}
			else
			{
				str = "REDO";
			}
			RatingText.Text = "CALIBRATION RESULT: " + str;
		}
	}
}
