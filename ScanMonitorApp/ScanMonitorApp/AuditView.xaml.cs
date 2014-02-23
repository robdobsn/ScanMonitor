using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
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
        private List<TextSubst> textSubst = new List<TextSubst>();

        public AuditView()
        {
            InitializeComponent();
            textSubst.Add(new TextSubst(@"\RobAndJudyPersonal\Info\Manuals - ", @"\RobAndJudyPersonal\"));
            textSubst.Add(new TextSubst(@"\8 Dick Place\Self storage", @"\8 Dick Place\Removals & Storage\Self storage"));
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
                    if (fields[5] != "OK")
                        continue;
                    AuditData ad = new AuditData();
                    ad.ProcDateAndTime = fields[0];
                    ad.DocType = fields[1];
                    ad.OrigFileName = fields[2];
                    string uniqName = ScanDocInfo.GetUniqNameForFile(fields[2]);
                    ad.UniqName =uniqName;
                    ad.DestFile = DoTextSubst(fields[3]);
                    ad.ArchiveFile = fields[4];
                    ad.ProcStatus = fields[5];
                    ad.ProcMessage = fields[6];
                    ad.DestOk = File.Exists(ad.DestFile) ? "" : "NO";
                    ad.ArcvOk = File.Exists(ad.ArchiveFile) ? "" : "NO";
                    _auditDataColl.Add(ad);
                }
            }
            auditListView.ItemsSource = _auditDataColl;

            //// Check validity
            //for (int rowidx = 0; rowidx < auditListView.Items.Count; rowidx++)
            //{
            //    AuditData audData = (AuditData)(auditListView.Items[rowidx]);
            //    string destFile = audData.DestFile;
            //    if (File.Exists(destFile))
            //        auditListView.
            //}
        }

        private void auditListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems != null && e.AddedItems.Count > 0)
            {
                AuditData selectedRow = e.AddedItems[0] as AuditData;
                if (selectedRow != null)
                {
                    // Get file name
                    string fileName = selectedRow.ArchiveFile;
                    if (File.Exists(fileName))
                    {
                        System.Drawing.Image img = PdfRasterizer.GetImageOfPage(fileName, 1);
                        MemoryStream ms = new MemoryStream();
                        img.Save(ms, System.Drawing.Imaging.ImageFormat.Jpeg);
                        System.Windows.Media.Imaging.BitmapImage bImg = new System.Windows.Media.Imaging.BitmapImage();
                        bImg.BeginInit();
                        bImg.StreamSource = new MemoryStream(ms.ToArray());
                        bImg.EndInit();
                        auditFileImage.Source = bImg;
                    }
                }
            }
        }

        private string DoTextSubst(string inStr)
        {
            foreach(TextSubst ts in textSubst)
            {
                inStr = inStr.Replace(ts.origText, ts.newText);
            }
            return inStr;
        }
    }

    public class AuditData
    {
        public string UniqName {get; set;}
        public string DestOk { get; set; }
        public string ArcvOk { get; set; }
        public string DocType { get; set; }
        public string ProcStatus { get; set; }
        public string DestFile { get; set; }
        public string ArchiveFile { get; set; }
        public string OrigFileName { get; set; }
        public string ProcDateAndTime { get; set; }
        public string ProcMessage { get; set; }
    }

    public class TextSubst
    {
        public TextSubst(string o, string n)
        {
            origText = o;
            newText = n;
        }
        public string origText;
        public string newText;
    }
}
