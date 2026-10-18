using System;
using System.IO;
using NekoRayNT5.Services;
using NekoRayNT5.App;

namespace NekoRayNT5.Core
{
    // Versions 0.9.x had a TUN mode (TAP-Windows adapter + nt5helper "tun"). It was removed; after an
    // update this only stops a TUN helper that such a version left running and removes its state file.
    // Routes are not touched: the ones a crashed 0.9.x session could leave behind were not persistent
    // and disappear when Windows restarts.
    internal static class LegacyCleanup
    {
        public const string TunStateFileName = "tun-state.xml";

        // Returns a note for the log, or "" when nothing was left over.
        public static string Run(string runDir, string helperPath)
        {
            bool killed = false;
            bool state = false;
            try
            {
                killed = ChildPidFile.KillLeftover(runDir, ChildPidFile.LegacyTun, helperPath);
                string path = Path.Combine(runDir, TunStateFileName);
                state = File.Exists(path);
                SafeFile.TryDelete(path);
                SafeFile.TryDelete(path + ".damaged");
            }
            catch (Exception)
            {
                // best effort: the old files never stop the program from starting
            }
            if (!killed && !state)
            {
                return "";
            }
            string what = (killed ? Lang.T("its helper process was stopped") : "")
                   + (killed && state ? "; " : "")
                   + (state ? Lang.T("its routes via 10.255.0.1 may still exist until Windows restarts (\"route print\" shows them)") : "");
            return Lang.F("Left over from the TUN mode of an older version (TUN mode was removed): {0}.", what);
        }
    }
}
