﻿//------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by a tool.
//     Runtime Version:4.0.30319.18444
//
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------

namespace ScanMonitorApp.Properties {
    
    
    [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute()]
    [global::System.CodeDom.Compiler.GeneratedCodeAttribute("Microsoft.VisualStudio.Editors.SettingsDesigner.SettingsSingleFileGenerator", "12.0.0.0")]
    internal sealed partial class Settings : global::System.Configuration.ApplicationSettingsBase {
        
        private static Settings defaultInstance = ((Settings)(global::System.Configuration.ApplicationSettingsBase.Synchronized(new Settings())));
        
        public static Settings Default {
            get {
                return defaultInstance;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("\\\\SCAN1\\Users\\Rob\\Documents\\ScanSnap")]
        public string FolderToMonitor {
            get {
                return ((string)(this["FolderToMonitor"]));
            }
            set {
                this["FolderToMonitor"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("\\\\MACALLAN\\Main\\PendingFiling\\Scans")]
        public string PendingDocFolder {
            get {
                return ((string)(this["PendingDocFolder"]));
            }
            set {
                this["PendingDocFolder"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("\\\\MACALLAN\\Main\\PendingFiling\\TempScanInfo")]
        public string PendingTmpFolder {
            get {
                return ((string)(this["PendingTmpFolder"]));
            }
            set {
                this["PendingTmpFolder"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("10")]
        public int MaxPagesForImages {
            get {
                return ((int)(this["MaxPagesForImages"]));
            }
            set {
                this["MaxPagesForImages"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("10")]
        public int MaxPagesForText {
            get {
                return ((int)(this["MaxPagesForText"]));
            }
            set {
                this["MaxPagesForText"] = value;
            }
        }
        
        [global::System.Configuration.ApplicationScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("ScanManager")]
        public string DbNameForDocs {
            get {
                return ((string)(this["DbNameForDocs"]));
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("ScanDocInfo")]
        public string DbCollectionForDocs {
            get {
                return ((string)(this["DbCollectionForDocs"]));
            }
            set {
                this["DbCollectionForDocs"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("DocTypes")]
        public string DbCollectionForDocTypes {
            get {
                return ((string)(this["DbCollectionForDocTypes"]));
            }
            set {
                this["DbCollectionForDocTypes"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("")]
        public string TestFolderToMonitor {
            get {
                return ((string)(this["TestFolderToMonitor"]));
            }
            set {
                this["TestFolderToMonitor"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("C:\\Users\\Rob\\Documents\\20140209 Train\\Scanning\\Pending")]
        public string TestPendingDocFolder {
            get {
                return ((string)(this["TestPendingDocFolder"]));
            }
            set {
                this["TestPendingDocFolder"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("C:\\Users\\Rob\\Documents\\20140209 Train\\Scanning\\PendingImgs")]
        public string TestPendingTmpFolder {
            get {
                return ((string)(this["TestPendingTmpFolder"]));
            }
            set {
                this["TestPendingTmpFolder"] = value;
            }
        }
    }
}
