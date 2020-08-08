using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Permissions;
using System.Text;
using System.Threading.Tasks;
using NLog;

namespace ScanMonitorApp
{
    class ScanFolderWatcher
    {
        private static Logger logger = LogManager.GetCurrentClassLogger();
        public delegate void CallbackDelegate(string fileName, WatcherChangeTypes changeType);
        CallbackDelegate _callbackOnChanged;

        [PermissionSet(SecurityAction.Demand, Name = "FullTrust")]
        public bool WatchFolder(string folder, CallbackDelegate callbackOnChanged)
        {
            _callbackOnChanged = callbackOnChanged;

            // Create a new FileSystemWatcher and set its properties.
            FileSystemWatcher watcher = new FileSystemWatcher();

            // Watch the folder
            try
            {
                watcher.Path = folder;
                /* Watch for changes in LastAccess and LastWrite times, and
                   the renaming of files or directories. */
                watcher.NotifyFilter = NotifyFilters.LastAccess | NotifyFilters.LastWrite
                   | NotifyFilters.FileName | NotifyFilters.DirectoryName;

                // Only watch pdf files.
                watcher.Filter = "*.pdf";

                // Add event handlers.
                watcher.Changed += new FileSystemEventHandler(OnChanged);
                watcher.Created += new FileSystemEventHandler(OnCreated);
                watcher.Renamed += new RenamedEventHandler(OnRenamed);

                // Begin watching.
                watcher.EnableRaisingEvents = true;
            }
            catch (Exception excp)
            {
                logger.Error("Failed to watch {0}, excp {1}", folder, excp.Message);
                return false;
            }
            return true;
        }

        // Define the event handlers. 
        private void OnCreated(object source, FileSystemEventArgs e)
        {
            logger.Debug("File: " + e.FullPath + " " + e.ChangeType.ToString() + " this event will be ignored");
            //Debug.Assert(false);
//            _callbackOnChanged(e.FullPath, e.ChangeType);
        }

        private void OnChanged(object source, FileSystemEventArgs e)
        {
            logger.Debug("File: " + e.FullPath + " " + e.ChangeType.ToString() + " this event will be acted upon");
            //Debug.Assert(false);
            _callbackOnChanged(e.FullPath, e.ChangeType);
        }

        private void OnRenamed(object source, RenamedEventArgs e)
        {
            logger.Debug("File: " + e.FullPath + " " + e.ChangeType.ToString() + " this event will be ignored");
            //Debug.Assert(false);
            //_callbackOnChanged(e.FullPath, e.ChangeType);
        }

    }
}
