using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Universal_Updater
{
    class Program
    {
        [DllImport("User32.dll", CharSet = CharSet.Unicode)]
        public static extern int MessageBox(IntPtr h, string Content, string Title, int type);
        public static ConsoleKeyInfo selectedUpdate;
        static ConsoleKeyInfo featureChoice;
        private static string packagePath;
        static readonly string[,] updateStructure = { { null, null, null }, { "13016.108", "13080.107", "Offline" }, { "13037.0", "Offline", null }, { "14393.1066", "Offline", null }, { "15063.297", "15254.603", "Offline" }, { "15254.603", "Offline", null }, { "Offline", null, null } };

        public Program()
        {
            while (true)
            {
                Thread.Sleep(100);
            }
        }

        static async void StartUpdater()
        {
            Directory.CreateDirectory(@"C:\ProgramData\Universal Updater");
            GetResourceFile("getdulogs.exe", 1);
            GetResourceFile("iutool.exe", 1);
            GetResourceFile("wget.exe", 1);
            ConsoleStyle("Reading device info . . .", 0);
            while (GetDeviceInfo.GetLog() != 0)
            {
                ConsoleStyle("Waiting for device to connect . . .", 0);
            }
            ConsoleStyle("Reading device info . . .", 0);
            var deviceInfo = await GetDeviceInfo.ReadInfo();
            if (string.IsNullOrEmpty(GetDeviceInfo.OSVersion[0].Split(',')[2]))
            {
                ConsoleStyle("Couldn't read the device informations. Make sure that you have no pending updates on your phone settings.", 0);
                return;
            }
            ConsoleStyle("DEVICE INFORMATIONS", 1);
            foreach (string info in deviceInfo)
            {
                Console.WriteLine(info);
            }
            string availableUpdates = CheckForUpdates.AvailableUpdates();
            Console.WriteLine("\n\nAVAILABLE UPDATES");
            Console.WriteLine(availableUpdates);
            if (CheckForUpdates.Choice == 1)
            {
                Console.Write("The OS version didn't reached the minimum required build. Please update to '8.10.14219.341' atleast.");
                return;
            }
            Console.Write("Choice: ");
            if (CheckForUpdates.Choice == 7)
            {
                do
                {
                    selectedUpdate = Console.ReadKey(true);
                }
                while (selectedUpdate.KeyChar != '1');
            }
            else if (CheckForUpdates.Choice == 3 || CheckForUpdates.Choice == 4 || CheckForUpdates.Choice == 6)
            {
                do
                {
                    selectedUpdate = Console.ReadKey(true);
                }
                while (selectedUpdate.KeyChar != '1' && selectedUpdate.KeyChar != '2');
            }
            else if (CheckForUpdates.Choice == 2 || CheckForUpdates.Choice == 5)
            {
                do
                {
                    selectedUpdate = Console.ReadKey(true);
                }
                while (selectedUpdate.KeyChar != '1' && selectedUpdate.KeyChar != '2' && selectedUpdate.KeyChar != '3');
            }
            else
            {
                Console.Write("\nSomething went wrong.");
                return;
            }
            Console.Write(selectedUpdate.KeyChar + "\n");
            if (updateStructure[CheckForUpdates.Choice - 1, Convert.ToInt32(Program.selectedUpdate.KeyChar.ToString()) - 1] == "Offline")
            {
                Console.Write("Enter offline packages path: ");
                string consoleScreen = OutputCapture.Captured.ToString();
                do
                {
                    Console.Clear();
                    Console.Write(consoleScreen);
                    packagePath = Console.ReadLine().Trim('"');
                }
                while (!Directory.Exists(packagePath) || !string.Join("\n", Directory.GetFiles(packagePath)).Contains(".cab", StringComparison.OrdinalIgnoreCase));
                if (string.Join("\n", Directory.GetFiles(packagePath)).Contains("MS_RCS_FEATURE_PACK.MainOS.cbsr", StringComparison.OrdinalIgnoreCase) || string.Join("\n", Directory.GetFiles(packagePath)).Contains("ms_projecta.mainos", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine("\n\nAVAILABLE FEATURES");
                    if (string.Join("\n", Directory.GetFiles(packagePath)).Contains("MS_RCS_FEATURE_PACK.MainOS.cbsr", StringComparison.OrdinalIgnoreCase))
                    {
                        Console.Write("Remove RCS feature? [Removal is not recommended for regular updates] (Y/N): ");
                    }
                    else
                    {
                        Console.Write("Install project astoria (Y/N): ");
                    }
                    do
                    {
                        featureChoice = Console.ReadKey(true);
                    }
                    while (featureChoice.KeyChar != 'Y' && featureChoice.KeyChar != 'N' && featureChoice.KeyChar != 'y' && featureChoice.KeyChar != 'n');
                    Console.Write(featureChoice.KeyChar.ToString().ToUpper() + "\n");
                }
                Console.WriteLine("\n\nPREPARING PACKAGES");
                DownloadPackages.OfflineUpdate(packagePath, featureChoice.KeyChar.ToString().ToUpper());
            }
            else
            {
                bool IsFeatureInstalled = DownloadPackages.OnlineUpdate(updateStructure[CheckForUpdates.Choice - 1, Convert.ToInt32(Program.selectedUpdate.KeyChar.ToString()) - 1]);
                if (IsFeatureInstalled == false)
                {
                    if (updateStructure[CheckForUpdates.Choice - 1, Convert.ToInt32(Program.selectedUpdate.KeyChar.ToString()) - 1] == "14393.1066" || updateStructure[CheckForUpdates.Choice - 1, Convert.ToInt32(Program.selectedUpdate.KeyChar.ToString()) - 1] == "13016.108" || updateStructure[CheckForUpdates.Choice - 1, Convert.ToInt32(Program.selectedUpdate.KeyChar.ToString()) - 1] == "13037.0")
                    {
                        Console.WriteLine("\n\nAVAILABLE FEATURES");
                        if (updateStructure[CheckForUpdates.Choice - 1, Convert.ToInt32(Program.selectedUpdate.KeyChar.ToString()) - 1] == "14393.1066")
                        {
                            Console.Write("Remove RCS feature? [Removal is not recommended for regular updates] (Y/N): ");
                        }
                        else
                        {
                            Console.Write("Install project astoria (Y/N): ");
                        }
                        do
                        {
                            featureChoice = Console.ReadKey(true);
                        }
                        while (featureChoice.KeyChar != 'Y' && featureChoice.KeyChar != 'N' && featureChoice.KeyChar != 'y' && featureChoice.KeyChar != 'n');
                        Console.Write(featureChoice.KeyChar.ToString().ToUpper() + "\n");
                    }
                }
                Console.WriteLine("\n\nDOWNLOADING PACKAGES");
                string consoleScreen = OutputCapture.Captured.ToString();
                while (!IsInternetConnected())
                {
                    Console.Clear();
                    Console.Write(consoleScreen);
                    Console.Write("No internet connection. Waiting for internet to connect . . .");
                    await Task.Delay(2000);
                }
                Console.Clear();
                Console.Write(consoleScreen);
                await DownloadPackages.DownloadUpdate(updateStructure[CheckForUpdates.Choice - 1, Convert.ToInt32(Program.selectedUpdate.KeyChar.ToString()) - 1]);
            }
            Console.Write("\n\nPUSHING PACKAGES");
            PushPackages.StartUpdate();
            await Task.Delay(15000);
            if (!PushPackages.updateProcess.HasExited)
            {
                MessageBox((IntPtr)0, "Please navigate to update settings on your phone. As soon as the update shows progression, you can safely disconnect your phone from the PC.", "Universal Updater.exe", 0);
            }
            return;
        }

        static void Main(string[] args)
        {
            if (!IsDependenciesInstalled("Microsoft Visual C++ 2012 Redistributable (x86)"))
            {
                Console.WriteLine("Error:\n  An assembly in the application dependencies manifest was not found:\n    package: 'Microsoft.Visual.C++.2012.Redistributable.(x86)'");
                return;
            }
            else if (!IsDependenciesInstalled("Microsoft Visual C++ 2013 Redistributable (x86)"))
            {
                Console.WriteLine("Error:\n  An assembly in the application dependencies manifest was not found:\n    package: 'Microsoft.Visual.C++.2013.Redistributable.(x86)'");
                return;
            }
            StartUpdater();
            new OutputCapture();
            new Program();
        }

        static void ConsoleStyle(string text, int line)
        {
            Console.SetWindowSize(115, 29);
            Console.SetBufferSize(115, 400);
            Console.Title = "Universal Updater.exe";
            Console.Clear();
            Console.WriteLine("Universal Updater");
            Console.WriteLine("Version " + FileVersionInfo.GetVersionInfo(System.Reflection.Assembly.GetExecutingAssembly().Location).FileVersion);
            Console.WriteLine("Copyright (c) 2021 - 2022 by Fadil Fadz");
            Console.WriteLine(string.Empty);
            if (line == 0)
            {
                Console.Write(text);
            }
            else
            {
                Console.WriteLine(text);
            }
        }

        public static string GetResourceFile(string resourceName, int dump)
        {
            var embeddedResource = Assembly.GetExecutingAssembly().GetManifestResourceNames().Where(s => s.Contains(resourceName, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (!string.IsNullOrWhiteSpace(embeddedResource[0]))
            {
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(embeddedResource[0]))
                {
                    if(dump == 0)
                    {
                        using (StreamReader reader = new StreamReader(stream))
                        {
                            string result = reader.ReadToEnd();
                            return result;
                        }
                    }
                    else
                    {
                        var data = new byte[stream.Length];
                        stream.Read(data, 0, data.Length);
                        File.WriteAllBytes($@"C:\ProgramData\Universal Updater\{resourceName}", data);
                    }
                }
            }
            return null;
        }

        static bool IsDependenciesInstalled(string dependency)
        {
            RegistryKey Key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Classes\Installer\Dependencies");
            foreach (string subKeyName in Key.GetSubKeyNames())
            {
                RegistryKey SubKey = Key.OpenSubKey(subKeyName);
                if (SubKey.GetValue("DisplayName") != null)
                {
                    if (SubKey.GetValue("DisplayName").ToString().Contains(dependency, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        static bool IsInternetConnected()
        {
            string host = "www.google.com";
            bool result = false;
            Ping p = new Ping();
            try
            {
                PingReply reply = p.Send(host, 3000);
                if (reply.Status == IPStatus.Success)
                {
                    return true;
                }
            }
            catch { }
            return result;
        }
    }
}
