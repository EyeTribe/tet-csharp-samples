# Change Log #
---

0.9.77.1 (2016-11-28)
---
- Updated C# SDK
- Updated JSON
- Fixed minor issue in Scroll demo where the aplication would crash if server was not running

0.9.77 (2016-05-18)
---
- Removing embedded dependencies, using NuGet for 'EyeTribe WPF Tools' instead

0.9.49 (2014-12-16)
---
- CalibrationSample introduces RefreshAPIConnection public method that helps to refresh listeners when the server has been manually restarted
- Simplified the calibration runner
- Fixed potential threading issue in trackbox control
- Trackbox sample queue is dependent on framerate
- Soved UI-update issue when closing Scroll sample  
- Updated C# SDK
- Minor performance improvements

0.9.40 (2014-07-30)
---
- Stability improvements for the calibration runner
- Updated C# SDK
- Minor performance improvements

0.9.35 (2014-05-20)
---
- Updated license
- Calibration result null check in the calibration sample
- Updated C# SDK
- Fixed stability issue in the calibration runner
- Calibration aborts if the Tracker is disconnected
- Minor performance improvements

0.9.27 (2014-02-12)
---
- Restyled and documented hard-coded values for device caps
- De-normalized calibration targets in calibration runner
- Setting opacity for the red background image when updating the background in TrackBox
- Fixed the halo artifacts in the gradient background for a large track box
- Reduced image size and memory consumption by using significantly smaller background images
- Calculations are kept in the background thread and UI elements are currently updated with a dispatcher timer running at 60 Hz
- Employing safe read write of shared variables between worker thread and UI thread in the TrackBox
- Introduced EyeCount enum in the calibration runner for improved readability

0.9.26 (2014-01-30)
---
- TETControls has been merged into this repository.
- The Utility class has been pruned
- Error states of the EyeTribe tracker is printed in the TrackBox
- TrackBoxHelper.cs has been integrated into the TrackBoxStatus.xaml  
- Minor UI tweaks 
- Improved scrolling function based on a Sigmoid
- A direction enum is used to determine the Scroll direction 

0.9.21 (2013-01-08)
---
- Initial release
