// Geometry for every part of the V8. Static parts are modelled directly in the
// engine coordinate frame so they drop into the assembly at the origin:
//   origin = crankshaft centreline at cylinder 1's crank throw
//   +Z = towards the front of the engine (balancer end), +Y = up
//   +X = left (driver) bank, cylinders 1-3-5-7;  -X = right bank, cylinders 2-4-6-8
// Moving parts (piston, pin, rod) are modelled in their own local frame.
using System;
using System.Collections.Generic;
using System.Linq;

namespace V8
{
    public static class Spec
    {
        public const double S = 0.70710678118654752;     // sin/cos 45 deg
        public const double BoreR = 49.5, Throw = 46.0, RodLen = 154.8;
        public const double Deck = 234.95, Spacing = 111.76, CompHt = 33.8, Stagger = 11.9;
        public const double MainR = 32.4, MainHalf = 13.88, PinR = 26.65, PinHalf = 24, WebOut = 42;
        public const double BankW = 72, SkirtX = 110, RailY = -70;
        public const double BlockFront = 72, BlockRear = -406;
        public const double HeadH = 105, CoverH = 48;
        public static readonly double[] PinDeg = { 0, 90, 270, 180 };   // cross-plane crank

        public static double ZPin(int k) { return -k * Spacing; }
        public static double ZMain(int j) { return Spacing / 2 - j * Spacing; }
        public static int Sigma(int cyl) { return cyl % 2 == 1 ? 1 : -1; }
        public static int ThrowOf(int cyl) { return (cyl - 1) / 2; }
        public static double ZCyl(int cyl) { return ZPin(ThrowOf(cyl)) + Sigma(cyl) * Stagger; }
        public static V2 U(int sg) { return new V2(sg * S, S); }      // bank (bore) axis
        public static V2 P(int sg) { return new V2(sg * S, -S); }     // across the bank, pointing outboard
        public static V2 Bank(int sg, double a, double b) { return U(sg) * a + P(sg) * b; }
        public static V2 CrankPin(int k, double crankDeg)
        {
            double ph = (PinDeg[k] + crankDeg) * Math.PI / 180;
            return new V2(Throw * Math.Sin(ph), Throw * Math.Cos(ph));
        }
        // distance from crank centreline to the piston pin along the bore axis
        public static double PistonPinDist(int cyl, double crankDeg)
        {
            var c = CrankPin(ThrowOf(cyl), crankDeg);
            var u = U(Sigma(cyl));
            double along = V2.Dot(c, u), perp = V2.Cross(u, c);
            return along + Math.Sqrt(RodLen * RodLen - perp * perp);
        }
        public static List<int> BankCyls(int sg) { return Enumerable.Range(1, 8).Where(n => Sigma(n) == sg).ToList(); }
        public static string Side(int sg) { return sg > 0 ? "Left" : "Right"; }
        public static double HeadZ0(int sg) { return Math.Max(BankCyls(sg).Min(n => ZCyl(n)) - 60, BlockRear + 1); }
        public static double HeadZ1(int sg) { return Math.Min(BankCyls(sg).Max(n => ZCyl(n)) + 60, BlockFront - 0.1); }
    }

    public static class Parts
    {
        const double H = Spec.Deck, W = Spec.BankW;

        static List<V2> L(params V2[] p) { return new List<V2>(p); }
        static V2 V(double x, double y) { return new V2(x, y); }
        static double[] R(params double[] r) { return r; }

