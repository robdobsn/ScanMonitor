using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Ghostscript.NET;
using Ghostscript.NET.Rasterizer;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.IO;
using NLog;
using MongoDB.Driver;
using System.Drawing;
using iTextSharp.text.pdf;

namespace ScanMonitorApp
{
    class PdfRasterizer
    {
        //private GhostscriptVersionInfo _lastInstalledVersion = null;
        private static GhostscriptRasterizer _rasterizer = new GhostscriptRasterizer();
        private Dictionary<int, System.Drawing.Image> _pageCache = new Dictionary<int, System.Drawing.Image>();
        private string _inputPdfPath;
        private List<int> _pageRotationInfo = new List<int>();
        private List<iTextSharp.text.Rectangle> _pageSizes = new List<iTextSharp.text.Rectangle>();
        private int _pointsPerInch = 0;
        private static Logger logger = LogManager.GetCurrentClassLogger();

        public PdfRasterizer(string inputPdfPath, int pointsPerInch)
        {
            _pointsPerInch = pointsPerInch;
            // Extract info from pdf using iTextSharp
            try
            {
                using (Stream newpdfStream = new FileStream(inputPdfPath, FileMode.Open, FileAccess.Read))
                {
                    using (PdfReader pdfReader = new PdfReader(newpdfStream))
                    {
                        int numPagesToUse = pdfReader.NumberOfPages;
                        for (int pageNum = 1; pageNum <= numPagesToUse; pageNum++)
                        {
                            iTextSharp.text.Rectangle pageRect = pdfReader.GetPageSize(pageNum);
                            _pageSizes.Add(pageRect);
                            int pageRot = pdfReader.GetPageRotation(pageNum);
                            _pageRotationInfo.Add(pageRot);
                        }
                    }
                }
            }
            catch (Exception excp)
            {
                logger.Error("Cannot open PDF with iTextSharp {0} excp {1}", inputPdfPath, excp.Message);
            }

            //_lastInstalledVersion =
            //    GhostscriptVersionInfo.GetLastInstalledVersion(
            //            GhostscriptLicense.GPL | GhostscriptLicense.AFPL,
            //            GhostscriptLicense.GPL);

            try
            {
                //string inpPath = inputPdfPath.Replace("/", @"\");
                byte[] buffer = File.ReadAllBytes(inputPdfPath);
                MemoryStream ms = new MemoryStream(buffer);
                _rasterizer.Open(ms);

                //string inpPath = inputPdfPath.Replace("/", @"\");
                //inpPath = System.Text.Encoding.UTF8.GetString(System.Text.Encoding.UTF8.GetBytes(inpPath));
                //_rasterizer.Open(inpPath, _lastInstalledVersion, false);
            }
            catch (Exception excp)
            {
                logger.Error("Cannot open PDF with ghostscript {0} excp {1}", inputPdfPath, excp.Message);
            }

            _inputPdfPath = inputPdfPath;

        }

        public void Close()
        {
            _rasterizer.Close();
        }

        public int NumPages()
        {
            return _pageRotationInfo.Count;
        }

        public System.Drawing.Image GetPageImage(int pageNum, bool rotateBasedOnText)
        {
            // Return from cache if available
            if (_pageCache.ContainsKey(pageNum))
                return _pageCache[pageNum];

            // Fill cache
            System.Drawing.Image img = null;
            try
            {
                img = _rasterizer.GetPage(_pointsPerInch, _pointsPerInch, pageNum);
                // Rotate image as required
                if (rotateBasedOnText)
                {
                    int pageIdx = pageNum - 1;
                    if (pageIdx < _pageRotationInfo.Count)
                        if (_pageRotationInfo[pageIdx] != 0)
                            img = RotateImageWithoutCrop(img, _pageRotationInfo[pageIdx]);
                    }
                _pageCache.Add(pageNum, img);
            }
            catch (Exception excp)
            {
                Console.WriteLine("Failed to create image of page {0}", _inputPdfPath, excp.Message);
            }

            return img;
        }

