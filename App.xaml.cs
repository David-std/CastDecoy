using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;

namespace CastDecoy;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        bool isPreview = e.Args.Any(a => a.Equals("--preview", StringComparison.OrdinalIgnoreCase));
        if (!isPreview)
        {
            try
            {
                var currentPid = Process.GetCurrentProcess().Id;
                var olderProcesses = Process.GetProcessesByName("CastDecoy")
                    .Where(p => p.Id != currentPid)
                    .ToList();

                foreach (var p in olderProcesses)
                {
                    try
                    {
                        p.Kill();
                        p.WaitForExit(1000);
                    }
                    catch { }
                }
            }
            catch { }
        }

        base.OnStartup(e);
    }
}
