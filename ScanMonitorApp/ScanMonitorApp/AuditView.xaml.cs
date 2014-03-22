using NLog;
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
        private static Logger logger = LogManager.GetCurrentClassLogger();
        private ScanDocHandler _scanDocHandler;
        private DocTypesMatcher _docTypesMatcher;

        public AuditView(ScanDocHandler scanDocHandler, DocTypesMatcher docTypesMatcher)
        {
            InitializeComponent();
            _scanDocHandler = scanDocHandler;
            _docTypesMatcher = docTypesMatcher;
        }

        ObservableCollection<AuditData> _auditDataColl = new ObservableCollection<AuditData>();
        public ObservableCollection<AuditData> AuditDataColl
        {
            get { return _auditDataColl; }
        }

        private void auditListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems != null && e.AddedItems.Count > 0)
            {
                AuditData selectedRow = e.AddedItems[0] as AuditData;
                if (selectedRow != null)
                {
                    string fileName = selectedRow.ArchiveFile;
                    string uniqName = System.IO.Path.GetFileNameWithoutExtension(fileName);
                    string imgFileName = PdfRasterizer.GetFilenameOfImageOfPage(Properties.Settings.Default.DocAdminImgFolderBase, uniqName, 1, false);
                    try
                    {
                        auditFileImage.Source = new BitmapImage(new Uri(imgFileName));
                    }
                    catch (Exception excp)
                    {
                        logger.Error("Loading bitmap file {0} excp {1}", imgFileName, excp.Message);
                    }


                    // Get doc type info
                    DocType dtype = _docTypesMatcher.GetDocType(selectedRow.DocType);
                    if (dtype != null)
                    {
                        txtOrigDocTypeName.Text = dtype.docTypeName;
                        txtOrigExpression.Text = dtype.matchExpression;
                    }

                    //ScanDocAllInfo scanDocAllInfo = _scanDocHandler.GetScanDocAllInfo(selectedRow.UniqName);
                    //txtNewDocTypeName.Text = scanDocAllInfo.scanDocInfo.docTypeMatchResult.docTypeName + "(" + scanDocAllInfo.scanDocInfo.docTypeMatchResult.matchCertaintyPercent.ToString() + "%)";
                    //DocType dtype2 = _docTypesMatcher.GetDocType(scanDocAllInfo.scanDocInfo.docTypeMatchResult.docTypeName);
                    //if (dtype2 != null)
                    //{
                    //    txtNewExpression.Text = dtype2.matchExpression;
                    //}

                    // Get file name
                    //string fileName = selectedRow.ArchiveFile;
                    //if (File.Exists(fileName))
                    //{
                    //    string imgName = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(selectedRow.ArchiveFile), System.IO.Path.GetFileNameWithoutExtension(selectedRow.ArchiveFile) + ".jpg");
                    //    if (File.Exists(imgName))
                    //        auditFileImage.Source = new BitmapImage(new Uri(imgName));

                        //System.Drawing.Image img = PdfRasterizer.GetImageOfPage(fileName, 1);
                        //MemoryStream ms = new MemoryStream();
                        //img.Save(ms, System.Drawing.Imaging.ImageFormat.Jpeg);
                        //System.Windows.Media.Imaging.BitmapImage bImg = new System.Windows.Media.Imaging.BitmapImage();
                        //bImg.BeginInit();
                        //bImg.StreamSource = new MemoryStream(ms.ToArray());
                        //bImg.EndInit();
                        //auditFileImage.Source = bImg;
                    // }
                }
            }
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

}
