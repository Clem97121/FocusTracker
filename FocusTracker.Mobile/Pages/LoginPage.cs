using FocusTracker.Mobile.Services;

namespace FocusTracker.Mobile.Pages;

// Вхід під обліковим записом Supabase, прив'язаним на ПК («Налаштування» → «Телефон»)
public class LoginPage : ContentPage
{
    private readonly Entry _project = new() { Placeholder = "Project ID (напр. jpzgqepfyolrwzknheyc)" };
    private readonly Entry _key = new() { Placeholder = "Publishable key (sb_publishable_…)" };
    private readonly Entry _email = new() { Placeholder = "Email", Keyboard = Keyboard.Email };
    private readonly Entry _password = new() { Placeholder = "Пароль", IsPassword = true };
    private readonly Button _signIn = new() { Text = "Увійти" };
    private readonly Label _status = new() { TextColor = Colors.Gray };

    public LoginPage()
    {
        Title = "FocusTracker";
        var account = new PhoneAccount();
        _project.Text = account.ProjectUrl.Length > 0 ? account.ProjectUrl : PhoneAccount.DefaultProjectUrl;
        _key.Text = account.PublishableKey.Length > 0 ? account.PublishableKey : PhoneAccount.DefaultPublishableKey;
        _email.Text = account.Email;

        _signIn.Clicked += async (_, _) => await SignInAsync();
        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = 24,
                Spacing = 12,
                Children =
                {
                    new Label { Text = "Вхід", FontSize = 24, FontAttributes = FontAttributes.Bold },
                    new Label
                    {
                        Text = "Email і пароль облікового запису Supabase, прив'язаного на ПК у «Налаштування» → «Телефон». Адресу проєкту й ключ уже заповнено.",
                        TextColor = Colors.Gray,
                    },
                    _project, _key, _email, _password, _signIn, _status,
                }
            }
        };
    }

    private async Task SignInAsync()
    {
        if (string.IsNullOrWhiteSpace(_project.Text) || string.IsNullOrWhiteSpace(_key.Text)
            || string.IsNullOrWhiteSpace(_email.Text) || string.IsNullOrEmpty(_password.Text))
        {
            _status.Text = "Заповніть усі поля.";
            return;
        }

        _signIn.IsEnabled = false;
        _status.Text = "Входжу…";
        try
        {
            await new PhoneAccount().SignInAsync(_project.Text, _key.Text, _email.Text, _password.Text);
            Application.Current!.Windows[0].Page = new NavigationPage(new MainPage());
        }
        catch (Exception ex)
        {
            _status.Text = $"Не вдалося увійти: {ex.Message}";
        }
        finally
        {
            _signIn.IsEnabled = true;
        }
    }
}
