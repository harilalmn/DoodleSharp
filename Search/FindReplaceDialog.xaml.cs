using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;

namespace DoodleSharp.Search
{
    /// <summary>
    /// Dialog for finding and replacing text in code files.
    /// </summary>
    public partial class FindReplaceDialog : Window
    {
        private readonly FindReplaceService _findService;
        private bool _showReplace = true;

        /// <summary>
        /// Event raised when Find All is clicked.
        /// </summary>
        public event EventHandler<SearchOptions>? FindAllRequested;

        /// <summary>
        /// Event raised when Find Next is clicked.
        /// </summary>
        public event EventHandler<SearchOptions>? FindNextRequested;

        /// <summary>
        /// Event raised when Find Previous is clicked.
        /// </summary>
        public event EventHandler<SearchOptions>? FindPreviousRequested;

        /// <summary>
        /// Event raised when Replace is clicked.
        /// </summary>
        public event EventHandler<SearchOptions>? ReplaceRequested;

        /// <summary>
        /// Event raised when Replace All is clicked.
        /// </summary>
        public event EventHandler<SearchOptions>? ReplaceAllRequested;

        public FindReplaceDialog()
        {
            InitializeComponent();
            _findService = new FindReplaceService();

            // Allow Enter key to trigger Find Next
            FindTextBox.PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter && FindNextButton.IsEnabled)
                {
                    FindNextButton_Click(s, e);
                    e.Handled = true;
                }
            };
        }

        /// <summary>
        /// Gets or sets whether to show replace functionality.
        /// </summary>
        public bool ShowReplace
        {
            get => _showReplace;
            set
            {
                _showReplace = value;
                ReplaceRow.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
                ReplaceButton.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
                ReplaceAllButton.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
                Title = value ? "Find and Replace" : "Find";
            }
        }

        /// <summary>
        /// Gets or sets the initial search text.
        /// </summary>
        public string SearchText
        {
            get => FindTextBox.Text;
            set => FindTextBox.Text = value;
        }

        /// <summary>
        /// Gets the current search options from the dialog.
        /// </summary>
        public SearchOptions GetSearchOptions()
        {
            return new SearchOptions
            {
                SearchText = FindTextBox.Text,
                ReplaceText = ReplaceTextBox.Text,
                CaseSensitive = CaseSensitiveCheckBox.IsChecked == true,
                WholeWord = WholeWordCheckBox.IsChecked == true,
                UseRegex = UseRegexCheckBox.IsChecked == true,
                Scope = EntireProjectRadio.IsChecked == true
                    ? SearchScope.EntireProject
                    : SearchScope.CurrentFile
            };
        }

        /// <summary>
        /// Sets the status message.
        /// </summary>
        public void SetStatus(string message)
        {
            StatusText.Text = message;
        }

        /// <summary>
        /// Sets whether the scope options are enabled.
        /// </summary>
        public void SetScopeEnabled(bool enabled)
        {
            CurrentFileRadio.IsEnabled = enabled;
            EntireProjectRadio.IsEnabled = enabled;
        }

        /// <summary>
        /// Sets the scope to "Entire Project".
        /// </summary>
        public void SetProjectScope()
        {
            EntireProjectRadio.IsChecked = true;
        }

        private void FindTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            bool hasText = !string.IsNullOrEmpty(FindTextBox.Text);
            bool isValidPattern = hasText && _findService.IsValidPattern(GetSearchOptions());

            FindNextButton.IsEnabled = isValidPattern;
            FindAllButton.IsEnabled = isValidPattern;
            ReplaceButton.IsEnabled = isValidPattern;
            ReplaceAllButton.IsEnabled = isValidPattern;

            if (hasText && !isValidPattern)
            {
                StatusText.Text = "Invalid regular expression";
            }
            else
            {
                StatusText.Text = "";
            }
        }

        private void FindNextButton_Click(object sender, RoutedEventArgs e)
        {
            FindNextRequested?.Invoke(this, GetSearchOptions());
        }

        private void FindAllButton_Click(object sender, RoutedEventArgs e)
        {
            FindAllRequested?.Invoke(this, GetSearchOptions());
        }

        private void ReplaceButton_Click(object sender, RoutedEventArgs e)
        {
            ReplaceRequested?.Invoke(this, GetSearchOptions());
        }

        private void ReplaceAllButton_Click(object sender, RoutedEventArgs e)
        {
            ReplaceAllRequested?.Invoke(this, GetSearchOptions());
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Hide();
        }

        /// <summary>
        /// Focus the Find text box when the dialog is shown.
        /// </summary>
        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            FindTextBox.Focus();
            FindTextBox.SelectAll();
        }

        /// <summary>
        /// Hide instead of close to preserve state.
        /// </summary>
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            e.Cancel = true;
            Hide();
        }
    }
}
