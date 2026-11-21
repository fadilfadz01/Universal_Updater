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
using System.Text;
using System.Xml;

namespace Universal_Updater
{
    internal static class PaperSpkgConverter
    {
        private const string DownlevelMetadataPrefix = @"Windows\Packages\DownlevelMetadata\";
        private const string DsmFilesPrefix = @"Windows\Packages\DsmFiles\";
        private const string CatRootPrefix = @"Windows\System32\catroot\{F750E6C3-38EE-11D1-85E5-00C04FC295EE}\";
        private const string WinSxSPrefix = @"windows\WinSxS\";

        private sealed class DdfItem
        {
            public string SourcePath;
            public string CabPath;
        }

        internal static bool ConvertPaperSpkgsInDirectory(string packagesDirectory)
        {
            if (string.IsNullOrWhiteSpace(packagesDirectory) || !Directory.Exists(packagesDirectory))
            {
                Program.appendLog($"[PAPER SPKG] Packages directory not found: {packagesDirectory}\n");
                return false;
            }

            string[] cabFiles = Directory.GetFiles(packagesDirectory, "*.cab", SearchOption.TopDirectoryOnly)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (cabFiles.Length == 0)
            {
                return true;
            }

            string makecabPath = GetMakecabPath();
            int convertedCount = 0;
            int inspectedCount = 0;

            for (int i = 0; i < cabFiles.Length; i++)
            {
                string cabPath = cabFiles[i];
                string cabName = Path.GetFileName(cabPath);
                inspectedCount++;

                try
                {
                    bool wasPaperSpkg;
                    bool ok = ConvertSinglePaperSpkg(cabPath, makecabPath, out wasPaperSpkg);
                    if (!ok)
                    {
                        Program.WriteLine($"\n[PAPER SPKG] Failed converting {cabName}", ConsoleColor.Red);
                        return false;
                    }

                    if (wasPaperSpkg)
                    {
                        convertedCount++;
                        Program.resetCursorPosition();
                        Program.WriteLine($"[PAPER SPKG -> CBS] [{i + 1}/{cabFiles.Length}] {cabName}", ConsoleColor.DarkGray, true);
                    }
                }
                catch (Exception ex)
                {
                    Program.appendLog($"[PAPER SPKG] Exception converting {cabPath}: {ex}\n");
                    Program.WriteLine($"\n[PAPER SPKG] Error converting {cabName}: {ex.Message}", ConsoleColor.Red);
                    return false;
                }
            }

            if (convertedCount > 0)
            {
                Program.WriteLine($"Converted {convertedCount} paper SPKG package(s) to CBS format.", ConsoleColor.Green);
            }
            Program.appendLog($"[PAPER SPKG] Inspected {inspectedCount} CAB(s), converted {convertedCount} paper SPKG(s).\n");
            return true;
        }

