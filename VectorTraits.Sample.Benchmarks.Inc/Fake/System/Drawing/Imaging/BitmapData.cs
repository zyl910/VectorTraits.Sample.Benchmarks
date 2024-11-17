using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Runtime.InteropServices;

namespace System.Drawing.Imaging {

    /// <summary>
    /// Specifies the attributes of a bitmap image. The BitmapData class is used by the LockBits and UnlockBits(BitmapData) methods of the Bitmap class. Not inheritable.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public sealed class BitmapData {
        int width;
        int height;
        int stride;
        int pixelFormat;
        IntPtr scan0;
        int reserved;

        /// <summary>
        ///    Specifies the pixel width of the <see cref='System.Drawing.Bitmap'/>.
        /// </summary>
        public int Width {
            get { return width; }
            set { width = value; }
        }

        /// <summary>
        ///    Specifies the pixel height of the <see cref='System.Drawing.Bitmap'/>.
        /// </summary>
        public int Height {
            get { return height; }
            set { height = value; }
        }

        /// <summary>
        ///    Specifies the stride width of the <see cref='System.Drawing.Bitmap'/>.
        /// </summary>
        public int Stride {
            get { return stride; }
            set { stride = value; }
        }

        /// <summary>
        ///    Specifies the format of the pixel
        ///    information in this <see cref='System.Drawing.Bitmap'/>.
        /// </summary>
        public PixelFormat PixelFormat {
            get { return (PixelFormat)pixelFormat; }
            set {
                switch (value) {
                    case PixelFormat.DontCare:
                    // case PixelFormat.Undefined: same as DontCare
                    case PixelFormat.Max:
                    case PixelFormat.Indexed:
                    case PixelFormat.Gdi:
                    case PixelFormat.Format16bppRgb555:
                    case PixelFormat.Format16bppRgb565:
                    case PixelFormat.Format24bppRgb:
                    case PixelFormat.Format32bppRgb:
                    case PixelFormat.Format1bppIndexed:
                    case PixelFormat.Format4bppIndexed:
                    case PixelFormat.Format8bppIndexed:
                    case PixelFormat.Alpha:
                    case PixelFormat.Format16bppArgb1555:
                    case PixelFormat.PAlpha:
                    case PixelFormat.Format32bppPArgb:
                    case PixelFormat.Extended:
                    case PixelFormat.Format16bppGrayScale:
                    case PixelFormat.Format48bppRgb:
                    case PixelFormat.Format64bppPArgb:
                    case PixelFormat.Canonical:
                    case PixelFormat.Format32bppArgb:
                    case PixelFormat.Format64bppArgb:
                        break;
                    default:
                        throw new System.ComponentModel.InvalidEnumArgumentException("value", unchecked((int)value), typeof(PixelFormat));
                }
                pixelFormat = (int)value;
            }
        }

        /// <summary>
        ///    Specifies the address of the pixel data.
        /// </summary>
        public IntPtr Scan0 {
            get { return scan0; }
            set { scan0 = value; }
        }

        /// <summary>
        ///    Reserved. Do not use.
        /// </summary>
        public int Reserved {
            get { return reserved; }
            set { reserved = value; }
        }
    }
}
