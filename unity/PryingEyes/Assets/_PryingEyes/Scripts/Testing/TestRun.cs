using System;
using System.IO;

namespace PryingEyes.Testing
{
    /// <summary>What the game may ask about a run, and the only part of the test harness that production code may name. It is compiled
    /// into every build on purpose: <c>GameManager</c> asks <see cref="Dedicated"/> and names no test type and no test flag. What answers
    /// it is <c>PlayerTest</c>, which a distribution build leaves out. Design: docs/engineering/agent-tooling.md, "The in-player harness".</summary>
    public static class TestRun
    {
        /// <summary>A test on the harness owns this process, so the automatic run of <c>-autotest</c> must not close the game by itself.
        /// <c>PlayerTest.InstallWhen</c> sets it when the flag of the test is on the command line; nothing else does.</summary>
        public static bool Dedicated;

        /// <summary>The folder a test writes into, created when it is missing: its <c>result.json</c> and its photographs. It is the folder
        /// that <c>-testout DIR</c> names (how a runner collects them) or, without that flag, <c>unity_logs/&lt;folder&gt;</c> of the checkout
        /// the game runs in, found like <c>hotel_build/</c> (see <see cref="TestPaths"/>) and not relative to the working directory.</summary>
        public static string OutDir(string folder)
        {
            string given = Value("-testout");
            string dir = string.IsNullOrEmpty(given) ? TestPaths.LogFolder(folder) : Path.GetFullPath(given);
            Directory.CreateDirectory(dir);
            return dir;
        }

        /// <summary>Whether the flag is on the command line.</summary>
        internal static bool Has(string flag)
        {
            return Array.IndexOf(Environment.GetCommandLineArgs(), flag) >= 0;
        }

        /// <summary>The word after the flag, or null when the flag is missing or last.</summary>
        internal static string Value(string flag)
        {
            string[] args = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(args, flag);
            return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
        }
    }
}
