using MahApps.Metro.Controls;
using System;
using System.Collections.Generic;
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
    /// Interaction logic for SettingsView.xaml
    /// </summary>
    public partial class SettingsView : MetroWindow
    {
        public SettingsView()
        {
            InitializeComponent();
        }

        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void btnOK_Click(object sender, RoutedEventArgs e)
        {
            int selIdx = comboListOrder.SelectedIndex;
            Properties.Settings.Default.UnfiledDocListOrder = ((ComboBoxItem)comboListOrder.SelectedItem).Tag.ToString();
            Properties.Settings.Default.Save();
            Close();
        }

        private void SettingsView_Loaded(object sender, RoutedEventArgs e)
        {
            bool cbiSet = false;
            foreach (ComboBoxItem cbi in comboListOrder.Items)
                if (cbi.Tag.ToString() == Properties.Settings.Default.UnfiledDocListOrder)
                {
                    comboListOrder.SelectedItem = cbi;
                    cbiSet = true;
                    break;
                }
            if (!cbiSet)
                comboListOrder.SelectedIndex = 0;
        }


    }
}
