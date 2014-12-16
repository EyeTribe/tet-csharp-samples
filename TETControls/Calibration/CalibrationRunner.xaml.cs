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
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using TETCSharpClient;
using TETCSharpClient.Data;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Size = System.Drawing.Size;
using WndSize = System.Windows.Size;

namespace TETControls.Calibration
{
    public partial class CalibrationRunner : ICalibrationProcessHandler, ITrackerStateListener
    {
        #region Variables

        private const string MESSAGE_FOLLOW = "Follow the circle..";
        private const string MESSAGE_COMPUTING = "Processing calibration, please wait.";

        private const double FADE_IN_TIME = 2.5; //sec
        private const double FADE_OUT_TIME = 0.5; //sec
        private readonly DoubleAnimation animateOut;
        private CalibrationPointWpf calPointWpf;

        private Screen screen = Screen.PrimaryScreen;
        private Size calibrationAreaSize;
        private static Visibility helpVisibility = Visibility.Collapsed;
        private static VerticalAlignment verticalAlignment = VerticalAlignment.Center;
        private static HorizontalAlignment horizontalAlignment = HorizontalAlignment.Center;

        private const double TARGET_PADDING = 0.1;
        private const int NUM_MAX_CALIBRATION_ATTEMPTS = 3;
        private const int NUM_MAX_RESAMPLE_POINTS = 3;

        private int count = 9;
        private int latencyMs = 500;
        private static int sampleTimeMs = 750;
        private int transitionTimeMs = 750;
        private int reSamplingCount;
        private bool calibrationFormReady = false;

        private DispatcherTimer timerLatency;
        private DispatcherTimer timerRecording;
        private Queue<Point2D> points;
        private Point2D currentPoint = null;
        private static readonly Random Random = new Random();

        private bool trackeStateOK = false;
        private bool isAborting = false;

        #endregion

        #region Events

        public event EventHandler<CalibrationRunnerEventArgs> OnResult;

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

        public SolidColorBrush BackgroundColor
        {
            get { return CalibrationCanvas.Background as SolidColorBrush; }
            set { CalibrationCanvas.Background = value; }
        }

        public VerticalAlignment Vertical_Alignment
        {
            get { return verticalAlignment; }
            set { verticalAlignment = value; }
        }

        public HorizontalAlignment Horizontal_Alignment
        {
            get { return horizontalAlignment; }
            set { horizontalAlignment = value; }
        }

        public Visibility HelpVisibility
        {
            get { return CalibrationHelp.Visibility; }
            set { CalibrationHelp.Visibility = value; }
        }

        public int PointCount
        {
            get { return count; }
            set { count = value; }
        }

        public int PointLatencyTime
        {
            get { return latencyMs; }
            set { latencyMs = value; }
        }

        public int PointTransitionTime
        {
            get { return transitionTimeMs; }
            set { transitionTimeMs = value; }
        }
        public int PointRecordingTime
        {
            get { return calPointWpf.AnimationTimeMilliseconds; }
            set { calPointWpf.AnimationTimeMilliseconds = value; }
        }

        public SolidColorBrush PointColor
        {
            get { return calPointWpf.PointColor; }
            set { calPointWpf.PointColor = value; }
        }

        #endregion

        #region Constructor

        public CalibrationRunner() : this(Screen.PrimaryScreen, Screen.PrimaryScreen.Bounds.Size, 9) { }

        public CalibrationRunner(Screen screen, Size calibrationAreaSize, int pointCount)
        {
            this.screen = screen;
            this.calibrationAreaSize = calibrationAreaSize;
            this.count = pointCount;

            InitializeComponent();

            GazeManager.Instance.AddTrackerStateListener(this);

            // Test whether the tracker state allows for a calibration
            OnTrackerStateChanged(GazeManager.Instance.Trackerstate);

            // Create the calibration target
            double width_dpi = Math.Round(Utility.Instance.ScaleDpi * screen.Bounds.Width, 0);
            double height_dpi = Math.Round(Utility.Instance.ScaleDpi * screen.Bounds.Height, 0);

            calPointWpf = new CalibrationPointWpf(new WndSize(width_dpi, height_dpi));
            CalibrationCanvas.Children.Add(calPointWpf);
            Canvas.SetLeft(calPointWpf, width_dpi / 2);
            Canvas.SetTop(calPointWpf, height_dpi / 2);

            // Set the properties of the CalibrationWindow
            BackgroundColor = new SolidColorBrush(Colors.DarkGray);
            PointColor = new SolidColorBrush(Colors.White);
            PointRecordingTime = sampleTimeMs;

            Opacity = 0;

            // Create the animation-out object and close form when completed
            animateOut = new DoubleAnimation(0, TimeSpan.FromSeconds(FADE_OUT_TIME))
            {
                From = 1.0,
                To = 0.0,
                AutoReverse = false
            };

            animateOut.Completed += delegate { Close(); };

            this.Cursor = System.Windows.Input.Cursors.None;
        }

