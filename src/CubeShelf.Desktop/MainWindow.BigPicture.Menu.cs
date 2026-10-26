using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using CubeShelf.Core.Library;

namespace CubeShelf.Desktop;

public sealed partial class MainWindow
{
    private string _bigPictureMenuPage = "main";
    private int _bigPictureMenuIndex;
    private readonly List<Button> _bigPictureMenuButtons = new();
    private static readonly IBrush BigPictureMenuTextBrush = new SolidColorBrush(Color.Parse("#303A44"));

    private void ShowBigPictureMenu(string page = "main")
    {
        if (!_bigPictureActive) return;
        ShelfTransition.Stop();
        _bigPictureMenuPage=page;
        BigPictureMenuRows.Children.Clear(); _bigPictureMenuButtons.Clear();
        BigPictureMenuTitle.Text=page switch
        {
            "controller"=>P7("PARAMÈTRES MANETTE","CONTROLLER SETTINGS"),
            "platform"=>P7("PLATEFORMES","PLATFORMS"),
            "game"=>P7("OPTIONS DU JEU","GAME OPTIONS"),
            _=>P7("MENU PRINCIPAL","MAIN MENU")
        };
        BigPictureMenuHelp.Text=P7("↑ ↓ Sélectionner     A Valider     B Retour     START Fermer",
            "↑ ↓ Select     A Confirm     B Back     START Close");
        void Row(string text,Action action,string value="")
        {
            var content=new Grid { ColumnDefinitions=new ColumnDefinitions("*,Auto"),ColumnSpacing=18 };
            content.Children.Add(new TextBlock { Text=text,TextWrapping=TextWrapping.Wrap,VerticalAlignment=VerticalAlignment.Center });
            var detail=new TextBlock { Text=value.Length>0?value:"›",FontSize=16,VerticalAlignment=VerticalAlignment.Center };
            Grid.SetColumn(detail,1);content.Children.Add(detail);
            var button=new Button { Content=content,FontSize=20,Padding=new Thickness(20,15),
                HorizontalAlignment=HorizontalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Stretch };
            var index=_bigPictureMenuButtons.Count;
            button.GotFocus+=(_,_)=>FocusBigPictureMenu(index);
            button.Click+=(_,_)=>action();
            _bigPictureMenuButtons.Add(button);BigPictureMenuRows.Children.Add(button);
        }
        void Go(Action action) { CloseBigPictureMenu();action(); }
        if(page=="controller")
        {
            Row(P7("Inverser stick et croix","Swap stick and D-pad"),()=>
            {
                _preferences=_preferences with { SwapBigPictureNavigation=!_preferences.SwapBigPictureNavigation };
                _preferencesStore.Save(_preferences);ShowBigPictureMenu("controller");
                _bigPictureLastInput=-1;
            },_preferences.SwapBigPictureNavigation?P7("OUI","YES"):P7("NON","NO"));
            BigPictureMenuHelp.Text=_preferences.SwapBigPictureNavigation
                ? P7("Croix : jeux et plateformes · Stick : menus\nA Valider     B Retour","D-pad: games and platforms · Stick: menus\nA Confirm     B Back")
                : P7("Stick : jeux et plateformes · Croix : menus\nA Valider     B Retour","Stick: games and platforms · D-pad: menus\nA Confirm     B Back");
        }
        else if(page=="platform")
        {
            foreach(var (name,index) in new[]{("GameCube",0),("Wii",1),("Switch",2)})
                Row(name,()=>Go(()=>BigPictureConsoleShelfPicker.SelectedIndex=index),BigPictureConsoleShelfPicker.SelectedIndex==index?"✓":"");
        }
        else if(page=="game")
        {
            Row(P7("Jouer","Play"),()=>Go(()=>PlayBigPicture(this,new RoutedEventArgs())));
            if(BigPictureGame is { } game && PortVersionsFor(GameVariants.BaseOf(_catalogGames,game)) is { Length: >1 } versions)
                Row(P7("Version du port","Port version"),()=>
                {
                    var index=Array.FindIndex(versions,v=>v.GameId==game.Id);
                    SelectPortVersion(versions[(index+1)%versions.Length].GameId);ShowBigPictureMenu("game");
                },game.RuntimeName);
            Row(P7("Retourner le boîtier","Flip case"),()=>Go(()=>FlipBigPictureCase(this,new RoutedEventArgs())));
            Row(P7("Mods du jeu","Game mods"),()=>Go(()=>OpenBigPictureMods(this,new RoutedEventArgs())));
            if(OnlineCompanion.IsSupported(BigPictureGame))
                Row(P7("Jouer en ligne","Play online"),()=>Go(()=>OpenBigPictureOnline(this,new RoutedEventArgs())));
        }
        else
        {
            Row(P7("Reprendre","Resume"),CloseBigPictureMenu);
            Row(P7("Options du jeu","Game options"),()=>ShowBigPictureMenu("game"));
            Row(P7("Plateformes","Platforms"),()=>ShowBigPictureMenu("platform"));
            Row(P7("Paramètres manette","Controller settings"),()=>ShowBigPictureMenu("controller"));
            Row(P7("Vue de la collection","Collection layout"),()=>{ToggleBigPictureLayout(this,new RoutedEventArgs());ShowBigPictureMenu();},_bigPictureRibbon?P7("Ruban","Ribbon"):P7("Rangée","Row"));
            Row(P7("Amis","Friends"),()=>Go(()=>OpenBigPictureFriends(this,new RoutedEventArgs())));
            Row(P7("Paramètres avancés","Advanced settings"),()=>Go(()=>ParityShowSettings(this,new RoutedEventArgs())));
            Row(P7("Retour au bureau","Return to desktop"),()=>Go(()=>ExitBigPicture(this,new RoutedEventArgs())));
        }
        if(page!="main") Row(P7("Retour","Back"),()=>ShowBigPictureMenu());
        BigPictureMenuOverlay.IsVisible=true;FocusBigPictureMenu(0);
    }

