using CleanSpaceShared.Events;
using CleanSpaceShared.Logging;
using CleanSpaceShared.Struct;
using CleanSpaceTorch.Util;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using VRage.Compression;

namespace CleanSpaceTorch
{
    // ReSharper disable once UnusedType.Global
    // ReSharper disable once RedundantExtendsListEntry
    public partial class ConfigView : UserControl
    {
        public static ConfigView Instance;
        public static IPluginLogger Log;
        public ConfigView()
        {
            Instance = this;
            InitializeComponent();
            DataContext = CleanSpaceTorchPlugin.Instance.Config;
            PluginHashGrid.ItemsSource = CleanSpaceTorchPlugin.Instance.Config.AnalyzedPlugins;
            
            Instance.PluginListModeCombo.SelectedIndex = System.Enum.GetNames(typeof(PluginListType)).ToList().IndexOf(CleanSpaceTorchPlugin.Instance.Config.PluginListType.ToString());
            Instance.PluginMatchActionCombo.SelectedIndex = System.Enum.GetNames(typeof(ListMatchAction)).ToList().IndexOf(CleanSpaceTorchPlugin.Instance.Config.ListMatchAction.ToString());
           
        }


        private void PluginDropArea_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                bool hasValidExtension = files.Any(file =>
                {
                    string ext = Path.GetExtension(file)?.ToLower();
                    return validFormats.Contains(ext);
                });

                e.Effects = hasValidExtension ? DragDropEffects.Copy : DragDropEffects.None;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }

            e.Handled = true;
        }

        private string[] validFormats = new string[] { ".zip" , ".dll"};

        private static bool AddPluginToPluginList(string file)
        {
            if (ServerUtil.IsValidPlugin(file))
            {
                Assembly a = Assembly.LoadFile(file);
                EventHub.OnCleanSpaceServerScannedPlugin(typeof(ConfigView), file, a);                
                Instance.PluginHashGrid.ItemsSource = CleanSpaceTorchPlugin.Instance.Config.AnalyzedPlugins;
                return true;
            }
            else
            {
                MessageBox.Show("Not a valid plugin library. Only plugins that contain a class compatible VRage's IPlugin interface are valid here.");
            }
            return false;
        }

        private void PluginDropArea_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                var validFiles = files.Where((file) =>
                {
                    return validFormats.Contains(Path.GetExtension(file).ToLower());
                }).ToArray();

                if (validFiles.Length == 0)
                {
                    Log.Debug("No valid files were dropped into the box.");
                    MessageBox.Show(
                        "None of the files you dropped are valid '.zip' archives containing the appropriate plugin assembly information. " +
                        "See the documentation included on the security tab for more information.",
                        "Invalid Files",
                        MessageBoxButton.OK, MessageBoxImage.Warning                       
                    );
                    return;
                }

                // Determine if any DLLs are present (either directly or within zips)
                bool containsDlls = false;
                foreach (var file in validFiles)
                {
                    if (Path.GetExtension(file).ToLower() == ".zip")
                    {
                        using (var archive = MyZipArchive.OpenOnFile(file))
                        {
                            if (archive.FileNames.Any(n => n.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)))
                            {
                                containsDlls = true;
                                break;
                            }
                        }
                    }
                    else if (file.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    {
                        containsDlls = true;
                        break;
                    }
                }

                if (containsDlls)
                {
                    var response = MessageBox.Show(
                        "Warning: One or more of the files you dropped appear to contain DLLs (which may or may not be plugins). In certain situations, these can execute code on your machine.\n\n" +
                        "Only continue if you trust the source (Have any enemies with an engineering department?). Do you want to proceed?",
                        "Security Warning",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning
                    );

                    if (response != MessageBoxResult.Yes)
                    {
                        Log.Debug("User declined to load plugin DLLs after warning.");
                        return;
                    }
                }

                Log.Debug($"Analyzing {validFiles.Length} archives...");

                foreach (string file in validFiles)
                {
                    if (!File.Exists(file))
                    {
                        Log.Error($"Valid filename {file} did not actually exist on the filesystem...");
                        continue;
                    }

                    if (Path.GetExtension(file).ToLower() == ".zip")
                    {
                        using (var archive = MyZipArchive.OpenOnFile(file))
                        {
                            var filesList = archive.FileNames
                                .Where(n => n.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                                .ToList();

                            Log.Info($"Adding plugin information from {file}...");
                            filesList.ForEach(containedFile => {

                                Log.Info($"Plugin class found in {containedFile}...");
                                AddPluginToPluginList(containedFile);
                                });
                        }
                    }
                    else
                    {
                        AddPluginToPluginList(file);
                    }
                }
            }
        }

        private void configPanelLoaded(object sender, RoutedEventArgs e)
        {

        }

        private void PluginDropArea_DragLeave(object sender, DragEventArgs e)
        {

        }

        private void SeedPasswordResetBtn_Click(object sender, RoutedEventArgs e)
        {

        }

        private void CheckBox_Checked(object sender, RoutedEventArgs e)
        {
           // MessageBox.Show("farts");
            //  Common.Plugin.Config.AnalyzedPlugins.
        }

        private void CheckBox_Unchecked(object sender, RoutedEventArgs e)
        {

        }

        private void CheckBox_TargetUpdated(object sender, System.Windows.Data.DataTransferEventArgs e)
        {

            MessageBox.Show("farts");
        }
    }
}