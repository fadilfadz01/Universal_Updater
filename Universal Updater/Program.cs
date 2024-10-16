﻿using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static System.Net.Mime.MediaTypeNames;

namespace Universal_Updater
{
    class Program
    {
        [DllImport("User32.dll", CharSet = CharSet.Unicode)]
        public static extern int MessageBox(IntPtr h, string Content, string Title, int type);
        public static ConsoleKeyInfo selectedUpdate;
        static ConsoleKeyInfo featureChoice;
        private static string packagePath;
        public static bool pushToFFU = false;
        static readonly string[,] updateStructure = { { null, null, null }, { "13016.108", "13080.107", "Offline" }, { "13037.0", "Offline", null }, { "14393.1066", "Offline", null }, { "15063.297", "15254.603", "Offline" }, { "15254.603", "Offline", null }, { "Offline", null, null } };
        public static string tempDirectory = "Temp";
        public static string toolsDirectory = "Resources";
        public static string filteredDirectory = $@"{Environment.CurrentDirectory}\Filtered";
        public static string logFile = $@"{Environment.CurrentDirectory}\Logs\universal_updater.txt";

        public Program()
        {
            while (true)
            {
                Thread.Sleep(100);
            }
        }

        public static void appendLog(string text)
        {
            try
            {
                File.AppendAllText(Program.logFile, text);
            }
            catch (Exception e)
            {
            }
        }
        public static void WriteLine(string text, ConsoleColor color = ConsoleColor.White)
        {
            Console.ForegroundColor = color;
            Console.WriteLine(text);
            Console.ResetColor();
            appendLog(text + Environment.NewLine);
        }
        public static void Write(string text, ConsoleColor color = ConsoleColor.White)
        {
            Console.ForegroundColor = color;
            Console.Write(text);
            Console.ResetColor();
            appendLog(text);
        }
        public static void resetCursorPosition()
        {
            if (Console.CursorTop > 0)
                Console.SetCursorPosition(0, Console.CursorTop - 1);
            else
                Console.SetCursorPosition(0, 0);

            Console.Write(new string(' ', Console.WindowWidth));
            Console.SetCursorPosition(0, Console.CursorTop - 1);
        }
        static async Task retryWithCount(int count = 3)
        {
            for (int i = 3; i > 0; i--)
            {
                ConsoleStyle($"Will retry after {i} seconds . . .", 0);
                await Task.Delay(1000);
            }
        }
        public static string prepareLogFile(string toolName)
        {
            string logFile = toolName;
            string directoryPath = $@"{Environment.CurrentDirectory}\Logs";
            if (!Directory.Exists(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }
            logFile = directoryPath + $@"\{toolName}_" + DateTime.Now.ToString("yyyy.MM.dd_HH.mm.ss") + ".txt";

            return logFile;
        }

        static async void StartUpdater()
        {
            logFile = prepareLogFile("universal_updater.exe");

            //Extract important tools if not ready
            string startPath = AppDomain.CurrentDomain.BaseDirectory;
            string zipPath = Path.Combine(startPath, @"Resources\i386.zip");
            string extractPath = Path.Combine(startPath, @"Resources");

            // Check if directory already extracted
            if (!Directory.Exists(extractPath + "\\i386"))
            {
                ConsoleStyle("Extracting resources, please wait...", 0, ConsoleColor.Blue);
                // Extract the archive
                ZipFile.ExtractToDirectory(zipPath, extractPath);
            }

            Console.ResetColor();
        cleanTempArea:
            if (Directory.Exists(tempDirectory))
            {
                try
                {
                    Directory.Delete(tempDirectory, true);
                }
                catch (Exception ex)
                {
                    // Handle iutool in use error (or any other tool)
                    ConsoleStyle(ex.Message, 0, ConsoleColor.Red);
                    WriteLine("\nRetrying...", ConsoleColor.DarkYellow);
                    await Task.Delay(5000);
                    //Give small delay between retries
                    await retryWithCount(3);
                    goto cleanTempArea;
                }
            }
            Directory.CreateDirectory(tempDirectory);

            ConsoleStyle("Reading device info . . .", 0, ConsoleColor.DarkYellow);
            bool deviceInfoReady = false;
            while (!deviceInfoReady)
            {
                ConsoleStyle("Reading device info . . .", 0, ConsoleColor.DarkYellow);
                var exitCode = GetDeviceInfo.GetLog();
                deviceInfoReady = exitCode == 0;
                await Task.Delay(1000);
                if (!deviceInfoReady)
                {
                    if (exitCode == -1)
                    {
                        ConsoleStyle("Read device info (Timeout), retrying . . .", 0, ConsoleColor.Red);
                    }
                    else
                    {
                        string formattedErrorCode = "0x" + exitCode.ToString("X");
                        ConsoleStyle($"Cannot read device info ({formattedErrorCode}), retrying . . .", 0, ConsoleColor.Red);
                        if (formattedErrorCode == "0x8000FFFF")
                        {
                            // Usually appear if device is not connected
                            WriteLine("");
                            WriteLine("Ensure the device is connected properly", ConsoleColor.Red);
                        }
                        // Give user more time to read and copy error
                        await Task.Delay(3000);
                    }
                    await Task.Delay(2000);
                    // Give small delay between retries
                    await retryWithCount(3);
                }
            }

            ConsoleStyle("Reading device info . . .", 0, ConsoleColor.DarkYellow);
            var deviceInfo = await GetDeviceInfo.ReadInfo();
            if (deviceInfo == null)
            {
                ConsoleStyle("Couldn't read the device information.\nMake sure that you have no pending updates on your phone settings.", 0, ConsoleColor.Red);
                return;
            }
            ConsoleStyle("DEVICE INFORMATIONS", 1, ConsoleColor.DarkGray);
            foreach (string info in deviceInfo)
            {
                WriteLine(info);
            }
            string availableUpdates = CheckForUpdates.AvailableUpdates();
            if (CheckForUpdates.Choice == 1)
            {
                WriteLine("\nThe OS version didn't reached the minimum required build.", ConsoleColor.Red);
                Write("Please update to ", ConsoleColor.Red);
                Write("'8.10.14219.341'", ConsoleColor.Blue);
                WriteLine(" at least.", ConsoleColor.Red);
                return;
            }

            WriteLine("\nAVAILABLE UPDATES", ConsoleColor.DarkGray);
            WriteLine(availableUpdates);

            var packagesReady = false;

            Write("Choice: ", ConsoleColor.Magenta);
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
                Write("\nSomething went wrong.", ConsoleColor.Red);
                return;
            }
            Write(selectedUpdate.KeyChar + "\n");
            if (updateStructure[CheckForUpdates.Choice - 1, Convert.ToInt32(Program.selectedUpdate.KeyChar.ToString()) - 1] == "Offline")
            {
            packagePathArea:
                packagePath = "";
                Write("\nEnter offline packages path: ", ConsoleColor.DarkYellow);
                do
                {
                    packagePath = Console.ReadLine().Trim('"');
                }
                while (packagePath.Length == 0);

                string[] folderFiles = new string[] { };
                if (Directory.Exists(packagePath))
                {
                    folderFiles = Directory.GetFiles(packagePath);
                    var hasCabFiles = string.Join("\n", folderFiles).IndexOf(".cab", StringComparison.OrdinalIgnoreCase) > 0;
                    var hasSpkgFiles = string.Join("\n", folderFiles).IndexOf(".spkg", StringComparison.OrdinalIgnoreCase) > 0;
                    if (!hasCabFiles && !hasSpkgFiles)
                    {
                        Program.WriteLine("\nFolder don't have update packages (.cab)!", ConsoleColor.Red);
                        Program.WriteLine("1. Retry");
                        Program.WriteLine("2. Exit");
                        Program.Write("Choice: ", ConsoleColor.Magenta);
                        ConsoleKeyInfo packagesAction;
                        do
                        {
                            packagesAction = Console.ReadKey(true);
                        }
                        while (packagesAction.KeyChar != '1' && packagesAction.KeyChar != '2');
                        Console.Write(packagesAction.KeyChar.ToString() + "\n");
                        if (packagesAction.KeyChar == '1')
                        {
                            Program.WriteLine("");
                            goto packagePathArea;
                        }
                        else
                        {
                            return;
                        }
                    }
                }
                if (folderFiles.Length > 0)
                {
                    if (string.Join("\n", folderFiles).IndexOf("MS_RCS_FEATURE_PACK.MainOS.cbsr", StringComparison.OrdinalIgnoreCase) >= 0 || string.Join("\n", Directory.GetFiles(packagePath)).IndexOf("ms_projecta.mainos", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        Console.WriteLine("\nAVAILABLE FEATURES", ConsoleColor.DarkGray);
                        if (string.Join("\n", Directory.GetFiles(packagePath)).IndexOf("MS_RCS_FEATURE_PACK.MainOS.cbsr", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            printRCSMessage();
                        }
                        else
                        {
                            printProjectAMessage();
                        }
                        do
                        {
                            featureChoice = Console.ReadKey(true);
                        }
                        while (featureChoice.KeyChar != 'Y' && featureChoice.KeyChar != 'N' && featureChoice.KeyChar != 'y' && featureChoice.KeyChar != 'n');
                        Write(featureChoice.KeyChar.ToString().ToUpper() + "\n");
                    }

                    WriteLine("\nPREPARING PACKAGES", ConsoleColor.DarkGray);
                    packagesReady = DownloadPackages.OfflineUpdate(folderFiles, featureChoice.KeyChar.ToString().ToUpper());
                }
                else
                {
                    Program.WriteLine("\nFolder is empty or not exists!", ConsoleColor.Red);
                    Program.WriteLine("1. Retry");
                    Program.WriteLine("2. Exit");
                    Program.Write("Choice: ", ConsoleColor.Magenta);
                    ConsoleKeyInfo packagesAction;
                    do
                    {
                        packagesAction = Console.ReadKey(true);
                    }
                    while (packagesAction.KeyChar != '1' && packagesAction.KeyChar != '2');
                    Console.Write(packagesAction.KeyChar.ToString() + "\n");
                    if (packagesAction.KeyChar == '1')
                    {
                        Program.WriteLine("");
                        goto packagePathArea;
                    }
                    else
                    {
                        return;
                    }
                }

            }
            else
            {
                bool IsFeatureInstalled = DownloadPackages.OnlineUpdate(updateStructure[CheckForUpdates.Choice - 1, Convert.ToInt32(Program.selectedUpdate.KeyChar.ToString()) - 1]);
                if (IsFeatureInstalled == false)
                {
                    if (updateStructure[CheckForUpdates.Choice - 1, Convert.ToInt32(Program.selectedUpdate.KeyChar.ToString()) - 1] == "14393.1066" || updateStructure[CheckForUpdates.Choice - 1, Convert.ToInt32(Program.selectedUpdate.KeyChar.ToString()) - 1] == "13016.108" || updateStructure[CheckForUpdates.Choice - 1, Convert.ToInt32(Program.selectedUpdate.KeyChar.ToString()) - 1] == "13037.0")
                    {
                        WriteLine("\nAVAILABLE FEATURES", ConsoleColor.DarkGray);
                        if (updateStructure[CheckForUpdates.Choice - 1, Convert.ToInt32(Program.selectedUpdate.KeyChar.ToString()) - 1] == "14393.1066")
                        {
                            printRCSMessage();
                        }
                        else
                        {
                            printProjectAMessage();
                        }
                        do
                        {
                            featureChoice = Console.ReadKey(true);
                        }
                        while (featureChoice.KeyChar != 'Y' && featureChoice.KeyChar != 'N' && featureChoice.KeyChar != 'y' && featureChoice.KeyChar != 'n');
                        Write(featureChoice.KeyChar.ToString().ToUpper() + "\n");
                    }
                }
                WriteLine("\nDOWNLOADING PACKAGES", ConsoleColor.DarkGray);
                while (!IsInternetConnected())
                {
                    ConsoleStyle("Waiting for internet to connect . . .", 1, ConsoleColor.DarkYellow);
                    await Task.Delay(2000);
                }

                ConsoleStyle("\nDOWNLOADING PACKAGES", 1, ConsoleColor.DarkGray);
                packagesReady = await DownloadPackages.DownloadUpdate(updateStructure[CheckForUpdates.Choice - 1, Convert.ToInt32(Program.selectedUpdate.KeyChar.ToString()) - 1]);
            }

            string messageBoxText = "";
            if (packagesReady)
            {
                if (!pushToFFU)
                {
                    WriteLine("\nPUSHING PACKAGES", ConsoleColor.DarkGray);
                    PushPackages.StartUpdate();
                    await Task.Delay(2000);
                    var commonErrors = "\n\nCommon Errors:";
                    commonErrors += "\n\n0x800b010a: Signature Verification Issue\nSolution: Enable flight signing";
                    commonErrors += "\n\n0x800b0101: Incorrect Date and Time\nSolution: Change date";
                    commonErrors += "\n\n0x80188302: Package already present on the image";
                    commonErrors += "\n\n0x80188305: Duplicate packages found in update set";
                    commonErrors += "\n\n0x80188306: File Collision or Not Found\nSolution: ensure list match InstalledPackages.csv";
                    commonErrors += "\n\n0x80188307: Package DSM; or contents are invalid\nSolution: Keep only one type in the folder CBS or SPKG,\nChoose the correct packages type";
                    commonErrors += "\n\n0x80188308: Not enough space";
                    commonErrors += "\n\n0x800b0114: Certificate has an invalid name\nSolution: Enable flight signing";
                    commonErrors += "\n\n0x80004005: E_FAIL\nSolution: Reboot the phone and try again";
                    commonErrors += "\n\n0x802A0006: Try with another PC";
                    if (!PushPackages.updateProcess.HasExited)
                    {
                        messageBoxText = $"Please navigate to update settings on your phone. As soon as the update shows progression, you can safely disconnect your phone from the PC.\n{commonErrors}";
                    }
                    else
                    {
                        messageBoxText = $"Error may detected!\n{commonErrors}";
                    }
                }
                else
                {
                    WriteLine("\nPUSHING PACKAGES (FFU)", ConsoleColor.DarkGray);
                    WriteLine("\n[IMPORTANT]\nDon't interrupt the process until update app exit", ConsoleColor.DarkYellow);

                    await Task.Delay(3500);
                    PushPackages.PushToFFU();

                    await Task.Delay(2000);
                    var commonErrors = "\n\nCommon Errors:";
                    commonErrors += "\n\n0x80070490: ERROR_NOT_FOUND\nSolution: Ensure you mounted the FFU first";
                    commonErrors += "\n\n0x80070241: ERROR_INVALID_IMAGE_HASH";
                    commonErrors += "\n\n0x80188302: Package already present on the image";
                    commonErrors += "\n\n0x80188305: Duplicate packages found in update set";
                    if (!PushPackages.updateProcess.HasExited)
                    {
                        messageBoxText = $"Don't interrupt the process until update app exit!\nYou can dismount your FFU once update app finished.\n{commonErrors}";
                    }
                    else
                    {
                        string formattedErrorCode = "0x" + PushPackages.updateProcess.ExitCode.ToString("X");
                        WriteLine($"\nError detected (updateapp.exe): {formattedErrorCode}\n", ConsoleColor.Red);
                        messageBoxText = $"Error may detected!.\n{commonErrors}";
                    }
                }
            }
            else
            {
                WriteLine("\nOperation cancelled\n", ConsoleColor.Red);
            }

            resetCursorPosition();
            var previewLogFile = logFile.Replace($@"{Environment.CurrentDirectory}\", "");
            WriteLine($"Finished.\n\nFor more details check the logs\n{previewLogFile}", ConsoleColor.DarkGray);

            if (messageBoxText.Length > 0)
            {
                MessageBox((IntPtr)0, messageBoxText, "Universal Updater.exe", 0);
            }
            return;
        }

        static void printRCSMessage()
        {
            WriteLine("Remove RCS feature?");
            Write("[Removal is ", ConsoleColor.DarkYellow);
            Write(" not recommended ", ConsoleColor.Red);
            Write("for ", ConsoleColor.DarkYellow);
            Write("regular updates", ConsoleColor.Blue);
            WriteLine("]: ", ConsoleColor.DarkYellow);
            Write("Choice (Y/N): ", ConsoleColor.Magenta);
        }

        static void printProjectAMessage()
        {
            Write("Install ");
            Write("ProjectA (Astoria)", ConsoleColor.Green);
            Write("?");
            Write(" (Y/N): ", ConsoleColor.Magenta);
        }

        static void Main(string[] args)
        {
            if (!IsDependenciesInstalled("Microsoft Visual C++ 2012 Redistributable (x86)"))
            {
                WriteLine("Error:\n  An assembly in the application dependencies manifest was not found:\n    package: 'Microsoft.Visual.C++.2012.Redistributable.(x86)'", ConsoleColor.Red);
                return;
            }
            else if (!IsDependenciesInstalled("Microsoft Visual C++ 2013 Redistributable (x86)"))
            {
                WriteLine("Error:\n  An assembly in the application dependencies manifest was not found:\n    package: 'Microsoft.Visual.C++.2013.Redistributable.(x86)'", ConsoleColor.Red);
                return;
            }
            StartUpdater();
            new OutputCapture();
            new Program();
        }

        static void ConsoleStyle(string text, int line, ConsoleColor color = ConsoleColor.White)
        {
            Console.ResetColor();
            Console.SetWindowSize(115, 29);
            Console.SetBufferSize(115, 400);
            Console.Title = FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location).InternalName;
            Console.Clear();
            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.Write(Path.GetFileNameWithoutExtension(FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location).InternalName));
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine(" Version " + FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location).FileVersion);
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine(FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location).LegalCopyright + " by " + FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location).CompanyName);
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.WriteLine("This app provided AS-IS without any warranty");
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("GitHub: https://github.com/fadilfadz01/Universal_Updater");

            Console.WriteLine(string.Empty);
            Console.ForegroundColor = color;
            if (line == 0)
            {
                Console.Write(text);
            }
            else
            {
                Console.WriteLine(text);
            }
        }

        public static string GetResourceFile(string resourceName, bool dump = false)
        {
            var embeddedResource = Assembly.GetExecutingAssembly().GetManifestResourceNames().Where(s => s.IndexOf(resourceName, StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
            if (!string.IsNullOrWhiteSpace(embeddedResource[0]))
            {
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(embeddedResource[0]))
                {
                    if (dump)
                    {
                        var data = new byte[stream.Length];
                        stream.Read(data, 0, data.Length);
                        File.WriteAllBytes(tempDirectory + $@"\{resourceName}", data);
                    }
                    else
                    {
                        using (StreamReader reader = new StreamReader(stream))
                        {
                            string result = reader.ReadToEnd();
                            return result;
                        }
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
                    if (SubKey.GetValue("DisplayName").ToString().IndexOf(dependency, StringComparison.OrdinalIgnoreCase) >= 0)
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