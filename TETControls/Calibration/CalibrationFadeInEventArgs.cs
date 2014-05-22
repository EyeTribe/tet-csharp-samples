/*
 * Copyright (c) 2013-present, The Eye Tribe. 
 * All rights reserved.
 *
 * This source code is licensed under the BSD-style license found in the LICENSE file in the root directory of this source tree. 
 *
 */
using System;

namespace TETControls.Calibration
{
    public delegate void CalibrationFadeInFormHandler(object sender, EventArgs e);

    public class CalibrationFadeInArgs : EventArgs
    {
        private readonly bool _eventInfo;

        public CalibrationFadeInArgs(bool ready)
        {
            _eventInfo = ready;
        }

        public bool GetInfo()
        {
            return _eventInfo;
        }
    }
}