        #endregion

        #region Public methods

        public void Start()
        {
            if (trackeStateOK != true)
            {
                RaiseResult(CalibrationRunnerResult.Error, "Device is not in a valid state, cannot calibrate.");
                return;
            }

            try
            {
                Show();
            }
            catch (Exception ex)
            {
                RaiseResult(CalibrationRunnerResult.Error, "Unable to show the calibration window. Please try again.");
                return;
            }

            // Start the calibration process
            DoStart();
        }

        public void ShowMessage(string message)
        {
            if (this.Dispatcher.Thread != Thread.CurrentThread)
            {
                Dispatcher.BeginInvoke(new MethodInvoker(() => ShowMessage(message)));
                return;
            }

            labelMessage.Content = message;
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

            if (trackeStateOK || isAborting)
                return;

            if (trackeStateOK == false)
            {
                // Lost device, abort calib now (raise event)
                AbortCalibration(errorMessage);
            }
        }

        public void OnCalibrationStarted()
        {
            // tracker engine is ready to calibrate - check if we can start to calibrate
            if (calibrationFormReady && currentPoint != null)
                DrawCalibrationPoint(currentPoint);
        }

        public void OnCalibrationProgress(double progress)
        {
            if (this.Dispatcher.Thread != Thread.CurrentThread)
            {
                this.Dispatcher.BeginInvoke(new MethodInvoker(() => OnCalibrationProgress(progress)));
                return;
            }

            if (progress.Equals(1.0)) // done
                return;

            // transition to next point
            Point2D curPos = currentPoint;
            Point2D nextPos = PickNextPoint();

            if (nextPos == null) // no more points?
                return;

            // Store next point as current (global)
            currentPoint = nextPos;

            // Animate transition to next position
            double curX = Utility.Instance.ScaleDpi * curPos.X;
            double curY = Utility.Instance.ScaleDpi * curPos.Y;
            double nxtX = Utility.Instance.ScaleDpi * nextPos.X;
            double nxtY = Utility.Instance.ScaleDpi * nextPos.Y;

            DoubleAnimation cX = CreateTransitionAnimation(curX, nxtX, transitionTimeMs);
            DoubleAnimation cY = CreateTransitionAnimation(curY, nxtY, transitionTimeMs);
            cX.Completed += delegate { DrawCalibrationPoint(currentPoint); };

            calPointWpf.BeginAnimation(Canvas.LeftProperty, cX);
            calPointWpf.BeginAnimation(Canvas.TopProperty, cY);
        }

        public void OnCalibrationProcessing()
        {
            ShowMessage(MESSAGE_COMPUTING);
        }