        // ------------------------------------------------------------------ block
        public static string Block()
        {
            var p = new PartBuilder("Engine Block");
            double s = Spec.S, xs = Spec.SkirtX;
            double aj = xs / s - W, yj = s * (aj - W);
            p.BeginSketch(p.FrontPlane);
            p.PolyXY(L(V(xs, Spec.RailY), V(xs, yj), Spec.Bank(1, H, W), Spec.Bank(1, H, -W), V(0, Math.Sqrt(2) * W),
                       Spec.Bank(-1, H, -W), Spec.Bank(-1, H, W), V(-xs, yj), V(-xs, Spec.RailY)),
                     0, R(0, 20, 3, 3, 8, 3, 3, 20, 0));
            p.Extrude(p.EndSketch(), Spec.BlockRear, Spec.BlockFront, "Block Casting");

            // engine mount pads on both skirts
            p.BeginSketch(p.FrontPlane);
            p.PolyXY(L(V(xs - 1, -45), V(xs + 12, -45), V(xs + 12, 0), V(xs - 1, 0)), 4);
            p.PolyXY(L(V(-xs + 1, -45), V(-xs + 1, 0), V(-xs - 12, 0), V(-xs - 12, -45)), 4);
            p.Extrude(p.EndSketch(), -260, -140, "Engine Mount Pads");

            // crankcase bays between the main bearing bulkheads
            for (int k = 0; k < 4; k++)
            {
                p.BeginSketch(p.FrontPlane);
                p.CircleXY(V(0, 0), 92);
                p.Cut(p.EndSketch(), Spec.ZPin(k) - 43.88, Spec.ZPin(k) + 43.88, "Crankcase Bay " + (k + 1));
            }
            p.BeginSketch(p.FrontPlane);
            p.CircleXY(V(0, 0), Spec.MainR + 0.1);
            p.CutThroughBoth(p.EndSketch(), "Main Bearing Bore");

            foreach (int sg in new[] { 1, -1 })
            {
                var cyls = Spec.BankCyls(sg);
                var deck = Spec.Bank(sg, H, 0);
                p.BeginSketchOnFace(deck.X, deck.Y, Spec.ZCyl(cyls[0]));
                foreach (int n in cyls) p.Circle(deck.X, deck.Y, Spec.ZCyl(n), Spec.BoreR);
                p.CutIntoFace(p.EndSketch(), H - 60, "Cylinder Bores " + Spec.Side(sg));

                var side = Spec.Bank(sg, 150, W);
                p.BeginSketchOnFace(side.X, side.Y, Spec.ZCyl(cyls[0]));
                foreach (int n in cyls) p.Circle(side.X, side.Y, Spec.ZCyl(n), 16);
                p.ExtrudeFromFace(p.EndSketch(), 3, "Core Plugs " + Spec.Side(sg));
            }

            p.AxisFromPlanes("Crank Axis", p.TopPlane, p.RightPlane);
            for (int n = 1; n <= 8; n++)
            {
                double z = Spec.ZCyl(n);
                var far = Spec.Bank(Spec.Sigma(n), 300, 0);
                p.Axis("Bore Axis " + n, 0, 0, z, far.X, far.Y, z);
                p.OffsetPlane("Cylinder Plane " + n, p.FrontPlane, z);
            }
            p.SetColor(Cad.Color(0.62, 0.64, 0.67, 0.3));
            p.Report();
            string path = p.Save(); p.Close(); return path;
        }

        // ------------------------------------------------------------ crankshaft
        public static string Crankshaft()
        {
            var p = new PartBuilder("Crankshaft");
            for (int j = 0; j < 5; j++)
            {
                double z0 = Spec.ZMain(j) - Spec.MainHalf, z1 = Spec.ZMain(j) + Spec.MainHalf;
                if (j == 4) z0 = Spec.BlockRear - 1;
                p.BeginSketch(p.FrontPlane);
                p.CircleXY(V(0, 0), Spec.MainR);
                p.Extrude(p.EndSketch(), z0, z1, "Main Journal " + (j + 1));
            }
            p.BeginSketch(p.FrontPlane);
            p.CircleXY(V(0, 0), 25);
            p.Extrude(p.EndSketch(), Spec.ZMain(0) + Spec.MainHalf, 139, "Snout");
            p.BeginSketch(p.FrontPlane);
            p.CircleXY(V(0, 0), 55);
            p.Extrude(p.EndSketch(), -419, Spec.BlockRear - 1, "Flywheel Flange");

            for (int k = 0; k < 4; k++)
            {
                var c = Spec.CrankPin(k, 0);
                double zp = Spec.ZPin(k);
                p.BeginSketch(p.FrontPlane);
                p.CircleXY(c, Spec.PinR);
                p.Extrude(p.EndSketch(), zp - Spec.PinHalf, zp + Spec.PinHalf, "Rod Journal " + (k + 1));
                p.BeginSketch(p.FrontPlane); WebOutline(p, c);
                p.Extrude(p.EndSketch(), zp + Spec.PinHalf, zp + Spec.WebOut, "Web " + (k + 1) + "A");
                p.BeginSketch(p.FrontPlane); WebOutline(p, c);
                p.Extrude(p.EndSketch(), zp - Spec.WebOut, zp - Spec.PinHalf, "Web " + (k + 1) + "B");
            }
            p.BeginSketch(p.FrontPlane);
            for (int i = 0; i < 6; i++) p.CircleXY(V(42 * Math.Cos(i * Math.PI / 3), 42 * Math.Sin(i * Math.PI / 3)), 5.5);
            p.Cut(p.EndSketch(), -420, Spec.BlockRear - 1, "Flange Bolt Holes");

            p.AxisFromPlanes("Main Axis", p.TopPlane, p.RightPlane);
            for (int k = 0; k < 4; k++)
            {
                var c = Spec.CrankPin(k, 0); double zp = Spec.ZPin(k);
                p.Axis("Pin Axis " + (k + 1), c.X, c.Y, zp - 20, c.X, c.Y, zp + 20);
            }
            p.SetColor(Cad.Color(0.66, 0.67, 0.70, 0.8));
            p.Report();
            string path = p.Save(); p.Close(); return path;
        }

