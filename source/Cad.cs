// Thin helper layer over the SOLIDWORKS API.
// All public coordinates are in millimetres, in the part's model space.
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace V8
{
    public struct V2
    {
        public double X, Y;
        public V2(double x, double y) { X = x; Y = y; }
        public static V2 operator +(V2 a, V2 b) { return new V2(a.X + b.X, a.Y + b.Y); }
        public static V2 operator -(V2 a, V2 b) { return new V2(a.X - b.X, a.Y - b.Y); }
        public static V2 operator *(V2 a, double s) { return new V2(a.X * s, a.Y * s); }
        public static V2 operator *(double s, V2 a) { return new V2(a.X * s, a.Y * s); }
        public double Len { get { return Math.Sqrt(X * X + Y * Y); } }
        public V2 Norm { get { double l = Len; return new V2(X / l, Y / l); } }
        public V2 Perp { get { return new V2(-Y, X); } } // rotate +90 deg
        public static double Dot(V2 a, V2 b) { return a.X * b.X + a.Y * b.Y; }
        public static double Cross(V2 a, V2 b) { return a.X * b.Y - a.Y * b.X; }
        public override string ToString() { return string.Format("({0:F2}, {1:F2})", X, Y); }
    }

    public static class Cad
    {
        public const double MM = 0.001;
        public static SldWorks App;
        public static MathUtility MU;
        public static string PartTemplate, AsmTemplate, OutDir;

        public static void Connect(string outDir)
        {
            App = (SldWorks)Marshal.GetActiveObject("SldWorks.Application");
            MU = (MathUtility)App.GetMathUtility();
            string tdir = App.GetUserPreferenceStringValue((int)swUserPreferenceStringValue_e.swFileLocationsDocumentTemplates).Split(';')[0];
            PartTemplate = App.GetUserPreferenceStringValue((int)swUserPreferenceStringValue_e.swDefaultTemplatePart);
            AsmTemplate = App.GetUserPreferenceStringValue((int)swUserPreferenceStringValue_e.swDefaultTemplateAssembly);
            if (string.IsNullOrEmpty(PartTemplate) || !File.Exists(PartTemplate))
                PartTemplate = Path.Combine(tdir, "MBD", "part 0251mm to 1000mm.prtdot");
            if (string.IsNullOrEmpty(AsmTemplate) || !File.Exists(AsmTemplate))
                AsmTemplate = Path.Combine(tdir, "MBD", "assembly 0251mm to 1000mm.asmdot");
            App.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swInputDimValOnCreate, false);
            OutDir = outDir;
            Directory.CreateDirectory(OutDir);
        }

        public static double[] Color(double r, double g, double b, double shine = 0.4, double transparency = 0)
        {
            // R, G, B, Ambient, Diffuse, Specular, Shininess, Transparency, Emission
            return new double[] { r, g, b, 1.0, 1.0, 0.6, shine, transparency, 0.0 };
        }

        public static void Log(string msg) { Console.WriteLine(msg); }
    }

    public class PartBuilder
    {
        const double MM = Cad.MM;
        public ModelDoc2 Doc;
        public PartDoc Part;
        public FeatureManager FM;
        public SketchManager SM;
        public ModelDocExtension Ext;
        public Feature FrontPlane, TopPlane, RightPlane;
        public string Name;
        MathTransform m2s;
        int sketchCount;

        public PartBuilder(string name)
        {
            Name = name;
            int err = 0;
            Doc = (ModelDoc2)Cad.App.NewDocument(Cad.PartTemplate, 0, 0, 0);
            if (Doc == null) throw new Exception("Could not create part from template " + Cad.PartTemplate);
            Part = (PartDoc)Doc;
            FM = Doc.FeatureManager;
            SM = Doc.SketchManager;
            Ext = Doc.Extension;
            Ext.SetUserPreferenceInteger((int)swUserPreferenceIntegerValue_e.swUnitSystem, 0, (int)swUnitSystem_e.swUnitSystem_MMGS);
            var planes = new List<Feature>();
            Feature f = (Feature)Doc.FirstFeature();
            while (f != null)
            {
                if (f.GetTypeName2() == "RefPlane") planes.Add(f);
                f = (Feature)f.GetNextFeature();
            }
            FrontPlane = planes[0]; TopPlane = planes[1]; RightPlane = planes[2];
            SM.AddToDB = true;
            SM.DisplayWhenAdded = false;
            var view = Doc.ActiveView as ModelView;
            if (view != null) view.EnableGraphicsUpdate = false;
            err++;
        }

        // ---------- sketching ----------
        public void BeginSketch(Feature plane)
        {
            Doc.ClearSelection2(true);
            if (!plane.Select2(false, 0)) throw new Exception(Name + ": could not select plane " + plane.Name);
            SM.InsertSketch(true);
            InitXform();
        }

        public void BeginSketchOnFace(double x, double y, double z)
        {
            Doc.ClearSelection2(true);
            var face = FaceAt(x, y, z);
            if (face == null || !((Entity)face).Select4(false, null))
                throw new Exception(Name + string.Format(": no face at ({0},{1},{2})", x, y, z));
            SM.InsertSketch(true);
            InitXform();
        }

        // Finds the planar face that contains the model point (view independent).
        public Face2 FaceAt(double x, double y, double z)
        {
            var bodies = (object[])Part.GetBodies2((int)swBodyType_e.swSolidBody, true);
            Face2 best = null; double bestD = 1e-5;
            foreach (Body2 b in bodies)
            {
                var faces = (object[])b.GetFaces();
                foreach (Face2 f in faces)
                {
                    var s = (Surface)f.GetSurface();
                    if (!s.IsPlane()) continue;
                    var c = (double[])f.GetClosestPointOn(x * MM, y * MM, z * MM);
                    if (c == null) continue;
                    double d = Math.Sqrt(Math.Pow(c[0] - x * MM, 2) + Math.Pow(c[1] - y * MM, 2) + Math.Pow(c[2] - z * MM, 2));
                    if (d < bestD) { bestD = d; best = f; }
                }
            }
            return best;
        }

        void InitXform()
        {
            var sk = SM.ActiveSketch;
            if (sk == null) throw new Exception(Name + ": sketch did not open");
            m2s = sk.ModelToSketchTransform;
        }

        // model point (mm) -> sketch point (m)
        public V2 S(double x, double y, double z)
        {
            var p = (MathPoint)Cad.MU.CreatePoint(new double[] { x * MM, y * MM, z * MM });
            p = (MathPoint)p.MultiplyTransform(m2s);
            var a = (double[])p.ArrayData;
            return new V2(a[0], a[1]);
        }

        public Feature EndSketch()
        {
            SM.InsertSketch(true);
            var f = (Feature)Doc.FeatureByPositionReverse(0);
            sketchCount++;
            return f;
        }

        public void LineS(V2 a, V2 b) { SM.CreateLine(a.X, a.Y, 0, b.X, b.Y, 0); }

        // Front-plane (XY) primitives, mm
        public void LineXY(V2 a, V2 b) { LineS(S(a.X, a.Y, 0), S(b.X, b.Y, 0)); }
        public void ArcXY(V2 c, V2 s, V2 e, int dir)
        {
            V2 cc = S(c.X, c.Y, 0), ss = S(s.X, s.Y, 0), ee = S(e.X, e.Y, 0);
            SM.CreateArc(cc.X, cc.Y, 0, ss.X, ss.Y, 0, ee.X, ee.Y, 0, (short)dir);
        }
        public void CircleXY(V2 c, double r) { Circle(c.X, c.Y, 0, r); }

        public void Circle(double x, double y, double z, double r)
        {
            var c = S(x, y, z);
            SM.CreateCircleByRadius(c.X, c.Y, 0, r * MM);
        }

        public void CenterLine(double x1, double y1, double z1, double x2, double y2, double z2)
        {
            var a = S(x1, y1, z1); var b = S(x2, y2, z2);
            SM.CreateCenterLine(a.X, a.Y, 0, b.X, b.Y, 0);
        }

        // Closed polygon through model points, optional corner radius (mm) applied at every corner
        // (or per-corner radii). Rounding happens in sketch space so plane orientation never matters.
        public void Poly(IList<double[]> pts3, double radius = 0, double[] radii = null)
        {
            int n = pts3.Count;
            var p = new V2[n];
            for (int i = 0; i < n; i++) p[i] = S(pts3[i][0], pts3[i][1], pts3[i][2]);
            var rr = new double[n];
            for (int i = 0; i < n; i++) rr[i] = (radii != null ? radii[i] : radius) * MM;

            var tIn = new V2[n]; var tOut = new V2[n]; var center = new V2[n]; var dirs = new int[n]; var rounded = new bool[n];
            for (int i = 0; i < n; i++)
            {
                V2 prev = p[(i - 1 + n) % n], cur = p[i], next = p[(i + 1) % n];
                V2 dIn = (cur - prev).Norm, dOut = (next - cur).Norm;
                double cosT = Math.Max(-1, Math.Min(1, V2.Dot(dIn * -1, dOut)));
                double theta = Math.Acos(cosT); // interior angle
                double r = rr[i];
                if (r <= 0 || theta < 1e-3 || Math.PI - theta < 1e-3) { tIn[i] = cur; tOut[i] = cur; continue; }
                double t = r / Math.Tan(theta / 2);
                double maxT = 0.45 * Math.Min((cur - prev).Len, (next - cur).Len);
                if (t > maxT) { t = maxT; r = t * Math.Tan(theta / 2); }
                tIn[i] = cur - dIn * t;
                tOut[i] = cur + dOut * t;
                V2 bis = ((dIn * -1) + dOut).Norm;
                center[i] = cur + bis * (r / Math.Sin(theta / 2));
                dirs[i] = V2.Cross(dIn, dOut) > 0 ? 1 : -1;
                rounded[i] = true;
            }
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                if (rounded[i])
                    SM.CreateArc(center[i].X, center[i].Y, 0, tIn[i].X, tIn[i].Y, 0, tOut[i].X, tOut[i].Y, 0, (short)dirs[i]);
                if ((tOut[i] - tIn[j]).Len > 1e-9)
                    LineS(tOut[i], tIn[j]);
            }
        }

        // Front-plane (XY) convenience: 2D points at z = 0
        public void PolyXY(IList<V2> pts, double radius = 0, double[] radii = null)
        {
            var l = new List<double[]>();
            foreach (var q in pts) l.Add(new double[] { q.X, q.Y, 0 });
            Poly(l, radius, radii);
        }

        // Outline of a thick polyline (strip) with half-width h, mitred joins.
        public static List<V2> Strip(IList<V2> c, double h)
        {
            int n = c.Count;
            var left = new List<V2>(); var right = new List<V2>();
            for (int i = 0; i < n; i++)
            {
                V2 nrm;
                if (i == 0) nrm = (c[1] - c[0]).Norm.Perp;
                else if (i == n - 1) nrm = (c[n - 1] - c[n - 2]).Norm.Perp;
                else
                {
                    V2 n1 = (c[i] - c[i - 1]).Norm.Perp, n2 = (c[i + 1] - c[i]).Norm.Perp;
                    V2 m = (n1 + n2).Norm;
                    nrm = m * (1.0 / V2.Dot(m, n1));
                }
                left.Add(c[i] + nrm * h);
                right.Add(c[i] - nrm * h);
            }
            right.Reverse();
            left.AddRange(right);
            return left;
        }

        // ---------- features ----------
        void SelectSketch(Feature sketch)
        {
            Doc.ClearSelection2(true);
            if (!sketch.Select2(false, 0)) throw new Exception(Name + ": could not select " + sketch.Name);
        }

        // Extrude from s0 to s1 (mm) measured along the sketch plane's normal.
        public Feature Extrude(Feature sketch, double s0, double s1, string label, bool merge = true)
        {
            SelectSketch(sketch);
            bool off = Math.Abs(s0) > 1e-9;
            double depth = s1 - s0;
            var f = FM.FeatureExtrusion3(true, false, depth < 0,
                (int)swEndConditions_e.swEndCondBlind, 0, Math.Abs(depth) * MM, 0,
                false, false, false, false, 0, 0, false, false, false, false,
                merge, false, true,
                off ? (int)swStartConditions_e.swStartOffset : (int)swStartConditions_e.swStartSketchPlane,
                Math.Abs(s0) * MM, s0 < 0);
            if (f == null) throw new Exception(Name + ": extrude failed: " + label);
            f.Name = label;
            return f;
        }

        public Feature ExtrudeMid(Feature sketch, double total, string label)
        {
            SelectSketch(sketch);
            var f = FM.FeatureExtrusion3(true, false, false,
                (int)swEndConditions_e.swEndCondMidPlane, 0, total * MM, 0,
                false, false, false, false, 0, 0, false, false, false, false,
                true, false, true, (int)swStartConditions_e.swStartSketchPlane, 0, false);
            if (f == null) throw new Exception(Name + ": mid-plane extrude failed: " + label);
            f.Name = label;
            return f;
        }

        public Feature Cut(Feature sketch, double s0, double s1, string label)
        {
            SelectSketch(sketch);
            bool off = Math.Abs(s0) > 1e-9;
            double depth = s1 - s0;
            // a cut's default direction is opposite the sketch normal, so flip for positive ranges
            var f = FM.FeatureCut4(true, false, depth > 0,
                (int)swEndConditions_e.swEndCondBlind, 0, Math.Abs(depth) * MM, 0,
                false, false, false, false, 0, 0, false, false, false, false,
                false, false, true, true, true, false,
                off ? (int)swStartConditions_e.swStartOffset : (int)swStartConditions_e.swStartSketchPlane,
                Math.Abs(s0) * MM, s0 < 0, false);
            if (f == null) throw new Exception(Name + ": cut failed: " + label);
            f.Name = label;
            return f;
        }

        // Extrude a face sketch outward; retries in the other direction if nothing was added.
        public Feature ExtrudeFromFace(Feature sketch, double depth, string label)
        {
            double v0 = VolumeCm3();
            for (int attempt = 0; attempt < 2; attempt++)
            {
                SelectSketch(sketch);
                var f = FM.FeatureExtrusion3(true, false, attempt == 1,
                    (int)swEndConditions_e.swEndCondBlind, 0, depth * MM, 0,
                    false, false, false, false, 0, 0, false, false, false, false,
                    true, false, true, (int)swStartConditions_e.swStartSketchPlane, 0, false);
                if (f != null && VolumeCm3() - v0 > 1e-4) { f.Name = label; return f; }
                if (f != null)
                {
                    Doc.ClearSelection2(true);
                    f.Select2(false, 0);
                    Ext.DeleteSelection2(0);
                }
            }
            throw new Exception(Name + ": face extrude failed: " + label);
        }

        // Cut from a face sketch into the material, blind depth.
        public Feature CutIntoFace(Feature sketch, double depth, string label, bool reverse = false)
        {
            SelectSketch(sketch);
            var f = FM.FeatureCut4(true, false, reverse,
                (int)swEndConditions_e.swEndCondBlind, 0, depth * MM, 0,
                false, false, false, false, 0, 0, false, false, false, false,
                false, false, true, true, true, false,
                (int)swStartConditions_e.swStartSketchPlane, 0, false, false);
            if (f == null) throw new Exception(Name + ": face cut failed: " + label);
            f.Name = label;
            return f;
        }

        public Feature CutThroughBoth(Feature sketch, string label)
        {
            SelectSketch(sketch);
            var f = FM.FeatureCut4(false, false, false,
                (int)swEndConditions_e.swEndCondThroughAll, (int)swEndConditions_e.swEndCondThroughAll, 0, 0,
                false, false, false, false, 0, 0, false, false, false, false,
                false, false, true, true, true, false,
                (int)swStartConditions_e.swStartSketchPlane, 0, false, false);
            if (f == null) throw new Exception(Name + ": through-all cut failed: " + label);
            f.Name = label;
            return f;
        }

        public Feature Revolve(Feature sketch, bool cut, string label)
        {
            SelectSketch(sketch);
            var f = FM.FeatureRevolve2(true, true, false, cut, false, false,
                (int)swEndConditions_e.swEndCondBlind, 0, 2 * Math.PI, 0,
                false, false, 0, 0, 0, 0, 0, true, false, true);
            if (f == null) throw new Exception(Name + ": revolve failed: " + label);
            f.Name = label;
            return f;
        }

        // Reference axis along a 3D line (mm)
        public Feature Axis(string name, double x1, double y1, double z1, double x2, double y2, double z2)
        {
            Doc.ClearSelection2(true);
            SM.Insert3DSketch(true);
            var seg = SM.CreateLine(x1 * MM, y1 * MM, z1 * MM, x2 * MM, y2 * MM, z2 * MM);
            SM.Insert3DSketch(true);
            var sk = (Feature)Doc.FeatureByPositionReverse(0);
            sk.Name = name + " Line";
            Doc.ClearSelection2(true);
            if (!seg.Select4(false, null)) throw new Exception(Name + ": could not select axis line " + name);
            if (!Doc.InsertAxis2(true)) throw new Exception(Name + ": axis failed " + name);
            var ax = (Feature)Doc.FeatureByPositionReverse(0);
            ax.Name = name;
            Doc.ClearSelection2(true);
            sk.Select2(false, 0);
            Doc.BlankSketch();
            Doc.ClearSelection2(true);
            return ax;
        }

        public Feature AxisFromPlanes(string name, Feature p1, Feature p2)
        {
            Doc.ClearSelection2(true);
            p1.Select2(false, 0); p2.Select2(true, 0);
            if (!Doc.InsertAxis2(true)) throw new Exception(Name + ": axis failed " + name);
            var ax = (Feature)Doc.FeatureByPositionReverse(0);
            ax.Name = name;
            Doc.ClearSelection2(true);
            return ax;
        }

        // Plane parallel to 'basePlane' at signed distance d (mm) along its normal.
        public Feature OffsetPlane(string name, Feature basePlane, double d)
        {
            Doc.ClearSelection2(true);
            basePlane.Select2(false, 0);
            int c = (int)swRefPlaneReferenceConstraints_e.swRefPlaneReferenceConstraint_Distance;
            if (d < 0) c |= (int)swRefPlaneReferenceConstraints_e.swRefPlaneReferenceConstraint_OptionFlip;
            var rp = FM.InsertRefPlane(c, Math.Abs(d) * MM, 0, 0, 0, 0);
            if (rp == null) throw new Exception(Name + ": plane failed " + name);
            var f = (Feature)Doc.FeatureByPositionReverse(0);
            f.Name = name;
            Doc.ClearSelection2(true);
            return f;
        }

        public static double[] PlaneOrigin(Feature plane)
        {
            var rp = (RefPlane)plane.GetSpecificFeature2();
            var t = (double[])rp.Transform.ArrayData;
            return new double[] { t[9] / MM, t[10] / MM, t[11] / MM };
        }

        public void HideAllPlanesAndAxes()
        {
            Feature f = (Feature)Doc.FirstFeature();
            while (f != null)
            {
                string t = f.GetTypeName2();
                if (t == "RefPlane" || t == "RefAxis")
                {
                    Doc.ClearSelection2(true);
                    f.Select2(false, 0);
                    if (t == "RefPlane") Doc.BlankRefGeom(); else Doc.BlankRefGeom();
                }
                f = (Feature)f.GetNextFeature();
            }
            Doc.ClearSelection2(true);
        }

        // ---------- appearance / io ----------
        public void SetColor(double[] mat)
        {
            Ext.SetMaterialPropertyValues(mat, (int)swInConfigurationOpts_e.swAllConfiguration, null);
        }

        public void SetFeatureColor(Feature f, double[] mat)
        {
            f.SetMaterialPropertyValues2(mat, (int)swInConfigurationOpts_e.swAllConfiguration, null);
        }

        public void SetMaterial(string material)
        {
            try { Part.SetMaterialPropertyName2("", "SOLIDWORKS Materials", material); }
            catch (Exception e) { Cad.Log("  (material '" + material + "' not applied: " + e.Message + ")"); }
        }

        public double[] BBox()
        {
            var bodies = (object[])Part.GetBodies2((int)swBodyType_e.swSolidBody, true);
            double[] bb = null;
            if (bodies == null) return null;
            foreach (Body2 b in bodies)
            {
                var x = (double[])b.GetBodyBox();
                if (bb == null) bb = (double[])x.Clone();
                else { for (int i = 0; i < 3; i++) { bb[i] = Math.Min(bb[i], x[i]); bb[i + 3] = Math.Max(bb[i + 3], x[i + 3]); } }
            }
            for (int i = 0; i < 6; i++) bb[i] /= MM;
            return bb;
        }

        public int BodyCount()
        {
            var bodies = (object[])Part.GetBodies2((int)swBodyType_e.swSolidBody, true);
            return bodies == null ? 0 : bodies.Length;
        }

        public double VolumeCm3()
        {
            var mp = (MassProperty)Ext.CreateMassProperty();
            return mp == null ? 0 : mp.Volume * 1e6;
        }

        public string Save()
        {
            string path = Path.Combine(Cad.OutDir, Name + ".SLDPRT");
            SM.AddToDB = false;
            SM.DisplayWhenAdded = true;
            var view = Doc.ActiveView as ModelView;
            if (view != null) view.EnableGraphicsUpdate = true;
            HideAllPlanesAndAxes();
            Doc.ForceRebuild3(false);
            Doc.ShowNamedView2("*Isometric", 7);
            Doc.ViewZoomtofit2();
            int err = 0, warn = 0;
            if (File.Exists(path)) File.Delete(path);
            bool ok = Ext.SaveAs3(path, (int)swSaveAsVersion_e.swSaveAsCurrentVersion,
                (int)swSaveAsOptions_e.swSaveAsOptions_Silent, null, null, ref err, ref warn);
            if (!ok) throw new Exception(Name + ": save failed err=" + err + " warn=" + warn);
            return path;
        }

        public void SaveImage(string path)
        {
            int err = 0, warn = 0;
            Ext.SaveAs3(path, (int)swSaveAsVersion_e.swSaveAsCurrentVersion,
                (int)swSaveAsOptions_e.swSaveAsOptions_Silent, null, null, ref err, ref warn);
        }

        public void Close() { Cad.App.CloseDoc(Doc.GetTitle()); }

        public void Report()
        {
            var bb = BBox();
            Cad.Log(string.Format("  {0}: bodies={1} vol={2:F1} cm3 bbox x[{3:F1},{4:F1}] y[{5:F1},{6:F1}] z[{7:F1},{8:F1}]",
                Name, BodyCount(), VolumeCm3(), bb[0], bb[3], bb[1], bb[4], bb[2], bb[5]));
        }
    }
}
