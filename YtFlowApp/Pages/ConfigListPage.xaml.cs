﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using YtFlow.App.Utils;
using YtFlow.Tunnel.Config;

// https://go.microsoft.com/fwlink/?LinkId=234238 上介绍了“空白页”项模板

namespace YtFlow.App.Pages
{
    /// <summary>
    /// 可用于自身或导航至 Frame 内部的空白页。
    /// </summary>
    public sealed partial class ConfigListPage : Page
    {
        public ObservableCollection<IAdapterConfig> adapterConfigs { get; set; } = new ObservableCollection<IAdapterConfig>();

        public ConfigListPage ()
        {
            this.InitializeComponent();
        }

        private async Task LoadAdapterConfigs ()
        {
            loadProgressBar.IsIndeterminate = true;
            loadProgressBar.Visibility = Visibility.Visible;
            try
            {
                var directory = await ConfigUtils.GetAdapterConfigDirectoryAsync();
                var files = await directory.GetFilesAsync();
                adapterConfigs.Clear();
                foreach (var config in files.Select(f => AdapterConfig.GetConfigFromFilePath(f.Path)))
                {
                    adapterConfigs.Add(config);
                }
            }
            catch (Exception ex)
            {
                await UiUtils.NotifyUser("Error loading config list: " + ex.ToString());
                Frame.GoBack();
            }
            finally
            {
                loadProgressBar.Visibility = Visibility.Collapsed;
            }
        }

        protected override async void OnNavigatedTo (NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            await LoadAdapterConfigs();
        }

        private void AdapterListView_ItemClick (object sender, ItemClickEventArgs e)
        {
            Frame.Navigate(typeof(ConfigPage), e.ClickedItem);
        }

        private async void SetDefaultButton_Click (object sender, RoutedEventArgs e)
        {
            var btn = (MenuFlyoutItem)sender;
            var defaultConfig = (IAdapterConfig)btn.DataContext;
            AdapterConfig.SetDefaultConfigFilePath(defaultConfig.Path);
            await LoadAdapterConfigs();
        }

        private async void RemoveButton_Click (object sender, RoutedEventArgs e)
        {
            var btn = (MenuFlyoutItem)sender;

            IList<object> configs;
            ContentDialogResult result;
            if (configList.SelectedItems.Count == 0)
            {
                var config = (IAdapterConfig)((FrameworkElement)sender).DataContext;
                configs = new List<object>() { config };
                (_, result) = await UiUtils.NotifyUser("Remove this config?", primaryCommandText: "Yes");
            }
            else
            {
                configs = configList.SelectedItems;
                (_, result) = await UiUtils.NotifyUser($"Remove {configList.SelectedItems.Count} configs?", primaryCommandText: "Yes");
            }
            if (result != ContentDialogResult.Primary)
            {
                return;
            }
            var deletedConfigs = new List<IAdapterConfig>(configs.Count);
            loadProgressBar.IsIndeterminate = false;
            loadProgressBar.Value = 0;
            loadProgressBar.Visibility = Visibility.Visible;
            try
            {
                var defaultConfigPath = AdapterConfig.GetDefaultConfigFilePath();
                var tasks = configs.Select(async obj =>
                {
                    var config = (IAdapterConfig)obj;
                    var file = await StorageFile.GetFileFromPathAsync(config.Path);
                    await file.DeleteAsync();
                    deletedConfigs.Add(config);
                    if (config.Path == defaultConfigPath)
                    {
                        AdapterConfig.ClearDefaultConfigFilePath();
                    }
                    loadProgressBar.Value = (double)deletedConfigs.Count / configs.Count;
                });
                await Task.WhenAll(tasks);
            }
            catch (Exception ex)
            {
                await UiUtils.NotifyUser("Error while deleting config file: " + ex.ToString());
            }
            finally
            {
                loadProgressBar.IsIndeterminate = true;
                loadProgressBar.Visibility = Visibility.Collapsed;
                if (deletedConfigs.Count == adapterConfigs.Count)
                {
                    adapterConfigs.Clear();
                }
                else
                {
                    foreach (var deleted in deletedConfigs)
                    {
                        adapterConfigs.Remove(deleted);
                    }
                }
            }
        }

        private void CreateShadowsocksButton_Click (object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(ConfigPage), new ShadowsocksConfig()
            {
                ServerHost = "la.maxlv.net",
                ServerPort = 1080,
                Password = "CCCCCCCC",
                Method = "aes-128-cfb",
                Name = "Connection " + (adapterConfigs.Count + 1)
            });
        }

        private void CreateHttpButton_Click (object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(ConfigPage), new HttpConfig()
            {
                ServerHost = "127.0.0.1",
                ServerPort = 80,
                Name = "Connection " + (adapterConfigs.Count + 1)
            });
        }

        private void CreateTrojanButton_Click (object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(ConfigPage), new TrojanConfig()
            {
                ServerHost = "example.com",
                ServerPort = 443,
                Name = "Connection " + (adapterConfigs.Count + 1),
                Password = "password1"
            });
        }

        private void QrCodeScanButton_Click (object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(QrCodeScannerPage));
        }

        private async void ClipboardPasteButton_Click (object sender, RoutedEventArgs e)
        {
            var content = Clipboard.GetContent();
            if (!content.Contains(StandardDataFormats.Text))
            {
                await UiUtils.NotifyUser("No text in Clipboard");
            }
            configList.IsEnabled = false;
            loadProgressBar.IsIndeterminate = false;
            loadProgressBar.Value = 0;
            loadProgressBar.Visibility = Visibility.Visible;
            try
            {
                var text = await content.GetTextAsync();
                var result = await ConfigUtils.ImportLinksAsync(text, p => loadProgressBar.Value = p);
                var notifyTask = UiUtils.NotifyUser(result.GenerateMessage());
                // Complete updates in background
                _ = result.Files.BatchCompleteUpdates();
                await LoadAdapterConfigs();
                await notifyTask;
            }
            finally
            {
                loadProgressBar.IsIndeterminate = true;
                loadProgressBar.Visibility = Visibility.Collapsed;
                configList.IsEnabled = true;
            }
        }

        private void SelectButton_Click (object sender, RoutedEventArgs e)
        {
            Frame.IsNavigationStackEnabled = false;
            configList.SelectionMode = ListViewSelectionMode.Extended;
            configList.IsItemClickEnabled = false;
            selectAllButton.Visibility = Visibility.Visible;
            selectButton.Visibility = Visibility.Collapsed;
            SystemNavigationManager.GetForCurrentView().BackRequested += MultiSelect_BackRequest;
            MainPage.OverrideGlobalBackNavigation = true;
        }

        private void MultiSelect_BackRequest (object sender, BackRequestedEventArgs e)
        {
            if (e.Handled)
            {
                return;
            }
            e.Handled = true;
            SystemNavigationManager.GetForCurrentView().BackRequested -= MultiSelect_BackRequest;
            configList.SelectionMode = ListViewSelectionMode.None;
            configList.IsItemClickEnabled = true;
            selectAllButton.Visibility = Visibility.Collapsed;
            selectButton.Visibility = Visibility.Visible;
        }

        private void SelectAllButton_Click (object sender, RoutedEventArgs e)
        {
            if (configList.SelectedItems.Count == configList.Items.Count)
            {
                configList.SelectedItems.Clear();
            }
            else
            {
                configList.SelectAll();
            }
        }
    }
}
