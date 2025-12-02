using System;
using Cairo;

namespace TreeSplitting.Utils;

public static class Drawing
{
    // DrawSplit and DrawUpset are shamelessly stolen from ItemHammer in vssurvivalmod
    public static void DrawSplit(Context cr, int x, int y, float width, float height, double[] colordoubles)
        {
            Pattern pattern = null;
            Matrix matrix = cr.Matrix;

            cr.Save();
            float w = 220;
            float h = 182;
            float scale = Math.Min(width / w, height / h);
            matrix.Translate(x + Math.Max(0, (width - w * scale) / 2), y + Math.Max(0, (height - h * scale) / 2));
            matrix.Scale(scale, scale);
            cr.Matrix = matrix;

            cr.Operator = Operator.Over;
            pattern = new SolidPattern(colordoubles[0], colordoubles[1], colordoubles[2], colordoubles[3]);
            cr.SetSource(pattern);

            cr.NewPath();
            cr.MoveTo(59, 105.003906);
            cr.LineTo(1, 105.003906);
            cr.LineTo(1, 182.003906);
            cr.LineTo(101.5, 182.003906);
            cr.ClosePath();
            cr.MoveTo(59, 105.003906);
            cr.Tolerance = 0.1;
            cr.Antialias = Antialias.Default;
            cr.FillRule = FillRule.Winding;
            cr.FillPreserve();
            if (pattern != null) pattern.Dispose();

            cr.Operator = Operator.Over;
            cr.LineWidth = 1;
            cr.MiterLimit = 10;
            cr.LineCap = LineCap.Butt;
            cr.LineJoin = LineJoin.Miter;
            pattern = new SolidPattern(colordoubles[0], colordoubles[1], colordoubles[2], colordoubles[3]);
            cr.SetSource(pattern);

            cr.NewPath();
            cr.MoveTo(59, 105.003906);
            cr.LineTo(1, 105.003906);
            cr.LineTo(1, 182.003906);
            cr.LineTo(101.5, 182.003906);
            cr.ClosePath();
            cr.MoveTo(59, 105.003906);
            cr.Tolerance = 0.1;
            cr.Antialias = Antialias.Default;
            cr.StrokePreserve();
            if (pattern != null) pattern.Dispose();

            cr.Operator = Operator.Over;
            pattern = new SolidPattern(colordoubles[0], colordoubles[1], colordoubles[2], colordoubles[3]);
            cr.SetSource(pattern);

            cr.NewPath();
            cr.MoveTo(161.5, 105.003906);
            cr.LineTo(219.5, 105.003906);
            cr.LineTo(219.5, 182.003906);
            cr.LineTo(119, 182.003906);
            cr.ClosePath();
            cr.MoveTo(161.5, 105.003906);
            cr.Tolerance = 0.1;
            cr.Antialias = Antialias.Default;
            cr.FillRule = FillRule.Winding;
            cr.FillPreserve();
            if (pattern != null) pattern.Dispose();

            cr.Operator = Operator.Over;
            cr.LineWidth = 1;
            cr.MiterLimit = 10;
            cr.LineCap = LineCap.Butt;
            cr.LineJoin = LineJoin.Miter;
            pattern = new SolidPattern(colordoubles[0], colordoubles[1], colordoubles[2], colordoubles[3]);
            cr.SetSource(pattern);

            cr.NewPath();
            cr.MoveTo(161.5, 105.003906);
            cr.LineTo(219.5, 105.003906);
            cr.LineTo(219.5, 182.003906);
            cr.LineTo(119, 182.003906);
            cr.ClosePath();
            cr.MoveTo(161.5, 105.003906);
            cr.Tolerance = 0.1;
            cr.Antialias = Antialias.Default;
            cr.StrokePreserve();
            if (pattern != null) pattern.Dispose();

            cr.Operator = Operator.Over;
            pattern = new SolidPattern(colordoubles[0], colordoubles[1], colordoubles[2], colordoubles[3]);
            cr.SetSource(pattern);

            cr.NewPath();
            cr.MoveTo(106.648438, 118.003906);
            cr.CurveTo(104.824219, 113.109375, 103.148438, 108.210938, 101.621094, 103.316406);
            cr.CurveTo(100.0625, 98.421875, 98.644531, 93.523438, 97.25, 88.628906);
            cr.CurveTo(95.914063, 83.730469, 94.53125, 78.835938, 93.371094, 73.941406);
            cr.CurveTo(92.183594, 69.042969, 91.214844, 64.148438, 90.199219, 59.253906);
            cr.CurveTo(89.710938, 56.804688, 89.003906, 54.355469, 88.191406, 51.90625);
            cr.CurveTo(87.378906, 49.460938, 86.460938, 47.011719, 85.734375, 44.5625);
            cr.CurveTo(85.015625, 42.117188, 84.542969, 39.667969, 84.503906, 37.21875);
            cr.CurveTo(84.453125, 34.773438, 84.820313, 32.324219, 85.5, 29.875);
            cr.CurveTo(86.886719, 24.980469, 89.078125, 20.085938, 92.378906, 15.1875);
            cr.CurveTo(95.769531, 10.292969, 99.902344, 5.394531, 106.648438, 0.5);
            cr.LineTo(111.351563, 0.5);
            cr.CurveTo(118.097656, 5.394531, 122.230469, 10.292969, 125.621094, 15.1875);
            cr.CurveTo(128.921875, 20.085938, 131.113281, 24.980469, 132.5, 29.875);
            cr.CurveTo(133.179688, 32.324219, 133.546875, 34.773438, 133.496094, 37.21875);
            cr.CurveTo(133.457031, 39.667969, 132.984375, 42.117188, 132.265625, 44.5625);
            cr.CurveTo(131.539063, 47.011719, 130.621094, 49.460938, 129.808594, 51.90625);
            cr.CurveTo(128.996094, 54.355469, 128.289063, 56.804688, 127.800781, 59.253906);
            cr.CurveTo(126.785156, 64.148438, 125.820313, 69.042969, 124.628906, 73.941406);
            cr.CurveTo(123.46875, 78.835938, 122.085938, 83.730469, 120.75, 88.628906);
            cr.CurveTo(119.355469, 93.523438, 117.9375, 98.421875, 116.378906, 103.316406);
            cr.CurveTo(114.855469, 108.210938, 113.175781, 113.105469, 111.351563, 118.003906);
            cr.ClosePath();
            cr.MoveTo(106.648438, 118.003906);
            cr.Tolerance = 0.1;
            cr.Antialias = Antialias.Default;
            cr.FillRule = FillRule.Winding;
            cr.FillPreserve();
            if (pattern != null) pattern.Dispose();

            cr.Operator = Operator.Over;
            cr.LineWidth = 4;
            cr.MiterLimit = 10;
            cr.LineCap = LineCap.Butt;
            cr.LineJoin = LineJoin.Miter;
            pattern = new SolidPattern(colordoubles[0], colordoubles[1], colordoubles[2], colordoubles[3]);
            cr.SetSource(pattern);

            cr.NewPath();
            cr.MoveTo(130.261719, 118.003906);
            cr.LineTo(165.261719, 70.003906);
            cr.Tolerance = 0.1;
            cr.Antialias = Antialias.Default;
            cr.StrokePreserve();
            if (pattern != null) pattern.Dispose();

            cr.Operator = Operator.Over;
            cr.LineWidth = 4;
            cr.MiterLimit = 10;
            cr.LineCap = LineCap.Butt;
            cr.LineJoin = LineJoin.Miter;
            pattern = new SolidPattern(colordoubles[0], colordoubles[1], colordoubles[2], colordoubles[3]);
            cr.SetSource(pattern);

            cr.NewPath();
            cr.MoveTo(51.25, 70.003906);
            cr.LineTo(86.25, 118.003906);
            cr.Tolerance = 0.1;
            cr.Antialias = Antialias.Default;
            cr.StrokePreserve();
            if (pattern != null) pattern.Dispose();

            cr.Restore();
        }


