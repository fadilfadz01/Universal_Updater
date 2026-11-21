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

using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Management;
using System.Text;
using System.Threading.Tasks;

namespace Universal_Updater
{
    class PushPackages
    {
        public static Process updateProcess;
        public static int lastIutoolExitCode = -1;
        public static int suggestedTimeOut = 6000;
        public static bool useOldTools = false;
        private static readonly object outputLock = new object();

        public static async Task StartUpdate()
        {
            stopRunningApps("iutool.exe");
            lastIutoolExitCode = -1;

            var packagesPath = Program.filteredDirectory + $@"\{GetDeviceInfo.SerialNumber[0]}\Packages";
            calculateTimeout(packagesPath);

            var iutoolPath = Path.Combine(Environment.CurrentDirectory, Program.toolsDirectory, "i386", "iutool.exe");
            var arguments = $"-V -p \"{packagesPath}\"";
            Program.appendLog($"\n[COMMAND]: {iutoolPath} {arguments}\n");
            Program.WriteLine("\n[OUTPUT] (iutool.exe)\nSending packages to device, please wait...\n", ConsoleColor.DarkYellow);
            Program.showTitleWaitMessage(true, "iutool.exe");

            try
            {
                using (var process = new Process())
                {
                    process.StartInfo.FileName = iutoolPath;
                    process.StartInfo.Arguments = arguments;
                    process.StartInfo.UseShellExecute = false;
                    process.StartInfo.CreateNoWindow = true;
                    process.StartInfo.RedirectStandardOutput = true;
                    process.StartInfo.RedirectStandardError = true;
                    process.StartInfo.RedirectStandardInput = true;

                    updateProcess = process;
                    var outputBuilder = new StringBuilder();

                    process.OutputDataReceived += (sender, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                        {
                            lock (outputLock)
                            {
                                outputBuilder.AppendLine(e.Data);
                                Program.resetCursorPosition();
                                Program.WriteLine(e.Data, ConsoleColor.DarkGray, true);
                            }
                        }
                    };
                    process.ErrorDataReceived += (sender, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                        {
                            lock (outputLock)
                            {
                                outputBuilder.AppendLine(e.Data);
                                Program.resetCursorPosition();
                                Program.WriteLine(e.Data, ConsoleColor.Red, true);
                            }
                        }
                    };

                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                    await Task.Run(() => process.WaitForExit());
                    lastIutoolExitCode = process.ExitCode;
                    Program.appendLog($"[iutool.exe] ExitCode: 0x{lastIutoolExitCode:X} ({lastIutoolExitCode})\n");
                }
            }
            catch (Exception ex)
            {
                Program.WriteLine($"Failed to run iutool.exe: {ex.Message}", ConsoleColor.Red);
                Program.appendLog($"[iutool.exe] Exception: {ex}\n");
                lastIutoolExitCode = -1;
            }
            finally
            {
                updateProcess = null;
                Program.showTitleWaitMessage(false);
            }
        }

        public static async Task PushToFFU(FFUEntry FFUMountData)
        {
            stopRunningApps("updateapp.exe");
            stopRunningApps("wpimage.exe");
            var packagesPath = Program.filteredDirectory + $@"\{GetDeviceInfo.SerialNumber[0]}\Packages";
            if (!string.IsNullOrEmpty(Program.FFUPath))
            {
                {
                    Program.WriteLine("Preparing mounted FFU registry files...", ConsoleColor.Blue);
                    if (!DecompressMountedFfuRegistryFiles(FFUMountData))
                    {
                        await dismountFFUQuestion(FFUMountData);
                        Program.WriteLine("Preparing mounted FFU registry files failed!", ConsoleColor.Red);
                        return;
                    }

                    Program.WriteLine("Checking staged packages for Paper SPKG format...", ConsoleColor.Blue);
                    if (!PaperSpkgConverter.ConvertPaperSpkgsInDirectory(packagesPath))
                    {
                        await dismountFFUQuestion(FFUMountData);
                        Program.WriteLine("Preparing Paper SPKG packages for CBS install failed!", ConsoleColor.Red);
                        return;
                    }

                    var pushState = await pushToFFU(packagesPath, FFUMountData);
                    if (!pushState)
                    {
                        await dismountFFUQuestion(FFUMountData);
                        Program.WriteLine("Pushing packages to FFU failed or cancelled!", ConsoleColor.Red);
                    }
                    else
                    {
                    hardResetArea:
                        Program.WriteLine("Enabling hard reset...", ConsoleColor.Blue);
                        var hardResetOutput = await RunBatWithLog("bcdedit.bat", FFUMountData);
                        if (string.IsNullOrEmpty(hardResetOutput))
                        {
                            // Something went wrong
                            var retryAnswer = showRetryQuestion("Enable hard reset failed!, retry?", "Skip");
                            if (retryAnswer)
                            {
                                goto hardResetArea;
                            }
                            else
                            {
                                Program.WriteLine("Ensure to perform hard reset after you flash the FFU!", ConsoleColor.DarkYellow);
                            }
                        }

                    // Ask for save location and dismount
                    saveFFUArea:
                        string saveLocation = null;
                        saveLocation = await GeneralPicker.ShowSaveFileDialogAsync("ffu");
                        if (string.IsNullOrEmpty(saveLocation))
                        {
                            if (showRetryQuestion("Save location is not valid!, retry?"))
                            {
                                goto saveFFUArea;
                            }
                            else
                            {
                                // Ask for dimount
                                await dismountFFUQuestion(FFUMountData);
                            }
                        }
                        else
                        {
                        dismountArea:
                            var dismountState = await dismountFFU(FFUMountData, saveLocation);
                            if (!dismountState)
                            {
                                if (showRetryQuestion("Dismount FFU failed!, retry?"))
                                {
                                    goto dismountArea;
                                }
                            }
                        }
                    }
                }
            }
        }

