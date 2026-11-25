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

    }

    private async Task CheckGameUpdateAsync(string title, string id, IMobileNativePorts native)
    {
        SetStatus("Recherche d’une mise à jour du jeu…");
        string? error = null;
        try
        {
            await native.CheckUpdateAsync(id);
        }
        catch (HttpRequestException)
        {
            error = "Impossible de joindre le dépôt du jeu. Réessaie dans quelques minutes.";
        }
        catch (Exception failure)
        {
            error = failure.Message;
        }

        var port = SelectedNativePort;
        if ((port?.Id ?? _catalog.ElementAtOrDefault(_selected)?.Id) != id || !_presentationActive
            || _launching || _nativePreparation is not null || _gameOptionsOverlay.IsVisible
            || _downloadsOverlay.IsVisible || _storageOverlay.IsVisible) return;

        if (port is not null && native.IsUpdateAvailable(id) && native.CompatibilityError(id) is null)
        {
            SetStatus("");
            ShowUpdatePrompt("Mettre à jour " + title + " ?",
                $"Version {port.Version}. DecompShelf crée une copie des sauvegardes détectées avant de remplacer le moteur.",
                "Mettre à jour", () => _ = PrepareSelectedPortAsync(update: true));
            return;
        }
        if (error is not null)
        {
            SetStatus(error);
            return;
        }
        if (native.UpstreamUpdate(id) is not { } release)
        {
            SetStatus("Aucune mise à jour du jeu disponible.");
            return;
        }
        if (port is not null && native.CompatibilityError(id) is { } issue)
        {
            SetStatus(issue);
            return;
        }

        SetStatus("");
        if (release.CanInstall)
            ShowUpdatePrompt(title, $"Version {release.Version}. Android met à jour le port installé et conserve ses données.",
                "Mettre à jour", () => _ = PrepareSelectedPortAsync(update: true));
        else
            ShowUpdatePrompt(title, port?.ExternalInstall == true
                ? $"La version {release.Version} est disponible. Le développeur doit fournir un APK vérifiable."
                : $"La version {release.Version} sera installable après son intégration dans DecompShelf.",
                "Vérifier DecompShelf", CheckLauncherForPortUpdate);
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