    private void FocusBigPictureMenu(int index)
    {
        if(_bigPictureMenuButtons.Count==0)return;
        if(_bigPictureMenuIndex==(index+_bigPictureMenuButtons.Count)%_bigPictureMenuButtons.Count
            && _bigPictureMenuButtons[_bigPictureMenuIndex].IsFocused)return;
        _bigPictureMenuIndex=(index+_bigPictureMenuButtons.Count)%_bigPictureMenuButtons.Count;
        for(var i=0;i<_bigPictureMenuButtons.Count;i++)
        {
            _bigPictureMenuButtons[i].Background=i==_bigPictureMenuIndex?Brushes.Crimson:Brushes.Transparent;
            _bigPictureMenuButtons[i].Foreground=i==_bigPictureMenuIndex?Brushes.White:BigPictureMenuTextBrush;
        }
        var selected=_bigPictureMenuButtons[_bigPictureMenuIndex];
        if(!selected.IsFocused)selected.Focus();
        selected.BringIntoView();
    }

    private void CloseBigPictureMenu()
    {
        BigPictureMenuOverlay.IsVisible=false;
        if(BigPictureView.IsVisible)BigPicturePlayButton.Focus();
    }

    internal void HandleBigPictureControllerInput(int rawInput)
    {
        if(!_bigPictureActive)return;
        var input=Controls.BigPictureGamepad.MapNavigation(rawInput,_preferences.SwapBigPictureNavigation);
        if(LaunchTransition.IsVisible) { if(input==6)LaunchTransition.Stop();return; }
        if(BigPictureInputWindow()!=this) { if(input<11)HandleBigPicturePageInput(input);return; }
        if(input==15) { if(BigPictureMenuOverlay.IsVisible)CloseBigPictureMenu();else ShowBigPictureMenu();return; }
        if(BigPictureMenuOverlay.IsVisible)
        {
            if(input==6) { if(_bigPictureMenuPage=="main")CloseBigPictureMenu();else ShowBigPictureMenu(); }
            else if(input is 3 or 4)FocusBigPictureMenu(_bigPictureMenuIndex+(input==3?-1:1));
            else if(input==5 || input is 1 or 2 && _bigPictureMenuPage is "controller" or "game")
                _bigPictureMenuButtons[_bigPictureMenuIndex].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            return;
        }
        if(!BigPictureView.IsVisible || BigPictureInputWindow()!=this)
        {
            if(input<11)HandleBigPicturePageInput(input);
            return;
        }
        switch(input)
        {
            case 11:MoveBigPictureGame(-1);break;
            case 12:MoveBigPictureGame(1);break;
            case 13:BigPictureConsoleShelfPicker.SelectedIndex=(BigPictureConsoleShelfPicker.SelectedIndex+1)%3;break;
            case 14:BigPictureConsoleShelfPicker.SelectedIndex=(BigPictureConsoleShelfPicker.SelectedIndex+2)%3;break;
            case >=1 and <=4:ShowBigPictureMenu();break;
            case 5:PlayBigPicture(this,new RoutedEventArgs());break;
            case 6:ShowBigPictureMenu();break;
            case 7:FlipBigPictureCase(this,new RoutedEventArgs());break;
            case 8:ToggleBigPictureLayout(this,new RoutedEventArgs());break;
        }
    }
}