        public static async Task dismountFFUQuestion(FFUEntry FFUMountData)
        {
            Program.WriteLine($"\nDo you want to dismount the FFU?", ConsoleColor.DarkYellow);
            Program.WriteLine("1. Save & Dismount");
            Program.WriteLine("2. Dismount");
            Program.WriteLine("3. Exit");
            Program.Write("Choice: ", ConsoleColor.Magenta);
            ConsoleKeyInfo dismountAction;
            do
            {
                dismountAction = Console.ReadKey(true);
            }
            while (!Program.IsNumberKeyOrNumpad(dismountAction, new char[] { '1', '2', '3' }));
            Program.Write(dismountAction.KeyChar.ToString() + "\n");

            string saveLocation = null;
            if (dismountAction.KeyChar == '1')
            {
                saveLocation = await GeneralPicker.ShowSaveFileDialogAsync("ffu");
            }

            if (dismountAction.KeyChar != '3')
            {
            dismountOnlyArea:
                var dismountState = await dismountFFU(FFUMountData, saveLocation);
                if (!dismountState)
                {
                    if (showRetryQuestion("Dismount FFU failed!, retry?"))
                    {
                        goto dismountOnlyArea;
                    }
                }
            }
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
            if (Math.Round(totalSizeInMB) == 0)
            {
                totalSizeInMB = 1;
            }
            Program.WriteLine("Total Size       : ~" + Math.Round(totalSizeInMB) + " MB", ConsoleColor.DarkGray);

            double transferSpeedMBps = 30.0; // Not sure, but approx
            double approxTransferTimeInSeconds = totalSizeInMB / transferSpeedMBps;
            double approxTransferTimeInSecondsSlower = totalSizeInMB / (transferSpeedMBps / 2);
            if (Math.Round(approxTransferTimeInSeconds) == 0)
            {
                approxTransferTimeInSeconds = 1;
                approxTransferTimeInSecondsSlower = 5;
            }

            Program.WriteLine("Approx. transfer : ~(" + Math.Round(approxTransferTimeInSeconds) + " - " + Math.Round(approxTransferTimeInSecondsSlower) + ") seconds", ConsoleColor.DarkGray);
            if (approxTransferTimeInSeconds * 1000 > timeout)
            {
                timeout = (int)(approxTransferTimeInSeconds * 1000);
            }
            suggestedTimeOut = timeout;
            return timeout;
        }

        public static void stopRunningApps(string appName)
        {
            var appFullPath = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, Program.toolsDirectory, "i386", appName));
            string processName = Path.GetFileNameWithoutExtension(appName);

            Process[] existingProcesses = Process.GetProcessesByName(processName);

