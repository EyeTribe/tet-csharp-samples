using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Media;
using TETCSharpClient.Data;
using TETCSharpClient;
using Image = System.Windows.Controls.Image;
using Size = System.Windows.Size;

namespace TETControls.TrackBox
{
    public partial class TrackBoxStatus : IGazeListener, ITrackerStateListener
	{
		#region Structs

		// trackbox struct
		public struct TrackBoxObject
		{
			public PointF Left;
			public PointF Right;
			public int LeftValidity;
			public int RightValidity;
		}

		#endregion

		#region Variables

		private const int MAX_BAD_SAMPLES = 3;
		private const int HISTORY_SIZE = 20;
		private const float ACCEPTABLE_QUALITY = 0.2f;

		private TrackBoxObject _latestGoodSample;
		private double _angleLatest;
		private double _normDistanceLatest;
		private TrackBoxObject _current;
		private int _badSuccessiveSamples;
		private float _trackingQuality;
		private Size _controlSize;
		private double _eyeScale;

		private readonly Queue<TrackBoxObject> _dataHistory = new Queue<TrackBoxObject>(HISTORY_SIZE);

		private TransformGroup _leftTransGroup;
		private RotateTransform _leftTransRot;
		private TranslateTransform _leftTransTranslation;
		private ScaleTransform _leftTransScale;

		private TransformGroup _rightTransGroup;
		private RotateTransform _rightTransRot;
		private TranslateTransform _rightTransTranslation;
		private ScaleTransform _rightTransScale;

		#endregion

		#region Get/Set

		public double GridOpacity
		{
			get { return GridImages.Opacity; }
			set { GridImages.Opacity = value; }
		}

		public Visibility GridBackgroundImageVisibility
		{
			get { return GridImageBackground.Visibility; }
			set { GridImageBackground.Visibility = value; }
		}

		#endregion

        #region Constructor

		public TrackBoxStatus()
		{
			InitializeComponent();

			Loaded += (sender, args) =>
			{
				GazeManager.Instance.AddGazeListener(this);
                GazeManager.Instance.AddTrackerStateListener(this);

				var size = new Size(Width, Height);
				if (double.IsNaN(size.Width))
					size = new Size(ActualWidth, ActualHeight);
				_controlSize = size;

				// normalize eyes and 'no-tracking image' - based on the designHeight of the user control
				_eyeScale = _controlSize.Height / 1000;
				status_no_tracking.Width = _controlSize.Height / 3;
				status_no_tracking.Height = _controlSize.Height / 3;

				// initialize the transformation variables for left and right eye
				_leftTransRot = new RotateTransform();
				_leftTransTranslation = new TranslateTransform();
				_leftTransScale = new ScaleTransform();
				_leftTransGroup = new TransformGroup();
				_leftTransGroup.Children.Add(_leftTransRot);
				_leftTransGroup.Children.Add(_leftTransScale);
				_leftTransGroup.Children.Add(_leftTransTranslation);

				_rightTransRot = new RotateTransform();
				_rightTransTranslation = new TranslateTransform();
				_rightTransScale = new ScaleTransform();
				_rightTransGroup = new TransformGroup();
				_rightTransGroup.Children.Add(_rightTransRot);
				_rightTransGroup.Children.Add(_rightTransScale);
				_rightTransGroup.Children.Add(_rightTransTranslation);

			    OnTrackerStateChanged(GazeManager.Instance.Trackerstate);

			};
		}

		#endregion

		#region Public

		public void OnGazeUpdate(GazeData gazeData)
		{
			if (Dispatcher.CheckAccess() == false)
			{
				Dispatcher.BeginInvoke(new Action(() => OnGazeUpdate(gazeData)));
				return;
			}

            if (gridContent.Visibility != Visibility.Visible)
                return;

			ProcessSample(gazeData);
			DoCurrentSampleQuality();
			DoSamplesQuality();
			DoEyes();
		}

        public void OnTrackerStateChanged(GazeManager.TrackerState trackerState)
        {
            if (labelDeviceConnected.Dispatcher.Thread != Thread.CurrentThread)
            {
                this.Dispatcher.BeginInvoke(new MethodInvoker(() => OnTrackerStateChanged(trackerState)));
                return;
            }

            gridContent.Visibility = Visibility.Hidden;

            switch (trackerState)
            {
                case GazeManager.TrackerState.TRACKER_CONNECTED:
                    labelDeviceConnected.Content = "";
                    gridContent.Visibility = Visibility.Visible;
                    break;
                case GazeManager.TrackerState.TRACKER_CONNECTED_NOUSB3:
                    labelDeviceConnected.Content = "Device connected to a USB2.0 port";
                    break;
                case GazeManager.TrackerState.TRACKER_CONNECTED_BADFW:
                    labelDeviceConnected.Content = "A firmware updated is required.";
                    break;
                case GazeManager.TrackerState.TRACKER_NOT_CONNECTED:
                    labelDeviceConnected.Content = "Device not connected.";
                    break;
                case GazeManager.TrackerState.TRACKER_CONNECTED_NOSTREAM:
                    labelDeviceConnected.Content = "No data coming out of the sensor.";
                    break;
            }
        }
        
        public void OnScreenStatesChanged(int screenIndex, int screenResolutionWidth, int screenResolutionHeight, float screenPhysicalWidth, float screenPhysicalHeight)
        {}

		#endregion

		#region Private Methods

