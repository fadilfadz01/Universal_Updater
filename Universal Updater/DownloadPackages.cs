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
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Universal_Updater
{
    internal enum PackageKind
    {
        Unknown = 0,
        CbsCanonical = 1,
        CbsUpdate = 2,
        CbsRemoval = 3,
        SpkgCanonical = 4,
        SpkgRemoval = 5,
        PlainCab = 6
    }

    class DownloadPackages
    {
        static Tuple<DateTime, long, long> DownloadingProgress = new Tuple<DateTime, long, long>(DateTime.MinValue, 0, 0);
        public static string[] installedPackages = new string[] { };
        public static List<string> filteredPackages = new List<string>();
        static bool isFeatureInstalled = false;
        static bool filterCBSPackagesOnly = false;
        static readonly string[] knownPackages = { "ms_commsenhancementglobal.mainos", "ms_commsmessagingglobal.mainos", "microsoftphonefm.platformmanifest.efiesp", "microsoftphonefm.platformmanifest.mainos", "microsoftphonefm.platformmanifest.updateos", "UserInstallableFM.PlatformManifest" };
        public static FFUEntry FFUMountData = null;
        static readonly string[] skippedPackages = { };
        static readonly string[] packageCBSExtension = { ".cab", ".cbs_", "cbs.", ".cbsu_", "cbsu." };
        static readonly string[] packageCBSExtensionRemoval = { ".cbsr_", ".cbsr", "cbsr." };
        static readonly string[] packageSPKGExtension = { ".spkg", ".spkg_", "spkg." };
        static readonly string[] packageSPKGExtensionRemoval = { ".spkr_", ".spkr", "spkr." };
        static Uri downloadFile;
        static string _currentDownloadName = string.Empty;
        static long _currentTotalBytes = -1;

        public static string[] getExtensionsList()
        {
            if (!filterCBSPackagesOnly)
            {
                return packageSPKGExtension.Concat(packageSPKGExtensionRemoval).ToArray();
            }
            return packageCBSExtension.Concat(packageCBSExtensionRemoval).ToArray();
        }

        internal static string GetPackageFileName(string pathOrUrl)
        {
            if (string.IsNullOrWhiteSpace(pathOrUrl))
                return string.Empty;

            string clean = pathOrUrl.Trim();
            int queryIdx = clean.IndexOf('?');
            if (queryIdx >= 0)
                clean = clean.Substring(0, queryIdx);

            int slashIdx = clean.LastIndexOfAny(new[] { '/', '\\' });
            return slashIdx >= 0 ? clean.Substring(slashIdx + 1) : clean;
        }

        private static bool IsHexString(string value)
        {
            if (string.IsNullOrEmpty(value))
                return false;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                bool isHex = (c >= '0' && c <= '9') ||
                             (c >= 'a' && c <= 'f') ||
                             (c >= 'A' && c <= 'F');
                if (!isHex)
                    return false;
            }
            return true;
        }

        internal static bool TryParsePackageFile(string pathOrUrl, out string fileName, out string packageStem, out PackageKind kind)
        {
            fileName = GetPackageFileName(pathOrUrl);
            packageStem = string.Empty;
            kind = PackageKind.Unknown;

            if (string.IsNullOrEmpty(fileName))
                return false;

            if (fileName.EndsWith(".cab", StringComparison.OrdinalIgnoreCase))
            {
                string body = fileName.Substring(0, fileName.Length - 4);
                int underscoreIdx = body.LastIndexOf('_');
                if (underscoreIdx > 0)
                {
                    string beforeHash = body.Substring(0, underscoreIdx);
                    string hashPart = body.Substring(underscoreIdx + 1);
                    if (hashPart.Length >= 8 && IsHexString(hashPart))
                    {
                        if (TryStripMarkerSuffix(beforeHash, out packageStem, out kind))
                            return true;
                    }
                }

                if (TryStripMarkerSuffix(body, out packageStem, out kind))
                    return true;

                packageStem = body;
                kind = PackageKind.PlainCab;
                return true;
            }

            if (fileName.EndsWith(".spkg", StringComparison.OrdinalIgnoreCase))
            {
                packageStem = fileName.Substring(0, fileName.Length - 5);
                kind = PackageKind.SpkgCanonical;
                return true;
            }

            if (fileName.EndsWith(".spkr", StringComparison.OrdinalIgnoreCase))
            {
                packageStem = fileName.Substring(0, fileName.Length - 5);
                kind = PackageKind.SpkgRemoval;
                return true;
            }

            return false;
        }

        private static bool TryStripMarkerSuffix(string body, out string packageStem, out PackageKind kind)
        {
            if (body.EndsWith(".cbs", StringComparison.OrdinalIgnoreCase))
            {
                packageStem = body.Substring(0, body.Length - 4);
                kind = PackageKind.CbsCanonical;
                return true;
            }
            if (body.EndsWith(".cbsu", StringComparison.OrdinalIgnoreCase))
            {
                packageStem = body.Substring(0, body.Length - 5);
                kind = PackageKind.CbsUpdate;
                return true;
            }
            if (body.EndsWith(".cbsr", StringComparison.OrdinalIgnoreCase))
            {
                packageStem = body.Substring(0, body.Length - 5);
                kind = PackageKind.CbsRemoval;
                return true;
            }
            if (body.EndsWith(".spkg", StringComparison.OrdinalIgnoreCase))
            {
                packageStem = body.Substring(0, body.Length - 5);
                kind = PackageKind.SpkgCanonical;
                return true;
            }
            if (body.EndsWith(".spkr", StringComparison.OrdinalIgnoreCase))
            {
                packageStem = body.Substring(0, body.Length - 5);
                kind = PackageKind.SpkgRemoval;
                return true;
            }

            packageStem = string.Empty;
            kind = PackageKind.Unknown;
            return false;
        }

        private static bool IsCbsFamilyKind(PackageKind kind)
        {
            return kind == PackageKind.CbsCanonical || kind == PackageKind.CbsUpdate || kind == PackageKind.CbsRemoval || kind == PackageKind.PlainCab;
        }

        private static bool IsSpkgFamilyKind(PackageKind kind)
        {
            return kind == PackageKind.SpkgCanonical || kind == PackageKind.SpkgRemoval;
        }

        private static bool IsRemovalKind(PackageKind kind)
        {
            return kind == PackageKind.CbsRemoval || kind == PackageKind.SpkgRemoval;
        }

        private static int GetPackageKindPriority(PackageKind kind)
        {
            // Prefer canonical base package over differential .cbsu update when both exist in a manifest/folder.
            switch (kind)
            {
                case PackageKind.CbsCanonical:
                case PackageKind.SpkgCanonical:
                    return 0;
                case PackageKind.PlainCab:
                    return 1;
                case PackageKind.CbsRemoval:
                case PackageKind.SpkgRemoval:
                    return 2;
                case PackageKind.CbsUpdate:
                    return 3;
                default:
                    return 99;
            }
        }

        internal static string ExtractPackageNameFromCsvOrLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return null;

            string trimmed = line.Trim();
            if (trimmed.IndexOf(',') >= 0)
            {
                var parts = trimmed.Split(',');
                if (parts.Length < 2)
                    return null;
                string part = parts[1].Trim().Trim('"');
                if (string.IsNullOrEmpty(part) ||
                    part.Equals("Name", StringComparison.OrdinalIgnoreCase) ||
                    part.Equals("PackageName", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }
                return part;
            }

            return trimmed.Trim('"');
        }

        private static bool StemMatchesTarget(string stem, string targetName, bool allowSegmentMatch)
        {
            if (string.IsNullOrEmpty(stem) || string.IsNullOrEmpty(targetName))
                return false;
            if (stem.Equals(targetName, StringComparison.OrdinalIgnoreCase))
                return true;
            if (!allowSegmentMatch)
                return false;
            return stem.EndsWith("." + targetName, StringComparison.OrdinalIgnoreCase) ||
                   stem.StartsWith(targetName + ".", StringComparison.OrdinalIgnoreCase) ||
                   stem.IndexOf("." + targetName + ".", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        internal static bool MatchesPackageFeatureName(string pathOrFileName, string featurePackageName)
        {
            if (string.IsNullOrWhiteSpace(featurePackageName))
                return false;
            if (!TryParsePackageFile(pathOrFileName, out _, out string stem, out PackageKind kind))
                return false;

            string target = featurePackageName.Trim();
            if (target.EndsWith(".cab", StringComparison.OrdinalIgnoreCase))
                target = target.Substring(0, target.Length - 4);

            if (target.EndsWith(".cbsr", StringComparison.OrdinalIgnoreCase))
            {
                if (kind != PackageKind.CbsRemoval)
                    return false;
                return StemMatchesTarget(stem, target.Substring(0, target.Length - 5), true);
            }
            if (target.EndsWith(".spkr", StringComparison.OrdinalIgnoreCase))
            {
                if (kind != PackageKind.SpkgRemoval)
                    return false;
                return StemMatchesTarget(stem, target.Substring(0, target.Length - 5), true);
            }
            if (target.EndsWith(".cbsu", StringComparison.OrdinalIgnoreCase))
            {
                if (kind != PackageKind.CbsUpdate)
                    return false;
                return StemMatchesTarget(stem, target.Substring(0, target.Length - 5), true);
            }
            if (target.EndsWith(".cbs", StringComparison.OrdinalIgnoreCase))
            {
                if (kind != PackageKind.CbsCanonical)
                    return false;
                return StemMatchesTarget(stem, target.Substring(0, target.Length - 4), true);
            }
            if (target.EndsWith(".spkg", StringComparison.OrdinalIgnoreCase))
            {
                if (kind != PackageKind.SpkgCanonical)
                    return false;
                return StemMatchesTarget(stem, target.Substring(0, target.Length - 5), true);
            }

            return StemMatchesTarget(stem, target, true);
        }

        private static bool FilteredListHasStem(string stem)
        {
            foreach (var item in filteredPackages)
            {
                if (TryParsePackageFile(item, out _, out string existingStem, out _))
                {
                    if (existingStem.Equals(stem, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            return false;
        }

        private static string FindBestPackageMatch(IEnumerable<string> candidates, string targetName, bool allowSuffixMatch, bool? requireCbsMode = null)
        {
            string bestEntry = null;
            int bestPriority = int.MaxValue;

            foreach (var candidate in candidates)
            {
                if (!TryParsePackageFile(candidate, out _, out string stem, out PackageKind kind))
                    continue;
                if (requireCbsMode.HasValue)
                {
                    if (requireCbsMode.Value && !IsCbsFamilyKind(kind))
                        continue;
                    if (!requireCbsMode.Value && !IsSpkgFamilyKind(kind))
                        continue;
                }
                if (!StemMatchesTarget(stem, targetName, allowSuffixMatch))
                    continue;

                int priority = GetPackageKindPriority(kind);
                if (priority < bestPriority)
                {
                    bestPriority = priority;
                    bestEntry = candidate;
                    if (bestPriority == 0)
                        break;
                }
            }

            return bestEntry;
        }

        public static bool OnlineUpdate(string updateBuild, bool includeAllPackages = false)
        {
            filteredPackages.Clear();
            isFeatureInstalled = false;

            var manifestContent = Program.GetResourceFile($"{updateBuild}.txt");
            if (string.IsNullOrWhiteSpace(manifestContent))
            {
                Program.WriteLine($"Manifest for build '{updateBuild}' is empty or missing!", ConsoleColor.Red);
                return false;
            }

            var targetListFile = manifestContent
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                .Where(l => l.Length > 0)
                .ToArray();

            if (includeAllPackages)
            {
                var bestByStem = new Dictionary<string, Tuple<int, string>>(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in targetListFile)
                {
                    if (!TryParsePackageFile(entry, out _, out string stem, out PackageKind kind))
                        continue;
                    int priority = GetPackageKindPriority(kind);
                    if (!bestByStem.TryGetValue(stem, out var existing) || priority < existing.Item1)
                    {
                        bestByStem[stem] = Tuple.Create(priority, entry);
                    }
                }

                foreach (var item in bestByStem.Values)
                {
                    var url = item.Item2;
                    if (url.IndexOf("ms_projecta.mainos", StringComparison.OrdinalIgnoreCase) >= 0 || url.IndexOf("MS_RCS_FEATURE_PACK.MainOS.cbsr", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        isFeatureInstalled = true;
                    }
                    if (!shouldSkip(url, true))
                    {
                        filteredPackages.Add(url);
                    }
                }
                return isFeatureInstalled;
            }

            Program.appendLog("\n[INSTALLED PACKAGES]\n");
            for (int i = 0; i < installedPackages.Length; i++)
            {
                string pkgName = ExtractPackageNameFromCsvOrLine(installedPackages[i]);
                if (!string.IsNullOrEmpty(pkgName))
                {
                    var requiredPackage = FindBestPackageMatch(targetListFile, pkgName, false);
                    if (requiredPackage != null)
                    {
                        if (requiredPackage.IndexOf("ms_projecta.mainos", StringComparison.OrdinalIgnoreCase) >= 0 || requiredPackage.IndexOf("MS_RCS_FEATURE_PACK.MainOS.cbsr", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            isFeatureInstalled = true;
                        }
                        if (!shouldSkip(requiredPackage, true))
                        {
                            filteredPackages.Add(requiredPackage);
                        }
                    }
                }
                Program.appendLog($"{installedPackages[i]}\n");
            }
            for (int i = 0; i < knownPackages.Length; i++)
            {
                if (!FilteredListHasStem(knownPackages[i]) &&
                    !filteredPackages.Any(p => TryParsePackageFile(p, out _, out string s, out _) && StemMatchesTarget(s, knownPackages[i], true)))
                {
                    var knownPackage = FindBestPackageMatch(targetListFile, knownPackages[i], true);
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

        internal static bool IncludeFeatureFromManifest(string updateBuild, string targetName)
        {
            var manifestContent = Program.GetResourceFile($"{updateBuild}.txt");
            if (string.IsNullOrWhiteSpace(manifestContent))
                return false;

            var targetListFile = manifestContent
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                .Where(l => l.Length > 0)
                .ToArray();

            var match = FindBestPackageMatch(targetListFile, targetName, true);
            if (match != null && !shouldSkip(match, true))
            {
                filteredPackages.Add(match);
                return true;
            }
            return false;
        }

        public static async Task<bool> OfflineUpdate(string[] folderFiles)
        {
        filterPackages:
            int totalRemovalPackages = 0;
            Program.LoadUpdateRules(Program.packagePath);
            filteredPackages.Clear();
            var folderHasExplicitCBS = false;
            var folderHasPlainCab = false;
            var folderHasSPKG = false;
            foreach (var file in folderFiles)
            {
                if (!TryParsePackageFile(file, out _, out _, out PackageKind kind))
                    continue;
                if (kind == PackageKind.CbsCanonical || kind == PackageKind.CbsUpdate || kind == PackageKind.CbsRemoval)
                    folderHasExplicitCBS = true;
                else if (kind == PackageKind.PlainCab)
                    folderHasPlainCab = true;
                else if (IsSpkgFamilyKind(kind))
                    folderHasSPKG = true;
            }
            var folderHasCBS = folderHasExplicitCBS || folderHasPlainCab;

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
            // For FFU only we skip checking installedPackages length,
            // because we mount FFU later which is intentionally made after all questions
            var showFilterOptions = (installedPackages.Length > 0 || Program.pushToFFU);
            if (!Program.updateRules.ForceFilterMode && !Program.updateRules.ForcePushAllMode && showFilterOptions)
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
            else if (Program.iutoolMode)
            {
                Program.Write("\nPUSH MODE: ", ConsoleColor.Gray);
                Program.WriteLine("IUTOOL", ConsoleColor.DarkGray);
            }

            if ((Program.updateRules.ForceFilterMode || packagesFilterAction.KeyChar == '1') && installedPackages.Length > 0)
            {
                Program.WriteLine($"\nFiltering packages (type: {previewType}), please wait...", ConsoleColor.DarkGray);
                Program.appendLog("\n[INSTALLED PACKAGES]\n");
                for (int i = 0; i < installedPackages.Length; i++)
                {
                    string pkgName = ExtractPackageNameFromCsvOrLine(installedPackages[i]);
                    if (!string.IsNullOrEmpty(pkgName))
                    {
                        var requiredPackage = FindBestPackageMatch(folderFiles, pkgName, false, filterCBSPackagesOnly);
                        if (requiredPackage != null && !shouldSkip(requiredPackage))
                        {
                            filteredPackages.Add(requiredPackage);
                        }
                    }
                    Program.appendLog($"{installedPackages[i]}\n");
                }

                for (int i = 0; i < knownPackages.Length; i++)
                {
                    if (!filteredPackages.Any(p => TryParsePackageFile(p, out _, out string s, out _) && StemMatchesTarget(s, knownPackages[i], true)))
                    {
                        var knownPackage = FindBestPackageMatch(folderFiles, knownPackages[i], true, filterCBSPackagesOnly);
                        if (knownPackage != null && !shouldSkip(knownPackage))
                        {
                            filteredPackages.Add(knownPackage);
                        }
                    }
                }
            }
            else
            {
                Program.WriteLine($"\nAdding packages (type: {previewType}), please wait...", ConsoleColor.DarkGray);
                var bestByStem = new Dictionary<string, Tuple<int, string>>(StringComparer.OrdinalIgnoreCase);
                foreach (var candidate in folderFiles)
                {
                    if (!TryParsePackageFile(candidate, out _, out string stem, out PackageKind kind))
                        continue;
                    if (filterCBSPackagesOnly && !IsCbsFamilyKind(kind))
                        continue;
                    if (!filterCBSPackagesOnly && !IsSpkgFamilyKind(kind))
                        continue;

                    int priority = GetPackageKindPriority(kind);
                    if (!bestByStem.TryGetValue(stem, out var existing) || priority < existing.Item1)
                    {
                        bestByStem[stem] = Tuple.Create(priority, candidate);
                    }
                }

                foreach (var entry in bestByStem.Values)
                {
                    if (!shouldSkip(entry.Item2))
                    {
                        filteredPackages.Add(entry.Item2);
                    }
                }
            }

            // Features packages
            foreach (var feature in Program.GlobalFeaturesList)
            {
                if (feature.ShouldPresent)
                {
                    if (feature.State)
                    {
                        for (int i = 0; i < feature.FeaturePackages.Length; i++)
                        {
                            var matchingFeatureFiles = folderFiles
                                .Where(j => MatchesPackageFeatureName(j, feature.FeaturePackages[i].PackageName))
                                .ToList();

                            var requiredPackage = FindBestPackageMatch(
                                matchingFeatureFiles,
                                feature.FeaturePackages[i].PackageName.EndsWith(".cab", StringComparison.OrdinalIgnoreCase)
                                    ? feature.FeaturePackages[i].PackageName.Substring(0, feature.FeaturePackages[i].PackageName.Length - 4)
                                    : feature.FeaturePackages[i].PackageName,
                                true);

                            if (requiredPackage == null && matchingFeatureFiles.Count > 0)
                            {
                                requiredPackage = matchingFeatureFiles[0];
                            }

                            if (requiredPackage != null)
                            {
                                if (feature.FeaturePackages[i].ReplaceStock)
                                {
                                    // Ensure to remove stock one from the list
                                    var stockName = feature.FeaturePackages[i].TargetStockName;
                                    bool removedAny = false;
                                    for (int idx = filteredPackages.Count - 1; idx >= 0; idx--)
                                    {
                                        if (TryParsePackageFile(filteredPackages[idx], out _, out string stem, out _) &&
                                            StemMatchesTarget(stem, stockName, false))
                                        {
                                            filteredPackages.RemoveAt(idx);
                                            removedAny = true;
                                        }
                                    }
                                    if (removedAny)
                                    {
                                        Program.WriteLine($"Stock ({stockName}) replaced with feature", ConsoleColor.DarkYellow);
                                        Program.appendLog($"Package ({stockName}) replaced with feature ({feature.FeaturePackages[i].PackageName})\n");
                                    }
                                    else
                                    {
                                        Program.WriteLine($"Stock ({stockName}) is not presented in filtered list!", ConsoleColor.DarkYellow);
                                    }
                                }

                                if (!shouldSkip(requiredPackage))
                                {
                                    filteredPackages.Add(requiredPackage);
                                    Program.appendLog($"Feature Package ({requiredPackage}) added to filtered list\n");
                                }
                            }
                        }
                    }
                    else
                    {
                        // Ensure package removed from the list
                        for (int i = 0; i < feature.FeaturePackages.Length; i++)
                        {
                            filteredPackages.RemoveAll(p => MatchesPackageFeatureName(p, feature.FeaturePackages[i].PackageName));
                        }
                    }
                }
            }

            if (filteredPackages.Count > 0)
            {
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
                    if (startData != null && startData.HasValue && expireData != null && expireData.HasValue)
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
                catch (Exception)
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
                catch (Exception)
                {
                }

                if (formatedDate != null && formatedDate.HasValue)
                {
                    var dateOnly = formatedDate.Value.Date;
                    Program.Write("Files  : " + dateOnly.ToString("d"), ConsoleColor.DarkGray);
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

                try
                {
                    // Count removal packages 
                    var removals = filteredPackages.Where(k =>
                        TryParsePackageFile(k, out _, out _, out PackageKind kind) && IsRemovalKind(kind)).ToList();
                    totalRemovalPackages = removals.Count;
                    if (totalRemovalPackages > 0)
                    {
                        Program.appendLog(filterCBSPackagesOnly ? "\n[REMOVALS CBS]\n" : "\n[REMOVALS SPKG]\n");
                        foreach (var removalItem in removals)
                        {
                            Program.appendLog($"{removalItem}\n");
                        }
                    }
                }
                catch (Exception exr)
                {
                    Program.appendLog($"{exr.Message}\n");
                }

                Program.Write("\nTotal expected packages: ", ConsoleColor.DarkGray);
                Program.WriteLine(filteredPackages.Count.ToString(), ConsoleColor.DarkYellow);
                if (totalRemovalPackages > 0)
                {
                    Program.Write("Removal packages: ", ConsoleColor.DarkGray);
                    Program.WriteLine(totalRemovalPackages.ToString(), ConsoleColor.Red);
                }
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
                    var destDir = Path.Combine(Program.filteredDirectory, GetDeviceInfo.SerialNumber[0], "Packages");
                    Directory.CreateDirectory(destDir);
                    var validPackages = filteredPackages.Where(j => !string.IsNullOrWhiteSpace(j)).ToList();
                    for (int i = 0; i < validPackages.Count; i++)
                    {
                        var srcPath = validPackages[i];
                        var fileName = Path.GetFileName(srcPath);
                        var dstPath = Path.Combine(destDir, fileName);
                        Program.resetCursorPosition();
                        Program.WriteLine($@"[{i + 1}/{validPackages.Count}] {fileName}", ConsoleColor.DarkGray, true);
                        if (!string.Equals(Path.GetFullPath(srcPath), Path.GetFullPath(dstPath), StringComparison.OrdinalIgnoreCase))
                        {
                            File.Copy(srcPath, dstPath, true);
                        }
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

        private static bool HasMscfSignature(string filePath)
        {
            try
            {
                var info = new FileInfo(filePath);
                if (!info.Exists || info.Length < 4)
                    return false;
                using (var fs = File.OpenRead(filePath))
                {
                    byte[] header = new byte[4];
                    if (fs.Read(header, 0, 4) != 4)
                        return false;
                    return header[0] == (byte)'M' && header[1] == (byte)'S' && header[2] == (byte)'C' && header[3] == (byte)'F';
                }
            }
            catch
            {
                return false;
            }
        }

        private static bool IsValidCachedPackage(string path, string fileName, long expectedSize = -1)
        {
            if (!File.Exists(path))
                return false;
            var info = new FileInfo(path);
            if (info.Length <= 0)
                return false;
            if (expectedSize > 0 && info.Length != expectedSize)
                return false;
            if (fileName.EndsWith(".cab", StringComparison.OrdinalIgnoreCase) && !HasMscfSignature(path))
                return false;
            return true;
        }

        private static long ParseWgetContentLength(string stderrText)
        {
            if (string.IsNullOrEmpty(stderrText))
                return -1;
            foreach (var rawLine in stderrText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (rawLine.IndexOf("Length:", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    var match = Regex.Match(rawLine, @"Length:\s*(\d+)", RegexOptions.IgnoreCase);
                    if (match.Success && long.TryParse(match.Groups[1].Value, out long parsed))
                    {
                        return parsed;
                    }
                }
            }
            return -1;
        }

        public static async Task<bool> DownloadUpdate(string update, string customDestinationFolder = null)
        {
            var validPackages = filteredPackages
                .Where(j => !string.IsNullOrWhiteSpace(j))
                .Select(j => j.Trim())
                .ToList();

            if (validPackages.Count == 0)
            {
                Program.WriteLine("\nNo packages matched for download.", ConsoleColor.Red);
                return false;
            }

            string destinationDir = customDestinationFolder;
            if (string.IsNullOrWhiteSpace(destinationDir))
            {
                string serialKey = (GetDeviceInfo.SerialNumber != null && GetDeviceInfo.SerialNumber.Length > 0 && !string.IsNullOrWhiteSpace(GetDeviceInfo.SerialNumber[0]))
                    ? GetDeviceInfo.SerialNumber[0]
                    : "Downloads";
                destinationDir = Path.Combine(Program.filteredDirectory, serialKey, "Packages");
            }
            string cacheDir = Path.Combine(Environment.CurrentDirectory, "Cache", update);
            Directory.CreateDirectory(destinationDir);
            Directory.CreateDirectory(cacheDir);

            const int maxRetries = 3;
            int cacheHits = 0;
            int downloadedCount = 0;
            var failedPackages = new List<string>();

            Program.WriteLine(update == "15254.603" ? "\n[OUTPUT] (wget.exe)\nPlease wait..." : "\n[OUTPUT] (Downloading)\nPlease wait...", ConsoleColor.DarkYellow);

            using (WebClient client = new WebClient())
            {
                client.DownloadProgressChanged += DownloadProgressChanged;

                for (int i = 0; i < validPackages.Count; i++)
                {
                    downloadFile = new Uri(validPackages[i]);
                    string fileName = GetPackageFileName(validPackages[i]);
                    _currentDownloadName = fileName;
                    _currentTotalBytes = -1;

                    string cacheFile = Path.Combine(cacheDir, fileName);
                    string partFile = cacheFile + ".part";
                    string destFile = Path.Combine(destinationDir, fileName);

                    Program.resetCursorPosition();
                    Program.WriteLine($@"[{i + 1}/{validPackages.Count}] {fileName}", ConsoleColor.DarkGray, true);

                    if (IsValidCachedPackage(cacheFile, fileName))
                    {
                        cacheHits++;
                        if (!string.Equals(Path.GetFullPath(cacheFile), Path.GetFullPath(destFile), StringComparison.OrdinalIgnoreCase))
                        {
                            File.Copy(cacheFile, destFile, true);
                        }
                        continue;
                    }

                    bool success = false;
                    string lastError = string.Empty;

                    for (int attempt = 1; attempt <= maxRetries; attempt++)
                    {
                        try
                        {
                            if (File.Exists(partFile))
                                File.Delete(partFile);
                        }
                        catch { }

                        if (update == "15254.603")
                        {
                            try
                            {
                                string wgetUrl = attempt == 1 ? validPackages[i] : (validPackages[i].Contains("?") ? validPackages[i] : validPackages[i] + "?dl=1");
                                long expectedSize = -1;

                                using (var spiderProc = new Process())
                                {
                                    spiderProc.StartInfo.FileName = Path.Combine(Program.toolsDirectory, "wget.exe");
                                    spiderProc.StartInfo.Arguments = $"--no-hsts --no-check-certificate --spider \"{wgetUrl}\"";
                                    spiderProc.StartInfo.UseShellExecute = false;
                                    spiderProc.StartInfo.CreateNoWindow = true;
                                    spiderProc.StartInfo.RedirectStandardOutput = true;
                                    spiderProc.StartInfo.RedirectStandardError = true;
                                    spiderProc.Start();
                                    var outTask = spiderProc.StandardOutput.ReadToEndAsync();
                                    var errTask = spiderProc.StandardError.ReadToEndAsync();
                                    await Task.Run(() => spiderProc.WaitForExit(30000));
                                    if (!spiderProc.HasExited)
                                    {
                                        try { spiderProc.Kill(); } catch { }
                                    }
                                    try { await outTask; } catch { }
                                    string spiderErr = string.Empty;
                                    try { spiderErr = await errTask; } catch { }
                                    expectedSize = ParseWgetContentLength(spiderErr);
                                }

                                using (var dlProc = new Process())
                                {
                                    dlProc.StartInfo.FileName = Path.Combine(Program.toolsDirectory, "wget.exe");
                                    dlProc.StartInfo.Arguments = $"-q --no-hsts -O \"{partFile}\" \"{wgetUrl}\" --no-check-certificate --show-progress";
                                    dlProc.StartInfo.UseShellExecute = false;
                                    dlProc.StartInfo.CreateNoWindow = false;
                                    dlProc.Start();
                                    await Task.Run(() => dlProc.WaitForExit());
                                    Program.showTitleWaitMessage(false);
                                    if (dlProc.ExitCode != 0)
                                    {
                                        lastError = $"wget exited with code {dlProc.ExitCode}";
                                    }
                                    else if (IsValidCachedPackage(partFile, fileName, expectedSize))
                                    {
                                        success = true;
                                    }
                                    else
                                    {
                                        lastError = "Downloaded file failed size or CAB header verification";
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                lastError = ex.Message;
                            }
                        }
                        else
                        {
                            try
                            {
                                Volatile.Write(ref _currentTotalBytes, -1);
                                await client.DownloadFileTaskAsync(downloadFile, partFile);
                                Program.showTitleWaitMessage(false);

                                long expectedSize = Volatile.Read(ref _currentTotalBytes);
                                if (expectedSize <= 0 && client.ResponseHeaders != null &&
                                    long.TryParse(client.ResponseHeaders["Content-Length"], out long headerLen) && headerLen > 0)
                                {
                                    expectedSize = headerLen;
                                }

                                if (IsValidCachedPackage(partFile, fileName, expectedSize))
                                {
                                    success = true;
                                }
                                else
                                {
                                    lastError = "Downloaded file failed size or CAB header verification";
                                }
                            }
                            catch (Exception ex)
                            {
                                Program.showTitleWaitMessage(false);
                                lastError = ex.Message;
                            }
                        }

                        if (success)
                        {
                            try
                            {
                                if (File.Exists(cacheFile))
                                    File.Delete(cacheFile);
                                File.Move(partFile, cacheFile);
                                if (!string.Equals(Path.GetFullPath(cacheFile), Path.GetFullPath(destFile), StringComparison.OrdinalIgnoreCase))
                                {
                                    File.Copy(cacheFile, destFile, true);
                                }
                                downloadedCount++;
                                break;
                            }
                            catch (Exception ex)
                            {
                                success = false;
                                lastError = ex.Message;
                            }
                        }

                        Program.appendLog($"[DOWNLOAD RETRY {attempt}/{maxRetries}] {fileName}: {lastError}\n");
                        if (attempt < maxRetries)
                        {
                            await Task.Delay(1000 * attempt);
                        }
                    }

                    if (!success)
                    {
                        try
                        {
                            if (File.Exists(partFile))
                                File.Delete(partFile);
                        }
                        catch { }
                        failedPackages.Add($"{fileName} ({lastError})");
                        Program.WriteLine($"Failed: {fileName} - {lastError}", ConsoleColor.Red);
                    }
                }
            }

            Program.showTitleWaitMessage(false);
            Program.appendLog($"[DOWNLOAD SUMMARY] Total={validPackages.Count}, Cached={cacheHits}, Downloaded={downloadedCount}, Failed={failedPackages.Count}\n");

            if (failedPackages.Count > 0)
            {
                Program.WriteLine($"\nDownload finished with {failedPackages.Count} failed package(s). Check the log for details.", ConsoleColor.Red);
                return false;
            }

            Program.WriteLine($"\nPackages ready: {validPackages.Count} (cached: {cacheHits}, downloaded: {downloadedCount})", ConsoleColor.Green);
            return true;
        }

        private static void DownloadProgressChanged(object sender, DownloadProgressChangedEventArgs downloadProgressChangedEventArgs)
        {
            if (downloadProgressChangedEventArgs.TotalBytesToReceive > 0)
            {
                Volatile.Write(ref _currentTotalBytes, downloadProgressChangedEventArgs.TotalBytesToReceive);
            }
            DownloadingProgress = new Tuple<DateTime, long, long>(DateTime.Now, downloadProgressChangedEventArgs.TotalBytesToReceive, downloadProgressChangedEventArgs.BytesReceived);
            string name = _currentDownloadName;
            if (string.IsNullOrEmpty(name) && downloadFile != null)
            {
                name = GetPackageFileName(downloadFile.AbsoluteUri);
            }
            string displayName = name.Length > 28 ? name.Substring(0, 28) + "..." : name;
            long total = DownloadingProgress.Item2;
            long received = DownloadingProgress.Item3;
            string progressText = total > 0
                ? $"{(received * 100) / total}% - {received}/{total}"
                : $"{received} bytes";
            try
            {
                Console.Title = $"Universal Updater.exe  [Downloading: {displayName} {progressText}]";
            }
            catch { }
        }
        public static bool shouldSkip(string package, bool onlinePackage = false)
        {
            foreach (var testCheck in skippedPackages)
            {
                if (package.IndexOf(testCheck, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    // Package exists in the skip list
                    Program.appendLog($"Package ({package}) skipped!, member of skipped list\n");
                    return true;
                }
            }

            if (!onlinePackage)
            {
                if (File.Exists(package))
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
                else
                {
                    Program.appendLog($"Package file ({package}) is no longer exits\n");
                    return true;
                }
            }

            if (filteredPackages.Any(p => string.Equals(p, package, StringComparison.OrdinalIgnoreCase)))
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