        public void OnCalibrationResult(CalibrationResult res)
        {
            // Invoke on UI thread
            if (this.Dispatcher.Thread != Thread.CurrentThread)
            {
                Dispatcher.BeginInvoke(new MethodInvoker(() => OnCalibrationResult(res)));
                return;
            }

            // No result?
            if (res == null || res.Calibpoints == null)
            {
                RaiseResult(CalibrationRunnerResult.Error, "Calibration result is empty.");
                StopAndClose();
                return;
            }

            Console.Out.WriteLine("CalibrationResult, avg: " + res.AverageErrorDegree + " left: " + res.AverageErrorDegreeLeft + " right: " + res.AverageErrorDegreeRight);

            // Success, check results for bad points
            foreach (CalibrationPoint cp in res.Calibpoints)
            {
                if (cp == null || cp.Coordinates == null)
                    continue;

                // Tracker tells us to resample this point, enque it
                if (cp.State == CalibrationPoint.STATE_RESAMPLE || cp.State == CalibrationPoint.STATE_NO_DATA)
                    points.Enqueue(new Point2D(cp.Coordinates.X, cp.Coordinates.Y));
            }

            // Time to stop?
            if (reSamplingCount++ > NUM_MAX_CALIBRATION_ATTEMPTS || points.Count > NUM_MAX_RESAMPLE_POINTS)
            {
                AbortCalibration(CalibrationRunnerResult.Failure, "Unable to calibrate.");
                StopAndClose();
                return;
            }

            // Resample?
            if (points != null && points.Count > 0)
            {
                // Transition from last point to first resample point
                Point2D firstPos = points.Peek(); // peek here, RunPointSequence pulls out of queue
                DoubleAnimation cX = CreateTransitionAnimation(currentPoint.X, Utility.Instance.ScaleDpi * firstPos.X, 500);
                DoubleAnimation cY = CreateTransitionAnimation(currentPoint.Y, Utility.Instance.ScaleDpi * firstPos.Y, 500);
                cX.Completed += delegate { RunPointSequence(); }; // once moved, start sequence
                calPointWpf.BeginAnimation(Canvas.LeftProperty, cX);
                calPointWpf.BeginAnimation(Canvas.TopProperty, cY);
            }
            else
            {
                RaiseResult(CalibrationRunnerResult.Success, string.Empty, res);
                StopAndClose();
            }
        }

        #endregion

        #endregion

        #region Private

        private void DoStart()
        {
            ShowMessage(MESSAGE_FOLLOW);
            calPointWpf.Visibility = Visibility.Visible;

            reSamplingCount = 0;
            points = CreatePointList();

            // run the fade-in animation
            var anim = new DoubleAnimation(0, TimeSpan.FromSeconds(FADE_IN_TIME))
            {
                From = 0.0,
                To = 1.0,
                AutoReverse = false
            };

            anim.Completed += delegate
            {
                calibrationFormReady = true;
                CalibrationMessage.Visibility = Visibility.Hidden;
                Focus();

                // windows faded in, now animate to first point
                Point2D firstPos = points.Peek();
                DoubleAnimation cX = CreateTransitionAnimation((Utility.Instance.ScaleDpi * screen.Bounds.Width) / 2, Utility.Instance.ScaleDpi * firstPos.X, 500);
                DoubleAnimation cY = CreateTransitionAnimation((Utility.Instance.ScaleDpi * screen.Bounds.Height) / 2, Utility.Instance.ScaleDpi * firstPos.Y, 500);
                cX.Completed += delegate { RunPointSequence(); }; // once moved to first point, start sequence

                calPointWpf.BeginAnimation(Canvas.LeftProperty, cX);
                calPointWpf.BeginAnimation(Canvas.TopProperty, cY);
            };

            // Start window fade in
            BeginAnimation(OpacityProperty, anim);
        }

        #region Timers

        private void CreateTimerRecording()
        {
            if (timerRecording == null)
            {
                timerRecording = new DispatcherTimer { Interval = new TimeSpan(0, 0, 0, 0, sampleTimeMs) };
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
                timerLatency = new DispatcherTimer { Interval = new TimeSpan(0, 0, 0, 0, latencyMs) };
                timerLatency.Stop();

                timerLatency.Tick += delegate
                {
                    timerLatency.Stop();

                    if (currentPoint != null)
                    {
                        // Signal tracker server that a point is starting, do the shrink animation and start timerRecording 
                        GazeManager.Instance.CalibrationPointStart((int)currentPoint.X, (int)currentPoint.Y);
                        AnimateCalibrationPoint();
                        timerRecording.Start();
                    }
                };
            }
        }

        #endregion

        #region Setup

        private void WindowContentRendered(object sender, EventArgs e)
        {
            FitToWindow();

            // Adjust for DPI scaling
            ActiveArea.Width = (int)(Utility.Instance.ScaleDpi * (ActiveArea.Width == 0 ? screen.Bounds.Width : ActiveArea.Width));
            ActiveArea.Height = (int)(Utility.Instance.ScaleDpi * (ActiveArea.Height == 0 ? screen.Bounds.Height : ActiveArea.Height));

            Show();
            Focus();
        }

