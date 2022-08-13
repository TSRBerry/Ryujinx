﻿using Avalonia;
using Avalonia.Threading;
using Ryujinx.Ui.App.Common;
using System;
using DynamicData;
using DynamicData.Binding;
using ReactiveUI;
using Ryujinx.Rsc.Views;
using Ryujinx.Ui.Common.Configuration;
using System.Collections.ObjectModel;
using System.Threading;
using Avalonia.Media;
using Ryujinx.Ava.Common;
using Ryujinx.Ava.Common.Locale;
using Ryujinx.Ava.Common.Ui.Controls;
using Ryujinx.Rsc.Models;
using System.Collections.Generic;
using System.Threading.Tasks;
using Ryujinx.HLE;
using ARMeilleure.Translation.PTC;
using ShaderCacheLoadingState = Ryujinx.Graphics.Gpu.Shader.ShaderCacheState;

namespace Ryujinx.Rsc.ViewModels
{
    public class MainViewModel : ViewModelBase
    {
        private ObservableCollection<ApplicationData> _applications;
        private ReadOnlyObservableCollection<ApplicationData> _appsObservableList;
        private bool _isLoading;
        private bool _enableVirtualController;
        private string _fifoStatusText;
        private string _gpuStatusText;
        private string _gameStatusText;
        private Brush _vsyncColor;
        private bool _isGameRunning;
        private bool _showOverlay;
        private View _currentView = View.GameList;
        private bool _showTabs = true;
        private float _volume;
        private float _currentVolume;
        private bool _isPaused;
        private bool _showContextOptions;
        private string _searchText;
        private bool _isLoadingIndeterminate = true;
        private bool _showLoadProgress;
        private Brush _progressBarBackgroundColor;
        private string _loadHeading;
        private int _progressValue;
        private int _progressMaximum;
        private string _cacheLoadStatus;
        private Brush _progressBarForegroundColor;
        private byte[] _selectedIcon;

        public MainView Owner { get; set; }
        public GamePage GamePage { get; set; }

        public MainViewModel()
        {
            Applications = new ObservableCollection<ApplicationData>();

            Applications.ToObservableChangeSet()
                .Bind(out _appsObservableList).AsObservableList();

            _vsyncColor = new SolidColorBrush(Colors.White);

            if (AppConfig.PreviewerDetached)
            {
                ConfigurationState.Instance.Ui.GridSize.Value = 2;
            }
        }

        public ObservableCollection<ApplicationData> Applications
        {
            get => _applications;
            set
            {
                _applications = value;

                this.RaisePropertyChanged();
            }
        }

        public ReadOnlyObservableCollection<ApplicationData> AppsObservableList
        {
            get => _appsObservableList;
            set
            {
                _appsObservableList = value;

                this.RaisePropertyChanged();
            }
        }

        public View CurrentView
        {
            get => _currentView;
            set
            {
                _currentView = value;

                this.RaisePropertyChanged();
                this.RaisePropertyChanged(nameof(IsGameListActive));
                this.RaisePropertyChanged(nameof(IsSettingsActive));
            }
        }

        public bool IsGameListActive => CurrentView == View.GameList;
        public bool IsSettingsActive => CurrentView == View.Settings;

        public bool IsGridSmall => ConfigurationState.Instance.Ui.GridSize == 1;
        public bool IsGridMedium => ConfigurationState.Instance.Ui.GridSize == 2;
        public bool IsGridLarge => ConfigurationState.Instance.Ui.GridSize == 3;
        public bool IsGridHuge => ConfigurationState.Instance.Ui.GridSize == 4;

        public bool IsGrid => Glyph == Glyph.Grid;
        public bool IsList => Glyph == Glyph.List;
        
        public Glyph Glyph
        {
            get => (Glyph)ConfigurationState.Instance.Ui.GameListViewMode.Value;
            set
            {
                ConfigurationState.Instance.Ui.GameListViewMode.Value = (int)value;

                this.RaisePropertyChanged();
                this.RaisePropertyChanged(nameof(IsGrid));
                this.RaisePropertyChanged(nameof(IsList));

                ShowContextOptions = false;

                ConfigurationState.Instance.ToFileFormat().SaveConfig(AppConfig.ConfigurationPath);
            }
        }
        
        public void ToggleFavorite(ApplicationData application)
        {
            if (application != null)
            {
                application.Favorite = !application.Favorite;

                Owner.ApplicationLibrary.LoadAndSaveMetaData(application.TitleId, appMetadata =>
                {
                    appMetadata.Favorite = application.Favorite;
                });

                RefreshView();
            }
        }

