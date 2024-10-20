using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Management;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ManagedWimLib;

namespace Universal_Updater
{
    class GetDeviceInfo
    {
        static Process getDeviceInfoProcess;
        static readonly string[] deviceDetails = { "PhoneFriendlyName", "PhoneManufacturer", "PhoneManufacturerDisplayName", "PhoneManufacturerModelName", "PhoneHardwareVariant", "PhoneModelName", "PhoneHardwareRevision", "PhoneFirmwareRevision", "PhoneMobileOperatorName", "PhoneRadioSoftwareRevision", "PhoneROMLanguage", "PhoneSOCVersion" };
        static readonly string[] spaceAlign = { "                 ", "             ", "           ", "           ", "", "  ", "        ", "              ", "              ", "       ", "       ", "     ", "  ", "            ", "             ", "             " };
        static string[] deviceInfo;
        public static string[] OSVersion { get; private set; }
        public static string[] SerialNumber { get; private set; }

        public static void OfflineFFU()
        {
            SerialNumber = new string[] { "FFUPackages" };
            if (Directory.Exists(Program.filteredDirectory + $@"\{SerialNumber[0]}"))
            {
                Directory.Delete(Program.filteredDirectory + $@"\{SerialNumber[0]}", true);
                Directory.CreateDirectory(Program.filteredDirectory + $@"\{SerialNumber[0]}\Packages");
            }
            else
            {
                Directory.CreateDirectory(Program.filteredDirectory + $@"\{SerialNumber[0]}\Packages");
            }
        }

        public static int GetLog()
        {
            Program.showTitleWaitMessage(true, "getdulogs.exe");
            getDeviceInfoProcess = new Process();
            getDeviceInfoProcess.StartInfo.FileName = Program.toolsDirectory + @"\i386\getdulogs.exe";
            getDeviceInfoProcess.StartInfo.Arguments = @"-o """ + Program.tempDirectory + @"\DeviceLog.cab""";
            getDeviceInfoProcess.StartInfo.RedirectStandardOutput = true;
            getDeviceInfoProcess.StartInfo.RedirectStandardError = true;
            getDeviceInfoProcess.StartInfo.RedirectStandardInput = true;
            getDeviceInfoProcess.StartInfo.UseShellExecute = false;
            getDeviceInfoProcess.Start();
            // Better to set timeout and retry?
            if (getDeviceInfoProcess.WaitForExit(30000))
            {
                Program.showTitleWaitMessage(false);
                return getDeviceInfoProcess.ExitCode;
            }
            else
            {
                getDeviceInfoProcess.Kill();
                Program.showTitleWaitMessage(false);
                return -1;
            }
        }

        public static async Task<string[]> ReadInfo()
        {
            getDeviceInfoProcess.StartInfo.Arguments = @"-l";
            getDeviceInfoProcess.Start();
            getDeviceInfoProcess.WaitForExit();
            string logOutput = await getDeviceInfoProcess.StandardOutput.ReadToEndAsync();
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
            deviceInfo[0] = SerialNumber[0];
            SerialNumber[0] = $@"{SerialNumber[0].Split(':')[1].Replace(" ", null)}";
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
                    deviceInfo[i + j] = "OSVersion: " + OSVersion[0].Split(',')[2];
                    j++;
                }
                string[] details = null;
                if (File.Exists(Program.tempDirectory + @"\UsoTroubleshooting.reg")) details = File.ReadAllLines(Program.tempDirectory + @"\UsoTroubleshooting.reg").Where(l => l.Contains(deviceDetails[i])).ToArray();
                else if (File.Exists(Program.tempDirectory + @"\DuTroubleshooting.reg")) details = File.ReadAllLines(Program.tempDirectory + @"\DuTroubleshooting.reg").Where(l => l.Contains(deviceDetails[i])).ToArray(); ;
                if (details.Count() > 0)
                {
                    deviceInfo[i + j] = details[0].Replace("    ", null).Replace("\"", null).Replace("=", ": ").Replace("Phone", null);
                }
                else
                {
                    deviceInfo[i + j] = deviceDetails[i].Replace("Phone", "") + ": ";
                }
            }
            string[] res = File.ReadAllLines(Program.tempDirectory + @"\OEMInput.xml").Where(i => i.Contains("<Resolution>")).ToArray();
            deviceInfo[deviceInfo.Length - 1] = "Resolution: " + res[0].Split('>')[1].Split('<')[0];
            if (Directory.Exists(Program.filteredDirectory + $@"\{SerialNumber[0]}"))
            {
                Directory.Delete(Program.filteredDirectory + $@"\{SerialNumber[0]}", true);
                Directory.CreateDirectory(Program.filteredDirectory + $@"\{SerialNumber[0]}\Packages");
            }
            else
            {
                Directory.CreateDirectory(Program.filteredDirectory + $@"\{SerialNumber[0]}\Packages");
            }
            File.Copy(Program.tempDirectory + @"\DeviceLog.cab", Program.filteredDirectory + $@"\{SerialNumber[0]}\DeviceLog.cab");
            for (int i = 0; i < deviceInfo.Length; i++)
            {
                var splitDetails = deviceInfo[i].Split(':');
                deviceInfo[i] = splitDetails[0] + ":" + spaceAlign[i] + splitDetails[1];
                File.AppendAllText(Program.filteredDirectory + $@"\{SerialNumber[0]}\DeviceInfo.txt", deviceInfo[i] + "\n");
            }
            return deviceInfo;
        }

        public static async Task<List<string>> GetInstalledPackagesFromFFU(FFUEntry FFUMountData)
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
                    Program.appendLog($"FFU] FFU type expected to be: ({(PushPackages.useOldTools ? "SPKG" : "CBS")}) \n");
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
                        Program.WriteLine("Error reading WIM file: " + ex.Message, ConsoleColor.Red);
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
            return installedPackages;
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

                wimHandle.ExtractPath(i, $"{Environment.CurrentDirectory}\\{wimPackagesTemp}", $"{PathClean}", ExtractFlags.NO_PRESERVE_DIR_STRUCTURE);

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
            string libPath = Path.Combine(arch, "libwim-15.dll");

            if (!File.Exists(libPath))
            {
                Program.WriteLine($"Unable to find native library [{libPath}].", ConsoleColor.Red);
                return false;
            }

            Wim.GlobalInit(libPath);

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
                if (str.Contains(value))
                    return true;
            }
            return false;
        }
    }
}