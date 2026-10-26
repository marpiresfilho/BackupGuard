using System.Windows.Controls;

namespace BackupGuard.App.Views;

public partial class LogsView
{
    public LogsView() => InitializeComponent();

    // Mantém visíveis as linhas mais recentes (fim do arquivo).
    private void OnTextChanged(object sender, TextChangedEventArgs e) => LogBox.ScrollToEnd();
}
