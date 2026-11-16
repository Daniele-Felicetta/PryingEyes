using System;
using UnityEngine;

namespace PryingEyes.Proc
{
    /// <summary>Launch flags that choose the theme of the floors, for photos, tests and the bots (the game itself draws it per floor):
    /// <c>-theme albergo|teatro|sanatorio|guardaroba</c> forces the theme of every grid floor, <c>-guardaroba N</c> sets how many floors
    /// out of a hundred, from the third up, are the Guardaroba (0 turns it off). The player reads them before the first scene; the
    /// editor tools (ProcCheck, ProcShots, ...) call <see cref="Apply"/> themselves because <c>-executeMethod</c> runs without the
    /// runtime initialisation of a player. <c>-survivallayout</c> is for those editor tools only: they build the plan of the candle
    /// cycle unless told otherwise, while the game's default cycle (waves) uses the survival layout; the player sets it per floor.</summary>
    public static class ThemeFlag
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot() => Apply();

        /// <summary>Reads -theme and -guardaroba from the command line; without them nothing changes.</summary>
        public static void Apply()
        {
            var args = Environment.GetCommandLineArgs();
            if (Array.IndexOf(args, "-survivallayout") >= 0) PlanGen.SurvivalLayout = true;
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-theme" && Enum.TryParse(args[i + 1], true, out Theme t)) PlanGen.ForceTheme = t;
                else if (args[i] == "-guardaroba" && int.TryParse(args[i + 1], out int percent)) PlanGen.GuardarobaPercent = Mathf.Clamp(percent, 0, 100);
            }
        }
    }
}
