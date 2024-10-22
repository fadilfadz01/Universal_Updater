using Microsoft.Win32;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using static System.Net.Mime.MediaTypeNames;
using static Universal_Updater.Program;

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
        public static string FFUPath = null;
        static readonly string[,] updateStructure = { { null, null, null }, { "13016.108", "13080.107", "Offline" }, { "13037.0", "Offline", null }, { "14393.1066", "Offline", null }, { "15063.297", "15254.603", "Offline" }, { "15254.603", "Offline", null }, { "Offline", null, null } };
        public static string tempDirectory = "Temp";
        public static string toolsDirectory = "Resources";
        public static string filteredDirectory = $@"{Environment.CurrentDirectory}\Filtered";
        public static string logFile = $@"{Environment.CurrentDirectory}\Logs\universal_updater.txt";

        // For testing only without device
        public static bool useConnectedDevice = true;

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
        public static void WriteLine(string text, ConsoleColor color = ConsoleColor.White, bool trimPreviewIfLong = false, bool appendToLog = true)
        {
            Console.ForegroundColor = color;
            if (trimPreviewIfLong)
            {
                WriteTrimmedLine(text);
            }
            else
            {
                Console.WriteLine(text);
            }
            Console.ResetColor();
            if (appendToLog)
            {
                appendLog(text + Environment.NewLine);
            }
        }
        public static void Write(string text, ConsoleColor color = ConsoleColor.White, bool trimPreviewIfLong = false, bool appendToLog = true)
        {
            Console.ForegroundColor = color;
            if (trimPreviewIfLong)
            {
                WriteTrimmedLine(text, false);
            }
            else
            {
                Console.Write(text);
            }
            Console.ResetColor();
            if (appendToLog)
            {
                appendLog(text);
            }
        }
        public static void WriteTrimmedLine(string text, bool writeLine = true)
        {
            // Get the current console window width
            int consoleWidth = (int)Math.Round(Console.WindowWidth * 0.95);

            // Calculate the maximum allowed length considering space for '...'
            const int ellipsisLength = 3;
            int maxAllowedLength = consoleWidth - ellipsisLength;

            if (text.Length > maxAllowedLength)
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

        static async void StartUpdater()
        {
            logFile = prepareLogFile("universal_updater.exe");
            appendAppInfoToLog();
            ConsoleStyle("", 0, ConsoleColor.Blue);

            // Extract important tools if not ready
            string startPath = AppDomain.CurrentDomain.BaseDirectory;
            string zipPath = Path.Combine(startPath, @"Resources\i386.zip");
            string extractPath = Path.Combine(startPath, @"Resources");
            string hashFilePath = Path.Combine(startPath, @"Resources\hash.txt");

            // Calculate current hash
            string currentHash = CalculateMD5(zipPath);

            // Read previous hash from file
            string previousHash = File.Exists(hashFilePath) ? File.ReadAllText(hashFilePath).Trim() : null;
            if (currentHash != previousHash)
            {
                // Tools zip changed and better to re-extract
                if (Directory.Exists(extractPath + "\\i386"))
                {
                    Directory.Delete(extractPath + "\\i386", true);
                }
                File.WriteAllText(hashFilePath, currentHash);
            }

            if (!IsRunningAsAdministrator())
            {
                Program.WriteLine("Please start the app as Administrator", ConsoleColor.Red);
                return;
            }

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
                    // Handle in use error 
                    // Try with CMD first before showing error
                    showTitleWaitMessage(true, "Cleaning temp");
                    var deleteProcess = new Process();
                    deleteProcess.StartInfo.FileName = toolsDirectory + $@"\cleanTemp.bat";
                    deleteProcess.StartInfo.Arguments = $"{tempDirectory} \"{logFile}\"";
                    deleteProcess.StartInfo.UseShellExecute = false;
                    deleteProcess.StartInfo.CreateNoWindow = true;
                    deleteProcess.Start();
                    deleteProcess.WaitForExit();
                    showTitleWaitMessage(false);
                    if (deleteProcess.HasExited)
                    {
                        ConsoleStyle(ex.Message, 0, ConsoleColor.Red);
                        WriteLine("\nRetrying...", ConsoleColor.DarkYellow);
                        await Task.Delay(5000);
                        // Give small delay between retries
                        await retryWithCount(3);
                        goto cleanTempArea;
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
            Write("Choice: ", ConsoleColor.Magenta);
            ConsoleKeyInfo updateTargetAction;
            do
            {
                updateTargetAction = Console.ReadKey(true);
            }
            while (updateTargetAction.KeyChar != '1' && updateTargetAction.KeyChar != '2');
            Write(updateTargetAction.KeyChar.ToString() + "\n");
            pushToFFU = updateTargetAction.KeyChar != '1';

            if (pushToFFU)
            {
            ffuPathArea:
                // Ask for FFU
                WriteLine("\n[NOTICE]", ConsoleColor.Red);
                //WriteLine("- Hard reset is required after you flash FFU!", ConsoleColor.DarkYellow);
                WriteLine("- Ensure to have enough space at C: drive", ConsoleColor.Gray);
                WriteLine("- No FFUs mounted before, check Computer Management", ConsoleColor.Gray);
                WriteLine("- Don't interrupt the process until you asked for", ConsoleColor.Gray);
                WriteLine("- Cleanup system temp after this process", ConsoleColor.Gray);

                FFUPath = null;
                Program.Write("\nEnter FFU path: ", ConsoleColor.DarkYellow);
                do
                {
                    FFUPath = Console.ReadLine().Trim('"');
                }
                while (FFUPath.Length == 0);
                appendLog(FFUPath + "\n");
                if (FFUPath == null || FFUPath.Length == 0 || !File.Exists(FFUPath))
                {
                    if (PushPackages.showRetryQuestion("FFU path is not valid or not exist", "Exit", ConsoleColor.Red))
                    {
                        goto ffuPathArea;
                    }
                    else
                    {
                        pushToFFU = false;
                        return;
                    }
                }

                GetDeviceInfo.OfflineFFU();
                useConnectedDevice = false;
            }

            var packagesReady = false;
            string selectedUpdateOption = "1";
            if (useConnectedDevice)
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
                    printLogLocation();
                    return;
                }

                ConsoleStyle("TARGET INFORMATIONS", 1, ConsoleColor.DarkGray);
                foreach (string info in deviceInfo)
                {
                    WriteLine(info);
                }
                DownloadPackages.installedPackages = File.ReadAllLines(Program.tempDirectory + @"\InstalledPackages.csv");

                var availableUpdates = CheckForUpdates.AvailableUpdates();
                if (CheckForUpdates.Choice == 1)
                {
                    WriteLine("\nThe OS version didn't reached the minimum required build.", ConsoleColor.Red);
                    Write("Please update to ", ConsoleColor.Red);
                    Write("'8.10.14219.341'", ConsoleColor.Blue);
                    WriteLine(" at least.", ConsoleColor.Red);
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
                    if (updateItem.IndexOf("Offline") >= 0)
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
                    WriteLine("\nSomething went wrong.", ConsoleColor.Red);
                    printLogLocation();
                    return;
                }
                Write(selectedUpdate.KeyChar + "\n");

                selectedUpdateOption = Program.selectedUpdate.KeyChar.ToString();
            }
            else
            {
                // Fall to offline by default
                CheckForUpdates.Choice = 7;
            }

            if (updateStructure[CheckForUpdates.Choice - 1, Convert.ToInt32(selectedUpdateOption) - 1] == "Offline")
            {
            packagePathArea:
                packagePath = "";
                Write("\nEnter Offline packages path: ", ConsoleColor.DarkYellow);
                do
                {
                    packagePath = Console.ReadLine().Trim('"');
                }
                while (packagePath.Length == 0);
                appendLog($"{packagePath}\n");

                string[] folderFiles = new string[] { };
                if (Directory.Exists(packagePath))
                {
                    folderFiles = Directory.GetFiles(packagePath);
                    var hasCabFiles = string.Join("\n", folderFiles).IndexOf(".cab", StringComparison.OrdinalIgnoreCase) > 0;
                    var hasSpkgFiles = string.Join("\n", folderFiles).IndexOf(".spkg", StringComparison.OrdinalIgnoreCase) > 0;
                    if (!hasCabFiles && !hasSpkgFiles)
                    {
                        WriteLine("\nFolder don't have update packages (.cab)!", ConsoleColor.Red);
                        WriteLine("1. Retry");
                        WriteLine("2. Exit");
                        Write("Choice: ", ConsoleColor.Magenta);
                        ConsoleKeyInfo packagesAction;
                        do
                        {
                            packagesAction = Console.ReadKey(true);
                        }
                        while (packagesAction.KeyChar != '1' && packagesAction.KeyChar != '2');
                        Console.Write(packagesAction.KeyChar.ToString() + "\n");
                        if (packagesAction.KeyChar == '1')
                        {
                            WriteLine("");
                            goto packagePathArea;
                        }
                        else
                        {
                            printLogLocation();
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
                    while (packagesAction.KeyChar != '1' && packagesAction.KeyChar != '2');
                    Write(packagesAction.KeyChar.ToString() + "\n");
                    if (packagesAction.KeyChar == '1')
                    {
                        WriteLine("");
                        goto packagePathArea;
                    }
                    else
                    {
                        printLogLocation();
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

            printLogLocation();

            if (messageBoxText.Length > 0)
            {
                MessageBox((IntPtr)0, messageBoxText, "Universal Updater.exe", 0);
            }
            return;
        }

        static void printLogLocation()
        {
            var previewLogFile = logFile.Replace($@"{Environment.CurrentDirectory}\", "");
            WriteLine($"\nFinished.\n\nFor more details check the logs\n{previewLogFile}", ConsoleColor.DarkGray);
        }

        public static List<FeaturesItem> GlobalFeaturesList = new List<FeaturesItem>();
        static void showFeaturesSelection(string[] folderFiles)
        {
            try
            {
                var featuresFile = toolsDirectory + @"\features.json";
                if (File.Exists(featuresFile))
                {
                    var data = File.ReadAllText(featuresFile);
                    GlobalFeaturesList = JsonConvert.DeserializeObject<List<FeaturesItem>>(data);

                    // Scan for possible attached packages info
                    foreach (var featureInfoCheck in folderFiles)
                    {
                        try
                        {
                            // This method don't expect duplicated features
                            // Built-in features (DevMenu, Astoria, Acer, CMDI) shouldn't have attached info
                            if (featureInfoCheck.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                            {
                                // This expected to be feature info attached with the cab
                                var featureInfoCheckData = File.ReadAllText(featureInfoCheck);
                                if (!string.IsNullOrEmpty(featureInfoCheckData))
                                {
                                    try
                                    {
                                        var featureObject = JsonConvert.DeserializeObject<FeaturesItem>(featureInfoCheckData);
                                        GlobalFeaturesList.Add(featureObject);
                                    }
                                    catch (Exception ex2)
                                    {
                                        // This file may have array instead
                                        var featuresObjects = JsonConvert.DeserializeObject<List<FeaturesItem>>(featureInfoCheckData);
                                        if (featuresObjects != null && featuresObjects.Count > 0)
                                        {
                                            foreach (var featureObject in featuresObjects)
                                            {
                                                GlobalFeaturesList.Add(featureObject);
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
                            int newRow = Math.Max(Console.CursorTop - (filteredFeatures.Count + 1), 0);
                            Console.SetCursorPosition(0, newRow);

                            // Display features with checkboxes
                            for (int i = 0; i < filteredFeatures.Count; i++)
                            {
                                if (filteredFeatures[i].ShouldPresent)
                                {
                                    string checkbox = filteredFeatures[i].State ? "[+]" : "[-]";
                                    Write($"{i + 1}. {checkbox} {filteredFeatures[i].Name}", filteredFeatures[i].State ? ConsoleColor.Green : ConsoleColor.Red, false, false);
                                    WriteLine($" {filteredFeatures[i].Description}", filteredFeatures[i].DescriptionColor, false, false);
                                }
                                else
                                {
                                    Write($"{i + 1}. [!] {filteredFeatures[i].Name}", ConsoleColor.DarkGray, false, false);
                                    WriteLine($" (Cab not presented)", ConsoleColor.DarkGray, false, false);
                                }
                            }

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

        [STAThread]
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
            Console.WriteLine("- " + FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location).LegalCopyright + " by " + FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location).CompanyName);
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("- Contributors: Bashar Astifan");
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.WriteLine("- This app provided AS-IS without any warranty");
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("- GitHub: https://github.com/fadilfadz01/Universal_Updater");

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
            Console.Title = FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location).InternalName + extraTitleText;
        }
        static void appendAppInfoToLog()
        {
            appendLog(FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location).InternalName + "\n");
            appendLog("Version " + FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location).FileVersion + "\n");
            appendLog(FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location).LegalCopyright + " by " + FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location).CompanyName + "\n");
            appendLog("This app provided AS-IS without any warranty" + "\n");
            appendLog("GitHub: https://github.com/fadilfadz01/Universal_Updater" + "\n");
            appendLog("IUTool: https://learn.microsoft.com/en-us/previous-versions/mt131833(v=vs.85)" + "\n\n");
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
            foreach (var testFeatureCab in FeaturePackages)
            {
                if (string.Join("\n", folderFiles).IndexOf(testFeatureCab.PackageName, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    ShouldPresent = true;
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
}