using System;
using System.IO;
using ProceduralInterchanges.Lab.Cases;
using ProceduralInterchanges.Lab.Rendering;
using ProceduralInterchanges.Lab.RL;
using ProceduralInterchanges.Lab.Server;
using ProceduralInterchanges.Tests.Audit;

namespace ProceduralInterchanges.Lab
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            string outDir = "docs/lab-gallery";
            bool runTrain = false;
            bool runSandbox = false;
            bool continuous = false;
            int port = 5050;

            for (int i = 0; i < args.Length; i++)
            {
                if ((args[i] == "--out" || args[i] == "-o") && i + 1 < args.Length)
                {
                    outDir = args[++i];
                }
                else if (args[i] == "--train")
                {
                    runTrain = true;
                }
                else if (args[i] == "--continuous" || args[i] == "--auto-train" || args[i] == "--train-continuous")
                {
                    continuous = true;
                }
                else if (args[i] == "--sandbox")
                {
                    runSandbox = true;
                }
                else if (args[i] == "--port" && i + 1 < args.Length)
                {
                    int.TryParse(args[++i], out port);
                }
            }

            if (runTrain && continuous)
            {
                Console.WriteLine("=================================================");
                Console.WriteLine("  Continuous Auto-Trainer (Headless .NET 8)      ");
                Console.WriteLine("=================================================");
                ContinuousTrainer.Start();
                while (ContinuousTrainer.IsRunning)
                {
                    System.Threading.Thread.Sleep(1000);
                }
                return 0;
            }

            if (runTrain)
            {
                RlPolicyTrainer.TrainAllArchetypes();
                return 0;
            }

            if (runSandbox)
            {
                if (continuous)
                {
                    ContinuousTrainer.Start();
                }
                SandboxServer.Start(port);
                return 0;
            }

            Console.WriteLine("=================================================");
            Console.WriteLine("   Procedural Interchanges Lab (Headless .NET 8) ");
            Console.WriteLine("=================================================");
            Console.WriteLine($"Output directory: {outDir}\n");

            Directory.CreateDirectory(outDir);

            // Generate standalone HTML sandbox
            string sandboxHtmlPath = Path.Combine(outDir, "sandbox.html");
            SandboxHtmlGenerator.WriteToFile(sandboxHtmlPath);
            Console.WriteLine($"Generated interactive sandbox at: {Path.GetFullPath(sandboxHtmlPath)}");

            var cards = LabCases.RunAllCases();
            int passedCount = 0;

            for (int i = 0; i < cards.Count; i++)
            {
                var card = cards[i];
                bool passed = card.Verdict != null && card.Verdict.Passed;
                if (passed) passedCount++;

                string status = passed ? "[PASS]" : "[AUDIT ISSUES]";
                Console.WriteLine($"{status,-16} {card.Archetype,-12} {card.Variant,-24} ({card.ExecutionTimeMs:F1} ms, {card.Verdict?.Metrics.TotalLengthMeters:F0} m)");

                // Save standalone SVG
                string safeName = $"{card.Archetype.ToLowerInvariant()}_{card.Variant.ToLowerInvariant().Replace(' ', '_').Replace('°', 'd')}.svg";
                string svgPath = Path.Combine(outDir, safeName);
                File.WriteAllText(svgPath, card.SvgContent);
            }

            // Save index.html gallery
            string html = HtmlGalleryGenerator.GenerateGalleryHtml(cards);
            string htmlPath = Path.Combine(outDir, "index.html");
            File.WriteAllText(htmlPath, html);

            Console.WriteLine("\n-------------------------------------------------");
            Console.WriteLine($"Summary: {passedCount}/{cards.Count} Passed Audit");
            Console.WriteLine($"HTML Gallery generated at: {Path.GetFullPath(htmlPath)}");
            Console.WriteLine("=================================================");

            return 0;
        }
    }
}