        internal static bool ConvertSinglePaperSpkg(string cabPath, string makecabPath, out bool wasPaperSpkg)
        {
            wasPaperSpkg = false;
            if (string.IsNullOrWhiteSpace(cabPath) || !File.Exists(cabPath) || !IsCabinetFile(cabPath))
            {
                return true;
            }

            List<string> entries = CabFdiSession.ListEntries(cabPath);
            if (entries.Count == 0)
            {
                return true;
            }

            bool hasUpdateMum = entries.Any(e => string.Equals(NormalizeRelPath(e), "update.mum", StringComparison.OrdinalIgnoreCase));
            bool hasManDsm = entries.Any(e => string.Equals(NormalizeRelPath(e), "man.dsm.xml", StringComparison.OrdinalIgnoreCase));
            if (hasUpdateMum || !hasManDsm)
            {
                return true;
            }

            byte[] dsmBytes = CabFdiSession.ExtractSingleFileToMemory(cabPath, "man.dsm.xml");
            if (dsmBytes == null || dsmBytes.Length == 0)
            {
                return true;
            }

            string metaCabRelPath;
            bool isRemoval;
            bool isBinaryPartition;
            Dictionary<string, string> deviceToRawCabPath;
            if (!TryParseManDsm(dsmBytes, out isRemoval, out isBinaryPartition, out metaCabRelPath, out deviceToRawCabPath))
            {
                Program.appendLog($"[PAPER SPKG] Failed to parse man.dsm.xml in {cabPath}\n");
                return false;
            }

            if (isRemoval || isBinaryPartition || string.IsNullOrEmpty(metaCabRelPath))
            {
                return true;
            }

            wasPaperSpkg = true;
            if (string.IsNullOrEmpty(makecabPath) || !File.Exists(makecabPath))
            {
                Program.appendLog("[PAPER SPKG] makecab.exe not found on host.\n");
                return false;
            }

            string workDir = Path.Combine(Path.GetTempPath(), "uu_spkg_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            string rawDir = Path.Combine(workDir, "r");
            string xapDir = Path.Combine(workDir, "x");
            string outDir = Path.Combine(workDir, "o");
            string shortDir = Path.Combine(workDir, "s");
            string tmpTarget = cabPath + ".cbs.tmp";

            try
            {
                Directory.CreateDirectory(rawDir);
                Directory.CreateDirectory(xapDir);
                Directory.CreateDirectory(outDir);

                if (!CabFdiSession.ExtractAllToDirectory(cabPath, rawDir))
                {
                    Program.appendLog($"[PAPER SPKG] Failed extracting {cabPath}\n");
                    return false;
                }

                string metaCabFullPath = Path.Combine(rawDir, NormalizeRelPath(metaCabRelPath));
                if (!File.Exists(metaCabFullPath) || !IsCabinetFile(metaCabFullPath))
                {
                    Program.appendLog($"[PAPER SPKG] Missing or invalid DownlevelMetadata CAB '{metaCabRelPath}' in {cabPath}\n");
                    return false;
                }

                if (!CabFdiSession.ExtractAllToDirectory(metaCabFullPath, xapDir))
                {
                    Program.appendLog($"[PAPER SPKG] Failed extracting DownlevelMetadata CAB '{metaCabRelPath}' from {cabPath}\n");
                    return false;
                }

                string mumPath = FindSingleFileRecursive(xapDir, "update.mum");
                string catPath = FindSingleFileRecursive(xapDir, "update.cat");
                string mappingPath = FindSingleFileRecursive(xapDir, "mapping.csv");
                string[] manifestPaths = Directory.GetFiles(xapDir, "*.manifest", SearchOption.AllDirectories)
                    .OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                if (string.IsNullOrEmpty(mumPath) || string.IsNullOrEmpty(catPath) || string.IsNullOrEmpty(mappingPath) || manifestPaths.Length == 0)
                {
                    Program.appendLog($"[PAPER SPKG] Incomplete DownlevelMetadata contents in {cabPath} (mum={mumPath != null}, cat={catPath != null}, map={mappingPath != null}, manifests={manifestPaths.Length})\n");
                    return false;
                }

                Dictionary<string, string> mumCabPaths;
                if (!TryParseUpdateMumCabPaths(mumPath, out mumCabPaths))
                {
                    Program.appendLog($"[PAPER SPKG] Failed parsing update.mum in {cabPath}\n");
                    return false;
                }

                List<DdfItem> ddfItems = new List<DdfItem>();
                HashSet<string> includedCabPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                ddfItems.Add(new DdfItem { SourcePath = EnsureShortMakecabSource(mumPath, shortDir, 0), CabPath = "update.mum" });
                includedCabPaths.Add("update.mum");

                ddfItems.Add(new DdfItem { SourcePath = EnsureShortMakecabSource(catPath, shortDir, 1), CabPath = "update.cat" });
                includedCabPaths.Add("update.cat");

                int aliasCounter = 2;
                HashSet<string> mappedDevices = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (string rawLine in File.ReadAllLines(mappingPath))
                {
                    if (string.IsNullOrWhiteSpace(rawLine))
                    {
                        continue;
                    }

                    int commaIdx = rawLine.IndexOf(',');
                    if (commaIdx <= 0 || commaIdx >= rawLine.Length - 1)
                    {
                        Program.appendLog($"[PAPER SPKG] Invalid mapping.csv line '{rawLine}' in {cabPath}\n");
                        return false;
                    }

                    string devKey = NormalizeDeviceKey(rawLine.Substring(0, commaIdx));
                    string winSxSTarget = NormalizeRelPath(rawLine.Substring(commaIdx + 1));
                    if (!winSxSTarget.StartsWith(WinSxSPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        Program.appendLog($"[PAPER SPKG] Unexpected mapping target '{winSxSTarget}' in {cabPath}\n");
                        return false;
                    }

                    string cabRelTarget = winSxSTarget.Substring(WinSxSPrefix.Length);
                    if (string.IsNullOrWhiteSpace(cabRelTarget))
                    {
                        Program.appendLog($"[PAPER SPKG] Empty relative cabpath for '{devKey}' in {cabPath}\n");
                        return false;
                    }

                    string exactCabPath;
                    if (mumCabPaths.TryGetValue(cabRelTarget, out exactCabPath))
                    {
                        cabRelTarget = exactCabPath;
                    }

                    string rawSrcRel;
                    if (!deviceToRawCabPath.TryGetValue(devKey, out rawSrcRel))
                    {
                        Program.appendLog($"[PAPER SPKG] DevicePath '{devKey}' from mapping.csv missing in man.dsm.xml ({cabPath})\n");
                        return false;
                    }

                    string srcFile = Path.Combine(rawDir, NormalizeRelPath(rawSrcRel));
                    if (!File.Exists(srcFile))
                    {
                        Program.appendLog($"[PAPER SPKG] Extracted payload file missing: '{srcFile}' ({cabPath})\n");
                        return false;
                    }

                    if (includedCabPaths.Add(cabRelTarget))
                    {
                        ddfItems.Add(new DdfItem
                        {
                            SourcePath = EnsureShortMakecabSource(srcFile, shortDir, aliasCounter++),
                            CabPath = cabRelTarget
                        });
                    }
                    mappedDevices.Add(devKey);
                }

                foreach (string devKey in deviceToRawCabPath.Keys)
                {
                    if (mappedDevices.Contains(devKey))
                    {
                        continue;
                    }

                    if (!IsIgnorableSpkgMetadataPath(devKey))
                    {
                        Program.appendLog($"[PAPER SPKG] Unmapped FileEntry '{devKey}' in man.dsm.xml ({cabPath})\n");
                        return false;
                    }
                }

                foreach (string manifestPath in manifestPaths)
                {
                    string manifestName = Path.GetFileName(manifestPath);
                    if (includedCabPaths.Add(manifestName))
                    {
                        ddfItems.Add(new DdfItem
                        {
                            SourcePath = EnsureShortMakecabSource(manifestPath, shortDir, aliasCounter++),
                            CabPath = manifestName
                        });
                    }
                }

                foreach (string expectedMumPath in mumCabPaths.Values)
                {
                    if (!includedCabPaths.Contains(expectedMumPath))
                    {
                        Program.appendLog($"[PAPER SPKG] update.mum cabpath '{expectedMumPath}' missing from converted payload ({cabPath})\n");
                        return false;
                    }
                }

                string outCabName = "converted.cab";
                string outCabPath = Path.Combine(outDir, outCabName);
                string ddfPath = Path.Combine(workDir, "pack.ddf");
                WriteDdfFile(ddfPath, outCabName, outDir, ddfItems);

                string makecabOutput;
                int makecabExit = RunMakecab(makecabPath, ddfPath, workDir, out makecabOutput);
                if (makecabExit != 0 || !File.Exists(outCabPath) || !IsCabinetFile(outCabPath))
                {
                    Program.appendLog($"[PAPER SPKG] makecab.exe failed (exit={makecabExit}) for {cabPath}:\n{makecabOutput}\n");
                    return false;
                }

                List<string> verifiedEntries = CabFdiSession.ListEntries(outCabPath);
                if (verifiedEntries.Count != ddfItems.Count ||
                    !verifiedEntries.Any(e => string.Equals(NormalizeRelPath(e), "update.mum", StringComparison.OrdinalIgnoreCase)))
                {
                    Program.appendLog($"[PAPER SPKG] Output CAB entry check failed for {cabPath}: expected {ddfItems.Count}, got {verifiedEntries.Count}\n");
                    return false;
                }

                if (File.Exists(tmpTarget))
                {
                    ClearReadOnly(tmpTarget);
                    File.Delete(tmpTarget);
                }

                File.Copy(outCabPath, tmpTarget, true);
                ClearReadOnly(cabPath);
                File.Delete(cabPath);
                File.Move(tmpTarget, cabPath);

                Program.appendLog($"[PAPER SPKG] Converted {Path.GetFileName(cabPath)} ({ddfItems.Count} CBS entries)\n");
                return true;
            }
            finally
            {
                try
                {
                    if (File.Exists(tmpTarget) && File.Exists(cabPath))
                    {
                        ClearReadOnly(tmpTarget);
                        File.Delete(tmpTarget);
                    }
                }
                catch { }

                try
                {
                    if (Directory.Exists(workDir))
                    {
                        Directory.Delete(workDir, true);
                    }
                }
                catch { }
            }
        }

        internal static bool IsCabinetFile(string filePath)
        {
            try
            {
                using (var fs = File.OpenRead(filePath))
                {
                    if (fs.Length < 36)
                        return false;
                    byte[] header = new byte[4];
                    if (fs.Read(header, 0, 4) != 4)
                        return false;
                    return header[0] == 0x4D && header[1] == 0x53 && header[2] == 0x43 && header[3] == 0x46; // MSCF
                }
            }
            catch
            {
                return false;
            }
        }

        private static bool TryParseManDsm(
            byte[] xmlBytes,
            out bool isRemoval,
            out bool isBinaryPartition,
            out string metaCabRelPath,
            out Dictionary<string, string> deviceToRawCabPath)
        {
            isRemoval = false;
            isBinaryPartition = false;
            metaCabRelPath = null;
            deviceToRawCabPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                var doc = new XmlDocument();
                var settings = new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null
                };
                using (var ms = new MemoryStream(xmlBytes))
                using (var reader = XmlReader.Create(ms, settings))
                {
                    doc.Load(reader);
                }

                var removalNode = doc.SelectSingleNode("/*[local-name()='Package']/*[local-name()='IsRemoval']");
                if (removalNode != null && bool.TryParse(removalNode.InnerText?.Trim(), out bool remVal))
                {
                    isRemoval = remVal;
                }

                var binaryNode = doc.SelectSingleNode("/*[local-name()='Package']/*[local-name()='IsBinaryPartition']");
                if (binaryNode != null && bool.TryParse(binaryNode.InnerText?.Trim(), out bool binVal))
                {
                    isBinaryPartition = binVal;
                }

                var entryNodes = doc.SelectNodes("/*[local-name()='Package']/*[local-name()='Files']/*[local-name()='FileEntry']");
                if (entryNodes == null)
                {
                    return true;
                }

                foreach (XmlNode entry in entryNodes)
                {
                    string devPath = null;
                    string cabPath = null;
                    foreach (XmlNode child in entry.ChildNodes)
                    {
                        if (string.Equals(child.LocalName, "DevicePath", StringComparison.OrdinalIgnoreCase))
                        {
                            devPath = child.InnerText;
                        }
                        else if (string.Equals(child.LocalName, "CabPath", StringComparison.OrdinalIgnoreCase))
                        {
                            cabPath = child.InnerText;
                        }
                    }

                    string normalizedDev = NormalizeDeviceKey(devPath);
                    string normalizedCab = NormalizeRelPath(cabPath);
                    if (string.IsNullOrEmpty(normalizedDev) || string.IsNullOrEmpty(normalizedCab))
                    {
                        continue;
                    }

                    if (normalizedDev.StartsWith(DownlevelMetadataPrefix, StringComparison.OrdinalIgnoreCase) &&
                        normalizedDev.EndsWith(".xap", StringComparison.OrdinalIgnoreCase))
                    {
                        metaCabRelPath = normalizedCab;
                    }
                    else
                    {
                        deviceToRawCabPath[normalizedDev] = normalizedCab;
                    }
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryParseUpdateMumCabPaths(string mumPath, out Dictionary<string, string> mumCabPaths)
        {
            mumCabPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var doc = new XmlDocument();
                var settings = new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null
                };
                using (var fs = File.OpenRead(mumPath))
                using (var reader = XmlReader.Create(fs, settings))
                {
                    doc.Load(reader);
                }

                var fileNodes = doc.SelectNodes("//*[local-name()='customInformation']/*[local-name()='file']");
                if (fileNodes == null)
                {
                    return true;
                }

                foreach (XmlNode node in fileNodes)
                {
                    var cabAttr = node.Attributes?["cabpath"];
                    if (cabAttr == null || string.IsNullOrWhiteSpace(cabAttr.Value))
                    {
                        continue;
                    }

                    string cabPath = NormalizeRelPath(cabAttr.Value);
                    if (!string.IsNullOrEmpty(cabPath))
                    {
                        mumCabPaths[cabPath] = cabPath;
                    }
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsIgnorableSpkgMetadataPath(string normalizedDevicePath)
        {
            if (string.IsNullOrEmpty(normalizedDevicePath))
                return false;

            if (normalizedDevicePath.StartsWith(DsmFilesPrefix, StringComparison.OrdinalIgnoreCase) &&
                normalizedDevicePath.EndsWith(".dsm.xml", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (normalizedDevicePath.StartsWith(CatRootPrefix, StringComparison.OrdinalIgnoreCase) &&
                normalizedDevicePath.EndsWith(".cat", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        private static string EnsureShortMakecabSource(string srcPath, string shortDir, int index)
        {
            if (srcPath.Length < 245)
            {
                return srcPath;
            }

            Directory.CreateDirectory(shortDir);
            string alias = Path.Combine(shortDir, $"f{index}.dat");
            File.Copy(srcPath, alias, true);
            return alias;
        }

        private static string FindSingleFileRecursive(string rootDir, string exactFileName)
        {
            return Directory.GetFiles(rootDir, exactFileName, SearchOption.AllDirectories)
                .FirstOrDefault(p => string.Equals(Path.GetFileName(p), exactFileName, StringComparison.OrdinalIgnoreCase));
        }

        private static string NormalizeDeviceKey(string path)
        {
            return NormalizeRelPath(path);
        }

        private static string NormalizeRelPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }
            return path.Trim().Replace('/', '\\').TrimStart('\\');
        }

        private static void WriteDdfFile(string ddfPath, string cabinetName, string diskDir, List<DdfItem> items)
        {
            var sb = new StringBuilder();
            sb.AppendLine(".OPTION EXPLICIT");
            sb.Append(".Set CabinetNameTemplate=").AppendLine(cabinetName);
            sb.Append(".Set DiskDirectory1=").AppendLine(diskDir);
            sb.AppendLine(".Set CompressionType=LZX");
            sb.AppendLine(".Set CompressionMemory=15");
            sb.AppendLine(".Set FolderSizeThreshold=2000000000");
            sb.AppendLine(".Set MaxDiskSize=0");
            sb.AppendLine(".Set MaxCabinetSize=0");
            sb.AppendLine(".Set MaxErrors=1");
            sb.AppendLine(".Set Cabinet=ON");
            sb.AppendLine(".Set Compress=ON");
            sb.AppendLine(".Set UniqueFiles=OFF");
            sb.AppendLine(".Set RptFileName=NUL");
            sb.AppendLine(".Set InfFileName=NUL");

            foreach (var item in items)
            {
                sb.Append('"').Append(item.SourcePath).Append("\" \"").Append(item.CabPath).AppendLine("\"");
            }

            // makecab.exe expects ANSI/non-BOM text; a UTF-8 BOM causes syntax errors on line 1.
            File.WriteAllText(ddfPath, sb.ToString(), Encoding.Default);
        }

        private static string GetMakecabPath()
        {
            string winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string candidate = Path.Combine(winDir, "System32", "makecab.exe");
            return File.Exists(candidate) ? candidate : null;
        }

        private static int RunMakecab(string makecabPath, string ddfPath, string workingDir, out string combinedOutput)
        {
            var sb = new StringBuilder();
            using (var proc = new Process())
            {
                proc.StartInfo.FileName = makecabPath;
                proc.StartInfo.Arguments = $"/V0 /F \"{ddfPath}\"";
                proc.StartInfo.WorkingDirectory = workingDir;
                proc.StartInfo.UseShellExecute = false;
                proc.StartInfo.CreateNoWindow = true;
                proc.StartInfo.RedirectStandardOutput = true;
                proc.StartInfo.RedirectStandardError = true;

                proc.OutputDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        lock (sb) { sb.AppendLine(e.Data); }
                    }
                };
                proc.ErrorDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        lock (sb) { sb.AppendLine(e.Data); }
                    }
                };

                proc.Start();
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();
                proc.WaitForExit();
                combinedOutput = sb.ToString();
                return proc.ExitCode;
            }
        }

        private static void ClearReadOnly(string filePath)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    var attrs = File.GetAttributes(filePath);
                    if ((attrs & FileAttributes.ReadOnly) != 0)
                    {
                        File.SetAttributes(filePath, attrs & ~FileAttributes.ReadOnly);
                    }
                }
            }
            catch { }
        }