        // cam-shaped web: counterweight arc round the main axis, tangent to a boss round the pin
        static void WebOutline(PartBuilder p, V2 c)
        {
            double rc = 72, rp = 34, d = c.Len;
            double g = Math.Acos((rc - rp) / d), psi = Math.Atan2(c.Y, c.X);
            Func<double, V2> dir = ang => new V2(Math.Cos(ang), Math.Sin(ang));
            V2 a1 = dir(psi + g) * rc, a2 = dir(psi - g) * rc;
            V2 b1 = c + dir(psi + g) * rp, b2 = c + dir(psi - g) * rp;
            p.ArcXY(c, b1, b2, -1);
            p.LineXY(b2, a2);
            p.ArcXY(V(0, 0), a2, a1, -1);
            p.LineXY(a1, b1);
        }

        // ---------------------------------------------------------------- piston
        public static string Piston()
        {
            var p = new PartBuilder("Piston");
            double top = Spec.CompHt;
            p.BeginSketch(p.FrontPlane);
            p.CenterLine(0, -60, 0, 0, -40, 0);
            p.PolyXY(L(V(0, top), V(49.3, top), V(49.3, -26), V(44.3, -26), V(44.3, 19), V(0, 19)), 0, R(0, 1.2, 0, 0, 0, 0));
            p.Revolve(p.EndSketch(), false, "Piston Body");

            foreach (var zr in new[] { new[] { 13.0, 44.5 }, new[] { -44.5, -13.0 } })
            {
                p.BeginSketch(p.FrontPlane);
                p.PolyXY(L(V(-17, 19.5), V(-17, -17), V(17, -17), V(17, 19.5)), 0, R(0, 16.9, 16.9, 0));
                p.Extrude(p.EndSketch(), zr[0], zr[1], zr[0] > 0 ? "Pin Boss Front" : "Pin Boss Rear");
            }
            p.BeginSketch(p.FrontPlane);
            p.CircleXY(V(0, 0), 11.85);
            p.CutThroughBoth(p.EndSketch(), "Pin Bore");

            p.BeginSketch(p.FrontPlane);
            p.CenterLine(0, -60, 0, 0, -40, 0);
            foreach (var y in new[] { new[] { 27.0, 28.5 }, new[] { 22.5, 24.0 }, new[] { 16.5, 19.5 } })
                p.PolyXY(L(V(46.4, y[0]), V(50.5, y[0]), V(50.5, y[1]), V(46.4, y[1])));
            p.Revolve(p.EndSketch(), true, "Ring Grooves");

            p.BeginSketchOnFace(0, top, 0);
            p.Circle(-20, top, -12, 17);
            p.Circle(20, top, 12, 15);
            p.CutIntoFace(p.EndSketch(), 1.5, "Valve Reliefs");

            p.AxisFromPlanes("Pin Axis", p.TopPlane, p.RightPlane);
            p.AxisFromPlanes("Bore Axis", p.FrontPlane, p.RightPlane);
            p.SetColor(Cad.Color(0.84, 0.85, 0.87, 0.6));
            p.Report();
            string path = p.Save(); p.Close(); return path;
        }