        public void SetListMode()
        {
            Glyph = Glyph.List;
        }

        public void SetGridMode()
        {
            Glyph = Glyph.Grid;
        }

        public bool IsGameRunning
        {
            get => _isGameRunning; set
            {
                _isGameRunning = value;

                this.RaisePropertyChanged();
            }
        }

        public bool ShowTabs
        {
            get => _showTabs; set
            {
                _showTabs = value;

                this.RaisePropertyChanged();
            }
        }

        public bool ShowOverlay
        {
            get => _showOverlay; set
            {
                _showOverlay = value;

                this.RaisePropertyChanged();
            }
        }

        public bool IsLoadingIndeterminate
        {
            get => _isLoadingIndeterminate;
            set
            {
                _isLoadingIndeterminate = value;

                this.RaisePropertyChanged();
            }
        }

        public bool ShowLoadProgress
        {
            get => _showLoadProgress;
            set
            {
                _showLoadProgress = value;

                this.RaisePropertyChanged();
                this.RaisePropertyChanged(nameof(EnableVirtualController));
            }
        }

        public Brush ProgressBarBackgroundColor
        {
            get => _progressBarBackgroundColor;
            set
            {
                _progressBarBackgroundColor = value;

                this.RaisePropertyChanged();
            }
        }

        public Brush ProgressBarForegroundColor
        {
            get => _progressBarForegroundColor;
            set
            {
                _progressBarForegroundColor = value;

                this.RaisePropertyChanged();
            }
        }

        public byte[] SelectedIcon
        {
            get => _selectedIcon;
            set
            {
                _selectedIcon = value;

                this.RaisePropertyChanged();
            }
        }

        public string LoadHeading
        {
            get => _loadHeading;
            set
            {
                _loadHeading = value;

                this.RaisePropertyChanged();
            }
        }

        public int ProgressMaximum
        {
            get => _progressMaximum;
            set
            {
                _progressMaximum = value;

                this.RaisePropertyChanged();
            }
        }

        public int ProgressValue
        {
            get => _progressValue;
            set
            {
                _progressValue = value;

                this.RaisePropertyChanged();
            }
        }

        public string CacheLoadStatus
        {
            get => _cacheLoadStatus;
            set
            {
                _cacheLoadStatus = value;

                this.RaisePropertyChanged();
            }
        }

        public bool VolumeMuted => _currentVolume == 0;

        public float Volume
        {
            get => _currentVolume;
            set
            {
                _volume = value;
                _currentVolume = value;

                if (_isGameRunning)
                {
                    GamePage.AppHost.Device.SetVolume(_volume);
                }

                this.RaisePropertyChanged(nameof(VolumeMuted));
                this.RaisePropertyChanged();
            }
        }

        public void ToggleMute()
        {
            _currentVolume = _currentVolume == 0 ? _volume : 0;

            if (_isGameRunning)
            {
                GamePage.AppHost.Device.SetVolume(_currentVolume);
            }

            this.RaisePropertyChanged(nameof(VolumeMuted));
            this.RaisePropertyChanged(nameof(Volume));

        }

        public string Title { get; set; }
        public bool IsPaused
        {
            get => _isPaused; set
            {
                _isPaused = value;

                this.RaisePropertyChanged(nameof(IsPaused));
            }
        }
        public string TitleName { get; set; }

        public bool EnableVirtualController
        {
            get => _enableVirtualController && !ShowLoadProgress; set
            {
                _enableVirtualController = value;

                this.RaisePropertyChanged();
            }
        }

        public void Initialize()
        {
            Owner.ApplicationLibrary.ApplicationCountUpdated += ApplicationLibrary_ApplicationCountUpdated;
            Owner.ApplicationLibrary.ApplicationAdded += ApplicationLibrary_ApplicationAdded;

            Ptc.PtcStateChanged -= ProgressHandler;
            Ptc.PtcStateChanged += ProgressHandler;

            ReloadGameList();

            ShowNames = false;
        }

        public void ToggleVirtualController()
        {
            EnableVirtualController = !EnableVirtualController;
        }

        public void HandleShaderProgress(Switch emulationContext)
        {
            emulationContext.Gpu.ShaderCacheStateChanged -= ProgressHandler;
            emulationContext.Gpu.ShaderCacheStateChanged += ProgressHandler;
        }


