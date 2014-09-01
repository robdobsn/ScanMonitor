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
        const int MAX_FILES_TO_SHOW = 50;
        const int MAX_FOLDERS_TO_SHOW = 10;
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
                int fileIdx = 0;
                foreach (string filePath in filePathsList)
                {
                    string fileOnly = System.IO.Path.GetFileName(filePath);
                    if (fileOnly.ToLower() == "thumbs.db")
                        continue;
                    sb.Append(fileOnly + "\n");
                    fileIdx++;
                    if (fileIdx > MAX_FILES_TO_SHOW)
                    {
                        sb.Append(String.Format("...... {0} more found", filePathsList.Count - MAX_FILES_TO_SHOW));
                        break;
                    }
                }

                // Add folders
                string[] folders = Directory.GetDirectories(folder);
                List<string> folderList = folders.ToList<string>();
                folderList.Sort();
                if (folderList.Count > 0)
                    sb.Append("Folders ...............................\n");
                int folderIdx = 0;
                foreach (string folderPath in folderList)
                {
                    sb.Append(folderPath + "\n");
                    folderIdx++;
                    if (folderIdx > MAX_FOLDERS_TO_SHOW)
                    {
                        sb.Append(String.Format("...... {0} more found", folderList.Count - MAX_FOLDERS_TO_SHOW));
                        break;
                    }
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
