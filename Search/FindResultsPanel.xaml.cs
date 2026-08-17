using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DoodleSharp.Search
{
    /// <summary>
    /// Panel for displaying find/search results.
    /// </summary>
    public partial class FindResultsPanel : UserControl
    {
        private List<SearchResult> _results = new();

        /// <summary>
        /// Event raised when a search result is double-clicked or activated.
        /// </summary>
        public event EventHandler<SearchResult>? ResultActivated;

        /// <summary>
        /// Event raised when the Clear button is clicked.
        /// </summary>
        public event EventHandler? ResultsCleared;

        public FindResultsPanel()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Gets or sets the search results to display.
        /// </summary>
        public List<SearchResult> Results
        {
            get => _results;
            set
            {
                _results = value ?? new List<SearchResult>();
                UpdateDisplay();
            }
        }

        /// <summary>
        /// Sets the search term to display in the header.
        /// </summary>
        public void SetSearchTerm(string searchTerm)
        {
            SearchTermText.Text = string.IsNullOrEmpty(searchTerm)
                ? ""
                : $"for \"{searchTerm}\"";
        }

        /// <summary>
        /// Clears all results.
        /// </summary>
        public void ClearResults()
        {
            _results.Clear();
            UpdateDisplay();
            SearchTermText.Text = "";
        }

        private void UpdateDisplay()
        {
            ResultsListView.ItemsSource = null;
            ResultsListView.ItemsSource = _results;

            if (_results.Count == 0)
            {
                ResultCountText.Text = "No results";
                NoResultsMessage.Visibility = Visibility.Visible;
                ResultsListView.Visibility = Visibility.Collapsed;
            }
            else
            {
                ResultCountText.Text = _results.Count == 1
                    ? "1 result"
                    : $"{_results.Count} results";
                NoResultsMessage.Visibility = Visibility.Collapsed;
                ResultsListView.Visibility = Visibility.Visible;
            }
        }

        private void ResultsListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (ResultsListView.SelectedItem is SearchResult result)
            {
                ResultActivated?.Invoke(this, result);
            }
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            ClearResults();
            ResultsCleared?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Navigates to a specific result by index and selects it.
        /// </summary>
        public void SelectResult(int index)
        {
            if (index >= 0 && index < _results.Count)
            {
                ResultsListView.SelectedIndex = index;
                ResultsListView.ScrollIntoView(ResultsListView.SelectedItem);
            }
        }

        /// <summary>
        /// Gets the currently selected result, if any.
        /// </summary>
        public SearchResult? SelectedResult => ResultsListView.SelectedItem as SearchResult;
    }
}
