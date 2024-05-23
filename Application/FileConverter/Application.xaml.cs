// <copyright file="Application.xaml.cs" company="AAllard">License: http://www.gnu.org/licenses/gpl.html GPL version 3.</copyright>

/*  File Converter - This program allow you to convert file format to another.
    Copyright (C) 2024 Adrien Allard
    email: adrien.allard.pro@gmail.com

    This program is free software: you can redistribute it and/or modify it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or any later version.

    This program is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU General Public License for more details.

    You should have received a copy of the GNU General Public License along with this program.  If not, see <http://www.gnu.org/licenses/>.
 */

namespace FileConverter
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Runtime.InteropServices;
    using System.Security.Principal;
    using System.Threading;
    using System.Windows;

    using CommunityToolkit.Mvvm.DependencyInjection;

    using FileConverter.ConversionJobs;
    using FileConverter.Services;
    using FileConverter.ViewModels;
    using FileConverter.Views;
    using Microsoft.Extensions.DependencyInjection;
    using Debug = FileConverter.Diagnostics.Debug;

    using System.Windows.Input;
    using NHotkey.Wpf;
    using NHotkey;
    using System.Linq;

    public partial class Application : System.Windows.Application
    {
        private static readonly Version Version = new Version()
                                                      {
                                                          Major = 2,
                                                          Minor = 0,
                                                          Patch = 2,
                                                      };

        private bool needToRunConversionThread;
        private bool cancelAutoExit;
        private bool isSessionEnding;
        private bool verbose;
        private bool showSettings;
        private bool showHelp;

        [DllImport("kernel32.dll")]
        static extern bool AttachConsole(uint dwProcessId);

        const uint ATTACH_PARENT_PROCESS = 0x0ffffffff;

        public event EventHandler<ApplicationTerminateArgs> OnApplicationTerminate;

        public static Version ApplicationVersion => Application.Version;

        public static bool IsInAdmininstratorPrivileges
        {
            get
            {
                return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
            }
        }

        public void CancelAutoExit()
        {
            this.cancelAutoExit = true;

            if (this.OnApplicationTerminate != null)
            {
                this.OnApplicationTerminate.Invoke(this, new ApplicationTerminateArgs(float.NaN));
            }
        }

        public static void AskForShutdown()
        {
            Application.Current.Dispatcher.BeginInvoke((Action)(() =>
            {
                if (Helpers.IsAnotherInstanceRunning())
                {
                    Application.Current.Dispatcher.BeginInvoke((Action)(() => Application.Current.Shutdown(Debug.FirstErrorCode)));
                }
                else
                {
                    INavigationService navigationService = Ioc.Default.GetRequiredService<INavigationService>();
                    navigationService.Close(Pages.Main, false);
                    // Close the application if no other windows are open
                    //if (Application.Current.Windows.Count == 0)
                    //{
                    //    Application.Current.Shutdown();
                    //}
                }
            }));
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            this.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            // Redirect standard output to the parent process in case the application is launch from command line.
            AttachConsole(ATTACH_PARENT_PROCESS);
            
            this.RegisterServices();
            this.RegisterHotKey();

            this.Initialize();

            // Navigate to the wanted view.
            INavigationService navigationService = Ioc.Default.GetRequiredService<INavigationService>();

            if (this.showHelp)
            {
                navigationService.Show(Pages.Help);
                return;
            }

            //navigationService.Show(Pages.Settings);

            if (this.needToRunConversionThread)
            {
                navigationService.Show(Pages.Main);

                IConversionService conversionService = Ioc.Default.GetRequiredService<IConversionService>();
                conversionService.ConversionJobsTerminated += this.ConversionService_ConversionJobsTerminated;
                conversionService.ConvertFilesAsync();
            }

            if (this.showSettings)
            {
                navigationService.Show(Pages.Settings);
            }

            if (this.verbose)
            {
                navigationService.Show(Pages.Diagnostics);
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            if (!Helpers.IsAnotherInstanceRunning())
            {
                HotkeyManager.Current.Remove("ConvertMP4CustomLower");
                HotkeyManager.Current.Remove("ConvertMP4Lower");
                HotkeyManager.Current.Remove("ConvertMP3");
            }

            base.OnExit(e);

            Debug.Log("Exit application.");

            IUpgradeService upgradeService = Ioc.Default.GetRequiredService<IUpgradeService>();

            if (!this.isSessionEnding && upgradeService.UpgradeVersionDescription != null && upgradeService.UpgradeVersionDescription.NeedToUpgrade)
            {
                Debug.Log($"A new version of file converter has been found: {upgradeService.UpgradeVersionDescription.LatestVersion}.");

                if (string.IsNullOrEmpty(upgradeService.UpgradeVersionDescription.InstallerPath))
                {
                    Debug.LogError("Invalid installer path.");
                }
                else
                {
                    Debug.Log("Wait for the end of the installer download.");
                    while (upgradeService.UpgradeVersionDescription.InstallerDownloadInProgress)
                    {
                        Thread.Sleep(1000);
                    }

                    string installerPath = upgradeService.UpgradeVersionDescription.InstallerPath;
                    if (!System.IO.File.Exists(installerPath))
                    {
                        Debug.LogError($"Can't find upgrade installer ({installerPath}). Try to restart the application.");
                        return;
                    }

                    // Start process.
                    Debug.Log($"Start file converter upgrade from version {ApplicationVersion} to {upgradeService.UpgradeVersionDescription.LatestVersion}.");

                    ProcessStartInfo startInfo = new System.Diagnostics.ProcessStartInfo(installerPath) { UseShellExecute = true, };

                    Debug.Log($"Start upgrade process: {System.IO.Path.GetFileName(startInfo.FileName)}{startInfo.Arguments}.");
                    Process process = new System.Diagnostics.Process { StartInfo = startInfo };

                    process.Start();
                }
            }

            Debug.Release();
        }

        protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
        {
            base.OnSessionEnding(e);

            this.isSessionEnding = true;
            this.Shutdown();
        }

        private void RegisterServices()
        {
            var services = new ServiceCollection();

            if (this.TryFindResource("Locator") is ViewModelLocator viewModelLocator)
            {
                viewModelLocator.RegisterViewModels(services);
            }
            else
            {
                Debug.LogError("Can't retrieve view model locator.");
                Application.AskForShutdown();
            }

            if (this.TryFindResource("Upgrade") is UpgradeService upgradeService)
            {
                services.AddSingleton<IUpgradeService>(upgradeService);
            }
            else
            {
                Debug.LogError("Can't retrieve Upgrade service.");
                Application.AskForShutdown();
            }

            services
              .AddSingleton<INavigationService, NavigationService>()
              .AddSingleton<IConversionService, ConversionService>()
              .AddSingleton<ISettingsService, SettingsService>();

            Ioc.Default.ConfigureServices(services.BuildServiceProvider());

            INavigationService navigationService = Ioc.Default.GetRequiredService<INavigationService>();

            navigationService.RegisterPage<HelpWindow>(Pages.Help, false, false);
            navigationService.RegisterPage<MainWindow>(Pages.Main, false, false);
            navigationService.RegisterPage<SettingsWindow>(Pages.Settings, true, false);
            navigationService.RegisterPage<DiagnosticsWindow>(Pages.Diagnostics, true, false);
            navigationService.RegisterPage<UpgradeWindow>(Pages.Upgrade, true, false);
        }

        private void Initialize()
        {
#if BUILD32
            Diagnostics.Debug.Log("File Converter v" + ApplicationVersion.ToString() + " (32 bits)");
#else
            Diagnostics.Debug.Log("File Converter v" + ApplicationVersion.ToString() + " (64 bits)");
#endif

            // Retrieve arguments.
            Debug.Log("Retrieve arguments...");
            string[] args = Environment.GetCommandLineArgs();

            // Log arguments.
            for (int index = 0; index < args.Length; index++)
            {
                string argument = args[index];
                Debug.Log($"Arg{index}: {argument}");
            }

            Debug.Log(string.Empty);

            if (args.Length == 1)
            {
                // Display help windows to explain that this application is a context menu extension.
                this.showHelp = true;
                return;
            }

            // Parse arguments.
            List<string> filePaths = new List<string>();
            string conversionPresetName = null;
            for (int index = 1; index < args.Length; index++)
            {
                string argument = args[index];
                if (string.IsNullOrEmpty(argument))
                {
                    continue;
                }

                if (argument.StartsWith("--"))
                {
                    // This is an optional parameter.
                    string parameterTitle = argument.Substring(2).ToLowerInvariant();

                    switch (parameterTitle)
                    {
                        case "post-install-init":
                            ISettingsService settingsService = Ioc.Default.GetRequiredService<ISettingsService>();
                            if (!settingsService.PostInstallationInitialization())
                            {
                                Debug.LogError(errorCode: 0x0F, $"Failed to execute post install initialization.");
                            }

                            Application.AskForShutdown();
                            return;

                        case "register-shell-extension":
                            {
                                if (index >= args.Length - 1)
                                {
                                    Debug.LogError(errorCode: 0x0B, $"Invalid format.");
                                    break;
                                }

                                string shellExtensionPath = args[index + 1];
                                index++;

                                if (!Helpers.RegisterShellExtension(shellExtensionPath))
                                {
                                    Debug.LogError(errorCode: 0x0C, $"Failed to register shell extension {shellExtensionPath}.");
                                }

                                Application.AskForShutdown();
                                return;
                            }

                        case "unregister-shell-extension":
                            {
                                if (index >= args.Length - 1)
                                {
                                    Debug.LogError(errorCode: 0x0D, $"Invalid format.");
                                    break;
                                }

                                string shellExtensionPath = args[index + 1];
                                index++;

                                if (!Helpers.UnregisterExtension(shellExtensionPath))
                                {
                                    Debug.LogError(errorCode: 0x0E, $"Failed to unregister shell extension {shellExtensionPath}.");
                                }

                                Application.AskForShutdown();
                                return;
                            }

                        case "version":
                            Console.WriteLine(ApplicationVersion.ToString());
                            Application.AskForShutdown();
                            return;

                        case "settings":
                            this.showSettings = true;
                            break;

                        case "conversion-preset":
                            if (index >= args.Length - 1)
                            {
                                Debug.LogError(errorCode: 0x01, $"Invalid format.");
                                Application.AskForShutdown();
                                return;
                            }

                            conversionPresetName = args[index + 1];
                            index++;
                            break;

                        case "input-files":
                            if (index >= args.Length - 1)
                            {
                                Debug.LogError(errorCode: 0x02, $"Invalid format.");
                                Application.AskForShutdown();
                                return;
                            }

                            string fileListPath = args[index + 1];
                            try
                            {
                                using (FileStream file = File.OpenRead(fileListPath))
                                using (StreamReader reader = new StreamReader(file))
                                {
                                    while (!reader.EndOfStream)
                                    {
                                        filePaths.Add(reader.ReadLine());
                                    }
                                }
                            }
                            catch (Exception exception)
                            {
                                Debug.LogError(errorCode: 0x03, $"Can't read input files list: {exception}");
                                Application.AskForShutdown();
                                return;
                            }

                            index++;
                            break;

                        case "verbose":
                            {
                                this.verbose = true;
                            }

                            break;

                        default:
                            Debug.LogError($"Unknown application argument: '--{parameterTitle}'.");
                            return;
                    }
                }
                else
                {
                    filePaths.Add(argument);
                }
            }

            this.RunConversions(filePaths, conversionPresetName);
        }

        private void RunConversions(List<string> filePaths, string conversionPresetName)
        {
            ISettingsService settingsService = Ioc.Default.GetRequiredService<ISettingsService>();
            if (settingsService.Settings == null)
            {
                Debug.LogError(errorCode: 0x04, "Can't load File Converter settings. The application will now shutdown, if you want to fix the problem yourself please edit or delete the file: C:\\Users\\UserName\\AppData\\Local\\FileConverter\\Settings.user.xml.");
                Application.AskForShutdown();
                return;
            }

            Debug.Assert(Debug.FirstErrorCode == 0, "An error happened during the initialization.");

            // Check for upgrade.
            if (settingsService.Settings.CheckUpgradeAtStartup)
            {
                IUpgradeService upgradeService = Ioc.Default.GetRequiredService<IUpgradeService>();
                upgradeService.NewVersionAvailable += this.UpgradeService_NewVersionAvailable;
                upgradeService.CheckForUpgrade();
            }

            ConversionPreset conversionPreset = null;
            if (!string.IsNullOrEmpty(conversionPresetName))
            {
                conversionPreset = settingsService.Settings.GetPresetFromName(conversionPresetName);
                if (conversionPreset == null)
                {
                    Debug.LogError(errorCode: 0x02, $"Invalid conversion preset '{conversionPresetName}'.");
                    Application.AskForShutdown();
                    return;
                }
            }

            if (conversionPreset != null)
            {
                IConversionService conversionService = Ioc.Default.GetRequiredService<IConversionService>();

                // Create conversion jobs.
                Debug.Log($"Create jobs for conversion preset: '{conversionPreset.FullName}'");
                try
                {
                    for (int index = 0; index < filePaths.Count; index++)
                    {
                        string inputFilePath = filePaths[index];
                        ConversionJob conversionJob = ConversionJobFactory.Create(conversionPreset, inputFilePath);

                        conversionService.RegisterConversionJob(conversionJob);
                    }
                }
                catch (Exception exception)
                {
                    Debug.LogError(exception.Message);
                    throw;
                }

                this.needToRunConversionThread = true;
            }
        }

        private void UpgradeService_NewVersionAvailable(object sender, UpgradeVersionDescription e)
        {
            Ioc.Default.GetRequiredService<INavigationService>().Show(Pages.Upgrade);

            IUpgradeService upgradeService = Ioc.Default.GetRequiredService<IUpgradeService>();
            upgradeService.NewVersionAvailable -= this.UpgradeService_NewVersionAvailable;
        }

        private void ConversionService_ConversionJobsTerminated(object sender, ConversionJobsTerminatedEventArgs e)
        {
            IConversionService conversionService = Ioc.Default.GetRequiredService<IConversionService>();
            conversionService.ConversionJobsTerminated -= this.ConversionService_ConversionJobsTerminated;

            ISettingsService settingsService = Ioc.Default.GetRequiredService<ISettingsService>();

            if (!settingsService.Settings.ExitApplicationWhenConversionsFinished)
            {
                return;
            }
            
            if (this.cancelAutoExit)
            {
                return;
            }

            if (e.AllConversionsSucceed)
            {
                float remainingTime = settingsService.Settings.DurationBetweenEndOfConversionsAndApplicationExit;
                while (remainingTime > 0f)
                {
                    if (this.OnApplicationTerminate != null)
                    {
                        this.OnApplicationTerminate.Invoke(this, new ApplicationTerminateArgs(remainingTime));
                    }

                    Thread.Sleep(1000);
                    remainingTime--;

                    if (this.cancelAutoExit)
                    {
                        return;
                    }
                }

                if (this.OnApplicationTerminate != null)
                {
                    this.OnApplicationTerminate.Invoke(this, new ApplicationTerminateArgs(remainingTime));
                }

                Application.AskForShutdown();
            }
        }

        private void RegisterHotKey()
        {
            try
            {
                if (!Helpers.IsAnotherInstanceRunning())
                {
                    HotkeyManager.Current.AddOrReplace("ConvertMP4CustomLower", Key.F9, ModifierKeys.Control, OnConvertMP4CustomLowerHotkey);
                    HotkeyManager.Current.AddOrReplace("ConvertMP4Lower", Key.F10, ModifierKeys.Control, OnConvertMP4LowerHotkey);
                    HotkeyManager.Current.AddOrReplace("ConvertMP3", Key.F11, ModifierKeys.Control, OnConvertMP3Hotkey);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to register hotkey: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnConvertMP4LowerHotkey(object sender, HotkeyEventArgs e)
        {
            ConvertMP4LowerFilesInDownloadFolder();
            e.Handled = true;
        }
        private void OnConvertMP4CustomLowerHotkey(object sender, HotkeyEventArgs e)
        {
            ConvertMP4CustomLowerFilesInDownloadFolder();
            e.Handled = true;
        }

        private void OnConvertMP3Hotkey(object sender, HotkeyEventArgs e)
        {
            ConvertMP3FilesInDownloadFolder();
            e.Handled = true;
        }

        private void ConvertMP4LowerFilesInDownloadFolder()
        {
            string downloadFolderPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            string[] mp4Files = Directory.GetFiles(downloadFolderPath, "*.mp4");
            // get last Date modified file
            FileInfo lastFile = mp4Files.Select(f => new FileInfo(f)).OrderByDescending(f => f.LastWriteTime).First();

            if (mp4Files.Length == 0)
            {
                MessageBox.Show("No MP4 files found in the download folder.", "File Converter", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            ISettingsService settingsService = Ioc.Default.GetRequiredService<ISettingsService>();
            ConversionPreset conversionPreset = settingsService.Settings.GetPresetFromName("To Mp4 (low quality)");

            if (conversionPreset == null)
            {
                MessageBox.Show("Invalid conversion preset.", "File Converter", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            IConversionService conversionService = Ioc.Default.GetRequiredService<IConversionService>();

            //foreach (string mp4File in mp4Files)
            //{
            //    ConversionJob conversionJob = ConversionJobFactory.Create(conversionPreset, mp4File);
            //    conversionService.RegisterConversionJob(conversionJob);
            //}

            ConversionJob conversionJob = ConversionJobFactory.Create(conversionPreset, lastFile.FullName);
            conversionService.RegisterConversionJob(conversionJob);

            // Navigate to the wanted view.
            INavigationService navigationService = Ioc.Default.GetRequiredService<INavigationService>();
            navigationService.Show(Pages.Main);

            conversionService.ConversionJobsTerminated += this.ConversionService_ConversionJobsTerminated;
            //conversionService.Conver();
            conversionService.ConvertFilesAsync();

        }
        private void ConvertMP4CustomLowerFilesInDownloadFolder()
        {
            string downloadFolderPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            string[] mp4Files = Directory.GetFiles(downloadFolderPath, "*.mp4");
            // get last Date modified file
            FileInfo lastFile = mp4Files.Select(f => new FileInfo(f)).OrderByDescending(f => f.LastWriteTime).First();

            if (mp4Files.Length == 0)
            {
                MessageBox.Show("No MP4 files found in the download folder.", "File Converter", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            ISettingsService settingsService = Ioc.Default.GetRequiredService<ISettingsService>();
            ConversionPreset conversionPreset = settingsService.Settings.GetPresetFromName("To Mp4 (low quality custom)");

            if (conversionPreset == null)
            {
                MessageBox.Show("Invalid conversion preset.", "File Converter", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            IConversionService conversionService = Ioc.Default.GetRequiredService<IConversionService>();

            //foreach (string mp4File in mp4Files)
            //{
            //    ConversionJob conversionJob = ConversionJobFactory.Create(conversionPreset, mp4File);
            //    conversionService.RegisterConversionJob(conversionJob);
            //}

            ConversionJob conversionJob = ConversionJobFactory.Create(conversionPreset, lastFile.FullName);
            conversionService.RegisterConversionJob(conversionJob);

            // Navigate to the wanted view.
            INavigationService navigationService = Ioc.Default.GetRequiredService<INavigationService>();
            navigationService.Show(Pages.Main);

            conversionService.ConversionJobsTerminated += this.ConversionService_ConversionJobsTerminated;
            //conversionService.Conver();
            conversionService.ConvertFilesAsync();

        }

        private void ConvertMP3FilesInDownloadFolder()
        {
            string downloadFolderPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            string[] mp4Files = Directory.GetFiles(downloadFolderPath, "*.ogg");
            // get last Date modified file
            FileInfo lastFile = mp4Files.Select(f => new FileInfo(f)).OrderByDescending(f => f.LastWriteTime).First();

            if (mp4Files.Length == 0)
            {
                MessageBox.Show("No ogg files found in the download folder.", "File Converter", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            ISettingsService settingsService = Ioc.Default.GetRequiredService<ISettingsService>();
            ConversionPreset conversionPreset = settingsService.Settings.GetPresetFromName("To Mp3");

            if (conversionPreset == null)
            {
                MessageBox.Show("Invalid conversion preset.", "File Converter", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            IConversionService conversionService = Ioc.Default.GetRequiredService<IConversionService>();

            //foreach (string mp4File in mp4Files)
            //{
            //    ConversionJob conversionJob = ConversionJobFactory.Create(conversionPreset, mp4File);
            //    conversionService.RegisterConversionJob(conversionJob);
            //}

            ConversionJob conversionJob = ConversionJobFactory.Create(conversionPreset, lastFile.FullName);
            conversionService.RegisterConversionJob(conversionJob);

            // Navigate to the wanted view.
            INavigationService navigationService = Ioc.Default.GetRequiredService<INavigationService>();
            navigationService.Show(Pages.Main);

            conversionService.ConversionJobsTerminated += this.ConversionService_ConversionJobsTerminated;
            //conversionService.Conver();
            conversionService.ConvertFilesAsync();

        }

    }
}
