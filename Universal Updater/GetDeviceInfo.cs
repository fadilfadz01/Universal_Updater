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

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using ManagedWimLib;

namespace Universal_Updater
{
    class GetDeviceInfo
    {
        static readonly string[] deviceDetails = { "PhoneFriendlyName", "PhoneManufacturer", "PhoneManufacturerDisplayName", "PhoneManufacturerModelName", "PhoneHardwareVariant", "PhoneModelName", "PhoneHardwareRevision", "PhoneFirmwareRevision", "PhoneMobileOperatorName", "PhoneRadioSoftwareRevision", "PhoneROMLanguage", "PhoneSOCVersion" };
        static readonly string[] spaceAlign = { "                 ", "             ", "           ", "           ", "", "  ", "        ", "              ", "              ", "       ", "       ", "     ", "  ", "            ", "             ", "             " };
        static string[] deviceInfo;
        public static string[] OSVersion { get; private set; }
        public static string[] SerialNumber { get; private set; }
        private static bool _wimInitialized = false;

        private static void EnsureCleanPackagesDir(string serialKey)
        {
            string baseDir = Path.Combine(Program.filteredDirectory, serialKey);
            string packagesDir = Path.Combine(baseDir, "Packages");
            if (Directory.Exists(packagesDir))
            {
                Directory.Delete(packagesDir, true);
            }
            Directory.CreateDirectory(packagesDir);
        }

        public static void OfflineFFU()
        {
            SerialNumber = new string[] { "FFUPackages" };
            EnsureCleanPackagesDir(SerialNumber[0]);
        }

        public static void OfflineIUTool()
        {
            SerialNumber = new string[] { "IUToolPackages" };
            EnsureCleanPackagesDir(SerialNumber[0]);
        }

        public static int GetLog()
        {
            Program.showTitleWaitMessage(true, "getdulogs.exe");
            try
            {
                using (var proc = new Process())
                {
                    proc.StartInfo.FileName = Path.Combine(Program.toolsDirectory, "i386", "getdulogs.exe");
                    proc.StartInfo.Arguments = $@"-o ""{Path.Combine(Program.tempDirectory, "DeviceLog.cab")}""";
                    proc.StartInfo.RedirectStandardOutput = true;
                    proc.StartInfo.RedirectStandardError = true;
                    proc.StartInfo.RedirectStandardInput = true;
                    proc.StartInfo.UseShellExecute = false;
                    proc.StartInfo.CreateNoWindow = true;

                    var outTask = Task<string>.Factory.StartNew(() => string.Empty);
                    var errTask = Task<string>.Factory.StartNew(() => string.Empty);
                    proc.Start();
                    outTask = proc.StandardOutput.ReadToEndAsync();
                    errTask = proc.StandardError.ReadToEndAsync();

                    if (proc.WaitForExit(60000))
                    {
                        try { Task.WaitAll(new Task[] { outTask, errTask }, 2000); } catch { }
                        if (proc.ExitCode != 0)
                        {
                            string details = (outTask.Status == TaskStatus.RanToCompletion ? outTask.Result : "") +
                                             (errTask.Status == TaskStatus.RanToCompletion ? errTask.Result : "");
                            if (!string.IsNullOrWhiteSpace(details))
                            {
                                Program.appendLog($"[getdulogs.exe] {details.Trim()}\n");
                            }
                        }
                        return proc.ExitCode;
                    }
                    else
                    {
                        try { proc.Kill(); } catch { }
                        return -1;
                    }
                }
            }
            catch (Exception ex)
            {
                Program.appendLog($"[getdulogs.exe] Exception: {ex.Message}\n");
                return -1;
            }
            finally
            {
                Program.showTitleWaitMessage(false);
            }
        }

