/*
 * Copyright (c) 2013-present, The Eye Tribe. 
 * All rights reserved.
 *
 * This source code is licensed under the BSD-style license found in the LICENSE file in the root directory of this source tree. 
 *
 */
using System;
using System.Collections;
using System.Drawing;
using System.Threading;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Media;
using System.Windows.Threading;
using TETCSharpClient.Data;
using TETCSharpClient;
using Size = System.Windows.Size;

namespace TETControls.TrackBox
{
    public partial class TrackBoxStatus : IGazeListener, ITrackerStateListener, IConnectionStateListener
    {
        #region Structs

        // trackbox struct
        private struct TrackBoxObject
        {
            public PointF Left;
            public PointF Right;
            public EyeCount LeftValidity;
            public EyeCount RightValidity;
        }

        #endregion

        #region Enums

        private enum EyeCount { Zero = 0, One, Two }

        #endregion

        #region Variables

        private const double RadToDeg = 180 / Math.PI;
        private const int UI_UPDATE_FREQUENCY = 60; // Hz
        private const int MAX_BAD_SAMPLES = 3;
        private const float ACCEPTABLE_QUALITY = 0.2f;
        private int queueSize = 20;

        private Queue trackBoxHistory; // initialized as syncronized in constructor

        private TrackBoxObject latestGoodSample;
        private TrackBoxObject currentTrackboxObj;

        private double latestAngle;
        private double latestNormalizedDistance;
        private double eyeScale;
        private float currentTrackingQuality;
        private int badSuccessiveSamples;
        private Size controlSize;

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

            trackBoxHistory = Queue.Synchronized(new Queue());

            DispatcherTimer uiUpdateTimer = new DispatcherTimer();
            uiUpdateTimer.Tick += UIUpdateTimerTick;
            uiUpdateTimer.Interval = new TimeSpan(0, 0, 0, 0, 1000 / UI_UPDATE_FREQUENCY);
            uiUpdateTimer.Start();

            EyeLeft.Source.Freeze();
            EyeRight.Source.Freeze();
            StatusNoTracking.Source.Freeze();
            StatusQuality.Source.Freeze();
            StatusQualityInverted.Source.Freeze();

            this.Loaded += TrackBoxStatusLoaded;
        }

        #endregion

        #region Public Methods

        public void OnConnectionStateChanged(bool isActivated)
        {
            // The connection state listener detects when the connection to the EyeTribe server changes
            if (LabelDeviceConnected.Dispatcher.Thread != Thread.CurrentThread)
            {
                this.Dispatcher.BeginInvoke(new MethodInvoker(() => OnConnectionStateChanged(isActivated)));
                return;
            }

            if (!isActivated)
            {
                LabelDeviceConnected.Content = "Connection to EyeTribe Server lost";
                GridContent.Visibility = Visibility.Hidden;
            }
            else
            {
                LabelDeviceConnected.Content = "";
                GridContent.Visibility = Visibility.Visible;
            }
        }

        public void OnScreenStatesChanged(int screenIndex, int screenResolutionWidth, int screenResolutionHeight, float screenPhysicalWidth, float screenPhysicalHeight)
        { }

        public void OnGazeUpdate(GazeData gazeData)
        {
            ProcessSample(gazeData);
            AnalyzeSamples();
        }

        public void OnTrackerStateChanged(GazeManager.TrackerState trackerState)
        {
            if (LabelDeviceConnected.Dispatcher.Thread != Thread.CurrentThread)
            {
                this.Dispatcher.BeginInvoke(new MethodInvoker(() => OnTrackerStateChanged(trackerState)));
                return;
            }

            switch (trackerState)
            {
                case GazeManager.TrackerState.TRACKER_CONNECTED:
                    LabelDeviceConnected.Content = "";
                    GridContent.Visibility = Visibility.Visible;
                    break;

                case GazeManager.TrackerState.TRACKER_CONNECTED_NOUSB3:
                    LabelDeviceConnected.Content = "Device connected to a USB2.0 port";
                    GridContent.Visibility = Visibility.Hidden;
                    break;

                case GazeManager.TrackerState.TRACKER_CONNECTED_BADFW:
                    LabelDeviceConnected.Content = "A firmware updated is required.";
                    GridContent.Visibility = Visibility.Hidden;
                    break;

                case GazeManager.TrackerState.TRACKER_NOT_CONNECTED:
                    LabelDeviceConnected.Content = "Device not connected.";
                    GridContent.Visibility = Visibility.Hidden;
                    break;

                case GazeManager.TrackerState.TRACKER_CONNECTED_NOSTREAM:
                    LabelDeviceConnected.Content = "No data coming out of the sensor.";
                    GridContent.Visibility = Visibility.Hidden;
                    break;
            }
        }