        private void FitToWindow()
        {
            Width = screen.Bounds.Width * Utility.Instance.ScaleDpi;
            Height = screen.Bounds.Height * Utility.Instance.ScaleDpi;
            Top = screen.Bounds.Y * Utility.Instance.ScaleDpi;
            Left = screen.Bounds.X * Utility.Instance.ScaleDpi;
        }

        private void RunPointSequence()
        {
            reSamplingCount = 0;

            isAborting = false;

            try
            {
                // Set up two timers, one for recording delay and another for recording duration
                // 1. When point is shown we start timerLatency, on tick we signal tracker to start sampling (for duration of timerRecording)
                // 2. A point is sampled for the duration of the timerRecording
                CreateTimerLatency();
                CreateTimerRecording();

                // Signal tracker server that we're about to start (not when recalibrating points)
                if (points.Count == PointCount)
                    GazeManager.Instance.CalibrationStart((short)PointCount, this);

                // Get first point, draw it, start timers etc.
                currentPoint = PickNextPoint();
                DrawCalibrationPoint(currentPoint);
            }
            catch (Exception ex)
            {
                RaiseResult(CalibrationRunnerResult.Error, "An error occured in the calibration. Message: " + ex.Message);
            }
        }

        private void RaiseResult(CalibrationRunnerResult result, string message)
        {
            if (OnResult != null)
                OnResult(this, new CalibrationRunnerEventArgs(result, message));
        }

        private void RaiseResult(CalibrationRunnerResult result, string message, CalibrationResult calibrationReport)
        {
            if (OnResult != null)
                OnResult(this, new CalibrationRunnerEventArgs(result, message, calibrationReport));
        }

        #endregion

        #region Drawing

        private void DrawCalibrationPoint(Point2D position)
        {
            if (this.Dispatcher.Thread != Thread.CurrentThread)
            {
                Dispatcher.BeginInvoke(new MethodInvoker(() => DrawCalibrationPoint(position)));
                return;
            }

            if (position == null)
                return;

            double x = Math.Round(Utility.Instance.ScaleDpi * position.X, 0);
            double y = Math.Round(Utility.Instance.ScaleDpi * position.Y, 0);
            Canvas.SetLeft(calPointWpf, x);
            Canvas.SetTop(calPointWpf, y);
            calPointWpf.Visibility = Visibility.Visible;

            timerLatency.Start(); // Will issue PointStart and start timerRecording on tick
        }

        private void AnimateCalibrationPoint()
        {
            Dispatcher.Invoke(new Action(() => calPointWpf.StartAnimate()));
        }

        private DoubleAnimation CreateTransitionAnimation(double from, double to, int durationMs)
        {
            return new DoubleAnimation
            {
                From = from,
                To = to,
                Duration = TimeSpan.FromMilliseconds(durationMs),
                AccelerationRatio = 0.4,
                DecelerationRatio = 0.6
            };
        }

        #endregion

        #region Abort, Close

        private void MouseDbClick(object sender, MouseEventArgs e)
        {
            AbortCalibration(CalibrationRunnerResult.Abort, "User aborted calibration");
        }

        private void KeyUpDetected(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
                AbortCalibration(CalibrationRunnerResult.Abort, "User aborted calibration");
        }

        private void CloseWindow()
        {
            Dispatcher.Invoke(new Action(() => BeginAnimation(OpacityProperty, animateOut)));
        }

        private void StopAndClose()
        {
            if (timerLatency != null)
                timerLatency.Stop();

            if (timerRecording != null)
                timerRecording.Stop();

            CloseWindow();
        }

        private void AbortCalibration(string errorMessage)
        {
            AbortCalibration(CalibrationRunnerResult.Abort, errorMessage);
        }

        private void AbortCalibration(CalibrationRunnerResult type, string errorMessage)
        {
            if (isAborting)
                return; // Only one call is needed

            isAborting = true;
            GazeManager.Instance.CalibrationAbort();

            StopAndClose();

            RaiseResult(type, errorMessage);
        }

        #endregion

        #region Targets Logic

        private Point2D PickNextPoint()
        {
            if (points == null)
                points = CreatePointList();

            if (points.Count != 0)
                return points.Dequeue();

            return null;
        }

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

            // Shuffle point order
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

        private static double Lerp(double value1, double value2, double amount)
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

            switch (verticalAlignment)
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

            switch (horizontalAlignment)
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