        public List<string> GeneratePageFiles(string uniqName, ScanPages scanPages, string outputPath, int maxPages, bool rotateBasedOnText)
        {
            List<string> imgFileNames = new List<string>();

            // Create new stopwatch
            Stopwatch stopwatch = new Stopwatch();

            // Begin timing
            stopwatch.Start();

            int numPagesToConvert = _rasterizer.PageCount;
            if (numPagesToConvert > maxPages)
                numPagesToConvert = maxPages;
            for (int pageNumber = 1; pageNumber <= numPagesToConvert; pageNumber++)
            {
                string pageFileName = GetFilenameOfImageOfPage(outputPath, uniqName, pageNumber, true, "jpg");
                try
                {
                    System.Drawing.Image img = _rasterizer.GetPage(_pointsPerInch, _pointsPerInch, pageNumber);
                    // Rotate image as required
                    if (rotateBasedOnText)
                    {
                        if (pageNumber - 1 < scanPages.pageRotations.Count)
                            if (scanPages.pageRotations[pageNumber - 1] != 0)
                                img = RotateImageWithoutCrop(img, scanPages.pageRotations[pageNumber - 1]);
                    }
                    // Save to file
                    img.Save(pageFileName, ImageFormat.Jpeg);
                    imgFileNames.Add(pageFileName);
                }
                catch (Exception excp)
                {
                    logger.Error("Failed to create image of page {0}", pageFileName, excp.Message);
                }
            }
            // Stop timing
            stopwatch.Stop();

            logger.Info("Converted {0} ({1} pages) to image files in {2}", _inputPdfPath, numPagesToConvert, stopwatch.Elapsed);

            return imgFileNames;
        }

        private Bitmap RotateImage(System.Drawing.Image inputImage, float angle)
        {
            int outWidth = inputImage.Width;
            int outHeight = inputImage.Height;
            if ((angle > 60 && angle < 120) || (angle > 240 && angle < 300))
            {
                outWidth = inputImage.Height;
                outHeight = inputImage.Width;
            }

            Bitmap rotatedImage = new Bitmap(outWidth, outHeight);
            using (Graphics g = Graphics.FromImage(rotatedImage))
            {
                g.TranslateTransform(inputImage.Width / 2, inputImage.Height / 2); //set the rotation point as the center into the matrix
                g.RotateTransform(angle); //rotate
                g.TranslateTransform(-inputImage.Width / 2, -inputImage.Height / 2); //restore rotation point into the matrix
                g.DrawImage(inputImage, new Point(0, 0)); //draw the image on the new bitmap
            }

            return rotatedImage;
        }

        public Image RotateImageWithoutCrop(Image b, float angle)
        {
            if (angle > 0)
            {
                int l = b.Width;
                int h = b.Height;
                double an = angle * Math.PI / 180;
                double cos = Math.Abs(Math.Cos(an));
                double sin = Math.Abs(Math.Sin(an));
                int nl = (int)(l * cos + h * sin);
                int nh = (int)(l * sin + h * cos);
                Bitmap returnBitmap = new Bitmap(nl, nh);
                Graphics g = Graphics.FromImage(returnBitmap);
                g.TranslateTransform((float)(nl - l) / 2, (float)(nh - h) / 2);
                g.TranslateTransform((float)b.Width / 2, (float)b.Height / 2);
                g.RotateTransform(angle);
                g.TranslateTransform(-(float)b.Width / 2, -(float)b.Height / 2);
                g.DrawImage(b, new Point(0, 0));
                return returnBitmap;
            }
            else return b;
        }

        public static string GetFilenameOfImageOfPage(string baseFolderForImages, string uniqName, int pageNum, bool bCreateFolderIfReqd, string fileExtForced = "")
        {
            if (fileExtForced != "")
                return Path.Combine(ScanDocInfo.GetImageFolderForFile(baseFolderForImages, uniqName, bCreateFolderIfReqd), uniqName + "_" + pageNum.ToString() + "." + fileExtForced).Replace('\\', '/');
            string jpgPath = Path.Combine(ScanDocInfo.GetImageFolderForFile(baseFolderForImages, uniqName, bCreateFolderIfReqd), uniqName + "_" + pageNum.ToString() + ".jpg").Replace('\\', '/');
            if (File.Exists(jpgPath))
                return jpgPath;
            string pngPath = Path.Combine(ScanDocInfo.GetImageFolderForFile(baseFolderForImages, uniqName, bCreateFolderIfReqd), uniqName + "_" + pageNum.ToString() + ".png").Replace('\\', '/');
            if (File.Exists(pngPath))
                return pngPath;
            return jpgPath;
        }

        public static System.Drawing.Image GetImageOfPage(string fileName, int pageNum)
        {
            int desired_x_dpi = 150;
            int desired_y_dpi = 150;

            GhostscriptVersionInfo lastInstalledVersion =
                GhostscriptVersionInfo.GetLastInstalledVersion(
                        GhostscriptLicense.GPL | GhostscriptLicense.AFPL,
                        GhostscriptLicense.GPL);

            GhostscriptRasterizer rasterizer = new GhostscriptRasterizer();

            rasterizer.Open(fileName, lastInstalledVersion, false);

            if (pageNum > rasterizer.PageCount)
                return null;

            System.Drawing.Image img = rasterizer.GetPage(desired_x_dpi, desired_y_dpi, pageNum);

            rasterizer = null;

            return img;
        }
    }
}
