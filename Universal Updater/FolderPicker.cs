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
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Universal_Updater
{
    public static class GeneralPicker
    {
        public static Task<string> ShowSaveFileDialogAsync(string fileExt)
        {
            var capsPreview = fileExt.ToUpper();
            MessageBox.Show(
            $"Please the ({capsPreview}) save location.",
            $"Save {capsPreview}",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
            var taskCompletionSource = new TaskCompletionSource<string>();

            // start STA thread for showing the SaveFileDialog
            Thread staThread = new Thread(() =>
            {
                using (var saveFileDialog = new SaveFileDialog())
                {
                    saveFileDialog.Title = $"Save {capsPreview} File";
                    saveFileDialog.Filter = $"{capsPreview} Files (*.{fileExt})|*.{fileExt}";
                    saveFileDialog.DefaultExt = $"{fileExt}";
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

        public static Task<string> ShowOpenFileDialogAsync(string fileExt)
        {
            var capsPreview = fileExt.ToUpper();
            MessageBox.Show(
            $"Please select the ({capsPreview}) file.",
            $"Open {capsPreview}",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
            var taskCompletionSource = new TaskCompletionSource<string>();

            // Start STA thread for showing the OpenFileDialog
            Thread staThread = new Thread(() =>
            {
                using (var openFileDialog = new OpenFileDialog())
                {
                    openFileDialog.Title = $"Open {capsPreview} File";
                    openFileDialog.Filter = $"{capsPreview} Files (*.{fileExt})|*.{fileExt}";
                    openFileDialog.DefaultExt = $"{fileExt}";
                    openFileDialog.AddExtension = true;

                    if (openFileDialog.ShowDialog() == DialogResult.OK)
                    {
                        taskCompletionSource.SetResult(openFileDialog.FileName);
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

        public static async Task<string> ShowSelectFolderDialogAsync()
        {
            MessageBox.Show(
            $"Please select update packages folder.",
            $"Select Folder",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);

            string result = await Task.Run(() =>
            {
                var dialog = (IFileOpenDialog)new FileOpenDialog();
                dialog.SetOptions(FOS.FOS_PICKFOLDERS | FOS.FOS_FORCEFILESYSTEM);
                if (dialog.Show(IntPtr.Zero) == 0)
                {
                    dialog.GetResult(out IShellItem item);
                    item.GetDisplayName(SIGDN.SIGDN_FILESYSPATH, out IntPtr ptr);
                    string path = Marshal.PtrToStringAuto(ptr);
                    return path;
                }
                return null;
            });

            return result;
        }

        [ComImport]
        [Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7")]
        [ClassInterface(ClassInterfaceType.None)]
        private class FileOpenDialog { }

        [ComImport]
        [Guid("42f85136-db7e-439c-85f1-e4075d135fc8")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFileOpenDialog
        {
            [PreserveSig] int Show([In] IntPtr parent);
            void SetFileTypes();  // Not fully defined
            void SetFileTypeIndex(int iFileType);
            void GetFileTypeIndex(out int piFileType);
            void Advise(); // Not fully defined
            void Unadvise();
            void SetOptions([In] FOS fos);
            void GetOptions(out FOS pfos);
            void SetDefaultFolder(IShellItem psi);
            void SetFolder(IShellItem psi);
            void GetFolder(out IShellItem ppsi);
            void GetCurrentSelection(out IShellItem ppsi);
            void SetFileName([In, MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string pszName);
            void SetTitle([In, MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
            void SetOkButtonLabel([In, MarshalAs(UnmanagedType.LPWStr)] string pszText);
            void SetFileNameLabel([In, MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
            void GetResult(out IShellItem ppsi);
            void AddPlace(IShellItem psi, int alignment);
            void SetDefaultExtension([In, MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);
            void Close(int hr);
            void SetClientGuid();  // Not fully defined
            void ClearClientData();
            void SetFilter([MarshalAs(UnmanagedType.IUnknown)] object filter);
            void GetResults(); // Not fully defined
            void GetSelectedItems(); // Not fully defined
        }

        [ComImport]
        [Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItem
        {
            void BindToHandler(); // Not fully defined
            void GetParent(); // Not fully defined
            void GetDisplayName(SIGDN sigdnName, out IntPtr ppszName);
            void GetAttributes();  // Not fully defined
            void Compare();  // Not fully defined
        }

        private enum SIGDN : uint
        {
            SIGDN_FILESYSPATH = 0x80058000,
        }

        [Flags]
        private enum FOS
        {
            FOS_FORCEFILESYSTEM = 0x40,
            FOS_PICKFOLDERS = 0x20,
        }
    }
}
