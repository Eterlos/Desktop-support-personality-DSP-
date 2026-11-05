using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace dcp_project
{
    public class PixelPerfectImage : Image
    {
        private byte[]? _alphaMask;
        private int _pixelWidth;
        private int _pixelHeight;

        public PixelPerfectImage()
        {
            DependencyPropertyDescriptor
                .FromProperty(SourceProperty, typeof(Image))
                .AddValueChanged(this, (_, _) => BuildAlphaMask());
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);
            BuildAlphaMask();
        }

        private void BuildAlphaMask()
        {
            if (Source is BitmapSource bmp && bmp.PixelWidth > 0)
            {
                if (bmp.IsDownloading)
                {
                    return; 
                }

                _pixelWidth = bmp.PixelWidth;
                _pixelHeight = bmp.PixelHeight;

                try
                {
                    var pixels = new byte[_pixelWidth * _pixelHeight * 4];
                    int stride = _pixelWidth * 4;
                    bmp.CopyPixels(pixels, stride, 0);

                    _alphaMask = new byte[_pixelWidth * _pixelHeight];

                    for (int i = 0; i < _pixelWidth * _pixelHeight; i++)
                    {
                        _alphaMask[i] = pixels[i * 4 + 3];
                    }
                }
                catch
                {
                    _alphaMask = null;
                }
            }
            else
            {
                _alphaMask = null;
            }
        }

        protected override HitTestResult? HitTestCore(PointHitTestParameters hitTestParameters)
        {
            // Безопасный возврат базового поведения на этапе инициализации (не ломает рендеринг)
            if (_alphaMask == null || ActualWidth <= 0 || ActualHeight <= 0)
            {
                return base.HitTestCore(hitTestParameters);
            }

            var point = hitTestParameters.HitPoint;

            int x = (int)(point.X * _pixelWidth / ActualWidth);
            int y = (int)(point.Y * _pixelHeight / ActualHeight);

            if (x >= 0 && x < _pixelWidth && y >= 0 && y < _pixelHeight)
            {
                if (_alphaMask[y * _pixelWidth + x] > 10)
                {
                    return new PointHitTestResult(this, point);
                }
            }

            // Клик в прозрачную зону пропускаем
            return null;
        }
    }
}
