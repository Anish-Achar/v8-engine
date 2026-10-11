using System;
using System.Collections.Generic;
using System.IO;
using SolidWorks.Interop.sldworks;

namespace V8
{
    static class Program
    {
        static readonly List<KeyValuePair<string, Func<string>>> PartBuilders = new List<KeyValuePair<string, Func<string>>>
        {
            P("block", Parts.Block),
            P("crank", Parts.Crankshaft),
            P("piston", Parts.Piston),
            P("pin", Parts.WristPin),
            P("rod", Parts.ConnectingRod),
            P("headL", () => Parts.CylinderHead(1)),
            P("headR", () => Parts.CylinderHead(-1)),
            P("coverL", () => Parts.ValveCover(1)),
            P("coverR", () => Parts.ValveCover(-1)),
            P("ignL", () => Parts.Ignition(1)),
            P("ignR", () => Parts.Ignition(-1)),
            P("intake", Parts.IntakeManifold),
            P("exhL", () => Parts.ExhaustManifold(1)),
            P("exhR", () => Parts.ExhaustManifold(-1)),
            P("pan", Parts.OilPan),
            P("timing", Parts.TimingCover),
            P("balancer", Parts.Balancer),
            P("flywheel", Parts.Flywheel),
        };

        static KeyValuePair<string, Func<string>> P(string k, Func<string> f) { return new KeyValuePair<string, Func<string>>(k, f); }

        [STAThread]
        static int Main(string[] args)
        {
            string outDir = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\.."));
            Cad.Connect(outDir);
            Cad.Log("Output folder: " + outDir);
            CloseStale(outDir);

            var targets = new List<string>(args.Length == 0 ? new[] { "all" } : args);
            int failures = 0;
            foreach (var kv in PartBuilders)
            {
                if (!(targets.Contains(kv.Key) || targets.Contains("parts") || targets.Contains("all"))) continue;
                Cad.Log("Building " + kv.Key + " ...");
                try { Cad.Log("  saved " + kv.Value()); }
                catch (Exception e) { failures++; Cad.Log("  FAILED: " + e.Message); CloseStale(outDir); }
            }
            if (targets.Contains("asm") || targets.Contains("all"))
            {
                try { EngineAssembly.Build(); }
                catch (Exception e) { failures++; Cad.Log("ASSEMBLY FAILED: " + e); }
            }
            // later steps work on the saved assembly; "all" runs the whole pipeline
            var steps = new List<KeyValuePair<string, Action>>
            {
                new KeyValuePair<string, Action>("verify", EngineAssembly.Verify),
                new KeyValuePair<string, Action>("images", EngineAssembly.Images),
                new KeyValuePair<string, Action>("gif", EngineAssembly.Gif),
                new KeyValuePair<string, Action>("motion", EngineAssembly.Motion),
                new KeyValuePair<string, Action>("diag", EngineAssembly.Diag),
                new KeyValuePair<string, Action>("web", WebExport.Run),
            };
            foreach (var st in steps)
            {
                if (!(targets.Contains(st.Key) || (targets.Contains("all") && st.Key != "diag" && st.Key != "web"))) continue;
                try { st.Value(); }
                catch (Exception e) { failures++; Cad.Log(st.Key.ToUpper() + " FAILED: " + e); }
            }
            Cad.Log(failures == 0 ? "Done." : "Finished with " + failures + " failure(s).");
            return failures;
        }

        // Close unsaved scratch documents and anything we are about to overwrite.
        public static void CloseStale(string outDir)
        {
            var docs = (object[])Cad.App.GetDocuments();
            if (docs == null) return;
            foreach (ModelDoc2 d in docs)
            {
                string path = d.GetPathName() ?? "";
                string title = d.GetTitle();
                bool scratch = path == "" && (title.StartsWith("Part") || title.StartsWith("Assem"));
                bool ours = path != "" && path.StartsWith(outDir, StringComparison.OrdinalIgnoreCase);
                if (scratch || ours) Cad.App.CloseDoc(title);
            }
        }
    }
}
