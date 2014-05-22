/*
 * Copyright (c) 2013-present, The Eye Tribe. 
 * All rights reserved.
 *
 * This source code is licensed under the BSD-style license found in the LICENSE file in the root directory of this source tree. 
 *
 */
using System;
using System.Collections;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Media;
using System.Windows.Threading;
using TETCSharpClient;
using TETCSharpClient.Data;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using MessageBox = System.Windows.MessageBox;
using Size = System.Drawing.Size;

namespace TETControls.Calibration
{
    public class CalibrationRunner : ICalibrationProcessHandler, ITrackerStateListener
    {
        #region Variables

        private Screen screen = Screen.PrimaryScreen;
        private CalibrationWpf calibrationWin;
        private Size calibrationAreaSize;
        private static Visibility helpVisibility = Visibility.Collapsed;
        private static VerticalAlignment verticalAlignment = VerticalAlignment.Center;
        private static HorizontalAlignment horizontalAlignment = HorizontalAlignment.Center;

        private const double TARGET_PADDING = 0.1;
        private const int NUM_MAX_CALIBRATION_ATTEMPTS = 3;
        private const int NUM_MAX_RESAMPLE_POINTS = 4;

        private int pointCount = 9;
        private int pointLatencyTime = 500;
        private static int pointRecordingTime = 750;
        private int reSamplingCount;
        private bool calibrationFormReady = false;
        private bool calibrationServiceReady = false;

        private SolidColorBrush colorBackground = new SolidColorBrush(Colors.DarkGray);
        private SolidColorBrush colorPoint = new SolidColorBrush(Colors.White);

        private CalibrationResult calResult;
        private DispatcherTimer timerLatency;
        private DispatcherTimer timerRecording;
        private Queue<Point2D> calibrationPoints;
        private static readonly Random Random = new Random();

        private bool trackeStateOK = false;
        private bool isAborting = false;
        private bool isAbortedByUser = false;

        #endregion

        #region Get/Set

        public Screen Screen
        {
            get { return screen; }
            set { screen = value; }
        }

        public Size CalibrationAreaSize
        {
            get { return calibrationAreaSize; }
            set { calibrationAreaSize = value; }
        }

        public int PointCount
        {
            get { return pointCount; }
            set { pointCount = value; }
        }

        public int PointRecordingTime
        {
            get { return pointRecordingTime; }
            set { pointRecordingTime = value; }
        }

        public int PointLatencyTime
        {
            get { return pointLatencyTime; }
            set { pointLatencyTime = value; }
        }

        public Point2D CurrentPoint { get; set; }

        public VerticalAlignment VerticalAlignment
        {
            get { return verticalAlignment; }
            set { verticalAlignment = value; }
        }

        public HorizontalAlignment HorizontalAlignment
        {
            get { return horizontalAlignment; }
            set { horizontalAlignment = value; }
        }

        public SolidColorBrush BackgroundColor
        {
            get { return colorBackground; }
            set { colorBackground = value; }
        }

        public SolidColorBrush PointColor
        {
            get { return colorPoint; }
            set { colorPoint = value; }
        }

        public Visibility HelpVisibility
        {
            get { return helpVisibility; }
            set { helpVisibility = value; }
        }

        public bool IsAbortedByUser
        {
            get { return isAbortedByUser; }
        }

        #endregion

        #region Constructor

        public CalibrationRunner() : this(Screen.PrimaryScreen, Screen.PrimaryScreen.Bounds.Size, 9) { }

        public CalibrationRunner(Screen screen, Size calibrationAreaSize, int pointCount)
        {
            this.screen = screen;
            this.calibrationAreaSize = calibrationAreaSize;
            this.pointCount = pointCount;
            GazeManager.Instance.AddTrackerStateListener(this);

            // Test whether the tracker state allows for a calibration
            OnTrackerStateChanged(GazeManager.Instance.Trackerstate);
        }