        /// <summary>
        /// // Call this when the server has been manually restarted to refresh the listeners
        /// </summary>
        public void RefreshAPIConnection()
        {
            // Gaze listener
            if (GazeManager.Instance.HasGazeListener(this))
                GazeManager.Instance.RemoveGazeListener(this);

            GazeManager.Instance.AddGazeListener(this);

            // Tracker state
            if (GazeManager.Instance.HasTrackerStateListener(this))
                GazeManager.Instance.RemoveTrackerStateListener(this);

            GazeManager.Instance.AddTrackerStateListener(this);

            // Connection
            if (GazeManager.Instance.HasConnectionStateListener(this))
                GazeManager.Instance.RemoveConnectionStateListener(this);

            GazeManager.Instance.AddConnectionStateListener(this);
        }

        #endregion

        #region Private UI Methods

        private void TrackBoxStatusLoaded(object sender, RoutedEventArgs e)
        {
            if (GazeManager.Instance.HasGazeListener(this))
                return;

            GazeManager.Instance.AddGazeListener(this);
            GazeManager.Instance.AddTrackerStateListener(this);
            GazeManager.Instance.AddConnectionStateListener(this);

            var size = new Size(Width, Height);
            if (double.IsNaN(size.Width))
                size = new Size(ActualWidth, ActualHeight);
            controlSize = size;

            // Normalize eyes and 'no-tracking image' - based on the height of the user control
            eyeScale = controlSize.Height / 1000;
            StatusNoTracking.Width = controlSize.Height / 3;
            StatusNoTracking.Height = controlSize.Height / 3;

            // What is the current state of the listenes
            OnConnectionStateChanged(GazeManager.Instance.IsActivated);
            OnTrackerStateChanged(GazeManager.Instance.Trackerstate);

            // Set queue size based on framerate
            switch (GazeManager.Instance.Framerate)
            {
                case GazeManager.FrameRate.FPS_30:
                    queueSize = 20;
                    break;

                case GazeManager.FrameRate.FPS_60:
                    queueSize = 40;
                    break;
            }
        }

        private void UIUpdateTimerTick(object sender, EventArgs e)
        {
            if (GridContent.Visibility != Visibility.Visible)
                return;

            // Do background opacity
            StatusQuality.Opacity = currentTrackingQuality;
            StatusQualityInverted.Opacity = 1f - currentTrackingQuality;

            // Determine what should visible and update the eye positions if needed
            if (currentTrackingQuality <= ACCEPTABLE_QUALITY)
            {
                StatusNoTracking.Visibility = Visibility.Visible;
                EyeLeft.Visibility = Visibility.Collapsed;
                EyeRight.Visibility = Visibility.Collapsed;
                return;
            }

            StatusNoTracking.Visibility = Visibility.Collapsed;
            UpdateEye(EyeLeft, currentTrackboxObj.Left, currentTrackboxObj.LeftValidity, TransformEyeLeft);
            UpdateEye(EyeRight, currentTrackboxObj.Right, currentTrackboxObj.RightValidity, TransformEyeRight);
        }

        private void UpdateEye(FrameworkElement eye, PointF pos, EyeCount validity, TransformGroup transformation)
        {
            if (pos != PointF.Empty && validity <= EyeCount.Two)
            {
                eye.Visibility = Visibility.Visible;
                var scale = eyeScale + DoEyeSizeDiff() * eyeScale;
                var x = pos.X * controlSize.Width;
                var y = pos.Y * controlSize.Height;

                ((RotateTransform)transformation.Children[0]).Angle = latestAngle;
                ((ScaleTransform)transformation.Children[1]).ScaleX = scale;
                ((ScaleTransform)transformation.Children[1]).ScaleY = scale;
                ((TranslateTransform)transformation.Children[2]).X = x - (eye.ActualWidth) / 2;
                ((TranslateTransform)transformation.Children[2]).Y = y - (eye.ActualHeight) / 2;
            }
            else
            {
                eye.Visibility = Visibility.Collapsed;
            }
        }