		private void ProcessSample(GazeData gazeData)
		{
			var right = PointF.Empty;
			var left = PointF.Empty;

			if ((gazeData.State & GazeData.STATE_TRACKING_EYES) != 0 || (gazeData.State & GazeData.STATE_TRACKING_PRESENCE) != 0)
			{
				if (gazeData.LeftEye.PupilCenterCoordinates.X != 0 && gazeData.LeftEye.PupilCenterCoordinates.Y != 0)
				{
					left.X = (float)gazeData.LeftEye.PupilCenterCoordinates.X;
					left.Y = (float)gazeData.LeftEye.PupilCenterCoordinates.Y;
				}

				if (gazeData.RightEye.PupilCenterCoordinates.X != 0 && gazeData.RightEye.PupilCenterCoordinates.Y != 0)
				{
					right.X = (float)gazeData.RightEye.PupilCenterCoordinates.X;
					right.Y = (float)gazeData.RightEye.PupilCenterCoordinates.Y;
				}
			}

			// create a new trackbox sample and enqueue it
			_current = new TrackBoxObject
			{
				Left = left,
				Right = right,
				LeftValidity = left != PointF.Empty ? 0 : 1,
				RightValidity = right != PointF.Empty ? 0 : 1
			};
			EnqueueTrackBoxObject(_current);
		}

		private void DoCurrentSampleQuality()
		{
			_trackingQuality = GetStatus();
		}

		private void DoSamplesQuality()
		{
			var quality = GetQuality(_current);
			if (quality == 1 || quality == 2)
			{
				_badSuccessiveSamples = 0;
				_latestGoodSample = _current;

				// calculate eye angle if both eyes are visible
				if (quality == 2)
				{
					_normDistanceLatest = Math.Sqrt(
						Math.Pow(_current.Right.X - _current.Left.X, 2) +
						Math.Pow(_current.Right.Y - _current.Left.Y, 2));
					_angleLatest = ((180 / Math.PI *
						Math.Atan2((_current.Right.Y - _current.Left.Y) * _controlSize.Height, (_current.Right.X - _current.Left.X) * _controlSize.Width)));
				}
			}
			else
			{
				// we are forgiving with a couple of bad samples
				_badSuccessiveSamples++;
				if (_badSuccessiveSamples < MAX_BAD_SAMPLES)
				{
					_current = _latestGoodSample;
				}
			}
		}

		private void DoEyes()
		{
			DoBackground();
			if (DoVisibility())
			{
				UpdateEyes();
			}
		}

		private void DoBackground()
		{
			status_quality.Opacity = _trackingQuality;
		}

		private bool DoVisibility()
		{
			if (_trackingQuality <= ACCEPTABLE_QUALITY)
			{
				status_no_tracking.Visibility = Visibility.Visible;
				eye_left.Visibility = Visibility.Collapsed;
				eye_right.Visibility = Visibility.Collapsed;
				return false;
			}
			status_no_tracking.Visibility = Visibility.Collapsed;
			return true;
		}

		private void UpdateEyes()
		{
			// Update each eye with their respective transformation group
			UpdateEye(eye_left, _current.Left, _current.LeftValidity, _leftTransGroup);
			UpdateEye(eye_right, _current.Right, _current.RightValidity, _rightTransGroup);
		}

		private double DoEyeSizeDiff()
		{
			// Linear scale - normalised with the overall eye scale
			const double b = 0.15; // magic number (gestimated normalized distance between eyes)
			const double a = 1;
			return ((_normDistanceLatest - b) / b) * a;
		}

		private void UpdateEye(Image eye, PointF pos, int validity, TransformGroup transformation)
		{
			if (pos != PointF.Empty && validity <= 2)
			{
				eye.Visibility = Visibility.Visible;
				var scale = _eyeScale + DoEyeSizeDiff() * _eyeScale;
				var x = pos.X * _controlSize.Width;
				var y = pos.Y * _controlSize.Height;

				((RotateTransform)transformation.Children[0]).Angle = _angleLatest;
				((ScaleTransform)transformation.Children[1]).ScaleX = scale;
				((ScaleTransform)transformation.Children[1]).ScaleY = scale;
				((TranslateTransform)transformation.Children[2]).X = x - (eye.ActualWidth) / 2;
				((TranslateTransform)transformation.Children[2]).Y = y - (eye.ActualHeight) / 2;
				eye.RenderTransform = transformation;
			}
			else
			{
				eye.Visibility = Visibility.Collapsed;
			}
		}

		private void EnqueueTrackBoxObject(TrackBoxObject tbo)
		{
			lock (_dataHistory)
			{
				_dataHistory.Enqueue(tbo);

				while (_dataHistory.Count > HISTORY_SIZE)
				{
					_dataHistory.Dequeue();
				}
			}
		}

		private float GetStatus()
		{
			var totalQuality = 0;
			var count = 0;
			lock (_dataHistory)
			{
				foreach (var item in _dataHistory)
				{
					totalQuality += GetQuality(item);
					count++;
				}
			}
			return (totalQuality == 0 ? totalQuality : totalQuality / (2F * count));
		}

		private static int GetQuality(TrackBoxObject tbi)
		{
			// Get the quality of a single frame based on eye count
			var quality = 0;
			if (tbi.LeftValidity == 1 && tbi.RightValidity == 1)
			{   // both eyes are gone
				quality += 0;
			}
			else if (tbi.LeftValidity == 0 && tbi.RightValidity == 0)
			{   // best case
				quality += 2;
			}
			else
			{   // only one eye is showing
				quality += 1;
			}
			return quality;
		}

		#endregion

    }
}