        #endregion

        #region Public

        public bool Start()
        {
            // If Tracker is anything but connected - return false
            if (trackeStateOK != true) return false;

            isAbortedByUser = false;

            try
            {
                // Blocks until calibration has completed
                DoCalibrate(out isAbortedByUser);
            }
            catch (Exception ex)
            { }

            if (isAbortedByUser)
            {
                MessageBox.Show("The calibration was aborted.");
                return false;
            }

            if (calResult == null)
                return false;

            if (calResult.Result)
                return true;

            MessageBox.Show("The calibration process failed. Please try again.");
            return false;
        }

        #region Interface Implementation

        public void OnScreenStatesChanged(int screenIndex, int screenResolutionWidth, int screenResolutionHeight, float screenPhysicalWidth, float screenPhysicalHeight)
        { }

        public void OnTrackerStateChanged(GazeManager.TrackerState trackerState)
        {
            trackeStateOK = false;
            string errorMessage = "";
            switch (trackerState)
            {
                case GazeManager.TrackerState.TRACKER_CONNECTED:
                    trackeStateOK = true;
                    break;
                case GazeManager.TrackerState.TRACKER_CONNECTED_NOUSB3:
                    errorMessage = "Device connected to a USB2.0 port";
                    break;
                case GazeManager.TrackerState.TRACKER_CONNECTED_BADFW:
                    errorMessage = "A firmware updated is required.";
                    break;
                case GazeManager.TrackerState.TRACKER_NOT_CONNECTED:
                   errorMessage = "Device not connected.";
                   break;
                case GazeManager.TrackerState.TRACKER_CONNECTED_NOSTREAM:
                   errorMessage = "No data coming out of the sensor.";
                   break;
            }

            if (trackeStateOK || isAborting) return;
            AbortCalibration();
            isAborting = true;
            MessageBox.Show(errorMessage);
        }

        public void OnCalibrationStarted()
        {
            // tracker engine is ready to calibrate - check if we can start to calibrate
            calibrationServiceReady = true;

            if (calibrationFormReady && CurrentPoint != null)
                MoveToPoint(CurrentPoint);
        }

        public void OnCalibrationProgress(double progress)
        {
            // move to new calibration point or end calibration if we have no more targets to show
            CurrentPoint = PickNextPoint();

            if (CurrentPoint != null)
                MoveToPoint(CurrentPoint);
        }

        public void OnCalibrationProcessing()
        {
            // tracker engine is processing results
        }

        public void OnCalibrationResult(CalibrationResult res)
        {
            if (res == null || res.Calibpoints == null)
            {
                StopAndClose();
                return;
            }

            calResult = res;

            // Success, check results for poor points (resample)
            foreach (CalibrationPoint calPoint in calResult.Calibpoints)
            {
                if (calPoint == null || calPoint.Coordinates == null)
                    continue;

                if (calPoint.State == CalibrationPoint.STATE_RESAMPLE || calPoint.State == CalibrationPoint.STATE_NO_DATA)
                    calibrationPoints.Enqueue(new Point2D(calPoint.Coordinates.X, calPoint.Coordinates.Y));
            }

            // Time to stop?
            if (reSamplingCount++ >= NUM_MAX_CALIBRATION_ATTEMPTS || calibrationPoints.Count >= NUM_MAX_RESAMPLE_POINTS)
            {
                AbortCalibration();
                return;
            }

            // If there is a point enqued for resampling we do that, otherwise we are done
            CurrentPoint = PickNextPoint();

            if(CurrentPoint != null)
               MoveToPoint(CurrentPoint);
            else
               StopAndClose();
        }

        #endregion

        #endregion

        #region Private