        private void ProgressHandler<T>(T state, int current, int total) where T : Enum
        {
            try
            {
                ProgressMaximum = total;
                ProgressValue = current;

                switch (state)
                {
                    case PtcLoadingState ptcState:
                        CacheLoadStatus = $"{current} / {total}";
                        switch (ptcState)
                        {
                            case PtcLoadingState.Start:
                            case PtcLoadingState.Loading:
                                LoadHeading = LocaleManager.Instance["CompilingPPTC"];
                                IsLoadingIndeterminate = false;
                                break;
                            case PtcLoadingState.Loaded:
                                LoadHeading = string.Format(LocaleManager.Instance["LoadingHeading"], TitleName);
                                IsLoadingIndeterminate = true;
                                CacheLoadStatus = "";
                                break;
                        }
                        break;
                    case ShaderCacheLoadingState shaderCacheState:
                        CacheLoadStatus = $"{current} / {total}";
                        switch (shaderCacheState)
                        {
                            case ShaderCacheLoadingState.Start:
                            case ShaderCacheLoadingState.Loading:
                                LoadHeading = LocaleManager.Instance["CompilingShaders"];
                                IsLoadingIndeterminate = false;
                                break;
                            case ShaderCacheLoadingState.Loaded:
                                LoadHeading = string.Format(LocaleManager.Instance["LoadingHeading"], TitleName);
                                IsLoadingIndeterminate = true;
                                CacheLoadStatus = "";
                                break;
                        }
                        break;
                    default:
                        throw new ArgumentException($"Unknown Progress Handler type {typeof(T)}");
                }
            }
            catch (Exception) { }
        }

        public void ToggleVSync()
        {
            if (IsGameRunning)
            {
                GamePage.AppHost.Device.EnableDeviceVsync = !GamePage.AppHost.Device.EnableDeviceVsync;
            }
        }

        public void Pause()
        {
            Task.Run(() =>
            {
                GamePage.AppHost.Pause();
            });
        }

        public void Resume()
        {
            Task.Run(() =>
            {
                GamePage.AppHost.Resume();
            });
        }

        public void Stop()
        {
            Task.Run(() =>
            {
                GamePage.AppHost.Stop();
            });
        }
        
        public Thickness GridItemPadding => ShowNames ? new Thickness() : new Thickness(5);
        
        public int GridSizeScale
        {
            get => ConfigurationState.Instance.Ui.GridSize;
            set
            {
                ConfigurationState.Instance.Ui.GridSize.Value = value;

                if (value < 2)
                {
                    ShowNames = false;
                }

                this.RaisePropertyChanged();
                this.RaisePropertyChanged(nameof(IsGridSmall));
                this.RaisePropertyChanged(nameof(IsGridMedium));
                this.RaisePropertyChanged(nameof(IsGridLarge));
                this.RaisePropertyChanged(nameof(IsGridHuge));
                this.RaisePropertyChanged(nameof(ShowNames));
                this.RaisePropertyChanged(nameof(GridItemPadding));

                ConfigurationState.Instance.ToFileFormat().SaveConfig(AppConfig.ConfigurationPath);
            }
        }
        
        public bool ShowNames
        {
            get => ConfigurationState.Instance.Ui.ShowNames && ConfigurationState.Instance.Ui.GridSize > 1; set
            {
                ConfigurationState.Instance.Ui.ShowNames.Value = value;

                this.RaisePropertyChanged();
                this.RaisePropertyChanged(nameof(GridItemPadding));
                this.RaisePropertyChanged(nameof(GridSizeScale));

                ConfigurationState.Instance.ToFileFormat().SaveConfig(AppConfig.ConfigurationPath);
            }
        }