    public static void DrawUpset(Context cr, int x, int y, float width, float height, double[] colordoubles, double rot)
        {
            Pattern pattern = null;
            Matrix matrix = cr.Matrix;

            cr.Save();
            float w = 91;
            float h = 170;
            float scale = Math.Min(width / w, height / h);
            matrix.Translate(x + Math.Max(0, (width - w * scale) / 2), y + Math.Max(0, (height - h * scale) / 2));
            matrix.Scale(scale, scale);
            matrix.Translate(w / 2, h / 2);
            matrix.Rotate(rot);
            matrix.Translate(-w/2, -h/2);

            cr.Matrix = matrix;

            cr.Operator = Operator.Over;
            pattern = new SolidPattern(colordoubles[0], colordoubles[1], colordoubles[2], colordoubles[3]);
            cr.SetSource(pattern);

            cr.NewPath();
            cr.MoveTo(91, 124.667969);
            cr.CurveTo(91, 149.519531, 70.851563, 169.667969, 46, 169.667969);
            cr.CurveTo(21.148438, 169.667969, 1, 149.519531, 1, 124.667969);
            cr.CurveTo(1, 99.816406, 21.148438, 79.667969, 46, 79.667969);
            cr.CurveTo(70.851563, 79.667969, 91, 99.816406, 91, 124.667969);
            cr.ClosePath();
            cr.MoveTo(91, 124.667969);
            cr.Tolerance = 0.1;
            cr.Antialias = Antialias.Default;
            cr.FillRule = FillRule.Winding;
            cr.FillPreserve();
            if (pattern != null) pattern.Dispose();

            cr.Operator = Operator.Over;
            cr.LineWidth = 1;
            cr.MiterLimit = 10;
            cr.LineCap = LineCap.Butt;
            cr.LineJoin = LineJoin.Miter;
            pattern = new SolidPattern(colordoubles[0], colordoubles[1], colordoubles[2], colordoubles[3]);
            cr.SetSource(pattern);

            cr.NewPath();
            cr.MoveTo(91, 124.667969);
            cr.CurveTo(91, 149.519531, 70.851563, 169.667969, 46, 169.667969);
            cr.CurveTo(21.148438, 169.667969, 1, 149.519531, 1, 124.667969);
            cr.CurveTo(1, 99.816406, 21.148438, 79.667969, 46, 79.667969);
            cr.CurveTo(70.851563, 79.667969, 91, 99.816406, 91, 124.667969);
            cr.ClosePath();
            cr.MoveTo(91, 124.667969);
            cr.Tolerance = 0.1;
            cr.Antialias = Antialias.Default;
            cr.StrokePreserve();
            if (pattern != null) pattern.Dispose();

            cr.Operator = Operator.Over;
            pattern = new SolidPattern(colordoubles[0], colordoubles[1], colordoubles[2], colordoubles[3]);
            cr.SetSource(pattern);

            cr.NewPath();
            cr.MoveTo(82.265625, 21.296875);
            cr.LineTo(47.160156, 0.5);
            cr.LineTo(11.734375, 21.296875);
            cr.LineTo(26.457031, 21.296875);
            cr.LineTo(26.457031, 71.335938);
            cr.LineTo(67.808594, 71.335938);
            cr.LineTo(67.808594, 21.296875);
            cr.ClosePath();
            cr.MoveTo(82.265625, 21.296875);
            cr.Tolerance = 0.1;
            cr.Antialias = Antialias.Default;
            cr.FillRule = FillRule.Winding;
            cr.FillPreserve();
            if (pattern != null) pattern.Dispose();

            cr.Restore();
        }
}