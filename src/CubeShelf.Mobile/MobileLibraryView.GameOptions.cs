using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace CubeShelf.Mobile;

public sealed partial class MobileLibraryView
{
    private readonly Border _gameOptionsOverlay=new(){IsVisible=false,ZIndex=110,Background=new SolidColorBrush(Color.Parse("#B3000000"))};
    private readonly List<Button> _gameOptionButtons=[];
    private int _gameOptionIndex;
    public void OptionsFromController()
    {
        SetControllerInput(true);
        if(_gameOptionsOverlay.IsVisible){CloseGameOptions();return;}
        if(_launching||_nativePreparation is not null||_downloadsOverlay.IsVisible||_storageOverlay.IsVisible||_inspectionCase is not null
            ||_platformHome.IsVisible||DualInputActive&&(HomeInput.IsPlatforms||HomeInput.IsPopupOpen)
            ||_catalog.ElementAtOrDefault(_selected) is not {} game)return;
        var panel=new StackPanel{Spacing=10};_gameOptionButtons.Clear();_gameOptionIndex=0;
        panel.Children.Add(new TextBlock{Text=game.Title,FontSize=23,FontWeight=FontWeight.SemiBold,Foreground=Brushes.White,TextWrapping=TextWrapping.Wrap});
        panel.Children.Add(new TextBlock{Text="Options du jeu · X / B : fermer",FontSize=13,Foreground=Brushes.LightGray});
        void Action(string text,Action action)
        {
            var button=MakeButton(text);button.MinHeight=48;
            var index=_gameOptionButtons.Count;button.GotFocus+=(_,_)=>_gameOptionIndex=index;
            button.Click+=(_,_)=>{action();CloseGameOptions();};panel.Children.Add(button);_gameOptionButtons.Add(button);
        }
        Action("Paramètres et préparation",ShowSelectedGameDetails);
        panel.Children.Add(new TextBlock{Text="Version, disponibilité et préparation du port.",FontSize=12,Foreground=Brushes.LightGray,TextWrapping=TextWrapping.Wrap});
        panel.Children.Add(new TextBlock{Text="Mods du jeu",FontSize=16,Foreground=Brushes.White});
        panel.Children.Add(new TextBlock{Text="La gestion des mods n’est pas encore disponible dans DecompShelf sur Android. Leur compatibilité dépend du port.",FontSize=13,Foreground=Brushes.LightGray,TextWrapping=TextWrapping.Wrap});
        if(_games.Storage is not null)Action("Stockage et sauvegardes",OpenStorage);
        Action("Retourner le boîtier",()=>{ShowSelectedGameDetails();FlipFromController();});
        Action("Fermer",()=>FocusPrimaryAction());
        ShowGameOptionsPanel(panel);
    }
    private void ShowGameOptionsPanel(StackPanel panel)
    {
        _gameOptionsOverlay.Child=new Border{Child=new ScrollViewer{Content=panel},Padding=new Thickness(20),Margin=new Thickness(12),MaxWidth=480,
            MaxHeight=Math.Max(180,Bounds.Height-28),HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,
            CornerRadius=new CornerRadius(22),Background=new SolidColorBrush(Color.Parse("#151D2D")),BorderBrush=new SolidColorBrush(Color.Parse("#344159")),BorderThickness=new Thickness(1)};
        _dualLibrary.StopMusic();_secondaryHome?.StopMusic();_gameOptionsOverlay.IsVisible=true;_gameOptionButtons[0].Focus();
    }
    private void OpenRetroAchievements()
    {
        if(_launching||_nativePreparation is not null||_storageOverlay.IsVisible)return;
        _gameOptionButtons.Clear();_gameOptionIndex=0;
        var panel=new StackPanel{Spacing=10};
        panel.Children.Add(new TextBlock{Text="RetroAchievements",FontSize=23,FontWeight=FontWeight.SemiBold,Foreground=Brushes.White});
        panel.Children.Add(new TextBlock{Text="Ports natifs compatibles uniquement.",Foreground=Brushes.LightGray,TextWrapping=TextWrapping.Wrap});
        var games=_allCatalog.Where(g=>_games.SupportsRetroAchievements(g.Id)).OrderBy(g=>g.Title).ToArray();
        foreach(var game in games)
        {
            var button=MakeButton("");button.Content=new TextBlock{Text=game.Title,TextWrapping=TextWrapping.Wrap,TextAlignment=TextAlignment.Center};
            Avalonia.Automation.AutomationProperties.SetName(button,game.Title);
            var index=_gameOptionButtons.Count;button.GotFocus+=(_,_)=>_gameOptionIndex=index;
            button.Click+=(_,_)=>
            {
                CloseGameOptions(false);_consolePicker.SelectedIndex=Array.FindIndex(_mobilePlatforms,p=>p.Id==game.ShelfConsole);
                ShowPersonalLibrary(false);var selected=Array.FindIndex(_catalog,g=>g.Id==game.Id);if(selected>=0)Select(selected,false);
            };
            panel.Children.Add(button);_gameOptionButtons.Add(button);
        }
        if(games.Length==0)panel.Children.Add(new TextBlock{Text="Aucun port compatible confirmé sur cet appareil.",Foreground=Brushes.LightGray,TextWrapping=TextWrapping.Wrap});
        var close=MakeButton("Fermer");close.Click+=(_,_)=>CloseGameOptions();panel.Children.Add(close);_gameOptionButtons.Add(close);
        ShowGameOptionsPanel(panel);
    }
    private void ShowSelectedGameDetails()
    {
        if(_presentation.IsDual){_dualDetail=true;_dualLibrary.IsVisible=false;RebuildMobileShelf();}
        FocusPrimaryAction();
    }
    private void CloseGameOptions(bool restoreFocus=true)
    {
        if(!_gameOptionsOverlay.IsVisible)return;
        _gameOptionsOverlay.IsVisible=false;_gameOptionsOverlay.Child=null;_gameOptionButtons.Clear();
        if(restoreFocus){if(DualInputActive){HomeInput.Move(0,0);_dualLibrary.RefreshPreferences();}else FocusPrimaryAction();}
    }
    private void MoveGameOption(int step)
    {
        _gameOptionIndex=(_gameOptionIndex+step+_gameOptionButtons.Count)%_gameOptionButtons.Count;
        var button=_gameOptionButtons[_gameOptionIndex];button.Focus();button.BringIntoView();
    }
    private void ActivateGameOption()=>_gameOptionButtons.ElementAtOrDefault(_gameOptionIndex)?.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
}
