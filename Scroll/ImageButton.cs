/*
 * Copyright (c) 2013-present, The Eye Tribe. 
 * All rights reserved.
 *
 * This source code is licensed under the BSD-style license found in the LICENSE file in the root directory of this source tree. 
 *
 */
using System.Windows;
using System.Windows.Media;
using System.Windows.Controls.Primitives;


namespace Scroll
{
    public class ImageButton : ToggleButton
    {
        private static readonly DependencyProperty ActiveIconProperty = DependencyProperty.Register("Icon", typeof(ImageSource), typeof(ImageButton));

        public ImageSource Icon
        {
            get { return (ImageSource)GetValue(ActiveIconProperty); }
            set { SetValue(ActiveIconProperty, value); }
        }

        static ImageButton()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(ImageButton), new FrameworkPropertyMetadata(typeof(ImageButton)));
        }
    }
}
