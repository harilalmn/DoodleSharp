using System;
using System.Linq;
using System.Numerics;
using System.Collections.Generic;
using C2VGeometry;
using DoodleSharp.Console;
using DoodleSharp.Animation;

namespace ViewportShowcase
{
    public class Viz
    {
        // ================================================================
        // VIEWPORTS · one drawing, several independent views
        //
        // Every cell below is a real canvas with its own pan and zoom, so
        // the four studies keep their own coordinates instead of being
        // shuffled apart by hand into one shared space.
        //
        // Layout:
        //     +---------------------+----------------+
        //     |                     |   harmonics    |
        //     |    rose curve       +----------------+
        //     |    (2 shares)       |   packing      |
        //     |                     +----------------+
        //     |                     |   star         |
        //     +---------------------+----------------+
        // ================================================================
        public static void Main()
        {
            ShapeDefaults.Reset();

            Viewports.Columns = 2;
            Viewports[0][0].Width = "2*";      // the main view takes two thirds
            Viewports[0][1].Width = "1*";

            Viewport studies = Viewports[0][1];
            studies.Rows = 3;
            studies[1].Height = "1.4*";        // give the middle study a little more room

            RoseCurve(Viewports[0][0]);
            Harmonics(studies[0][0]);
            CirclePacking(studies[1][0]);
            StarPolygon(studies[2][0]);

            VizConsole.Log("Four views, four coordinate spaces.");
            VizConsole.Log("Scroll to zoom a cell; hover one to reveal its zoom controls.");
        }

        /// <summary>A rose curve, r = a·cos(k·θ), drawn as one continuous polyline.</summary>
        private static void RoseCurve(Viewport target)
        {
            Label(target, new VXYZ(0, 118), "rose  r = 100 cos 5t", "White");

            var pts = new List<VXYZ>();
            for (int i = 0; i <= 720; i++)
            {
                double t = i * Math.PI / 360.0;
                double r = 100 * Math.Cos(5 * t);
                pts.Add(new VXYZ(r * Math.Cos(t), r * Math.Sin(t)));
            }

            new VPolyline(pts) { Color = "Cyan", LineWeight = 2 }.Place(target);

            // The circle the petals are inscribed in, as a construction guide.
            new VCircle(new VXYZ(0, 0), 100)
            {
                Color = "#3A6EA5",
                LineType = LineType.Dashed
            }.Place(target);

            new VLine(new VXYZ(-110, 0), new VXYZ(110, 0)) { Color = "#444444" }.Place(target);
            new VLine(new VXYZ(0, -110), new VXYZ(0, 110)) { Color = "#444444" }.Place(target);
        }

        /// <summary>Three stacked sine waves — the first three harmonics, and their sum.</summary>
        private static void Harmonics(Viewport target)
        {
            Label(target, new VXYZ(0, 46), "harmonics 1:2:3", "Orange");

            string[] colours = { "#FF8C42", "#FFB86B", "#FFD9A0" };

            for (int h = 1; h <= 3; h++)
            {
                var pts = new List<VXYZ>();
                for (int i = 0; i <= 200; i++)
                {
                    double x = -80 + i * 0.8;
                    pts.Add(new VXYZ(x, 22 / h * Math.Sin(h * x * Math.PI / 80)));
                }
                new VPolyline(pts) { Color = colours[h - 1], LineWeight = 2 }.Place(target);
            }

            var sum = new List<VXYZ>();
            for (int i = 0; i <= 200; i++)
            {
                double x = -80 + i * 0.8;
                double y = 0;
                for (int h = 1; h <= 3; h++) y += 22.0 / h * Math.Sin(h * x * Math.PI / 80);
                sum.Add(new VXYZ(x, y));
            }
            new VPolyline(sum) { Color = "White", LineWeight = 3 }.Place(target);

            new VLine(new VXYZ(-84, 0), new VXYZ(84, 0)) { Color = "#444444" }.Place(target);
        }

        /// <summary>Circles packed on a hexagonal lattice, clipped to a bounding circle.</summary>
        private static void CirclePacking(Viewport target)
        {
            Label(target, new VXYZ(0, 74), "hex packing", "Lime");

            const double r = 9;
            const double bound = 62;

            new VCircle(new VXYZ(0, 0), bound)
            {
                Color = "#2E7D32",
                LineType = LineType.Dashed
            }.Place(target);

            for (int row = -6; row <= 6; row++)
            {
                double y = row * r * Math.Sqrt(3);
                double offset = (row % 2 == 0) ? 0 : r;

                for (int col = -6; col <= 6; col++)
                {
                    var centre = new VXYZ(col * 2 * r + offset, y);
                    if (centre.GetLength() + r > bound) continue;

                    new VCircle(centre, r) { Color = "Lime" }.Place(target);
                }
            }
        }

        /// <summary>A {9/4} star polygon: every fourth vertex of a nonagon, joined in one loop.</summary>
        private static void StarPolygon(Viewport target)
        {
            Label(target, new VXYZ(0, 74), "star  {9/4}", "Magenta");

            const int n = 9;
            const int step = 4;
            const double radius = 60;

            var vertices = new List<VXYZ>();
            for (int i = 0; i < n; i++)
            {
                double a = i * 2 * Math.PI / n + Math.PI / 2;
                vertices.Add(new VXYZ(radius * Math.Cos(a), radius * Math.Sin(a)));
            }

            var loop = new List<VXYZ>();
            for (int i = 0; i <= n; i++) loop.Add(vertices[(i * step) % n]);

            new VPolyline(loop) { Color = "Magenta", LineWeight = 2 }.Place(target);

            new VCircle(new VXYZ(0, 0), radius)
            {
                Color = "#7B2D8E",
                LineType = LineType.Dashed
            }.Place(target);

            foreach (var v in vertices)
            {
                new VCircle(v, 2.5) { Color = "#D48CE0" }.Place(target);
            }
        }

        /// <summary>
        /// A caption for one view. Text masks itself in the canvas colour by default, so a label
        /// stays readable wherever it lands.
        /// </summary>
        private static void Label(Viewport target, VXYZ at, string text, string colour)
        {
            new VText(at, text, 9)
            {
                Color = colour,
                Anchor = VTextAnchor.BottomCenter
            }.Place(target);
        }
    }
}