        private void DoCalibrate(out bool userAbort)
        {
            reSamplingCount = 0;
            userAbort = false;

            try
            {
                // Set up two timers, one for recording delay and another for recording duration
                // 1. When point is shown we start timerLatency, on tick we signal tracker to start sampling (for duration of timerRecording)
                // 2. A point is sampled for the duration of the timerRecording
                CreateTimerLatency();
                CreateTimerRecording();

                // Create a fullscreen Calibration window
                CreateCalibrationWin();

                // Set the properties of the CalibrationWindow
                calibrationWin.BackgroundColor = BackgroundColor;
                calibrationWin.PointColor = PointColor;
                calibrationWin.HelpVisbility = HelpVisibility;
                calibrationWin.PointDisplayTimeMs = PointRecordingTime;

                // Signal tracker server that we're about to start a calibration
                GazeManager.Instance.CalibrationStart((short)PointCount, this);

                // Create points, get first in line, draw it and show window
                calibrationPoints = CreatePointList();
                CurrentPoint = PickNextPoint();

                calibrationWin.DrawCalibrationPoint(CurrentPoint);
                calibrationWin.ShowDialog();

                userAbort = calibrationWin.IsAborted;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Unknown error occured in the calibration,  message: " + ex.Message);
            }
        }

        private void CreateCalibrationWin()
        {
            if (calibrationWin == null)
            {
                calibrationWin = new CalibrationWpf(screen);
                calibrationWin.OnCalibrationAborted += AbortCalibration;
                calibrationWin.OnFadeInDone += delegate
                {
                    // window fade-in completed, move to first point
                    calibrationFormReady = true;

                    if (calibrationServiceReady)
                        MoveToPoint(CurrentPoint);
                };
            }
        }

        private void CreateTimerRecording()
        {
            if (timerRecording == null)
            {
                timerRecording = new DispatcherTimer { Interval = new TimeSpan(0, 0, 0, 0, pointRecordingTime) };
                timerRecording.Stop();
                timerRecording.Tick += delegate
                {
                    timerRecording.Stop();
                    GazeManager.Instance.CalibrationPointEnd();
                    // tracker server callbacks to interface methods, e.g. OnCalibrationProgressUpdate
                    // which proceeds to MoveToPoint until OnCalibrationResults (the end) is called.
                };
            }
        }

        private void CreateTimerLatency()
        {
            if (timerLatency == null)
            {
                timerLatency = new DispatcherTimer { Interval = new TimeSpan(0, 0, 0, 0, pointLatencyTime) };
                timerLatency.Stop();
                timerLatency.Tick += delegate
                {
                    timerLatency.Stop();

                    if (CurrentPoint != null)
                    {
                        // Signal tracker server that a point is starting, do the shrink animation and start timerRecording 
                        GazeManager.Instance.CalibrationPointStart((int)CurrentPoint.X, (int)CurrentPoint.Y);
                        calibrationWin.AnimateCalibrationPoint();
                        timerRecording.Start();                        
                    }
                };
            }
        }

        private void MoveToPoint(Point2D point)
        {
            if (point == null)
                return;

            // draw the new calibration target and start latency timer
            calibrationWin.DrawCalibrationPoint(point);
            timerLatency.Start(); // Will issue PointStart and start timerRecording on tick
        }

        private void AbortCalibration()
        {
            if (isAborting) return; // Only one call is needed
            GazeManager.Instance.CalibrationAbort();
            StopAndClose();
        }

        private void StopAndClose()
        {
            if (timerLatency != null)
                timerLatency.Stop();

            if (timerRecording != null)
                timerRecording.Stop();

            if (calibrationWin != null)
                calibrationWin.CloseWindow();
        }

        #region Targets Logic

        private Point2D PickNextPoint()
        {
            if (calibrationPoints == null)
                calibrationPoints = CreatePointList();

            if (calibrationPoints.Count != 0)
                return calibrationPoints.Dequeue();

            return null;
        }

