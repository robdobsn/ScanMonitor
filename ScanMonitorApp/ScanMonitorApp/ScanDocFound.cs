using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ScanMonitorApp
{
    class ScanDocFound
    {
        public ScanDocFound(string fileName)
        {
            FileName = fileName;
        }
        public string FileName { get; set; }
    }
}
