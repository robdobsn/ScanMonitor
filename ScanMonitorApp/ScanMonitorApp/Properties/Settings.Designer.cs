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
    [global::System.CodeDom.Compiler.GeneratedCodeAttribute("Microsoft.VisualStudio.Editors.SettingsDesigner.SettingsSingleFileGenerator", "11.0.0.0")]
    internal sealed partial class Settings : global::System.Configuration.ApplicationSettingsBase {
        
        private static Settings defaultInstance = ((Settings)(global::System.Configuration.ApplicationSettingsBase.Synchronized(new Settings())));
        
        public static Settings Default {
            get {
                return defaultInstance;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("\\\\SCAN2\\Users\\Rob\\Documents\\ScanSnap")]
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
        [global::System.Configuration.DefaultSettingValueAttribute("\\\\MACALLAN\\Admin\\ScanAdmin\\ScannedDocImgs")]
        public string DocAdminImgFolderBase {
            get {
                return ((string)(this["DocAdminImgFolderBase"]));
            }
            set {
                this["DocAdminImgFolderBase"] = value;
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
        [global::System.Configuration.DefaultSettingValueAttribute("ScanDocInfoTEST")]
        public string DbCollectionForDocInfo {
            get {
                return ((string)(this["DbCollectionForDocInfo"]));
            }
            set {
                this["DbCollectionForDocInfo"] = value;
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
        [global::System.Configuration.DefaultSettingValueAttribute("ScanDocPagesTEST")]
        public string DbCollectionForDocPages {
            get {
                return ((string)(this["DbCollectionForDocPages"]));
            }
            set {
                this["DbCollectionForDocPages"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("FiledDocInfoTEST")]
        public string DbCollectionForFiledDocs {
            get {
                return ((string)(this["DbCollectionForFiledDocs"]));
            }
            set {
                this["DbCollectionForFiledDocs"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("150")]
        public int PickThumbHeight {
            get {
                return ((int)(this["PickThumbHeight"]));
            }
            set {
                this["PickThumbHeight"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("SCAN2")]
        public string PCtoRunMonitorOn {
            get {
                return ((string)(this["PCtoRunMonitorOn"]));
            }
            set {
                this["PCtoRunMonitorOn"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("[PREFIX] [Y-M-D] [SUBJECT]")]
        public string DefaultRenameTo {
            get {
                return ((string)(this["DefaultRenameTo"]));
            }
            set {
                this["DefaultRenameTo"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("DocTypePathMacros")]
        public string DbCollectionForPathMacros {
            get {
                return ((string)(this["DbCollectionForPathMacros"]));
            }
            set {
                this["DbCollectionForPathMacros"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("mongodb://macallan/")]
        public string DbConnectionString {
            get {
                return ((string)(this["DbConnectionString"]));
            }
            set {
                this["DbConnectionString"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("\\\\MACALLAN\\Main\\RobAndJudyPersonal")]
        public string BasePathForFilingFolderSelection {
            get {
                return ((string)(this["BasePathForFilingFolderSelection"]));
            }
            set {
                this["BasePathForFilingFolderSelection"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("\\\\macallan\\main\\RobAndJudyPersonal\\IT\\Scanning\\rules.xml")]
        public string OldRulesFile {
            get {
                return ((string)(this["OldRulesFile"]));
            }
            set {
                this["OldRulesFile"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("\\\\N7700PRO\\Archive\\ScanAdmin\\ScanLogs\\ScanLog.log")]
        public string OldScanLogFile {
            get {
                return ((string)(this["OldScanLogFile"]));
            }
            set {
                this["OldScanLogFile"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("\\\\MACALLAN\\Admin\\ScanAdmin\\ScanDocBackups")]
        public string DocArchiveFolder {
            get {
                return ((string)(this["DocArchiveFolder"]));
            }
            set {
                this["DocArchiveFolder"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("smtp.gmail.com")]
        public string EmailService {
            get {
                return ((string)(this["EmailService"]));
            }
            set {
                this["EmailService"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("587")]
        public string EmailServicePort {
            get {
                return ((string)(this["EmailServicePort"]));
            }
            set {
                this["EmailServicePort"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("Scan Filer <rob@robdobson.com>")]
        public string EmailFrom {
            get {
                return ((string)(this["EmailFrom"]));
            }
            set {
                this["EmailFrom"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("Rob Dobson<rob@dobson.com>, Judy Wilson <judyw@marketry.co.uk>")]
        public string EmailTo {
            get {
                return ((string)(this["EmailTo"]));
            }
            set {
                this["EmailTo"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("")]
        public string TestModeFileTo {
            get {
                return ((string)(this["TestModeFileTo"]));
            }
            set {
                this["TestModeFileTo"] = value;
            }
        }
    }
}
