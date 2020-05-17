using Delimon.Win32.IO;
using MahApps.Metro.Controls;
using NLog;
using System;
using System.Collections.Generic;
using System.ComponentModel;
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
    /// Interaction logic for MaintenanceView.xaml
    /// </summary>
    public partial class MaintenanceView : MetroWindow
    {
        private static Logger logger = LogManager.GetCurrentClassLogger();
        BackgroundWorker _bwThread_forFileHashCreation = null;
        private ScanDocHandler _scanDocHandler;
        private DocTypesMatcher _docTypesMatcher;
        private WindowClosingDelegate _windowClosingCB;

        public MaintenanceView(ScanDocHandler scanDocHandler, DocTypesMatcher docTypesMatcher, 
                WindowClosingDelegate windowClosingCB)
        {
            InitializeComponent();
            _scanDocHandler = scanDocHandler;
            _docTypesMatcher = docTypesMatcher;
            _windowClosingCB = windowClosingCB;
    }
        private void btnViewDocTypes_Click(object sender, RoutedEventArgs e)
        {
            DocTypeView dtv = new DocTypeView(_scanDocHandler, _docTypesMatcher);
            dtv.ShowDocTypeList("", null, null);
            dtv.ShowDialog();
        }

        private void butViewSubstMacros_Click(object sender, RoutedEventArgs e)
        {
            PathSubstView ptv = new PathSubstView(_docTypesMatcher);
            ptv.ShowDialog();
        }

        private void butRecomputeMD5_Click(object sender, RoutedEventArgs e)
        {
            if ((_bwThread_forFileHashCreation != null) && (_bwThread_forFileHashCreation.IsBusy))
            {
                if (butRecomputeMD5.Content.ToString().ToLower().Contains("cancel"))
                {
                    _bwThread_forFileHashCreation.CancelAsync();
                    hashCreateStatus.Content = "Cancelling...";
                }
                return;
            }

            // Go through all folders in the filing area and compute MD5 hashes for them
            // Matcher thread
            _bwThread_forFileHashCreation = new BackgroundWorker();
            _bwThread_forFileHashCreation.WorkerSupportsCancellation = true;
            _bwThread_forFileHashCreation.WorkerReportsProgress = true;
            _bwThread_forFileHashCreation.DoWork += new DoWorkEventHandler(FileHashCreation_DoWork);
            _bwThread_forFileHashCreation.RunWorkerCompleted += new RunWorkerCompletedEventHandler(FileHashCreation_RunWorkerCompleted);
            _bwThread_forFileHashCreation.ProgressChanged += new ProgressChangedEventHandler(FileHashCreation_ProgressChanged);
            _bwThread_forFileHashCreation.RunWorkerAsync(Properties.Settings.Default.FoldersToSearchForFiledDocs);

            // Change button to cancel
            butRecomputeMD5.Content = "Cancel MD5";
            hashCreateStatus.Content = "Busy...";
        }

        private void FileHashCreation_DoWork(object sender, DoWorkEventArgs e)
        {
            string startFolders = (string)e.Argument;
            string[] startFolderList = startFolders.Split(';');
            BackgroundWorker worker = sender as BackgroundWorker;

            // Empty database initially
            _scanDocHandler.EmptyExistingFileRecDB();

            // Process
            int numFilesFound = 0;
            int folderIdx = 0;
            foreach (string startFolder in startFolderList)
            {
                worker.ReportProgress((int)((folderIdx + 1) * 5 / startFolderList.Length));
                EstimateNumFilesToAdd(startFolder, worker, e, ref numFilesFound);
                folderIdx++;
            }

            int filesAdded = 0;
            foreach (string startFolder in startFolderList)
            {
                RecurseFoldersAddingFilesToDb(startFolder, worker, e, numFilesFound, ref filesAdded);
                if (e.Cancel)
                    break;
            }
        }

        private void FileHashCreation_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            progBar.Value = (e.ProgressPercentage);
        }

        private void FileHashCreation_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            object[] rslt = (object[])e.Result;
            string rsltMessage = (string)rslt[0];
            int rsltFilesAdded = (int)rslt[1];
            hashCreateStatus.Content = rsltMessage;

            progBar.Value = (100);
            butRecomputeMD5.Content = "Recompute MD5";
        }

        private void AddFileToDb(string filename, byte[] md5Hash, long fileLength)
        {
            ExistingFileInfoRec ir = new ExistingFileInfoRec();
            ir.filename = filename;
            ir.md5Hash = md5Hash;
            ir.fileLength = fileLength;
            _scanDocHandler.AddExistingFileRecToMongo(ir);
        }

        string[] fileFilters = new string[] { "*.pdf", "*.jpg", "*.png" };

        private void RecurseFoldersAddingFilesToDb(string startingFolder, BackgroundWorker worker, DoWorkEventArgs e, int totalNumFilesEstimate, ref int filesAdded)
        {
            try
            {
                //const bool TEST = true;
                //string fddd = @"\\MACALLAN\Main\RobAndJudyPersonal\Grace and Joe\Joe's own files\Diary\Joe's Notes & Stories - 2012-13 Diary Entries.pdf";
                //if (TEST)
                //{
                //    byte[] md5Val = new byte[0];
                //    long fileLen = 0;
                //    md5Val = ScanDocHandler.GenHashOnFileExcludingMetadata(fddd, out fileLen);
                //    if (md5Val.Length > 0)
                //    {
                //        AddFileToDb(fddd, md5Val, fileLen);
                //        filesAdded++;
                //    }

                //}
                //else
                //{
                if (!Directory.Exists(startingFolder))
                    return;
                foreach (string fileFilter in fileFilters)
                {
                    foreach (string f in Directory.GetFiles(startingFolder, fileFilter))
                    {
                        // Check for cancel
                        if ((worker.CancellationPending == true))
                        {
                            e.Cancel = true;
                            object[] rslt = { "Cancelled", filesAdded };
                            e.Result = rslt;
                            return;
                        }

                        try
                        {
                            byte[] md5Val = new byte[0];
                            long fileLen = 0;
                            md5Val = ScanDocHandler.GenHashOnFileExcludingMetadata(f, out fileLen);
                            if (md5Val.Length > 0)
                            {
                                AddFileToDb(f, md5Val, fileLen);
                                filesAdded++;
                            }
                        }
                        catch (Exception excp)
                        {
                            logger.Error("Error in RecurseFoldersAddingFilesToDb " + f + " error " + excp.Message);
                        }

                        /*
                        bool addResult = false;
                        byte[] md5Val;
                        using (var md5 = MD5.Create())
                        {
                            using (var stream = File.OpenRead(f))
                            {
                                md5Val = md5.ComputeHash(stream);
                                AddFileToDb(f, md5Val, stream.Length);
                                filesAdded++;
                                addResult = true;
                            }
                        }

                        bool readFileToMem = false;
                        bool foundStrInFile = false;
                        string extractedText = "";
                        try
                        {
                            byte[] fileData = File.ReadAllBytes(f);
                            readFileToMem = true;
                            bool completeMatch = false;
                            int matchPos = 0;
                            for (int testPos = 0; testPos < fileData.Length - pdfImageTagBytes.Length; testPos++)
                            {
                                completeMatch = true;
                                for (int i = 0; i < pdfImageTagBytes.Length; i++)
                                    if (fileData[testPos + i] != pdfImageTagBytes[i])
                                    {
                                        completeMatch = false;
                                        break;
                                    }
                                if (completeMatch)
                                {
                                    matchPos = testPos;
                                    extractedText += Encoding.UTF8.GetString(fileData, matchPos, 70);
                                    break;
                                }
                            }
                        }
                        finally
                        {
                        }
                            * */
                        /*
                        string path = @"C:\Users\rob_2\Documents\md5files.tsv";
                        if (!File.Exists(path))
                        {
                            // Create a file to write to. 
                            using (StreamWriter sw = File.CreateText(path))
                            {
                                sw.WriteLine("Filename\tMD5\tMD5OK\tpartial\tonlen\ttotlen");
                            }
                        }
                        using (StreamWriter sw = File.AppendText(path))
                        {
                            if (md5CreatedOk)
                            {
                                sw.Write(f + "\t");
                                for (int i = 0; i < md5Val.Length; i++)
                                    sw.Write(String.Format("#{0:X},",md5Val[i]));
                                sw.WriteLine("\tTRUE");
                                sw.WriteLine(partialMD5.ToString() + "\t" + md5OnLen.ToString() + "\t" + fileLen.ToString());
                            }
                            else
                            {
                                sw.WriteLine(f + "\t\tFALSE\t\t\t\t");
                            }
                        }	
                        */
                    }
                }
                foreach (string d in Directory.GetDirectories(startingFolder))
                {
                    RecurseFoldersAddingFilesToDb(d, worker, e, totalNumFilesEstimate, ref filesAdded);
                    worker.ReportProgress((int)(5 + (filesAdded * 95 / totalNumFilesEstimate)));
                    if ((worker.CancellationPending == true))
                    {
                        e.Cancel = true;
                        object[] rslt = { "Cancelled", filesAdded };
                        return;
                    }
                }
                //TEST                }
            }
            catch (System.Exception excpt)
            {
                logger.Error("Failed to create MD5 {0}", excpt.Message);
                object[] rslt = { "Error - check log", filesAdded };
                e.Result = rslt;
            }
            object[] finalRslt = { String.Format("Added {0} files", filesAdded), filesAdded };
            e.Result = finalRslt;
        }

        private void EstimateNumFilesToAdd(string startingFolder, BackgroundWorker worker, DoWorkEventArgs e, ref int numFilesFound)
        {
            try
            {
                if (!Directory.Exists(startingFolder))
                    return;
                foreach (string fileFilter in fileFilters)
                    numFilesFound += Directory.GetFiles(startingFolder, fileFilter).Length;
                foreach (string d in Directory.GetDirectories(startingFolder))
                {
                    EstimateNumFilesToAdd(d, worker, e, ref numFilesFound);
                    if ((worker.CancellationPending == true))
                    {
                        e.Cancel = true;
                        e.Result = "Cancelled";
                        return;
                    }
                }
            }
            catch (System.Exception excpt)
            {
                logger.Error("Error estimating number of files already filed", excpt.Message);
                e.Result = "Error - check log";
            }
        }
        private void Window_Closed(object sender, EventArgs e)
        {
            _windowClosingCB();
        }
    }
}
