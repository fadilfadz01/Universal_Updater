using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Universal_Updater
{
    public class OutputCapture : TextWriter, IDisposable
    {
        private TextWriter stdOutWriter;
        public static TextWriter Captured { get; private set; }
        public override Encoding Encoding { get { return Encoding.ASCII; } }

        public OutputCapture()
        {
            this.stdOutWriter = Console.Out;
            Console.SetOut(this);
            Captured = new StringWriter();
        }

        override public void Write(string output)
        {
            Captured.Write(output);
            stdOutWriter.Write(output);
        }

        override public void WriteLine(string output)
        {
            Captured.WriteLine(output);
            stdOutWriter.WriteLine(output);
        }
    }
}
