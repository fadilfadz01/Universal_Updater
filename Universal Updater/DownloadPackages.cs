using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Universal_Updater
{
    class DownloadPackages
    {
        static Tuple<DateTime, long, long> DownloadingProgress = new Tuple<DateTime, long, long>(DateTime.MinValue, 0, 0);
        public static string[] installedPackages = new string[] { "Dummy" };
        static List<string> filteredPackages = new List<string>();
        static bool isFeatureInstalled = false;
        static bool filterCBSPackagesOnly = false;
        static readonly string[] knownPackages = { "ms_commsenhancementglobal.mainos", "ms_commsmessagingglobal.mainos", "microsoftphonefm.platformmanifest.efiesp", "microsoftphonefm.platformmanifest.mainos", "microsoftphonefm.platformmanifest.updateos", "UserInstallableFM.PlatformManifest" };
        public static FFUEntry FFUMountData = null;
        static readonly string[] skippedPackages = { };
        static readonly string[] packageCBSExtension = { ".cab", ".cbs_", "cbs." };
        static readonly string[] packageCBSExtensionRemoval = { ".cbsr", "cbsr." };
        static readonly string[] packageSPKGExtension = { ".spkg", ".spkg_", "spkg." };
        static readonly string[] packageSPKGExtensionRemoval = { ".spkr", "spkr." };
        static Uri downloadFile;

        public static string[] getExtensionsList()
        {
            if (!filterCBSPackagesOnly)
            {
                return packageSPKGExtension.Concat(packageSPKGExtensionRemoval).ToArray();
            }
            return packageCBSExtension.Concat(packageCBSExtensionRemoval).ToArray();
        }

        public static bool OnlineUpdate(string updateBuild)
        {
            // For pre-built in packages currently check for both CBS or SPKG,
            // those suppose to be sorted already for SPGK or CBS
            var cbsList = packageCBSExtension.Concat(packageCBSExtensionRemoval).ToArray();
            var spkgList = packageSPKGExtension.Concat(packageSPKGExtensionRemoval).ToArray();
            var targetExtensionList = cbsList.Concat(spkgList).ToArray();

            // Get file content only once
            var targetListFile = Program.GetResourceFile($"{updateBuild}.txt").Split('\n');
            Program.appendLog("\n[INSTALLED PACKAGES]\n");
            for (int i = 1; i < installedPackages.Length; i++)
            {
                for (int j = 0; j < targetExtensionList.Length; j++)
                {
                    var requiredPackages = targetListFile.Where(k => k.IndexOf(installedPackages[i].Split(',')[1] + targetExtensionList[j], StringComparison.OrdinalIgnoreCase) >= 0).FirstOrDefault();
                    if (requiredPackages != null)
                    {
                        if (requiredPackages.IndexOf("ms_projecta.mainos", StringComparison.OrdinalIgnoreCase) >= 0 || requiredPackages.IndexOf("MS_RCS_FEATURE_PACK.MainOS.cbsr", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            isFeatureInstalled = true;
                        }
                        if (!shouldSkip(requiredPackages, true))
                        {
                            filteredPackages.Add(requiredPackages);
                        }
                    }
                }
                Program.appendLog($"{installedPackages[i]}\n");
            }
            for (int i = 0; i < knownPackages.Length; i++)
            {
                if (string.Join("\n", filteredPackages).IndexOf(knownPackages[i], StringComparison.OrdinalIgnoreCase) < 0)
                {
                    var knownPackage = targetListFile.Where(j => j.IndexOf(knownPackages[i], StringComparison.OrdinalIgnoreCase) >= 0).FirstOrDefault();
                    if (knownPackage != null)
                    {
                        if (!shouldSkip(knownPackage, true))
                        {
                            filteredPackages.Add(knownPackage);
                        }
                    }
                }
            }
            return isFeatureInstalled;
        }

        public static async Task<bool> OfflineUpdate(string[] folderFiles)
        {
        filterPackages:
            Program.LoadUpdateRules(Program.packagePath);
            filteredPackages.Clear();
            var folderHasCBS = false;
            var folderHasSPKG = false;
            for (int i = 0; i < packageCBSExtension.Length; i++)
            {
                var testPackages = folderFiles.Where(k => k.IndexOf(packageCBSExtension[i], StringComparison.OrdinalIgnoreCase) >= 0 && !k.ContainsAny(packageSPKGExtension)).FirstOrDefault();
                if (testPackages != null)
                {
                    folderHasCBS = true;
                    break;
                }
            }
            for (int i = 0; i < packageSPKGExtension.Length; i++)
            {
                var testPackages = folderFiles.Where(k => k.IndexOf(packageSPKGExtension[i], StringComparison.OrdinalIgnoreCase) >= 0).FirstOrDefault();
                if (testPackages != null)
                {
                    folderHasSPKG = true;
                    break;
                }
            }

            // We show this question only if folder has mixed packages
            if (folderHasCBS && folderHasSPKG)
            {
                Program.Write("CBS", ConsoleColor.Blue);
                Program.Write(" and ", ConsoleColor.DarkYellow);
                Program.Write("SPKG", ConsoleColor.Blue);
                Program.WriteLine(" packages detected\nEnsure to use the correct type\nUsually SPKG used for WP8, CBS for W10M", ConsoleColor.DarkYellow);
                Program.WriteLine("1. Use CBS");
                Program.WriteLine("2. Use SPKG");
                Program.Write("Choice: ", ConsoleColor.Magenta);
                ConsoleKeyInfo packagesType;
                do
                {
                    packagesType = Console.ReadKey(true);
                }
                while (!Program.IsNumberKeyOrNumpad(packagesType, new char[] { '1', '2' }));
                Program.Write(packagesType.KeyChar.ToString() + "\n");
                filterCBSPackagesOnly = packagesType.KeyChar == '1';
            }
            else
            {
                // Fall to what exists
                filterCBSPackagesOnly = folderHasCBS;
            }



            ConsoleKeyInfo packagesFilterAction = default(ConsoleKeyInfo);
            if (!Program.updateRules.ForceFilterMode && !Program.updateRules.ForcePushAllMode)
            {
                // Allow user to push all package if he want
                Program.WriteLine("\nFilter packages options: ", ConsoleColor.Blue);
                Program.WriteLine("1. Filter packages", ConsoleColor.Green);
                Program.WriteLine("2. Include all", ConsoleColor.DarkYellow);
                Program.Write("Choice: ", ConsoleColor.Magenta);
                do
                {
                    packagesFilterAction = Console.ReadKey(true);
                }
                while (!Program.IsNumberKeyOrNumpad(packagesFilterAction, new char[] { '1', '2' }));
                Program.Write(packagesFilterAction.KeyChar.ToString() + "\n");
            }
            else
            {
                Program.appendLog("Always filter file detected, forcing packages filter\n");
            }

            var targetExtensionList = getExtensionsList();
            var previewType = "cbs";
            if (!filterCBSPackagesOnly)
            {
                previewType = "spkg";
            }

            if (Program.pushToFFU && FFUMountData == null)
            {
                Program.WriteLine("");
            // For FFU we start mount from here
            // Mount and fetch FFU info
            mountFFUArea:
                FFUMountData = await PushPackages.mountFFU(Program.FFUPath);
                if (FFUMountData == null)
                {
                    if (PushPackages.showRetryQuestion("Mount FFU failed!, retry?"))
                    {
                        goto mountFFUArea;
                    }
                    else
                    {
                        Program.WriteLine("Couldn't mount target FFU.", ConsoleColor.Red);
                        return false;
                    }
                }
                else
                {
                getInstalledPackagesArea:
                    installedPackages = (await GetDeviceInfo.GetInstalledPackagesFromFFU(FFUMountData)).ToArray();
                    if (installedPackages.Length == 0)
                    {
                        if (PushPackages.showRetryQuestion("Cannot get installed packages!, retry?"))
                        {
                            goto getInstalledPackagesArea;
                        }
                        else
                        {
                            Program.WriteLine("Couldn't mount target FFU.", ConsoleColor.Red);
                            return false;
                        }
                    }
                }

                await GetDeviceInfo.printFFUDetails(FFUMountData);
                Program.Write("\nPUSH MODE: ", ConsoleColor.Gray);
                Program.WriteLine((PushPackages.useOldTools ? "FFU (SPKG)" : "FFU (CBS)"), (PushPackages.useOldTools ? ConsoleColor.DarkYellow : ConsoleColor.Blue));
            }

            if (Program.updateRules.ForceFilterMode || packagesFilterAction.KeyChar == '1')
            {
                Program.WriteLine($"\nFiltering packages (type: {previewType}), please wait...", ConsoleColor.DarkGray);
                Program.appendLog("\n[INSTALLED PACKAGES]\n");
                for (int i = 0; i < installedPackages.Length; i++)
                {
                    for (int j = 0; j < targetExtensionList.Length; j++)
                    {
                        // Packages fetched from device info has `,`, from FFU usually clean
                        var checkName = installedPackages[i] + targetExtensionList[j];
                        if (checkName.IndexOf(",") >= 0)
                        {
                            checkName = installedPackages[i].Split(',')[1] + targetExtensionList[j];
                        }
                        var requiredPackages = folderFiles.Where(k => k.IndexOf(checkName, StringComparison.OrdinalIgnoreCase) >= 0).FirstOrDefault();
                        if (requiredPackages != null)
                        {
                            if (!shouldSkip(requiredPackages))
                            {
                                filteredPackages.Add(requiredPackages);
                            }
                        }
                    }
                    Program.appendLog($"{installedPackages[i]}\n");
                }

                for (int i = 0; i < knownPackages.Length; i++)
                {
                    if (string.Join("\n", filteredPackages).IndexOf(knownPackages[i], StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        var knownPackage = folderFiles.Where(j => j.IndexOf(knownPackages[i], StringComparison.OrdinalIgnoreCase) >= 0).FirstOrDefault();
                        if (knownPackage != null)
                        {
                            bool skipThisPackage = false;
                            if (filterCBSPackagesOnly)
                            {
                                foreach (var spkgExt in packageSPKGExtension)
                                {
                                    if (knownPackage.IndexOf(spkgExt, StringComparison.OrdinalIgnoreCase) >= 0)
                                    {
                                        skipThisPackage = true;
                                        break;
                                    }
                                }
                            }
                            else
                            {
                                if (knownPackage.IndexOf(".cbs_", StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    skipThisPackage = true;
                                    break;
                                }
                            }
                            if (!skipThisPackage && !shouldSkip(knownPackage))
                            {
                                filteredPackages.Add(knownPackage);
                            }
                        }
                    }
                }
            }
            else
            {
                Program.WriteLine($"\nAdding packages (type: {previewType}), please wait...", ConsoleColor.DarkGray);
                var spkgList = packageSPKGExtension.Concat(packageSPKGExtensionRemoval).ToArray();
                for (int j = 0; j < targetExtensionList.Length; j++)
                {
                    var requiredPackages = folderFiles.Where(k => k.IndexOf(targetExtensionList[j], StringComparison.OrdinalIgnoreCase) >= 0);
                    if (requiredPackages != null)
                    {
                        foreach (var requiredPackage in requiredPackages)
                        {
                            bool skipThisPackage = false;
                            if (filterCBSPackagesOnly)
                            {
                                if (requiredPackage.ContainsAny(spkgList))
                                {
                                    skipThisPackage = true;
                                    break;
                                }
                            }
                            else
                            {
                                if (!requiredPackage.ContainsAny(spkgList))
                                {
                                    skipThisPackage = true;
                                    break;
                                }
                            }
                            if (!skipThisPackage && !shouldSkip(requiredPackage))
                            {
                                filteredPackages.Add(requiredPackage);
                            }
                        }
                    }
                }
            }

            if (filteredPackages.Count > 0)
            {
                // Features packages
                foreach (var feature in Program.GlobalFeaturesList)
                {
                    if (feature.ShouldPresent)
                    {
                        if (feature.State)
                        {
                            for (int i = 0; i < feature.FeaturePackages.Length; i++)
                            {
                                var requiredPackages = folderFiles.Where(j => j.IndexOf(feature.FeaturePackages[i].PackageName, StringComparison.OrdinalIgnoreCase) >= 0).FirstOrDefault();
                                if (requiredPackages != null)
                                {
                                    if (feature.FeaturePackages[i].ReplaceStock)
                                    {
                                        // Ensure to remove stock on from the list
                                        var stockName = feature.FeaturePackages[i].TargetStockName;
                                        for (int j = 0; j < targetExtensionList.Length; j++)
                                        {
                                            var checkName = stockName + targetExtensionList[j];
                                            var packageRemoval = folderFiles.Where(a => a.IndexOf(checkName, StringComparison.OrdinalIgnoreCase) >= 0).FirstOrDefault();
                                            if (packageRemoval != null)
                                            {
                                                if (filteredPackages.Remove(packageRemoval))
                                                {
                                                    Program.WriteLine($"Stock ({stockName}) replaced with feature", ConsoleColor.DarkYellow);
                                                    Program.appendLog($"Package ({stockName}) replaced with feature ({feature.FeaturePackages[i].PackageName})\n");
                                                }
                                                else
                                                {
                                                    Program.WriteLine($"Stock ({stockName}) is not presented in filtered list!", ConsoleColor.DarkYellow);
                                                }
                                            }
                                        }
                                    }

                                    if (!shouldSkip(requiredPackages))
                                    {
                                        filteredPackages.Add(requiredPackages);
                                        Program.appendLog($"Feature Package ({requiredPackages}) added to filtered list\n");
                                    }
                                }
                            }
                        }
                        else
                        {
                            // Ensure package removed from the list
                            for (int i = 0; i < feature.FeaturePackages.Length; i++)
                            {
                                var requiredPackages = folderFiles.Where(j => j.IndexOf(feature.FeaturePackages[i].PackageName, StringComparison.OrdinalIgnoreCase) >= 0).FirstOrDefault();
                                if (requiredPackages != null)
                                {
                                    filteredPackages.Remove(requiredPackages);
                                }
                            }
                        }
                    }
                }

                var testCabFile = filteredPackages.FirstOrDefault();
                var certificateIssuer = "";
                var certificateDate = "";
                var certificateExpireDate = "";
                var dtFmt = "";
                bool isTestSigned = false;
                try
                {
                    X509Certificate cert = X509Certificate.CreateFromSignedFile(testCabFile);

                    certificateDate = cert.GetEffectiveDateString();
                    certificateExpireDate = cert.GetExpirationDateString();
                    certificateIssuer = cert.Issuer;
                    Match m = Regex.Match(certificateIssuer, @"CN=(.*?)(?=,)");
                    if (m.Success)
                    {
                        certificateIssuer = m.Groups[1].Value;
                    }

                    Program.WriteLine("\n[CERTIFICATE]", ConsoleColor.Green);
                    Program.Write("Issuer : ", ConsoleColor.DarkGray);
                    Program.WriteLine(certificateIssuer, ConsoleColor.DarkCyan);
                    Program.Write("Date   : ", ConsoleColor.DarkGray);

                    var startData = certificateDate.ToDate(ref dtFmt);
                    var expireData = certificateExpireDate.ToDate(ref dtFmt);
                    if (startData != null && startData.HasValue)
                    {
                        Program.Write(startData.Value.ToString("d"), ConsoleColor.DarkGray);
                        Program.Write(" - ", ConsoleColor.DarkGray);
                        Program.WriteLine(expireData.Value.ToString("d"), ConsoleColor.DarkGray);
                    }
                    else
                    {
                        Program.Write(certificateDate, ConsoleColor.DarkGray);
                        Program.Write(" - ", ConsoleColor.DarkGray);
                        Program.WriteLine(certificateExpireDate, ConsoleColor.DarkGray);
                    }
                    Program.Write("Type   : ", ConsoleColor.DarkGray);

                    // Currently we do basic check using [Issuer]
                    // Test signed mostly contain `Development` or `Test`
                    var hasDevelopmentKeyword = certificateIssuer.IndexOf("Development", StringComparison.OrdinalIgnoreCase) >= 0;
                    var hasTestKeyword = certificateIssuer.IndexOf("Test", StringComparison.OrdinalIgnoreCase) >= 0;
                    if ((hasDevelopmentKeyword || hasTestKeyword))
                    {
                        Program.Write("Test signed", ConsoleColor.DarkYellow);
                        Program.WriteLine(" (Please double check)", ConsoleColor.DarkGray);
                        isTestSigned = true;
                    }
                    else
                    {
                        Program.Write("Production signed", ConsoleColor.Green);
                        Program.WriteLine(" (Please double check)", ConsoleColor.DarkGray);
                    }
                }
                catch (Exception ex)
                {
                }

                DateTime? formatedDate = null;
                var expectedDateFormat = "";
                var dateModifiedString = "";
                try
                {
                    // Not sure how this works, but tested on Windows 11 and seems fine
                    var shellAppType = Type.GetTypeFromProgID("Shell.Application");
                    dynamic shellApp = Activator.CreateInstance(shellAppType);
                    var cabFolder = shellApp.NameSpace(testCabFile);

                    foreach (var item in cabFolder.Items())
                    {
                        dateModifiedString = (string)cabFolder.GetDetailsOf(item, 3);
                        // Fix possible encoding crap
                        byte[] bytes = Encoding.Default.GetBytes(dateModifiedString);
                        dateModifiedString = Encoding.UTF8.GetString(bytes).Replace("?", "");
                        formatedDate = dateModifiedString.ToDate(ref expectedDateFormat);
                        break;
                    }
                }
                catch (Exception ex)
                {
                }

                if (formatedDate != null && formatedDate.HasValue)
                {
                    var dateOnly = formatedDate.Value.Date;
                    Program.Write("Files  : " + dateOnly.ToString("d"), ConsoleColor.DarkGray);
                    var datePlusTwoDays = dateOnly.AddDays(2);
                    Program.WriteLine($" ({dtFmt})", ConsoleColor.DarkGray);
                    if (!Program.pushToFFU)
                    {
                        if (isTestSigned)
                        {
                            // Show this red label if we expect that packages are test signed
                            Program.Write("\n[IMPORTANT] ", ConsoleColor.DarkRed);
                            Program.Write("\nPackages expected to be ", ConsoleColor.DarkYellow);
                            Program.WriteLine("(Test Signed)", ConsoleColor.Blue);
                            if (string.IsNullOrEmpty(Program.updateRules.RecommendedDate))
                            {
                                Program.WriteLine("Set your device to the required date", ConsoleColor.DarkYellow);
                            }
                            else
                            {
                                Program.Write("Set your device to: ", ConsoleColor.DarkYellow);
                                Program.WriteLine(Program.updateRules.RecommendedDate, ConsoleColor.Green);
                            }
                        }
                    }
                }
                else
                {
                    Program.Write("Files  : " + dateModifiedString, ConsoleColor.DarkGray);
                    Program.WriteLine($" ({dtFmt})", ConsoleColor.DarkGray);
                }

                Program.Write("\nTotal expected packages: ", ConsoleColor.DarkGray);
                Program.WriteLine(filteredPackages.Count.ToString(), ConsoleColor.DarkYellow);
                Program.WriteLine("1. Push packages", ConsoleColor.Green);
                Program.WriteLine("2. Retry");
                Program.WriteLine("3. Exit");
                Program.Write("Choice: ", ConsoleColor.Magenta);
                ConsoleKeyInfo packagesAction;
                do
                {
                    packagesAction = Console.ReadKey(true);
                }
                while (!Program.IsNumberKeyOrNumpad(packagesAction, new char[] { '1', '2', '3' }));
                Program.Write(packagesAction.KeyChar.ToString() + "\n");

                if (packagesAction.KeyChar == '1')
                {
                    Program.WriteLine("\n[PREPARING]\nPlease wait...", ConsoleColor.DarkYellow);
                    for (int i = 0; i < filteredPackages.Where(j => !string.IsNullOrWhiteSpace(j)).Count(); i++)
                    {
                        Program.resetCursorPosition();
                        Program.WriteLine($@"[{i + 1}/{filteredPackages.Where(j => !string.IsNullOrWhiteSpace(j)).Count()}] {filteredPackages[i].Split('\\').Last()}", ConsoleColor.DarkGray);
                        File.Copy(filteredPackages[i], Program.filteredDirectory + $@"\{GetDeviceInfo.SerialNumber[0]}\Packages\{filteredPackages[i].Split('\\').Last()}", true);
                    }
                }
                else if (packagesAction.KeyChar == '2')
                {
                    Program.WriteLine("");
                    goto filterPackages;
                }
                else
                {
                    return false;
                }
            }
            else
            {
                Program.WriteLine("\nNo packages detected that match with your system!", ConsoleColor.Red);
                Program.WriteLine("1. Retry");
                Program.WriteLine("2. Exit");
                Program.Write("Choice: ", ConsoleColor.Magenta);
                ConsoleKeyInfo packagesAction;
                do
                {
                    packagesAction = Console.ReadKey(true);
                }
                while (!Program.IsNumberKeyOrNumpad(packagesAction, new char[] { '1', '2' }));
                Program.Write(packagesAction.KeyChar.ToString() + "\n");
                if (packagesAction.KeyChar == '1')
                {
                    Program.WriteLine("");
                    goto filterPackages;
                }
                else
                {
                    return false;
                }
            }

            return true;
        }

        public static async Task<bool> DownloadUpdate(string update)
        {
            WebClient client = new WebClient();
            Process downloadProcess = new Process();
            downloadProcess.StartInfo.FileName = Program.toolsDirectory + @"\wget.exe";
            downloadProcess.StartInfo.UseShellExecute = false;

            Program.WriteLine("\n[OUTPUT] (wget.exe)\nPlease wait...", ConsoleColor.DarkYellow);
            for (int i = 0; i < filteredPackages.Where(j => !string.IsNullOrWhiteSpace(j)).Count(); i++)
            {
                downloadFile = new Uri(filteredPackages[i]);
                Program.resetCursorPosition();
                Program.WriteLine($@"[{i + 1}/{filteredPackages.Where(j => !string.IsNullOrWhiteSpace(j)).Count()}] {downloadFile.LocalPath.Split('/').Last()}", ConsoleColor.DarkGray);
                if (update == "15254.603")
                {
                    downloadProcess.StartInfo.Arguments = $@"{downloadFile} --spider";
                    downloadProcess.StartInfo.RedirectStandardOutput = true;
                    downloadProcess.StartInfo.RedirectStandardError = true;
                    downloadProcess.StartInfo.RedirectStandardInput = true;
                    downloadProcess.Start();
                    downloadProcess.WaitForExit();
                    var logOutput = (await downloadProcess.StandardError.ReadToEndAsync()).Split('\n');
                    var fileSize = Convert.ToInt64(logOutput.Where(j => j.IndexOf("Length:", StringComparison.OrdinalIgnoreCase) >= 0).ToArray()[0].Split(' ')[1]);
                    do
                    {
                        downloadProcess.StartInfo.Arguments = $@"-q -c -P """ + Program.filteredDirectory + $@"\{GetDeviceInfo.SerialNumber[0]}\Packages"" {downloadFile} --no-check-certificate --show-progress";
                        downloadProcess.StartInfo.RedirectStandardOutput = false;
                        downloadProcess.StartInfo.RedirectStandardError = false;
                        downloadProcess.StartInfo.RedirectStandardInput = false;
                        downloadProcess.Start();
                        Console.Title = "Universal Updater.exe";
                        downloadProcess.WaitForExit();
                    }
                    while (fileSize != new FileInfo(Program.filteredDirectory + $@"\{GetDeviceInfo.SerialNumber[0]}\Packages\{downloadFile.LocalPath.Split('/').Last()}").Length);
                }
                else
                {
                    var fileStream = client.OpenRead(downloadFile);
                    var fileSize = Convert.ToInt64(client.ResponseHeaders["Content-Length"]);
                    fileStream.Close();
                    do
                    {
                        client.DownloadProgressChanged += DownloadProgressChanged;
                        await client.DownloadFileTaskAsync(downloadFile, Program.filteredDirectory + $@"\{GetDeviceInfo.SerialNumber[0]}\Packages\{downloadFile.LocalPath.Split('/').Last()}");
                        Console.Title = "Universal Updater.exe";
                    }
                    while (fileSize != new FileInfo(Program.filteredDirectory + $@"\{GetDeviceInfo.SerialNumber[0]}\Packages\{downloadFile.LocalPath.Split('/').Last()}").Length);
                }
            }

            return true;
        }

        private static void DownloadProgressChanged(object sender, DownloadProgressChangedEventArgs downloadProgressChangedEventArgs)
        {
            DownloadingProgress = new Tuple<DateTime, long, long>(DateTime.Now, downloadProgressChangedEventArgs.TotalBytesToReceive, downloadProgressChangedEventArgs.BytesReceived);
            Console.Title = $"Universal Updater.exe  [Downloading: {downloadFile.LocalPath.Split('/').Last().Remove(28)}... {((DownloadingProgress.Item3 * 100) / DownloadingProgress.Item2)}% - {DownloadingProgress.Item3}/{DownloadingProgress.Item2}]";
        }
        public static bool shouldSkip(string package, bool onlinePackage = false)
        {
            foreach (var testCheck in skippedPackages)
            {
                if (package.IndexOf(testCheck, StringComparison.OrdinalIgnoreCase) > 0)
                {
                    // Package exists in the skip list
                    Program.appendLog($"Package ({package}) skipped!, member of skipped list\n");
                    return true;
                }
            }

            if (!onlinePackage)
            {
                FileInfo fileInfo = new FileInfo(package);
                long sizeInBytes = fileInfo.Length;

                if (sizeInBytes <= 0)
                {
                    // Package has 0MB size, not a valid package
                    Program.appendLog($"Package ({package}) skipped!, 0MB size, not a valid package\n");
                    return true;
                }
            }

            if (filteredPackages.Contains(package))
            {
                // Package already added, this happens due to extension and prefix match
                Program.appendLog($"Package ({package}) skipped!, already exists\n");
                return true;
            }

            return false;
        }
    }
    public static class Extensions
    {
        public static DateTime? ToDate(this string dateTimeStr, ref string fmtOut)
        {
            const DateTimeStyles style = DateTimeStyles.AllowWhiteSpaces;

            DateTime? result = null;
            CultureInfo currentCulture = CultureInfo.CurrentCulture;
            fmtOut = currentCulture.DateTimeFormat.ShortDatePattern;
            result = DateTime.TryParse(dateTimeStr, CultureInfo.InvariantCulture,
                          style, out var dt) ? dt : null as DateTime?;
            return result;
        }
    }
}