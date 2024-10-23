using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Runtime.Remoting.Messaging;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using static System.Net.Mime.MediaTypeNames;

namespace Universal_Updater
{
    class PushPackages
    {
        public static Process updateProcess;
        public static int suggestedTimeOut = 10000;
        public static bool useOldTools = false;
        public static async Task StartUpdate()
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

            Program.WriteLine("\n[OUTPUT] (iutool.exe)\nPlease check iutool window.", ConsoleColor.DarkYellow);
            updateProcess.Start();
            updateProcess.WaitForExit(timeout);
        }

        public static async Task PushToFFU(FFUEntry FFUMountData)
        {
            stopRunningApps("updateapp.exe");
            stopRunningApps("wpimage.exe");
            var packagesPath = Program.filteredDirectory + $@"\{GetDeviceInfo.SerialNumber[0]}\Packages";
            if (!string.IsNullOrEmpty(Program.FFUPath))
            {
                {
                    var pushState = await pushToFFU(packagesPath);
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
                        saveLocation = await ShowSaveFileDialogAsync();
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
                saveLocation = await ShowSaveFileDialogAsync();
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

        public static async Task safeMountCheck()
        {
            var mountedDriveCachedData = Program.toolsDirectory + @"\mounted.ffu.json";
            if (File.Exists(mountedDriveCachedData))
            {
                var data = File.ReadAllText(mountedDriveCachedData);
                FFUEntry entry = JsonConvert.DeserializeObject<FFUEntry>(data);

                if (Directory.Exists(entry.MountPath) && IsDriveMounted(entry.DriveIdentifier))
                {
                    // User closed the app without dismounting the FFU
                    // this can lead to issues and we should dismount first
                    Program.WriteLine($"Mounted FFU detected! at {entry.DriveIdentifier}", ConsoleColor.Red);
                    Program.WriteLine($"This can lead to issues, dismounting {entry.DriveIdentifier}..", ConsoleColor.Red);
                    await Task.Delay(1000);
                    await dismountFFU(entry, null);
                }
                File.Delete(mountedDriveCachedData);
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
            }
            else
            {
                Program.appendLog($"Dismounted successfully\n");
            }

            if (!string.IsNullOrEmpty(outputFFU))
            {
                Program.WriteLine($"\nFFU should be saved to:\n{outputFFU}", ConsoleColor.Blue);
            }
            return true;
        }

        public static async Task<bool> pushToFFU(string packagePath)
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
            var batProcess = new Process();

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

            string output = "";
            bool hasError = false;
            Program.WriteLine($"[{batfile}]\n", ConsoleColor.DarkYellow);
            batProcess.OutputDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    Program.resetCursorPosition();
                    Program.WriteLine($"{e.Data}", ConsoleColor.DarkGray, true);
                    output += $"{e.Data}\n";
                }
            };
            batProcess.ErrorDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    Program.resetCursorPosition();
                    Program.WriteLine($"{e.Data}", ConsoleColor.Red, true);
                    output += $"{e.Data}\n";
                    hasError = true;
                }
            };

            batProcess.Start();
            batProcess.BeginOutputReadLine();
            batProcess.BeginErrorReadLine();
            batProcess.WaitForExit();
            if (batProcess.ExitCode != 0)
            {
                Program.WriteLine($"Process '{batfile}' has errors, check the log.", ConsoleColor.Red);
                hasError = true;
            }

            Program.showTitleWaitMessage(false);
            return !hasError || ignoreErrors ? output : null;
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

                string output = "";
                Program.WriteLine($"[{fileName}]\n", ConsoleColor.DarkYellow);
                process.OutputDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        output += $"{e.Data}\n";
                        Program.resetCursorPosition();
                        Program.WriteLine($"{e.Data}", ConsoleColor.DarkGray, true);
                    }
                };

                bool hasError = false;
                process.ErrorDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        output += $"{e.Data}\n";
                        Program.resetCursorPosition();
                        Program.WriteLine($"{e.Data}", ConsoleColor.Red, true);
                        hasError = true;
                    }
                };
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                process.WaitForExit();

                await Task.Delay(500);

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
                if (hasError)
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
            int endIndex = output.IndexOf(Environment.NewLine, startIndex);

            return output.Substring(startIndex, endIndex - startIndex).Trim();
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
            int endIndex = output.IndexOf(Environment.NewLine, startIndex);

            return output.Substring(startIndex, endIndex - startIndex).Trim();
        }

        public static Task<string> ShowSaveFileDialogAsync()
        {
            var taskCompletionSource = new TaskCompletionSource<string>();

            // start STA thread for showing the SaveFileDialog
            Thread staThread = new Thread(() =>
            {
                using (var saveFileDialog = new SaveFileDialog())
                {
                    saveFileDialog.Title = "Save FFU File";
                    saveFileDialog.Filter = "FFU Files (*.ffu)|*.ffu";
                    saveFileDialog.DefaultExt = "ffu";
                    saveFileDialog.AddExtension = true;

                    if (saveFileDialog.ShowDialog() == DialogResult.OK)
                    {
                        taskCompletionSource.SetResult(saveFileDialog.FileName);
                    }
                    else
                    {
                        taskCompletionSource.SetResult(null);
                    }
                }
            });

            staThread.SetApartmentState(ApartmentState.STA);
            staThread.Start();

            return taskCompletionSource.Task;
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