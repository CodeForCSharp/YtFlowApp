﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Markup;
using Windows.UI.Xaml.Navigation;
using YtFlow.App.Models;
using YtFlow.App.Utils;
using YtFlow.Tasks.Hosted;
using YtFlow.Tasks.Hosted.Format;
using YtFlow.Tasks.Hosted.Source;

// https://go.microsoft.com/fwlink/?LinkId=234238 上介绍了“空白页”项模板

namespace YtFlow.App.Pages
{
    [DefaultMember("Item")]
    public class NewHostedConfigTypeGroupCollection : Collection<NewHostedConfigTypeGroup>
    {
    }
    [ContentProperty(Name = "Items")]
    public class NewHostedConfigTypeGroup
    {
        public string Title { get; set; }
        public List<NewHostedConfigType> Items { get; set; } = new List<NewHostedConfigType>();
    }
    public class NewHostedConfigType
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
    }

    /// <summary>
    /// 可用于自身或导航至 Frame 内部的空白页。
    /// </summary>
    public sealed partial class NewHostedConfigPage : Page
    {
        private CancellationTokenSource LoadSnapshotCancellationSource;
        private ObservableCollection<HostedConfigListItem> HostedConfigListItems;
        public NewHostedConfigPage ()
        {
            this.InitializeComponent();
        }

        protected override void OnNavigatedTo (NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            LoadSnapshotCancellationSource = new CancellationTokenSource();
            HostedConfigListItems = (ObservableCollection<HostedConfigListItem>)e.Parameter;
        }

        protected override void OnNavigatingFrom (NavigatingCancelEventArgs e)
        {
            base.OnNavigatingFrom(e);

            LoadSnapshotCancellationSource.Cancel();
            LoadSnapshotCancellationSource.Dispose();
        }

        private async Task AddSubscribeAsync ()
        {
            var cancellationToken = LoadSnapshotCancellationSource.Token;
            var config = new HostedConfig();
            IInputStream stream;
            // Create source
            switch (sourcePivot.SelectedIndex)
            {
                case 0:
                    // URL
                    if (string.IsNullOrWhiteSpace(urlText.Text))
                    {
                        throw new OperationCanceledException("Empty input");
                    }
                    if (!Uri.IsWellFormedUriString(urlText.Text, UriKind.Absolute))
                    {
                        throw new InvalidDataException("The URL is invalid");
                    }
                    var urlSource = new UrlSource()
                    {
                        Url = urlText.Text
                    };
                    config.Source = urlSource;
                    stream = await config.Source.FetchAsync().AsTask(cancellationToken);
                    break;
                default:
                    throw new NotSupportedException("The source type is not supported yet");
            }
            Snapshot snapshot;
            // Create format
            cancellationToken.ThrowIfCancellationRequested();
            switch (((NewHostedConfigType)hostedTypeGridView.SelectedItem).Id)
            {
                case "ssd":
                    var ssd = new Ssd();
                    config.Format = ssd;
                    snapshot = await ssd.DecodeAsync(stream).AsTask(cancellationToken);
                    break;
                case "clash":
                    var clash = new Clash();
                    config.Format = clash;
                    snapshot = await clash.DecodeAsync(stream).AsTask(cancellationToken);
                    break;
                default:
                    throw new NotSupportedException("The hosted config type is not supported yet");
            }
            config.Name = config.Source.GetFileName();

            // Save
            cancellationToken.ThrowIfCancellationRequested();
            var configFile = await HostedUtils.SaveHostedConfigAsync(config);
            try
            {
                _ = CachedFileManager.CompleteUpdatesAsync(configFile);
                cancellationToken.ThrowIfCancellationRequested();
                // No need to update local file
                _ = await HostedUtils.SaveSnapshotAsync(snapshot, config);
            }
            catch (Exception)
            {
                // Rollback if snapshot is not saved
                await configFile.DeleteAsync();
                throw;
            }

            if (Frame.CanGoBack)
            {
                Frame.GoBack();
            }
            var newItem = new HostedConfigListItem(config, snapshot);
            await Task.Delay(300);
            HostedConfigListItems?.Add(newItem);
            await Task.Delay(800);
            HostedConfigListPage.itemForBackNavigation = newItem;
            Frame.Navigate(typeof(HostedConfigPage), newItem);
        }

        private async void SubscribeButton_Click (object sender, RoutedEventArgs e)
        {
            BottomAppBar.IsEnabled = false;
            loadingStartStoryboard.Begin();
            loadingCover.Visibility = Visibility.Visible;

            try
            {
                await AddSubscribeAsync();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                await UiUtils.NotifyUser(ex.ToString());
            }
            finally
            {
                loadingEndStoryboard.Begin();
                BottomAppBar.IsEnabled = true;
            }
        }
    }
}
