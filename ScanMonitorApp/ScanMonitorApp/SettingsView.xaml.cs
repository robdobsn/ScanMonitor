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
        private WindowClosingDelegate _windowClosingCB;

        public SettingsView(WindowClosingDelegate windowClosingCB)
        {
            InitializeComponent();
            _windowClosingCB = windowClosingCB;
        }

        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void btnOK_Click(object sender, RoutedEventArgs e)
        {
            int selIdx = comboListOrder.SelectedIndex;
            Properties.Settings.Default.UnfiledDocListOrder = ((ComboBoxItem)comboListOrder.SelectedItem).Tag.ToString();
            // Removed save to defaults so that we always go back to original order
//            Properties.Settings.Default.Save();
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

        private void SettingsView_Closed(object sender, EventArgs e)
        {
            _windowClosingCB();
        }
    }
}
