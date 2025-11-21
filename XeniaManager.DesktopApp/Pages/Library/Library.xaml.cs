using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Threading;
using System.Threading.Tasks;

// Imported
using Microsoft.Win32;
using Serilog;
using XeniaManager.DesktopApp.CustomControls;
using XeniaManager.DesktopApp.Windows;
using XeniaManager.Input;

namespace XeniaManager.DesktopApp.Pages
{
    /// <summary>
    /// Interaction logic for Library.xaml
    /// </summary>
    public partial class Library
    {
        // Buttons
        /// <summary>
        /// Enables/Disables showing of game titles on box arts
        /// </summary>
        private void ChkShowGameTitle_Click(object sender, RoutedEventArgs e)
        {
            ConfigurationManager.AppConfig.DisplayGameTitle = (bool)ChkShowGameTitle.IsChecked;
            ConfigurationManager.SaveConfigurationFile();
            LoadGames();
        }
        
        /// <summary>
        /// Opens FileDialog where user selects the game/games they want to add to Xenia Manager
        /// </summary>
        private void BtnAddGame_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Log.Information("Opening file dialog");
                OpenFileDialog openFileDialog = new OpenFileDialog
                {
                    Title = "Select a game",
                    Filter = "All Files|*|Supported Files|*.iso;*.xex;*.zar",
                    Multiselect = true
                };
                bool? result = openFileDialog.ShowDialog();
                if (result == false)
                {
                    Log.Information("Cancelling adding of games");
                    return;
                }

                // Checking what emulator versions are installed
                List<EmulatorVersion> installedXeniaVersions = new List<EmulatorVersion>();
                if (ConfigurationManager.AppConfig.XeniaCanary != null)
                    installedXeniaVersions.Add(EmulatorVersion.Canary);
                if (ConfigurationManager.AppConfig.XeniaMousehook != null)
                    installedXeniaVersions.Add(EmulatorVersion.Mousehook);
                if (ConfigurationManager.AppConfig.XeniaNetplay != null)
                    installedXeniaVersions.Add(EmulatorVersion.Netplay);

