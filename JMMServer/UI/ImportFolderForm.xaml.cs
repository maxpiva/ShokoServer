﻿using System;
using System.IO;
using System.Linq;
using System.Windows;
using JMMContracts;
using JMMServer.Entities;

//using System.Windows.Media;
//using System.Windows.Media.Imaging;
//using System.Windows.Shapes;

namespace JMMServer
{
    /// <summary>
    /// Interaction logic for ImportFolder.xaml
    /// </summary>
    public partial class ImportFolderForm : Window
    {
        private ImportFolder importFldr = null;

        public ImportFolderForm()
        {
            InitializeComponent();

            btnCancel.Click += new RoutedEventHandler(btnCancel_Click);
            btnSave.Click += new RoutedEventHandler(btnSave_Click);
            btnChooseFolder.Click += new RoutedEventHandler(btnChooseFolder_Click);
            comboProvider.SelectionChanged += ComboProvider_SelectionChanged;
        }

        private void ComboProvider_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (comboProvider.SelectedIndex < 0)
                return;
            if (comboProvider.SelectedIndex == 0)
                importFldr.CloudID = null;
            else
                importFldr.CloudID = ((CloudAccount)comboProvider.SelectedItem).CloudID;
        }

        void btnChooseFolder_Click(object sender, RoutedEventArgs e)
        {
            if (comboProvider.SelectedIndex == 0)
            {
                System.Windows.Forms.FolderBrowserDialog dialog = new System.Windows.Forms.FolderBrowserDialog();

                if (!string.IsNullOrEmpty(txtImportFolderLocation.Text) &&
                    Directory.Exists(txtImportFolderLocation.Text))
                    dialog.SelectedPath = txtImportFolderLocation.Text;

                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    txtImportFolderLocation.Text = dialog.SelectedPath;
                }
            }
            else
            {
                CloudFolderBrowser frm=new CloudFolderBrowser();
                frm.Owner = this;
                frm.Init(importFldr);
                bool? result=frm.ShowDialog();
                if (result.HasValue && result.Value)
                    txtImportFolderLocation.Text = frm.SelectedPath;
            }
        }

        void btnSave_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // An import folder cannot be both the drop source and the drop destination
                if (chkDropDestination.IsChecked.HasValue && chkDropSource.IsChecked.HasValue &&
                    chkDropDestination.IsChecked.Value &&
                    chkDropSource.IsChecked.Value)
                {
                    MessageBox.Show(JMMServer.Properties.Resources.ImportFolders_SameFolder,
                        JMMServer.Properties.Resources.Error,
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // The import folder location cannot be blank. Enter a valid path on OMM Server
                if (string.IsNullOrEmpty(txtImportFolderLocation.Text))
                {
                    MessageBox.Show(JMMServer.Properties.Resources.ImportFolders_BlankImport,
                        JMMServer.Properties.Resources.Error,
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    txtImportFolderLocation.Focus();
                    return;
                }

                Contract_ImportFolder contract = new Contract_ImportFolder();
                if (importFldr.ImportFolderID == 0)
                    contract.ImportFolderID = null;
                else
                    contract.ImportFolderID = importFldr.ImportFolderID;
                contract.ImportFolderType = (int)(importFldr.CloudID.HasValue ? ImportFolderType.Cloud : ImportFolderType.HDD);
                contract.ImportFolderName = "NA";
                contract.ImportFolderLocation = txtImportFolderLocation.Text.Trim();
                contract.IsDropDestination = chkDropDestination.IsChecked.Value ? 1 : 0;
                contract.IsDropSource = chkDropSource.IsChecked.Value ? 1 : 0;
                contract.IsWatched = chkIsWatched.IsChecked.Value ? 1 : 0;
                if (comboProvider.SelectedIndex == 0)
                    contract.CloudID = null;
                else
                    contract.CloudID = ((CloudAccount) comboProvider.SelectedItem).CloudID;
                JMMServiceImplementation imp = new JMMServiceImplementation();
                Contract_ImportFolder_SaveResponse response = imp.SaveImportFolder(contract);
                if (!string.IsNullOrEmpty(response.ErrorMessage))
                    MessageBox.Show(response.ErrorMessage, JMMServer.Properties.Resources.Error, MessageBoxButton.OK,
                        MessageBoxImage.Error);

                ServerInfo.Instance.RefreshImportFolders();
            }
            catch (Exception ex)
            {
                Utils.ShowErrorMessage(ex);
            }

            this.DialogResult = true;
            this.Close();
        }

        void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }

        public void Init(ImportFolder ifldr)
        {
            try
            {
                importFldr = ifldr;
                ServerInfo.Instance.RefreshFolderProviders();
                txtImportFolderLocation.Text = importFldr.ImportFolderLocation;
                chkDropDestination.IsChecked = importFldr.IsDropDestination == 1;
                chkDropSource.IsChecked = importFldr.IsDropSource == 1;
                chkIsWatched.IsChecked = importFldr.IsWatched == 1;
                if (ifldr.CloudID.HasValue)
                    comboProvider.SelectedItem = ServerInfo.Instance.CloudAccounts.FirstOrDefault(a => a.CloudID == ifldr.CloudID.Value);
                else
                    comboProvider.SelectedIndex = 0;
                txtImportFolderLocation.Focus();
            }
            catch (Exception ex)
            {
                Utils.ShowErrorMessage(ex);
            }
        }
    }
}