        public static string WristPin()
        {
            var p = new PartBuilder("Wrist Pin");
            p.BeginSketch(p.FrontPlane);
            p.CircleXY(V(0, 0), 11.75);
            p.CircleXY(V(0, 0), 7);
            p.ExtrudeMid(p.EndSketch(), 60, "Pin");
            p.AxisFromPlanes("Pin Axis", p.TopPlane, p.RightPlane);
            p.SetColor(Cad.Color(0.78, 0.78, 0.80, 0.9));
            p.Report();
            string path = p.Save(); p.Close(); return path;
        }

        // ---------------------------------------------------------- connecting rod
        public static string ConnectingRod()
        {
            var p = new PartBuilder("Connecting Rod");
            double l = Spec.RodLen;
            p.BeginSketch(p.FrontPlane); p.CircleXY(V(0, 0), 36); p.ExtrudeMid(p.EndSketch(), 22, "Big End");
            p.BeginSketch(p.FrontPlane); p.CircleXY(V(0, l), 18); p.ExtrudeMid(p.EndSketch(), 22, "Small End");
            p.BeginSketch(p.FrontPlane);
            p.PolyXY(L(V(-15, 20), V(15, 20), V(10, l - 12), V(-10, l - 12)));
            p.ExtrudeMid(p.EndSketch(), 22, "Beam");
            p.BeginSketch(p.FrontPlane);
            p.PolyXY(L(V(26, -9), V(40, -9), V(40, 12), V(26, 12)), 3);
            p.PolyXY(L(V(-40, -9), V(-26, -9), V(-26, 12), V(-40, 12)), 3);
            p.ExtrudeMid(p.EndSketch(), 22, "Cap Bolt Bosses");
            foreach (var zr in new[] { new[] { 7.0, 12.0 }, new[] { -12.0, -7.0 } })
            {
                p.BeginSketch(p.FrontPlane);
                p.PolyXY(L(V(-8.5, 44), V(8.5, 44), V(5.5, l - 30), V(-5.5, l - 30)), 3);
                p.Cut(p.EndSketch(), zr[0], zr[1], zr[0] > 0 ? "Beam Pocket Front" : "Beam Pocket Rear");
            }
            p.BeginSketch(p.FrontPlane); p.CircleXY(V(0, 0), 26.75); p.CutThroughBoth(p.EndSketch(), "Big End Bore");
            p.BeginSketch(p.FrontPlane); p.CircleXY(V(0, l), 11.85); p.CutThroughBoth(p.EndSketch(), "Small End Bore");
            p.AxisFromPlanes("Big End Axis", p.TopPlane, p.RightPlane);
            p.Axis("Small End Axis", 0, l, -11, 0, l, 11);
            p.SetColor(Cad.Color(0.46, 0.47, 0.50, 0.7));
            p.Report();
            string path = p.Save(); p.Close(); return path;
        }

        // ------------------------------------------------------------ cylinder head
        public static string CylinderHead(int sg)
        {
            var p = new PartBuilder("Cylinder Head " + Spec.Side(sg));
            Func<double, double, V2> B = (a, b) => Spec.Bank(sg, a, b);
            double hh = Spec.HeadH;
            p.BeginSketch(p.FrontPlane);
            p.PolyXY(L(B(H, -W), B(H, W), B(H + hh, W), B(H + hh, -W)), 0, R(0, 0, 5, 5));
            p.Extrude(p.EndSketch(), Spec.HeadZ0(sg), Spec.HeadZ1(sg), "Head Casting");
            foreach (int n in Spec.BankCyls(sg))
            {
                double z = Spec.ZCyl(n);
                p.BeginSketch(p.FrontPlane);
                p.PolyXY(L(B(H + 30, -W - 1), B(H + 30, -W + 30), B(H + 70, -W + 30), B(H + 70, -W - 1)), 0, R(0, 6, 6, 0));
                p.Cut(p.EndSketch(), z - 14, z + 14, "Intake Port " + n);
                p.BeginSketch(p.FrontPlane);
                p.PolyXY(L(B(H + 32, W + 1), B(H + 32, W - 30), B(H + 68, W - 30), B(H + 68, W + 1)), 0, R(0, 8, 8, 0));
                p.Cut(p.EndSketch(), z - 16, z + 16, "Exhaust Port " + n);
            }
            p.SetColor(Cad.Color(0.78, 0.79, 0.81, 0.4));
            p.Report();
            string path = p.Save(); p.Close(); return path;
        }

