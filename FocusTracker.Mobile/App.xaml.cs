using FocusTracker.Mobile.Pages;
using FocusTracker.Mobile.Services;

namespace FocusTracker.Mobile;

public partial class App : Application
{
	public App()
	{
		InitializeComponent();
		UserAppTheme = AppTheme.Dark;
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		var account = new PhoneAccount();
		Page start = account.IsConfigured ? new MainPage() : new LoginPage();
		return new Window(new NavigationPage(start));
	}
}
