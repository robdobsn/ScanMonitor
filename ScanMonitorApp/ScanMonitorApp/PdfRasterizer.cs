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
using SkiaSharp;

namespace ScanMonitorApp
{
    class PdfRasterizer
    {
        //private GhostscriptVersionInfo _lastInstalledVersion = null;
        private GhostscriptRasterizer _rasterizer = new GhostscriptRasterizer();
        private Dictionary<int, System.Drawing.Image> _pageCache = new Dictionary<int, System.Drawing.Image>();
        private string _inputPdfPath;
        private List<int> _pageRotationInfo = new List<int>();
        private List<iTextSharp.text.Rectangle> _pageSizes = new List<iTextSharp.text.Rectangle>();
        private int _pointsPerInch = 0;
        private static Logger logger = LogManager.GetCurrentClassLogger();

        private static GhostscriptVersionInfo FindGhostscriptVersion()
        {
            string dllName = Environment.Is64BitProcess ? "gsdll64.dll" : "gsdll32.dll";

            // Prefer the native library bundled with the application (ClickOnce / xcopy deploy)
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string[] bundledCandidates = new[]
                {
                    Path.Combine(baseDir, "Native", dllName),
                    Path.Combine(baseDir, dllName),
                };
                foreach (string candidate in bundledCandidates)
                {
                    if (File.Exists(candidate))
                    {
                        logger.Info("Using bundled Ghostscript native library at {0}", candidate);
                        return new GhostscriptVersionInfo(candidate);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Warn("Error probing for bundled Ghostscript: {0}", ex.Message);
            }

            // Fall back to the standard Ghostscript install location
            string[] searchRoots = new[]
            {
                Path.Combine(Environment.GetEnvironmentVariable("ProgramW6432") ?? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "gs"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "gs"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "gs"),
            };

            foreach (string root in searchRoots)
            {
                if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
                    continue;
                try
                {
                    foreach (string file in Directory.GetFiles(root, dllName, SearchOption.AllDirectories))
                    {
                        logger.Info("Found Ghostscript native library at {0}", file);
                        return new GhostscriptVersionInfo(file);
                    }
                }
                catch (Exception ex)
                {
                    logger.Warn("Error searching {0} for Ghostscript: {1}", root, ex.Message);
                }
            }

            // Fall back to registry-based discovery
            try
            {
                var version = GhostscriptVersionInfo.GetLastInstalledVersion(
                    GhostscriptLicense.GPL | GhostscriptLicense.AFPL,
                    GhostscriptLicense.GPL);
                if (version != null)
                {
                    logger.Info("Found Ghostscript via registry: {0}", version.DllPath);
                    return version;
                }
            }
            catch
            {
                // Registry lookup failed
            }

            return null;
        }

        private static GhostscriptVersionInfo _gsVersion;
        private static bool _gsVersionSearched = false;

        private static GhostscriptVersionInfo GetGhostscriptVersion()
        {
            if (!_gsVersionSearched)
            {
                _gsVersion = FindGhostscriptVersion();
                _gsVersionSearched = true;
                if (_gsVersion == null)
                    logger.Error("Ghostscript native library not found. PDF rasterization will not work.");
                else
                    logger.Info("Using Ghostscript: {0}", _gsVersion.DllPath);
            }
            return _gsVersion;
        }

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

            try
            {
                byte[] buffer = File.ReadAllBytes(inputPdfPath);
                MemoryStream ms = new MemoryStream(buffer);
                var gsVersion = GetGhostscriptVersion();
                if (gsVersion != null)
                    _rasterizer.Open(ms, gsVersion, false);
                else
                    _rasterizer.Open(ms);
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
                img = SKBitmapToImage(_rasterizer.GetPage(_pointsPerInch, pageNum));
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
                    System.Drawing.Image img = SKBitmapToImage(_rasterizer.GetPage(_pointsPerInch, pageNumber));
                    // Rotate image as required
                    if (rotateBasedOnText)
                    {
                        if (pageNumber - 1 < scanPages.pageRotations.Count)
                            if (scanPages.pageRotations[pageNumber - 1] != 0)
                                img = RotateImageWithoutCrop(img, scanPages.pageRotations[pageNumber - 1]);
                    }
                    // Save to file
                    if (System.IO.File.Exists(pageFileName))
                        System.IO.File.Delete(pageFileName);
                    img.Save(pageFileName, ImageFormat.Jpeg);
                    imgFileNames.Add(pageFileName);
                }
                catch (Exception excp)
                {
                    logger.Error("Failed to create image of page {0} {1}", pageFileName, excp.Message);
                }
            }
            // Stop timing
            stopwatch.Stop();

            logger.Debug("Converted {0} ({1} pages) to image files in {2}", _inputPdfPath, numPagesToConvert, stopwatch.Elapsed);

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

        private static System.Drawing.Image SKBitmapToImage(SKBitmap skBitmap)
        {
            using (var skImage = SKImage.FromBitmap(skBitmap))
            using (var skData = skImage.Encode(SKEncodedImageFormat.Png, 100))
            {
                var stream = new MemoryStream(skData.ToArray());
                return System.Drawing.Image.FromStream(stream);
            }
        }

        public static string GetFilenameOfImageOfPage(string baseFolderForImages, string uniqName, int pageNum, bool bCreateFolderIfReqd, string fileExtForced = "")
        {
            if (fileExtForced != "")
                return Path.Combine(ScanDocInfo.GetImageFolderForFile(baseFolderForImages, uniqName, bCreateFolderIfReqd), uniqName + "_" + pageNum.ToString() + "." + fileExtForced);
            string jpgPath = Path.Combine(ScanDocInfo.GetImageFolderForFile(baseFolderForImages, uniqName, bCreateFolderIfReqd), uniqName + "_" + pageNum.ToString() + ".jpg");
            if (File.Exists(jpgPath))
                return jpgPath;
            string pngPath = Path.Combine(ScanDocInfo.GetImageFolderForFile(baseFolderForImages, uniqName, bCreateFolderIfReqd), uniqName + "_" + pageNum.ToString() + ".png");
            if (File.Exists(pngPath))
                return pngPath;
            return jpgPath;
        }

        public static System.Drawing.Image GetImageOfPage(string fileName, int pageNum)
        {
            int desired_x_dpi = 150;

            var gsVersion = GetGhostscriptVersion();

            GhostscriptRasterizer rasterizer = new GhostscriptRasterizer();

            if (gsVersion != null)
                rasterizer.Open(fileName, gsVersion, false);
            else
                rasterizer.Open(fileName);

            if (pageNum > rasterizer.PageCount)
                return null;

            System.Drawing.Image img = SKBitmapToImage(rasterizer.GetPage(desired_x_dpi, pageNum));

            rasterizer = null;

            return img;
        }
    }
}
