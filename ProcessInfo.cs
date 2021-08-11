using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace Process_Affinity_Utility
{
    class ProcessInfo
    {
        public Process Process { get; set; }
        public Mode StringMode { get; set; } = Mode.ProcessName;

        public ProcessInfo(Process process)
        {
            Process = process;
        }

        public enum Mode
        {
            ProcessName,
            MainWindowTitle
        }

        public override string ToString()
        {
            return (StringMode == Mode.MainWindowTitle ? Process.ProcessName + " - " + Process.MainWindowTitle : Process.ProcessName);
        }
    }
}
