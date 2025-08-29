using System.Linq;
using System.Windows;
using Core;

namespace PopupGuard;

public partial class RulesWindow : Window
{
    private readonly RuleSet _rules;

    public RulesWindow(RuleSet rules)
    {
        InitializeComponent();
        _rules = rules;
        RefreshList();
    }

    private void RefreshList()
    {
        BlockedList.ItemsSource = _rules.GetBlockedPaths().OrderBy(p => p).ToList();
    }

    private void OnUnblockClick(object sender, RoutedEventArgs e)
    {
        var selected = BlockedList.SelectedItem as string;
        if (string.IsNullOrWhiteSpace(selected)) return;
        if (_rules.RemoveBlock(selected))
        {
            _rules.Save();
            RefreshList();
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Close();
    }
}


