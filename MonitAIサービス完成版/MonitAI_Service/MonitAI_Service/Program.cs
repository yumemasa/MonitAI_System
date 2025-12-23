using System;
using System.ServiceProcess;

namespace MonitAI_Service
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            ServiceBase[] ServicesToRun;
            ServicesToRun = new ServiceBase[]
            {
                new Worker()
            };
            ServiceBase.Run(ServicesToRun);
        }
    }
}
