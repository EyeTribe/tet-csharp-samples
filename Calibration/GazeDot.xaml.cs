using System;
using TETCSharpClient;
using TETCSharpClient.Data;
using TETWinControls;


namespace Calibration
{
	public partial class GazeDot : IGazeUpdateListener
	{
		public GazeDot()
		{
			InitializeComponent();
			GazeManager.Instance.AddGazeListener(this);
		}

		public void OnScreenIndexChanged(int number)
		{
		}

		public void OnCalibrationStateChanged(bool val)
		{
		}

		public void OnGazeUpdate(GazeData gazeData)
		{
			if (Dispatcher.CheckAccess() == false)
			{
				Dispatcher.BeginInvoke(new Action(() => OnGazeUpdate(gazeData)));
				return;
			}

			// Start or stop tracking lost animation
			if ((gazeData.State & GazeData.STATE_TRACKING_GAZE) == 0 &&
			    (gazeData.State & GazeData.STATE_TRACKING_PRESENCE) == 0) return;
			//Tracking coordinates
			var d = Utility.Instance.ScaleDpi;
			var x = Utility.Instance.RecordingPosition.X;
			var y = Utility.Instance.RecordingPosition.Y;

			//var gX = gazeData.RawCoordinates.X;
			//var gY = gazeData.RawCoordinates.Y;

			var gX = gazeData.SmoothedCoordinates.X;
			var gY = gazeData.SmoothedCoordinates.Y;

			Left = d*x + d*gX - Width/2;
			Top = d*y + d*gY - Height/2;
		}
	}
}
