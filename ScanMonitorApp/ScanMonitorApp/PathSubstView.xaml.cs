using NLog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
    /// Interaction logic for PathSubstView.xaml
    /// </summary>
    public partial class PathSubstView : Window
    {
        private static Logger logger = LogManager.GetCurrentClassLogger();
        private DocTypesMatcher _docTypesMatcher;
        private PathSubstMacro _curSelPathSubstMacro;
//        private ObservableCollection<SubstText> pathSubstObs = new ObservableCollection<SubstTexts>();

        public PathSubstView(DocTypesMatcher docTypesMatcher)
        {
            InitializeComponent();
            _docTypesMatcher = docTypesMatcher;

            PopulateGrid();

            SetInitialFieldEnables();
        }

        public void PopulateGrid()
        {

            List<PathSubstMacro> pathSubMacros = _docTypesMatcher.ListPathSubstMacros();
            Binding bind = new Binding();
            listMacroReplacements.DataContext = pathSubMacros;
            listMacroReplacements.SetBinding(ListView.ItemsSourceProperty, bind);

//MongoCollection<TestUserDocument> collection = database.GetCollection<TestUserDocument>("testuser");
//var results = collection.FindAll();
//List<TestUserDocument> resultList = results.ToList<TestUserDocument>();
//// Bind result data to WPF view.
//if (resultList.Count() > 0)
//{
//Binding bind = new Binding();
//TestListView.DataContext = resultList;
//TestListView.SetBinding(ListView.ItemsSourceProperty, bind);
//}
        }

        private void SetInitialFieldEnables()
        {
            txtOrigText.IsEnabled = false;
            txtReplaceWith.IsEnabled = false;
            listMacroReplacements.IsEnabled = true;
            btnCancelMacro.IsEnabled = false;
            btnEditMacro.IsEnabled = false;
            btnNewMacro.IsEnabled = true;
            btnSaveMacro.IsEnabled = false;
            btnDeleteMacro.IsEnabled = false;
            listMacroReplacements.SelectedItem = null;
            _curSelPathSubstMacro = null;
            txtOrigText.Text = "";
            txtReplaceWith.Text = "";
        }

        private void listMacroReplacements_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Get the corresponding doc type to display
            if (e.AddedItems != null && e.AddedItems.Count > 0)
            {
                PathSubstMacro ptm = e.AddedItems[0] as PathSubstMacro;
                txtOrigText.Text = ptm.origText;
                txtReplaceWith.Text = ptm.replaceText;
                _curSelPathSubstMacro = ptm;
                btnDeleteMacro.IsEnabled = true;
                btnEditMacro.IsEnabled = true;
            }
            else
            {
                _curSelPathSubstMacro = null;
            }

        }

        private void btnNewMacro_Click(object sender, RoutedEventArgs e)
        {
            txtOrigText.IsEnabled = true;
            txtOrigText.Text = "";
            txtReplaceWith.IsEnabled = true;
            txtReplaceWith.Text = "";
            listMacroReplacements.IsEnabled = false;
            btnCancelMacro.IsEnabled = true;
            btnEditMacro.IsEnabled = false;
            btnNewMacro.IsEnabled = false;
            btnSaveMacro.IsEnabled = true;
            btnDeleteMacro.IsEnabled = false;
            _curSelPathSubstMacro = new PathSubstMacro();
            listMacroReplacements.SelectedItem = null;
        }

        private void btnEditMacro_Click(object sender, RoutedEventArgs e)
        {
            if (_curSelPathSubstMacro != null)
            {
                txtOrigText.IsEnabled = true;
                txtReplaceWith.IsEnabled = true;
                listMacroReplacements.IsEnabled = false;
                btnCancelMacro.IsEnabled = true;
                btnEditMacro.IsEnabled = false;
                btnNewMacro.IsEnabled = false;
                btnSaveMacro.IsEnabled = true;
                btnDeleteMacro.IsEnabled = false;
            }
        }

        private void btnCancelMacro_Click(object sender, RoutedEventArgs e)
        {
            SetInitialFieldEnables();
        }

        private void btnSaveMacro_Click(object sender, RoutedEventArgs e)
        {
            if (_curSelPathSubstMacro != null)
            {
                _curSelPathSubstMacro.origText = txtOrigText.Text;
                _curSelPathSubstMacro.replaceText = txtReplaceWith.Text;
                _docTypesMatcher.AddOrUpdateSubstMacroRecInDb(_curSelPathSubstMacro);
            }

            PopulateGrid();
            SetInitialFieldEnables();
        }

        private void btnDeleteMacro_Click(object sender, RoutedEventArgs e)
        {
            if (_curSelPathSubstMacro != null)
                _docTypesMatcher.DeletePathSubstMacro(_curSelPathSubstMacro);
            PopulateGrid();
            SetInitialFieldEnables();
        }


    }
}