        public static async Task<string[]> ReadInfo()
        {
            string logOutput = string.Empty;
            using (var listProc = new Process())
            {
                listProc.StartInfo.FileName = Path.Combine(Program.toolsDirectory, "i386", "getdulogs.exe");
                listProc.StartInfo.Arguments = @"-l";
                listProc.StartInfo.RedirectStandardOutput = true;
                listProc.StartInfo.RedirectStandardError = true;
                listProc.StartInfo.RedirectStandardInput = true;
                listProc.StartInfo.UseShellExecute = false;
                listProc.StartInfo.CreateNoWindow = true;
                listProc.Start();
                var outTask = listProc.StandardOutput.ReadToEndAsync();
                var errTask = listProc.StandardError.ReadToEndAsync();
                await Task.Run(() => listProc.WaitForExit(30000));
                logOutput = await outTask;
                try { await errTask; } catch { }
            }
            File.WriteAllText(Program.tempDirectory + @"\DeviceInfo.txt", logOutput);
            CabExtractor.ExtractFile(Program.tempDirectory + @"\DeviceLog.cab", "OEMDevicePlatform.xml", Program.tempDirectory + @"\OEMDevicePlatform.xml");
            try
            {
                CabExtractor.ExtractFile(Program.tempDirectory + @"\DeviceLog.cab", "UsoTroubleshooting.reg", Program.tempDirectory + @"\UsoTroubleshooting.reg");
            }
            catch (FileNotFoundException) { }
            try
            {
                CabExtractor.ExtractFile(Program.tempDirectory + @"\DeviceLog.cab", "DuTroubleshooting.reg", Program.tempDirectory + @"\DuTroubleshooting.reg");
            }
            catch (FileNotFoundException) { }
            CabExtractor.ExtractFile(Program.tempDirectory + @"\DeviceLog.cab", "InstalledPackages.csv", Program.tempDirectory + @"\InstalledPackages.csv");
            CabExtractor.ExtractFile(Program.tempDirectory + @"\DeviceLog.cab", "OEMInput.xml", Program.tempDirectory + @"\OEMInput.xml");

            deviceInfo = new string[deviceDetails.Length + 4];
            SerialNumber = File.ReadAllLines(Program.tempDirectory + @"\DeviceInfo.txt").Where(i => i.Contains("Serial:")).ToArray();
            string[] platID = File.ReadAllLines(Program.tempDirectory + @"\OEMDevicePlatform.xml").Where(i => i.Contains("<DevicePlatformID>")).ToArray();
            if (SerialNumber.Length == 0 || platID.Length == 0)
            {
                Program.appendLog("[ReadInfo] Missing SerialNumber or DevicePlatformID in device logs.\n");
                return null;
            }
            deviceInfo[0] = SerialNumber[0];
            SerialNumber[0] = $@"{SerialNumber[0].Split(':')[1].Replace(" ", null).Trim()}";
            deviceInfo[1] = "PlatformID: " + platID[0].Split('>')[1].Split('<')[0];

            // Here we defined multiple tags for various cases
            var checkTagsArray = new string[] { "Microsoft.DEVICELAYOUT_QC", "Microsoft.MobileCore.UpdateOS" };
            for (int i = 0, j = 2; i < deviceDetails.Length; i++)
            {
                if (i == 6)
                {
                    try
                    {
                        OSVersion = File.ReadAllLines(Program.tempDirectory + @"\InstalledPackages.csv").Where(k => k.ContainsAny(checkTagsArray)).ToArray();
                    }
                    catch (FileNotFoundException)
                    {
                        return null;
                    }
                    if (OSVersion.Length == 0 || OSVersion[0].Split(',').Length < 3)
                    {
                        return null;
                    }
                    deviceInfo[i + j] = "OSVersion: " + OSVersion[0].Split(',')[2];
                    j++;
                }
                string[] details = null;
                if (File.Exists(Program.tempDirectory + @"\UsoTroubleshooting.reg")) details = File.ReadAllLines(Program.tempDirectory + @"\UsoTroubleshooting.reg").Where(l => l.Contains(deviceDetails[i])).ToArray();
                else if (File.Exists(Program.tempDirectory + @"\DuTroubleshooting.reg")) details = File.ReadAllLines(Program.tempDirectory + @"\DuTroubleshooting.reg").Where(l => l.Contains(deviceDetails[i])).ToArray();
                if (details != null && details.Length > 0)
                {
                    deviceInfo[i + j] = details[0].Replace("    ", null).Replace("\"", null).Replace("=", ": ").Replace("Phone", null);
                }
                else
                {
                    deviceInfo[i + j] = deviceDetails[i].Replace("Phone", "") + ": ";
                }
            }
            string[] res = File.ReadAllLines(Program.tempDirectory + @"\OEMInput.xml").Where(i => i.Contains("<Resolution>")).ToArray();
            deviceInfo[deviceInfo.Length - 1] = res.Length > 0 ? "Resolution: " + res[0].Split('>')[1].Split('<')[0] : "Resolution: Unknown";

            EnsureCleanPackagesDir(SerialNumber[0]);
            File.Copy(Program.tempDirectory + @"\DeviceLog.cab", Program.filteredDirectory + $@"\{SerialNumber[0]}\DeviceLog.cab", true);
            var infoLines = new List<string>(deviceInfo.Length);
            for (int i = 0; i < deviceInfo.Length; i++)
            {
                var splitDetails = deviceInfo[i].Split(new[] { ':' }, 2);
                deviceInfo[i] = splitDetails[0] + ":" + spaceAlign[i] + (splitDetails.Length > 1 ? splitDetails[1] : "");
                infoLines.Add(deviceInfo[i]);
            }
            File.WriteAllLines(Program.filteredDirectory + $@"\{SerialNumber[0]}\DeviceInfo.txt", infoLines);
            return deviceInfo;
        }

