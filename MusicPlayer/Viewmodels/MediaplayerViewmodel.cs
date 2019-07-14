﻿using MusicPlayer.Controls;
using MusicPlayer.Core;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage.FileProperties;
using Windows.UI.Xaml;

namespace MusicPlayer.Viewmodels
{
    public class MediaPlayerAccessor : INotifyPropertyChanged
    {
        public MediaplayerViewmodel Instance => MediaplayerViewmodel.Instance;

        public event PropertyChangedEventHandler PropertyChanged;

        public MediaPlayerAccessor()
        {
            this.Init();
        }

        private async void Init()
        {
            if (MediaplayerViewmodel.Initilized.IsCompleted)
                return;
            await MediaplayerViewmodel.Initilized;
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(this.Instance)));
        }
    }

    public class MediaplayerViewmodel : DependencyObject
    {
        public static MediaplayerViewmodel Instance { get; private set; }
        public static Task Initilized { get; }
        private static readonly TaskCompletionSource<object> initilized;
        static MediaplayerViewmodel()
        {
            initilized = new TaskCompletionSource<object>();
            Initilized = initilized.Task;
        }


        private readonly TransportControls transportControls;
        private readonly MediaPlaybackList mediaPlaybackList;
        //private readonly MediaPlaybackList singleRepeatPlaylist;
        private readonly Dictionary<Song, List<PlayingSong>> mediaItemLookup = new Dictionary<Song, List<PlayingSong>>();
        private readonly Dictionary<MediaPlaybackItem, PlayingSong> playbackItemLookup = new Dictionary<MediaPlaybackItem, PlayingSong>();

        public ReadOnlyObservableCollection<PlayingSong> CurrentPlaylist { get; }
        private readonly ObservableCollection<PlayingSong> currentPlaylist;



        public int CurrentPlayingIndex
        {
            get { return (int)this.GetValue(CurrentPlayingIndexProperty); }
            set { this.SetValue(CurrentPlayingIndexProperty, value); }
        }

        // Using a DependencyProperty as the backing store for CurrentPlayingIndex.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty CurrentPlayingIndexProperty =
            DependencyProperty.Register("CurrentPlayingIndex", typeof(int), typeof(MediaplayerViewmodel), new PropertyMetadata(-1, CurrentPlayingIndexChanged));

        private void CurrentPlayingIndexChanged(int newIndex)
        {
            if (newIndex > -1)
            {
                var newItem = this.CurrentPlaylist[newIndex];
                this.transportControls.CurrentMediaPlaybackItem = newItem.MediaPlaybackItem;
            }
            else
            {
                this.transportControls.CurrentMediaPlaybackItem = null;
            }

        }
        private static void CurrentPlayingIndexChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var newIndex = (int)e.NewValue;
            var me = (MediaplayerViewmodel)d;
            me.CurrentPlayingIndexChanged(newIndex);
        }

        private MediaplayerViewmodel(TransportControls transportControls)
        {
            this.transportControls = transportControls;

            this.mediaPlaybackList = new MediaPlaybackList();
            //this.singleRepeatPlaylist = new MediaPlaybackList();
            //this._mediaPlaybackList.CurrentItemChanged += this._mediaPlaybackList_CurrentItemChanged;
            this.transportControls.PlayList = this.mediaPlaybackList;

            this.currentPlaylist = new ObservableCollection<PlayingSong>();
            this.CurrentPlaylist = new ReadOnlyObservableCollection<PlayingSong>(this.currentPlaylist);

            transportControls.RegisterPropertyChangedCallback(TransportControls.IsShuffledProperty, (sender, e) =>
            {
                this.ResetSorting();
            });

            transportControls.RegisterPropertyChangedCallback(TransportControls.CurrentMediaPlaybackItemProperty, (sender, e) => this.RefresCurrentIndex());

        }

        private void RefresCurrentIndex()
        {
            var currentItem = this.transportControls.CurrentMediaPlaybackItem;
            int index;
            if (currentItem is null)
                index = -1;
            else
            {
                var viewmodel = this.playbackItemLookup[currentItem];
                index = this.CurrentPlaylist.IndexOf(viewmodel);
            }
            this.CurrentPlayingIndex = index;
        }

        public static void Init(TransportControls transportControls)
        {
            if (Instance != null)
                throw new InvalidOperationException("Already Initilized");

            Instance = new MediaplayerViewmodel(transportControls);
            initilized.SetResult(null);
        }

        private async Task ResetSorting()
        {
            var currentItem = this.transportControls.CurrentMediaPlaybackItem;
            this.currentPlaylist.Clear();

            foreach (var item in
                this.mediaPlaybackList.ShuffleEnabled
                ? this.mediaPlaybackList.ShuffledItems
                : this.mediaPlaybackList.Items as IEnumerable<MediaPlaybackItem>)
                this.currentPlaylist.Add(this.playbackItemLookup[item]);

            var newIndex = this.currentPlaylist.Select((value, index) => (value, index)).First(x => x.value.MediaPlaybackItem.Equals(currentItem)).index;

            this.CurrentPlayingIndex = newIndex;

        }


        public async Task Play()
        {
            if (!this.Dispatcher.HasThreadAccess)
            {
                var completionSource = new TaskCompletionSource<object>();
                await this.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, async () =>
                {
                    await this.Play();
                    completionSource.SetResult(null);
                });
                await completionSource.Task;
            }

            this.transportControls.IsPlaying = true;
        }

        public async Task ClearSongs()
        {
            if (!this.Dispatcher.HasThreadAccess)
            {
                var completionSource = new TaskCompletionSource<object>();
                await this.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, async () =>
                {
                    await this.ClearSongs();
                    completionSource.SetResult(null);
                });
                await completionSource.Task;
            }

            this.mediaPlaybackList.Items.Clear();
            this.playbackItemLookup.Clear();
            foreach (var item in this.mediaItemLookup)
                item.Value.Clear();
            this.currentPlaylist.Clear();
            this.mediaItemLookup.Clear();
        }

        public async Task RemoveSong(PlayingSong song)
        {
            if (!this.Dispatcher.HasThreadAccess)
            {
                var completionSource = new TaskCompletionSource<object>();
                await this.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, async () =>
                {
                    await this.RemoveSong(song);
                    completionSource.SetResult(null);
                });
                await completionSource.Task;
            }
            this.mediaPlaybackList.Items.Remove(song.MediaPlaybackItem);
            this.playbackItemLookup.Remove(song.MediaPlaybackItem);
            var list = this.mediaItemLookup[song.Song];
            this.currentPlaylist.Remove(song);
            list.Remove(song);
            if (list.Count == 0)
                this.mediaItemLookup.Remove(song.Song);
        }

        public async Task<PlayingSong> AddSong(Song song)
        {
            if (!this.Dispatcher.HasThreadAccess)
            {
                var completionSource = new TaskCompletionSource<Task<PlayingSong>>();
                await this.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    var result = this.AddSong(song);
                    completionSource.SetResult(result);
                });
                return await completionSource.Task.Unwrap();
            }

            Task getCover;
            MediaSource media;
            List<PlayingSong> list;
            MediaItemDisplayProperties oldProperties = null;
            if (this.mediaItemLookup.ContainsKey(song))
            {
                list = this.mediaItemLookup[song];
                var oldMedia = list.FirstOrDefault()?.MediaPlaybackItem;
                media = oldMedia?.Source;
                if (media is null) // Async could add list before mediasource is added.
                    media = await LibraryRegistry<MediaSource, Uri>.Get(song.LibraryProvider).GetMediaSource(song.MediaId, default);
                else
                    oldProperties = oldMedia.GetDisplayProperties();
            }
            else
            {
                list = new List<PlayingSong>();
                this.mediaItemLookup.Add(song, list);
                media = await LibraryRegistry<MediaSource, Uri>.Get(song.LibraryProvider).GetMediaSource(song.MediaId, default);
            }

            var mediaItem = new MediaPlaybackItem(media);
            var viewModel = new PlayingSong(mediaItem, song);
            this.playbackItemLookup.Add(mediaItem, viewModel);
            list.Add(viewModel);

            if (oldProperties is null)
            {
                var displayProperties = mediaItem.GetDisplayProperties();
                displayProperties.Type = Windows.Media.MediaPlaybackType.Music;
                displayProperties.MusicProperties.AlbumTitle = song.AlbumName;
                displayProperties.MusicProperties.TrackNumber = (uint)song.Track;
                displayProperties.MusicProperties.Title = song.Title;

                displayProperties.MusicProperties.Genres.Clear();
                foreach (var genre in song.Genres)
                    displayProperties.MusicProperties.Genres.Add(genre);

                displayProperties.MusicProperties.Artist = string.Join(", ", song.Interpreters);

                mediaItem.ApplyDisplayProperties(displayProperties);

                var coverTask = song.GetCover(300, default);
                getCover = coverTask.ContinueWith(t =>
                {
                    var coverStreamReferance = t.Result;
                    displayProperties.Thumbnail = coverStreamReferance;
                    mediaItem.ApplyDisplayProperties(displayProperties);
                });
            }
            else
                getCover = null;

            this.mediaPlaybackList.Items.Add(mediaItem);

            int indexAdded;
            if (this.mediaPlaybackList.ShuffleEnabled)
                indexAdded = this.mediaPlaybackList.ShuffledItems.Select((value, index) => (value, index)).First(x => x.value == mediaItem).index;
            else
                indexAdded = this.mediaPlaybackList.Items.Count - 1;

            this.currentPlaylist.Insert(indexAdded, viewModel);

            if (getCover != null)
                await getCover;
            return viewModel;
        }
    }

    public class PlayingSong
    {
        public PlayingSong(MediaPlaybackItem mediaPlaybackItem, Song song)
        {
            this.MediaPlaybackItem = mediaPlaybackItem;
            this.Song = song;
        }

        public MediaPlaybackItem MediaPlaybackItem { get; }
        public Song Song { get; }
    }
}