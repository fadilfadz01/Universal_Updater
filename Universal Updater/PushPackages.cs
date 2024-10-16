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
            updateProcess.StartInfo.FileName = Program.toolsDirectory + $@"\i386\iutool.exe";
            updateProcess.StartInfo.Arguments = $@"-V -p """ + Program.filteredDirectory + $@"\{GetDeviceInfo.SerialNumber[0]}\Packages""";
            updateProcess.StartInfo.UseShellExecute = false;
            updateProcess.Start();
        }

        public static void PushToFFU()
        {
            updateProcess = new Process();
            updateProcess.StartInfo.FileName = Program.toolsDirectory + $@"\i386\updateapp.exe";
            updateProcess.StartInfo.Arguments = $@"install """ + Program.filteredDirectory + $@"\{GetDeviceInfo.SerialNumber[0]}\Packages""";
            updateProcess.StartInfo.UseShellExecute = false;
            updateProcess.Start();
        }
    }
}