        public static async Task printFFUDetails(FFUEntry FFUMountData)
        {
            string mountedDrive = FFUMountData.MountPath.TrimEnd('\\'); // Change this to your mounted drive letter
            string softwareHivePath = System.IO.Path.Combine(mountedDrive, @"Windows\System32\config\SOFTWARE");
            Program.appendLog($"[FFU] Reading FFU registry at:\n{softwareHivePath}\n");
            if (File.Exists(softwareHivePath))
            {
                List<string> labels = new List<string>();
                List<string> values = new List<string>();

                try
                {
                    var FFUInfoOutput = await PushPackages.RunBatWithLog("ffuinfo.bat", FFUMountData, true);
                    if (!string.IsNullOrEmpty(FFUInfoOutput))
                    {
                        var FFUInfoOutputData = FFUInfoOutput.Split('\n');
                        var skipList = new string[] { "SystemRoot", "BuildGUID", "PhoneOEMSupportLink", "InstallDate", "EditionID", "PhoneSupportLink", "SoftwareType", "RegisteredOrganization", "PhoneSupportPhoneNumber", "PhoneMobileOperatorDisplayName" };
                        foreach (var InfoLine in FFUInfoOutputData)
                        {
                            if (InfoLine.IndexOf(":") >= 0)
                            {
                                var InfoLineSplit = InfoLine.Split(new[] { ':' }, 2);
                                if (InfoLineSplit.Length < 2) continue;
                                var label = InfoLineSplit[0].Trim();
                                var value = InfoLineSplit[1].Trim();
                                if (!label.ContainsAny(skipList))
                                {
                                    labels.Add(label);
                                    values.Add(value);
                                }
                            }
                        }

                    }
                    else
                    {
                        Program.WriteLine("Failed to access the FFU registry, check logs.", ConsoleColor.DarkYellow);
                    }
                }
                catch (Exception ex)
                {
                    Program.WriteLine(ex.Message, ConsoleColor.DarkYellow);
                }

                var OEMInputPath = mountedDrive + "\\Windows\\ImageUpdate\\OEMInput.xml";
                var OEMDevicePlatformPath = mountedDrive + "\\Windows\\ImageUpdate\\OEMDevicePlatform.xml";
                if (File.Exists(OEMDevicePlatformPath))
                {
                    string[] platID = File.ReadAllLines(OEMDevicePlatformPath).Where(i => i.Contains("<DevicePlatformID>")).ToArray();
                    if (platID.Length > 0 && platID[0].IndexOf('>') >= 0 && platID[0].IndexOf('<', platID[0].IndexOf('>')) >= 0)
                    {
                        labels.Add("PlatformID");
                        values.Add(platID[0].Split('>')[1].Split('<')[0]);
                    }
                }

                if (File.Exists(OEMInputPath))
                {
                    string[] res = File.ReadAllLines(OEMInputPath).Where(i => i.Contains("<Resolution>")).ToArray();
                    if (res.Length > 0 && res[0].IndexOf('>') >= 0 && res[0].IndexOf('<', res[0].IndexOf('>')) >= 0)
                    {
                        labels.Add("Resolution");
                        values.Add(res[0].Split('>')[1].Split('<')[0]);
                    }
                }

                if (labels.Count > 0)
                {
                    Program.WriteLine("");
                    Program.WriteLine($"\n[FFU INFO]", ConsoleColor.DarkGray);

                    int maxLabelWidth = 0;
                    foreach (var label in labels)
                    {
                        if (label.Length > maxLabelWidth)
                            maxLabelWidth = label.Length;
                    }
                    for (int i = 0; i < labels.Count; i++)
                    {
                        Program.Write($"{labels[i].PadRight(maxLabelWidth)}: ", ConsoleColor.Gray);
                        Program.WriteLine($"{values[i]}");
                    }
                }
                else
                {
                    // Something is wrong
                    Program.WriteLine("FFU info empty!, check logs.", ConsoleColor.DarkYellow);
                }
            }
            else
            {
                Program.appendLog($"Cannot find or read FFU registry path at:\n{softwareHivePath}\n");
            }
        }

