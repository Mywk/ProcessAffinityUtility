/* Copyright (C) 2021 - Mywk.Net
 * Licensed under the EUPL, Version 1.2
 * You may obtain a copy of the Licence at: https://joinup.ec.europa.eu/community/eupl/og_page/eupl
 * Unless required by applicable law or agreed to in writing, software distributed under the Licence is distributed on an "AS IS" basis, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 */
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Process_Affinity_Utility.Properties;
using Application = System.Windows.Application;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;

namespace Process_Affinity_Utility
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : AcrylicWindow
    {
        private List<Profile> _profiles = new List<Profile>();
        private bool _isLoaded = false;
        private bool _closePending = false;
        private bool AutoApply { get; set; } = false;
        private string SelectedProcessName { get; set; } = String.Empty;

        /// <summary>
        /// Message colors, this will be changed on OnLoaded if using the dark mode
        /// </summary>
        private Color RedColor = Colors.Red;
        private Color GreenColor = Colors.DarkGreen;

        public MainWindow()
        {
            InitializeComponent();

            // Start minimized to tray if necessary
            if (((App)App.Current).StartMinimizedToTray)
                HideToTrayButton_Click(null, null);
        }

        private System.ComponentModel.BackgroundWorker backgroundWorker = new BackgroundWorker();


        /// <summary>
        /// Sets the bottom info label and checks for updates
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void FrameworkElement_OnLoaded(object sender, RoutedEventArgs e)
        {
            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            BottomLabel.Content = this.Title + " v" + version.Major + "." + version.Minor + " © " + DateTime.Now.Year + " - Mywk.Net";

            // Check for updates and show the update label if necessary
            if (CheckForUpdates())
                UpdateLabel.Visibility = Visibility.Visible;

            // Load core count and refresh process list
            LoadCoreCount();
            _ = RefreshProcessListAsync();

            // Load last saved settings
            LoadSettings();

            // Start the auto-apply background worker
            backgroundWorker.DoWork += new DoWorkEventHandler(backgroundWorker_DoWork);
            backgroundWorker.RunWorkerAsync();

            // Add shield to open as administrator button if we are not running as administrator
            if (!WindowsIdentity.GetCurrent().Owner
                .IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid))
            {
                var shield = System.Drawing.SystemIcons.Shield.ToBitmap();
                shield.MakeTransparent();
                BitmapSource bitmapSource = Imaging.CreateBitmapSourceFromHBitmap(
                    shield.GetHbitmap(),
                    IntPtr.Zero,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());

                RunAsAdministratorButton.Content = new Image
                {
                    Source = bitmapSource,
                    VerticalAlignment = VerticalAlignment.Center,
                    Height = 14, Width = 14
                };
            }
            else
                RunAsAdministratorButton.Visibility = Visibility.Hidden;

            // Adjust colors for dark mode
            if (((App) App.Current).IsDarkTheme)
            {
                RedColor = Color.FromRgb(255, 83, 71);
                GreenColor = Colors.GreenYellow;
            }

            // Hotfix for Windows making the window not correctly render
            this.Height += 1;
            this.Width += 1;

            _isLoaded = true;
        }

        /// <summary>
        /// Sets the given profile process affinity
        /// </summary>
        /// <param name="processlist"></param>
        /// <param name="profile"></param>
        /// <param name="showAlreadyApplied">Return previously updated processes too</param>
        /// <returns></returns>
        private string SetProfileProcessAffinity(Process[] processlist, Profile profile, bool showAlreadyApplied = false)
        {
            string updatedProcesses = String.Empty;

            foreach (var process in processlist)
            {
                if (!String.IsNullOrEmpty(process.MainWindowTitle))
                {
                    if (profile.ProcessName.ToLowerInvariant() == process.ProcessName.ToLowerInvariant())
                    {
                        try
                        {
                            if (profile.ProcessAffinity != process.ProcessorAffinity.ToInt64())
                            {
                                IntPtr processAffinity = (IntPtr) profile.ProcessAffinity;
                                Dispatcher.Invoke((Action) delegate
                                {
                                    process.ProcessorAffinity = processAffinity;

                                    // For convenience, update the displayed process if it's the one selected
                                    if (SelectedProcessName == process.ProcessName)
                                    {
                                        ProcessesListBox_OnSelectionChanged(null, null);
                                    }
                                });

                                updatedProcesses += process.ProcessName + " ";
                            }
                            else if (showAlreadyApplied)
                                updatedProcesses += process.ProcessName + " ";
                        }
                        catch (Exception exception)
                        {
                            if ((uint) exception.HResult == 0x80004005) // ACCESSDENIED
                            {
                                Dispatcher.Invoke((Action) delegate
                                {
                                    ShowMessage(
                                        "Access denied while attempting to set process affinity for: " +
                                        process.ProcessName, RedColor);
                                });

                                System.Threading.Thread.Sleep(3000);
                            }
                        }

                    }
                }
            }

            return updatedProcesses;
        }

        /// <summary>
        /// Auto-apply profiles if the process is open and the affinity differs from the profile
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void backgroundWorker_DoWork(object sender, DoWorkEventArgs e)
        {
            while (!_closePending)
            {
                if (AutoApply && _profiles.Count > 0)
                {
                    Process[] processlist = Process.GetProcesses();
                    string updatedProcesses = String.Empty;

                    foreach (var profile in _profiles)
                    {
                        updatedProcesses = SetProfileProcessAffinity(processlist, profile);
                    }

                    if (!String.IsNullOrEmpty(updatedProcesses))
                    {
                        Dispatcher.Invoke((Action)delegate
                        {
                            ShowMessage("Applied profiles: " + updatedProcesses, GreenColor);
                        });
                    }
                }

                // Check for process every 5 seconds
                System.Threading.Thread.Sleep(5000);
            }
        }

        /// <summary>
        /// Check if a newer version of the software is available
        /// </summary>
        private bool CheckForUpdates()
        {
            try
            {
                var web = new System.Net.WebClient();
                var url = "https://Mywk.Net/software.php?assembly=" + System.Reflection.Assembly.GetExecutingAssembly().GetName().Name;
                var responseString = web.DownloadString(url);

                foreach (var str in responseString.Split('\n'))
                {
                    if (str.Contains("Version"))
                    {
                        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                        if (version.Major + "." + version.Minor != str.Split('=')[1])
                            return true;
                    }
                }
            }
            catch (Exception)
            {
                return false;
            }

            return false;
        }

        /// <summary>
        /// Load last saved settings if any
        /// </summary>
        private void LoadSettings()
        {
            bool profileError = false;
            try
            {
                // Window size and position
                {
                    if (Settings.Default.WindowSize.Width != 0)
                    {
                        this.Width = Settings.Default.WindowSize.Width;
                        this.Height = Settings.Default.WindowSize.Height;
                    }

                    if (Settings.Default.WindowLeft != 0)
                        this.Left = Settings.Default.WindowLeft;

                    if (Settings.Default.WindowTop != 0)
                        this.Top = Settings.Default.WindowTop;
                }

                // Auto-apply
                AutoApplyCheckBox.IsChecked = AutoApply = Settings.Default.AutoApply;

                // Window title mode
                WindowTitleCheckBox.IsChecked = Settings.Default.WindowTitle;

                // Load profiles
                if (Settings.Default.Profiles != null && Settings.Default.Profiles.Count > 0)
                {
                    for (int i = 0; i < Settings.Default.Profiles.Count; i++)
                    {
                        // Suppress any errors on individual profiles, if one of them is somehow screwed at least not all of them are gone
                        try
                        {
                            var profile = new Profile(Settings.Default.Profiles[i]);
                            _profiles.Add(profile);
                            ProfilesListBox.Items.Add(profile);
                        }
                        catch (Exception)
                        {
                            profileError = true;
                        }
                    }
                }
            }
            catch (Exception)
            {
                profileError = true;
            }

            if(profileError)
                ShowMessage("An error occurred while attempting to load your profiles.", RedColor);
        }

        /// <summary>
        /// Refresh the process list without hanging the UI thread
        /// </summary>
        private bool _isRefreshing = false;
        private async Task RefreshProcessListAsync()
        {
            if (_isRefreshing)
                return;

            _isRefreshing = true;

            SetAllCoresListBoxSelection(false);
            ProcessesListBox.Items.Clear();

            Process[] processlist = null;

            await Task.Run(() =>
            {
                processlist = Process.GetProcesses();
            }).ContinueWith(antecedent =>
            {
                if (processlist != null)
                {
                    foreach (Process process in processlist)
                    {
                        if (!String.IsNullOrEmpty(process.MainWindowTitle) && process.MainWindowTitle != this.Title)
                        {
                            var processInfo = new ProcessInfo(process);

                            if ((bool) WindowTitleCheckBox.IsChecked)
                                processInfo.StringMode = ProcessInfo.Mode.MainWindowTitle;
                            
                            ProcessesListBox.Items.Add(processInfo);
                        }
                    }
                }

                _isRefreshing = false;

            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        /// <summary>
        /// Adds the amount of processor cores to the CoreListBox
        /// </summary>
        private void LoadCoreCount()
        {
            for (int i = 0; i < Environment.ProcessorCount; i++)
                CoresListBox.Items.Add(new CheckBox() { Content = "Core " + i });
        }


        /// <summary>
        /// Sets all cores as enabled/disabled
        /// </summary>
        /// <param name="state"></param>
        private void SetAllCoresListBoxSelection(bool state)
        {
            foreach (var core in CoresListBox.Items)
            {
                (core as CheckBox).IsChecked = state;
            }
        }

        /// <summary>
        /// Sets the core-related buttons as enabled/disabled
        /// </summary>
        /// <param name="state"></param>
        private void SetCoreButtons(bool state)
        {
            ApplyButton.IsEnabled = AddProfileButton.IsEnabled = SelectAllCoresButton.IsEnabled = DeselectAllCoresButton.IsEnabled = CoresListBox.IsEnabled = state;
        }

        /// <summary>
        /// Sets the profile-related buttons as enabled/disabled
        /// </summary>
        /// <param name="state"></param>
        private void SetProfileButtons(bool state)
        {
            ApplyProfileButton.IsEnabled = RemoveProfileButton.IsEnabled = state;
        }

        /// <summary>
        /// Gets the selected process bitmask string
        /// </summary>
        /// <returns>base 2 string containing the bitmask of the selected process processor affinity or null</returns>
        private string GetSelectedProcessBitmaskString()
        {
            string bitMaskString = null;
            if (ProcessesListBox.SelectedItem != null)
            {
                var selectedProcess = ((ProcessInfo) ProcessesListBox.SelectedItem);

                // We get the list of processes and refresh the selected process
                Process[] processlist = Process.GetProcesses();
                foreach (var process in processlist)
                {
                    if (process.ProcessName.ToLowerInvariant() == selectedProcess.Process.ProcessName)
                        selectedProcess.Process = process;
                }

                var bitMask = (ProcessesListBox.SelectedItem as ProcessInfo).Process.ProcessorAffinity.ToInt64();

                // Convert the bitmask to bits without using the BitArray class
                bitMaskString = Convert.ToString(bitMask, 2);
            }

            return bitMaskString;
        }


        /// <summary>
        /// Save last known window position
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void MainWindow_OnLocationChanged(object? sender, EventArgs e)
        {
            if (!_isLoaded) return;

            Settings.Default.WindowLeft = Application.Current.MainWindow.Left;
            Settings.Default.WindowTop = Application.Current.MainWindow.Top;
            Settings.Default.Save();
        }

        /// <summary>
        /// Save last known window size
        /// </summary>
        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (!_isLoaded) return;

            Settings.Default.WindowSize = e.NewSize;
            Settings.Default.Save();
        }

        /// <summary>
        /// Show the cores that process is able to use when the selected process changes
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ProcessesListBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLoaded)
                return;

            if (ProcessesListBox.SelectedItem == null)
            {
                SetCoreButtons(false);
                SelectedProcessName = String.Empty;
            }
            else
            {
                try
                {
                    SetAllCoresListBoxSelection(false);

                    string bitMaskString = GetSelectedProcessBitmaskString();

                    if (!String.IsNullOrEmpty(bitMaskString))
                    {
                        bool[] bits = bitMaskString.PadLeft(Environment.ProcessorCount, '0').Select(c => (c == '1')).ToArray();

                        // Go through the values and set them on the processor combobox accordingly
                        for (int i = 0; i < CoresListBox.Items.Count; i++)
                        {
                            (CoresListBox.Items[CoresListBox.Items.Count - 1 - i] as CheckBox).IsChecked = bits[i];
                        }

                        CoresListBox.IsEnabled = true;

                        // Hide error message if necessary to prevent confusing the user
                        if (WarningLabel.Visibility == Visibility.Visible)
                            WarningLabel.Visibility = Visibility.Hidden;

                        SetCoreButtons(true);

                        SelectedProcessName = ((ProcessInfo)ProcessesListBox.SelectedItem).Process.ProcessName;
                    }
                }
                catch (Exception exception)
                {
                    if ((uint)exception.HResult == 0x80004005) // ACCESSDENIED
                    {
                        ShowMessage("Access denied to process: " + ProcessesListBox.SelectedItem, RedColor);
                        CoresListBox.IsEnabled = false;
                        SetCoreButtons(false);
                    }
                }
            }
        }

        /// <summary>
        ///  A QND implementation of a temporary message using a label
        /// </summary>
        /// <param name="message"></param>
        private int _messageLabelShown = 0;
        private async void ShowMessage(string message, Color color)
        {
            _messageLabelShown++;

            WarningLabel.Content = message;
            WarningLabel.Foreground = new SolidColorBrush(color);
            WarningLabel.Visibility = Visibility.Visible;
            await Task.Run(async () => { await Task.Delay(4000); }).ContinueWith(antecedent =>
            {
                _messageLabelShown--;

                if (_messageLabelShown == 0)
                    WarningLabel.Visibility = Visibility.Hidden;

            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        /// <summary>
        /// Convert checkbox selections to the process affinity without using the BitArray class
        /// </summary>
        /// <returns></returns>
        private Int64 CoresListBoxSelectionToProcessAffinity()
        {
            if (ProcessesListBox.SelectedItems.Count == 1)
            {
                // Convert checkbox selections to the process affinity without using the BitArray class
                bool[] bitsx = new bool[CoresListBox.Items.Count];
                for (int i = 0; i < CoresListBox.Items.Count; i++)
                {
                    bitsx[i] = (bool) (CoresListBox.Items[i] as CheckBox).IsChecked;
                }

                if (bitsx.All(b => b == false))
                    throw new Exception("Please select at least one core.");
                else
                {
                    Int64 processAffinity = 0;
                    for (int i = 0; i < bitsx.Length; ++i)
                    {
                        if (bitsx[i])
#pragma warning disable CS0675 // Bitwise-or operator used on a sign-extended operand
                            processAffinity |= 1 << i;
#pragma warning restore CS0675 // Bitwise-or operator used on a sign-extended operand
                    }

                    return processAffinity;
                }
            }
            else
                throw new Exception("Please select a process.");
        }

        /// <summary>
        /// Saves current profile list to our default settings
        /// </summary>
        private void SaveProfiles()
        {
            if (Settings.Default.Profiles == null)
                Settings.Default.Profiles = new StringCollection();

            Settings.Default.Profiles.Clear();
            foreach (var profile in _profiles)
                Settings.Default.Profiles.Add(profile.ToBase64String());

            Settings.Default.Save();
        }


        /// <summary>
        /// Tray notify icon
        /// </summary>
        System.Windows.Forms.NotifyIcon notifyIcon = null;
        private void HideToTrayButton_Click(object sender, RoutedEventArgs e)
        {
            // Create our notify icon if it's still null
            if (notifyIcon == null)
            {
                notifyIcon = new System.Windows.Forms.NotifyIcon();
                notifyIcon.Icon = System.Drawing.Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetExecutingAssembly().Location);
                notifyIcon.Text = this.Title;
                notifyIcon.Click += NotifyIcon_Click;

                notifyIcon.BalloonTipIcon = System.Windows.Forms.ToolTipIcon.Info;
                notifyIcon.BalloonTipTitle = this.Title;
                notifyIcon.BalloonTipText = "Window is now hidden, click the tray bar icon to restore it.";
            }

            this.ShowInTaskbar = false;
            notifyIcon.Visible = true;

            // Here I'm just assuming anyone who can start a program with args doesn't need to be notified every time we minimize
            if (!((App)App.Current).StartMinimizedToTray)
                notifyIcon.ShowBalloonTip(2000);

            this.Hide();
        }

        private void NotifyIcon_Click(object sender, EventArgs e)
        {
            notifyIcon.Visible = false;
            this.ShowInTaskbar = true;
            this.Show();
        }

        /// <summary>
        /// Applies the selected affinity to the selected process
        /// </summary>
        /// <todo>
        /// Add multi-selection support
        /// </todo>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ApplyButton_OnClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var processAffinity = CoresListBoxSelectionToProcessAffinity();

                (ProcessesListBox.SelectedItem as ProcessInfo).Process.ProcessorAffinity = (IntPtr)processAffinity;
                ShowMessage("Applied successfully.", GreenColor);

            }
            catch (Exception exception)
            {
                ShowMessage(exception.Message, RedColor);
            }
        }

        /// <summary>
        /// Add profile to the list and save it into our default settings
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void AddProfileButton_OnClick(object sender, RoutedEventArgs e)
        {
            try
            {
                if (ProcessesListBox.SelectedItem != null)
                {
                    string processName = (ProcessesListBox.SelectedItem as ProcessInfo).Process.ProcessName;

                    if (!string.IsNullOrEmpty(processName))
                    {
                        // Check if profile already exists
                        foreach (var p in _profiles)
                        {
                            if (processName.ToLowerInvariant() == p.ProcessName.ToLowerInvariant())
                                throw new Exception("Profile for this process already exists.");
                        }

                        var processAffinity = CoresListBoxSelectionToProcessAffinity();

                        Profile profile = new Profile(processName, processAffinity);
                        _profiles.Add(profile);

                        ProfilesListBox.Items.Add(profile);

                        SaveProfiles();

                        // For convenience, select the just added profile
                        ProfilesListBox.SelectedItem = profile;
                        ProfilesListBox.Focus();
                    }
                }
            }
            catch (Exception exception)
            {
                ShowMessage(exception.Message, RedColor);
            }
        }

        /// <summary>
        /// Save the state of auto-apply when it changes
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void AutoApplyCheckBox_OnCheckedUnchecked(object sender, RoutedEventArgs e)
        {
                Settings.Default.AutoApply = AutoApply = (bool)AutoApplyCheckBox.IsChecked;
                Settings.Default.Save();
        }

        /// <summary>
        /// Prevent selection on CoresListBox
        /// </summary>
        private void CoresListBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            CoresListBox.SelectedIndex = -1;
        }

        /// <summary>
        /// Close window
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void CloseButton_OnClick(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        /// <summary>
        /// Minimize window
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void MinimizeButton_OnClick(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        /// <summary>
        /// Drag the window from anywhere
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed && e.ChangedButton == MouseButton.Left)
                this.DragMove();
        }

        /// <summary>
        /// Open the website for this program
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void BottomLabel_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var targetURL = "https://mywk.net/software/process-affinity-utility";
            var psi = new ProcessStartInfo
            {
                FileName = targetURL,
                UseShellExecute = true
            };
            Process.Start(psi);
        }


        /// <summary>
        /// Removes selected profile from our list
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void RemoveProfileButton_OnClick(object sender, RoutedEventArgs e)
        {
            if (ProfilesListBox.SelectedItems.Count > 0)
            {
                // Remove bottom to top
                for (int i = ProfilesListBox.SelectedItems.Count - 1; i >= 0; i--)
                {
                    var selectedProfile = (Profile)ProfilesListBox.SelectedItems[i];
                    _profiles.Remove(selectedProfile);
                    SaveProfiles();

                    ProfilesListBox.Items.Remove(selectedProfile);
                }

                if (ProfilesListBox.Items.Count > 0)
                {
                    ProfilesListBox.SelectedIndex = 0;
                    ProfilesListBox.Focus();
                }
            }
        }

        /// <summary>
        /// Change ProcessInfo string mode accordingly
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void WindowTitleCheckBox_OnCheckedUnchecked(object sender, RoutedEventArgs e)
        {
            Settings.Default.WindowTitle = (bool)WindowTitleCheckBox.IsChecked;
            Settings.Default.Save();

            ProcessInfo.Mode mode = ((bool) WindowTitleCheckBox.IsChecked
                ? ProcessInfo.Mode.MainWindowTitle
                : ProcessInfo.Mode.ProcessName);

            foreach (var process in ProcessesListBox.Items)
            {
                var processInfo = (process as ProcessInfo);
                processInfo.StringMode = mode;
            }

            // Just for convenience, re-select the last selected item if any
            if (ProcessesListBox.SelectedItems.Count > 0)
            {
                var selectedProcessName= ((ProcessInfo)ProcessesListBox.SelectedItem).Process.ProcessName;
                await RefreshProcessListAsync();

                foreach (var processInfoItem in ProcessesListBox.Items)
                {
                    if (((ProcessInfo) processInfoItem).Process.ProcessName == selectedProcessName)
                    {
                        ProcessesListBox.SelectedItem = processInfoItem;
                        break;
                    }
                }
            }
            else
                _ = RefreshProcessListAsync();

            ProcessesListBox.Focus();
        }

        /// <summary>
        /// Open as administrator
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OpenAsAdministratorBase_OnClick(object sender, RoutedEventArgs e)
        {
            ProcessStartInfo proc = new ProcessStartInfo();
            proc.UseShellExecute = true;
            proc.WorkingDirectory = Environment.CurrentDirectory;
            proc.FileName = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;

            proc.Verb = "runas";

            try
            {
                Process.Start(proc);
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                ShowMessage("Unable to start as administrator.", RedColor);
            }
        }

        /// <summary>
        /// Apply the selected profile
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ApplyProfileButton_OnClick(object sender, RoutedEventArgs e)
        {
            if (ProfilesListBox.SelectedItems.Count > 0)
            {
                Process[] processlist = Process.GetProcesses();
                string updatedProcesses = String.Empty;

                foreach (Profile profile in ProfilesListBox.SelectedItems)
                {
                    updatedProcesses += SetProfileProcessAffinity(processlist, profile, true);
                }

                if (!String.IsNullOrEmpty(updatedProcesses)) 
                    ShowMessage("Applied profiles: " + updatedProcesses, GreenColor);
            }
        }

        /// <summary>
        /// Stop background worker before closing
        /// </summary>
        /// <remarks>
        /// Not really necessary but it doesn't hurt to make this
        /// </remarks>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void MainWindow_OnClosing(object sender, CancelEventArgs e)
        {
            _closePending = true;
        }

        private void refreshButton_OnClick(object sender, RoutedEventArgs e)
        {
            _ = RefreshProcessListAsync();
        }

        private void UpdateLabel_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            BottomLabel_OnMouseLeftButtonDown(sender, e);
        }

        private void SelectAllButton_OnClick(object sender, RoutedEventArgs e)
        {
            SetAllCoresListBoxSelection(true);
        }

        private void DeselectAllButton_OnClick(object sender, RoutedEventArgs e)
        {
            SetAllCoresListBoxSelection(false);
        }

        private void ProfilesListBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            SetProfileButtons((ProfilesListBox.SelectedItem != null));
        }

        private void SelectAllProfilesButton_OnClick(object sender, RoutedEventArgs e)
        {
            ProfilesListBox.SelectAll();
            ProfilesListBox.Focus();
        }

    }
}

