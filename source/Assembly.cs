using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace V8
{
    public static class EngineAssembly
    {
        const double MM = Cad.MM;
        const string AsmFile = "V8 Engine.SLDASM";
        static ModelDoc2 Doc;
        static AssemblyDoc Asm;
        static string AsmName;

        static readonly string[] StaticParts =
        {
            "Cylinder Head Left", "Cylinder Head Right", "Valve Cover Left", "Valve Cover Right",
            "Ignition Left", "Ignition Right", "Intake Manifold", "Exhaust Manifold Left", "Exhaust Manifold Right",
            "Oil Pan", "Timing Cover",
        };

        static string PartPath(string name) { return Path.Combine(Cad.OutDir, name + ".SLDPRT"); }

        public static void Build()
        {
            Cad.Log("Building assembly ...");
            var all = new List<string> { "Engine Block", "Crankshaft", "Piston", "Wrist Pin", "Connecting Rod", "Harmonic Balancer", "Flywheel" };
            all.AddRange(StaticParts);
            foreach (var n in all)
                if (!File.Exists(PartPath(n))) throw new Exception("missing part file: " + n);

            Cad.App.DocumentVisible(false, (int)swDocumentTypes_e.swDocPART);
            foreach (var n in all)
            {
                int e = 0, w = 0;
                Cad.App.OpenDoc6(PartPath(n), (int)swDocumentTypes_e.swDocPART, (int)swOpenDocOptions_e.swOpenDocOptions_Silent, "", ref e, ref w);
            }
            Cad.App.DocumentVisible(true, (int)swDocumentTypes_e.swDocPART);

            Doc = (ModelDoc2)Cad.App.NewDocument(Cad.AsmTemplate, 0, 0, 0);
            if (Doc == null) throw new Exception("could not create assembly from " + Cad.AsmTemplate);
            Asm = (AssemblyDoc)Doc;
            Doc.Extension.SetUserPreferenceInteger((int)swUserPreferenceIntegerValue_e.swUnitSystem, 0, (int)swUnitSystem_e.swUnitSystem_MMGS);
            SaveAsm();
            AsmName = Path.GetFileNameWithoutExtension(Doc.GetPathName());

            var block = Add("Engine Block");
            Fix(block);
            var crank = Add("Crankshaft");
            Mate(Ent("Main Axis", crank), "AXIS", Ent("Crank Axis", block), "AXIS", swMateType_e.swMateCOINCIDENT);
            Mate(Ent("Front Plane", crank), "PLANE", Ent("Front Plane", block), "PLANE", swMateType_e.swMateCOINCIDENT);

            var z3 = new double[] { 0, 0, 1 };
            for (int n = 1; n <= 8; n++)
            {
                int sg = Spec.Sigma(n), k = Spec.ThrowOf(n);
                double zc = Spec.ZCyl(n);
                V2 u = Spec.U(sg);
                V2 pinPos = u * Spec.PistonPinDist(n, 0);
                V2 crankPin = Spec.CrankPin(k, 0);
                V2 rodDir = (pinPos - crankPin).Norm;

                var piston = Add("Piston");
                Place(piston, new[] { u.Y, -u.X, 0 }, new[] { u.X, u.Y, 0 }, z3, new[] { pinPos.X, pinPos.Y, zc });
                var pin = Add("Wrist Pin");
                Place(pin, new[] { u.Y, -u.X, 0 }, new[] { u.X, u.Y, 0 }, z3, new[] { pinPos.X, pinPos.Y, zc });
                var rod = Add("Connecting Rod");
                Place(rod, new[] { rodDir.Y, -rodDir.X, 0 }, new[] { rodDir.X, rodDir.Y, 0 }, z3, new[] { crankPin.X, crankPin.Y, zc });

                Mate(Ent("Bore Axis", piston), "AXIS", Ent("Bore Axis " + n, block), "AXIS", swMateType_e.swMateCOINCIDENT);
                Mate(Ent("Front Plane", piston), "PLANE", Ent("Cylinder Plane " + n, block), "PLANE", swMateType_e.swMateCOINCIDENT);
                Mate(pin.Name2 + "@" + AsmName, "COMPONENT", piston.Name2 + "@" + AsmName, "COMPONENT", swMateType_e.swMateLOCK);
                Mate(Ent("Small End Axis", rod), "AXIS", Ent("Pin Axis", piston), "AXIS", swMateType_e.swMateCOINCIDENT);
                Mate(Ent("Front Plane", rod), "PLANE", Ent("Front Plane", piston), "PLANE", swMateType_e.swMateCOINCIDENT);
                Mate(Ent("Big End Axis", rod), "AXIS", Ent("Pin Axis " + (k + 1), crank), "AXIS", swMateType_e.swMateCOINCIDENT);
                Cad.Log("  cylinder " + n + " mated");
            }

            foreach (var name in new[] { "Harmonic Balancer", "Flywheel" })
            {
                var c = Add(name);
                Mate(c.Name2 + "@" + AsmName, "COMPONENT", crank.Name2 + "@" + AsmName, "COMPONENT", swMateType_e.swMateLOCK);
            }
            foreach (var name in StaticParts) Fix(Add(name));

            // see-through block so the moving parts are visible
            block.SetMaterialPropertyValues2(Cad.Color(0.62, 0.64, 0.67, 0.3, 0.55), (int)swInConfigurationOpts_e.swThisConfiguration, null);

            var ext = Doc.Extension;
            ext.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swDisplayPlanes, 0, false);
            ext.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swDisplayAxes, 0, false);
            ext.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swDisplayOrigins, 0, false);
            ext.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swDisplaySketches, 0, false);
            ext.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swDisplayTemporaryAxes, 0, false);

            Doc.ForceRebuild3(false);
            ReportProblems();
            CheckPistons(0);
            Doc.ShowNamedView2("*Isometric", 7);
            Doc.ViewZoomtofit2();
            SaveAsm();
            Cad.Log("Assembly saved: " + Doc.GetPathName());
        }

        // ---------------------------------------------------------------- helpers
        static Component2 Add(string part)
        {
            var c = Asm.AddComponent5(PartPath(part), (int)swAddComponentConfigOptions_e.swAddComponentConfigOptions_CurrentSelectedConfig,
                "", false, "", 0, 0, 0);
            if (c == null) throw new Exception("could not insert " + part);
            // AddComponent5 centres the part's bounding box on the given point; put the part origin
            // on the assembly origin instead, since static parts are modelled in engine coordinates.
            Place(c, new double[] { 1, 0, 0 }, new double[] { 0, 1, 0 }, new double[] { 0, 0, 1 }, new double[] { 0, 0, 0 });
            return c;
        }

        static void Fix(Component2 c)
        {
            Doc.ClearSelection2(true);
            c.Select4(false, null, false);
            Asm.FixComponent();
            Doc.ClearSelection2(true);
        }

        static string Ent(string feature, Component2 c) { return feature + "@" + c.Name2 + "@" + AsmName; }

        static void Place(Component2 c, double[] x, double[] y, double[] z, double[] originMm)
        {
            var mu = Cad.MU;
            var t = (MathTransform)mu.ComposeTransform(
                (MathVector)mu.CreateVector(x), (MathVector)mu.CreateVector(y), (MathVector)mu.CreateVector(z),
                (MathVector)mu.CreateVector(new[] { originMm[0] * MM, originMm[1] * MM, originMm[2] * MM }), 1.0);
            c.Transform2 = t;
        }

        static Mate2 Mate(string n1, string t1, string n2, string t2, swMateType_e type, double angle = 0, bool flip = false)
        {
            Doc.ClearSelection2(true);
            if (!Doc.Extension.SelectByID2(n1, t1, 0, 0, 0, false, 1, null, 0)) throw new Exception("cannot select " + n1);
            if (!Doc.Extension.SelectByID2(n2, t2, 0, 0, 0, true, 1, null, 0)) throw new Exception("cannot select " + n2);
            int err = 0;
            var m = Asm.AddMate5((int)type, (int)swMateAlign_e.swMateAlignCLOSEST, flip, 0, 0, 0, 1, 1, angle, 0, 0, false, false, 0, out err);
            Doc.ClearSelection2(true);
            if (m == null || err != (int)swAddMateError_e.swAddMateError_NoError)
                throw new Exception("mate " + type + " failed (" + err + "): " + n1 + " / " + n2);
            return m;
        }

        static void SaveAsm()
        {
            string path = Path.Combine(Cad.OutDir, AsmFile);
            int err = 0, warn = 0;
            bool ok = Doc.GetPathName() == ""
                ? Doc.Extension.SaveAs3(path, (int)swSaveAsVersion_e.swSaveAsCurrentVersion, (int)swSaveAsOptions_e.swSaveAsOptions_Silent, null, null, ref err, ref warn)
                : Doc.Save3((int)swSaveAsOptions_e.swSaveAsOptions_Silent, ref err, ref warn);
            if (!ok) throw new Exception("assembly save failed err=" + err + " warn=" + warn);
        }

        static void ReportProblems()
        {
            int count = Doc.Extension.GetWhatsWrongCount();
            if (count == 0) { Cad.Log("  rebuild: no errors"); return; }
            object feats, codes, warns;
            Doc.Extension.GetWhatsWrong(out feats, out codes, out warns);
            var f = (object[])feats; var c = (int[])codes; var w = (bool[])warns;
            for (int i = 0; i < count; i++)
                Cad.Log(string.Format("  {0}: {1} code {2}", w[i] ? "warning" : "ERROR", ((Feature)f[i]).Name, c[i]));
        }

        static Component2 Comp(string name)
        {
            var c = Asm.GetComponentByName(name);
            if (c == null) throw new Exception("component not found: " + name);
            return (Component2)c;
        }

        static double[] ToAsm(Component2 c, double x, double y, double z)
        {
            var p = (MathPoint)Cad.MU.CreatePoint(new[] { x * MM, y * MM, z * MM });
            p = (MathPoint)p.MultiplyTransform(c.Transform2);
            var a = (double[])p.ArrayData;
            return new[] { a[0] / MM, a[1] / MM, a[2] / MM };
        }

        // Compares every piston's pin position against the slider-crank equation
        // for the crank angle the crankshaft is actually sitting at.
        static double CheckPistons(double expectDeg)
        {
            var crank = Comp("Crankshaft-1");
            var p1 = ToAsm(crank, 0, Spec.Throw, 0);
            double theta = Math.Atan2(p1[0], p1[1]) * 180 / Math.PI;
            double worst = 0;
            var line = new System.Text.StringBuilder();
            for (int n = 1; n <= 8; n++)
            {
                var piston = Comp("Piston-" + n);
                var o = ToAsm(piston, 0, 0, 0);
                V2 u = Spec.U(Spec.Sigma(n));
                double actual = V2.Dot(new V2(o[0], o[1]), u);
                double expected = Spec.PistonPinDist(n, theta);
                double offAxis = Math.Abs(V2.Cross(u, new V2(o[0], o[1]))) + Math.Abs(o[2] - Spec.ZCyl(n));
                worst = Math.Max(worst, Math.Abs(actual - expected) + offAxis);
                line.AppendFormat(" {0}:{1:F1}", n, actual - Spec.Deck + Spec.CompHt);
            }
            Cad.Log(string.Format("  crank at {0,7:F1} deg | piston crown vs deck (mm){1} | max error {2:F4} mm", theta, line, worst));
            return worst;
        }

        static void OpenAsm()
        {
            if (Doc != null) return;
            string path = Path.Combine(Cad.OutDir, AsmFile);
            int e = 0, w = 0;
            Doc = Cad.App.OpenDoc6(path, (int)swDocumentTypes_e.swDocASSEMBLY, (int)swOpenDocOptions_e.swOpenDocOptions_Silent, "", ref e, ref w);
            if (Doc == null) throw new Exception("could not open assembly (" + e + ")");
            int ae = 0;
            Cad.App.ActivateDoc3(Doc.GetTitle(), false, 0, ref ae);
            Asm = (AssemblyDoc)Doc;
            AsmName = Path.GetFileNameWithoutExtension(Doc.GetPathName());
        }

        // Turns the crank through a full revolution with a temporary angle mate,
        // checking piston kinematics and interference along the way.
        public static void Verify()
        {
            OpenAsm();
            Cad.Log("Verifying motion ...");
            double worst = 0;
            foreach (double deg in new double[] { 0, 30, 45, 60, 90, 120, 135, 150, 179, 0 })
            {
                SetCrank(deg);
                worst = Math.Max(worst, CheckPistons(deg));
                if (deg == 45 || deg == 135) Interference();
            }
            Doc.ForceRebuild3(false);
            ReportProblems();
            Cad.Log(string.Format("Motion check: worst piston position error {0:F4} mm", worst));
            SaveAsm();
        }

        // Rotates the crank to an angle with a temporary angle mate, then removes the mate
        // (the crank stays put but is free to turn again).
        static void SetCrank(double deg)
        {
            deg = ((deg % 360) + 360) % 360;
            double d2 = 360 - deg;
            var tries = new[] { Tuple.Create(deg, false), Tuple.Create(deg, true), Tuple.Create(d2, false), Tuple.Create(d2, true) };
            double actual = double.NaN;
            foreach (var t in tries)
            {
                if (t.Item1 > 180.0001) continue;
                var m = Mate(Ent("Top Plane", Comp("Crankshaft-1")), "PLANE", Ent("Top Plane", Comp("Engine Block-1")), "PLANE",
                             swMateType_e.swMateANGLE, t.Item1 * Math.PI / 180, t.Item2);
                Doc.ForceRebuild3(false);
                Doc.ClearSelection2(true);
                ((Feature)m).Select2(false, 0);
                Doc.Extension.DeleteSelection2(0);
                Doc.ClearSelection2(true);
                actual = CrankAngle();
                double diff = Math.Abs(((actual - deg) % 360 + 540) % 360 - 180);
                if (diff < 0.5) return;
            }
            Cad.Log(string.Format("    (crank asked for {0:F1} deg, reached {1:F1} deg)", deg, actual));
        }

        static double CrankAngle()
        {
            var p1 = ToAsm(Comp("Crankshaft-1"), 0, Spec.Throw, 0);
            return (Math.Atan2(p1[0], p1[1]) * 180 / Math.PI + 360) % 360;
        }

        // Renders one revolution of the rotating assembly and stitches it into an animated GIF.
        public static void Gif()
        {
            OpenAsm();
            string dir = Path.Combine(Cad.OutDir, "Renders");
            string frameDir = Path.Combine(dir, "frames");
            Directory.CreateDirectory(frameDir);
            foreach (var old in Directory.GetFiles(frameDir, "*.jpg")) File.Delete(old);
            var hide = new[] { "Engine Block-1", "Cylinder Head Left-1", "Cylinder Head Right-1", "Valve Cover Left-1", "Valve Cover Right-1",
                               "Ignition Left-1", "Ignition Right-1", "Intake Manifold-1", "Exhaust Manifold Left-1", "Exhaust Manifold Right-1",
                               "Oil Pan-1", "Timing Cover-1" };
            foreach (var h in hide) Comp(h).Visible = (int)swComponentVisibilityState_e.swComponentHidden;
            SetCrank(0);
            Doc.ShowNamedView2("*Isometric", 7);
            Doc.ViewZoomtofit2();
            Doc.ViewZoomout();
            var frames = new List<string>();
            const int steps = 24;
            for (int i = 0; i < steps; i++)
            {
                SetCrank(i * 360.0 / steps);
                Doc.GraphicsRedraw2();
                string f = Path.Combine(frameDir, string.Format("frame{0:D2}.jpg", i));
                int err = 0, warn = 0;
                Doc.Extension.SaveAs3(f, (int)swSaveAsVersion_e.swSaveAsCurrentVersion,
                    (int)swSaveAsOptions_e.swSaveAsOptions_Silent | (int)swSaveAsOptions_e.swSaveAsOptions_Copy, null, null, ref err, ref warn);
                frames.Add(f);
            }
            SetCrank(0);
            foreach (var h in hide) Comp(h).Visible = (int)swComponentVisibilityState_e.swComponentVisible;
            Doc.ShowNamedView2("*Isometric", 7);
            Doc.ViewZoomtofit2();
            SaveAsm();
            string gif = Path.Combine(dir, "V8 - running.gif");
            GifWriter.Write(frames, gif, 6, 640);
            Cad.Log("  wrote " + gif + " (" + new FileInfo(gif).Length / 1024 + " KB)");
        }

        static void Interference()
        {
            var idm = Asm.InterferenceDetectionManager;
            idm.TreatCoincidenceAsInterference = false;
            idm.TreatSubAssembliesAsComponents = true;
            idm.IncludeMultibodyPartInterferences = false;
            idm.MakeInterferingPartsTransparent = false;
            idm.CreateFastenersFolder = false;
            idm.IgnoreHiddenBodies = true;
            idm.ShowIgnoredInterferences = false;
            idm.UseTransform = true;
            var list = (object[])idm.GetInterferences();
            int n = list == null ? 0 : list.Length;
            Cad.Log("    interference check: " + n + " clash(es)");
            if (list != null)
                foreach (IInterference it in list)
                {
                    var comps = ((object[])it.Components).Cast<Component2>().Select(c => c.Name2);
                    Cad.Log(string.Format("      {0}  vol {1:F1} mm3", string.Join(" x ", comps), it.Volume * 1e9));
                }
            idm.Done();
        }

        // Animation study with a constant-speed rotary motor on the crank snout: press Play to run the engine.
        public static void Motion()
        {
            OpenAsm();
            Cad.Log("Creating motion study ...");
            var msm = (SolidWorks.Interop.swmotionstudy.MotionStudyManager)Doc.Extension.GetMotionStudyManager();
            var names = (object[])msm.GetMotionStudyNames();
            SolidWorks.Interop.swmotionstudy.MotionStudy ms = null;
            if (names != null)
                foreach (string n in names) if (n == "Engine Running") ms = msm.GetMotionStudy(n);
            if (ms == null)
            {
                ms = (names != null && names.Length > 0) ? msm.GetMotionStudy((string)names[0]) : msm.CreateMotionStudy();
                ms.Name = "Engine Running";
            }
            ms.Activate();
            ms.StudyType = (int)SolidWorks.Interop.swmotionstudy.swMotionStudyType_e.swMotionStudyTypeAssembly;
            if (ms.GetMotionFeaturesCount() > 0) Cad.Log("  study already has motion features; keeping them");
            else
            {
                Cad.Log(string.Format("  study '{0}' active={1} type={2}", ms.Name, ms.IsActive, ms.StudyType));
                var snout = CrankFace(25);
                Doc.ClearSelection2(true);
                ((Entity)snout).Select4(false, null);
                var selected = ((SelectionMgr)Doc.SelectionManager).GetSelectedObject6(1, -1);
                var data = (SimulationMotorFeatureData)ms.CreateDefinition((int)swFeatureNameID_e.swFmAEMRotationalMotor);
                if (data == null) throw new Exception("no motor definition");
                data.Location = selected;
                data.DirectionReference = selected;
                data.MotionType = (int)swSimulationMotorMotionType_e.swSimulationMotorMotion_Constant;
                data.ConstantSpeedMotor(2 * Math.PI);   // rad/s -> 60 rpm, slow enough to watch
                var f = ms.CreateFeature(data);
                Doc.ClearSelection2(true);
                if (f == null) throw new Exception("motor feature not created (motor type " + data.MotorType + ")");
                Cad.Log(string.Format("  rotary motor added (velocity value {0})", data.Velocity));
            }
            ms.SetDuration(5);
            bool ok = ms.Calculate();
            Cad.Log("  calculate: " + ok);
            foreach (double t in new[] { 0.0, 0.25, 0.5 })
            {
                ms.SetTime(t);
                var p1 = ToAsm(Comp("Crankshaft-1"), 0, Spec.Throw, 0);
                Cad.Log(string.Format("  t={0:F2}s crank angle {1:F1} deg", t, Math.Atan2(p1[0], p1[1]) * 180 / Math.PI));
            }
            ms.SetTime(0);
            SaveAsm();
        }

        static Face2 CrankFace(double radiusMm)
        {
            var crank = Comp("Crankshaft-1");
            object info;
            var bodies = (object[])crank.GetBodies3((int)swBodyType_e.swSolidBody, out info);
            foreach (Body2 b in bodies)
                foreach (Face2 f in (object[])b.GetFaces())
                {
                    var s = (Surface)f.GetSurface();
                    if (!s.IsCylinder()) continue;
                    var cp = (double[])s.CylinderParams;
                    if (Math.Abs(cp[6] / MM - radiusMm) < 0.01) return f;
                }
            throw new Exception("no crank face with radius " + radiusMm);
        }

        public static void Diag()
        {
            OpenAsm();
            foreach (var name in new[] { "Engine Block-1", "Crankshaft-1", "Piston-1", "Connecting Rod-1", "Cylinder Head Left-1", "Oil Pan-1", "Flywheel-1" })
            {
                var t = (double[])Comp(name).Transform2.ArrayData;
                Cad.Log(string.Format("  {0,-22} R=[{1:F3} {2:F3} {3:F3} | {4:F3} {5:F3} {6:F3} | {7:F3} {8:F3} {9:F3}] T=({10:F1}, {11:F1}, {12:F1})",
                    name, t[0], t[1], t[2], t[3], t[4], t[5], t[6], t[7], t[8], t[9] / MM, t[10] / MM, t[11] / MM));
            }
        }

        public static void Images()
        {
            OpenAsm();
            string dir = Path.Combine(Cad.OutDir, "Renders");
            Directory.CreateDirectory(dir);
            Doc.ViewDisplayShaded();
            Snap("*Isometric", Path.Combine(dir, "V8 - isometric.jpg"));
            Snap("*Front", Path.Combine(dir, "V8 - front.jpg"));
            Snap("*Right", Path.Combine(dir, "V8 - side.jpg"));
            Snap("*Top", Path.Combine(dir, "V8 - top.jpg"));
            // internals only
            var hide = new[] { "Engine Block-1", "Cylinder Head Left-1", "Cylinder Head Right-1", "Valve Cover Left-1", "Valve Cover Right-1",
                               "Ignition Left-1", "Ignition Right-1", "Intake Manifold-1", "Exhaust Manifold Left-1", "Exhaust Manifold Right-1",
                               "Oil Pan-1", "Timing Cover-1" };
            foreach (var h in hide) Comp(h).Visible = (int)swComponentVisibilityState_e.swComponentHidden;
            Snap("*Isometric", Path.Combine(dir, "V8 - rotating assembly.jpg"));
            Snap("*Front", Path.Combine(dir, "V8 - rotating assembly front.jpg"));
            foreach (var h in hide) Comp(h).Visible = (int)swComponentVisibilityState_e.swComponentVisible;
            Doc.ShowNamedView2("*Isometric", 7);
            Doc.ViewZoomtofit2();
            SaveAsm();
        }

        static void Snap(string view, string file)
        {
            Doc.ShowNamedView2(view, -1);
            Doc.ViewZoomtofit2();
            Doc.GraphicsRedraw2();
            int err = 0, warn = 0;
            if (File.Exists(file)) File.Delete(file);
            bool ok = Doc.Extension.SaveAs3(file, (int)swSaveAsVersion_e.swSaveAsCurrentVersion, (int)swSaveAsOptions_e.swSaveAsOptions_Silent | (int)swSaveAsOptions_e.swSaveAsOptions_Copy, null, null, ref err, ref warn);
            Cad.Log("  image " + Path.GetFileName(file) + (ok ? " ok" : " FAILED err=" + err));
        }
    }
}