        /// <summary>
        /// Create a set of normalized calibration points
        /// </summary>
        private Queue<Point2D> CreatePointList()
        {
            if (screen == null)
                screen = Screen.PrimaryScreen; // default to primary

            Size size = Screen.Bounds.Size;
            double scaleW = 1.0;
            double scaleH = 1.0;
            double offsetX = 0.0;
            double offsetY = 0.0;

            // if we are using a subset of the screen as calibration area
            if (!CalibrationAreaSize.IsEmpty)
            {
                scaleW = CalibrationAreaSize.Width / (double)size.Width;
                scaleH = CalibrationAreaSize.Height / (double)size.Height;

                offsetX = GetHorizontalAlignmentOffset();
                offsetY = GetVerticalAlignmentOffset();
            }

            // add some padding 
            double paddingHeight = TARGET_PADDING;
            double paddingWidth = (size.Height * TARGET_PADDING) / (double)size.Width; // use the same distance for the width padding

            double columns = Math.Sqrt(PointCount);
            double rows = columns;

            if (PointCount == 12)
            {
                columns = Math.Round(columns + 1, 0);
                rows = Math.Round(rows, 0);
            }

            ArrayList points = new ArrayList();
            for (int dirX = 0; dirX < columns; dirX++)
            {
                for (int dirY = 0; dirY < rows; dirY++)
                {
                    double x = Lerp(paddingWidth, 1 - paddingWidth, dirX / (columns - 1));
                    double y = Lerp(paddingHeight, 1 - paddingHeight, dirY / (rows - 1));
                    points.Add(new Point2D(offsetX + x * scaleW, offsetY + y * scaleH));
                }
            }

            Queue<Point2D> calibrationPoints = new Queue<Point2D>();
            int[] order = new int[PointCount];

            for (var c = 0; c < PointCount; c++)
                order[c] = c;

            Shuffle(order);

            foreach (int number in order)
                calibrationPoints.Enqueue((Point2D)points[number]);

            // De-normalize points to fit the current screen
            foreach (var point in calibrationPoints)
            {
                point.X *= Screen.Bounds.Width;
                point.Y *= Screen.Bounds.Height;
            }
            return calibrationPoints;
        }

        public static double Lerp(double value1, double value2, double amount)
        {
            return value1 + (value2 - value1) * amount;
        }

        private static void Shuffle<T>(IList<T> array)
        {
            if (array == null)
                return;

            var random = Random;

            for (var i = array.Count; i > 1; i--)
            {
                var j = random.Next(i);
                var tmp = array[j];
                array[j] = array[i - 1];
                array[i - 1] = tmp;
            }
        }

        private double GetVerticalAlignmentOffset()
        {
            double offsetY = 0.0;

            switch (VerticalAlignment)
            {
                case VerticalAlignment.Center:
                case VerticalAlignment.Stretch: // center
                    offsetY = ((Screen.Bounds.Size.Height - CalibrationAreaSize.Height) / 2d) / (double)Screen.Bounds.Size.Height;
                    break;
                case VerticalAlignment.Bottom:
                    offsetY = (Screen.Bounds.Size.Height - CalibrationAreaSize.Height) / (double)Screen.Bounds.Size.Height;
                    break;
                case VerticalAlignment.Top:
                    offsetY = 0.0;
                    break;
            }
            return offsetY;
        }

        private double GetHorizontalAlignmentOffset()
        {
            double offsetX = 0.0;

            switch (HorizontalAlignment)
            {
                case HorizontalAlignment.Center:
                case HorizontalAlignment.Stretch: // center
                    offsetX = ((Screen.Bounds.Size.Width - CalibrationAreaSize.Width) / 2d) / (double)Screen.Bounds.Size.Width;
                    break;
                case HorizontalAlignment.Right:
                    offsetX = (Screen.Bounds.Size.Width - CalibrationAreaSize.Width) / (double)Screen.Bounds.Size.Width;
                    break;
                case HorizontalAlignment.Left:
                    offsetX = 0.0;
                    break;
            }
            return offsetX;
        }

        #endregion

        #endregion
    }
}
