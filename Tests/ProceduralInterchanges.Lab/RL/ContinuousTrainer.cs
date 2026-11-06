using System;
using System.IO;
using System.Threading;
using ProceduralInterchanges.Core.Templates.RL;

namespace ProceduralInterchanges.Lab.RL
{
    public static class ContinuousTrainer
    {
        private static volatile bool s_IsRunning = false;
        private static Thread s_WorkerThread;
        private static readonly object s_Lock = new object();

        public static int CompletedCycles { get; private set; } = 0;
        public static string LastStatus { get; private set; } = "Idle";
        public static DateTime? LastCycleFinishedUtc { get; private set; }
        public static DateTime? StartedUtc { get; private set; }

        public static bool IsRunning => s_IsRunning;

        public static void Start()
        {
            lock (s_Lock)
            {
                if (s_IsRunning) return;
                s_IsRunning = true;
                StartedUtc = DateTime.UtcNow;
                s_WorkerThread = new Thread(WorkerLoop)
                {
                    IsBackground = true,
                    Name = "ContinuousAutoTrainer"
                };
                s_WorkerThread.Start();
                Console.WriteLine("[ContinuousTrainer] Background training loop started.");
            }
        }

        public static void Stop()
        {
            lock (s_Lock)
            {
                if (!s_IsRunning) return;
                s_IsRunning = false;
                LastStatus = "Stopping...";
                Console.WriteLine("[ContinuousTrainer] Background training loop requested to stop.");
            }
        }

        private static void WorkerLoop()
        {
            while (s_IsRunning)
            {
                int currentCycle = CompletedCycles + 1;
                LastStatus = $"Cycle {currentCycle} running";
                Console.WriteLine($"\n[ContinuousTrainer] >>> Commencing Auto-Training Cycle #{currentCycle} <<<");

                RlPolicyTrainer.TrainAllArchetypes(
                    outputPath: "Core/Templates/RL/LearnedPolicyWeights.cs",
                    log: msg =>
                    {
                        LastStatus = $"Cycle {currentCycle}: {msg}";
                    },
                    shouldContinue: () => s_IsRunning
                );

                if (!s_IsRunning) break;

                CompletedCycles = currentCycle;
                LastCycleFinishedUtc = DateTime.UtcNow;
                LastStatus = $"Cycle {CompletedCycles} completed at {DateTime.UtcNow:HH:mm:ss}";
                LogCycleEntry(CompletedCycles);
                Console.WriteLine($"[ContinuousTrainer] Finished Cycle #{CompletedCycles}. Waiting 3s before next cycle...\n");

                // Rest 3 seconds between continuous cycles, allowing responsive shutdown
                for (int i = 0; i < 30 && s_IsRunning; i++)
                {
                    Thread.Sleep(100);
                }
            }

            LastStatus = "Stopped";
            Console.WriteLine("[ContinuousTrainer] Background training loop safely terminated.");
        }

        private static void LogCycleEntry(int cycle)
        {
            try
            {
                string dir = "docs";
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                string logFile = Path.Combine(dir, "auto-training-history.jsonl");
                string line = $"{{\"cycle\":{cycle},\"timestampUtc\":\"{DateTime.UtcNow:o}\",\"status\":\"completed\"}}\n";
                File.AppendAllText(logFile, line);
            }
            catch
            {
                // Ignore file write errors
            }
        }
    }
}