        private sealed class CabFdiSession
        {
            private static readonly object SessionLock = new object();

            private enum FdiNotificationType
            {
                CabinetInfo,
                PartialFile,
                CopyFile,
                CloseFileInfo,
                NextCabinet,
                Enumerate
            }

            [StructLayout(LayoutKind.Sequential)]
            private class CabError
            {
                public int erfOper;
                public int erfType;
                public int fError;
            }

            [StructLayout(LayoutKind.Sequential)]
            private class FdiNotification
            {
                public int cb;
                public IntPtr psz1;
                public IntPtr psz2;
                public IntPtr psz3;
                public IntPtr pv;
                public IntPtr hf;
                public short date;
                public short time;
                public short attribs;
                public short setID;
                public short iCabinet;
                public short iFolder;
                public int fdie;
            }

            [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
            private delegate IntPtr FdiMemAllocDelegate(int numBytes);

            [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
            private delegate void FdiMemFreeDelegate(IntPtr mem);

            [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
            private delegate IntPtr FdiFileOpenDelegate(string fileName, int oflag, int pmode);

            [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
            private delegate int FdiFileReadDelegate(IntPtr hf,
                [In, Out] [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 2, ArraySubType = UnmanagedType.U1)] byte[] buffer,
                int cb);

            [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
            private delegate int FdiFileWriteDelegate(IntPtr hf,
                [In] [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 2, ArraySubType = UnmanagedType.U1)] byte[] buffer,
                int cb);

            [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
            private delegate int FdiFileCloseDelegate(IntPtr hf);

            [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
            private delegate int FdiFileSeekDelegate(IntPtr hf, int dist, int seektype);

            [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
            private delegate IntPtr FdiNotifyDelegate(
                FdiNotificationType fdint,
                [In] [MarshalAs(UnmanagedType.LPStruct)] FdiNotification fdin);

            [DllImport("cabinet.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "FDICreate", CharSet = CharSet.Ansi)]
            private static extern IntPtr FdiCreate(
                FdiMemAllocDelegate fnMemAlloc,
                FdiMemFreeDelegate fnMemFree,
                FdiFileOpenDelegate fnFileOpen,
                FdiFileReadDelegate fnFileRead,
                FdiFileWriteDelegate fnFileWrite,
                FdiFileCloseDelegate fnFileClose,
                FdiFileSeekDelegate fnFileSeek,
                int cpuType,
                [MarshalAs(UnmanagedType.LPStruct)] CabError erf);

            [DllImport("cabinet.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "FDIDestroy")]
            private static extern bool FdiDestroy(IntPtr hfdi);

            [DllImport("cabinet.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "FDICopy", CharSet = CharSet.Ansi)]
            private static extern bool FdiCopy(
                IntPtr hfdi,
                string cabinetName,
                string cabinetPath,
                int flags,
                FdiNotifyDelegate fnNotify,
                IntPtr fnDecrypt,
                IntPtr userData);

            private static string _activeCabPath;
            private static string _extractRootDir;
            private static string _singleTargetName;
            private static byte[] _singleTargetBytes;
            private static List<string> _listedEntries;
            private static readonly HashSet<GCHandle> OpenHandles = new HashSet<GCHandle>();

            private static readonly FdiMemAllocDelegate AllocHandler = MemAlloc;
            private static readonly FdiMemFreeDelegate FreeHandler = MemFree;
            private static readonly FdiFileOpenDelegate OpenHandler = OpenCabinetInput;
            private static readonly FdiFileReadDelegate ReadHandler = StreamRead;
            private static readonly FdiFileWriteDelegate WriteHandler = StreamWrite;
            private static readonly FdiFileCloseDelegate CloseHandler = CloseStreamHandle;
            private static readonly FdiFileSeekDelegate SeekHandler = StreamSeek;
            private static readonly FdiNotifyDelegate ListNotifyHandler = ListCallback;
            private static readonly FdiNotifyDelegate SingleMemoryNotifyHandler = SingleMemoryCallback;
            private static readonly FdiNotifyDelegate ExtractAllNotifyHandler = ExtractAllCallback;

            public static List<string> ListEntries(string cabPath)
            {
                lock (SessionLock)
                {
                    var results = new List<string>();
                    _listedEntries = results;
                    RunCopyLocked(cabPath, ListNotifyHandler);
                    _listedEntries = null;
                    return results;
                }
            }

            public static byte[] ExtractSingleFileToMemory(string cabPath, string entryName)
            {
                lock (SessionLock)
                {
                    _singleTargetName = NormalizeRelPath(entryName);
                    _singleTargetBytes = null;
                    bool ok = RunCopyLocked(cabPath, SingleMemoryNotifyHandler);
                    byte[] bytes = ok ? _singleTargetBytes : null;
                    _singleTargetName = null;
                    _singleTargetBytes = null;
                    return bytes;
                }
            }

            public static bool ExtractAllToDirectory(string cabPath, string destDir)
            {
                lock (SessionLock)
                {
                    _extractRootDir = Path.GetFullPath(destDir);
                    try
                    {
                        return RunCopyLocked(cabPath, ExtractAllNotifyHandler);
                    }
                    finally
                    {
                        _extractRootDir = null;
                    }
                }
            }

            private static bool RunCopyLocked(string cabPath, FdiNotifyDelegate notifyHandler)
            {
                _activeCabPath = cabPath;
                IntPtr hfdi = IntPtr.Zero;
                var erf = new CabError();
                try
                {
                    hfdi = FdiCreate(AllocHandler, FreeHandler, OpenHandler, ReadHandler, WriteHandler, CloseHandler, SeekHandler, -1, erf);
                    if (hfdi == IntPtr.Zero)
                        return false;
                    return FdiCopy(hfdi, "<cab>", string.Empty, 0, notifyHandler, IntPtr.Zero, IntPtr.Zero);
                }
                catch
                {
                    return false;
                }
                finally
                {
                    ReleaseDanglingHandles();
                    if (hfdi != IntPtr.Zero)
                    {
                        try { FdiDestroy(hfdi); } catch { }
                    }
                    _activeCabPath = null;
                }
            }

            private static void ReleaseDanglingHandles()
            {
                foreach (GCHandle gch in OpenHandles.ToArray())
                {
                    try
                    {
                        if (gch.IsAllocated)
                        {
                            var stream = gch.Target as Stream;
                            stream?.Dispose();
                            gch.Free();
                        }
                    }
                    catch { }
                }
                OpenHandles.Clear();
            }

            private static string ReadEntryName(FdiNotification fdin)
            {
                var encoding = ((int)fdin.attribs & 128) != 0 ? Encoding.UTF8 : Encoding.Default;
                int len = 0;
                while (Marshal.ReadByte(fdin.psz1, len) != 0)
                    checked { len++; }
                byte[] buf = new byte[len];
                Marshal.Copy(fdin.psz1, buf, 0, len);
                string raw = encoding.GetString(buf);
                return raw.Replace('/', '\\').TrimStart('\\');
            }

            private static string SanitizeSafeRelativePath(string rawRelPath)
            {
                if (string.IsNullOrWhiteSpace(rawRelPath))
                    return null;
                if (Path.IsPathRooted(rawRelPath))
                    rawRelPath = rawRelPath.Replace(Path.VolumeSeparatorChar.ToString(), string.Empty).TrimStart('\\');

                string[] parts = rawRelPath.Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
                var safeParts = new List<string>(parts.Length);
                foreach (string part in parts)
                {
                    if (part == ".")
                        continue;
                    if (part == "..")
                        return null;
                    safeParts.Add(part);
                }
                return safeParts.Count > 0 ? Path.Combine(safeParts.ToArray()) : null;
            }

            private static IntPtr ListCallback(FdiNotificationType fdint, FdiNotification fdin)
            {
                if (fdint == FdiNotificationType.CopyFile)
                {
                    _listedEntries?.Add(ReadEntryName(fdin));
                    return IntPtr.Zero;
                }
                if (fdint == FdiNotificationType.NextCabinet)
                {
                    return (IntPtr)(-1);
                }
                return IntPtr.Zero;
            }

            private static IntPtr SingleMemoryCallback(FdiNotificationType fdint, FdiNotification fdin)
            {
                switch (fdint)
                {
                    case FdiNotificationType.CopyFile:
                        string name = NormalizeRelPath(ReadEntryName(fdin));
                        if (string.Equals(name, _singleTargetName, StringComparison.OrdinalIgnoreCase))
                        {
                            var ms = new MemoryStream();
                            var gch = GCHandle.Alloc(ms);
                            OpenHandles.Add(gch);
                            return (IntPtr)gch;
                        }
                        return IntPtr.Zero;

                    case FdiNotificationType.CloseFileInfo:
                        var handle = (GCHandle)fdin.hf;
                        var stream = (MemoryStream)handle.Target;
                        _singleTargetBytes = stream.ToArray();
                        stream.Dispose();
                        OpenHandles.Remove(handle);
                        handle.Free();
                        return (IntPtr)1;

                    case FdiNotificationType.NextCabinet:
                        return (IntPtr)(-1);

                    default:
                        return IntPtr.Zero;
                }
            }

            private static IntPtr ExtractAllCallback(FdiNotificationType fdint, FdiNotification fdin)
            {
                switch (fdint)
                {
                    case FdiNotificationType.CopyFile:
                        string rel = SanitizeSafeRelativePath(ReadEntryName(fdin));
                        if (string.IsNullOrEmpty(rel))
                            return (IntPtr)(-1);

                        string destFull = Path.GetFullPath(Path.Combine(_extractRootDir, rel));
                        if (!destFull.StartsWith(_extractRootDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                            return (IntPtr)(-1);

                        string parent = Path.GetDirectoryName(destFull);
                        if (!string.IsNullOrEmpty(parent))
                            Directory.CreateDirectory(parent);

                        var fs = new FileStream(destFull, FileMode.Create, FileAccess.Write, FileShare.None);
                        var gch = GCHandle.Alloc(fs);
                        OpenHandles.Add(gch);
                        return (IntPtr)gch;

                    case FdiNotificationType.CloseFileInfo:
                        var handle = (GCHandle)fdin.hf;
                        var outStream = (Stream)handle.Target;
                        outStream.Dispose();
                        OpenHandles.Remove(handle);
                        handle.Free();
                        return (IntPtr)1;

                    case FdiNotificationType.NextCabinet:
                        return (IntPtr)(-1);

                    default:
                        return IntPtr.Zero;
                }
            }

            private static IntPtr MemAlloc(int cb) => Marshal.AllocHGlobal(cb);
            private static void MemFree(IntPtr mem) => Marshal.FreeHGlobal(mem);

            private static IntPtr OpenCabinetInput(string fileName, int oflag, int pmode)
            {
                var fs = new FileStream(_activeCabPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                var gch = GCHandle.Alloc(fs);
                OpenHandles.Add(gch);
                return (IntPtr)gch;
            }

            private static int CloseStreamHandle(IntPtr hf)
            {
                var gch = (GCHandle)hf;
                var s = (Stream)gch.Target;
                s.Dispose();
                OpenHandles.Remove(gch);
                gch.Free();
                return 0;
            }

            private static int StreamRead(IntPtr hf, byte[] buffer, int cb)
            {
                var s = (Stream)((GCHandle)hf).Target;
                return s.Read(buffer, 0, cb);
            }

            private static int StreamWrite(IntPtr hf, byte[] buffer, int cb)
            {
                var s = (Stream)((GCHandle)hf).Target;
                s.Write(buffer, 0, cb);
                return cb;
            }

            private static int StreamSeek(IntPtr hf, int dist, int seektype)
            {
                var s = (Stream)((GCHandle)hf).Target;
                return (int)s.Seek(dist, (SeekOrigin)seektype);
            }
        }
    }
}
