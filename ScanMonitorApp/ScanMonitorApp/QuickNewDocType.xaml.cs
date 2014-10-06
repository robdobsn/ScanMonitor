using MahApps.Metro.Controls;
using Microsoft.WindowsAPICodePack.Dialogs;
using NLog;
using System;
using System.Collections.Generic;
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
    /// Interaction logic for QuickNewDocType.xaml
    /// </summary>
    public partial class QuickNewDocType : MetroWindow
    {
        private static Logger logger = LogManager.GetCurrentClassLogger();
        private ScanDocHandler _scanDocHandler;
        private DocTypesMatcher _docTypesMatcher;
        private string _curDocTypeThumbnail = "";
        private string _curDocUniqName = "";
        private int _curDocPageNum = 0;
        public string _newDocTypeName = "";

        public QuickNewDocType(ScanDocHandler scanDocHandler, DocTypesMatcher docTypesMatcher, string curDocUniqName, int curDocPageNum)
        {
            InitializeComponent();
            _scanDocHandler = scanDocHandler;
            _docTypesMatcher = docTypesMatcher;
            _curDocPageNum = curDocPageNum;
            _curDocUniqName = curDocUniqName;
            txtDocTypeName.Focus();
        }

        private void btnCancelTypeChanges_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void btnSaveTypeChanges_Click(object sender, RoutedEventArgs e)
        {
            string docTypeName = txtDocTypeName.Text.Trim();
            if (docTypeName == "")
            {
                MessageBoxButton btnMessageBox = MessageBoxButton.OK;
                MessageBoxImage icnMessageBox = MessageBoxImage.Information;
                MessageBoxResult rsltMessageBox = MessageBox.Show("Document Type cannot be blank", "Naming Problem", btnMessageBox, icnMessageBox);
                return;
            }

            DocType testDocType = _docTypesMatcher.GetDocType(txtDocTypeName.Text);
            if (testDocType != null)
            {
                MessageBoxButton btnMessageBox = MessageBoxButton.OK;
                MessageBoxImage icnMessageBox = MessageBoxImage.Information;
                MessageBoxResult rsltMessageBox = MessageBox.Show("There is already a Document Type with this name", "Naming Problem", btnMessageBox, icnMessageBox);
                return;
            }

            if (docTypeName.Split('-').Length < 2)
            {
                MessageBoxButton btnMessageBox = MessageBoxButton.OK;
                MessageBoxImage icnMessageBox = MessageBoxImage.Information;
                MessageBoxResult rsltMessageBox = MessageBox.Show("A category name should be included separated by a -", "Naming Problem", btnMessageBox, icnMessageBox);
                return;
            }
            string categoryStr = docTypeName.Split('-')[0].Trim();

            // Check category already exists
            List<string> docTypeStrings = new List<string>();
            List<DocType> docTypeList = _docTypesMatcher.ListDocTypes();
            foreach (DocType dt in docTypeList)
            {
                if (!dt.isEnabled)
                    continue;
                docTypeStrings.Add(dt.docTypeName);
            }
            docTypeStrings.Sort();
            string curHead = "";
            List<string> categoryStrings = new List<string>();
            foreach (string docTypeString in docTypeStrings)
            {
                string[] elemsS = docTypeString.Split('-');
                if (elemsS.Length <= 0)
                    continue;
                string hdrStr = elemsS[0].Trim();
                if (curHead.ToLower() != hdrStr.ToLower())
                {
                    categoryStrings.Add(hdrStr);
                    curHead = hdrStr;
                }
            }
            if (!categoryStrings.Contains(categoryStr))
            {
                MessageBoxButton btnMessageBox = MessageBoxButton.YesNo;
                MessageBoxImage icnMessageBox = MessageBoxImage.Information;
                MessageBoxResult rsltMessageBox = MessageBox.Show("This category string has not been used before. Add new category?", "Naming Problem", btnMessageBox, icnMessageBox);
                if (rsltMessageBox != MessageBoxResult.Yes)
                    return;
            }

            // Check the path is valid
            string moveToFolder = txtMoveTo.Text.Trim();
            if (moveToFolder == "")
            {
                MessageBoxButton btnMessageBox = MessageBoxButton.OK;
                MessageBoxImage icnMessageBox = MessageBoxImage.Information;
                MessageBoxResult rsltMessageBox = MessageBox.Show("A Move To folder needs to be specified", "Naming Problem", btnMessageBox, icnMessageBox);
                return;
            }

            // Create the new record
            DocType newDocType = GetDocTypeFromForm(new DocType());
            _docTypesMatcher.AddOrUpdateDocTypeRecInDb(newDocType);

            _newDocTypeName = docTypeName;
            DialogResult = true;
            Close();
        }

        private void btnUseCurrentDocImageAsThumbnail_Click(object sender, RoutedEventArgs e)
        {
            if (_curDocUniqName.Trim() != "")
                ShowDocTypeThumbnail(_curDocUniqName + "~" + _curDocPageNum.ToString());
            UpdateUIForDocTypeChanges();
        }

        private void btnPickThumbnail_Click(object sender, RoutedEventArgs e)
        {
            CommonOpenFileDialog cofd = new CommonOpenFileDialog("Select thumbnail file");
            cofd.Multiselect = false;
            cofd.InitialDirectory = Properties.Settings.Default.BasePathForFilingFolderSelection;
            cofd.Filters.Add(new CommonFileDialogFilter("Image File", ".jpg,.png"));
            CommonFileDialogResult result = cofd.ShowDialog(this);
            if (result == CommonFileDialogResult.Ok)
                ShowDocTypeThumbnail(cofd.FileName);
            UpdateUIForDocTypeChanges();

        }

        private void btnClearThumbail_Click(object sender, RoutedEventArgs e)
        {
            ShowDocTypeThumbnail("");
            UpdateUIForDocTypeChanges();
        }

        private void imgDocThumbMenuPaste_Click(object sender, RoutedEventArgs e)
        {
            // Save to a file in the thumbnails folder
            string thumbnailStr = DocTypeHelper.GetNameForPastedThumbnail();
            string thumbFilename = DocTypeHelper.GetFilenameFromThumbnailStr(thumbnailStr);
            if (SaveClipboardImageToFile(thumbFilename))
                ShowDocTypeThumbnail(thumbnailStr);
            UpdateUIForDocTypeChanges();
        }

        private void btnMoveToPick_Click(object sender, RoutedEventArgs e)
        {
            // Check what path to use
            string folderToUse = "";
            if (txtMoveTo.Text.Trim() != "")
            {
                bool pathContainsMacros = false;
                folderToUse = _docTypesMatcher.ComputeExpandedPath(txtMoveTo.Text.Trim(), DateTime.Now, true, ref pathContainsMacros);
            }
            else if (Directory.Exists(Properties.Settings.Default.BasePathForFilingFolderSelection))
            {
                folderToUse = Properties.Settings.Default.BasePathForFilingFolderSelection;
            }

            CommonOpenFileDialog cofd = new CommonOpenFileDialog("Select Folder for filing this document type");
            cofd.IsFolderPicker = true;
            cofd.Multiselect = false;
            cofd.InitialDirectory = folderToUse;
            cofd.DefaultDirectory = folderToUse;
            cofd.EnsurePathExists = true;
            CommonFileDialogResult result = cofd.ShowDialog(this);
            if (result == CommonFileDialogResult.Ok)
            {
                string folderName = cofd.FileName;
                txtMoveTo.Text = _docTypesMatcher.ComputeMinimalPath(folderName);
            }
        }

        private void moveToCtx_Year_Click(object sender, RoutedEventArgs e)
        {
            // Add year & yearmonth to folder name
            txtMoveTo.Text += @"\[year]";
        }

        private void moveToCtx_YearQtr_Click(object sender, RoutedEventArgs e)
        {
            // Add year & yearmonth to folder name
            txtMoveTo.Text += @"\[year]\[year-qtr]";
        }

        private void moveToCtx_FinYear_Click(object sender, RoutedEventArgs e)
        {
            // Add year & yearmonth to folder name
            txtMoveTo.Text += @"\[finyear]";
        }

        private void moveToCtx_YearFinQtr_Click(object sender, RoutedEventArgs e)
        {
            // Add year & yearmonth to folder name
            txtMoveTo.Text += @"\[year]\[year-fqtr]";
        }

        private void moveToCtx_YearMon_Click(object sender, RoutedEventArgs e)
        {
            txtMoveTo.Text += @"\[year-month]";
        }

        private DocType GetDocTypeFromForm(DocType docType)
        {
            docType.docTypeName = txtDocTypeName.Text;
            docType.isEnabled = true;
            docType.matchExpression = "";
            docType.dateExpression = "";
            docType.moveFileToPath = txtMoveTo.Text;
            string defaultRenameToContents = Properties.Settings.Default.DefaultRenameTo;
            docType.renameFileTo = txtRenameTo.Text == defaultRenameToContents ? "" : txtRenameTo.Text;
            docType.thumbnailForDocType = _curDocTypeThumbnail;
            return docType;
        }

        private void ShowDocTypeThumbnail(string thumbnailStr)
        {
            _curDocTypeThumbnail = thumbnailStr;
            int heightOfThumb = 150;
            if (!double.IsNaN(imgDocThumbnail.Height))
                heightOfThumb = (int)imgDocThumbnail.Height;
            if (thumbnailStr == "")
                imgDocThumbnail.Source = new BitmapImage(new Uri("res/NoThumbnail.png", UriKind.Relative));
            else
                imgDocThumbnail.Source = DocTypeHelper.LoadDocThumbnail(thumbnailStr, heightOfThumb);
        }

        private void UpdateUIForDocTypeChanges()
        { 
        }

        private static bool SaveClipboardImageToFile(string filePath)
        {
            var image = Clipboard.GetImage();
            if (image == null)
                return false;
            try
            {
                using (var fileStream = new FileStream(filePath, FileMode.Create))
                {
                    BitmapEncoder encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(image));
                    encoder.Save(fileStream);
                    return true;
                }
            }
            catch
            {

            }
            return false;
        }
    }
}
