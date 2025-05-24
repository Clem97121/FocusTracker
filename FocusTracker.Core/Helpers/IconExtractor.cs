using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace FocusTracker.Core.Helpers
{
    public static class IconExtractor
    {
        public static byte[]? GetIconBytes(string exePath)
        {
            try
            {
                Icon icon = Icon.ExtractAssociatedIcon(exePath);
                if (icon == null)
                    return null;

                using var bmp = icon.ToBitmap();
                using var ms = new MemoryStream();
                bmp.Save(ms, ImageFormat.Png);
                return ms.ToArray();
            }
            catch
            {
                return null;
            }
        }
    }
}