        // -------------------------------------------------------------- valve cover
        public static string ValveCover(int sg)
        {
            var p = new PartBuilder("Valve Cover " + Spec.Side(sg));
            Func<double, double, V2> B = (a, b) => Spec.Bank(sg, a, b);
            double a0 = H + Spec.HeadH, a1 = a0 + Spec.CoverH;
            double z0 = Spec.HeadZ0(sg) + 8, z1 = Spec.HeadZ1(sg) - 8;
            p.BeginSketch(p.FrontPlane);
            p.PolyXY(L(B(a0, -66), B(a0, 66), B(a1, 66), B(a1, -66)), 0, R(0, 0, 22, 22));
            p.Extrude(p.EndSketch(), z0, z1, "Cover");
            p.BeginSketch(p.FrontPlane);
            p.PolyXY(L(B(a0, -69), B(a0, 69), B(a0 + 6, 69), B(a0 + 6, -69)));
            p.Extrude(p.EndSketch(), z0 - 3, z1 + 3, "Rail Flange");
            p.SetColor(Cad.Color(0.62, 0.05, 0.04, 0.9));
            if (sg > 0)
            {
                var top = B(a1, 0);
                p.BeginSketchOnFace(top.X, top.Y, -34);
                p.Circle(top.X, top.Y, -34, 22);
                var cap = p.ExtrudeFromFace(p.EndSketch(), 16, "Oil Filler Cap");
                p.SetFeatureColor(cap, Cad.Color(0.08, 0.08, 0.09, 0.6));
            }
            p.Report();
            string path = p.Save(); p.Close(); return path;
        }

        // ----------------------------------------------------- coils, leads, boots
        public static string Ignition(int sg)
        {
            var p = new PartBuilder("Ignition " + Spec.Side(sg));
            Func<double, double, V2> B = (a, b) => Spec.Bank(sg, a, b);
            double ht = H + Spec.HeadH + Spec.CoverH;
            p.SetColor(Cad.Color(0.10, 0.10, 0.11, 0.5));
            foreach (int n in Spec.BankCyls(sg))
            {
                double z = Spec.ZCyl(n);
                p.BeginSketch(p.FrontPlane);
                p.PolyXY(L(B(ht, -18), B(ht, 18), B(ht + 26, 18), B(ht + 26, -18)), 0, R(0, 0, 4, 4));
                p.Extrude(p.EndSketch(), z - 16, z + 36, "Coil " + n);

                var path2 = PartBuilder.Strip(L(V(ht + 14, 10), V(ht + 14, 80), V(H + 92, 86)), 4)
                    .Select(q => B(q.X, q.Y)).ToList();
                p.BeginSketch(p.FrontPlane);
                p.PolyXY(path2, 0, R(0, 6, 0, 0, 6, 0));
                var lead = p.Extrude(p.EndSketch(), z + 28, z + 36, "Plug Lead " + n);
                p.SetFeatureColor(lead, Cad.Color(0.78, 0.08, 0.06, 0.5));

                p.BeginSketch(p.FrontPlane);
                p.PolyXY(L(B(H + 84, 72), B(H + 84, 92), B(H + 100, 92), B(H + 100, 72)), 0, R(0, 4, 4, 0));
                p.Extrude(p.EndSketch(), z + 24, z + 40, "Plug Boot " + n);
            }
            p.Report();
            string path = p.Save(); p.Close(); return path;
        }

