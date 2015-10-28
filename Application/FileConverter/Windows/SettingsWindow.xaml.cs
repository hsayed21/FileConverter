﻿// <copyright file="SettingsWindow.xaml.cs" company="AAllard">License: http://www.gnu.org/licenses/gpl.html GPL version 3.</copyright>

namespace FileConverter
{
    using System;
    using System.Runtime.CompilerServices;
    using System.Windows;
    using System.ComponentModel;
    using System.Windows.Controls;
    using System.Windows.Input;

    using FileConverter.Annotations;

    /// <summary>
    /// Interaction logic for Settings.xaml
    /// </summary>
    public partial class SettingsWindow : Window, INotifyPropertyChanged
    {
        private ConversionPreset selectedPreset;

        private Settings settings;

        public SettingsWindow()
        {
            this.InitializeComponent();
            
            Application application = Application.Current as Application;
            this.settings = application.Settings;
            this.PresetList.ItemsSource = settings.ConversionPresets;

            OutputType[] outputTypes = new[]
                                           {
                                               OutputType.Flac, 
                                               OutputType.Mp3, 
                                               OutputType.Ogg, 
                                               OutputType.Wav, 
                                           };

            this.OutputFormats.ItemsSource = outputTypes;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public event System.EventHandler<System.EventArgs> OnSettingsWindowHide;

        public ConversionPreset SelectedPreset
        {
            get
            {
                return this.selectedPreset;
            }

            set
            {
                this.selectedPreset = value;
                this.OnPropertyChanged();
            }
        }
        
        [NotifyPropertyChangedInvocator]
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void ToggleButton_OnChecked(object sender, RoutedEventArgs e)
        {
            if (this.selectedPreset == null)
            {
                return;
            }

            CheckBox checkBox = sender as System.Windows.Controls.CheckBox;
            string inputFormat = checkBox.Content as string;

            if (!this.selectedPreset.InputTypes.Contains(inputFormat))
            {
                this.selectedPreset.InputTypes.Add(inputFormat);
            }
        }

        private void ToggleButton_OnUnchecked(object sender, RoutedEventArgs e)
        {
            if (this.selectedPreset == null)
            {
                return;
            }

            CheckBox checkBox = sender as System.Windows.Controls.CheckBox;
            string inputFormat = checkBox.Content as string;

            this.selectedPreset.InputTypes.Remove(inputFormat);
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.HideSettingsWindow();
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            // Save changes.
            Application application = Application.Current as Application;
            application.Settings.Save();

            this.Hide();

            this.OnSettingsWindowHide?.Invoke(this, new EventArgs());
        }

        private void AddPresetButton_Click(object sender, RoutedEventArgs e)
        {
            Application application = Application.Current as Application;
            ConversionPreset newPreset = new ConversionPreset("New preset", OutputType.None, new string[0]);
            application.Settings.ConversionPresets.Add(newPreset);
            this.SelectedPreset = newPreset;
            this.PresetNameTextBox.Focus();
            this.PresetNameTextBox.SelectAll();
        }

        private void RemovePresetButton_Click(object sender, RoutedEventArgs e)
        {
            Application application = Application.Current as Application;
            application.Settings.ConversionPresets.Remove(this.selectedPreset);
        }

        private void CanSaveSettings(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = this.settings != null && string.IsNullOrEmpty(this.settings.Error);
        }
        
        private void SettingsWindow_OnClosing(object sender, CancelEventArgs e)
        {
            e.Cancel = true;

            this.HideSettingsWindow();
        }

        private void HideSettingsWindow()
        {
            // Load previous preset in order to cancel changes.
            Application application = Application.Current as Application;
            application.Settings.Load();

            this.Hide();

            this.OnSettingsWindowHide?.Invoke(this, new EventArgs());
        }

        private void MovePresetUpButton_Click(object sender, RoutedEventArgs e)
        {
            ConversionPreset presetToMove = this.SelectedPreset;
            if (presetToMove == null)
            {
                return;
            }
            
            int indexOfSelectedPreset = this.settings.ConversionPresets.IndexOf(presetToMove);
            int newIndexOfSelectedPreset = System.Math.Max(0, indexOfSelectedPreset - 1);
            
            this.settings.ConversionPresets.Move(indexOfSelectedPreset, newIndexOfSelectedPreset);
        }

        private void MovePresetDownButton_Click(object sender, RoutedEventArgs e)
        {
            ConversionPreset presetToMove = this.SelectedPreset;
            if (presetToMove == null)
            {
                return;
            }

            int indexOfSelectedPreset = this.settings.ConversionPresets.IndexOf(presetToMove);
            int newIndexOfSelectedPreset = System.Math.Min(this.settings.ConversionPresets.Count - 1, indexOfSelectedPreset + 1);

            this.settings.ConversionPresets.Move(indexOfSelectedPreset, newIndexOfSelectedPreset);
        }
    }
}
