using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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
            stopRunningApps("iutool.exe");

            var packagesPath = Program.filteredDirectory + $@"\{GetDeviceInfo.SerialNumber[0]}\Packages";
            int timeout = calculateTimeout(packagesPath);

            updateProcess = new Process();
            // We use our wrapper to handle logs
            // this is prepared at iutool.bat but not implemented to write logs to file yet
            // generally update issues depends on getdulogs rather than this
            var iutoolPath = Program.toolsDirectory + $@"\i386\iutool.exe";
            updateProcess.StartInfo.FileName = Program.toolsDirectory + $@"\iutool.bat";
            updateProcess.StartInfo.Arguments = $"\"{iutoolPath}\" \"{packagesPath}\" \"{Program.logFile}\"";

            // This tool is hard to handle it's output,
            // better to show the window
            updateProcess.StartInfo.UseShellExecute = true;
            updateProcess.StartInfo.CreateNoWindow = false;

            Program.WriteLine("\n[OUTPUT] (iutool.exe)\nPlease wait...", ConsoleColor.DarkYellow);
            updateProcess.Start();
            updateProcess.WaitForExit(timeout);
        }

        public static void PushToFFU()
        {
            stopRunningApps("updateapp.exe");

            var packagesPath = Program.filteredDirectory + $@"\{GetDeviceInfo.SerialNumber[0]}\Packages";
            int timeout = calculateTimeout(packagesPath);

            updateProcess = new Process();
            updateProcess.StartInfo.FileName = Program.toolsDirectory + $@"\i386\updateapp.exe";
            updateProcess.StartInfo.Arguments = $@"install """ + packagesPath + $@"""";

            updateProcess.StartInfo.UseShellExecute = false;
            updateProcess.StartInfo.CreateNoWindow = true;
            updateProcess.StartInfo.RedirectStandardOutput = true;
            updateProcess.StartInfo.RedirectStandardError = true;
            updateProcess.StartInfo.RedirectStandardInput = true;

            Program.WriteLine("\n[OUTPUT] (updateapp.exe)\nPlease wait...", ConsoleColor.DarkYellow);
            updateProcess.OutputDataReceived += (sender, args) =>
            {
                if (!String.IsNullOrEmpty(args.Data))
                {
                    Program.appendLog(args.Data + Environment.NewLine);
                    Program.resetCursorPosition();
                    Program.WriteLine(args.Data, ConsoleColor.DarkGray);
                }
            };

            updateProcess.ErrorDataReceived += (sender, args) =>
            {
                if (!String.IsNullOrEmpty(args.Data))
                {
                    Program.appendLog(args.Data + Environment.NewLine);
                    Program.resetCursorPosition();
                    Program.WriteLine(args.Data, ConsoleColor.Red);
                }
            };

            updateProcess.Start();
            updateProcess.BeginOutputReadLine();
            updateProcess.BeginErrorReadLine();
            updateProcess.WaitForExit(timeout);
        }

        public static int calculateTimeout(string packagesPath)
        {
            int timeout = 10000;
            var files = Directory.GetFiles(packagesPath);

            long totalBytes = 0;

            foreach (var file in files)
            {
                FileInfo info = new FileInfo(file);
                totalBytes += info.Length;
            }
            Program.WriteLine("-------------------", ConsoleColor.DarkGray);

            double totalSizeInMB = totalBytes / (1024.0 * 1024.0);
            Program.WriteLine("Total Size       : " + Math.Round(totalSizeInMB) + " MB", ConsoleColor.DarkGray);

            double transferSpeedMBps = 30.0; // Not sure, but approx
            double approxTransferTimeInSeconds = totalSizeInMB / transferSpeedMBps;
            double approxTransferTimeInSecondsSlower = totalSizeInMB / (transferSpeedMBps / 2);
            Program.WriteLine("Approx. transfer : ~(" + Math.Round(approxTransferTimeInSeconds) + " - " + Math.Round(approxTransferTimeInSecondsSlower) + ") seconds", ConsoleColor.DarkGray);

            if (approxTransferTimeInSeconds * 1000 > timeout)
            {
                timeout = (int)(approxTransferTimeInSeconds * 1000);
            }
            return timeout;
        }

        public static void stopRunningApps(string appName)
        {
            var appFullPath = $@"{Environment.CurrentDirectory}\{Program.toolsDirectory}\i386\{appName}";
            string processName = Path.GetFileNameWithoutExtension(appName);

            Process[] existingProcesses = Process.GetProcessesByName(processName);

            foreach (Process proc in existingProcesses)
            {
                string fullPath = proc.MainModule.FileName;

                if (fullPath == appFullPath)
                {
                    Program.WriteLine($"Tool ({appName}) detected in use, force closing..", ConsoleColor.Red);
                    proc.Kill();
                    proc.WaitForExit();
                    Program.WriteLine($"Tool ({appName}) closed.", ConsoleColor.DarkYellow);

                }
            }
        }
    }
}