        // ---------------------------------------------------------- intake manifold
        public static string IntakeManifold()
        {
            var p = new PartBuilder("Intake Manifold");
            p.BeginSketch(p.FrontPlane);
            p.PolyXY(L(V(-70, 285), V(70, 285), V(70, 355), V(-70, 355)), 20);
            p.Extrude(p.EndSketch(), -375, 40, "Plenum");
            p.BeginSketch(p.FrontPlane);
            foreach (double x in new[] { -46.0, 0.0, 46.0 })
                p.PolyXY(L(V(x - 4, 350), V(x + 4, 350), V(x + 4, 362), V(x - 4, 362)), 0, R(0, 0, 3, 3));
            p.Extrude(p.EndSketch(), -360, 25, "Plenum Ribs");

            V2 railC = V(0, 0);
            for (int n = 1; n <= 8; n++)
            {
                int sg = Spec.Sigma(n); double z = Spec.ZCyl(n);
                V2 p0 = Spec.Bank(sg, H + 50, -W), p1 = p0 - Spec.P(sg) * 30, p2 = V(sg * 40, 318);
                p.BeginSketch(p.FrontPlane);
                p.PolyXY(PartBuilder.Strip(L(p0, p1, p2), 25), 0, R(0, 10, 0, 0, 10, 0));
                p.Extrude(p.EndSketch(), z - 18, z + 18, "Runner " + n);
                if (n == 1)
                {
                    V2 d2 = (p2 - p1).Norm, nrm = d2.Perp;
                    if (nrm.Y < 0) nrm = nrm * -1;
                    railC = p1 + (p2 - p1) * 0.4 + nrm * 30;
                }
            }
            p.BeginSketch(p.FrontPlane); p.CircleXY(V(0, 320), 44); p.Extrude(p.EndSketch(), 40, 48, "Throttle Flange");
            p.BeginSketch(p.FrontPlane); p.CircleXY(V(0, 320), 36); p.Extrude(p.EndSketch(), 48, 88, "Throttle Body");
            p.BeginSketch(p.FrontPlane); p.CircleXY(V(0, 320), 29); p.Cut(p.EndSketch(), 50, 90, "Throttle Bore");
            p.SetColor(Cad.Color(0.21, 0.21, 0.22, 0.3));
            p.BeginSketch(p.FrontPlane);
            p.CircleXY(railC, 8);
            p.CircleXY(V(-railC.X, railC.Y), 8);
            var rails = p.Extrude(p.EndSketch(), -360, 30, "Fuel Rails");
            p.SetFeatureColor(rails, Cad.Color(0.80, 0.81, 0.83, 0.9));
            p.Report();
            string path = p.Save(); p.Close(); return path;
        }

        // --------------------------------------------------------- exhaust manifold
        public static string ExhaustManifold(int sg)
        {
            var p = new PartBuilder("Exhaust Manifold " + Spec.Side(sg));
            Func<double, double, V2> B = (a, b) => Spec.Bank(sg, a, b);
            var cyls = Spec.BankCyls(sg);
            double zmin = cyls.Min(n => Spec.ZCyl(n)), zmax = cyls.Max(n => Spec.ZCyl(n));
            p.BeginSketch(p.FrontPlane);
            p.PolyXY(L(B(H + 20, W), B(H + 20, W + 8), B(H + 80, W + 8), B(H + 80, W)), 0, R(0, 3, 3, 0));
            p.Extrude(p.EndSketch(), zmin - 26, zmax + 26, "Head Flange");
            V2 e0 = B(H + 50, W), e2 = B(H + 50, W + 62);
            foreach (int n in cyls)
            {
                double z = Spec.ZCyl(n);
                p.BeginSketch(p.FrontPlane);
                p.PolyXY(PartBuilder.Strip(L(e0, e2), 20));
                p.Extrude(p.EndSketch(), z - 17, z + 17, "Primary " + n);
            }
            p.BeginSketch(p.FrontPlane); p.CircleXY(e2, 30);
            p.Extrude(p.EndSketch(), zmin - 20, zmax + 22, "Collector");
            double zo = zmin + 6;
            p.BeginSketch(p.TopPlane); p.Circle(e2.X, 0, zo, 26); p.Extrude(p.EndSketch(), -60, e2.Y, "Outlet Pipe");
            p.BeginSketch(p.TopPlane); p.Circle(e2.X, 0, zo, 42); p.Extrude(p.EndSketch(), -72, -60, "Outlet Flange");
            p.BeginSketch(p.TopPlane); p.Circle(e2.X, 0, zo, 20); p.Cut(p.EndSketch(), -73, e2.Y - 10, "Outlet Bore");
            p.SetColor(Cad.Color(0.30, 0.28, 0.27, 0.2));
            p.Report();
            string path = p.Save(); p.Close(); return path;
        }

