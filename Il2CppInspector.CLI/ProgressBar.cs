using System.Diagnostics;
using System.Globalization;

namespace Il2CppInspector.CLI
{
    internal static class ProgressBar
    {
        private static readonly object Sync = new();
        private static readonly Stopwatch Elapsed = new();
        private static OperationProgress? active;
        private static int lastLineLength;
        private static int lastBucket = -2;
        private static long lastRefresh;
        private static long generation;
        private static bool running;

        public static void Update(OperationProgress progress)
        {
            lock (Sync)
            {
                if (active == null || active.Value.Label != progress.Label)
                {
                    FinishLine();
                    Elapsed.Restart();
                    lastBucket = -2;
                    lastRefresh = -100;
                }
                active = progress;
                bool complete = progress.Total >= 0 && progress.Current >= progress.Total;
                Draw(complete);
                if (complete && !running)
                    FinishLine();
            }
        }

        public static void WriteStatus(string message)
        {
            lock (Sync)
            {
                FinishLine();
                Console.WriteLine($"  {message}");
            }
        }

        public static void Fail()
        {
            lock (Sync)
            {
                if (active != null)
                {
                    running = false;
                    active = active.Value with { Detail = "failed" };
                    Draw(true);
                    FinishLine();
                }
            }
        }

        public static void Run(string label, Action<Action<OperationProgress>> action)
        {
            long id;
            lock (Sync)
            {
                FinishLine();
                running = true;
                id = ++generation;
                active = new OperationProgress(label, 0, -1);
                Elapsed.Restart();
                lastBucket = -2;
                lastRefresh = -100;
            }
            using var timer = new Timer(
                _ =>
                {
                    lock (Sync)
                        if (running && generation == id)
                            Draw(false);
                },
                null,
                100,
                100
            );
            try
            {
                action(Update);
                lock (Sync)
                {
                    var progress = active ?? new OperationProgress(label, 0, -1);
                    long total = Math.Max(0, progress.Total);
                    running = false;
                    Update(progress with { Current = total, Total = total, Detail = "done" });
                }
            }
            catch
            {
                Fail();
                throw;
            }
            finally
            {
                lock (Sync)
                {
                    running = false;
                    FinishLine();
                }
            }
        }

        public static void Run(string label, Action action) => Run(label, _ => action());

        internal static string Render(OperationProgress progress, TimeSpan elapsed, int width = int.MaxValue)
        {
            int percent = Percentage(progress);
            int barLength = Math.Clamp(width - 30, 5, 30);
            int filled = percent < 0 ? 0 : percent * barLength / 100;
            char[] bar = Enumerable.Repeat('-', barLength).ToArray();
            if (percent < 0)
            {
                int position = (int)(elapsed.TotalMilliseconds / 100 % (barLength - 2));
                Array.Fill(bar, '#', position, 3);
            }
            else
                Array.Fill(bar, '#', 0, filled);
            string suffix =
                $" [{new string(bar)}] {(percent < 0 ? " --" : percent.ToString(CultureInfo.InvariantCulture).PadLeft(3))}% ({elapsed.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture)}s)";
            string label = Fit(progress.Label, Math.Max(0, width - suffix.Length - 2));
            string line = "  " + label + suffix;
            if (!string.IsNullOrEmpty(progress.Detail))
                line += Fit(" " + progress.Detail, Math.Max(0, width - line.Length));
            return Fit(line, width);
        }

        private static int Percentage(OperationProgress progress) =>
            progress.Total < 0 ? -1
            : progress.Total == 0 ? 100
            : (int)(Math.Clamp((double)progress.Current / progress.Total, 0, 1) * 100);

        private static string Fit(string value, int width) =>
            value.Length <= width ? value
            : width <= 3 ? value[..width]
            : value[..(width - 3)] + "...";

        private static void Draw(bool force)
        {
            if (active == null)
                return;
            if (running && active.Value.Total >= 0 && active.Value.Current >= active.Value.Total)
                return;
            int bucket = Percentage(active.Value) / 10;
            if (Console.IsOutputRedirected)
            {
                if (!force && bucket == lastBucket)
                    return;
                Console.WriteLine(Render(active.Value, Elapsed.Elapsed));
            }
            else
            {
                if (!force && Elapsed.ElapsedMilliseconds - lastRefresh < 100)
                    return;
                int width = Console.WindowWidth > 1 ? Console.WindowWidth - 1 : 80;
                string line = Render(active.Value, Elapsed.Elapsed, width);
                Console.Write('\r' + line + new string(' ', Math.Max(0, Math.Min(lastLineLength, width) - line.Length)));
                lastLineLength = line.Length;
            }
            lastBucket = bucket;
            lastRefresh = Elapsed.ElapsedMilliseconds;
        }

        private static void FinishLine()
        {
            if (active != null && !Console.IsOutputRedirected)
                Console.WriteLine();
            active = null;
            lastLineLength = 0;
        }
    }
}
