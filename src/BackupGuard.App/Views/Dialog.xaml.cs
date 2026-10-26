using System.Windows;
using System.Windows.Input;

namespace BackupGuard.App.Views;

/// <summary>Diálogo no mesmo visual do app (avisos, confirmações, campos de texto e senha).</summary>
public partial class Dialog : Window
{
    private Func<string, string?>? _validate;

    private Dialog(string title, string message, string ok, string? cancel)
    {
        InitializeComponent();
        Owner = Application.Current.MainWindow is { IsLoaded: true } main && main != this ? main : null;
        if (Owner is null)
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        TitleText.Text = title;
        MessageText.Text = message;
        MessageText.Visibility = string.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;
        OkButton.Content = ok;
        if (cancel is null)
            CancelButton.Visibility = Visibility.Collapsed;
        else
            CancelButton.Content = cancel;
    }

    public static void Info(string title, string message) => new Dialog(title, message, "OK", null).ShowDialog();

    public static void Error(string title, string message) => Info(title, message);

    public static bool Confirm(string title, string message, string ok = "Confirmar") =>
        new Dialog(title, message, ok, "Cancelar").ShowDialog() == true;

    public static string? Prompt(string title, string message, string initial = "")
    {
        var d = new Dialog(title, message, "OK", "Cancelar");
        d.InputBox.Text = initial;
        d.InputBox.Visibility = Visibility.Visible;
        d.Loaded += (_, _) => d.InputBox.Focus();
        return d.ShowDialog() == true ? d.InputBox.Text : null;
    }

    /// <summary>Pede uma senha/token oculto. Com <paramref name="confirm"/>, exige digitar duas vezes.</summary>
    public static string? PromptSecret(string title, string message, bool confirm = false, Func<string, string?>? validate = null)
    {
        var d = new Dialog(title, message, "Salvar", "Cancelar");
        d.PasswordInput.Visibility = Visibility.Visible;
        if (confirm)
        {
            d.ConfirmLabel.Visibility = Visibility.Visible;
            d.PasswordConfirm.Visibility = Visibility.Visible;
        }
        d._validate = value =>
        {
            if (confirm && value != d.PasswordConfirm.Password)
                return "As duas digitações não conferem.";
            return validate?.Invoke(value);
        };
        d.Loaded += (_, _) => d.PasswordInput.Focus();
        return d.ShowDialog() == true ? d.PasswordInput.Password : null;
    }

    /// <summary>Mostra um segredo com botão Copiar (ex.: senha gerada).</summary>
    public static void ShowSecret(string title, string message, string secret)
    {
        var d = new Dialog(title, message, "Já guardei", null);
        d.SecretText.Text = secret;
        d.SecretPanel.Visibility = Visibility.Visible;
        d.ShowDialog();
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        if (PasswordInput.Visibility == Visibility.Visible)
        {
            var error = _validate?.Invoke(PasswordInput.Password);
            if (error is not null)
            {
                ErrorText.Text = error;
                ErrorText.Visibility = Visibility.Visible;
                return;
            }
        }
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;

    private void OnCopy(object sender, RoutedEventArgs e) => Clipboard.SetText(SecretText.Text);

    private void OnDrag(object sender, MouseButtonEventArgs e) => DragMove();
}