        // ------------------------------------------------------------------ oil pan
        public static string OilPan()
        {
            var p = new PartBuilder("Oil Pan");
            Func<double, double, double[]> YZ = (y, z) => new double[] { 0, y, z };
            p.BeginSketch(p.RightPlane);
            p.Poly(new List<double[]> { YZ(-70, 72), YZ(-70, -406), YZ(-215, -406), YZ(-215, -230), YZ(-150, -150), YZ(-150, 72) },
                   0, R(0, 0, 20, 25, 25, 20));
            p.Extrude(p.EndSketch(), -106, 106, "Pan Body");
            p.BeginSketch(p.FrontPlane);
            p.PolyXY(L(V(-114, -78), V(114, -78), V(114, -70), V(-114, -70)));
            p.Extrude(p.EndSketch(), -406, 72, "Pan Rail");
            p.BeginSketch(p.RightPlane);
            p.Poly(new List<double[]> { YZ(-60, 66), YZ(-60, -400), YZ(-209, -400), YZ(-209, -232), YZ(-144, -152), YZ(-144, 66) },
                   0, R(0, 0, 14, 20, 20, 14));
            p.Cut(p.EndSketch(), -100, 100, "Sump");
            p.BeginSketch(p.TopPlane); p.Circle(0, 0, -380, 11);
            p.Extrude(p.EndSketch(), -225, -214, "Drain Plug");
            p.SetColor(Cad.Color(0.21, 0.21, 0.22, 0.5));
            // spin-on oil filter hanging under the front of the pan
            p.BeginSketch(p.TopPlane); p.Circle(-45, 0, 0, 42);
            p.Extrude(p.EndSketch(), -160, -149, "Filter Adapter");
            p.BeginSketch(p.TopPlane); p.Circle(-45, 0, 0, 38);
            var filter = p.Extrude(p.EndSketch(), -262, -160, "Oil Filter");
            p.SetFeatureColor(filter, Cad.Color(0.10, 0.28, 0.70, 0.7));
            p.Report();
            string path = p.Save(); p.Close(); return path;
        }

        // ------------------------------------------------------------- timing cover
        public static string TimingCover()
        {
            var p = new PartBuilder("Timing Cover");
            p.BeginSketch(p.FrontPlane);
            p.PolyXY(L(V(-85, -70), V(85, -70), V(95, 10), V(60, 95), V(-60, 95), V(-95, 10)), 0, R(6, 6, 18, 18, 18, 18));
            p.Extrude(p.EndSketch(), Spec.BlockFront, 90, "Cover");
            p.BeginSketch(p.FrontPlane);
            p.PolyXY(L(V(-30, 80), V(30, 80), V(30, 150), V(-30, 150)));
            p.Extrude(p.EndSketch(), Spec.BlockFront, 96, "Pump Web");
            p.BeginSketch(p.FrontPlane); p.CircleXY(V(0, 150), 45); p.Extrude(p.EndSketch(), Spec.BlockFront, 125, "Water Pump");
            p.BeginSketch(p.FrontPlane); p.CircleXY(V(0, 150), 40); p.Extrude(p.EndSketch(), 125, 137, "Pump Pulley");
            p.BeginSketch(p.FrontPlane); p.CircleXY(V(0, 0), 30); p.CutThroughBoth(p.EndSketch(), "Seal Bore");
            p.SetColor(Cad.Color(0.70, 0.71, 0.73, 0.4));
            p.Report();
            string path = p.Save(); p.Close(); return path;
        }

