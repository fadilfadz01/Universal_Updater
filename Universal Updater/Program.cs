/**************************************************************************
 * 
 *  Project Name:     Universal Updater
 *  Description:      Console based unofficial updater for Windows Phone.
 * 
 *  Author:           Fadil Fadz
 *  Created Date:     2021
 *  
 *  Contributors:     Bashar Astifan
 * 
 *  Copyright © 2021 - 2024 Fadil Fadz
 * 
 *  Permission is hereby granted, free of charge, to any person obtaining a copy 
 *  of this software and associated documentation files (the "Software"), to deal 
 *  in the Software without restriction, including without limitation the rights 
 *  to use, copy, modify, merge, publish, distribute, sublicense, and/or sell 
 *  copies of the Software, and to permit persons to whom the Software is 
 *  furnished to do so, subject to the following conditions:
 * 
 *  The above copyright notice and this permission notice shall be included in all 
 *  copies or substantial portions of the Software.
 * 
 *  THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR 
 *  IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, 
 *  FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE 
 *  AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER 
 *  LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, 
 *  OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE 
 *  SOFTWARE.
 * 
 **************************************************************************/

using Microsoft.Win32;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
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
        public static string packagePath;
        public static bool pushToFFU = false;
        public static string FFUPath = null;
        static readonly string[,] updateStructure = { { null, null, null }, { "13016.108", "13080.107", "Offline" }, { "13037.0", "Offline", null }, { "14393.1066", "Offline", null }, { "15063.297", "15254.603", "Offline" }, { "15254.603", "Offline", null }, { "Offline", null, null } };
        public static string tempDirectory = "Temp";
        public static string toolsDirectory = "Resources";
        public static string filteredDirectory = $@"{Environment.CurrentDirectory}\Filtered";
        public static string logFile = $@"{Environment.CurrentDirectory}\Logs\universal_updater.txt";
        public static UpdateRules updateRules = new UpdateRules();
        private static readonly object logLock = new object();

        // For testing only without device
        public static bool useConnectedDevice = true;
        public static bool iutoolMode = false;

        public static void appendLog(string text)
        {
            if (string.IsNullOrEmpty(text))
                return;

            lock (logLock)
            {
                try
                {
                    var logDir = Path.GetDirectoryName(logFile);
                    if (!string.IsNullOrEmpty(logDir) && !Directory.Exists(logDir))
                    {
                        Directory.CreateDirectory(logDir);
                    }
                    using (var stream = new FileStream(logFile, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                    {
                        using (var writer = new StreamWriter(stream))
                        {
                            writer.Write(text);
                        }
                    }
                }
                catch
                {
                }
            }
        }
        public static void WriteLine(string text, ConsoleColor color = ConsoleColor.White, bool trimPreviewIfLong = false, bool appendToLog = true)
        {
            text = text ?? string.Empty;
            lock (logLock)
            {
                if (!Console.IsOutputRedirected)
                {
                    try { Console.ForegroundColor = color; } catch { }
                }
                if (trimPreviewIfLong)
                {
                    WriteTrimmedLine(text);
                }
                else
                {
                    Console.WriteLine(text);
                }
                if (!Console.IsOutputRedirected)
                {
                    try { Console.ResetColor(); } catch { }
                }
            }
            if (appendToLog)
            {
                appendLog(text + Environment.NewLine);
            }
        }
        public static void Write(string text, ConsoleColor color = ConsoleColor.White, bool trimPreviewIfLong = false, bool appendToLog = true)
        {
            text = text ?? string.Empty;
            lock (logLock)
            {
                if (!Console.IsOutputRedirected)
                {
                    try { Console.ForegroundColor = color; } catch { }
                }
                if (trimPreviewIfLong)
                {
                    WriteTrimmedLine(text, false);
                }
                else
                {
                    Console.Write(text);
                }
                if (!Console.IsOutputRedirected)
                {
                    try { Console.ResetColor(); } catch { }
                }
            }
            if (appendToLog)
            {
                appendLog(text);
            }
        }
        public static void WriteTrimmedLine(string text, bool writeLine = true)
        {
            text = text ?? string.Empty;
            int windowWidth = 115;
            if (!Console.IsOutputRedirected)
            {
                try { windowWidth = Console.WindowWidth; } catch { }
            }

            // Get the current console window width
            int consoleWidth = Math.Max(20, (int)Math.Round(windowWidth * 0.95));

            // Calculate the maximum allowed length considering space for '...'
            const int ellipsisLength = 3;
            int maxAllowedLength = consoleWidth - ellipsisLength;

            if (maxAllowedLength > 0 && text.Length > maxAllowedLength)
            {
                // If text is too long, trim and add ellipses
                text = text.Substring(0, maxAllowedLength) + "...";
            }

            if (writeLine)
            {
                Console.WriteLine(text);
            }
            else
            {
                Console.Write(text);
            }
        }

        public static void resetCursorPosition()
        {
            if (Console.IsOutputRedirected)
                return;

            try
            {
                int targetTop = Math.Max(0, Console.CursorTop - 1);
                Console.SetCursorPosition(0, targetTop);
                int clearWidth = Math.Max(1, Console.WindowWidth - 1);
                Console.Write(new string(' ', clearWidth));
                Console.SetCursorPosition(0, targetTop);
            }
            catch
            {
            }
        }
        static async Task retryWithCount(int count = 3)
        {
            for (int i = count; i > 0; i--)
            {
                ConsoleStyle($"Will retry after {i} seconds . . .", 0);
                await Task.Delay(1000);
            }
            ConsoleStyle("", 0);
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

        public static void printFlashInstructions()
        {
            WriteLine("\n[FLASH INSTRUCTIONS]", ConsoleColor.Green);
            WriteLine("Bootloader Spec A (Unlocked):", ConsoleColor.DarkGray);
            Write("  1. ", ConsoleColor.Gray);
            Write("thor2.exe ", ConsoleColor.Blue);
            Write("-mode uefiflash ", ConsoleColor.Gray);
            WriteLine("-ffufile <FFU_Path>", ConsoleColor.DarkYellow);
            Write("  2. ", ConsoleColor.Gray);
            Write("wpinternals.exe ", ConsoleColor.Blue);
            WriteLine("-enabletestsigning", ConsoleColor.Green);
            WriteLine("Bootloader Spec B / Unsigned (-noSign) FFU:", ConsoleColor.DarkGray);
            Write("  - ", ConsoleColor.Gray);
            WriteLine("Use WPinternals -> Flash -> Flash Custom ROM (or dump & flash partitions separately)", ConsoleColor.DarkYellow);
        }

        static string GetHostSysX86WimgapiPath()
        {
            string systemX86 = Environment.GetFolderPath(Environment.SpecialFolder.SystemX86);
            if (!string.IsNullOrEmpty(systemX86))
            {
                string candidate = Path.Combine(systemX86, "wimgapi.dll");
                if (File.Exists(candidate))
                    return candidate;
            }

            string winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string fallback = Path.Combine(winDir, Environment.Is64BitOperatingSystem ? "SysWOW64" : "System32", "wimgapi.dll");
            return File.Exists(fallback) ? fallback : null;
        }

        static Version ParseFileVersion(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                    return null;
                var info = FileVersionInfo.GetVersionInfo(filePath);
                return new Version(info.FileMajorPart, info.FileMinorPart, info.FileBuildPart, info.FilePrivatePart);
            }
            catch
            {
                return null;
            }
        }

        static bool IsPeX86Dll(string filePath)
        {
            try
            {
                using (var fs = File.OpenRead(filePath))
                using (var br = new BinaryReader(fs))
                {
                    if (fs.Length < 0x40 || br.ReadUInt16() != 0x5A4D) // MZ
                        return false;
                    fs.Seek(0x3C, SeekOrigin.Begin);
                    int peOffset = br.ReadInt32();
                    if (peOffset <= 0 || peOffset + 6 > fs.Length)
                        return false;
                    fs.Seek(peOffset, SeekOrigin.Begin);
                    if (br.ReadUInt32() != 0x00004550) // PE\0\0
                        return false;
                    ushort machine = br.ReadUInt16();
                    return machine == 0x014C; // IMAGE_FILE_MACHINE_I386
                }
            }
            catch
            {
                return false;
            }
        }

        internal static void EnsureCompatibleWimgapi(string resourcesPath)
        {
            try
            {
                string hostWimgapi = GetHostSysX86WimgapiPath();
                if (string.IsNullOrEmpty(hostWimgapi) || !IsPeX86Dll(hostWimgapi))
                    return;

                Version hostVersion = ParseFileVersion(hostWimgapi);
                if (hostVersion == null)
                    return;

                string[] targets = new[]
                {
                    Path.Combine(resourcesPath, "i386", "wimgapi.dll"),
                    Path.Combine(resourcesPath, "i386", "_old_", "wimgapi.dll")
                };

                foreach (var targetPath in targets)
                {
                    string parentDir = Path.GetDirectoryName(targetPath);
                    if (string.IsNullOrEmpty(parentDir) || !Directory.Exists(parentDir))
                        continue;

                    Version targetVersion = ParseFileVersion(targetPath);
                    if (targetVersion == null || hostVersion > targetVersion)
                    {
                        File.Copy(hostWimgapi, targetPath, true);
                        appendLog($"[WIMGAPI] Synced {targetPath} ({targetVersion?.ToString() ?? "missing"} -> {hostVersion})\n");
                    }
                }
            }
            catch (Exception ex)
            {
                appendLog($"[WIMGAPI] Failed to sync host wimgapi.dll: {ex.Message}\n");
            }
        }
        static async Task<bool> ReadConnectedDeviceInfoAsync(bool allowCancelOnError)
        {
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

                    if (allowCancelOnError)
                    {
                        if (!PushPackages.showRetryQuestion("Failed to read connected device info, retry?", "Cancel", ConsoleColor.DarkYellow))
                        {
                            return false;
                        }
                    }
                    else
                    {
                        await Task.Delay(2000);
                        // Give small delay between retries
                        await retryWithCount(3);
                    }
                }
            }

            ConsoleStyle("Reading device info . . .", 0, ConsoleColor.DarkYellow);
            string[] deviceInfo = null;
            try
            {
                deviceInfo = await GetDeviceInfo.ReadInfo();
            }
            catch (Exception ex)
            {
                appendLog($"[ReadInfo] Exception: {ex}\n");
            }

            if (deviceInfo == null)
            {
                ConsoleStyle("Couldn't read the device information.\nMake sure that you have no pending updates on your phone settings.", 0, ConsoleColor.Red);
                return false;
            }

            string installedCsv = Path.Combine(tempDirectory, "InstalledPackages.csv");
            if (!File.Exists(installedCsv))
            {
                WriteLine("InstalledPackages.csv was not found in device logs.", ConsoleColor.Red);
                return false;
            }

            ConsoleStyle("TARGET INFORMATIONS", 1, ConsoleColor.DarkGray);
            foreach (string info in deviceInfo)
            {
                WriteLine(info);
            }
            DownloadPackages.installedPackages = File.ReadAllLines(installedCsv);
            return true;
        }

        static async Task WaitForInternetAsync()
        {
            while (!IsInternetConnected())
            {
                ConsoleStyle("Waiting for internet to connect . . .", 1, ConsoleColor.DarkYellow);
                await Task.Delay(2000);
            }
        }

        static void PromptOnlineFeatureSelection(string targetBuild, bool isFeatureInstalled)
        {
            if (isFeatureInstalled)
                return;

            if (targetBuild == "14393.1066" || targetBuild == "13016.108" || targetBuild == "13037.0")
            {
                WriteLine("\nAVAILABLE FEATURES", ConsoleColor.DarkGray);
                if (targetBuild == "14393.1066")
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

                if ((featureChoice.KeyChar == 'Y' || featureChoice.KeyChar == 'y') &&
                    (targetBuild == "13016.108" || targetBuild == "13037.0"))
                {
                    if (DownloadPackages.IncludeFeatureFromManifest(targetBuild, "ms_projecta.mainos"))
                    {
                        appendLog($"[FEATURE] Added ms_projecta.mainos for build {targetBuild}\n");
                    }
                }
            }
        }

        static async Task HandleDownloadCabsMode()
        {
            useConnectedDevice = false;
            pushToFFU = false;
            iutoolMode = false;

            var builds = new[]
            {
                Tuple.Create("10.0.10549.4   (13016.108)", "13016.108"),
                Tuple.Create("10.0.10570.0   (13037.0)",   "13037.0"),
                Tuple.Create("10.0.10586.107 (13080.107)", "13080.107"),
                Tuple.Create("10.0.14393.1066 (AU)",       "14393.1066"),
                Tuple.Create("10.0.15063.297  (CU)",       "15063.297"),
                Tuple.Create("10.0.15254.603  (FCU)",      "15254.603")
            };

            WriteLine("\n[TARGET BUILD]:", ConsoleColor.DarkGray);
            for (int i = 0; i < builds.Length; i++)
            {
                WriteLine($"{i + 1}. {builds[i].Item1}", ConsoleColor.Gray);
            }
            Write("Choice: ", ConsoleColor.Magenta);
            ConsoleKeyInfo buildChoice;
            do
            {
                buildChoice = Console.ReadKey(true);
            }
            while (!IsNumberKeyOrNumpad(buildChoice, new char[] { '1', '2', '3', '4', '5', '6' }));
            char buildDigit = NormalizeNumberKey(buildChoice);
            Write(buildDigit.ToString() + "\n");
            int buildIdx = buildDigit - '1';
            string targetBuild = builds[buildIdx].Item2;

            WriteLine("\n[PACKAGE FILTER]:", ConsoleColor.DarkGray);
            WriteLine("1. Connected Device", ConsoleColor.Green);
            WriteLine("2. FFU Image", ConsoleColor.Blue);
            WriteLine("3. InstalledPackages.csv", ConsoleColor.DarkYellow);
            WriteLine("4. All packages in manifest", ConsoleColor.Gray);
            Write("Choice: ", ConsoleColor.Magenta);
            ConsoleKeyInfo filterChoice;
            do
            {
                filterChoice = Console.ReadKey(true);
            }
            while (!IsNumberKeyOrNumpad(filterChoice, new char[] { '1', '2', '3', '4' }));
            char filterDigit = NormalizeNumberKey(filterChoice);
            Write(filterDigit.ToString() + "\n");

            bool includeAll = false;
            switch (filterDigit)
            {
                case '1':
                    if (!await ReadConnectedDeviceInfoAsync(true))
                    {
                        WriteLine("\nOperation cancelled", ConsoleColor.Red);
                        return;
                    }
                    break;

                case '2':
                    while (true)
                    {
                        string selectedFfu = await GeneralPicker.ShowOpenFileDialogAsync("ffu");
                        Write("\nFFU path: ", ConsoleColor.DarkYellow);
                        WriteLine((selectedFfu ?? string.Empty) + "\n");
                        if (string.IsNullOrEmpty(selectedFfu) || !File.Exists(selectedFfu))
                        {
                            if (!PushPackages.showRetryQuestion("FFU path is not valid or not exist", "Cancel", ConsoleColor.Red))
                            {
                                WriteLine("\nOperation cancelled", ConsoleColor.Red);
                                return;
                            }
                            continue;
                        }

                        FFUEntry ffuMount = null;
                        try
                        {
                            ffuMount = await PushPackages.mountFFU(selectedFfu);
                            if (ffuMount == null)
                            {
                                if (!PushPackages.showRetryQuestion("Mount FFU failed!, retry?", "Cancel"))
                                {
                                    WriteLine("\nOperation cancelled", ConsoleColor.Red);
                                    return;
                                }
                                continue;
                            }

                            var ffuPackages = await GetDeviceInfo.GetInstalledPackagesFromFFU(ffuMount);
                            DownloadPackages.installedPackages = ffuPackages.ToArray();
                            await GetDeviceInfo.printFFUDetails(ffuMount);
                        }
                        finally
                        {
                            if (ffuMount != null)
                            {
                                await PushPackages.dismountFFU(ffuMount, null);
                            }
                        }

                        if (DownloadPackages.installedPackages.Length == 0)
                        {
                            if (!PushPackages.showRetryQuestion("Cannot read installed packages from FFU, retry?", "Cancel"))
                            {
                                WriteLine("\nOperation cancelled", ConsoleColor.Red);
                                return;
                            }
                            continue;
                        }
                        break;
                    }
                    break;

                case '3':
                    while (true)
                    {
                        string csvPath = await GeneralPicker.ShowOpenFileDialogAsync("csv");
                        if (string.IsNullOrEmpty(csvPath) || !File.Exists(csvPath))
                        {
                            if (!PushPackages.showRetryQuestion("CSV file path is not valid or not exist", "Cancel", ConsoleColor.Red))
                            {
                                WriteLine("\nOperation cancelled", ConsoleColor.Red);
                                return;
                            }
                            continue;
                        }
                        Write("\nCSV file: ", ConsoleColor.DarkYellow);
                        WriteLine($"{csvPath}\n");
                        DownloadPackages.installedPackages = File.ReadAllLines(csvPath);
                        break;
                    }
                    break;

                case '4':
                    includeAll = true;
                    DownloadPackages.installedPackages = new string[0];
                    break;
            }

            string destFolder = null;
            while (true)
            {
                destFolder = await GeneralPicker.ShowSelectFolderDialogAsync("Please select destination folder for downloaded CAB files.");
                if (string.IsNullOrEmpty(destFolder))
                {
                    if (!PushPackages.showRetryQuestion("Destination folder is not valid!, retry?", "Cancel", ConsoleColor.Red))
                    {
                        WriteLine("\nOperation cancelled", ConsoleColor.Red);
                        return;
                    }
                    continue;
                }
                break;
            }

            Write("\nTarget build      : ", ConsoleColor.DarkGray);
            WriteLine(targetBuild, ConsoleColor.Blue);
            Write("Destination folder: ", ConsoleColor.DarkGray);
            WriteLine(destFolder, ConsoleColor.DarkYellow);

            bool isFeatureInstalled = DownloadPackages.OnlineUpdate(targetBuild, includeAll);
            if (!includeAll)
            {
                PromptOnlineFeatureSelection(targetBuild, isFeatureInstalled);
            }

            Write("Matched packages  : ", ConsoleColor.DarkGray);
            WriteLine(DownloadPackages.filteredPackages.Count.ToString(), ConsoleColor.Green);

            if (DownloadPackages.filteredPackages.Count == 0)
            {
                WriteLine("\nNo matching packages found to download.", ConsoleColor.Red);
                return;
            }

            WriteLine("\nDOWNLOADING PACKAGES", ConsoleColor.DarkGray);
            await WaitForInternetAsync();

            ConsoleStyle("\nDOWNLOADING PACKAGES", 1, ConsoleColor.DarkGray);
            bool downloaded = await DownloadPackages.DownloadUpdate(targetBuild, destFolder);
            if (downloaded)
            {
                WriteLine($"\nCAB files saved to:\n{destFolder}", ConsoleColor.Green);
            }
        }

        static async Task StartUpdater()
        {
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11;
                appendAppInfoToLog();
                ConsoleStyle("", 0, ConsoleColor.Blue);

                // Load rules / configs
                LoadUpdateRules();

                if (!IsRunningAsAdministrator())
                {
                    WriteLine("Please start the app as Administrator", ConsoleColor.Red);
                    await printLogLocation();
                    return;
                }

                // Extract important tools if not ready
                string startPath = AppDomain.CurrentDomain.BaseDirectory;
                string zipPath = Path.Combine(startPath, @"Resources\i386.zip");
                string extractPath = Path.Combine(startPath, @"Resources");
                string i386Dir = Path.Combine(extractPath, "i386");
                string hashFilePath = Path.Combine(startPath, @"Resources\hash.txt");

                PushPackages.stopRunningApps("updateapp.exe");
                PushPackages.stopRunningApps("wpimage.exe");
                PushPackages.stopRunningApps("iutool.exe");

                if (File.Exists(zipPath))
                {
                    string currentHash = CalculateMD5(zipPath);
                    string previousHash = File.Exists(hashFilePath) ? File.ReadAllText(hashFilePath).Trim() : null;
                    if (!string.Equals(currentHash, previousHash, StringComparison.OrdinalIgnoreCase) || !Directory.Exists(i386Dir))
                    {
                        if (Directory.Exists(i386Dir))
                        {
                            Directory.Delete(i386Dir, true);
                        }
                        ConsoleStyle("Extracting resources, please wait...", 0, ConsoleColor.Blue);
                        ZipFile.ExtractToDirectory(zipPath, extractPath);
                        File.WriteAllText(hashFilePath, currentHash);
                    }
                }

                EnsureCompatibleWimgapi(extractPath);
                if (!Console.IsOutputRedirected)
                {
                    try { Console.ResetColor(); } catch { }
                }

                await PushPackages.safeMountCheck();
                await PushPackages.CleanupStaleWimMounts();

                const int maxCleanAttempts = 3;
                for (int cleanAttempt = 1; cleanAttempt <= maxCleanAttempts && Directory.Exists(tempDirectory); cleanAttempt++)
                {
                    try
                    {
                        Directory.Delete(tempDirectory, true);
                    }
                    catch (Exception ex)
                    {
                        showTitleWaitMessage(true, "Cleaning temp");
                        int batExitCode = -1;
                        using (var deleteProcess = new Process())
                        {
                            deleteProcess.StartInfo.FileName = toolsDirectory + $@"\cleanTemp.bat";
                            deleteProcess.StartInfo.Arguments = $"{tempDirectory} \"{logFile}\"";
                            deleteProcess.StartInfo.UseShellExecute = false;
                            deleteProcess.StartInfo.CreateNoWindow = true;
                            deleteProcess.Start();
                            deleteProcess.WaitForExit();
                            batExitCode = deleteProcess.ExitCode;
                        }
                        showTitleWaitMessage(false);
                        if (Directory.Exists(tempDirectory) && batExitCode != 0)
                        {
                            appendLog($"[CLEANUP] Temp delete attempt {cleanAttempt}/{maxCleanAttempts} failed: {ex.Message}\n");
                            if (cleanAttempt < maxCleanAttempts)
                            {
                                ConsoleStyle(ex.Message, 0, ConsoleColor.Red);
                                WriteLine($"\nRetrying ({cleanAttempt}/{maxCleanAttempts})...", ConsoleColor.DarkYellow);
                                await Task.Delay(2000);
                                await retryWithCount(3);
                            }
                            else
                            {
                                WriteLine($"\nWarning: Could not fully clean '{tempDirectory}' ({ex.Message}). Continuing...", ConsoleColor.DarkYellow);
                            }
                        }
                    }
                }
                Directory.CreateDirectory(tempDirectory);

                // Expected errors simulate check (For testing only)
                //PushPackages.isErrorExpected("Blabla 0x80070002", "updateapp.exe", "iutocbsfromupdateoutput");
                //PushPackages.isErrorExpected("Blabla 0x80004001", "updateapp.exe", "iutocbsfromupdateoutput");
                //PushPackages.isErrorExpected("Blabla 0x80070002", "updateapp.exe", "install .....");
                //PushPackages.isErrorExpected("Blabla 0x80004001", "updateapp.exe", "install .....");

                // Get update target
                WriteLine("\n[UPDATE TARGET]: ", ConsoleColor.DarkGray);
                WriteLine("1. Device", ConsoleColor.Green);
                WriteLine("2. FFU", ConsoleColor.Blue);
                WriteLine("3. IUTool", ConsoleColor.DarkGray);
                WriteLine("4. Download CABs", ConsoleColor.DarkYellow);
                Write("Choice: ", ConsoleColor.Magenta);
                ConsoleKeyInfo updateTargetAction;
                do
                {
                    updateTargetAction = Console.ReadKey(true);
                }
                while (!IsNumberKeyOrNumpad(updateTargetAction, new char[] { '1', '2', '3', '4' }));
                char targetDigit = NormalizeNumberKey(updateTargetAction);
                Write(targetDigit.ToString() + "\n");

                if (targetDigit == '4')
                {
                    await HandleDownloadCabsMode();
                    await printLogLocation();
                    return;
                }

                pushToFFU = targetDigit == '2';
                iutoolMode = targetDigit == '3';

                if (pushToFFU)
                {
                ffuPathArea:
                    // Ask for FFU
                    WriteLine("\n[NOTICE]", ConsoleColor.Red);
                    WriteLine("- After flashing FFU, phone will perform hard reset", ConsoleColor.DarkYellow);
                    WriteLine("- Ensure to have enough space at C: drive", ConsoleColor.Gray);
                    WriteLine("- No FFUs mounted before, check Computer Management", ConsoleColor.Gray);
                    WriteLine("- Don't interrupt the process until you asked for", ConsoleColor.Gray);
                    WriteLine("- Cleanup system temp after this process", ConsoleColor.Gray);

                    printFlashInstructions();

                    FFUPath = await GeneralPicker.ShowOpenFileDialogAsync("ffu");
                    Write("\nFFU path: ", ConsoleColor.DarkYellow);
                    WriteLine((FFUPath ?? string.Empty) + "\n");
                    if (FFUPath == null || FFUPath.Length == 0 || !File.Exists(FFUPath))
                    {
                        if (PushPackages.showRetryQuestion("FFU path is not valid or not exist", "Exit", ConsoleColor.Red))
                        {
                            goto ffuPathArea;
                        }
                        else
                        {
                            await printLogLocation();
                            return;
                        }
                    }

                    GetDeviceInfo.OfflineFFU();
                    useConnectedDevice = false;
                }
                else if (iutoolMode)
                {
                    // Ask for CSV
                    WriteLine("\n[IMPORTANT]", ConsoleColor.Red);
                    WriteLine("- You need to filter cabs correctly", ConsoleColor.DarkYellow);
                    WriteLine("- Use `Choose InstalledPackages.csv`", ConsoleColor.Gray);
                    WriteLine("- Or skip if you have cabs filtered", ConsoleColor.Gray);

                    WriteLine("");
                    WriteLine("[FILTER FILE]", ConsoleColor.DarkGray);
                    WriteLine("1. Choose `InstalledPackages.csv`", ConsoleColor.Green);
                    WriteLine("2. Skip", ConsoleColor.Blue);
                    Write("Choice: ", ConsoleColor.Magenta);
                    ConsoleKeyInfo csvTargetAction;
                    do
                    {
                        csvTargetAction = Console.ReadKey(true);
                    }
                    while (!IsNumberKeyOrNumpad(csvTargetAction, new char[] { '1', '2' }));
                    Write(csvTargetAction.KeyChar.ToString() + "\n");

                    if (csvTargetAction.KeyChar == '1')
                    {
                    chooseCSVArea:
                        var csvFilePath = await GeneralPicker.ShowOpenFileDialogAsync("csv");
                        if (string.IsNullOrEmpty(csvFilePath) || !File.Exists(csvFilePath))
                        {
                            WriteLine("\nFile path is not valid!", ConsoleColor.Red);
                            WriteLine("1. Retry");
                            WriteLine("2. Skip");
                            Write("Choice: ", ConsoleColor.Magenta);
                            ConsoleKeyInfo csvRetryAction;
                            do
                            {
                                csvRetryAction = Console.ReadKey(true);
                            }
                            while (!IsNumberKeyOrNumpad(csvRetryAction, new char[] { '1', '2' }));
                            Write(csvRetryAction.KeyChar.ToString() + "\n");
                            if (csvRetryAction.KeyChar == '1')
                            {
                                WriteLine("");
                                goto chooseCSVArea;
                            }
                        }
                        else
                        {
                            Write("\nCSV file: ", ConsoleColor.DarkYellow);
                            WriteLine($"{csvFilePath}\n");
                            DownloadPackages.installedPackages = File.ReadAllLines(csvFilePath);
                        }
                    }
                    else
                    {
                        WriteLine("User skipped `InstalledPackages.csv`\nAll packages will be pushed by default", ConsoleColor.DarkYellow);
                    }

                    GetDeviceInfo.OfflineIUTool();
                    useConnectedDevice = false;
                }

                var packagesReady = false;
                string selectedUpdateOption = "1";
                if (useConnectedDevice)
                {
                    if (!await ReadConnectedDeviceInfoAsync(false))
                    {
                        await printLogLocation();
                        return;
                    }

                    var availableUpdates = CheckForUpdates.AvailableUpdates();
                    if (CheckForUpdates.Choice == 1)
                    {
                        WriteLine("\nThe OS version didn't reached the minimum required build.", ConsoleColor.Red);
                        Write("Please update to ", ConsoleColor.Red);
                        Write("'8.10.14219.341'", ConsoleColor.Blue);
                        WriteLine(" at least.", ConsoleColor.Red);
                        await printLogLocation();
                        return;
                    }

                    Write("\nPUSH MODE: ", ConsoleColor.Gray);
                    WriteLine(pushToFFU ? (PushPackages.useOldTools ? "FFU (SPKG)" : "FFU (CBS)") : "DEVICE", pushToFFU ? (PushPackages.useOldTools ? ConsoleColor.DarkYellow : ConsoleColor.Blue) : ConsoleColor.Green);
                    if (pushToFFU && !string.IsNullOrEmpty(FFUPath))
                    {
                        WriteLine($"FFU PATH: {FFUPath}", ConsoleColor.DarkGray);
                    }

                    WriteLine("\nAVAILABLE UPDATES", ConsoleColor.DarkGray);
                    var updatesArray = availableUpdates.Split('\n');
                    foreach (var updateItem in updatesArray)
                    {
                        if (updateItem.IndexOf("Offline", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            WriteLine(updateItem, ConsoleColor.DarkYellow);
                        }
                        else
                        {
                            WriteLine(updateItem, ConsoleColor.Gray);
                        }
                    }

                    Write("Choice: ", ConsoleColor.Magenta);

                    if (CheckForUpdates.Choice == 7)
                    {
                        do
                        {
                            selectedUpdate = Console.ReadKey(true);
                        }
                        while (!IsNumberKeyOrNumpad(selectedUpdate, new char[] { '1' }));
                    }
                    else if (CheckForUpdates.Choice == 3 || CheckForUpdates.Choice == 4 || CheckForUpdates.Choice == 6)
                    {
                        do
                        {
                            selectedUpdate = Console.ReadKey(true);
                        }
                        while (!IsNumberKeyOrNumpad(selectedUpdate, new char[] { '1', '2' }));
                    }
                    else if (CheckForUpdates.Choice == 2 || CheckForUpdates.Choice == 5)
                    {
                        do
                        {
                            selectedUpdate = Console.ReadKey(true);
                        }
                        while (!IsNumberKeyOrNumpad(selectedUpdate, new char[] { '1', '2', '3' }));
                    }
                    else
                    {
                        WriteLine("\nSomething went wrong.", ConsoleColor.Red);
                        await printLogLocation();
                        return;
                    }
                    char selectedDigit = NormalizeNumberKey(selectedUpdate);
                    Write(selectedDigit + "\n");

                    selectedUpdateOption = selectedDigit.ToString();
                }
                else
                {
                    // Fall to offline by default
                    CheckForUpdates.Choice = 7;
                }

                string targetBuild = updateStructure[CheckForUpdates.Choice - 1, Convert.ToInt32(selectedUpdateOption) - 1];
                if (targetBuild == "Offline")
                {
                packagePathArea:
                    packagePath = await GeneralPicker.ShowSelectFolderDialogAsync();
                    if (string.IsNullOrEmpty(packagePath))
                    {
                        WriteLine("\nFolder path is not valid!", ConsoleColor.Red);
                        WriteLine("1. Retry");
                        WriteLine("2. Exit");
                        Write("Choice: ", ConsoleColor.Magenta);
                        ConsoleKeyInfo csvRetryAction;
                        do
                        {
                            csvRetryAction = Console.ReadKey(true);
                        }
                        while (!IsNumberKeyOrNumpad(csvRetryAction, new char[] { '1', '2' }));
                        Write(csvRetryAction.KeyChar.ToString() + "\n");
                        if (csvRetryAction.KeyChar == '1')
                        {
                            WriteLine("");
                            goto packagePathArea;
                        }
                        else
                        {
                            await printLogLocation();
                            return;
                        }
                    }

                    Write("\nOffline packages: ", ConsoleColor.DarkYellow);
                    WriteLine($"{packagePath}\n");
                    string[] folderFiles = new string[] { };
                    if (Directory.Exists(packagePath))
                    {
                        folderFiles = Directory.GetFiles(packagePath);
                        var hasValidPackages = folderFiles.Any(f => DownloadPackages.TryParsePackageFile(f, out _, out _, out _));
                        if (!hasValidPackages)
                        {
                            WriteLine("\nFolder don't have update packages (.cab / .spkg)!", ConsoleColor.Red);
                            WriteLine("1. Retry");
                            WriteLine("2. Exit");
                            Write("Choice: ", ConsoleColor.Magenta);
                            ConsoleKeyInfo packagesAction;
                            do
                            {
                                packagesAction = Console.ReadKey(true);
                            }
                            while (!IsNumberKeyOrNumpad(packagesAction, new char[] { '1', '2' }));
                            Write(packagesAction.KeyChar.ToString() + "\n");
                            if (packagesAction.KeyChar == '1')
                            {
                                WriteLine("");
                                goto packagePathArea;
                            }
                            else
                            {
                                await printLogLocation();
                                return;
                            }
                        }
                    }
                    if (folderFiles.Length > 0)
                    {
                        showFeaturesSelection(folderFiles);
                        WriteLine("\nPREPARING PACKAGES", ConsoleColor.DarkGray);
                        packagesReady = await DownloadPackages.OfflineUpdate(folderFiles);
                    }
                    else
                    {
                        WriteLine("\nFolder is empty or not exists!", ConsoleColor.Red);
                        WriteLine("1. Retry");
                        WriteLine("2. Exit");
                        Write("Choice: ", ConsoleColor.Magenta);
                        ConsoleKeyInfo packagesAction;
                        do
                        {
                            packagesAction = Console.ReadKey(true);
                        }
                        while (!IsNumberKeyOrNumpad(packagesAction, new char[] { '1', '2' }));
                        Write(packagesAction.KeyChar.ToString() + "\n");
                        if (packagesAction.KeyChar == '1')
                        {
                            WriteLine("");
                            goto packagePathArea;
                        }
                        else
                        {
                            await printLogLocation();
                            return;
                        }
                    }
                }
                else
                {
                    bool IsFeatureInstalled = DownloadPackages.OnlineUpdate(targetBuild);
                    PromptOnlineFeatureSelection(targetBuild, IsFeatureInstalled);

                    WriteLine("\nDOWNLOADING PACKAGES", ConsoleColor.DarkGray);
                    await WaitForInternetAsync();

                    ConsoleStyle("\nDOWNLOADING PACKAGES", 1, ConsoleColor.DarkGray);
                    packagesReady = await DownloadPackages.DownloadUpdate(targetBuild);
                }

                string messageBoxText = "";
                if (packagesReady)
                {
                    if (!pushToFFU)
                    {
                        WriteLine("\nPUSHING PACKAGES", ConsoleColor.DarkGray);
                        await PushPackages.StartUpdate();
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
                        if (PushPackages.lastIutoolExitCode == 0)
                        {
                            messageBoxText = $"Please navigate to update settings on your phone. As soon as the update shows progression, you can safely disconnect your phone from the PC.\n{commonErrors}";
                        }
                        else
                        {
                            string formattedExitCode = "0x" + PushPackages.lastIutoolExitCode.ToString("X");
                            messageBoxText = $"iutool.exe exited with code {formattedExitCode} ({PushPackages.lastIutoolExitCode}). Check the log for details.\n{commonErrors}";
                        }
                    }
                    else
                    {
                        WriteLine("\nPUSHING PACKAGES (FFU)", ConsoleColor.DarkGray);
                        await PushPackages.PushToFFU(DownloadPackages.FFUMountData);

                        var commonErrors = "Common Errors:";
                        commonErrors += "\n\n0x80070490: ERROR_NOT_FOUND\nSolution: Mount the FFU first\nThis also may happen if disk full";
                        commonErrors += "\n\n0x80070002: ERROR_FILE_NOT_FOUND";
                        commonErrors += "\n\n0x80070241: ERROR_INVALID_IMAGE_HASH";
                        commonErrors += "\n\n0x80188302: Package already present on the image";
                        commonErrors += "\n\n0x80188305: Duplicate packages found in update set";
                        messageBoxText = $"Ensure to clean system temp after this process\n\n{commonErrors}";
                    }
                }
                else
                {
                    if (DownloadPackages.FFUMountData != null)
                    {
                        await PushPackages.dismountFFUQuestion(DownloadPackages.FFUMountData);
                        DownloadPackages.FFUMountData = null;
                    }
                    WriteLine("\nOperation cancelled\n", ConsoleColor.Red);
                }

                Task exitPromptTask = printLogLocation();

                if (messageBoxText.Length > 0)
                {
                    MessageBox((IntPtr)0, messageBoxText, "Universal Updater.exe", 0);
                }

                await exitPromptTask;
            }
            catch (Exception ex)
            {
                appendLog($"[FATAL] {ex}\n");
                WriteLine($"\nUnexpected error: {ex.Message}", ConsoleColor.Red);
                if (DownloadPackages.FFUMountData != null)
                {
                    try
                    {
                        await PushPackages.dismountFFU(DownloadPackages.FFUMountData, null);
                    }
                    catch { }
                    DownloadPackages.FFUMountData = null;
                }
                await printLogLocation();
            }
        }

        static Task printLogLocation()
        {
            var previewLogFile = logFile.Replace($@"{Environment.CurrentDirectory}\", "");
            WriteLine($"\nFinished.\n\nFor more details check the logs\n{previewLogFile}\n", ConsoleColor.DarkGray);

            // Run this as task to avoid blocking the final dialog
            return Task.Run(() =>
            {
                WriteLine("\nDo you want to restart Universal Updater?", ConsoleColor.Blue);
                WriteLine("1. Yes");
                WriteLine("2. Exit");
                Write("Choice: ", ConsoleColor.Magenta);
                ConsoleKeyInfo packagesAction;
                do
                {
                    packagesAction = Console.ReadKey(true);
                }
                while (!IsNumberKeyOrNumpad(packagesAction, new char[] { '1', '2' }));
                Write(packagesAction.KeyChar.ToString() + "\n");
                if (packagesAction.KeyChar == '1')
                {
                    RestartApplication();
                }
                else
                {
                    Environment.Exit(0);
                }
            });
        }
        static void RestartApplication()
        {
            var exePath = Process.GetCurrentProcess().MainModule.FileName;
            Process.Start(exePath);
            Environment.Exit(0);
        }

        public static List<FeaturesItem> GlobalFeaturesList = new List<FeaturesItem>();
        static void showFeaturesSelection(string[] folderFiles)
        {
            GlobalFeaturesList.Clear();
            LoadUpdateRules(packagePath);
            try
            {
                var featuresFile = toolsDirectory + @"\features.json";
                if (File.Exists(featuresFile))
                {
                    try
                    {
                        var data = File.ReadAllText(featuresFile);
                        if (!string.IsNullOrEmpty(data))
                        {
                            var loaded = JsonConvert.DeserializeObject<List<FeaturesItem>>(data);
                            if (loaded != null)
                            {
                                GlobalFeaturesList = loaded;
                            }
                        }
                        else
                        {
                            appendLog("Universal Update features.json is empty!\n");
                        }
                    }
                    catch (Exception ex)
                    {
                        appendLog($"Universal Update features.json error: {ex.Message}\n");
                    }
                }

                // Scan for possible attached packages info
                foreach (var featureInfoCheck in folderFiles)
                {
                    try
                    {
                        // This method don't expect duplicated features
                        // Built-in features (DevMenu, Astoria, Acer, CMDI) shouldn't have attached info
                        if (featureInfoCheck.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                        {
                            // This expected to be feature info attached with the cabs if it contain (FeaturePackages)
                            var featureInfoCheckData = File.ReadAllText(featureInfoCheck);
                            if (featureInfoCheckData.Contains("FeaturePackages"))
                            {
                                if (!string.IsNullOrEmpty(featureInfoCheckData))
                                {
                                    try
                                    {
                                        var featureObject = JsonConvert.DeserializeObject<FeaturesItem>(featureInfoCheckData);
                                        if (featureObject != null)
                                        {
                                            GlobalFeaturesList.Add(featureObject);
                                        }
                                    }
                                    catch
                                    {
                                        // This file may have array instead
                                        var featuresObjects = JsonConvert.DeserializeObject<List<FeaturesItem>>(featureInfoCheckData);
                                        if (featuresObjects != null && featuresObjects.Count > 0)
                                        {
                                            foreach (var featureObject in featuresObjects)
                                            {
                                                if (featureObject != null)
                                                {
                                                    GlobalFeaturesList.Add(featureObject);
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception exf)
                    {
                        appendLog(exf.Message + "\n");
                    }
                }

                GlobalFeaturesList.Sort((x, y) => x.Order.CompareTo(y.Order));

                foreach (var feature in GlobalFeaturesList)
                {
                    feature.UpdatePresentState(folderFiles);
                }

                // Get only items that presented as packages in target folder
                var filteredFeatures = GlobalFeaturesList.Where(feature => feature.ShouldPresent).ToList();
                if (filteredFeatures.Count > 0)
                {
                    WriteLine("\n[AVAILABLE FEATURES]", ConsoleColor.DarkGray);
                    WriteLine("Toggle features using their number:", ConsoleColor.Blue, false, false);
                    WriteLine("", ConsoleColor.Gray, false, false);
                    for (int i = 0; i < filteredFeatures.Count; i++)
                    {
                        WriteLine("", ConsoleColor.Gray, false, false);
                    }

                    while (true)
                    {
                        if (!Console.IsOutputRedirected)
                        {
                            try
                            {
                                int newRow = Math.Max(Console.CursorTop - (filteredFeatures.Count + 1), 0);
                                Console.SetCursorPosition(0, newRow);
                            }
                            catch { }
                        }

                        // Display features with checkboxes
                        for (int i = 0; i < filteredFeatures.Count; i++)
                        {
                            if (filteredFeatures[i].ShouldPresent)
                            {
                                string checkbox = filteredFeatures[i].State ? "[ON]" : "[  ]";
                                ConsoleColor color = filteredFeatures[i].State ? ConsoleColor.Green : ConsoleColor.Red;
                                Write($"{i + 1}. ", color, false, false);
                                Write($"{checkbox} ", color, false, false);
                                Write($"{filteredFeatures[i].Name}", color, false, false);
                                WriteLine($" {filteredFeatures[i].Description}", filteredFeatures[i].DescriptionColor, false, false);
                            }
                            else
                            {
                                Write($"{i + 1}. [!] {filteredFeatures[i].Name}", ConsoleColor.DarkGray, false, false);
                                WriteLine($" (Cab not presented)", ConsoleColor.DarkGray, false, false);
                            }
                        }

                        if (!updateRules.ForcePushFeatures)
                        {
                            WriteLine($"0. Confirm", ConsoleColor.DarkYellow, false, false);
                            Write($"Choice: ", ConsoleColor.Magenta, false, false);

                            // Get user input
                            bool confirmOptions = false;
                            ConsoleKeyInfo input;
                            do
                            {
                                input = Console.ReadKey(true);
                                if (input.KeyChar == '0')
                                {
                                    confirmOptions = true;
                                    break;
                                }
                            }
                            while (input.KeyChar.ToString().Length == 0);

                            if (!confirmOptions)
                            {
                                if (int.TryParse(input.KeyChar.ToString(), out int index) && index > 0 && index <= filteredFeatures.Count)
                                {
                                    // Toggle the selected feature's check state
                                    if (filteredFeatures[index - 1].ShouldPresent)
                                    {
                                        filteredFeatures[index - 1].State = !filteredFeatures[index - 1].State;
                                    }
                                }
                            }
                            else
                            {
                                Write(input.KeyChar.ToString() + "\n", ConsoleColor.Gray, false, false);
                                break;
                            }
                        }
                        else
                        {
                            WriteLine($"Push all features enabled", ConsoleColor.Magenta);
                            break;
                        }
                    }

                    // Print the result to log
                    for (int i = 0; i < GlobalFeaturesList.Count; i++)
                    {
                        var feature = GlobalFeaturesList[i];
                        if (feature.ShouldPresent)
                        {
                            appendLog($"{feature.Name}: " + (feature.State ? "Enabled" : "Disabled") + "\n");
                        }
                        else
                        {
                            appendLog($"{feature.Name}: Cab not presented in files\n");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                WriteLine(ex.Message, ConsoleColor.Red);
            }
        }

        static void printRCSMessage()
        {
            WriteLine("Remove RCS feature?");
            Write("[Removal is ", ConsoleColor.DarkYellow);
            Write("not recommended ", ConsoleColor.Red);
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


        static System.Threading.Mutex mutex = new System.Threading.Mutex(true, "9245fe4a-universal-updater-9c1a04247482");
        static void Main(string[] args)
        {
            bool allowToRun = true;

            logFile = prepareLogFile("universal_updater.exe");
            new OutputCapture();

            if ((!IsDependenciesInstalled("Microsoft Visual C++ 2012 Redistributable (x86)")) &&
                ((!IsDependenciesInstalled("Microsoft Visual C++ 2012 x86 Minimum Runtime")) ||
                 (!IsDependenciesInstalled("Microsoft Visual C++ 2012 x86 Additional Runtime"))))
            {
                WriteLine("Error:\n  An assembly in the application dependencies manifest was not found:\n    package: 'Microsoft.Visual.C++.2012.Redistributable.(x86)'", ConsoleColor.Red);
                allowToRun = false;
            }
            else if ((!IsDependenciesInstalled("Microsoft Visual C++ 2013 Redistributable (x86)")) &&
                     ((!IsDependenciesInstalled("Microsoft Visual C++ 2013 x86 Minimum Runtime")) ||
                      (!IsDependenciesInstalled("Microsoft Visual C++ 2013 x86 Additional Runtime"))))
            {
                WriteLine("Error:\n  An assembly in the application dependencies manifest was not found:\n    package: 'Microsoft.Visual.C++.2013.Redistributable.(x86)'", ConsoleColor.Red);
                allowToRun = false;
            }

            try
            {
                if (!mutex.WaitOne(TimeSpan.Zero, true))
                {
                    WriteLine("Another instance of the application is already running.", ConsoleColor.Red);
                    allowToRun = false;
                }
            }
            catch
            {
            }

            if (allowToRun)
            {
                StartUpdater().GetAwaiter().GetResult();
            }
            else
            {
                Thread.Sleep(10000);
                Environment.Exit(0);
            }
        }

        static void PrintMachineInformation()
        {
            appendLog($"[SYSTEM INFO]\n");

            // OS Description
            appendLog($"Operating System: {RuntimeInformation.OSDescription}\n");

            // Process Architecture
            appendLog($"Process Architecture: {RuntimeInformation.ProcessArchitecture}\n");

            // OS Architecture
            appendLog($"OS Architecture: {RuntimeInformation.OSArchitecture}\n");

            // Framework Description
            appendLog($"Framework Description: {RuntimeInformation.FrameworkDescription}\n");

            // Environment Information
            appendLog($"Machine Name: {Environment.MachineName}\n");
            appendLog($"User Domain Name: {Environment.UserDomainName}\n");
            appendLog($"User Name: {Environment.UserName}\n");

            // Number of processors
            appendLog($"Processor Count: {Environment.ProcessorCount}\n");

            // System page size
            appendLog($"System Page Size: {Environment.SystemPageSize} bytes\n");

            // Is 64-bit OS
            appendLog($"Is 64-Bit OS: {Environment.Is64BitOperatingSystem}\n");

            // Is 64-bit Process
            appendLog($"Is 64-Bit Process: {Environment.Is64BitProcess}\n");
            appendLog($"\n");
        }

        static void ConsoleStyle(string text, int line, ConsoleColor color = ConsoleColor.White)
        {
            var versionInfo = FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location);
            lock (logLock)
            {
                if (!Console.IsOutputRedirected)
                {
                    try
                    {
                        Console.ResetColor();
                        int targetWidth = Math.Min(115, Console.LargestWindowWidth > 0 ? Console.LargestWindowWidth : 115);
                        int targetHeight = Math.Min(29, Console.LargestWindowHeight > 0 ? Console.LargestWindowHeight : 29);
                        if (Console.BufferWidth < targetWidth)
                        {
                            Console.SetBufferSize(targetWidth, Math.Max(Console.BufferHeight, 400));
                        }
                        if (Console.BufferHeight < targetHeight)
                        {
                            Console.SetBufferSize(Math.Max(Console.BufferWidth, targetWidth), 400);
                        }
                        Console.SetWindowSize(targetWidth, targetHeight);
                        Console.SetBufferSize(targetWidth, 400);
                    }
                    catch { }

                    try
                    {
                        Console.Title = versionInfo.InternalName;
                    }
                    catch { }

                    try
                    {
                        Console.Clear();
                    }
                    catch { }
                }

                if (!Console.IsOutputRedirected) try { Console.ForegroundColor = ConsoleColor.Magenta; } catch { }
                Console.Write(Path.GetFileNameWithoutExtension(versionInfo.InternalName));
                if (!Console.IsOutputRedirected) try { Console.ForegroundColor = ConsoleColor.DarkGray; } catch { }
                Console.WriteLine(" Version " + versionInfo.FileVersion);
                if (!Console.IsOutputRedirected) try { Console.ForegroundColor = ConsoleColor.Green; } catch { }
                Console.WriteLine("- " + versionInfo.LegalCopyright + " by " + versionInfo.CompanyName);
                if (!Console.IsOutputRedirected) try { Console.ForegroundColor = ConsoleColor.DarkGray; } catch { }
                Console.WriteLine("- Contributors: Bashar Astifan");
                if (!Console.IsOutputRedirected) try { Console.ForegroundColor = ConsoleColor.DarkYellow; } catch { }
                Console.WriteLine("- This app provided AS-IS without any warranty");
                if (!Console.IsOutputRedirected) try { Console.ForegroundColor = ConsoleColor.DarkGray; } catch { }
                Console.WriteLine("- GitHub: https://github.com/fadilfadz01/Universal_Updater");

                Console.WriteLine(string.Empty);
                if (!Console.IsOutputRedirected) try { Console.ForegroundColor = color; } catch { }
                if (line == 0)
                {
                    Console.Write(text);
                }
                else
                {
                    Console.WriteLine(text);
                }
                if (!Console.IsOutputRedirected) try { Console.ResetColor(); } catch { }
            }
        }
        public static void showTitleWaitMessage(bool state, string extraDetails = "")
        {
            string extraTitleText = "";
            if (state)
            {
                if (extraDetails.Length > 0)
                {
                    extraTitleText = $" [{extraDetails} in progress, please wait...]";
                }
                else
                {
                    extraTitleText = " (Please wait...)";
                }
            }
            try
            {
                Console.Title = FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location).InternalName + extraTitleText;
            }
            catch { }
        }
        static void appendAppInfoToLog()
        {
            var versionInfo = FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location);
            appendLog(versionInfo.InternalName + "\n");
            appendLog("Version " + versionInfo.FileVersion + "\n");
            appendLog(versionInfo.LegalCopyright + " by " + versionInfo.CompanyName + "\n");
            appendLog("This app provided AS-IS without any warranty" + "\n");
            appendLog("GitHub: https://github.com/fadilfadz01/Universal_Updater" + "\n");
            appendLog("IUTool: https://learn.microsoft.com/en-us/previous-versions/mt131833(v=vs.85)" + "\n\n");

            PrintMachineInformation();
        }

        public static string GetResourceFile(string resourceName, bool dump = false)
        {
            var embeddedResource = Assembly.GetExecutingAssembly().GetManifestResourceNames().Where(s => s.IndexOf(resourceName, StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
            if (embeddedResource.Length > 0 && !string.IsNullOrWhiteSpace(embeddedResource[0]))
            {
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(embeddedResource[0]))
                {
                    if (stream == null)
                    {
                        return null;
                    }
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
            using (RegistryKey Key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Classes\Installer\Dependencies"))
            {
                if (Key == null)
                    return false;

                foreach (string subKeyName in Key.GetSubKeyNames())
                {
                    using (RegistryKey SubKey = Key.OpenSubKey(subKeyName))
                    {
                        var displayName = SubKey?.GetValue("DisplayName")?.ToString();
                        if (!string.IsNullOrEmpty(displayName) &&
                            displayName.IndexOf(dependency, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        static bool IsInternetConnected()
        {
            if (!NetworkInterface.GetIsNetworkAvailable())
                return false;

            using (var p = new Ping())
            {
                try
                {
                    PingReply reply = p.Send("www.google.com", 2500);
                    if (reply != null && reply.Status == IPStatus.Success)
                    {
                        return true;
                    }
                }
                catch { }
            }

            // Fallback to lightweight HTTP check in case ICMP is blocked by a firewall/ISP
            string[] probeUrls = new[]
            {
                "http://www.msftconnecttest.com/connecttest.txt",
                "http://download.windowsupdate.com/"
            };
            foreach (var probeUrl in probeUrls)
            {
                try
                {
                    var req = (HttpWebRequest)WebRequest.Create(probeUrl);
                    req.Method = "HEAD";
                    req.Timeout = 4000;
                    req.AllowAutoRedirect = true;
                    using (var resp = (HttpWebResponse)req.GetResponse())
                    {
                        return true;
                    }
                }
                catch (WebException wex)
                {
                    if (wex.Response != null)
                    {
                        wex.Response.Close();
                        return true;
                    }
                }
                catch { }
            }
            return false;
        }

        static string CalculateMD5(string filePath)
        {
            using (var md5 = MD5.Create())
            {
                using (var stream = File.OpenRead(filePath))
                {
                    byte[] hashBytes = md5.ComputeHash(stream);
                    return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
                }
            }
        }
        static bool IsRunningAsAdministrator()
        {
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
            {
                WindowsPrincipal principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
        }

        public static bool IsNumberKeyOrNumpad(ConsoleKeyInfo keyInfo, char[] validKeys)
        {
            foreach (char c in validKeys)
            {
                if ((keyInfo.Key == ConsoleKey.D0 + (c - '0')) ||
                    (keyInfo.Key == ConsoleKey.NumPad0 + (c - '0')))
                {
                    return true;
                }
            }
            return false;
        }

        public static char NormalizeNumberKey(ConsoleKeyInfo keyInfo)
        {
            if (keyInfo.Key >= ConsoleKey.D0 && keyInfo.Key <= ConsoleKey.D9)
            {
                return (char)('0' + (keyInfo.Key - ConsoleKey.D0));
            }
            if (keyInfo.Key >= ConsoleKey.NumPad0 && keyInfo.Key <= ConsoleKey.NumPad9)
            {
                return (char)('0' + (keyInfo.Key - ConsoleKey.NumPad0));
            }
            return keyInfo.KeyChar;
        }

        public static void LoadUpdateRules(string customPath = null)
        {
            var defaultPath = $@"{Environment.CurrentDirectory}\{toolsDirectory}";
            if (!string.IsNullOrEmpty(customPath))
            {
                defaultPath = customPath;
            }

            var rulesFile = Path.Combine(defaultPath, "rules.json");
            if (File.Exists(rulesFile))
            {
                try
                {
                    var data = File.ReadAllText(rulesFile);
                    if (!string.IsNullOrEmpty(data))
                    {
                        updateRules = JsonConvert.DeserializeObject<UpdateRules>(data) ?? new UpdateRules();
                    }
                    else
                    {
                        appendLog($"[RULES] rules file detected but it's empty\n{rulesFile}\n");
                    }
                }
                catch (Exception ex)
                {
                    appendLog(ex.Message + "\n");
                }
            }
            else
            {
                appendLog($"[RULES] rules file not exists at: {rulesFile}\n");
            }
        }
    }

    class FeaturesItem
    {
        public string Name;
        public string Description;
        public int Order = 0;
        public ConsoleColor DescriptionColor;
        public bool State;
        public bool ShouldPresent = false;
        public FeaturePackageItem[] FeaturePackages;

        [JsonConstructor]
        public FeaturesItem(string name, string description, FeaturePackageItem[] packages, bool state, ConsoleColor descriptionColor = ConsoleColor.DarkGray)
        {
            Name = name;
            Description = description;
            DescriptionColor = descriptionColor;
            FeaturePackages = packages;
            State = state;
        }

        public void UpdatePresentState(string[] folderFiles)
        {
            ShouldPresent = false;
            if (FeaturePackages == null || folderFiles == null)
                return;

            foreach (var testFeatureCab in FeaturePackages)
            {
                if (folderFiles.Any(file => DownloadPackages.MatchesPackageFeatureName(file, testFeatureCab.PackageName)))
                {
                    ShouldPresent = true;
                    break;
                }
            }
        }
    }
    class FeaturePackageItem
    {
        public string PackageName;
        public bool ReplaceStock; // Means original stock will be skipped
        public string TargetStockName;
        public FeaturePackageItem(string packageName, bool replaceStock = false, string targetStockName = null)
        {
            PackageName = packageName;
            ReplaceStock = replaceStock;
            TargetStockName = targetStockName;
        }
    }

    class UpdateRules
    {
        public string RecommendedDate = "";
        public bool ForceFilterMode = false;
        public bool ForcePushAllMode = false;
        public bool ForcePushFeatures = false;
    }
}