// Exports every unique part for the website's three.js viewer: triangle meshes grouped by colour,
// the part's real CAD edges as line segments, and a few stats. Coordinates are millimetres in each
// part's own frame (static parts are already in engine coordinates). Writes Web\v8.json + Web\v8.bin.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace V8
{
    public static class WebExport
    {
        const double MM = Cad.MM;

        static readonly string[] Names =
        {
            "Engine Block", "Crankshaft", "Piston", "Wrist Pin", "Connecting Rod", "Harmonic Balancer", "Flywheel",
            "Cylinder Head Left", "Cylinder Head Right", "Valve Cover Left", "Valve Cover Right",
            "Ignition Left", "Ignition Right", "Intake Manifold", "Exhaust Manifold Left", "Exhaust Manifold Right",
            "Oil Pan", "Timing Cover",
        };

        static readonly HashSet<string> SolidFeatureTypes = new HashSet<string> { "Extrusion", "Cut", "Revolution", "RevCut", "ICE", "Boss", "BossThin", "CutThin" };

        class Group
        {
            public double[] Mat;
            public List<float> P = new List<float>(), N = new List<float>();
            public List<int> I = new List<int>();
        }

        static CultureInfo Inv = CultureInfo.InvariantCulture;
        static string F(double v) { return v.ToString("0.####", Inv); }

        public static void Run()
        {
            string dir = Path.Combine(Cad.OutDir, "Web");
            Directory.CreateDirectory(dir);
            Cad.Log("Exporting web meshes ...");
            var bin = new MemoryStream();
            var bw = new BinaryWriter(bin);
            var parts = new List<string>();
            foreach (var name in Names)
            {
                int e = 0, w = 0;
                var doc = Cad.App.OpenDoc6(Path.Combine(Cad.OutDir, name + ".SLDPRT"), (int)swDocumentTypes_e.swDocPART,
                    (int)swOpenDocOptions_e.swOpenDocOptions_Silent, "", ref e, ref w);
                if (doc == null) throw new Exception("could not open " + name + " (" + e + ")");
                int ae = 0;
                Cad.App.ActivateDoc3(doc.GetTitle(), false, 0, ref ae);
                doc.ViewDisplayShaded();
                doc.GraphicsRedraw2();

                var partMat = (double[])doc.MaterialPropertyValues;
                var groups = new List<Group>();
                var edges = new List<float>();
                int faces = 0, tris = 0, edgeCount = 0;
                foreach (Body2 body in (object[])((PartDoc)doc).GetBodies2((int)swBodyType_e.swSolidBody, true))
                {
                    foreach (Face2 face in (object[])body.GetFaces())
                    {
                        faces++;
                        var mat = FaceMat(face) ?? partMat;
                        var g = groups.FirstOrDefault(x => SameMat(x.Mat, mat));
                        if (g == null) { g = new Group { Mat = mat }; groups.Add(g); }
                        tris += AddFace(face, g);
                    }
                    var box = (double[])body.GetBodyBox();
                    foreach (Edge edge in (object[])body.GetEdges())
                    {
                        try { AddEdge(edge, box, edges); edgeCount++; }
                        catch (Exception ex) { Cad.Log("    edge skipped: " + ex.Message); }
                    }
                }

                var mp = (MassProperty)doc.Extension.CreateMassProperty();
                double volume = mp == null ? 0 : mp.Volume * 1e6, area = mp == null ? 0 : mp.SurfaceArea * 1e4;
                var features = new List<string>();
                var types = new SortedSet<string>();
                for (var f = (Feature)doc.FirstFeature(); f != null; f = (Feature)f.GetNextFeature())
                {
                    types.Add(f.GetTypeName2());
                    if (SolidFeatureTypes.Contains(f.GetTypeName2())) features.Add(f.Name);
                }

                var sb = new StringBuilder();
                sb.Append("{\"name\":\"").Append(name).Append("\",\"faces\":").Append(faces).Append(",\"tris\":").Append(tris)
                  .Append(",\"edgeCount\":").Append(edgeCount)
                  .Append(",\"volumeCm3\":").Append(F(volume)).Append(",\"areaCm2\":").Append(F(area))
                  .Append(",\"features\":[").Append(string.Join(",", features.Select(x => "\"" + x.Replace("\"", "'") + "\""))).Append("]")
                  .Append(",\"groups\":[");
                for (int gi = 0; gi < groups.Count; gi++)
                {
                    var g = groups[gi];
                    int nv = g.P.Count / 3;
                    bool wide = nv > 65535;
                    long pOff = Align(bw); foreach (var v in g.P) bw.Write(v);
                    long nOff = Align(bw); foreach (var v in g.N) bw.Write((sbyte)Math.Max(-127, Math.Min(127, Math.Round(v * 127))));
                    long iOff = Align(bw); foreach (var i in g.I) { if (wide) bw.Write((uint)i); else bw.Write((ushort)i); }
                    if (gi > 0) sb.Append(",");
                    sb.Append("{\"rgb\":[").Append(F(g.Mat[0])).Append(",").Append(F(g.Mat[1])).Append(",").Append(F(g.Mat[2]))
                      .Append("],\"shine\":").Append(F(g.Mat[6])).Append(",\"alpha\":").Append(F(1 - g.Mat[7]))
                      .Append(",\"nv\":").Append(nv).Append(",\"ni\":").Append(g.I.Count).Append(",\"wide\":").Append(wide ? "true" : "false")
                      .Append(",\"pos\":").Append(pOff).Append(",\"nrm\":").Append(nOff).Append(",\"idx\":").Append(iOff).Append("}");
                }
                long eOff = Align(bw);
                foreach (var v in edges) bw.Write(v);
                sb.Append("],\"edges\":{\"off\":").Append(eOff).Append(",\"n\":").Append(edges.Count / 3).Append("}}");
                parts.Add(sb.ToString());
                Cad.Log(string.Format("  {0,-24} {1,4} faces {2,6} tris {3,5} edges ({7} rejected)  {4} colour group(s)  {5,7:F1} cm3  features {6}",
                    name, faces, tris, edgeCount, groups.Count, volume, features.Count, Rejected));
                Rejected = 0;
                if (features.Count == 0) Cad.Log("    feature types seen: " + string.Join(", ", types));
                Cad.App.CloseDoc(doc.GetTitle());
            }
            Align(bw);
            bw.Flush();
            File.WriteAllBytes(Path.Combine(dir, "v8.bin"), bin.ToArray());
            File.WriteAllText(Path.Combine(dir, "v8.json"), "{\"unit\":\"mm\",\"parts\":[\n" + string.Join(",\n", parts) + "\n]}\n");
            Cad.Log(string.Format("  wrote Web\\v8.bin ({0} KB) and Web\\v8.json", bin.Length / 1024));
        }

        static long Align(BinaryWriter bw)
        {
            while (bw.BaseStream.Position % 4 != 0) bw.Write((byte)0);
            return bw.BaseStream.Position;
        }

        static double[] FaceMat(Face2 face)
        {
            var m = face.MaterialPropertyValues as double[];
            if (Valid(m)) return m;
            var f = face.GetFeature() as Feature;
            if (f != null)
            {
                m = f.GetMaterialPropertyValues2((int)swInConfigurationOpts_e.swThisConfiguration, null) as double[];
                if (Valid(m)) return m;
            }
            return null;
        }

        static bool Valid(double[] m) { return m != null && m.Length >= 9 && m[0] >= 0; }

        static bool SameMat(double[] a, double[] b)
        {
            for (int i = 0; i < 9; i++) if (Math.Abs(a[i] - b[i]) > 1e-6) return false;
            return true;
        }

        // Face tessellation comes as a triangle soup; merge repeated vertices within the face.
        static int AddFace(Face2 face, Group g)
        {
            var t = face.GetTessTriangles(true) as float[];
            var n = face.GetTessNorms() as float[];
            if (t == null || n == null) return 0;
            var seen = new Dictionary<Tuple<float, float, float, float, float, float>, int>();
            for (int v = 0; v < t.Length / 3; v++)
            {
                var key = Tuple.Create(t[3 * v], t[3 * v + 1], t[3 * v + 2], n[3 * v], n[3 * v + 1], n[3 * v + 2]);
                int idx;
                if (!seen.TryGetValue(key, out idx))
                {
                    idx = g.P.Count / 3;
                    seen[key] = idx;
                    g.P.Add(t[3 * v] * 1000f); g.P.Add(t[3 * v + 1] * 1000f); g.P.Add(t[3 * v + 2] * 1000f);
                    g.N.Add(n[3 * v]); g.N.Add(n[3 * v + 1]); g.N.Add(n[3 * v + 2]);
                }
                g.I.Add(idx);
            }
            return t.Length / 9;
        }

        // Appends the edge as line segments (mm). Lines use their real end points; open curves are
        // tessellated between their end points; closed curves (full circles) are sampled over their
        // parameter range. Anything that strays outside the body's box is rejected as bad data.
        static int Rejected;
        static void AddEdge(Edge edge, double[] box, List<float> o)
        {
            var cp = (double[])edge.GetCurveParams2();
            var c = (Curve)edge.GetCurve();
            var s = new[] { cp[0], cp[1], cp[2] };
            var e = new[] { cp[3], cp[4], cp[5] };
            var pts = new List<double[]>();
            bool closed = Dist(s, e) < 1e-7;
            if (c.IsLine() && !closed) { pts.Add(s); pts.Add(e); }
            else if (!closed)
            {
                var t = (double[])c.GetTessPts(0.05 * MM, 3 * MM, s, e);
                for (int i = 0; t != null && i + 2 < t.Length; i += 3) pts.Add(new[] { t[i], t[i + 1], t[i + 2] });
                if (pts.Count < 2 || Dist(pts[0], s) > 0.05 * MM) { pts.Clear(); pts.Add(s); pts.Add(e); }
            }
            else
            {
                double t0 = cp[6], t1 = cp[7];
                double len = c.GetLength3(t0, t1) / MM;
                int steps = Math.Max(24, Math.Min(128, (int)Math.Ceiling(len / 3.0)));
                for (int i = 0; i <= steps; i++)
                {
                    var r = (double[])c.Evaluate2(t0 + (t1 - t0) * i / steps, 0);
                    pts.Add(new[] { r[0], r[1], r[2] });
                }
            }
            double tol = 1 * MM;
            foreach (var p in pts)
                for (int k = 0; k < 3; k++)
                    if (p[k] < box[k] - tol || p[k] > box[k + 3] + tol) { Rejected++; return; }
            for (int i = 1; i < pts.Count; i++)
            {
                o.Add((float)(pts[i - 1][0] / MM)); o.Add((float)(pts[i - 1][1] / MM)); o.Add((float)(pts[i - 1][2] / MM));
                o.Add((float)(pts[i][0] / MM)); o.Add((float)(pts[i][1] / MM)); o.Add((float)(pts[i][2] / MM));
            }
        }

        static double Dist(double[] a, double[] b)
        {
            return Math.Sqrt((a[0] - b[0]) * (a[0] - b[0]) + (a[1] - b[1]) * (a[1] - b[1]) + (a[2] - b[2]) * (a[2] - b[2]));
        }
    }
}