        // ------------------------------------------------------- harmonic balancer
        public static string Balancer()
        {
            var p = new PartBuilder("Harmonic Balancer");
            Func<double, double, double[]> YZ = (y, z) => new double[] { 0, y, z };
            p.BeginSketch(p.RightPlane);
            p.CenterLine(0, 0, 80, 0, 0, 90);
            p.Poly(new List<double[]> { YZ(25.05, 98), YZ(100, 98), YZ(100, 116), YZ(94, 116), YZ(94, 140), YZ(25.05, 140) },
                   0, R(0, 1.5, 1.5, 0, 1.5, 0));
            p.Revolve(p.EndSketch(), false, "Balancer");
            p.BeginSketch(p.RightPlane);
            p.CenterLine(0, 0, 80, 0, 0, 90);
            for (int i = 0; i < 6; i++)
            {
                double z = 119 + i * 3.4;
                p.Poly(new List<double[]> { YZ(96, z), YZ(91.5, z + 1.5), YZ(96, z + 3.0) });
            }
            p.Revolve(p.EndSketch(), true, "Belt Grooves");
            p.BeginSketch(p.RightPlane);
            p.CenterLine(0, 0, 80, 0, 0, 90);
            p.Poly(new List<double[]> { YZ(64, 137), YZ(67, 137), YZ(67, 141), YZ(64, 141) });
            p.Revolve(p.EndSketch(), true, "Isolator Groove");
            p.BeginSketch(p.FrontPlane); p.CircleXY(V(0, 0), 32); p.Extrude(p.EndSketch(), 140, 146, "Washer");
            p.BeginSketch(p.FrontPlane);
            var hex = new List<V2>();
            for (int i = 0; i < 6; i++) hex.Add(V(16 * Math.Cos(i * Math.PI / 3), 16 * Math.Sin(i * Math.PI / 3)));
            p.PolyXY(hex);
            p.Extrude(p.EndSketch(), 146, 156, "Crank Bolt");
            p.SetColor(Cad.Color(0.22, 0.22, 0.24, 0.6));
            p.Report();
            string path = p.Save(); p.Close(); return path;
        }

        // ----------------------------------------------------------------- flywheel
        public static string Flywheel()
        {
            var p = new PartBuilder("Flywheel");
            Func<double, double, double[]> YZ = (y, z) => new double[] { 0, y, z };
            p.BeginSketch(p.RightPlane);
            p.CenterLine(0, 0, -400, 0, 0, -410);
            p.Poly(new List<double[]> { YZ(30, -419), YZ(146, -419), YZ(146, -444), YZ(30, -444) }, 0, R(0, 2, 2, 0));
            p.Revolve(p.EndSketch(), false, "Plate");
            p.BeginSketch(p.RightPlane);
            p.CenterLine(0, 0, -400, 0, 0, -410);
            p.Poly(new List<double[]> { YZ(80, -441.5), YZ(136, -441.5), YZ(136, -446), YZ(80, -446) });
            p.Revolve(p.EndSketch(), true, "Clutch Face");

            p.BeginSketch(p.FrontPlane);
            int teeth = 140; double rr = 144, rt = 152, pitch = 2 * Math.PI / teeth;
            var gear = new List<V2>();
            for (int i = 0; i < teeth; i++)
            {
                double t = i * pitch;
                gear.Add(V(rr * Math.Cos(t), rr * Math.Sin(t)));
                gear.Add(V(rt * Math.Cos(t + 0.3 * pitch), rt * Math.Sin(t + 0.3 * pitch)));
                gear.Add(V(rt * Math.Cos(t + 0.5 * pitch), rt * Math.Sin(t + 0.5 * pitch)));
                gear.Add(V(rr * Math.Cos(t + 0.8 * pitch), rr * Math.Sin(t + 0.8 * pitch)));
            }
            p.PolyXY(gear);
            p.CircleXY(V(0, 0), 120);
            p.Extrude(p.EndSketch(), -440, -426, "Ring Gear");

            p.BeginSketch(p.FrontPlane);
            for (int i = 0; i < 6; i++) p.CircleXY(V(42 * Math.Cos(i * Math.PI / 3), 42 * Math.Sin(i * Math.PI / 3)), 5.5);
            p.CutThroughBoth(p.EndSketch(), "Crank Bolt Holes");
            p.SetColor(Cad.Color(0.52, 0.53, 0.55, 0.6));
            p.Report();
            string path = p.Save(); p.Close(); return path;
        }
    }
}