        private void ApplicationLibrary_ApplicationAdded(object? sender, ApplicationAddedEventArgs e)
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                Applications.Add(e.AppData);
            });
        }

        public void OpenSettings()
        {
            Owner.Navigate(typeof(SettingsView), Owner);
        }

        public void ReloadGameList()
        {
            if (_isLoading)
            {
                return;
            }
            
            ShowContextOptions = false;

            _isLoading = true;

            Applications.Clear();

            Thread thread = new(() =>
            {
                Owner.ApplicationLibrary.LoadApplications(ConfigurationState.Instance.Ui.GameDirs.Value,
                    ConfigurationState.Instance.System.Language);

                _isLoading = false;
            })
            { Name = "GUI.AppListLoadThread", Priority = ThreadPriority.AboveNormal };

            thread.Start();
        }
        

        public bool IsSortedByFavorite => SortMode == ApplicationSort.Favorite;
        public bool IsSortedByTitle => SortMode == ApplicationSort.Title;
        public bool IsSortedByDeveloper => SortMode == ApplicationSort.Developer;
        public bool IsSortedByLastPlayed => SortMode == ApplicationSort.LastPlayed;
        public bool IsSortedByTimePlayed => SortMode == ApplicationSort.TotalTimePlayed;
        public bool IsSortedByType => SortMode == ApplicationSort.FileType;
        public bool IsSortedBySize => SortMode == ApplicationSort.FileSize;
        public bool IsSortedByPath => SortMode == ApplicationSort.Path;

        private void ApplicationLibrary_ApplicationCountUpdated(object? sender, ApplicationCountUpdatedEventArgs e)
        {
        }

        public string FifoStatusText
        {
            get => _fifoStatusText;
            set
            {
                _fifoStatusText = value;

                this.RaisePropertyChanged();
            }
        }

        public string GpuStatusText
        {
            get => _gpuStatusText;
            set
            {
                _gpuStatusText = value;

                this.RaisePropertyChanged();
            }
        }
        public string GameStatusText
        {
            get => _gameStatusText;
            set
            {
                _gameStatusText = value;

                this.RaisePropertyChanged();
            }
        }

        public Brush VsyncColor
        {
            get => _vsyncColor;
            set
            {
                _vsyncColor = value;

                this.RaisePropertyChanged();
            }
        }

        public string SearchText
        {
            get { return _searchText; }
            set
            {
                _searchText = value;
                RefreshView();
            }
        }

        public void Sort(ApplicationSort sort)
        {
            SortMode = sort;
            RefreshView();
        }

        public ApplicationSort SortMode { get; set; }

        internal void Sort(bool isAscending)
        {
            IsAscending = isAscending;
            RefreshView();
        }

        public bool IsAscending { get; set; }

        public string ApplicationPath { get; set; }

        public bool ShowContextOptions
        {
            get => _showContextOptions;
            set
            {
                _showContextOptions = value;
                this.RaisePropertyChanged();
            }
        }

        private void RefreshView()
        {
            ShowContextOptions = false;
            
            Applications.ToObservableChangeSet()
                .Filter(Filter)
                .Sort(GetComparer())
                .Bind(out _appsObservableList).AsObservableList();

            this.RaisePropertyChanged(nameof(AppsObservableList));
        }

        private IComparer<ApplicationData> GetComparer()
        {
            switch (SortMode)
            {
                case ApplicationSort.LastPlayed:
                    return new Ryujinx.Ava.Common.Ui.Models.Generic.LastPlayedSortComparer(IsAscending);
                case ApplicationSort.FileSize:
                    return new Ryujinx.Ava.Common.Ui.Models.Generic.FileSizeSortComparer(IsAscending);
                case ApplicationSort.TotalTimePlayed:
                    return new Ryujinx.Ava.Common.Ui.Models.Generic.TimePlayedSortComparer(IsAscending);
                case ApplicationSort.Title:
                    return IsAscending ? SortExpressionComparer<ApplicationData>.Ascending(app => app.TitleName) : SortExpressionComparer<ApplicationData>.Descending(app => app.TitleName);
                case ApplicationSort.Favorite:
                    return !IsAscending ? SortExpressionComparer<ApplicationData>.Ascending(app => app.Favorite) : SortExpressionComparer<ApplicationData>.Descending(app => app.Favorite);
                case ApplicationSort.Developer:
                    return IsAscending ? SortExpressionComparer<ApplicationData>.Ascending(app => app.Developer) : SortExpressionComparer<ApplicationData>.Descending(app => app.Developer);
                case ApplicationSort.FileType:
                    return IsAscending ? SortExpressionComparer<ApplicationData>.Ascending(app => app.FileExtension) : SortExpressionComparer<ApplicationData>.Descending(app => app.FileExtension);
                case ApplicationSort.Path:
                    return IsAscending ? SortExpressionComparer<ApplicationData>.Ascending(app => app.Path) : SortExpressionComparer<ApplicationData>.Descending(app => app.Path);
                default:
                    return null;
            }
        }
        
        private bool Filter(object arg)
        {
            if (arg is ApplicationData app)
            {
                return string.IsNullOrWhiteSpace(_searchText) || app.TitleName.ToLower().Contains(_searchText.ToLower());
            }

            return false;
        }
    }
}
