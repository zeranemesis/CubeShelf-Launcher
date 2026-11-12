using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace CubeShelf.Mobile;

public sealed partial class MobileLibraryView
{
    private readonly DispatcherTimer _updatePromptTimer=new(){Interval=TimeSpan.FromMilliseconds(700)};
    private readonly HashSet<(string Id,string Version)> _offeredUpdates=[];

    private void ScheduleUpdatePrompt()
    {
        _updatePromptTimer.Stop();
        if(_presentationActive&&_realized)_updatePromptTimer.Start();
    }

    private void OfferUpdate()
    {
        _updatePromptTimer.Stop();
        if(!_presentationActive||!_realized||!IsEffectivelyVisible||_launching||_nativePreparation is not null
            ||_downloadsOverlay.IsVisible||_storageOverlay.IsVisible||_gameOptionsOverlay.IsVisible
            ||_inspectionCase is not null||_consolePicker.IsDropDownOpen||_achievementLibrary
            ||DualInputActive&&HomeInput.IsPopupOpen)return;

        if(_games.AppUpdates is {} app&&app.Snapshot is {State:AppUpdateState.Available or AppUpdateState.Ready,Package:{} package}
            &&_offeredUpdates.Add(("launcher",package.Version)))
        {
            var ready=app.Snapshot.State==AppUpdateState.Ready;
            ShowUpdatePrompt("Mettre à jour DecompShelf ?",ready
                ? $"La version {package.Version} est prête à installer."
                : $"La version {package.Version} est disponible.",
                ready?"Installer":"Mettre à jour",()=>
                {
                    if(ready)_appUpdateText.Text=app.Install();
                    else{ShowDownloads();_ = DownloadAndInstallAppUpdateAsync(app);}
                });
            return;
        }

        if(_platformHome.IsVisible||_dualLibrary.IsVisible&&_dualPlatforms
            ||_catalog.ElementAtOrDefault(_selected) is not {} game||_games.NativePorts is not {} native)return;
        var port=SelectedNativePort;
        var id=port?.Id??game.Id;
        if(port is not null&&native.CompatibilityError(id) is not null)return;
        if(port is not null&&native.IsUpdateAvailable(id)&&_offeredUpdates.Add((id,port.Version)))
        {
            ShowUpdatePrompt("Mettre à jour "+game.Title+" ?",$"La version {port.Version} est prête à télécharger ou à préparer.",
                "Mettre à jour",()=>_ = PrepareSelectedPortAsync(update:true));
            return;
        }
        if(native.UpstreamUpdate(id) is not {} release||!_offeredUpdates.Add((id,release.Version)))return;
        var message=release.CanInstall?$"DecompShelf téléchargera la version Android {release.Version}. Android te demandera de confirmer l’installation."
            :port?.ExternalInstall==true?$"La version Android {release.Version} est disponible. Le développeur doit fournir un APK vérifiable."
            :$"La version Android {release.Version} sera installable après son intégration dans DecompShelf.";
        ShowUpdatePrompt(game.Title,message,release.CanInstall?"Mettre à jour":"Vérifier DecompShelf",()=>
        {
            if(release.CanInstall)_=PrepareSelectedPortAsync(update:true);
            else CheckLauncherForPortUpdate();
        });
    }

    private void CheckLauncherForPortUpdate()
    {
        if(_games.AppUpdates is not {} app)return;
        ShowDownloads();
        _=app.CheckAsync();
    }

    private void ShowUpdatePrompt(string title,string message,string accept,Action confirmed)
    {
        var light=UsesHome&&_presentation.LightHome;
        static IBrush ColorBrush(string color)=>new SolidColorBrush(Color.Parse(color));
        var ink=ColorBrush(light?"#202E38":"#F4F8FC");
        var panel=new StackPanel{Spacing=12};
        panel.Children.Add(new TextBlock{Text=title,FontSize=22,FontWeight=FontWeight.Bold,Foreground=ink,TextWrapping=TextWrapping.Wrap});
        panel.Children.Add(new TextBlock{Text=message,FontSize=15,Foreground=ink,TextWrapping=TextWrapping.Wrap});
        _gameOptionButtons.Clear();_gameOptionIndex=0;
        foreach(var label in new[]{"Plus tard",accept})
        {
            var button=MakeButton(label);
            button.Content=new TextBlock{Text=label,TextWrapping=TextWrapping.Wrap};
            button.MinHeight=48;button.Foreground=ink;button.HorizontalContentAlignment=global::Avalonia.Layout.HorizontalAlignment.Stretch;
            button.FocusAdorner=null;
            AutomationProperties.SetName(button,label);
            var index=_gameOptionButtons.Count;
            button.GotFocus+=(_,_)=>{_gameOptionIndex=index;_highlightGameOption?.Invoke();};
            button.Click+=(_,_)=>{CloseGameOptions();if(index==1)confirmed();};
            _gameOptionButtons.Add(button);panel.Children.Add(button);
        }
        var active=ColorBrush(light?"#D3E8EF":"#27475E");
        var idle=ColorBrush(light?"#EDF2F6":"#202B40");
        _highlightGameOption=()=>
        {
            for(var index=0;index<_gameOptionButtons.Count;index++)
                _gameOptionButtons[index].Background=index==_gameOptionIndex?active:idle;
        };
        _highlightGameOption();
        ShowGameOptionsPanel(panel,background:ColorBrush(light?"#F8FAFC":"#151D2D"),
            outline:ColorBrush(light?"#B9C8D1":"#52687A"));
    }
}
