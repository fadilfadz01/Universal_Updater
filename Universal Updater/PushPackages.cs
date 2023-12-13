using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Universal_Updater
{
    class PushPackages
    {
        public static Process updateProcess;

        public static void StartUpdate()
        {
            updateProcess = new Process();
            updateProcess.StartInfo.FileName = $@"C:\ProgramData\Universal Updater\iutool.exe";
            updateProcess.StartInfo.Arguments = $@"-V -p ""{Environment.CurrentDirectory}\{GetDeviceInfo.SerialNumber[0]}\Packages""";
            updateProcess.StartInfo.UseShellExecute = false;
            updateProcess.Start();
        }
    }
}