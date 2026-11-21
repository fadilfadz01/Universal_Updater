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
using System.IO;
using System.Text;

namespace Universal_Updater
{
    public class OutputCapture : TextWriter
    {
        private readonly TextWriter stdOutWriter;
        private readonly object _writeLock = new object();
        public static TextWriter Captured { get; private set; }
        public override Encoding Encoding { get { return stdOutWriter != null ? stdOutWriter.Encoding : Encoding.UTF8; } }

        public OutputCapture()
        {
            this.stdOutWriter = Console.Out;
            Console.SetOut(this);
            Captured = TextWriter.Synchronized(new StringWriter());
        }

        public override void Write(char value)
        {
            lock (_writeLock)
            {
                Captured.Write(value);
                stdOutWriter.Write(value);
            }
        }

        override public void Write(string output)
        {
            if (output == null)
                return;

            lock (_writeLock)
            {
                Captured.Write(output);
                stdOutWriter.Write(output);
            }
        }

        override public void WriteLine(string output)
        {
            lock (_writeLock)
            {
                Captured.WriteLine(output);
                stdOutWriter.WriteLine(output);
            }
        }
    }
}