        public static Task<List<string>> GetInstalledPackagesFromFFU(FFUEntry FFUMountData)
        {
            return Task.Run(() =>
            {
                List<string> installedPackages = new List<string>();

                var mountedPath = FFUMountData.MountPath.TrimEnd('\\');
                var UpdateOS = $"MainOS:\\Programs\\UpdateOS\\UpdateOS.wim";

                // CBS
                var CBSPath = $"MainOS:\\Windows\\servicing\\Packages";
                var CBSFullPaths = new string[] {
                $"MainOS:\\Data\\Windows\\servicing\\Packages",
                $"MainOS:\\EFIESP\\Windows\\servicing\\Packages"
                };

                // SPKG
                var SPKGPath = $"MainOS:\\Windows\\Packages\\DsmFiles";
                var SPKGFullPaths = new string[] {
                $"MainOS:\\Data\\Windows\\Packages\\DsmFiles",
                $"MainOS:\\EFIESP\\Windows\\Packages\\DsmFiles"
                };

                string[] expectedLocations = new string[] {
                CBSPath,
                SPKGPath
                };
                foreach (var checkLocation in expectedLocations)
                {
                    AddFilesFromFolder(checkLocation, mountedPath, ref installedPackages, "FFU");
                    if (installedPackages.Count > 0)
                    {
                        PushPackages.useOldTools = checkLocation == SPKGPath;
                        Program.appendLog($"[FFU] FFU type expected to be: ({(PushPackages.useOldTools ? "SPKG" : "CBS")}) \n");
                        break;
                    }
                }
                if (!PushPackages.useOldTools)
                {
                    // Extra search in CBS list
                    foreach (var extraCBSPath in CBSFullPaths)
                    {
                        AddFilesFromFolder(extraCBSPath, mountedPath, ref installedPackages, "FFU CBS");
                    }
                }
                else
                {
                    // Extra search in SPKG list
                    foreach (var extraSPKGPath in SPKGFullPaths)
                    {
                        AddFilesFromFolder(extraSPKGPath, mountedPath, ref installedPackages, "FFU SPKG");
                    }
                }

                // Try to extract more from UpdateOS.wim
                var UpdateOSClean = UpdateOS.Replace("MainOS:", mountedPath);
                if (File.Exists(UpdateOSClean))
                {
                    if (InitNativeLibrary())
                    {
                        Program.WriteLine("Fetching information from UpdateOS.wim", ConsoleColor.Blue);
                        try
                        {
                            using (var wimHandle = Wim.OpenWim(UpdateOSClean, OpenFlags.DEFAULT))
                            {
                                var imageCount = wimHandle.GetWimInfo().ImageCount;
                                for (int i = 1; i <= imageCount; i++)
                                {
                                    if (!PushPackages.useOldTools)
                                    {
                                        AddFilesFromFolder(wimHandle, i, CBSPath, ref installedPackages, "UpdateOS");
                                    }
                                    else
                                    {
                                        AddFilesFromFolder(wimHandle, i, SPKGPath, ref installedPackages, "UpdateOS");
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Program.WriteLine(ex.Message, ConsoleColor.Red);
                        }
                    }
                    else
                    {
                        Program.WriteLine("WimLib failed, reading UpdateOS.wim skipped!", ConsoleColor.DarkYellow);
                    }
                }
                else
                {
                    Program.appendLog($"[UpdateOS] File not found: {UpdateOSClean}\n");
                }
                return installedPackages.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            });
        }

        public static void AddFilesFromFolder(string folderPath, string mountedPath, ref List<string> installedPackages, string logTag)
        {
            var PathClean = folderPath.Replace("MainOS:", mountedPath);
            if (Directory.Exists(PathClean))
            {
                var listOfFiles = Directory.GetFiles(PathClean);
                var uniqueFileNames = listOfFiles.Select(file => Path.GetFileNameWithoutExtension(file)).Distinct().ToList();
                Program.appendLog($"[{logTag}] Unique names ({uniqueFileNames.Count}) detected at: {PathClean}\n");
                if (uniqueFileNames.Count > 0)
                {
                    foreach (var file in uniqueFileNames)
                    {
                        installedPackages.Add(getCleanName(file));
                    }
                }
            }
            else
            {
                Program.appendLog($"[UpdateOS] Folder not found: {PathClean}\n");
            }
        }
        public static void AddFilesFromFolder(Wim wimHandle, int i, string folderPath, ref List<string> installedPackages, string logTag)
        {
            var PathClean = folderPath.Replace("MainOS:\\", "");
            if (wimHandle.DirExists(i, PathClean))
            {
                var wimPackagesTemp = Program.tempDirectory + "\\wimPackages";
                if (Directory.Exists(wimPackagesTemp))
                {
                    Directory.Delete(wimPackagesTemp, true);
                }
                Directory.CreateDirectory(wimPackagesTemp);

                wimHandle.ExtractPath(i, $"{Environment.CurrentDirectory}\\{wimPackagesTemp}", $"{PathClean}", ExtractFlags.NO_ACLS | ExtractFlags.NO_ATTRIBUTES | ExtractFlags.NO_PRESERVE_DIR_STRUCTURE);

                var fullPathCheck = $"{wimPackagesTemp}\\" + Path.GetFileName(PathClean);
                if (Directory.Exists(fullPathCheck))
                {
                    var listOfFiles = Directory.GetFiles(fullPathCheck);
                    var uniqueFileNames = listOfFiles.Select(file => Path.GetFileNameWithoutExtension(file)).Distinct().ToList();
                    Program.appendLog($"[{logTag}] Unique names ({uniqueFileNames.Count}) detected at: {fullPathCheck}\n");
                    if (uniqueFileNames.Count > 0)
                    {
                        foreach (var file in uniqueFileNames)
                        {
                            installedPackages.Add(getCleanName(file));
                        }
                    }
                }
                else
                {
                    Program.appendLog($"[UpdateOS] Folder not found: {fullPathCheck}\n");
                }
            }
            else
            {
                Program.appendLog($"[UpdateOS] Folder not found: {PathClean}\n");
            }
        }

        public static bool InitNativeLibrary()
        {
            if (_wimInitialized)
            {
                return true;
            }

            string arch = null;
            switch (RuntimeInformation.ProcessArchitecture)
            {
                case Architecture.X86:
                    arch = "x86";
                    break;
                case Architecture.X64:
                    arch = "x64";
                    break;
                case Architecture.Arm64:
                    arch = "arm64";
                    break;
            }
            if (arch == null)
            {
                return false;
            }
            string libPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, arch, "libwim-15.dll");
            if (!File.Exists(libPath))
            {
                string asmDir = Path.GetDirectoryName(typeof(GetDeviceInfo).Assembly.Location);
                if (!string.IsNullOrEmpty(asmDir))
                {
                    string altPath = Path.Combine(asmDir, arch, "libwim-15.dll");
                    if (File.Exists(altPath))
                    {
                        libPath = altPath;
                    }
                }
            }

            if (!File.Exists(libPath))
            {
                Program.WriteLine($"Unable to find native library [{libPath}].", ConsoleColor.Red);
                return false;
            }

            Wim.GlobalInit(libPath);
            _wimInitialized = true;

            return true;
        }

        public static string getCleanName(string file)
        {
            string[] cleanupKeywords = new string[] { ".dsm", ".xml", ".mum", ".cat" };
            var cleanName = file;
            foreach (var cleanKey in cleanupKeywords)
            {
                cleanName = cleanName.Replace(cleanKey, "");
            }
            if (cleanName.IndexOf("~") >= 0)
            {
                cleanName = cleanName.Split('~')[0];
            }
            cleanName = cleanName.Trim();

            return cleanName;
        }
    }

    public static class StringExtensions
    {
        public static bool ContainsAny(this string str, params string[] values)
        {
            foreach (var value in values)
            {
                if (str.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }
    }
}