using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ScanMonitorApp
{
    class ScanUtils
    {
        public static string GetFolderContentsAsString(string folder)
        {
            // Get folder contents for tooltip
            StringBuilder sb = new StringBuilder();
            if (System.IO.Directory.Exists(folder))
            {
                sb.Append("Folder ... " + folder + "\n");
                string[] filePaths = Directory.GetFiles(folder);
                List<string> filePathsList = filePaths.ToList<string>();
                filePathsList.Sort();
                foreach (string filePath in filePathsList)
                {
                    string fileOnly = System.IO.Path.GetFileName(filePath);
                    if (fileOnly.ToLower() == "thumbs.db")
                        continue;
                    sb.Append(fileOnly + "\n");
                }

                // Add folders
                string[] folders = Directory.GetDirectories(folder);
                List<string> folderList = folders.ToList<string>();
                folderList.Sort();
                if (folderList.Count > 0)
                    sb.Append("Folders ...............................\n");
                foreach (string folderPath in folderList)
                {
                    sb.Append(folderPath + "\n");
                }
            }
            else
            {
                sb.Append("Folder doesn't currently exist ... " + folder);
            }
            return sb.ToString();
        }
    }
}