        #endregion

        #region Private Methods

        private void ProcessSample(GazeData gazeData)
        {
            var right = PointF.Empty;
            var left = PointF.Empty;

            if ((gazeData.State & GazeData.STATE_TRACKING_EYES) != 0 ||
                (gazeData.State & GazeData.STATE_TRACKING_PRESENCE) != 0)
            {
                if (!Equals(gazeData.LeftEye.PupilCenterCoordinates.X, 0.0) &&
                    !Equals(gazeData.LeftEye.PupilCenterCoordinates.Y, 0.0))
                {
                    left.X = (float)gazeData.LeftEye.PupilCenterCoordinates.X;
                    left.Y = (float)gazeData.LeftEye.PupilCenterCoordinates.Y;
                }
                if (!Equals(gazeData.RightEye.PupilCenterCoordinates.X, 0.0) &&
                    !Equals(gazeData.RightEye.PupilCenterCoordinates.Y, 0.0))
                {
                    right.X = (float)gazeData.RightEye.PupilCenterCoordinates.X;
                    right.Y = (float)gazeData.RightEye.PupilCenterCoordinates.Y;
                }
            }

            // create a new trackbox sample and enqueue it
            currentTrackboxObj = new TrackBoxObject
            {
                Left = left,
                Right = right,
                LeftValidity = left != PointF.Empty ? EyeCount.One : EyeCount.Zero,
                RightValidity = right != PointF.Empty ? EyeCount.One : EyeCount.Zero
            };

            while (trackBoxHistory.Count > queueSize)
                trackBoxHistory.Dequeue();

            trackBoxHistory.Enqueue(currentTrackboxObj);
        }

        private void AnalyzeSamples()
        {
            currentTrackingQuality = GetStatus();
            EyeCount quality = VisibleEyesCount(currentTrackboxObj);

            if (quality == EyeCount.One || quality == EyeCount.Two)
            {
                badSuccessiveSamples = 0;
                latestGoodSample = currentTrackboxObj;

                // calculate eye angle if both eyes are visible
                if (quality == EyeCount.Two)
                {
                    float dx = currentTrackboxObj.Right.X - currentTrackboxObj.Left.X;
                    float dy = currentTrackboxObj.Right.Y - currentTrackboxObj.Left.Y;
                    latestNormalizedDistance = Math.Sqrt(dx * dx + dy * dy);
                    latestAngle = RadToDeg * Math.Atan2(dy * controlSize.Height, dx * controlSize.Width);
                }
            }
            else
            {
                // we are forgiving with a couple of bad samples
                badSuccessiveSamples++;

                if (badSuccessiveSamples < MAX_BAD_SAMPLES)
                    currentTrackboxObj = latestGoodSample;
            }
        }

        private double DoEyeSizeDiff()
        {
            // Linear scale - normalised with the overall eye scale
            const double b = 0.15; // magic number (gestimated normalized distance between eyes)
            const double a = 1;
            return ((latestNormalizedDistance - b) / b) * a;
        }

        private float GetStatus()
        {
            // Get the overall tracking quality from our TrackBoxHistory
            var totalQuality = 0;
            var count = 0;

            lock (trackBoxHistory.SyncRoot)
            {
                foreach (var item in trackBoxHistory)
                {
                    totalQuality += (int)VisibleEyesCount((TrackBoxObject)item);
                    count++;
                }
            }

            return (totalQuality == (int)EyeCount.Zero ? totalQuality : totalQuality / ((float)EyeCount.Two * count));
        }

        private static EyeCount VisibleEyesCount(TrackBoxObject tbi)
        {
            // Get the quality of a single frame based on eye count
            if (tbi.LeftValidity == EyeCount.Zero && tbi.RightValidity == EyeCount.Zero)
            {
                // both eyes are gone
                return EyeCount.Zero;
            }
            if (tbi.LeftValidity == EyeCount.One && tbi.RightValidity == EyeCount.One)
            {
                // two eyes are found
                return EyeCount.Two;
            }
            // only left or right eye is showing
            return EyeCount.One;
        }

        #endregion
    }
}