                switch (installedXeniaVersions.Count)
                {
                    case 0:
                        Log.Information("Xenia has not been installed");
                        MessageBox.Show("Xenia has not been installed");
                        break;
                    case 1:
                        Log.Information($"Only Xenia {installedXeniaVersions[0]} is installed");
                        // Calls for the function that adds the game into Xenia Manager
                        AddGames(openFileDialog.FileNames, installedXeniaVersions[0]);
                        break;
                    default:
                        Log.Information("Detected multiple Xenia installations");
                        Log.Information("Asking user what Xenia version will the game use");
                        XeniaSelection xeniaSelection = new XeniaSelection();
                        xeniaSelection.ShowDialog();
                        Log.Information($"User selected Xenia {xeniaSelection.UserSelection}");
                        AddGames(openFileDialog.FileNames, xeniaSelection.UserSelection);
                        break;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex.Message + "\nFull Error:\n" + ex);
                return;
            }
        }

        // SearchBar
        /// <summary>
        /// If SearchBar is focused, check if it has placeholder text and remove it and reset the foreground color
        /// </summary>
        private void TxtSearchBar_GotFocus(object sender, RoutedEventArgs e)
        {
            TextBox textBox = sender as TextBox;
            if (textBox.Text == "Search games by name")
            {
                textBox.Text = "";
                textBox.Foreground = (Brush)textBox.TryFindResource("ForegroundColor"); // Change text color to normal
            }
        }

        /// <summary>
        /// If SearchBar lost focus, check if it has any text and if it doesn't, apply placeholder text
        /// </summary>
        private void TxtSearchBar_LostFocus(object sender, RoutedEventArgs e)
        {
            TextBox textBox = sender as TextBox;
            if (string.IsNullOrWhiteSpace(textBox.Text))
            {
                textBox.Text = "Search games by name";
                textBox.Foreground =
                    (Brush)textBox.TryFindResource("PlaceholderText"); // Change text color to gray for placeholder
            }
        }

        /// <summary>
        /// Executes code only when text has been changed
        /// </summary>
        private void TxtSearchBar_TextChanged(object sender, TextChangedEventArgs e)
        {
            TextBox textBox = sender as TextBox;

            // Don't execute search if it has placeholder text, or it's empty
            if (textBox.Text == "Search games by name" || string.IsNullOrWhiteSpace(textBox.Text))
            {
                // Reset the filter
                if (WpGameLibrary != null)
                {
                    foreach (var child in WpGameLibrary.Children)
                    {
                        if (child is GameButton gameButton)
                        {
                            gameButton.Visibility = Visibility.Visible;
                        }
                    }
                }

                return;
            }

            // Grab the searchQuery
            string searchQuery = textBox.Text.ToLower();

            // Search through games
            foreach (var child in WpGameLibrary.Children)
            {
                // Ensure the element is GameButton
                if (child is GameButton gameButton)
                {
                    // Check if game title contains the search query
                    if (gameButton.GameTitle.ToLower().Contains(searchQuery))
                    {
                        gameButton.Visibility = Visibility.Visible; // Show the button if it matches
                    }
                    else
                    {
                        gameButton.Visibility = Visibility.Collapsed; // Hide it if it doesn't match
                    }
                }
            }
        }

        private CancellationTokenSource _inputCts;

        private async void Page_Loaded(object sender, RoutedEventArgs e)
        {
            _inputCts = new CancellationTokenSource();
            try
            {
                await ControllerInputLoop(_inputCts.Token);
            }
            catch (OperationCanceledException) { }
        }

        private void Page_Unloaded(object sender, RoutedEventArgs e)
        {
            _inputCts?.Cancel();
        }

        private async Task ControllerInputLoop(CancellationToken token)
        {
            int selectedIndex = -1;
            DateTime lastInputTime = DateTime.MinValue;

            while (!token.IsCancellationRequested)
            {
                // Check if window is visible (game not running)
                if (Application.Current.MainWindow.Visibility != Visibility.Visible)
                {
                    await Task.Delay(500, token);
                    continue;
                }

                if (InputManager.IsConnected)
                {
                    if ((DateTime.Now - lastInputTime).TotalMilliseconds > 150)
                    {
                        bool inputDetected = false;
                        var visibleChildren = WpGameLibrary.Children.OfType<GameButton>().Where(b => b.Visibility == Visibility.Visible).ToList();
                        
                        if (visibleChildren.Count > 0)
                        {
                            // Initialize selection if needed
                            if (selectedIndex == -1 || selectedIndex >= visibleChildren.Count)
                            {
                                // Try to find currently focused item
                                int focusedIndex = -1;
                                for(int i=0; i<visibleChildren.Count; i++)
                                {
                                    if (visibleChildren[i].IsFocused)
                                    {
                                        focusedIndex = i;
                                        break;
                                    }
                                }
                                
                                if (focusedIndex != -1) selectedIndex = focusedIndex;
                                else selectedIndex = 0;
                                
                                visibleChildren[selectedIndex].Focus();
                            }

                            if (InputManager.IsDpadRightPressed())
                            {
                                selectedIndex++;
                                if (selectedIndex >= visibleChildren.Count) selectedIndex = 0;
                                inputDetected = true;
                            }
                            else if (InputManager.IsDpadLeftPressed())
                            {
                                selectedIndex--;
                                if (selectedIndex < 0) selectedIndex = visibleChildren.Count - 1;
                                inputDetected = true;
                            }
                            else if (InputManager.IsDpadDownPressed())
                            {
                                // Estimate items per row based on width
                                // Assuming ~160px per item (150 width + 10 margin)
                                double panelWidth = WpGameLibrary.ActualWidth;
                                int itemsPerRow = (int)(panelWidth / 160);
                                if (itemsPerRow < 1) itemsPerRow = 1;
                                
                                selectedIndex += itemsPerRow;
                                if (selectedIndex >= visibleChildren.Count) selectedIndex = visibleChildren.Count - 1;
                                inputDetected = true;
                            }
                            else if (InputManager.IsDpadUpPressed())
                            {
                                double panelWidth = WpGameLibrary.ActualWidth;
                                int itemsPerRow = (int)(panelWidth / 160);
                                if (itemsPerRow < 1) itemsPerRow = 1;
                                
                                selectedIndex -= itemsPerRow;
                                if (selectedIndex < 0) selectedIndex = 0;
                                inputDetected = true;
                            }

                            if (inputDetected)
                            {
                                visibleChildren[selectedIndex].Focus();
                                visibleChildren[selectedIndex].BringIntoView();
                                lastInputTime = DateTime.Now;
                            }

                            if (InputManager.IsAPressed())
                            {
                                // Debounce A button more
                                if ((DateTime.Now - lastInputTime).TotalMilliseconds > 500)
                                {
                                    visibleChildren[selectedIndex].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                                    lastInputTime = DateTime.Now.AddSeconds(1); // Extra delay
                                }
                            }
                        }
                    }
                }
                await Task.Delay(33, token); // ~30fps polling
            }
        }
    }
}