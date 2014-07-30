/*
 * Copyright (c) 2013-present, The Eye Tribe. 
 * All rights reserved.
 *
 * This source code is licensed under the BSD-style license found in the LICENSE file in the root directory of this source tree. 
 *
 */
using System;
using System.Threading;
using System.Windows;
using System.Windows.Forms;
using TETControls.Calibration;
using TETControls.TrackBox;
using TETCSharpClient.Data;
using System.Windows.Interop;
using TETCSharpClient;


namespace Calibration
{
    public partial class MainWindow : IConnectionStateListener
    {
        public MainWindow()
        {
            ConnectClient();
            InitializeComponent();
            Loaded += (sender, args) => InitClient();
        }

        private void ConnectClient()
        {
            // Create a client for the eye tracker
            GazeManager.Instance.Activate(GazeManager.ApiVersion.VERSION_1_0, GazeManager.ClientMode.Push);
        }

        private void InitClient()
        {
            // Default content of the action button
            btnAction.Content = "Calibrate";

            // Add a fresh instance of the trackbox in case we reinitialize the client connection.
            TrackingStatusGrid.Children.Clear();
            TrackingStatusGrid.Children.Add(new TrackBoxStatus());

            // Add listener if EyeTribe Server is closed
            GazeManager.Instance.AddConnectionStateListener(this);

            // What is the current connection state
            OnConnectionStateChanged(GazeManager.Instance.IsActivated);

            if (GazeManager.Instance.IsCalibrated)
            {
                // Get the latest successful calibration from the EyeTribe server
                RatingText.Text = RatingFunction(GazeManager.Instance.LastCalibrationResult);
                btnAction.Content = "Re-Calibrate";
            }
        }

        private void ButtonClicked(object sender, RoutedEventArgs e)
        {
            if (GazeManager.Instance.IsActivated)
            {
                Calibrate();
            }
            else
            {
                ConnectClient();
                InitClient();
            }
        }

        private void Calibrate()
        {
            btnAction.Content = "Re-Calibrate";

            //Run the calibration on 'this' monitor
            var ActiveScreen = Screen.FromHandle(new WindowInteropHelper(this).Handle);

            // Initialize and start the calibration
            CalibrationRunner calRunner = new CalibrationRunner(ActiveScreen, ActiveScreen.Bounds.Size, 9);
            calRunner.OnResult += calRunner_OnResult;
            calRunner.Start();
        }

        private void calRunner_OnResult(object sender, CalibrationRunnerEventArgs e)
        {
            if (RatingText.Dispatcher.Thread != Thread.CurrentThread)
            {
                this.Dispatcher.BeginInvoke(new MethodInvoker(() => calRunner_OnResult(sender, e)));
                return;
            }

            if (e.Result == CalibrationRunnerResult.Success)
            {
                RatingText.Text = RatingFunction(e.CalibrationResult);
            }
            else
            {
                System.Windows.MessageBox.Show("Calibration failed, please try again");
            }
        }

        private void WindowClosed(object sender, EventArgs e)
        {
            GazeManager.Instance.Deactivate();
        }

        public string RatingFunction(CalibrationResult result)
        {
            if (result == null)
                return "";

            var accuracy = result.AverageErrorDegree;

            if (accuracy < 0.5)
                return "Calibration Quality: PERFECT";

            if (accuracy < 0.7)
                return "Calibration Quality: GOOD";

            if (accuracy < 1)
                return "Calibration Quality: MODERATE";

            if (accuracy < 1.5)
                return "Calibration Quality: POOR";

            return "Calibration Quality: REDO";
        }

        public void OnConnectionStateChanged(bool IsActivated)
        {
            // The connection state listener detects when the connection to the EyeTribe server changes
            if (btnAction.Dispatcher.Thread != Thread.CurrentThread)
            {
                this.Dispatcher.BeginInvoke(new MethodInvoker(() => OnConnectionStateChanged(IsActivated)));
                return;
            }
            if (!IsActivated)
            {
                GazeManager.Instance.Deactivate();
                RatingText.Text = "";
                btnAction.Content = "Re-Connect";
            }
        }
    }
}
