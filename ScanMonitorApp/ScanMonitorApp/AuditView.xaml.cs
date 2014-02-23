using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace ScanMonitorApp
{
    /// <summary>
    /// Interaction logic for AuditView.xaml
    /// </summary>
    public partial class AuditView : Window
    {
        public AuditView()
        {
            InitializeComponent();
        }

        ObservableCollection<AuditData> _auditDataColl = new ObservableCollection<AuditData>();
        public ObservableCollection<AuditData> AuditDataColl
        {
            get { return _auditDataColl; }
        }

        public void ReadAuditFile(string fileName)
        {
            // Read file
            using (StreamReader sr = new StreamReader(fileName))
            {
                while (sr.Peek() >= 0)
                {
                    string line = sr.ReadLine();
                    string[] fields = line.Split('\t');
                    AuditData ad = new AuditData();
                    ad.ProcDateAndTime = fields[0];
                    ad.DocType = fields[1];
                    ad.OrigFileName = fields[2];
                    string uniqName = System.IO.Path.GetFileNameWithoutExtension(fields[2]);
                    ad.UniqName =uniqName;
                    ad.DestFile = fields[3];
                    ad.ArchiveFile = fields[4];
                    ad.ProcMessage = fields[5];
                    ad.ProcStatus = fields[6];
                    _auditDataColl.Add(ad);
                }
            }
        }

        private void auditListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            auditFileImage.se
        }
    }

    public class AuditData
    {
        public string UniqName {get; set;}
        public string DocType { get; set; }
        public string ProcStatus { get; set; }
        public string DestFile { get; set; }
        public string ArchiveFile { get; set; }
        public string OrigFileName { get; set; }
        public string ProcDateAndTime { get; set; }
        public string ProcMessage { get; set; }
    }
}
