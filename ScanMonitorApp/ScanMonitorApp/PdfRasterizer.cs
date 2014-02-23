﻿using System;
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

namespace ScanMonitorApp
{
    class PdfRasterizer
    {
        private GhostscriptVersionInfo _lastInstalledVersion = null;
        private GhostscriptRasterizer _rasterizer = null;
        private static Logger logger = LogManager.GetCurrentClassLogger();

        public List<string> Start(string inputPdfPath, string outputPath, int maxPages)
        {
            List<string> imgFileNames = new List<string>();

            int desired_x_dpi = 150;
            int desired_y_dpi = 150;

            // Create new stopwatch
            Stopwatch stopwatch = new Stopwatch();

            // Begin timing
            stopwatch.Start();

            _lastInstalledVersion =
                GhostscriptVersionInfo.GetLastInstalledVersion(
                        GhostscriptLicense.GPL | GhostscriptLicense.AFPL,
                        GhostscriptLicense.GPL);

            _rasterizer = new GhostscriptRasterizer();

            _rasterizer.Open(inputPdfPath, _lastInstalledVersion, false);

            string baseName = Path.GetFileNameWithoutExtension(inputPdfPath);

            int numPagesToConvert = _rasterizer.PageCount;
            if (numPagesToConvert > maxPages)
                numPagesToConvert = maxPages;
            for (int pageNumber = 1; pageNumber <= numPagesToConvert; pageNumber++)
            {
                string pageFilePath = Path.Combine(outputPath, baseName + "_" + pageNumber.ToString() + ".png");

                System.Drawing.Image img = _rasterizer.GetPage(desired_x_dpi, desired_y_dpi, pageNumber);
                img.Save(pageFilePath, ImageFormat.Png);

                imgFileNames.Add(pageFilePath); 
            }
            // Stop timing
            stopwatch.Stop();
            _rasterizer.Dispose();

            logger.Info("Converted {0} ({1} pages) to image files in {2}", inputPdfPath, numPagesToConvert, stopwatch.Elapsed);

            return imgFileNames;
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
