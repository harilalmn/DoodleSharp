using System;
using System.Linq;
using System.Numerics;
using System.Collections.Generic;
using C2VGeometry;
using DoodleSharp.Console;

namespace ConvexHull
{
    public class Step
    {
        public int Going { get; private set; }
        public int Rise { get; private set; }
        public VXYZ Location { get; private set; }
        public VLine Tread { get; set; }
        public VLine Riser { get; set; }
        public VXYZ Start { get; set; }
        public VXYZ Mid { get; set; }
        public VXYZ End { get; set; }

        public Step(VXYZ location, int going, int rise)
        {
            Location = location;
            Going = going;
            Rise = rise;

            Start = (VXYZ) location.Clone();
            Mid = Start + new VXYZ(0, rise);
            End = Mid + new VXYZ(Going, 0);

            // Start/Mid/End are VXYZ — coordinates, not shapes, so they have nothing to draw.
            // A visible dot is a VPoint built from the coordinate.
            VPoint startMarker = new VPoint(Start);
            VPoint midMarker = new VPoint(Mid);
            VPoint endMarker = new VPoint(End);

            VPolyline pl = new VPolyline(new VXYZ[]{Start, Mid, End});
            pl.Draw();

        }
    }
}