            foreach (Process proc in existingProcesses)
            {
                using (proc)
                {
                    try
                    {
                        string fullPath = Path.GetFullPath(proc.MainModule.FileName);
                        if (string.Equals(fullPath, appFullPath, StringComparison.OrdinalIgnoreCase))
                        {
                            Program.WriteLine($"Tool ({appName}) detected in use, force closing..", ConsoleColor.Red);
                            proc.Kill();
                            proc.WaitForExit();
                            Program.WriteLine($"Tool ({appName}) closed.", ConsoleColor.DarkYellow);
                        }
                    }
                    catch (Exception ex)
                    {
                        Program.appendLog($"[stopRunningApps] Skipped process inspection for {appName}: {ex.Message}\n");
                    }
                }
            }
        }

        // Set tempMount to true for short mount usage like .wim
        public static async Task<FFUEntry> mountFFU(string ffuPath, bool tempMount = false)
        {
            try
            {
                if (!tempMount)
                {
                    await safeMountCheck();
                    Program.WriteLine("Mounting FFU...", ConsoleColor.Blue);
                    await Task.Delay(1500);
                }
                else
                {
                    Program.appendLog($"Mounting {ffuPath}\n");
                }

                var physicalDrive = RunProcess("wpimage.exe", $"Mount \"{ffuPath}\"");

                var driveIdentifier = ExtractPhysicalDrive(physicalDrive);
                if (driveIdentifier != null)
                {
                    var tempMountPath = ExtractTempMountPath(physicalDrive);
                    if (tempMountPath != null)
                    {
                        var FFUMountData = new FFUEntry(driveIdentifier.Replace("?", "."), tempMountPath, tempMount);
                        if (!tempMount)
                        {
                            Program.WriteLine($"Mounted on : {driveIdentifier}", ConsoleColor.Green);
                            Program.WriteLine($"Temp folder: {tempMountPath}", ConsoleColor.DarkGray);
                            FFUMountData.saveMountData();
                        }
                        else
                        {
                            Program.appendLog($"Mounted on : {driveIdentifier}\n");
                            Program.appendLog($"Temp folder: {tempMountPath}\n");
                        }
                        return FFUMountData;
                    }
                }
            }
            catch (Exception ex)
            {
                Program.WriteLine($"Error: {ex.Message}", ConsoleColor.Red);
            }

            return null;
        }

        public static async Task CleanupStaleWimMounts()
        {
            try
            {
                var dismPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                    Environment.Is64BitOperatingSystem && !Environment.Is64BitProcess
                        ? @"Sysnative\dism.exe"
                        : @"System32\dism.exe");

                if (!File.Exists(dismPath))
                {
                    return;
                }

                Program.appendLog($"[CLEANUP] Running: {dismPath} /Cleanup-Wim\n");
                using (var proc = new Process())
                {
                    proc.StartInfo.FileName = dismPath;
                    proc.StartInfo.Arguments = "/Cleanup-Wim";
                    proc.StartInfo.UseShellExecute = false;
                    proc.StartInfo.CreateNoWindow = true;
                    proc.StartInfo.RedirectStandardOutput = true;
                    proc.StartInfo.RedirectStandardError = true;
                    proc.Start();

                    var outTask = proc.StandardOutput.ReadToEndAsync();
                    var errTask = proc.StandardError.ReadToEndAsync();
                    await Task.Run(() =>
                    {
                        if (!proc.WaitForExit(30000))
                        {
                            try { proc.Kill(); } catch { }
                        }
                    });
                    string stdOut = string.Empty;
                    string stdErr = string.Empty;
                    try { stdOut = await outTask; } catch { }
                    try { stdErr = await errTask; } catch { }
                    if (!string.IsNullOrWhiteSpace(stdOut))
                        Program.appendLog(stdOut.TrimEnd() + "\n");
                    if (!string.IsNullOrWhiteSpace(stdErr))
                        Program.appendLog(stdErr.TrimEnd() + "\n");
                    Program.appendLog($"[CLEANUP] DISM ExitCode: {proc.ExitCode}\n");
                }
            }
            catch (Exception ex)
            {
                Program.appendLog($"[CLEANUP] DISM /Cleanup-Wim error: {ex.Message}\n");
            }
        }

        public static async Task safeMountCheck()
        {
            var mountedDriveCachedData = Program.toolsDirectory + @"\mounted.ffu.json";
            if (File.Exists(mountedDriveCachedData))
            {
                try
                {
                    var data = File.ReadAllText(mountedDriveCachedData);
                    FFUEntry entry = JsonConvert.DeserializeObject<FFUEntry>(data);

                    if (entry != null && !string.IsNullOrEmpty(entry.DriveIdentifier) &&
                        ((!string.IsNullOrEmpty(entry.MountPath) && Directory.Exists(entry.MountPath)) || IsDriveMounted(entry.DriveIdentifier)))
                    {
                        // User closed the app without dismounting the FFU
                        // this can lead to issues and we should dismount first
                        Program.WriteLine($"Mounted FFU detected! at {entry.DriveIdentifier}", ConsoleColor.Red);
                        Program.WriteLine($"This can lead to issues, dismounting {entry.DriveIdentifier}..", ConsoleColor.Red);
                        await Task.Delay(1000);
                        await dismountFFU(entry, null);
                    }
                }
                catch (Exception ex)
                {
                    Program.appendLog($"[safeMountCheck] {ex.Message}\n");
                }
                try
                {
                    if (File.Exists(mountedDriveCachedData))
                    {
                        File.Delete(mountedDriveCachedData);
                    }
                }
                catch { }
            }
        }

        public static async Task<bool> dismountFFU(FFUEntry FFUMountData, string outputFFU)
        {
            string output = null;
            try
            {
                if (!FFUMountData.TempMount)
                {
                    if (string.IsNullOrEmpty(outputFFU))
                    {
                        Program.WriteLine("Dismounting FFU...", ConsoleColor.Blue);
                    }
                    else
                    {
                        Program.WriteLine("Dismounting & Saving FFU...", ConsoleColor.Blue);
                    }
                }
                else
                {
                    Program.appendLog($"Dismounting {FFUMountData.DriveIdentifier}\n");
                }
                if (outputFFU == null)
                {
                    output = await RunProcessWithLog("wpimage.exe", $"Dismount -physicalDrive \"{FFUMountData.DriveIdentifier}\"");
                }
                else
                {
                    output = await RunProcessWithLog("wpimage.exe", $"Dismount -physicalDrive \"{FFUMountData.DriveIdentifier}\" -imagePath \"{outputFFU}\" -noSign");
                }
            }
            catch (Exception ex)
            {
                Program.WriteLine($"Error: {ex.Message}", ConsoleColor.Red);
                return false;
            }

            if (output == null)
            {
                return false;
            }
            if (!FFUMountData.TempMount)
            {
                Program.WriteLine("FFU dismounted successfully", ConsoleColor.Green);
                var mountedDriveCachedData = Program.toolsDirectory + @"\mounted.ffu.json";
                try
                {
                    if (File.Exists(mountedDriveCachedData))
                    {
                        File.Delete(mountedDriveCachedData);
                    }
                }
                catch { }
            }
            else
            {
                Program.appendLog($"Dismounted successfully\n");
            }

            if (!string.IsNullOrEmpty(outputFFU))
            {
                Program.WriteLine($"\nFFU should be saved to:\n{outputFFU}", ConsoleColor.Blue);
                Program.printFlashInstructions();
            }
            return true;
        }

        public static bool DecompressMountedFfuRegistryFiles(FFUEntry ffuMountData)
        {
            if (ffuMountData == null || string.IsNullOrWhiteSpace(ffuMountData.MountPath))
            {
                return true;
            }

            string mountRoot = ffuMountData.MountPath.TrimEnd('\\', '/');
            if (!Directory.Exists(mountRoot))
            {
                Program.appendLog($"[FFU PREP] Mount path not found: {mountRoot}\n");
                return false;
            }

            string[] candidateRelativeDirs = new[]
            {
                @"Windows\Packages\RegistryFiles",
                @"EFIESP\Windows\Packages\RegistryFiles",
                @"Data\Windows\Packages\RegistryFiles",
                @"Windows\ImageUpdate\RegistryFiles"
            };

            int scannedFiles = 0;
            int decompressedFiles = 0;

            try
            {
                foreach (string relDir in candidateRelativeDirs)
                {
                    string fullDir = Path.Combine(mountRoot, relDir);
                    if (!Directory.Exists(fullDir))
                    {
                        continue;
                    }

                    foreach (string filePath in Directory.GetFiles(fullDir, "*", SearchOption.AllDirectories))
                    {
                        scannedFiles++;
                        if (!IsGzipFile(filePath))
                        {
                            continue;
                        }

                        byte[] decompressedBytes;
                        using (var fs = File.OpenRead(filePath))
                        using (var gz = new GZipStream(fs, CompressionMode.Decompress))
                        using (var ms = new MemoryStream())
                        {
                            gz.CopyTo(ms);
                            decompressedBytes = ms.ToArray();
                        }

                        if (decompressedBytes.Length == 0)
                        {
                            Program.appendLog($"[FFU PREP] Decompressed registry file is empty: {filePath}\n");
                            return false;
                        }

                        var attrs = File.GetAttributes(filePath);
                        if ((attrs & FileAttributes.ReadOnly) != 0)
                        {
                            File.SetAttributes(filePath, attrs & ~FileAttributes.ReadOnly);
                        }

                        string tempOut = filePath + ".ungz.tmp";
                        try
                        {
                            File.WriteAllBytes(tempOut, decompressedBytes);
                            File.Delete(filePath);
                            File.Move(tempOut, filePath);
                        }
                        finally
                        {
                            if (File.Exists(tempOut) && File.Exists(filePath))
                            {
                                try { File.Delete(tempOut); } catch { }
                            }
                        }

                        if (IsGzipFile(filePath))
                        {
                            Program.appendLog($"[FFU PREP] Registry file remained GZIP-compressed after write: {filePath}\n");
                            return false;
                        }

                        decompressedFiles++;
                    }
                }

                if (decompressedFiles > 0)
                {
                    Program.WriteLine($"Decompressed {decompressedFiles} GZIP registry file(s) in mounted FFU.", ConsoleColor.Green);
                }
                Program.appendLog($"[FFU PREP] Scanned {scannedFiles} registry file(s), decompressed {decompressedFiles} GZIP file(s).\n");
                return true;
            }
            catch (Exception ex)
            {
                Program.appendLog($"[FFU PREP] Exception decompressing FFU registry files: {ex}\n");
                Program.WriteLine($"Failed decompressing mounted FFU registry files: {ex.Message}", ConsoleColor.Red);
                return false;
            }
        }

        private static bool IsGzipFile(string filePath)
        {
            using (var fs = File.OpenRead(filePath))
            {
                if (fs.Length < 2)
                {
                    return false;
                }
                int b0 = fs.ReadByte();
                int b1 = fs.ReadByte();
                return b0 == 0x1F && b1 == 0x8B;
            }
        }

        public static async Task<bool> pushToFFU(string packagePath, FFUEntry ffuMountData = null)
        {
            try
            {
                bool userExitTheProcess = false;

                if (useOldTools)
                {
                converttocbsArea:
                    Program.WriteLine("Converting to CBS...", ConsoleColor.Blue);
                    var converttocbs = await RunProcessWithLog("updateapp.exe", "converttocbs MainOS Data EFIESP UpdateOS", useOldTools);
                    if (converttocbs == null)
                    {
                        // Something went wrong
                        var retryAnswer = showRetryQuestionWithContinue("Converting to CBS failed, retry?");
                        if (retryAnswer == '1')
                        {
                            goto converttocbsArea;
                        }
                        else if (retryAnswer == '2')
                        {
                            goto iutocbsfromupdateoutputArea;
                        }
                        else
                        {
                            userExitTheProcess = true;
                            goto exitProcessArea;
                        }
                    }

                iutocbsfromupdateoutputArea:
                    Program.WriteLine("IU to CBS from update output...", ConsoleColor.Blue);
                    var iutocbsfromupdateoutput = await RunProcessWithLog("updateapp.exe", "iutocbsfromupdateoutput", useOldTools);
                    if (iutocbsfromupdateoutput == null)
                    {
                        // Something went wrong
                        var retryAnswer = showRetryQuestionWithContinue("IU to CBS from update output failed, retry?");
                        if (retryAnswer == '1')
                        {
                            goto iutocbsfromupdateoutputArea;
                        }
                        else if (retryAnswer == '2')
                        {
                            goto installArea;
                            //goto stageArea;
                        }
                        else
                        {
                            userExitTheProcess = true;
                            goto exitProcessArea;
                        }
                    }
                }

            installArea:
                if (useOldTools && ffuMountData != null && !DecompressMountedFfuRegistryFiles(ffuMountData))
                {
                    userExitTheProcess = true;
                    goto exitProcessArea;
                }

                Program.WriteLine("Installing Packages...", ConsoleColor.Blue);
                var install = await RunProcessWithLog("updateapp.exe", $"install \"{packagePath}\"");
                if (install == null)
                {
                    // Something went wrong
                    var retryAnswer = showRetryQuestionWithContinue("Installing packages failed!, retry?");
                    if (retryAnswer == '1')
                    {
                        goto installArea;
                    }
                    else if (retryAnswer == '2')
                    {
                        goto cleanupArea;
                    }
                    else
                    {
                        userExitTheProcess = true;
                        goto exitProcessArea;
                    }
                }

            //stageArea:
            //    Program.WriteLine("Staging Packages...", ConsoleColor.Blue);
            //    var stage = await RunProcessWithLog("updateapp.exe", $"stage \"{packagePath}\"");
            //    if (stage == null)
            //    {
            //        // Something went wrong
            //        var retryAnswer = showRetryQuestionWithContinue("Staging packages failed!, retry?");
            //        if (retryAnswer == '1')
            //        {
            //            goto stageArea;
            //        }
            //        else if (retryAnswer == '2')
            //        {
            //            goto commitArea;
            //        }
            //        else
            //        {
            //            userExitTheProcess = true;
            //            goto exitProcessArea;
            //        }
            //    }

            //commitArea:
            //    Program.WriteLine("Committing Packages...", ConsoleColor.Blue);
            //    var commit = await RunProcessWithLog("updateapp.exe", "commit migratedata noreboot");
            //    if (commit == null)
            //    {
            //        // Something went wrong
            //        var retryAnswer = showRetryQuestionWithContinue("Commit failed!, retry?");
            //        if (retryAnswer == '1')
            //        {
            //            goto commitArea;
            //        }
            //        else if (retryAnswer == '2')
            //        {
            //            goto cleanupArea;
            //        }
            //        else
            //        {
            //            userExitTheProcess = true;
            //            goto exitProcessArea;
            //        }
            //    }

            cleanupArea:
                Program.WriteLine("Cleaning up...", ConsoleColor.Blue);
                var cleanup = await RunProcessWithLog("updateapp.exe", "cleanup");
                if (cleanup == null)
                {
                    // Something went wrong
                    var retryAnswer = showRetryQuestionWithContinue("Cleaning up failed!, retry?");
                    if (retryAnswer == '1')
                    {
                        goto cleanupArea;
                    }
                    else if (retryAnswer == '2')
                    {
                        goto postcommitfixupArea;
                    }
                    else
                    {
                        userExitTheProcess = true;
                        goto exitProcessArea;
                    }
                }

            postcommitfixupArea:
                if (useOldTools)
                {
                    Program.WriteLine("Post-commit fixup...", ConsoleColor.Blue);
                    var postcommitfixup = await RunProcessWithLog("updateapp.exe", "postcommitfixup", useOldTools);
                    if (postcommitfixup == null)
                    {
                        // Something went wrong
                        var retryAnswer = showRetryQuestionWithContinue("Post-commit fixup failed!, retry?");
                        if (retryAnswer == '1')
                        {
                            goto postcommitfixupArea;
                        }
                        else if (retryAnswer == '2')
                        {
                            goto exitProcessArea;
                        }
                        else
                        {
                            userExitTheProcess = true;
                            goto exitProcessArea;
                        }
                    }
                }

            exitProcessArea:
                if (userExitTheProcess)
                {
                    Program.WriteLine("Push process terminated!", ConsoleColor.Red);
                    return false;
                }
            }
            catch (Exception ex)
            {
                Program.WriteLine($"Error: {ex.Message}", ConsoleColor.Red);
                return false;
            }

            return true;
        }

        public static async Task<string> RunBatWithLog(string batfile, FFUEntry FFUMountData, bool ignoreErrors = false)
        {
            Program.showTitleWaitMessage(true, batfile);
            var mountedPath = FFUMountData.MountPath.TrimEnd('\\');
            using (var batProcess = new Process())
            {
                // We have to do it this way if process is x86 running on x64 host
                var cmdFullFileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                                          Environment.Is64BitOperatingSystem && !Environment.Is64BitProcess
                                              ? @"Sysnative\cmd.exe"
                                              : @"System32\cmd.exe");

                var targetBat = $@"{Environment.CurrentDirectory}\{Program.toolsDirectory}\{batfile}";
                string args = $"/c \"\"{targetBat}\" \"{mountedPath}\" \"{Program.logFile}\"\"";
                Program.appendLog($"[CMD DETAILS]\nIf you see Sysnative in the path this fine and expected\n");
                Program.appendLog($"CMD PATH: {cmdFullFileName}\n");
                Program.appendLog($"CMD ARGS: {args}\n");
                batProcess.StartInfo.FileName = cmdFullFileName;
                batProcess.StartInfo.Arguments = args;
                batProcess.StartInfo.RedirectStandardInput = true;
                batProcess.StartInfo.RedirectStandardOutput = true;
                batProcess.StartInfo.RedirectStandardError = true;
                batProcess.StartInfo.UseShellExecute = false;
                batProcess.StartInfo.CreateNoWindow = true;

                var outputBuilder = new StringBuilder();
                bool hasError = false;
                Program.WriteLine($"[{batfile}]\n", ConsoleColor.DarkYellow);
                batProcess.OutputDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        lock (outputLock)
                        {
                            Program.resetCursorPosition();
                            Program.WriteLine($"{e.Data}", ConsoleColor.DarkGray, true);
                            outputBuilder.AppendLine(e.Data);
                        }
                    }
                };
                batProcess.ErrorDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        lock (outputLock)
                        {
                            Program.resetCursorPosition();
                            Program.WriteLine($"{e.Data}", ConsoleColor.Red, true);
                            outputBuilder.AppendLine(e.Data);
                            hasError = true;
                        }
                    }
                };

                batProcess.Start();
                batProcess.BeginOutputReadLine();
                batProcess.BeginErrorReadLine();
                await Task.Run(() => batProcess.WaitForExit());
                if (batProcess.ExitCode != 0)
                {
                    Program.WriteLine($"Process '{batfile}' has errors, check the log.", ConsoleColor.Red);
                    hasError = true;
                }

                Program.showTitleWaitMessage(false);
                return !hasError || ignoreErrors ? outputBuilder.ToString() : null;
            }
        }

        public static bool showRetryQuestion(string message, string exitText = "Exit", ConsoleColor color = ConsoleColor.DarkYellow)
        {
            Program.WriteLine($"\n{message}", color);
            Program.WriteLine("1. Retry", ConsoleColor.Gray);
            Program.WriteLine($"2. {exitText}", ConsoleColor.Gray);
            Program.Write("Choice: ", ConsoleColor.Magenta);
            ConsoleKeyInfo generalAction;
            do
            {
                generalAction = Console.ReadKey(true);
            }
            while (!Program.IsNumberKeyOrNumpad(generalAction, new char[] { '1', '2' }));
            Program.Write(generalAction.KeyChar.ToString() + "\n");

            return generalAction.KeyChar == '1';
        }
        public static char showRetryQuestionWithContinue(string message, string exitText = "Exit", ConsoleColor color = ConsoleColor.DarkYellow)
        {
            Program.WriteLine($"\n{message}", color);
            Program.WriteLine("1. Retry", ConsoleColor.Gray);
            Program.WriteLine("2. Continue", ConsoleColor.Gray);
            Program.WriteLine($"3. {exitText}", ConsoleColor.Gray);
            Program.Write("Choice: ", ConsoleColor.Magenta);
            ConsoleKeyInfo generalAction;
            do
            {
                generalAction = Console.ReadKey(true);
            }
            while (!Program.IsNumberKeyOrNumpad(generalAction, new char[] { '1', '2', '3' }));
            Program.Write(generalAction.KeyChar.ToString() + "\n");

            return generalAction.KeyChar;
        }

        public static string RunProcess(string fileName, string arguments)
        {
            Program.showTitleWaitMessage(true, fileName);
            using (var process = new Process())
            {
                var fullFilePath = Program.toolsDirectory + $@"\i386\{fileName}";
                Program.appendLog($"\n[COMMAND]: {fullFilePath} {arguments}\n");
                process.StartInfo.FileName = fullFilePath;
                process.StartInfo.Arguments = arguments;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.CreateNoWindow = true;

                process.Start();

                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                Program.appendLog(output);
                if (process.ExitCode != 0)
                {
                    string formattedErrorCode = "0x" + process.ExitCode.ToString("X");
                    Program.WriteLine($"Process '{fileName}' exited with code {formattedErrorCode}.", ConsoleColor.Red);
                    Program.showTitleWaitMessage(false);
                    return null;
                }

                Program.showTitleWaitMessage(false);
                return output;
            }
        }

        public static async Task<string> RunProcessWithLog(string fileName, string arguments, bool useOldSet = false)
        {
            Program.showTitleWaitMessage(true, fileName);
            using (var process = new Process())
            {
                var prefix = "";
                if (useOldSet)
                {
                    prefix = @"_old_\";
                }
                var fullFilePath = Program.toolsDirectory + $@"\i386\{prefix}{fileName}";
                Program.appendLog($"\n[COMMAND]: {fullFilePath} {arguments}\n");
                process.StartInfo.FileName = fullFilePath;
                process.StartInfo.Arguments = arguments;
                process.StartInfo.RedirectStandardInput = true;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.CreateNoWindow = true;

                var outputBuilder = new StringBuilder();
                Program.WriteLine($"[{fileName}]\n", ConsoleColor.DarkYellow);
                process.OutputDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        lock (outputLock)
                        {
                            outputBuilder.AppendLine(e.Data);
                            Program.resetCursorPosition();
                            Program.WriteLine($"{e.Data}", ConsoleColor.DarkGray, true);
                        }
                    }
                };

                bool hasError = false;
                process.ErrorDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        lock (outputLock)
                        {
                            outputBuilder.AppendLine(e.Data);
                            Program.resetCursorPosition();
                            Program.WriteLine($"{e.Data}", ConsoleColor.Red, true);
                            hasError = true;
                        }
                    }
                };
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                await Task.Run(() => process.WaitForExit());

                string output = outputBuilder.ToString();
                bool hasFatalOutput = HasFatalOutputFailure(output);

                if (process.ExitCode != 0)
                {
                    string formattedErrorCode = "0x" + process.ExitCode.ToString("X");
                    Program.WriteLine($"Process '{fileName}' exited with code {formattedErrorCode}.", ConsoleColor.Red);
                    bool errorIsExpected = isErrorExpected($"Error, {formattedErrorCode}", fileName, arguments);
                    if (!errorIsExpected)
                    {
                        Program.showTitleWaitMessage(false);
                        return null;
                    }
                }

                // Exclude expected errors
                if (hasError || hasFatalOutput)
                {
                    bool errorIsExpected = isErrorExpected(output, fileName, arguments);
                    if (!errorIsExpected)
                    {
                        Program.WriteLine($"Process '{fileName}' has errors, check the log.", ConsoleColor.Red);
                        Program.showTitleWaitMessage(false);
                        return null;
                    }
                }

                Program.showTitleWaitMessage(false);
                return output;
            }
        }

        private static bool HasFatalOutputFailure(string output)
        {
            if (string.IsNullOrEmpty(output))
            {
                return false;
            }

            string[] fatalMarkers = new[]
            {
                "install completed unsuccessfully",
                "Update completed unsuccessfully",
                "Staging completed unsuccessfully",
                "converttocbs completed unsuccessfully",
                "iutocbsfromupdateoutput completed unsuccessfully",
                "Commit failed"
            };

            foreach (string marker in fatalMarkers)
            {
                if (output.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }
            return false;
        }

        static Dictionary<string, List<ErrorEntry>> expectedErrorsMap = new Dictionary<string, List<ErrorEntry>>();
        public static bool isErrorExpected(string output, string fileName, string arguments)
        {
            bool errorIsExpected = false;
            try
            {
                if (expectedErrorsMap.Count == 0)
                {
                    string jsonFilePath = Program.toolsDirectory + @"\expectedErrors.json";
                    string jsonString = File.ReadAllText(jsonFilePath);
                    expectedErrorsMap = JsonConvert.DeserializeObject<Dictionary<string, List<ErrorEntry>>>(jsonString);
                }

                foreach (var error in expectedErrorsMap)
                {
                    if (fileName == error.Key)
                    {
                        foreach (var errorData in error.Value)
                        {
                            var argCheck = errorData.Argument;
                            var errorsList = errorData.Errors;
                            if (argCheck == null || arguments.IndexOf(argCheck) >= 0)
                            {
                                foreach (var errorCode in errorsList)
                                {
                                    if (output.IndexOf(errorCode) >= 0)
                                    {
                                        errorIsExpected = true;
                                        string argumentPreview = arguments;
                                        if (argCheck != null)
                                        {
                                            argumentPreview = argCheck;
                                        }
                                        Program.WriteLine($"This error ({errorCode}) expected, ({fileName} {argumentPreview})", ConsoleColor.DarkYellow);
                                        break;
                                    }
                                }
                            }
                            if (errorIsExpected)
                            {
                                break;
                            }
                        }
                    }
                    if (errorIsExpected)
                    {
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Program.appendLog(ex.Message);
            }
            return errorIsExpected;
        }

        public static string ExtractPhysicalDrive(string output)
        {
            if (string.IsNullOrEmpty(output))
            {
                Program.WriteLine("Failed to read physical drive information.", ConsoleColor.Red);
                return null;
            }
            string searchPattern = "Mounted virtual disk at phys ";
            string searchPattern2 = "Physical Disk Name: ";
            int startIndex = output.IndexOf(searchPattern);
            if (startIndex < 0)
            {
                // Check another pattern for older tool
                startIndex = output.IndexOf(searchPattern2);
                searchPattern = searchPattern2;
            }
            if (startIndex < 0)
            {
                Program.WriteLine("Failed to find physical drive information.", ConsoleColor.Red);
                return null;
            }

            startIndex += searchPattern.Length;
            int endIndex = output.IndexOfAny(new[] { '\r', '\n' }, startIndex);
            string val = endIndex < 0 ? output.Substring(startIndex) : output.Substring(startIndex, endIndex - startIndex);

            return val.Trim();
        }
        public static string ExtractTempMountPath(string output)
        {
            if (string.IsNullOrEmpty(output))
            {
                Program.WriteLine("Failed to read physical drive information.", ConsoleColor.Red);
                return null;
            }
            string searchPattern = "Main Mount Path: ";
            int startIndex = output.IndexOf(searchPattern);
            if (startIndex < 0)
            {
                Program.WriteLine("Failed to find main mount path information.", ConsoleColor.Red);
                return null;
            }

            startIndex += searchPattern.Length;
            int endIndex = output.IndexOfAny(new[] { '\r', '\n' }, startIndex);
            string val = endIndex < 0 ? output.Substring(startIndex) : output.Substring(startIndex, endIndex - startIndex);

            return val.Trim();
        }

        public static bool IsDriveMounted(string physicalDrive)
        {
            try
            {
                var searcher = new ManagementObjectSearcher(
                    @"SELECT * FROM Win32_DiskDrive WHERE DeviceID='" + physicalDrive.Replace("\\", "\\\\") + "'");

                foreach (ManagementObject disk in searcher.Get())
                {
                    var partitionSearcher = new ManagementObjectSearcher(
                        $"ASSOCIATORS OF {{Win32_DiskDrive.DeviceID='{disk["DeviceID"]}'}} WHERE AssocClass=Win32_DiskDriveToDiskPartition");

                    foreach (ManagementObject partition in partitionSearcher.Get())
                    {
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                Program.appendLog(ex.Message);
            }
            return false;
        }

    }

    public class ErrorEntry
    {
        public string Argument { get; set; }
        public List<string> Errors { get; set; }
    }
    public class FFUEntry
    {
        public string DriveIdentifier { get; set; }
        public string MountPath { get; set; }
        public bool TempMount { get; set; }
        public FFUEntry(string driveIdentifier, string mountPath, bool tempMount)
        {
            DriveIdentifier = driveIdentifier;
            MountPath = mountPath;
            TempMount = tempMount;
        }

        public void saveMountData()
        {
            var mountedDriveCachedData = Program.toolsDirectory + @"\mounted.ffu.json";
            string jsonString = JsonConvert.SerializeObject(this, Formatting.Indented);
            File.WriteAllText(mountedDriveCachedData, jsonString);
        }
    }
}