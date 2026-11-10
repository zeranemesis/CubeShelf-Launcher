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
    private IBrush _bigPictureMenuTextBrush = Brushes.White;
    private IBrush _bigPictureMenuSelectionBrush = Brushes.Teal;
    private IBrush _bigPictureMenuSelectedTextBrush = Brushes.White;
    private Control? _bigPictureMenuReturnFocus;
    private readonly Dictionary<string,int> _bigPictureMenuPositions = new();
    private string BigPictureMenuParent => _bigPictureMenuPage is "controller" or "appearance" ? "settings" : "main";
    private bool BigPictureLightChrome => _presentation?.IsDual == true ? _presentation.LightHome
        : BigPictureView.IsVisible || ActualThemeVariant == Avalonia.Styling.ThemeVariant.Light;

    private void UpdateBigPictureChrome()
    {
        var theme=BigPictureLightChrome?Avalonia.Styling.ThemeVariant.Light:Avalonia.Styling.ThemeVariant.Dark;
        BigPictureChromeTheme.RequestedThemeVariant=BigPictureMenuTheme.RequestedThemeVariant=theme;
        foreach(var button in new[]{BigPictureConsolesButton,BigPictureMyGamesButton,BigPictureReturnButton,BigPictureKeyboardButton,BigPictureSettingsButton,BigPictureExitButton,BigPictureMenuCloseButton})button.Transitions=null;
    }

    private void ShowBigPictureMenu(string page = "main")
    {
        if (!_bigPictureActive) return;
        var game=!PlatformHome.IsVisible && (BigPictureView.IsVisible || DualScreenView.IsVisible&&!DualScreenView.IsPlatforms)
            ? BigPictureGame : null;
        if(page=="game" && game is null)page="main";
        if(!BigPictureMenuOverlay.IsVisible)_bigPictureMenuReturnFocus=FocusManager?.GetFocusedElement() as Control;
        DualScreenView.DismissPopup();ShelfTransition.Stop();UpdateBigPictureChrome();
        _bigPictureMenuPositions[_bigPictureMenuPage]=_bigPictureMenuIndex;
        _bigPictureMenuPage=page;
        BigPictureMenuRows.Children.Clear(); _bigPictureMenuButtons.Clear();
        var light=BigPictureLightChrome;
        _bigPictureMenuTextBrush=new SolidColorBrush(Color.Parse(light?"#243044":"#F3F5FC"));
        _bigPictureMenuSelectionBrush=new SolidColorBrush(Color.Parse(light?"#417D95":"#A8D7E2"));
        _bigPictureMenuSelectedTextBrush=light?Brushes.White:new SolidColorBrush(Color.Parse("#142835"));
        var muted=new SolidColorBrush(Color.Parse(light?"#66718B":"#B7C3DA"));
        BigPictureMenuTitle.Text=page switch
        {
            "controller"=>P7("Manette","Controller"),
            "appearance"=>P7("Affichage et audio","Display and audio"),
            "settings"=>P7("Paramètres","Settings"),
            "game"=>P7("Options du jeu","Game options"),
            "achievements"=>"RetroAchievements",
            _=>P7("Menu","Menu")
        };
        BigPictureMenuContext.Text=page=="achievements"?P7("Ports natifs compatibles uniquement.","Compatible native ports only."):page=="game"?game?.Title:page=="main"
            ?P7("Ta bibliothèque, tes jeux et tes réglages.","Your library, games and settings.")
            :P7("Menu  /  Paramètres","Menu  /  Settings");
        BigPictureMenuHelp.Text=P7("↑ ↓ Sélectionner · Entrée / A Valider · Échap / B Retour · F10 / START Fermer",
            "↑ ↓ Select · Enter / A Confirm · Esc / B Back · F10 / START Close");
        BigPictureMenuCloseButton.Content=P7("Fermer","Close");
        BigPictureMenuExitButton.Content=P7("Quitter Big Picture  ·  F11","Exit Big Picture  ·  F11");
        void Section(string text)
        {
            BigPictureMenuRows.Children.Add(new TextBlock {Text=text,Foreground=muted,FontSize=11,
                FontWeight=FontWeight.SemiBold,LetterSpacing=1.4,Margin=new Thickness(14,12,0,6)});
        }
        Button Row(string text,Action action,string description="",string value="")
        {
            var content=new Grid { ColumnDefinitions=new ColumnDefinitions("*,Auto"),ColumnSpacing=20 };
            var words=new StackPanel {Spacing=3,VerticalAlignment=VerticalAlignment.Center};
            words.Children.Add(new TextBlock {Text=text,TextWrapping=TextWrapping.Wrap,FontWeight=FontWeight.SemiBold});
            if(description.Length>0)words.Children.Add(new TextBlock {Text=description,FontSize=13,TextWrapping=TextWrapping.Wrap});
            content.Children.Add(words);
            var detail=new TextBlock { Text=value.Length>0?value:"›",FontSize=15,VerticalAlignment=VerticalAlignment.Center };
            Grid.SetColumn(detail,1);content.Children.Add(detail);
            var button=new Button { Content=content,FontSize=18,MinHeight=56,Padding=new Thickness(16,10),CornerRadius=new CornerRadius(14),
                Margin=new Thickness(0),BorderThickness=new Thickness(1),Transitions=null,RenderTransform=new ScaleTransform(),
                HorizontalAlignment=HorizontalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Stretch };
            Avalonia.Automation.AutomationProperties.SetName(button,text);
            var index=_bigPictureMenuButtons.Count;
            button.GotFocus+=(_,_)=>FocusBigPictureMenu(index);
            button.Click+=(_,_)=>action();
            _bigPictureMenuButtons.Add(button);BigPictureMenuRows.Children.Add(button);return button;
        }
        void Go(Action action) { CloseBigPictureMenu();action(); }
        void RefreshHome(){DualScreenView.Configure(_presentation!,_previewMusic.Play,Controls.BigPictureSounds.PlayFile);UpdateBigPictureChrome();ShowBigPictureMenu(page);}
        if(page=="achievements")
        {
            var compatible=CompatibleRetroAchievementGames();
            Section(P7("JEUX COMPATIBLES","COMPATIBLE GAMES"));
            foreach(var entry in compatible)Row(entry.Title,()=>Go(()=>OpenRetroAchievementGame(entry)),ShelfProduct.Resolve(entry.ShelfConsole).Console);
            if(compatible.Length==0)Section(P7("Aucun port installé ne déclare la prise en charge de RetroAchievements.","No installed port declares RetroAchievements support."));
        }
        else if(page=="controller")
        {
            Section(P7("NAVIGATION","NAVIGATION"));
            Row(P7("Inverser stick et croix","Swap stick and D-pad"),()=>
            {
                _preferences=_preferences with { SwapBigPictureNavigation=!_preferences.SwapBigPictureNavigation };
                _preferencesStore.Save(_preferences);ShowBigPictureMenu("controller");_bigPictureLastInput=-1;
            },_preferences.SwapBigPictureNavigation
                ?P7("Croix : jeux et consoles. Stick : menus.","D-pad: games and consoles. Stick: menus.")
                :P7("Stick : jeux et consoles. Croix : menus.","Stick: games and consoles. D-pad: menus."),
                _preferences.SwapBigPictureNavigation?P7("Inversées","Swapped"):P7("Par défaut","Default"));
        }
        else if(page=="appearance")
        {
            Section(P7("AFFICHAGE","DISPLAY"));
            Row(P7("Thème de la bibliothèque","Library theme"),()=>{LibraryStylePicker.SelectedIndex=_presentation?.IsDual==true?0:1;ShowBigPictureMenu(page);},
                P7("Conserve tes jeux et ta sélection.","Keeps your games and selection."),_presentation?.IsDual==true?"HOME":P7("Classique","Classic"));
            if(_presentation?.IsDual==true)
                Row(P7("Apparence","Appearance"),()=>{_presentation.ToggleHomeAppearance();RefreshHome();},value:_presentation.LightHome?P7("Claire","Light"):P7("Sombre","Dark"));
            else Row(P7("Disposition des jeux","Game layout"),()=>{ToggleBigPictureLayout(this,new RoutedEventArgs());ShowBigPictureMenu(page);},value:_bigPictureRibbon?P7("Ruban","Ribbon"):P7("Rangée","Row"));
            Section(P7("AUDIO","AUDIO"));
            if(_presentation?.IsDual==true)
                Row(P7("Musique d’ambiance","Background music"),()=>{_presentation.ToggleMusic();RefreshHome();},value:_presentation.MusicEnabled?P7("Activée","On"):P7("Coupée","Off"));
            Row(P7("Effets sonores","Sound effects"),()=>
            {
                if(_presentation?.IsDual==true){_presentation.ToggleSounds();RefreshHome();}
                else {BigPictureSoundsBox.IsChecked=!_preferences.BigPictureSounds;ShowBigPictureMenu(page);}
            },value:(_presentation?.IsDual==true?_presentation.SoundsEnabled:_preferences.BigPictureSounds)?P7("Activés","On"):P7("Coupés","Off"));
        }
        else if(page=="settings")
        {
            Section(P7("EXPÉRIENCE BIG PICTURE","BIG PICTURE EXPERIENCE"));
            Row(P7("Affichage et audio","Display and audio"),()=>ShowBigPictureMenu("appearance"),P7("Thème, apparence et sons.","Theme, appearance and sounds."));
            Row(P7("Manette","Controller"),()=>ShowBigPictureMenu("controller"),P7("Commandes du stick et de la croix.","Stick and D-pad navigation."));
            Section(P7("APPLICATION","APPLICATION"));
            Row(P7("Chercher mes jeux…","Find my games…"),()=>FindMyGames(this,new RoutedEventArgs()),P7("Tes propres fichiers de jeux : rien n’est envoyé ni copié.","Your own game files: nothing is uploaded or copied."));
            Row(P7("Paramètres avancés","Advanced settings"),()=>Go(()=>ParityShowSettings(this,new RoutedEventArgs())),P7("Compte, réseau et préférences générales.","Account, network and general preferences."));
        }
        else if(page=="game" && game is not null)
        {
            Section(P7("JOUER","PLAY"));
            Row(QuickPlayLabel(game),()=>Go(()=>PlayBigPicture(this,new RoutedEventArgs()))).IsEnabled=PlayIntentFor(game) is null;
            if(PortVersionsFor(GameVariants.BaseOf(_catalogGames,game)) is { Length: >1 } versions)
                Row(P7("Version du port","Port version"),()=>
                {
                    var index=Array.FindIndex(versions,v=>v.GameId==game.Id);
                    SelectPortVersion(versions[(index+1)%versions.Length].GameId);ShowBigPictureMenu("game");
                },value:game.RuntimeName);
            Section(P7("PERSONNALISER CE JEU","CUSTOMIZE THIS GAME"));
            Row(P7("Paramètres du jeu","Game settings"),()=>Go(()=>SelectCatalogGame(game)),P7("Fichiers du jeu et programme associé.","Game files and linked program."));
            if(game.RuntimeOwnsSetup)Row(P7("Moteur et mises à jour","Engine and updates"),()=>Go(()=>OpenNativeEngineManager(game)),P7("Versions officielles, programme associé et retour au moteur précédent.","Upstream releases, linked program and previous engine."));
            Row(P7("Mods du jeu","Game mods"),()=>Go(()=>OpenBigPictureMods(this,new RoutedEventArgs())));
            if(BigPictureView.IsVisible)Row(P7("Retourner le boîtier","Flip case"),()=>Go(()=>FlipBigPictureCase(this,new RoutedEventArgs())));
            if(OnlineCompanion.IsSupported(game))Row(P7("Jouer en ligne","Play online"),()=>Go(()=>OpenBigPictureOnline(this,new RoutedEventArgs())));
        }
        else
        {
            Section(P7("BIBLIOTHÈQUE","LIBRARY"));
            Row(P7("Consoles","Consoles"),()=>Go(ShowPlatformHome),P7("Parcourir le catalogue des ports natifs.","Browse the native port catalogue."));
            Row(P7("Mes jeux","My games"),()=>Go(()=>OpenPersonalLibrary(this,new RoutedEventArgs())),P7("Tes jeux installés ou déjà ajoutés.","Your installed or added games."));
            if(game is not null){Section(P7("JEU SÉLECTIONNÉ","SELECTED GAME"));Row(P7("Options du jeu","Game options"),()=>ShowBigPictureMenu("game"),game.Title);}
            Section(P7("GÉRER","MANAGE"));
            Row(P7("Téléchargements","Downloads"),()=>Go(()=>ParityShowDownloads(this,new RoutedEventArgs())));
            Row(P7("Stockage et sauvegardes","Storage and saves"),()=>Go(()=>OpenGameStorage(this,new RoutedEventArgs())));
            Row(P7("Amis","Friends"),()=>Go(()=>OpenBigPictureFriends(this,new RoutedEventArgs())));
            Row(P7("Paramètres","Settings"),()=>ShowBigPictureMenu("settings"));
        }
        if(page!="main")Row(P7("← Retour","← Back"),()=>ShowBigPictureMenu(BigPictureMenuParent));
        _bigPictureMenuButtons.Add(BigPictureMenuExitButton);
        BigPictureMenuExitButton.Transitions=null;
        BigPictureMenuExitButton.GotFocus-=BigPictureMenuExitFocused;BigPictureMenuExitButton.GotFocus+=BigPictureMenuExitFocused;
        BigPictureMenuOverlay.IsVisible=true;
        FocusBigPictureMenu(Math.Min(_bigPictureMenuPositions.GetValueOrDefault(page),_bigPictureMenuButtons.Count-1));
    }

    private void BigPictureMenuExitFocused(object? sender,Avalonia.Input.GotFocusEventArgs args)=>FocusBigPictureMenu(_bigPictureMenuButtons.Count-1);
    private void CloseBigPictureMenuClick(object? sender,RoutedEventArgs args)=>CloseBigPictureMenu();
    private void FocusBigPictureMenu(int index)
    {
        if(_bigPictureMenuButtons.Count==0)return;
        var step=index<_bigPictureMenuIndex?-1:1;
        _bigPictureMenuIndex=(index+_bigPictureMenuButtons.Count)%_bigPictureMenuButtons.Count;
        for(var attempt=0;attempt<_bigPictureMenuButtons.Count&&!_bigPictureMenuButtons[_bigPictureMenuIndex].IsEnabled;attempt++)
            _bigPictureMenuIndex=(_bigPictureMenuIndex+step+_bigPictureMenuButtons.Count)%_bigPictureMenuButtons.Count;
        for(var i=0;i<_bigPictureMenuButtons.Count;i++)
        {
            var selected=i==_bigPictureMenuIndex;
            _bigPictureMenuButtons[i].Background=selected?_bigPictureMenuSelectionBrush:Brushes.Transparent;
            _bigPictureMenuButtons[i].BorderBrush=selected?_bigPictureMenuSelectionBrush:
                _bigPictureMenuButtons[i]==BigPictureMenuExitButton?new SolidColorBrush(Color.Parse(BigPictureLightChrome?"#D8DEEA":"#3B4B69")):Brushes.Transparent;
            _bigPictureMenuButtons[i].Foreground=selected?_bigPictureMenuSelectedTextBrush:_bigPictureMenuTextBrush;
        }
        var focused=_bigPictureMenuButtons[_bigPictureMenuIndex];
        if(!focused.IsFocused)focused.Focus();
        focused.BringIntoView();
    }

    private void CloseBigPictureMenu()
    {
        var wasOpen=BigPictureMenuOverlay.IsVisible;BigPictureMenuOverlay.IsVisible=false;_bigPictureMenuPositions.Clear();_bigPictureMenuPage="main";_bigPictureMenuIndex=0;
        if(!wasOpen)return;
        if(_bigPictureMenuReturnFocus is {IsEffectivelyVisible:true,IsEffectivelyEnabled:true} previous)previous.Focus();
        else BigPictureConsolesButton.Focus();
        _bigPictureMenuReturnFocus=null;
    }

    private bool TryBigPictureXShortcut()
    {
        if(!_bigPictureActive||BigPictureMenuOverlay.IsVisible||LaunchTransition.IsVisible
            ||BigPictureInputWindow()!=this||BigPicturePageHasTextFocus()
            ||PlatformHome.IsVisible&&PlatformHome.SortHasFocus
            ||DualScreenView.IsVisible&&DualScreenView.IsPopupOpen)return false;
        if(PlatformHome.IsVisible||DualScreenView.IsVisible&&DualScreenView.IsPlatforms)
        {OpenPersonalLibrary(this,new RoutedEventArgs());return true;}
        if(!(BigPictureView.IsVisible||DualScreenView.IsVisible)||BigPictureGame is null)return false;
        ShowBigPictureMenu("game");return true;
    }

    internal void HandleBigPictureControllerInput(int rawInput)
    {
        if(!_bigPictureActive)return;
        if(rawInput is >=1 and <=16)PlayBigPictureSound();
        if(rawInput==16){OpenPersonalLibrary(this,new RoutedEventArgs());return;}
        if(rawInput==7){TryBigPictureXShortcut();return;}
        if(rawInput==5&&!BigPictureMenuOverlay.IsVisible&&!LaunchTransition.IsVisible&&BigPictureInputWindow()==this
            &&BigPictureSubnav.IsKeyboardFocusWithin&&FocusManager?.GetFocusedElement() is Button toolbarButton)
        {toolbarButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));return;}
        if(PlatformHome.IsVisible && !BigPictureMenuOverlay.IsVisible)
        {
            if(rawInput is 1 or 11)PlatformHome.Move(-1);
            else if(rawInput is 2 or 12)PlatformHome.Move(1);
            else if(rawInput==5)PlatformHome.Choose();
            else if(rawInput==15)ShowBigPictureMenu();
            else if(rawInput is 8 or 13)PlatformHome.CycleSort();
            return;
        }
        var input=Controls.BigPictureGamepad.MapNavigation(rawInput,_preferences.SwapBigPictureNavigation);
        if(LaunchTransition.IsVisible) { if(input==6)LaunchTransition.Stop();return; }
        if(BigPictureInputWindow()!=this) { if(input<11)HandleBigPicturePageInput(input);return; }
        if(input==15) { if(BigPictureMenuOverlay.IsVisible)CloseBigPictureMenu();else ShowBigPictureMenu();return; }
        if(BigPictureMenuOverlay.IsVisible)
        {
            if(input==6) { if(_bigPictureMenuPage=="main")CloseBigPictureMenu();else ShowBigPictureMenu(BigPictureMenuParent); }
            else if(input is 3 or 4)FocusBigPictureMenu(_bigPictureMenuIndex+(input==3?-1:1));
            else if(input==5)
            {
                if(input==5&&BigPictureMenuCloseButton.IsFocused)CloseBigPictureMenu();
                else if(_bigPictureMenuButtons[_bigPictureMenuIndex].IsEnabled)_bigPictureMenuButtons[_bigPictureMenuIndex].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            return;
        }
        if(DualScreenView.IsVisible)
        {
            if(input==6&&DualScreenView.DismissPopup())return;
            if(input is 1 or 11)DualScreenView.Move(-1,0);
            else if(input is 2 or 12)DualScreenView.Move(1,0);
            else if(input is 3 or 13)DualScreenView.Move(0,-1);
            else if(input is 4 or 14)DualScreenView.Move(0,1);
            else if(input==5)DualScreenView.ActivateFocused();
            else if(input==6){if(DualScreenView.IsPlatforms)ExitBigPicture(this,new RoutedEventArgs());else ShowDualPlatforms();}
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
            case 13:BigPictureConsoleShelfPicker.SelectedIndex=(BigPictureConsoleShelfPicker.SelectedIndex+1)%BigPictureConsoleShelfPicker.ItemCount;break;
            case 14:BigPictureConsoleShelfPicker.SelectedIndex=(BigPictureConsoleShelfPicker.SelectedIndex+BigPictureConsoleShelfPicker.ItemCount-1)%BigPictureConsoleShelfPicker.ItemCount;break;
            case >=1 and <=4:ShowBigPictureMenu();break;
            case 5:PlayBigPicture(this,new RoutedEventArgs());break;
            case 6:ShowPlatformHome();break;
            case 8:ToggleBigPictureLayout(this,new RoutedEventArgs());break;
        }
    }
}
