using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

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

        public static void Offline()
        {
            if (!Program.useConnectedDevice)
            {
                SerialNumber = new string[] { "Dummy" };
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
        }

        public static int GetLog()
        {
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
                return getDeviceInfoProcess.ExitCode;
            }
            else
            {
                getDeviceInfoProcess.Kill();
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