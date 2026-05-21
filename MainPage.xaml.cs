namespace MauiButtonLayoutNreRepro;

public partial class MainPage : ContentPage
{
    public MainPage()
    {
        InitializeComponent();
    }

    private async void OnGoToBugPageClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync(nameof(BugPage));
    }
}
