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
        [global::System.Configuration.DefaultSettingValueAttribute("ScanDocInfo")]
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
        [global::System.Configuration.DefaultSettingValueAttribute("ScanDocPages")]
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
        [global::System.Configuration.DefaultSettingValueAttribute("FiledDocInfo")]
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
        [global::System.Configuration.DefaultSettingValueAttribute("FRACTAL")]
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
        [global::System.Configuration.DefaultSettingValueAttribute("mongodb://macallan")]
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
    }
}
