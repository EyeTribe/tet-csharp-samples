/*
 * Copyright (c) 2013-present, The Eye Tribe. 
 * All rights reserved.
 *
 * This source code is licensed under the BSD-style license found in the LICENSE file in the root directory of this source tree. 
 *
 */

using System;
using TETCSharpClient.Data;

namespace TETControls.Calibration
{
    public enum CalibrationRunnerResult
    {
        Unknown = 0,
        Success = 1,
        Failure = 2,
        Abort = 3,
        Error = 4
    }

    public class CalibrationRunnerEventArgs : EventArgs
    {
        // This event argument is used to return result from the Calibration Runner. 
        // Application can listen for this single event instead of the Client library Interface method OnCalibrationResult

        private readonly CalibrationRunnerResult result = CalibrationRunnerResult.Unknown;
        private readonly string message = string.Empty;
        private readonly CalibrationResult calibrationResult = new CalibrationResult();

        public CalibrationRunnerEventArgs(CalibrationRunnerResult result)
        {
            this.result = result;
        }

        public CalibrationRunnerEventArgs(CalibrationRunnerResult result, string message)
        {
            this.result = result;
            this.message = message;
        }

        public CalibrationRunnerEventArgs(CalibrationRunnerResult result, string message, CalibrationResult calibrationResult)
        {
            this.result = result;
            this.message = message;
            this.calibrationResult = calibrationResult;
        }

        public CalibrationRunnerResult Result
        {
            get { return result; }
        }

        public string Message
        {
            get { return message; }
        }

        public CalibrationResult CalibrationResult
        {
            get { return calibrationResult; }
        }

    }
}
