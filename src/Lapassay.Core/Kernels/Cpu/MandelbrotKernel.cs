using System.Numerics;
using System.Runtime.Versioning;
using Lapassay.Core.Harness;

namespace Lapassay.Core.Kernels.Cpu;

/// <summary>
/// Mandelbrot set, 2048x2048, max 256 iterations, escape radius 2.
/// Uses Vector&lt;double&gt; for SIMD and parallelizes across rows.
/// Reports megapixels per second and total iterations completed.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class MandelbrotKernel
{
    readonly int _width;
    readonly int _height;
    readonly int _maxIter;
    readonly ushort[] _output;
    readonly double _xMin, _xMax, _yMin, _yMax;

    public MandelbrotKernel(int width = 2048, int height = 2048, int maxIter = 256)
    {
        _width = width;
        _height = height;
        _maxIter = maxIter;
        _output = new ushort[width * height];
        _xMin = -2.0; _xMax = 1.0;
        _yMin = -1.5; _yMax = 1.5;
    }

    public void Run()
    {
        var dx = (_xMax - _xMin) / _width;
        var dy = (_yMax - _yMin) / _height;
        var width = _width;
        var maxIter = _maxIter;
        var vecSize = Vector<double>.Count;

        Parallel.For(0, _height, y =>
        {
            var y0 = _yMin + y * dy;
            var cy = new Vector<double>(y0);
            var fourVec = new Vector<double>(4.0);

            // SIMD across vecSize adjacent pixels in a row.
            var x = 0;
            var strideOffsets = new double[vecSize];
            for (; x <= width - vecSize; x += vecSize)
            {
                for (var i = 0; i < vecSize; i++) strideOffsets[i] = _xMin + (x + i) * dx;
                var cx = new Vector<double>(strideOffsets);

                var zx = Vector<double>.Zero;
                var zy = Vector<double>.Zero;
                var iterations = Vector<long>.Zero;
                var one = Vector<long>.One;

                for (var iter = 0; iter < maxIter; iter++)
                {
                    var zx2 = zx * zx;
                    var zy2 = zy * zy;
                    var mask = Vector.LessThanOrEqual(zx2 + zy2, fourVec);
                    var longMask = Vector.AsVectorInt64(mask);
                    // Early-out: stop iterating once all lanes have escaped.
                    if (longMask.Equals(Vector<long>.Zero)) break;
                    iterations += longMask & one;

                    var newZx = zx2 - zy2 + cx;
                    var newZy = 2.0 * zx * zy + cy;
                    zx = newZx;
                    zy = newZy;
                }

                for (var i = 0; i < vecSize; i++)
                    _output[y * width + x + i] = (ushort)iterations[i];
            }

            // Scalar tail
            for (; x < width; x++)
            {
                var x0 = _xMin + x * dx;
                double zx = 0, zy = 0;
                int iter;
                for (iter = 0; iter < maxIter; iter++)
                {
                    var zx2 = zx * zx;
                    var zy2 = zy * zy;
                    if (zx2 + zy2 > 4.0) break;
                    var newZx = zx2 - zy2 + x0;
                    zy = 2 * zx * zy + y0;
                    zx = newZx;
                }
                _output[y * width + x] = (ushort)iter;
            }
        });

        // Prevent DCE
        Sink.Consume((int)_output[0]);
    }

    public double MegapixelsPerSecond(double seconds) => _width * _height / seconds / 1e6;
    public int Width => _width;
    public int Height => _height;
    public ushort[] Output => _output;
}
