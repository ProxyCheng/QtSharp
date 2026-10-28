using System;
using QtSharp.QtGui;

namespace QtSharp.Examples.Hosting
{
    /// <summary>Pixel helpers shared by the examples and the binding tests.</summary>
    public static class ExamplePixels
    {
        /// <summary>
        /// Whether an off-screen render covers the widget's logical area. QWidget::grab() hands back
        /// a pixmap in *device* pixels, so a widget that is 200 logical pixels across comes back
        /// 200 * devicePixelRatio() pixels wide on a scaled display; comparing against the logical
        /// number only holds on a 100% display. A fractional ratio also rounds the product (297
        /// logical at 1.5 is 445.5 device), hence the one-pixel allowance.
        /// </summary>
        public static bool GrabCovers(QPixmap pixmap, int logicalWidth, int logicalHeight)
            => pixmap != null && !pixmap.IsNull()
               && Math.Abs(pixmap.Width() - logicalWidth * pixmap.DevicePixelRatio()) < 1.0
               && Math.Abs(pixmap.Height() - logicalHeight * pixmap.DevicePixelRatio()) < 1.0;
    }
}
