﻿/**************************************************************************
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
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

public class CabExtractor : IDisposable
{
    private static class NativeMethods
    {
        [StructLayout(LayoutKind.Sequential)]
        internal class CabError //Cabinet API: "ERF"
        {
            public int erfOper;
            public int erfType;
            public int fError;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal class FdiNotification //Cabinet API: "FDINOTIFICATION"
        {
            internal int cb;
            //not sure if this should be a IntPtr or a strong
            internal IntPtr psz1;
            internal IntPtr psz2;
            internal IntPtr psz3;
            internal IntPtr pv;
            internal IntPtr hf;
            internal short date;
            internal short time;
            internal short attribs;
            internal short setID;
            internal short iCabinet;
            internal short iFolder;
            internal int fdie;

        }

        internal enum FdiNotificationType
        {
            CabinetInfo,
            PartialFile,
            CopyFile,
            CloseFileInfo,
            NextCabinet,
            Enumerate
        }


        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate IntPtr FdiMemAllocDelegate(int numBytes);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate void FdiMemFreeDelegate(IntPtr mem);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate IntPtr FdiFileOpenDelegate(string fileName, int oflag, int pmode);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate Int32 FdiFileReadDelegate(IntPtr hf,
                                                   [In, Out] [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 2,
                                                       ArraySubType = UnmanagedType.U1)] byte[] buffer, int cb);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate Int32 FdiFileWriteDelegate(IntPtr hf,
                                                    [In] [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 2,
                                                        ArraySubType = UnmanagedType.U1)] byte[] buffer, int cb);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate Int32 FdiFileCloseDelegate(IntPtr hf);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate Int32 FdiFileSeekDelegate(IntPtr hf, int dist, int seektype);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate IntPtr FdiNotifyDelegate(
            FdiNotificationType fdint, [In] [MarshalAs(UnmanagedType.LPStruct)] FdiNotification fdin);

        [DllImport("cabinet.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "FDICreate", CharSet = CharSet.Ansi)]
        internal static extern IntPtr FdiCreate(
            FdiMemAllocDelegate fnMemAlloc,
            FdiMemFreeDelegate fnMemFree,
            FdiFileOpenDelegate fnFileOpen,
            FdiFileReadDelegate fnFileRead,
            FdiFileWriteDelegate fnFileWrite,
            FdiFileCloseDelegate fnFileClose,
            FdiFileSeekDelegate fnFileSeek,
            int cpuType,
            [MarshalAs(UnmanagedType.LPStruct)] CabError erf);

        [DllImport("cabinet.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "FDIDestroy", CharSet = CharSet.Ansi)]
        internal static extern bool FdiDestroy(IntPtr hfdi);

        [DllImport("cabinet.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "FDICopy", CharSet = CharSet.Ansi)]
        internal static extern bool FdiCopy(
            IntPtr hfdi,
            string cabinetName,
            string cabinetPath,
            int flags,
            FdiNotifyDelegate fnNotify,
            IntPtr fnDecrypt,
            IntPtr userData);
    }

    internal class ArchiveFile
    {
        public IntPtr Handle { get; set; }
        public string Name { get; set; }
        public bool Found { get; set; }
        public int Length { get; set; }
        public byte[] Data { get; set; }
    }

    #region fields and properties

    /// Very important!
    /// Do not try to call directly to this methods, instead use the delegates. if you use them directly it may cause application crashes, corruption and data loss.
    /// Using fields to save the delegate so that the delegate won't be garbage collected  !
    /// When passing delegates to unmanaged code, they must be kept alive by the managed application until it is guaranteed that they will never be called.
    private static NativeMethods.FdiMemAllocDelegate _fdiAllocMemHandler;
    private static NativeMethods.FdiMemFreeDelegate _fdiFreeMemHandler;
    private static NativeMethods.FdiFileOpenDelegate _fdiOpenStreamHandler;
    private static NativeMethods.FdiFileReadDelegate _fdiReadStreamHandler;
    private static NativeMethods.FdiFileWriteDelegate _fdiWriteStreamHandler;
    private static NativeMethods.FdiFileCloseDelegate _fdiCloseStreamHandler;
    private static NativeMethods.FdiFileSeekDelegate _fdiSeekStreamHandler;

    private static ArchiveFile _currentFileToDecompress;
    readonly List<string> _fileNames = new List<string>();
    private static NativeMethods.CabError _erf;
    private const int CpuTypeUnknown = -1;
    private static byte[] _inputData;
    private bool _disposed;
    /// <summary>
    /// 
    /// </summary>
    private readonly List<string> _subDirectoryToIgnore = new List<string>();
    /// <summary>
    /// Path to the folder where the files will be extracted to
    /// </summary>
    private readonly string _extractionFolderPath;
    /// <summary>
    /// The name of the folder where the files will be extracted to
    /// </summary>
    public const string ExtractedFolderName = "ExtractedFiles";

    public const string CabFileName = "setup.cab";

    #endregion

    public CabExtractor(string cabFilePath, IEnumerable<string> subDirectoryToUnpack)
        : this(cabFilePath)
    {
        if (subDirectoryToUnpack != null)
            _subDirectoryToIgnore.AddRange(subDirectoryToUnpack);
    }
    public CabExtractor(string cabFilePath)
    {
        var cabBytes =
           File.ReadAllBytes(cabFilePath);
        _inputData = cabBytes;
        var cabFileLocation = Path.GetDirectoryName(cabFilePath) ?? "";
        _extractionFolderPath = Path.Combine(cabFileLocation, ExtractedFolderName);
        _erf = new NativeMethods.CabError();
        FdiContext = IntPtr.Zero;

        _fdiAllocMemHandler = MemAlloc;
        _fdiFreeMemHandler = MemFree;
        _fdiOpenStreamHandler = InputFileOpen;
        _fdiReadStreamHandler = FileRead;
        _fdiWriteStreamHandler = FileWrite;
        _fdiCloseStreamHandler = InputFileClose;
        _fdiSeekStreamHandler = FileSeek;

        FdiContext = FdiCreate(_fdiAllocMemHandler, _fdiFreeMemHandler, _fdiOpenStreamHandler, _fdiReadStreamHandler, _fdiWriteStreamHandler, _fdiCloseStreamHandler, _fdiSeekStreamHandler, _erf);


    }

    public bool ExtractCabFiles()
    {
        if (!FdiIterate())
        {
            throw new Exception("Failed to iterate cab files");
        }

        foreach (var file in _fileNames)
        {
            //ExtractFile(file);
        }
        return true;
    }

    public static void ExtractFile(string filePath, string fileName, string savePath)
    {
        var cabBytes = File.ReadAllBytes(filePath);
        _inputData = cabBytes;
        _erf = new NativeMethods.CabError();
        FdiContext = IntPtr.Zero;

        _fdiAllocMemHandler = MemAlloc;
        _fdiFreeMemHandler = MemFree;
        _fdiOpenStreamHandler = InputFileOpen;
        _fdiReadStreamHandler = FileRead;
        _fdiWriteStreamHandler = FileWrite;
        _fdiCloseStreamHandler = InputFileClose;
        _fdiSeekStreamHandler = FileSeek;

        FdiContext = FdiCreate(_fdiAllocMemHandler, _fdiFreeMemHandler, _fdiOpenStreamHandler, _fdiReadStreamHandler, _fdiWriteStreamHandler, _fdiCloseStreamHandler, _fdiSeekStreamHandler, _erf);
        _currentFileToDecompress = new ArchiveFile { Name = fileName };
        FdiCopy();
        if (_currentFileToDecompress.Data != null)
        {
            File.WriteAllBytes(savePath, _currentFileToDecompress.Data);
        }

    }

    private void CreateAllRelevantDirectories(string filePath)
    {
        try
        {
            if (!Directory.Exists(_extractionFolderPath))
            {
                Directory.CreateDirectory(_extractionFolderPath);
            }
            var fullPathToFile = Path.GetDirectoryName(filePath);
            if (fullPathToFile != null &&
                !Directory.Exists(Path.Combine(_extractionFolderPath, fullPathToFile)))
            {
                Directory.CreateDirectory(Path.Combine(_extractionFolderPath, fullPathToFile));
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
            Console.WriteLine(string.Format("Failed to create directories for the file {0}", filePath));
        }

    }



    private static string GetFileName(NativeMethods.FdiNotification notification)
    {
        var encoding = ((int)notification.attribs & 128) != 0 ? Encoding.UTF8 : Encoding.Default;
        int length = 0;
        while (Marshal.ReadByte(notification.psz1, length) != 0)
            checked { ++length; }
        var numArray = new byte[length];
        Marshal.Copy(notification.psz1, numArray, 0, length);
        string path = encoding.GetString(numArray);
        if (Path.IsPathRooted(path))
            path = path.Replace(String.Concat(Path.VolumeSeparatorChar), "");
        return path;
    }
    private static IntPtr ExtractCallback(NativeMethods.FdiNotificationType fdint, NativeMethods.FdiNotification fdin)
    {
        switch (fdint)
        {
            case NativeMethods.FdiNotificationType.CopyFile:
                return CopyFiles(fdin);
            case NativeMethods.FdiNotificationType.CloseFileInfo:
                return OutputFileClose(fdin);
            default:
                return IntPtr.Zero;
        }
    }

    private IntPtr IterateCallback(NativeMethods.FdiNotificationType fdint, NativeMethods.FdiNotification fdin)
    {
        switch (fdint)
        {
            case NativeMethods.FdiNotificationType.CopyFile:
                return OutputFileOpen(fdin);
            default:
                return IntPtr.Zero;
        }
    }

    private static IntPtr InputFileOpen(string fileName, int oflag, int pmode)
    {
        var stream = new MemoryStream(_inputData);
        GCHandle gch = GCHandle.Alloc(stream);
        return (IntPtr)gch;
    }

    private static int InputFileClose(IntPtr hf)
    {
        var stream = StreamFromHandle(hf);
        stream.Close();
        ((GCHandle)(hf)).Free();
        return 0;
    }
    /// <summary>
    /// Copies the contents of input to output. Doesn't close either stream.
    /// </summary>
    public static void CopyStream(Stream input, Stream output)
    {
        var buffer = new byte[8 * 1024];
        int len;
        while ((len = input.Read(buffer, 0, buffer.Length)) > 0)
        {
            output.Write(buffer, 0, len);
        }
    }


    private static IntPtr CopyFiles(NativeMethods.FdiNotification fdin)
    {
        var fileName = GetFileName(fdin);
        var extractFile = _currentFileToDecompress.Name == fileName ? _currentFileToDecompress : null;
        if (extractFile != null)
        {
            var stream = new MemoryStream();
            GCHandle gch = GCHandle.Alloc(stream);
            extractFile.Handle = (IntPtr)gch;
            return extractFile.Handle;
        }

        //Do not extract this file
        return IntPtr.Zero;
    }
    private IntPtr OutputFileOpen(NativeMethods.FdiNotification fdin)
    {
        try
        {
            var extractFile = new ArchiveFile { Name = GetFileName(fdin) };
            if (ShouldIgnoreFile(extractFile))
            {
                //ignore this file.
                return IntPtr.Zero;
            }
            var stream = new MemoryStream();
            GCHandle gch = GCHandle.Alloc(stream);
            extractFile.Handle = (IntPtr)gch;

            AddToListOfFiles(extractFile);


        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
        }
        //return IntPtr.Zero so that the iteration will keep on going
        return IntPtr.Zero;

    }

    private bool ShouldIgnoreFile(ArchiveFile extractFile)
    {
        var rootFolder = GetFileRootFolder(extractFile.Name);
        return _subDirectoryToIgnore.Any(dir => dir.Equals(rootFolder, StringComparison.InvariantCultureIgnoreCase));
    }

    private string GetFileRootFolder(string path)
    {
        try
        {
            return path.Split(new[] { Path.DirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        }
        catch (Exception)
        {

            return string.Empty;
        }

    }

    private void AddToListOfFiles(ArchiveFile extractFile)
    {
        if (!_fileNames.Any(file => file.Equals(extractFile.Name)))
        {
            _fileNames.Add(extractFile.Name);
        }
    }

    private static IntPtr OutputFileClose(NativeMethods.FdiNotification fdin)
    {
        var extractFile = _currentFileToDecompress.Handle == fdin.hf ? _currentFileToDecompress : null;
        var stream = StreamFromHandle(fdin.hf);

        if (extractFile != null)
        {
            extractFile.Found = true;
            extractFile.Length = (int)stream.Length;

            if (stream.Length > 0)
            {
                extractFile.Data = new byte[stream.Length];
                stream.Position = 0;
                stream.Read(extractFile.Data, 0, (int)stream.Length);
            }
        }

        stream.Close();
        return IntPtr.Zero;
    }

    private static IntPtr FdiCreate(
    NativeMethods.FdiMemAllocDelegate fnMemAlloc,
    NativeMethods.FdiMemFreeDelegate fnMemFree,
    NativeMethods.FdiFileOpenDelegate fnFileOpen,
    NativeMethods.FdiFileReadDelegate fnFileRead,
    NativeMethods.FdiFileWriteDelegate fnFileWrite,
    NativeMethods.FdiFileCloseDelegate fnFileClose,
    NativeMethods.FdiFileSeekDelegate fnFileSeek,
    NativeMethods.CabError erf)
    {
        return NativeMethods.FdiCreate(fnMemAlloc, fnMemFree, fnFileOpen, fnFileRead, fnFileWrite,
                         fnFileClose, fnFileSeek, CpuTypeUnknown, erf);
    }

    private static int FileRead(IntPtr hf, byte[] buffer, int cb)
    {
        var stream = StreamFromHandle(hf);
        return stream.Read(buffer, 0, cb);
    }

    private static int FileWrite(IntPtr hf, byte[] buffer, int cb)
    {
        var stream = StreamFromHandle(hf);
        stream.Write(buffer, 0, cb);
        return cb;
    }

    private static Stream StreamFromHandle(IntPtr hf)
    {
        return (Stream)((GCHandle)hf).Target;
    }

    private static IntPtr MemAlloc(int cb)
    {
        return Marshal.AllocHGlobal(cb);
    }

    private static void MemFree(IntPtr mem)
    {
        Marshal.FreeHGlobal(mem);
    }

    private static int FileSeek(IntPtr hf, int dist, int seektype)
    {
        var stream = StreamFromHandle(hf);
        return (int)stream.Seek(dist, (SeekOrigin)seektype);
    }

    private static bool FdiCopy()
    {
        try
        {
            return NativeMethods.FdiCopy(FdiContext, "<notused>", "<notused>", 0, ExtractCallback, IntPtr.Zero, IntPtr.Zero);
        }
        catch (Exception)
        {

            return false;
        }

    }

    private bool FdiIterate()
    {
        return NativeMethods.FdiCopy(FdiContext, "<notused>", "<notused>", 0, IterateCallback, IntPtr.Zero, IntPtr.Zero);
    }



    private static IntPtr FdiContext { get; set; }

    public void Dispose()
    {
        Dispose(true);
    }
    private void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (!_disposed)
            {
                if (FdiContext != IntPtr.Zero)
                {
                    NativeMethods.FdiDestroy(FdiContext);
                    FdiContext = IntPtr.Zero;
                }
                _disposed = true;
            }
        }
    }
}