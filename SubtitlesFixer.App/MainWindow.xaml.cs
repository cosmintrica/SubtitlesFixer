using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using Media = System.Windows.Media;
using SubtitlesFixer.App.Subtitles;

namespace SubtitlesFixer.App;

public partial class MainWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly AppSettings _settings = AppSettings.Load();
    private readonly HashSet<string> _expandedPlanTitles = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _expandedPlanSeasons = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _expandedLastRunTitles = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _expandedLastRunSeasons = new(StringComparer.OrdinalIgnoreCase);

    private FixPlanPayload? _currentPlan;
    private FixSummaryPayload? _lastRun;
    private PreparedUpdate? _preparedUpdate;
    private string? _analyzedFolder;
    private bool _analyzedRecurse;
    private bool _analyzedOverwrite;
    private bool _autoUpdateChecked;
    private bool _isBusy;
    private bool _uiReady;
    private string _workProgressLabel = string.Empty;

    public MainWindow()
    {
        InitializeComponent();

        RecurseCheckBox.IsChecked = _settings.IncludeSubfolders;
        OverwriteRoCheckBox.IsChecked = _settings.OverwriteExistingRo;
        if (!string.IsNullOrWhiteSpace(_settings.LastFolderPath) && Directory.Exists(_settings.LastFolderPath))
            FolderPathBox.Text = _settings.LastFolderPath;

        PopulatePlanView(null);
        _lastRun = LastRunStore.Load();
        PopulateLastRunView(_lastRun);
        _uiReady = true;
        UpdatePlanHint();
        UpdateActionState();

        Loaded += MainWindow_Loaded;
        Closed += (_, _) => UpdateService.Release(_preparedUpdate?.Manager);
    }

    private void FolderPathBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        HandleInputsChanged();
    }

    private void Window_DragOver(object sender, System.Windows.DragEventArgs e)
    {
        if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
        {
            var paths = e.Data.GetData(System.Windows.DataFormats.FileDrop) as string[];
            if (paths is { Length: > 0 } && Directory.Exists(paths[0]))
            {
                e.Effects = System.Windows.DragDropEffects.Link;
                e.Handled = true;
                return;
            }
        }
        e.Effects = System.Windows.DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, System.Windows.DragEventArgs e)
    {
        if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop)
            && e.Data.GetData(System.Windows.DataFormats.FileDrop) is string[] paths
            && paths.Length > 0)
        {
            var target = paths[0];
            if (File.Exists(target))
                target = Path.GetDirectoryName(target) ?? target;
            if (Directory.Exists(target))
                FolderPathBox.Text = target;
        }
    }

    private void OptionChanged(object sender, RoutedEventArgs e)
    {
        HandleInputsChanged();
    }

    private void HandleInputsChanged()
    {
        if (!_uiReady)
            return;

        UpdatePlanHint();
        UpdateActionState();
    }

    private void SearchOnlineButton_Click(object sender, RoutedEventArgs e)
    {
        // Trece folderul curent ca sugestie in fereastra de cautare
        var currentFolder = FolderPathBox.Text?.Trim();
        var win = new SubtitleSearchWindow(_settings, currentFolder)
        {
            Owner = this,
        };
        win.ShowDialog();
    }

    internal void ReloadLastRunFromStore()
    {
        _lastRun = LastRunStore.Load();
        PopulateLastRunView(_lastRun);
        UpdateActionState();
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (_autoUpdateChecked)
            return;

        _autoUpdateChecked = true;
        await CheckForAutomaticUpdatesAsync().ConfigureAwait(true);
    }

    private async Task CheckForAutomaticUpdatesAsync()
    {
        if (!UpdateService.IsEligibleForAutomaticUpdates() || !UpdateService.ShouldCheckNow(_settings))
            return;

        var originalStatus = StatusTextBlock.Text;
        try
        {
            if (!_isBusy)
                StatusTextBlock.Text = "Verific update-uri...";

            var preparedUpdate = await UpdateService.CheckAndPrepareAsync(_settings).ConfigureAwait(true);
            if (preparedUpdate is null)
            {
                if (!_isBusy)
                    StatusTextBlock.Text = originalStatus;
                return;
            }

            _preparedUpdate = preparedUpdate;
            if (!_isBusy)
                StatusTextBlock.Text = $"{preparedUpdate.VersionLabel} este gata de instalare.";

            var answer = System.Windows.MessageBox.Show(
                $"Am descarcat {preparedUpdate.VersionLabel}. Vrei sa inchid aplicatia acum si sa instalez update-ul?",
                "Update disponibil",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (answer == MessageBoxResult.Yes)
            {
                preparedUpdate.Manager.ApplyUpdatesAndRestart(preparedUpdate.UpdateInfo);
                return;
            }

            AppendLogLine("[Update] " + preparedUpdate.VersionLabel + " este descarcat si poate fi instalat la urmatoarea pornire.");
        }
        catch (Exception ex)
        {
            AppendLogLine("[Update] Verificarea automata a fost sarita: " + ex.Message);
            if (!_isBusy)
                StatusTextBlock.Text = originalStatus;
        }
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        using var dlg = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Alege folderul cu seriale, filme si subtitrari",
            UseDescriptionForTitle = true,
        };

        if (TryGetExistingFolder(FolderPathBox.Text, out var existing))
            dlg.SelectedPath = existing;

        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            FolderPathBox.Text = dlg.SelectedPath;
    }

    private async void AnalyzeButton_Click(object sender, RoutedEventArgs e)
    {
        await AnalyzeAndLoadPlanAsync().ConfigureAwait(true);
    }

    private async void RunButton_Click(object sender, RoutedEventArgs e)
    {
        if (!await EnsureFreshPlanAsync().ConfigureAwait(true))
            return;

        if (_currentPlan?.Items is not { Count: > 0 })
        {
            System.Windows.MessageBox.Show(
                "Mai intai analizeaza folderul.",
                "Subtitles Fixer",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var selectionValidation = ValidatePlanSelections(_currentPlan);
        if (selectionValidation.Count > 0)
        {
            MainTabs.SelectedItem = PlanTab;
            PopulatePlanView(_currentPlan);
            StatusTextBlock.Text = "Mai trebuie verificat.";
            System.Windows.MessageBox.Show(
                string.Join(Environment.NewLine, selectionValidation),
                "Mai trebuie verificat",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var runnableItems = _currentPlan.Items.Where(IsRunnablePlanItem).ToList();
        if (runnableItems.Count == 0)
        {
            System.Windows.MessageBox.Show(
                "Nu exista nimic de schimbat cu setarile actuale.",
                "Subtitles Fixer",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (!TryGetFolder(out var folder) || !TryGetScriptPath(out var scriptPath))
            return;

        SaveCurrentSettings(folder);

        var recurse = RecurseCheckBox.IsChecked == true;
        var overwriteRo = OverwriteRoCheckBox.IsChecked == true;
        var summaryPath = CreateTempJsonPath("summary");
        string? selectionPath = null;

        try
        {
            SetBusyState(true);
            BeginWorkProgress("Procesez");
            StatusTextBlock.Text = "Procesez...";
            AppendLogSection("RULARE FIX");

            selectionPath = await WriteSelectionRequestAsync(_currentPlan).ConfigureAwait(true);
            await RunScriptAsync(
                    scriptPath,
                    folder,
                    recurse,
                    overwriteRo,
                    summaryPath: summaryPath,
                    selectionPath: selectionPath)
                .ConfigureAwait(true);

            var payload = await ReadSummaryPayloadAsync(summaryPath).ConfigureAwait(true);
            await PostProcessGeneratedSubtitlesAsync(payload).ConfigureAwait(true);
            InitializeLastRunSelectionState(payload);
            _lastRun = payload;
            LastRunStore.Save(payload);
            PopulateLastRunView(_lastRun);

            _currentPlan = null;
            _analyzedFolder = null;
            PopulatePlanView(null);
            UpdatePlanHint();
            UpdateActionState();

            MainTabs.SelectedItem = LastRunTab;
            StatusTextBlock.Text = "Rulare terminata.";
        }
        catch (Exception ex)
        {
            AppendLogLine("[EROARE APLICATIE] " + ex.Message);
            StatusTextBlock.Text = "Eroare.";
            ShowPlanMessage("Eroare la rulare: " + ex.Message, isError: true);
            System.Windows.MessageBox.Show(
                ex.Message,
                "Subtitles Fixer",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            TryDeleteFile(summaryPath);
            TryDeleteFile(selectionPath);
            EndWorkProgress();
            SetBusyState(false);
        }
    }

    private async void RestoreSelectedButton_Click(object sender, RoutedEventArgs e)
    {
        if (_lastRun?.Items is not { Count: > 0 })
            return;

        var selected = _lastRun.Items.Where(i => i.IsSelectedForRestore && CanRestoreItem(i)).ToList();
        if (selected.Count == 0)
        {
            System.Windows.MessageBox.Show(
                "Selecteaza cel putin un episod inainte de restore.",
                "Subtitles Fixer",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var result = System.Windows.MessageBox.Show(
            $"Voi restaura {selected.Count} episod(e) din backup. Continui?",
            "Confirmare restore",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
            return;

        try
        {
            SetBusyState(true);
            BeginWorkProgress("Restaurez");
            StatusTextBlock.Text = "Restaurez...";
            AppendLogSection("RESTAURARE DIN BACKUP");

            var outcomes = await RestoreSelectedItemsAsync(
                    selected,
                    new Progress<RestoreProgress>(progress =>
                        UpdateWorkProgress(progress.Current, progress.Total, progress.Label)))
                .ConfigureAwait(true);
            foreach (var outcome in outcomes)
            {
                var item = _lastRun.Items.FirstOrDefault(x =>
                    string.Equals(x.VideoPath, outcome.VideoPath, StringComparison.OrdinalIgnoreCase));
                if (item is null)
                    continue;

                item.IsSelectedForRestore = false;
                item.RestoreStatus = outcome.Success ? "restored" : "failed";
                item.RestoreMessage = outcome.Message;
                AppendLogLine($"[Restore] {(outcome.Success ? "OK" : "Eroare")}: {item.VideoName} - {outcome.Message}");
            }

            LastRunStore.Save(_lastRun);
            PopulateLastRunView(_lastRun);
            MainTabs.SelectedItem = LastRunTab;
            StatusTextBlock.Text = "Restore terminat.";
        }
        catch (Exception ex)
        {
            AppendLogLine("[EROARE RESTORE] " + ex.Message);
            StatusTextBlock.Text = "Eroare restore.";
            System.Windows.MessageBox.Show(
                ex.Message,
                "Subtitles Fixer",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            EndWorkProgress();
            SetBusyState(false);
        }
    }

    private void SelectAllRestoreButton_Click(object sender, RoutedEventArgs e)
    {
        if (_lastRun?.Items is not { Count: > 0 })
            return;

        foreach (var item in _lastRun.Items)
            item.IsSelectedForRestore = CanRestoreItem(item);

        PopulateLastRunView(_lastRun);
        UpdateActionState();
    }

    private void ClearRestoreSelectionButton_Click(object sender, RoutedEventArgs e)
    {
        if (_lastRun?.Items is not { Count: > 0 })
            return;

        foreach (var item in _lastRun.Items)
            item.IsSelectedForRestore = false;

        PopulateLastRunView(_lastRun);
        UpdateActionState();
    }

    private async Task<bool> AnalyzeAndLoadPlanAsync()
    {
        if (!TryGetFolder(out var folder) || !TryGetScriptPath(out var scriptPath))
            return false;

        SaveCurrentSettings(folder);

        var recurse = RecurseCheckBox.IsChecked == true;
        var overwriteRo = OverwriteRoCheckBox.IsChecked == true;
        var previewPath = CreateTempJsonPath("preview");
        string? selectionPath = null;

        try
        {
            SetBusyState(true);
            BeginWorkProgress("Analizez");
            StatusTextBlock.Text = "Analizez...";
            AppendLogSection("ANALIZA FOLDER");

            if (_currentPlan?.Items is { Count: > 0 } &&
                string.Equals(NormalizeFolderPath(folder), _analyzedFolder, StringComparison.OrdinalIgnoreCase))
            {
                selectionPath = await WriteSelectionRequestAsync(_currentPlan).ConfigureAwait(true);
            }

            await RunScriptAsync(
                    scriptPath,
                    folder,
                    recurse,
                    overwriteRo,
                    previewPath: previewPath,
                    selectionPath: selectionPath,
                    previewOnly: true)
                .ConfigureAwait(true);

            var payload = await ReadPlanPayloadAsync(previewPath).ConfigureAwait(true);
            _currentPlan = payload;
            _analyzedFolder = NormalizeFolderPath(folder);
            _analyzedRecurse = recurse;
            _analyzedOverwrite = overwriteRo;

            PopulatePlanView(payload);
            MainTabs.SelectedItem = PlanTab;
            StatusTextBlock.Text = "Analiza terminata.";
            UpdatePlanHint();
            UpdateActionState();
            return true;
        }
        catch (Exception ex)
        {
            AppendLogLine("[EROARE ANALIZA] " + ex.Message);
            StatusTextBlock.Text = "Eroare.";
            ShowPlanMessage("Nu am putut construi planul: " + ex.Message, isError: true);
            System.Windows.MessageBox.Show(
                ex.Message,
                "Subtitles Fixer",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return false;
        }
        finally
        {
            TryDeleteFile(previewPath);
            TryDeleteFile(selectionPath);
            EndWorkProgress();
            SetBusyState(false);
        }
    }

    private async Task<bool> EnsureFreshPlanAsync()
    {
        if (_currentPlan is not { Items: { Count: > 0 } } || !IsCurrentPlanFresh())
            return await AnalyzeAndLoadPlanAsync().ConfigureAwait(true);

        return true;
    }

    private bool IsCurrentPlanFresh()
    {
        var currentFolder = NormalizeFolderPath(FolderPathBox.Text);
        return _currentPlan is not null
               && !string.IsNullOrWhiteSpace(currentFolder)
               && string.Equals(currentFolder, _analyzedFolder, StringComparison.OrdinalIgnoreCase)
               && (RecurseCheckBox.IsChecked == true) == _analyzedRecurse
               && (OverwriteRoCheckBox.IsChecked == true) == _analyzedOverwrite;
    }

    private void SaveCurrentSettings(string folder)
    {
        _settings.LastFolderPath = folder;
        _settings.IncludeSubfolders = RecurseCheckBox.IsChecked == true;
        _settings.OverwriteExistingRo = OverwriteRoCheckBox.IsChecked == true;
        _settings.Save();
    }

    private static async Task<FixPlanPayload> ReadPlanPayloadAsync(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Fisierul cu planul de analiza lipseste.", path);

        var json = await File.ReadAllTextAsync(path, Encoding.UTF8).ConfigureAwait(false);
        return FixPlanPayloadParser.Parse(json)
               ?? throw new InvalidOperationException("Nu am putut interpreta planul generat.");
    }

    private static async Task<FixSummaryPayload> ReadSummaryPayloadAsync(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Fisierul cu rezultatul rularii lipseste.", path);

        var json = await File.ReadAllTextAsync(path, Encoding.UTF8).ConfigureAwait(false);
        return FixSummaryPayloadParser.Parse(json)
               ?? throw new InvalidOperationException("Nu am putut interpreta rezumatul rularii.");
    }

    private static async Task<string?> WriteSelectionRequestAsync(FixPlanPayload? plan)
    {
        if (plan?.Items is not { Count: > 0 })
            return null;

        var path = CreateTempJsonPath("selection");
        var items = plan.Items
            .Where(RequiresSubtitleSelection)
            .Where(i => !string.IsNullOrWhiteSpace(i.VideoPath))
            .Select(i => new
            {
                videoPath = i.VideoPath,
                selectedSubtitlePath = i.SelectedSubtitlePath,
                selectionMode = i.SelectionMode ?? "none",
            })
            .ToList();

        var json = JsonSerializer.Serialize(new { items }, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(path, json, Encoding.UTF8).ConfigureAwait(false);
        return path;
    }

    private async Task RunScriptAsync(
        string scriptPath,
        string folder,
        bool recurse,
        bool overwriteRo,
        string? summaryPath = null,
        string? previewPath = null,
        string? selectionPath = null,
        bool previewOnly = false)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        try
        {
            psi.StandardOutputEncoding = Encoding.UTF8;
            psi.StandardErrorEncoding = Encoding.UTF8;
        }
        catch (NotSupportedException)
        {
            // Unele configuratii ignora encodingul explicit.
        }

        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-ExecutionPolicy");
        psi.ArgumentList.Add("Bypass");
        psi.ArgumentList.Add("-File");
        psi.ArgumentList.Add(scriptPath);
        psi.ArgumentList.Add("-Paths");
        psi.ArgumentList.Add(folder);
        psi.ArgumentList.Add("-NoPause");

        if (!recurse)
            psi.ArgumentList.Add("-Recurse:$false");
        if (overwriteRo)
            psi.ArgumentList.Add("-OverwriteRo");
        if (!string.IsNullOrWhiteSpace(summaryPath))
        {
            psi.ArgumentList.Add("-SummaryJsonPath");
            psi.ArgumentList.Add(summaryPath);
        }
        if (!string.IsNullOrWhiteSpace(previewPath))
        {
            psi.ArgumentList.Add("-PreviewJsonPath");
            psi.ArgumentList.Add(previewPath);
        }
        if (!string.IsNullOrWhiteSpace(selectionPath))
        {
            psi.ArgumentList.Add("-SelectionJsonPath");
            psi.ArgumentList.Add(selectionPath);
        }
        if (previewOnly)
            psi.ArgumentList.Add("-PreviewOnly");

        using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };

        proc.OutputDataReceived += (_, args) =>
        {
            if (args.Data is not null)
                Dispatcher.Invoke(() =>
                {
                    if (!TryHandleProgressLine(args.Data))
                        AppendLogLine(args.Data);
                });
        };
        proc.ErrorDataReceived += (_, args) =>
        {
            if (args.Data is not null)
                Dispatcher.Invoke(() =>
                {
                    if (!TryHandleProgressLine(args.Data))
                        AppendLogLine(args.Data);
                });
        };

        if (!proc.Start())
            throw new InvalidOperationException("Nu s-a putut porni powershell.exe.");

        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        await proc.WaitForExitAsync().ConfigureAwait(true);
        proc.WaitForExit();

        AppendLogLine(string.Empty);
        AppendLogLine($"[Proces incheiat cu codul {proc.ExitCode}]");
        if (proc.ExitCode != 0)
            throw new InvalidOperationException($"Scriptul s-a oprit cu eroarea {proc.ExitCode}. Verifica jurnalul tehnic.");
    }

    private void PopulatePlanView(FixPlanPayload? payload)
    {
        PlanSummaryPanel.Children.Clear();

        if (payload?.Items is not { Count: > 0 })
        {
            PlanSummaryPanel.Children.Add(CreatePlaceholderText(
                "Apasa Analizeaza ca sa vezi clar ce se schimba si ce ramane asa."));
            return;
        }

        var duplicateSelections = GetDuplicatePlanSelectionPaths(payload);
        var computed = payload.Items.Select(item =>
        {
            var display = GetPlanDisplay(item, duplicateSelections);
            return new { Item = item, display.Status, display.Message };
        }).ToList();

        var pending = computed.Count(x => GetPlanSummaryBucket(x.Item, duplicateSelections) == "pending");
        var alreadyOk = computed.Count(x => GetPlanSummaryBucket(x.Item, duplicateSelections) == "already-ok");
        var review = computed.Count(x => x.Status == "review");
        var warn = computed.Count(x => x.Status == "warn");
        var err = computed.Count(x => x.Status == "error");

        var header = new WrapPanel
        {
            Margin = new Thickness(0, 0, 0, 12),
        };
        header.Children.Add(new TextBlock
        {
            Text = "Sumar analiza: ",
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 2, 10, 8),
        });
        header.Children.Add(MakeChip("Total", computed.Count, new Media.SolidColorBrush(Media.Color.FromRgb(0x6B, 0x72, 0x80))));
        header.Children.Add(MakeChip("Se schimba", pending, new Media.SolidColorBrush(Media.Color.FromRgb(0x39, 0x7A, 0xF6))));
        header.Children.Add(MakeChip("Nu se schimba", alreadyOk, TryBrush("SystemFillColorSuccessBrush", Media.Brushes.ForestGreen)));
        header.Children.Add(MakeChip("Alege manual", review, TryBrush("SystemFillColorCautionBrush", Media.Brushes.Goldenrod)));
        header.Children.Add(MakeChip("Atentie", warn, TryBrush("SystemFillColorCautionBrush", new Media.SolidColorBrush(Media.Color.FromRgb(0xB5, 0x7E, 0x00)))));
        header.Children.Add(MakeChip("Eroare", err, TryBrush("SystemFillColorCriticalBrush", Media.Brushes.IndianRed)));
        PlanSummaryPanel.Children.Add(header);

        if (duplicateSelections.Count > 0)
        {
            PlanSummaryPanel.Children.Add(new TextBlock
            {
                Text = "Aceeasi subtitrare este aleasa pentru mai multe episoade. Corecteaza cardurile marcate inainte sa rulezi.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = TryBrush("SystemFillColorCriticalBrush", Media.Brushes.IndianRed),
                Margin = new Thickness(0, 0, 0, 12),
            });
        }

        var kindGroups = payload.Items
            .GroupBy(i => GetMediaKind(i.Episode, i.VideoName, i.VideoPath))
            .OrderBy(g => MediaKindSortKey(g.Key))
            .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var kindGroup in kindGroups)
        {
            var kindItems = kindGroup.ToList();
            var kindHeader = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            kindHeader.Children.Add(BuildPlanMediaTypeHeader(
                kindGroup.Key,
                kindItems.Count,
                kindItems.Count(i => GetPlanSummaryBucket(i, duplicateSelections) == "pending"),
                kindItems.Count(i => GetPlanSummaryBucket(i, duplicateSelections) == "already-ok"),
                kindItems.Count(i => GetPlanDisplay(i, duplicateSelections).Status == "review"),
                kindItems.Count(i => GetPlanDisplay(i, duplicateSelections).Status == "warn"),
                kindItems.Count(i => GetPlanDisplay(i, duplicateSelections).Status == "error")));
            PlanSummaryPanel.Children.Add(kindHeader);

            var titleGroups = kindItems
                .GroupBy(i => GetMediaTitle(i.VideoName, i.VideoPath, i.Episode))
                .OrderBy(g => MediaTitleSortKey(g.Key))
                .ToList();

            foreach (var titleGroup in titleGroups)
            {
                var titleItems = titleGroup.ToList();
                var titleKey = BuildMediaGroupKey(kindGroup.Key, titleGroup.Key);
                var titleExpander = new Expander
                {
                    IsExpanded = _expandedPlanTitles.Contains(titleKey),
                    Margin = new Thickness(0, 0, 0, 8),
                    Header = BuildPlanMediaHeader(
                        titleGroup.Key,
                        titleItems.Count,
                        titleItems.Count(i => GetPlanSummaryBucket(i, duplicateSelections) == "pending"),
                        titleItems.Count(i => GetPlanSummaryBucket(i, duplicateSelections) == "already-ok"),
                        titleItems.Count(i => GetPlanDisplay(i, duplicateSelections).Status == "review"),
                        titleItems.Count(i => GetPlanDisplay(i, duplicateSelections).Status == "warn"),
                        titleItems.Count(i => GetPlanDisplay(i, duplicateSelections).Status == "error")),
                };
                titleExpander.Expanded += (_, _) => _expandedPlanTitles.Add(titleKey);
                titleExpander.Collapsed += (_, _) => _expandedPlanTitles.Remove(titleKey);

                var titleInner = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
                if (string.Equals(kindGroup.Key, "Seriale", StringComparison.OrdinalIgnoreCase))
                {
                    var seasonGroups = titleItems
                        .GroupBy(i => NormalizeSeasonKey(i.Season))
                        .OrderBy(g => SeasonSortKey(g.Key))
                        .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    var skipSeasonNesting = seasonGroups.Count == 1 && IsNoSeasonKey(seasonGroups[0].Key);

                    if (skipSeasonNesting)
                    {
                        foreach (var item in titleItems.OrderBy(i => NaturalEpisodeSort(i.Episode)).ThenBy(i => i.VideoName))
                            titleInner.Children.Add(CreatePlanCard(item, duplicateSelections));
                    }
                    else
                    {
                        foreach (var seasonGroup in seasonGroups)
                        {
                            var seasonItems = seasonGroup
                                .OrderBy(i => NaturalEpisodeSort(i.Episode))
                                .ThenBy(i => i.VideoName)
                                .ToList();
                            var seasonKey = titleKey + "|" + seasonGroup.Key;
                            var seasonExpander = new Expander
                            {
                                IsExpanded = _expandedPlanSeasons.Contains(seasonKey),
                                Margin = new Thickness(0, 0, 0, 8),
                                Header = BuildPlanSeasonHeader(
                                    seasonGroup.Key,
                                    seasonItems.Count,
                                    seasonItems.Count(i => GetPlanSummaryBucket(i, duplicateSelections) == "pending"),
                                    seasonItems.Count(i => GetPlanSummaryBucket(i, duplicateSelections) == "already-ok"),
                                    seasonItems.Count(i => GetPlanDisplay(i, duplicateSelections).Status == "review"),
                                    seasonItems.Count(i => GetPlanDisplay(i, duplicateSelections).Status == "warn"),
                                    seasonItems.Count(i => GetPlanDisplay(i, duplicateSelections).Status == "error")),
                            };
                            seasonExpander.Expanded += (_, _) => _expandedPlanSeasons.Add(seasonKey);
                            seasonExpander.Collapsed += (_, _) => _expandedPlanSeasons.Remove(seasonKey);

                            var seasonInner = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
                            foreach (var item in seasonItems)
                                seasonInner.Children.Add(CreatePlanCard(item, duplicateSelections));

                            seasonExpander.Content = seasonInner;
                            titleInner.Children.Add(seasonExpander);
                        }
                    }
                }
                else
                {
                    foreach (var item in titleItems.OrderBy(i => MediaTitleSortKey(i.VideoName)))
                        titleInner.Children.Add(CreatePlanCard(item, duplicateSelections));
                }

                titleExpander.Content = titleInner;
                PlanSummaryPanel.Children.Add(titleExpander);
            }
        }
    }

    private void PopulateLastRunView(FixSummaryPayload? payload)
    {
        LastRunSummaryPanel.Children.Clear();

        if (payload?.Items is not { Count: > 0 })
        {
            LastRunInfoText.Text = "Ultima rulare salvata apare aici. Selecteaza ce vrei sa readuci din backup.";
            LastRunSummaryPanel.Children.Add(CreatePlaceholderText(
                "Nu exista inca o rulare salvata."));
            UpdateActionState();
            return;
        }

        var totals = payload.Totals ?? new FixTotals();
        var restored = payload.Items.Count(i => string.Equals(i.RestoreStatus, "restored", StringComparison.OrdinalIgnoreCase));
        var failed = payload.Items.Count(i => string.Equals(i.RestoreStatus, "failed", StringComparison.OrdinalIgnoreCase));
        var selected = payload.Items.Count(i => i.IsSelectedForRestore && CanRestoreItem(i));
        LastRunInfoText.Text = selected > 0
            ? $"Ai selectat {selected} element(e) pentru restore."
            : "Ultima rulare salvata apare aici. Selecteaza ce vrei sa readuci din backup.";

        var header = new WrapPanel
        {
            Margin = new Thickness(0, 0, 0, 12),
        };
        header.Children.Add(new TextBlock
        {
            Text = "Sumar ultima rulare: ",
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 2, 10, 8),
        });
        header.Children.Add(MakeChip("Total", payload.Items.Count, new Media.SolidColorBrush(Media.Color.FromRgb(0x6B, 0x72, 0x80))));
        header.Children.Add(MakeChip("OK", totals.Ok, TryBrush("SystemFillColorSuccessBrush", Media.Brushes.ForestGreen)));
        header.Children.Add(MakeChip("Atentie", totals.Warn, TryBrush("SystemFillColorCautionBrush", Media.Brushes.Goldenrod)));
        header.Children.Add(MakeChip("Eroare", totals.Err, TryBrush("SystemFillColorCriticalBrush", Media.Brushes.IndianRed)));
        if (restored > 0)
            header.Children.Add(MakeChip("Restaurate", restored, new Media.SolidColorBrush(Media.Color.FromRgb(0x0F, 0x76, 0x78))));
        if (failed > 0)
            header.Children.Add(MakeChip("Restore esuat", failed, TryBrush("SystemFillColorCriticalBrush", Media.Brushes.IndianRed)));
        LastRunSummaryPanel.Children.Add(header);

        var kindGroups = payload.Items
            .GroupBy(i => GetMediaKind(i.Episode, i.VideoName, i.VideoPath))
            .OrderBy(g => MediaKindSortKey(g.Key))
            .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var kindGroup in kindGroups)
        {
            var kindItems = kindGroup.ToList();
            var kindHeader = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            kindHeader.Children.Add(BuildLastRunMediaTypeHeader(
                kindGroup.Key,
                kindItems.Count,
                kindItems.Count(x => string.Equals(x.Status, "ok", StringComparison.OrdinalIgnoreCase)),
                kindItems.Count(x => string.Equals(x.Status, "warn", StringComparison.OrdinalIgnoreCase)),
                kindItems.Count(x => string.Equals(x.Status, "error", StringComparison.OrdinalIgnoreCase)),
                kindItems.Count(x => string.Equals(x.RestoreStatus, "restored", StringComparison.OrdinalIgnoreCase))));
            LastRunSummaryPanel.Children.Add(kindHeader);

            var titleGroups = kindItems
                .GroupBy(i => GetMediaTitle(i.VideoName, i.VideoPath, i.Episode))
                .OrderBy(g => MediaTitleSortKey(g.Key))
                .ToList();

            foreach (var titleGroup in titleGroups)
            {
                var titleItems = titleGroup.ToList();
                var titleKey = BuildMediaGroupKey(kindGroup.Key, titleGroup.Key);
                var titleExpander = new Expander
                {
                    IsExpanded = _expandedLastRunTitles.Contains(titleKey),
                    Margin = new Thickness(0, 0, 0, 8),
                    Header = BuildLastRunMediaHeader(
                        titleGroup.Key,
                        titleItems.Count,
                        titleItems.Count(x => string.Equals(x.Status, "ok", StringComparison.OrdinalIgnoreCase)),
                        titleItems.Count(x => string.Equals(x.Status, "warn", StringComparison.OrdinalIgnoreCase)),
                        titleItems.Count(x => string.Equals(x.Status, "error", StringComparison.OrdinalIgnoreCase)),
                        titleItems.Count(x => string.Equals(x.RestoreStatus, "restored", StringComparison.OrdinalIgnoreCase))),
                };
                titleExpander.Expanded += (_, _) => _expandedLastRunTitles.Add(titleKey);
                titleExpander.Collapsed += (_, _) => _expandedLastRunTitles.Remove(titleKey);

                var titleInner = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
                if (string.Equals(kindGroup.Key, "Seriale", StringComparison.OrdinalIgnoreCase))
                {
                    var seasonGroups = titleItems
                        .GroupBy(i => NormalizeSeasonKey(i.Season))
                        .OrderBy(g => SeasonSortKey(g.Key))
                        .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    var skipSeasonNesting = seasonGroups.Count == 1 && IsNoSeasonKey(seasonGroups[0].Key);

                    if (skipSeasonNesting)
                    {
                        foreach (var item in titleItems.OrderBy(i => NaturalEpisodeSort(i.Episode)).ThenBy(i => i.VideoName))
                            titleInner.Children.Add(CreateLastRunCard(item));
                    }
                    else
                    {
                        foreach (var seasonGroup in seasonGroups)
                        {
                            var seasonItems = seasonGroup
                                .OrderBy(i => NaturalEpisodeSort(i.Episode))
                                .ThenBy(i => i.VideoName)
                                .ToList();
                            var seasonKey = titleKey + "|" + seasonGroup.Key;
                            var seasonExpander = new Expander
                            {
                                IsExpanded = _expandedLastRunSeasons.Contains(seasonKey),
                                Margin = new Thickness(0, 0, 0, 8),
                                Header = BuildLastRunSeasonHeader(
                                    seasonGroup.Key,
                                    seasonItems.Count,
                                    seasonItems.Count(x => string.Equals(x.Status, "ok", StringComparison.OrdinalIgnoreCase)),
                                    seasonItems.Count(x => string.Equals(x.Status, "warn", StringComparison.OrdinalIgnoreCase)),
                                    seasonItems.Count(x => string.Equals(x.Status, "error", StringComparison.OrdinalIgnoreCase)),
                                    seasonItems.Count(x => string.Equals(x.RestoreStatus, "restored", StringComparison.OrdinalIgnoreCase))),
                            };
                            seasonExpander.Expanded += (_, _) => _expandedLastRunSeasons.Add(seasonKey);
                            seasonExpander.Collapsed += (_, _) => _expandedLastRunSeasons.Remove(seasonKey);

                            var seasonInner = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
                            foreach (var item in seasonItems)
                                seasonInner.Children.Add(CreateLastRunCard(item));

                            seasonExpander.Content = seasonInner;
                            titleInner.Children.Add(seasonExpander);
                        }
                    }
                }
                else
                {
                    foreach (var item in titleItems.OrderBy(i => MediaTitleSortKey(i.VideoName)))
                        titleInner.Children.Add(CreateLastRunCard(item));
                }

                titleExpander.Content = titleInner;
                LastRunSummaryPanel.Children.Add(titleExpander);
            }
        }

        UpdateActionState();
    }

    private UIElement CreatePlanCard(FixPlanItem item, HashSet<string> duplicateSelections)
    {
        var display = GetPlanDisplay(item, duplicateSelections);
        var accent = PlanStatusBrush(item, display.Status);
        var isStandalone = IsStandaloneSubtitleMode(item.ItemMode);

        var border = new Border
        {
            Background = TryBrush("ControlFillColorSecondaryBrush", new Media.SolidColorBrush(Media.Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF))),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(0, 0, 0, 8),
        };

        var leftBar = new Border
        {
            Width = 4,
            Background = accent,
            CornerRadius = new CornerRadius(4, 0, 0, 4),
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(leftBar, 0);
        grid.Children.Add(leftBar);

        var details = new StackPanel { Margin = new Thickness(12, 0, 0, 0) };
        Grid.SetColumn(details, 1);
        grid.Children.Add(details);

        var row1 = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 4),
        };
        row1.Children.Add(new TextBlock
        {
            Text = GetPlanCardLead(item),
            FontWeight = FontWeights.Bold,
            FontSize = 14,
            Margin = new Thickness(0, 0, 8, 0),
        });
        row1.Children.Add(new TextBlock
        {
            Text = PlanStatusLabel(item, display.Status),
            FontWeight = FontWeights.SemiBold,
            Foreground = accent,
            FontSize = 13,
        });
        details.Children.Add(row1);

        if (!string.IsNullOrWhiteSpace(item.VideoName))
            details.Children.Add(CreateDetailLine(isStandalone ? "Fisier subtitrare:" : "Fisier video:", item.VideoName, emphasize: true));

        if (!string.IsNullOrWhiteSpace(item.TargetName) &&
            (!isStandalone || !string.Equals(item.TargetName, item.VideoName, StringComparison.OrdinalIgnoreCase)))
        {
            details.Children.Add(CreateDetailLine(isStandalone ? "Fisier dupa reparare:" : "Subtitrarea finala:", item.TargetName, emphasize: true));
        }

        var candidates = item.Candidates ?? new List<FixPlanCandidate>();
        if (candidates.Count > 0)
            details.Children.Add(CreateDetailLine("Subtitrari candidate:", candidates.Count.ToString(), emphasize: false));
        else if (item.SubtitleCount > 0)
            details.Children.Add(CreateDetailLine("Subtitrari .srt gasite aici:", item.SubtitleCount.ToString(), emphasize: false));

        details.Children.Add(CreateDetailLine(
            "Ce fac aici:",
            PlanActionLabel(item),
            emphasize: false,
            margin: new Thickness(0, 0, 0, 6)));

        if (candidates.Count > 1)
        {
            details.Children.Add(new TextBlock
            {
                Text = $"Am gasit {candidates.Count} subtitrari posibile:",
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 4),
            });

            details.Children.Add(new TextBlock
            {
                Text = "Daca varianta aleasa nu este buna, schimb-o aici inainte sa rulezi.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = TryBrush("TextFillColorSecondaryBrush", Media.Brushes.DimGray),
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 6),
            });

            var combo = new System.Windows.Controls.ComboBox
            {
                ItemsSource = candidates,
                DisplayMemberPath = nameof(FixPlanCandidate.Name),
                SelectedValuePath = nameof(FixPlanCandidate.Path),
                SelectedValue = item.SelectedSubtitlePath,
                MinWidth = 420,
                Margin = new Thickness(0, 0, 0, 6),
                IsEnabled = !string.Equals(item.Action, "skip-existing", StringComparison.OrdinalIgnoreCase)
                            && !string.Equals(item.Status, "error", StringComparison.OrdinalIgnoreCase),
            };
            combo.SelectionChanged += (_, _) =>
            {
                if (combo.SelectedItem is not FixPlanCandidate candidate)
                    return;

                item.SelectedSubtitlePath = candidate.Path;
                item.SelectedSubtitleName = candidate.Name;
                item.SelectionMode = "manual";
                PopulatePlanView(_currentPlan);
                UpdateActionState();
            };
            details.Children.Add(combo);

            details.Children.Add(new TextBlock
            {
                Text = string.Equals(item.SelectionMode, "manual", StringComparison.OrdinalIgnoreCase)
                    ? "Alegerea este manuala"
                    : "Alegerea este automata",
                Foreground = TryBrush("TextFillColorSecondaryBrush", Media.Brushes.DimGray),
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 4),
            });
        }
        else if (!string.IsNullOrWhiteSpace(item.SelectedSubtitleName))
        {
            details.Children.Add(CreateDetailLine(
                "Subtitrarea aleasa:",
                item.SelectedSubtitleName,
                emphasize: true));
        }

        if (!string.IsNullOrWhiteSpace(display.Message))
            details.Children.Add(CreateMessageBlock(display.Message, display.Status));

        border.Child = grid;
        return border;
    }

    private UIElement CreateLastRunCard(FixSummaryItem item)
    {
        var accent = RestoreAwareStatusBrush(item);
        var isStandalone = IsStandaloneSubtitleMode(item.ItemMode);

        var border = new Border
        {
            Background = TryBrush("ControlFillColorSecondaryBrush", new Media.SolidColorBrush(Media.Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF))),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(0, 0, 0, 8),
        };

        var leftBar = new Border
        {
            Width = 4,
            Background = accent,
            CornerRadius = new CornerRadius(4, 0, 0, 4),
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(leftBar, 0);
        grid.Children.Add(leftBar);

        var details = new StackPanel { Margin = new Thickness(12, 0, 0, 0) };
        Grid.SetColumn(details, 1);
        grid.Children.Add(details);

        var row1 = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 4),
        };

        if (CanRestoreItem(item))
        {
            var checkbox = new System.Windows.Controls.CheckBox
            {
                IsChecked = item.IsSelectedForRestore,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
            };
            checkbox.Checked += (_, _) =>
            {
                item.IsSelectedForRestore = true;
                PopulateLastRunView(_lastRun);
            };
            checkbox.Unchecked += (_, _) =>
            {
                item.IsSelectedForRestore = false;
                PopulateLastRunView(_lastRun);
            };
            row1.Children.Add(checkbox);
        }

        row1.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(item.Episode) ? "-" : item.Episode,
            FontWeight = FontWeights.Bold,
            FontSize = 14,
            Margin = new Thickness(0, 0, 8, 0),
        });
        row1.Children.Add(new TextBlock
        {
            Text = LastRunStatusLabel(item),
            FontWeight = FontWeights.SemiBold,
            Foreground = accent,
            FontSize = 13,
        });
        details.Children.Add(row1);

        if (!string.IsNullOrWhiteSpace(item.VideoName))
            details.Children.Add(CreateDetailLine(isStandalone ? "Fisier subtitrare:" : "Fisier video:", item.VideoName, emphasize: true));

        if (string.Equals(item.Status, "ok", StringComparison.OrdinalIgnoreCase))
        {
            if (!isStandalone && (!string.IsNullOrWhiteSpace(item.SubtitleBefore) || !string.IsNullOrWhiteSpace(item.SubtitleAfter)))
                details.Children.Add(CreateDetailLine(
                    "Subtitrare:",
                    $"{item.SubtitleBefore ?? "-"}  ->  {item.SubtitleAfter ?? "-"}",
                    emphasize: true));

            if (!string.IsNullOrWhiteSpace(item.EncodingDetected))
                details.Children.Add(CreateDetailLine(
                    "Encoding:",
                    item.EncodingDetected + " -> UTF-8 fara BOM",
                    emphasize: false));

            if (!string.IsNullOrWhiteSpace(item.Message))
                details.Children.Add(CreateMessageBlock(item.Message, item.Status ?? "ok"));
        }
        else if (!string.IsNullOrWhiteSpace(item.Message))
            details.Children.Add(CreateMessageBlock(item.Message, item.Status ?? string.Empty));

        if (!string.IsNullOrWhiteSpace(item.RestoreMessage))
            details.Children.Add(CreateDetailLine("Restore:", item.RestoreMessage, emphasize: false));

        border.Child = grid;
        return border;
    }

    private void ShowPlanMessage(string message, bool isError = false)
    {
        PlanSummaryPanel.Children.Clear();
        PlanSummaryPanel.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            Foreground = isError
                ? TryBrush("SystemFillColorCriticalBrush", Media.Brushes.IndianRed)
                : TryBrush("TextFillColorSecondaryBrush", Media.Brushes.DimGray),
        });
    }

    private static TextBlock CreateDetailLine(string label, string value, bool emphasize, Thickness? margin = null)
    {
        var brush = emphasize
            ? TryBrush("TextFillColorPrimaryBrush", Media.Brushes.WhiteSmoke)
            : TryBrush("TextFillColorSecondaryBrush", Media.Brushes.Gainsboro);

        var text = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13,
            Foreground = brush,
            Margin = margin ?? new Thickness(0, 0, 0, 4),
        };
        text.Inlines.Add(new Run(label + " ") { FontWeight = FontWeights.SemiBold, Foreground = brush });
        text.Inlines.Add(new Run(value) { Foreground = brush });
        return text;
    }

    private static TextBlock CreateMessageBlock(string message, string status)
    {
        var normalizedStatus = (status ?? string.Empty).Trim().ToLowerInvariant();
        var brush = normalizedStatus switch
        {
            "error" => TryBrush("SystemFillColorCriticalBrush", Media.Brushes.IndianRed),
            "warn" or "review" => TryBrush("SystemFillColorCautionBrush", Media.Brushes.Goldenrod),
            _ => TryBrush("TextFillColorSecondaryBrush", Media.Brushes.Gainsboro),
        };

        return new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            Foreground = brush,
            FontSize = 12.5,
            Margin = new Thickness(0, 2, 0, 0),
        };
    }

    private void BeginWorkProgress(string label)
    {
        _workProgressLabel = label;
        if (WorkProgressBar is null)
            return;

        WorkProgressBar.IsIndeterminate = true;
        WorkProgressBar.Minimum = 0;
        WorkProgressBar.Maximum = 100;
        WorkProgressBar.Value = 0;
        WorkProgressBar.Visibility = Visibility.Visible;
    }

    private void EndWorkProgress()
    {
        _workProgressLabel = string.Empty;
        if (WorkProgressBar is null)
            return;

        WorkProgressBar.IsIndeterminate = false;
        WorkProgressBar.Value = 0;
        WorkProgressBar.Visibility = Visibility.Collapsed;
    }

    private bool TryHandleProgressLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("__SF_PROGRESS__|", StringComparison.Ordinal))
            return false;

        var parts = line.Split('|', 4);
        if (parts.Length < 4)
            return true;

        if (!int.TryParse(parts[1], out var current))
            current = 0;
        if (!int.TryParse(parts[2], out var total))
            total = 0;

        UpdateWorkProgress(current, total, parts[3]);
        return true;
    }

    private void UpdateWorkProgress(int current, int total, string? itemLabel)
    {
        if (WorkProgressBar is not null)
        {
            WorkProgressBar.IsIndeterminate = total <= 0;
            WorkProgressBar.Visibility = Visibility.Visible;
            if (total > 0)
            {
                WorkProgressBar.Minimum = 0;
                WorkProgressBar.Maximum = total;
                WorkProgressBar.Value = Math.Max(0, Math.Min(current, total));
            }
        }

        var cleanLabel = Path.GetFileNameWithoutExtension(itemLabel ?? string.Empty);
        if (cleanLabel.Length > 56)
            cleanLabel = cleanLabel[..26] + "..." + cleanLabel[^24..];
        StatusTextBlock.Text = total > 0
            ? $"{_workProgressLabel} {current} din {total}" + (string.IsNullOrWhiteSpace(cleanLabel) ? string.Empty : $": {cleanLabel}")
            : _workProgressLabel + "...";
    }

    private static TextBlock CreatePlaceholderText(string message)
    {
        return new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12.5,
            Foreground = TryBrush("TextFillColorSecondaryBrush", Media.Brushes.Gray),
        };
    }

    private void UpdatePlanHint()
    {
        if (!_uiReady || PlanHintTextBlock is null || FolderPathBox is null || RecurseCheckBox is null || OverwriteRoCheckBox is null)
            return;

        PlanHintTextBlock.Visibility = _currentPlan is not null && !IsCurrentPlanFresh()
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void UpdateActionState()
    {
        if (!_uiReady ||
            FolderPathBox is null ||
            AnalyzeButton is null ||
            RunButton is null ||
            BrowseButton is null ||
            RecurseCheckBox is null ||
            OverwriteRoCheckBox is null ||
            SearchOnlineButton is null ||
            SelectAllRestoreButton is null ||
            ClearRestoreSelectionButton is null ||
            RestoreSelectedButton is null)
        {
            return;
        }

        var hasValidFolder = TryGetExistingFolder(FolderPathBox.Text, out _);
        AnalyzeButton.IsEnabled = !_isBusy && hasValidFolder;
        RunButton.IsEnabled = !_isBusy && hasValidFolder;
        SearchOnlineButton.IsEnabled = !_isBusy;

        var hasRestorableItems = _lastRun?.Items?.Any(CanRestoreItem) == true;
        var hasRestoreSelection = _lastRun?.Items?.Any(i => i.IsSelectedForRestore && CanRestoreItem(i)) == true;
        SelectAllRestoreButton.IsEnabled = !_isBusy && hasRestorableItems;
        ClearRestoreSelectionButton.IsEnabled = !_isBusy && hasRestoreSelection;
        RestoreSelectedButton.IsEnabled = !_isBusy && hasRestoreSelection;
    }

    private async Task PostProcessGeneratedSubtitlesAsync(FixSummaryPayload payload)
    {
        if (payload.Items is not { Count: > 0 })
            return;

        foreach (var item in payload.Items.Where(i =>
                     string.Equals(i.Status, "ok", StringComparison.OrdinalIgnoreCase) &&
                     !string.IsNullOrWhiteSpace(i.TargetPath) &&
                     File.Exists(i.TargetPath)))
        {
            var changed = await NormalizeSubtitleFileInPlaceAsync(item.TargetPath!).ConfigureAwait(true);
            if (!changed)
                continue;

            item.EncodingDetected = string.IsNullOrWhiteSpace(item.EncodingDetected)
                ? "Normalizare suplimentara aplicata"
                : item.EncodingDetected + " + normalizare suplimentara";
            if (!string.IsNullOrWhiteSpace(item.Message))
                item.Message += " Am reparat suplimentar caracterele stricate.";

            AppendLogLine("[Normalizare suplimentara] " + Path.GetFileName(item.TargetPath));
        }
    }

    private static async Task<bool> NormalizeSubtitleFileInPlaceAsync(string path)
    {
        var originalBytes = await File.ReadAllBytesAsync(path).ConfigureAwait(false);
        var decoded = SubtitleNormalizer.DecodeBytes(originalBytes);
        var normalized = SubtitleNormalizer.Normalize(decoded);
        var normalizedBytes = new UTF8Encoding(false).GetBytes(normalized);

        if (originalBytes.AsSpan().SequenceEqual(normalizedBytes))
            return false;

        await File.WriteAllTextAsync(path, normalized, new UTF8Encoding(false)).ConfigureAwait(false);
        return true;
    }

    private void SetBusyState(bool busy)
    {
        _isBusy = busy;
        if (FolderPathBox is null || BrowseButton is null || RecurseCheckBox is null || OverwriteRoCheckBox is null)
            return;

        FolderPathBox.IsEnabled = !busy;
        BrowseButton.IsEnabled = !busy;
        RecurseCheckBox.IsEnabled = !busy;
        OverwriteRoCheckBox.IsEnabled = !busy;
        UpdateActionState();
    }

    private static bool TryGetExistingFolder(string? raw, out string folder)
    {
        folder = string.Empty;
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        try
        {
            var full = Path.GetFullPath(raw.Trim());
            if (!Directory.Exists(full))
                return false;

            folder = full;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private bool TryGetFolder(out string folder)
    {
        if (TryGetExistingFolder(FolderPathBox.Text, out folder))
            return true;

        System.Windows.MessageBox.Show(
            "Alege un folder valid inainte sa continui.",
            "Subtitles Fixer",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
        return false;
    }

    private bool TryGetScriptPath(out string scriptPath)
    {
        try
        {
            scriptPath = ScriptLocator.GetScriptPath();
            return true;
        }
        catch (Exception ex)
        {
            scriptPath = string.Empty;
            System.Windows.MessageBox.Show(
                "Nu pot gasi fixsubs.ps1: " + ex.Message,
                "Subtitles Fixer",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return false;
        }
    }

    private static string? NormalizeFolderPath(string? path)
    {
        try
        {
            return string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(path.Trim());
        }
        catch
        {
            return null;
        }
    }

    private static string CreateTempJsonPath(string prefix)
    {
        var dir = Path.Combine(Path.GetTempPath(), "SubtitlesFixer");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, $"{prefix}-{Guid.NewGuid():N}.json");
    }

    private static void TryDeleteFile(string? path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Ignorat.
        }
    }

    private static List<string> ValidatePlanSelections(FixPlanPayload plan)
    {
        var messages = new List<string>();
        var duplicates = GetDuplicatePlanSelectionPaths(plan);
        if (duplicates.Count > 0)
            messages.Add("Aceeasi subtitrare este aleasa pentru mai multe episoade.");

        var missing = plan.Items?
            .Where(RequiresSubtitleSelection)
            .Where(i => string.IsNullOrWhiteSpace(i.SelectedSubtitlePath))
            .Select(i => $"{i.Episode ?? i.VideoName}: lipseste subtitrarea sursa.")
            .ToList();

        if (missing is { Count: > 0 })
            messages.AddRange(missing);

        return messages;
    }

    private static bool IsRunnablePlanItem(FixPlanItem item)
    {
        var action = (item.Action ?? string.Empty).Trim().ToLowerInvariant();
        return action is "create" or "overwrite" or "repair";
    }

    private static bool RequiresSubtitleSelection(FixPlanItem item)
    {
        var action = (item.Action ?? string.Empty).Trim().ToLowerInvariant();
        return action is "create" or "overwrite";
    }

    private static HashSet<string> GetDuplicatePlanSelectionPaths(FixPlanPayload payload)
    {
        return payload.Items?
            .Where(RequiresSubtitleSelection)
            .Where(i => !string.IsNullOrWhiteSpace(i.SelectedSubtitlePath))
            .GroupBy(i => i.SelectedSubtitlePath!, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase)
               ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    private static (string Status, string Message) GetPlanDisplay(FixPlanItem item, HashSet<string> duplicateSelections)
    {
        var baseStatus = (item.Status ?? string.Empty).Trim().ToLowerInvariant();
        var action = (item.Action ?? string.Empty).Trim().ToLowerInvariant();
        var message = item.Message ?? string.Empty;

        if (baseStatus == "error" || baseStatus == "warn")
            return (baseStatus, message);

        if (!string.IsNullOrWhiteSpace(item.SelectedSubtitlePath) && duplicateSelections.Contains(item.SelectedSubtitlePath))
            return ("review", "Aceeasi subtitrare este aleasa si la alt episod. Alege aici varianta corecta.");

        if (action == "already-ok")
            return ("ready", string.IsNullOrWhiteSpace(message)
                ? IsStandaloneSubtitleMode(item.ItemMode)
                    ? "Subtitrarea este deja curata. Nu schimb nimic aici."
                    : "Exista deja subtitrarea finala. Nu schimb nimic aici."
                : message);

        if (action == "repair")
            return ("ready", string.IsNullOrWhiteSpace(message)
                ? "Repar subtitrarea in acelasi fisier si mut originalul in backup."
                : message);

        if (RequiresSubtitleSelection(item) && string.IsNullOrWhiteSpace(item.SelectedSubtitlePath))
            return ("warn", string.IsNullOrWhiteSpace(message) ? "Nu am gasit nicio subtitrare potrivita pentru acest episod." : message);

        if ((item.Candidates?.Count ?? 0) > 1 && !string.Equals(item.SelectionMode, "manual", StringComparison.OrdinalIgnoreCase))
            return ("review", string.IsNullOrWhiteSpace(message)
                ? "Am gasit mai multe subtitrari posibile. Alege varianta buna inainte sa rulezi."
                : message);

        if (string.Equals(item.SelectionMode, "manual", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(message))
            return ("ready", "Folosesc alegerea facuta de tine.");

        return ("ready", message);
    }

    private static bool CanRestoreItem(FixSummaryItem item)
    {
        var hasNormalSourceRestore =
            !string.IsNullOrWhiteSpace(item.SourceOriginalPath) &&
            (!string.IsNullOrWhiteSpace(item.SourceBackupPath) || !string.IsNullOrWhiteSpace(item.BackupPath));
        var hasOldTargetBackup = !string.IsNullOrWhiteSpace(item.ReplacedTargetBackupPath);
        var canRemoveGeneratedOnlineTarget =
            string.IsNullOrWhiteSpace(item.SourceOriginalPath) &&
            !string.IsNullOrWhiteSpace(item.SourceBackupPath) &&
            !string.IsNullOrWhiteSpace(item.TargetPath);

        return string.Equals(item.Status, "ok", StringComparison.OrdinalIgnoreCase)
               && !string.Equals(item.RestoreStatus, "restored", StringComparison.OrdinalIgnoreCase)
               && (hasNormalSourceRestore || hasOldTargetBackup || canRemoveGeneratedOnlineTarget);
    }

    private static async Task<List<RestoreOutcome>> RestoreSelectedItemsAsync(IReadOnlyList<FixSummaryItem> items, IProgress<RestoreProgress>? progress = null)
    {
        return await Task.Run(() =>
        {
            var outcomes = new List<RestoreOutcome>();
            var total = items.Count;
            var current = 0;

            foreach (var item in items)
            {
                current++;
                progress?.Report(new RestoreProgress(current, total, item.VideoName ?? item.Episode ?? string.Empty));

                var videoPath = item.VideoPath ?? Guid.NewGuid().ToString("N");
                var sourceBackupPath = item.SourceBackupPath ?? item.BackupPath;
                var sourceOriginalPath = item.SourceOriginalPath;
                var targetPath = item.TargetPath;
                var replacedTargetBackupPath = item.ReplacedTargetBackupPath;
                var hasSourceBackup = !string.IsNullOrWhiteSpace(sourceBackupPath) && File.Exists(sourceBackupPath);
                var hasReplacedTargetBackup = !string.IsNullOrWhiteSpace(replacedTargetBackupPath) && File.Exists(replacedTargetBackupPath);
                var canRestoreSourceFile = hasSourceBackup && !string.IsNullOrWhiteSpace(sourceOriginalPath);
                var steps = new List<string>();
                var tempGeneratedTargetPath = string.Empty;
                var restoredSource = false;
                var restoredOldTarget = false;

                try
                {
                    if (!string.IsNullOrWhiteSpace(sourceBackupPath) &&
                        !File.Exists(sourceBackupPath) &&
                        !string.IsNullOrWhiteSpace(sourceOriginalPath))
                    {
                        throw new FileNotFoundException("Lipseste subtitrarea sursa din backup.", sourceBackupPath);
                    }
                    if (!string.IsNullOrWhiteSpace(replacedTargetBackupPath) && !File.Exists(replacedTargetBackupPath))
                        throw new FileNotFoundException("Lipseste varianta veche .ro.srt din backup.", replacedTargetBackupPath);
                    if (!canRestoreSourceFile && !hasReplacedTargetBackup && (string.IsNullOrWhiteSpace(targetPath) || !File.Exists(targetPath)))
                        throw new InvalidOperationException("Nu mai exista fisiere de backup disponibile pentru restaurare.");

                    if (canRestoreSourceFile)
                    {
                        if (File.Exists(sourceOriginalPath))
                            throw new InvalidOperationException("Exista deja subtitrarea sursa in folder; nu o suprascriu automat.");
                    }

                    if (hasReplacedTargetBackup && string.IsNullOrWhiteSpace(targetPath))
                        throw new InvalidOperationException("Lipseste calea tinta pentru .ro.srt.");

                    if (!string.IsNullOrWhiteSpace(targetPath) && File.Exists(targetPath))
                    {
                        tempGeneratedTargetPath = CreateSiblingTempPath(targetPath, ".restore-current");
                        File.Move(targetPath, tempGeneratedTargetPath);
                    }

                    if (canRestoreSourceFile && !string.IsNullOrWhiteSpace(sourceOriginalPath))
                    {
                        var sourceDir = Path.GetDirectoryName(sourceOriginalPath);
                        if (!string.IsNullOrWhiteSpace(sourceDir))
                            Directory.CreateDirectory(sourceDir);

                        File.Move(sourceBackupPath!, sourceOriginalPath);
                        restoredSource = true;
                        steps.Add("restaurata subtitrarea sursa");
                    }

                    if (hasReplacedTargetBackup && !string.IsNullOrWhiteSpace(targetPath))
                    {
                        var targetDir = Path.GetDirectoryName(targetPath);
                        if (!string.IsNullOrWhiteSpace(targetDir))
                            Directory.CreateDirectory(targetDir);

                        File.Move(replacedTargetBackupPath!, targetPath);
                        restoredOldTarget = true;
                        steps.Add("restaurat .ro.srt anterior");
                    }
                    else if (!string.IsNullOrWhiteSpace(tempGeneratedTargetPath))
                    {
                        steps.Add("sters .ro.srt generat");
                    }

                    if (!string.IsNullOrWhiteSpace(tempGeneratedTargetPath) && File.Exists(tempGeneratedTargetPath))
                        File.Delete(tempGeneratedTargetPath);

                    if (steps.Count == 0)
                        throw new InvalidOperationException("Nu a existat nimic de restaurat pentru acest episod.");

                    outcomes.Add(new RestoreOutcome(videoPath, true, string.Join(", ", steps)));
                }
                catch (Exception ex)
                {
                    var rollbackMessages = new List<string>();

                    if (restoredOldTarget &&
                        !string.IsNullOrWhiteSpace(replacedTargetBackupPath) &&
                        !string.IsNullOrWhiteSpace(targetPath) &&
                        File.Exists(targetPath) &&
                        !File.Exists(replacedTargetBackupPath))
                    {
                        try
                        {
                            File.Move(targetPath, replacedTargetBackupPath);
                            rollbackMessages.Add("am mutat inapoi .ro.srt anterior in backup");
                        }
                        catch (Exception rollbackEx)
                        {
                            rollbackMessages.Add("nu am putut muta inapoi .ro.srt anterior: " + rollbackEx.Message);
                        }
                    }

                    if (restoredSource &&
                        !string.IsNullOrWhiteSpace(sourceBackupPath) &&
                        !string.IsNullOrWhiteSpace(sourceOriginalPath) &&
                        File.Exists(sourceOriginalPath) &&
                        !File.Exists(sourceBackupPath))
                    {
                        try
                        {
                            var backupDir = Path.GetDirectoryName(sourceBackupPath);
                            if (!string.IsNullOrWhiteSpace(backupDir))
                                Directory.CreateDirectory(backupDir);

                            File.Move(sourceOriginalPath, sourceBackupPath);
                            rollbackMessages.Add("am mutat inapoi subtitrarea sursa in backup");
                        }
                        catch (Exception rollbackEx)
                        {
                            rollbackMessages.Add("nu am putut muta inapoi subtitrarea sursa: " + rollbackEx.Message);
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(tempGeneratedTargetPath) &&
                        !string.IsNullOrWhiteSpace(targetPath) &&
                        File.Exists(tempGeneratedTargetPath) &&
                        !File.Exists(targetPath))
                    {
                        try
                        {
                            File.Move(tempGeneratedTargetPath, targetPath);
                            rollbackMessages.Add("am restaurat .ro.srt generat");
                        }
                        catch (Exception rollbackEx)
                        {
                            rollbackMessages.Add("nu am putut pune la loc .ro.srt generat: " + rollbackEx.Message);
                        }
                    }

                    var message = ex.Message;
                    if (rollbackMessages.Count > 0)
                        message += " Rollback: " + string.Join("; ", rollbackMessages) + ".";

                    outcomes.Add(new RestoreOutcome(videoPath, false, message));
                }
            }

            return outcomes;
        }).ConfigureAwait(false);
    }

    private static string CreateSiblingTempPath(string targetPath, string suffix)
    {
        var directory = Path.GetDirectoryName(targetPath)
                        ?? throw new InvalidOperationException("Calea fisierului nu are director parinte.");
        var fileName = Path.GetFileName(targetPath);
        var candidate = Path.Combine(directory, fileName + suffix);
        var index = 1;
        while (File.Exists(candidate))
        {
            candidate = Path.Combine(directory, $"{fileName}{suffix}.{index}");
            index++;
        }

        return candidate;
    }

    private static void InitializeLastRunSelectionState(FixSummaryPayload payload)
    {
        if (payload.Items is null)
            return;

        foreach (var item in payload.Items)
        {
            item.IsSelectedForRestore = false;
            item.RestoreStatus = null;
            item.RestoreMessage = null;
        }
    }

    private static UIElement BuildPlanMediaTypeHeader(string kind, int total, int pending, int alreadyOk, int review, int warn, int err)
    {
        var sp = new WrapPanel
        {
            Margin = new Thickness(0, 4, 0, 8),
        };
        sp.Children.Add(new TextBlock
        {
            Text = kind,
            FontWeight = FontWeights.Bold,
            FontSize = 16,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
        });
        sp.Children.Add(MakeMini($"Fisiere - {total}", Media.Brushes.Gray));
        if (pending > 0) sp.Children.Add(MakeMini($"Se schimba - {pending}", new Media.SolidColorBrush(Media.Color.FromRgb(0x39, 0x7A, 0xF6))));
        if (alreadyOk > 0) sp.Children.Add(MakeMini($"Nu se schimba - {alreadyOk}", TryBrush("SystemFillColorSuccessBrush", Media.Brushes.ForestGreen)));
        if (review > 0) sp.Children.Add(MakeMini($"Alege manual - {review}", TryBrush("SystemFillColorCautionBrush", Media.Brushes.Goldenrod)));
        if (warn > 0) sp.Children.Add(MakeMini($"Atentie - {warn}", TryBrush("SystemFillColorCautionBrush", new Media.SolidColorBrush(Media.Color.FromRgb(0xB5, 0x7E, 0x00)))));
        if (err > 0) sp.Children.Add(MakeMini($"Eroare - {err}", TryBrush("SystemFillColorCriticalBrush", Media.Brushes.IndianRed)));
        return sp;
    }

    private static UIElement BuildLastRunMediaTypeHeader(string kind, int total, int ok, int warn, int err, int restored)
    {
        var sp = new WrapPanel
        {
            Margin = new Thickness(0, 4, 0, 8),
        };
        sp.Children.Add(new TextBlock
        {
            Text = kind,
            FontWeight = FontWeights.Bold,
            FontSize = 16,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
        });
        sp.Children.Add(MakeMini($"Fisiere - {total}", Media.Brushes.Gray));
        if (ok > 0) sp.Children.Add(MakeMini($"OK - {ok}", TryBrush("SystemFillColorSuccessBrush", Media.Brushes.ForestGreen)));
        if (warn > 0) sp.Children.Add(MakeMini($"Atentie - {warn}", TryBrush("SystemFillColorCautionBrush", Media.Brushes.Goldenrod)));
        if (err > 0) sp.Children.Add(MakeMini($"Eroare - {err}", TryBrush("SystemFillColorCriticalBrush", Media.Brushes.IndianRed)));
        if (restored > 0) sp.Children.Add(MakeMini($"Restaurate - {restored}", new Media.SolidColorBrush(Media.Color.FromRgb(0x0F, 0x76, 0x78))));
        return sp;
    }

    private static UIElement BuildPlanMediaHeader(string title, int total, int pending, int alreadyOk, int review, int warn, int err)
    {
        var sp = new WrapPanel();
        sp.Children.Add(new TextBlock
        {
            Text = title,
            FontWeight = FontWeights.SemiBold,
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0),
        });
        sp.Children.Add(MakeMini($"Fisiere - {total}", Media.Brushes.Gray));
        if (pending > 0) sp.Children.Add(MakeMini($"Se schimba - {pending}", new Media.SolidColorBrush(Media.Color.FromRgb(0x39, 0x7A, 0xF6))));
        if (alreadyOk > 0) sp.Children.Add(MakeMini($"Nu se schimba - {alreadyOk}", TryBrush("SystemFillColorSuccessBrush", Media.Brushes.ForestGreen)));
        if (review > 0) sp.Children.Add(MakeMini($"Alege manual - {review}", TryBrush("SystemFillColorCautionBrush", Media.Brushes.Goldenrod)));
        if (warn > 0) sp.Children.Add(MakeMini($"Atentie - {warn}", TryBrush("SystemFillColorCautionBrush", new Media.SolidColorBrush(Media.Color.FromRgb(0xB5, 0x7E, 0x00)))));
        if (err > 0) sp.Children.Add(MakeMini($"Eroare - {err}", TryBrush("SystemFillColorCriticalBrush", Media.Brushes.IndianRed)));
        return sp;
    }

    private static UIElement BuildLastRunMediaHeader(string title, int total, int ok, int warn, int err, int restored)
    {
        var sp = new WrapPanel();
        sp.Children.Add(new TextBlock
        {
            Text = title,
            FontWeight = FontWeights.SemiBold,
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0),
        });
        sp.Children.Add(MakeMini($"Fisiere - {total}", Media.Brushes.Gray));
        if (ok > 0) sp.Children.Add(MakeMini($"OK - {ok}", TryBrush("SystemFillColorSuccessBrush", Media.Brushes.ForestGreen)));
        if (warn > 0) sp.Children.Add(MakeMini($"Atentie - {warn}", TryBrush("SystemFillColorCautionBrush", Media.Brushes.Goldenrod)));
        if (err > 0) sp.Children.Add(MakeMini($"Eroare - {err}", TryBrush("SystemFillColorCriticalBrush", Media.Brushes.IndianRed)));
        if (restored > 0) sp.Children.Add(MakeMini($"Restaurate - {restored}", new Media.SolidColorBrush(Media.Color.FromRgb(0x0F, 0x76, 0x78))));
        return sp;
    }

    private static UIElement BuildPlanSeasonHeader(string seasonKey, int total, int pending, int alreadyOk, int review, int warn, int err)
    {
        var sp = new WrapPanel();
        sp.Children.Add(new TextBlock
        {
            Text = IsNoSeasonKey(seasonKey) ? "Alte episoade" : $"Sezon {seasonKey}",
            FontWeight = FontWeights.SemiBold,
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0),
        });
        sp.Children.Add(MakeMini($"Episoade - {total}", Media.Brushes.Gray));
        if (pending > 0) sp.Children.Add(MakeMini($"Se schimba - {pending}", new Media.SolidColorBrush(Media.Color.FromRgb(0x39, 0x7A, 0xF6))));
        if (alreadyOk > 0) sp.Children.Add(MakeMini($"Nu se schimba - {alreadyOk}", TryBrush("SystemFillColorSuccessBrush", Media.Brushes.ForestGreen)));
        if (review > 0) sp.Children.Add(MakeMini($"Alege manual - {review}", TryBrush("SystemFillColorCautionBrush", Media.Brushes.Goldenrod)));
        if (warn > 0) sp.Children.Add(MakeMini($"Atentie - {warn}", TryBrush("SystemFillColorCautionBrush", new Media.SolidColorBrush(Media.Color.FromRgb(0xB5, 0x7E, 0x00)))));
        if (err > 0) sp.Children.Add(MakeMini($"Eroare - {err}", TryBrush("SystemFillColorCriticalBrush", Media.Brushes.IndianRed)));
        return sp;
    }

    private static UIElement BuildLastRunSeasonHeader(string seasonKey, int total, int ok, int warn, int err, int restored)
    {
        var sp = new WrapPanel();
        sp.Children.Add(new TextBlock
        {
            Text = IsNoSeasonKey(seasonKey) ? "Alte episoade" : $"Sezon {seasonKey}",
            FontWeight = FontWeights.SemiBold,
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0),
        });
        sp.Children.Add(MakeMini($"Episoade - {total}", Media.Brushes.Gray));
        if (ok > 0) sp.Children.Add(MakeMini($"OK - {ok}", TryBrush("SystemFillColorSuccessBrush", Media.Brushes.ForestGreen)));
        if (warn > 0) sp.Children.Add(MakeMini($"Atentie - {warn}", TryBrush("SystemFillColorCautionBrush", Media.Brushes.Goldenrod)));
        if (err > 0) sp.Children.Add(MakeMini($"Eroare - {err}", TryBrush("SystemFillColorCriticalBrush", Media.Brushes.IndianRed)));
        if (restored > 0) sp.Children.Add(MakeMini($"Restaurate - {restored}", new Media.SolidColorBrush(Media.Color.FromRgb(0x0F, 0x76, 0x78))));
        return sp;
    }

    private static Border MakeMini(string text, Media.Brush brush)
    {
        var baseColor = GetBrushColor(brush, Media.Colors.DimGray);
        var border = new Border
        {
            Background = new Media.SolidColorBrush(Media.Color.FromArgb(0x28, baseColor.R, baseColor.G, baseColor.B)),
            BorderBrush = new Media.SolidColorBrush(Media.Color.FromArgb(0xA0, baseColor.R, baseColor.G, baseColor.B)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8, 3, 8, 3),
            Margin = new Thickness(0, 0, 8, 6),
            VerticalAlignment = VerticalAlignment.Center,
        };
        border.Child = new TextBlock
        {
            Text = text,
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Foreground = brush,
            FontFamily = new Media.FontFamily("Consolas"),
        };
        return border;
    }

    private static Border MakeChip(string label, int count, Media.Brush brush)
    {
        var isZero = count <= 0;
        var accent = isZero
            ? TryBrush("TextFillColorDisabledBrush", Media.Brushes.Gray)
            : brush;
        var accentColor = GetBrushColor(accent, Media.Colors.Gray);
        var labelBrush = isZero
            ? TryBrush("TextFillColorSecondaryBrush", Media.Brushes.Gray)
            : TryBrush("TextFillColorPrimaryBrush", Media.Brushes.WhiteSmoke);

        var border = new Border
        {
            Background = TryBrush("ControlFillColorSecondaryBrush", new Media.SolidColorBrush(Media.Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF))),
            BorderBrush = TryBrush("ControlStrokeColorDefaultBrush", new Media.SolidColorBrush(Media.Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF))),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(14, 8, 14, 8),
            Margin = new Thickness(0, 0, 12, 10),
        };

        var panel = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };
        panel.Children.Add(new TextBlock
        {
            Text = label + " -",
            Foreground = labelBrush,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0),
        });
        var badge = new Border
        {
            Background = new Media.SolidColorBrush(isZero ? Media.Color.FromArgb(0x40, 0x80, 0x80, 0x80) : Media.Color.FromArgb(0xFF, accentColor.R, accentColor.G, accentColor.B)),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10, 3, 10, 3),
            VerticalAlignment = VerticalAlignment.Center,
            MinHeight = 24,
            MinWidth = 32,
        };
        badge.Child = new TextBlock
        {
            Text = count.ToString(),
            Foreground = isZero ? Media.Brushes.Silver : GetContrastingTextBrush(accentColor),
            FontSize = 14,
            TextAlignment = TextAlignment.Center,
            FontWeight = FontWeights.ExtraBold,
            FontFamily = new Media.FontFamily("Consolas"),
        };
        panel.Children.Add(badge);
        border.Child = panel;
        return border;
    }

    private static string GetPlanSummaryBucket(FixPlanItem item, HashSet<string> duplicateSelections)
    {
        var display = GetPlanDisplay(item, duplicateSelections);
        if (display.Status == "error")
            return "error";
        if (display.Status == "warn")
            return "warn";
        if (display.Status == "review")
            return "review";
        if (string.Equals(item.Action, "already-ok", StringComparison.OrdinalIgnoreCase))
            return "already-ok";
        return "pending";
    }

    private static string GetPlanCardLead(FixPlanItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.Episode))
            return item.Episode;

        if (IsStandaloneSubtitleMode(item.ItemMode))
            return "Subtitrare";

        return string.IsNullOrWhiteSpace(item.VideoName) ? "Folder" : "Fisier";
    }

    private static string PlanStatusLabel(FixPlanItem item, string status) => status switch
    {
        "review" => "Alege manual",
        "warn" => "Trebuie verificat",
        "error" => "Blocat",
        _ => (item.Action ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "create" => "Se creeaza",
            "overwrite" => "Se reface",
            "repair" => "Se repara",
            "already-ok" => "Nu se schimba",
            _ => "Pregatit",
        },
    };

    private static Media.Brush PlanStatusBrush(FixPlanItem item, string status) => status switch
    {
        "review" => TryBrush("SystemFillColorCautionBrush", Media.Brushes.Goldenrod),
        "warn" => TryBrush("SystemFillColorCautionBrush", new Media.SolidColorBrush(Media.Color.FromRgb(0xB5, 0x7E, 0x00))),
        "error" => TryBrush("SystemFillColorCriticalBrush", Media.Brushes.IndianRed),
        _ => (item.Action ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "already-ok" => TryBrush("SystemFillColorSuccessBrush", Media.Brushes.ForestGreen),
            _ => new Media.SolidColorBrush(Media.Color.FromRgb(0x39, 0x7A, 0xF6)),
        },
    };

    private static string PlanActionLabel(FixPlanItem item) => (item.Action ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "create" => "Creez subtitrarea finala si mut subtitrarea sursa in backup.",
        "overwrite" => "Refac subtitrarea finala si mut varianta veche in backup.",
        "repair" => "Repar subtitrarea in acelasi fisier si mut originalul in backup.",
        "skip-existing" => "Las episodul asa. Daca vrei sa il refaci, lasa activa suprascrierea.",
        "already-ok" => IsStandaloneSubtitleMode(item.ItemMode)
            ? "Las subtitrarea asa. Fisierul este deja curat."
            : "Las episodul asa. Exista deja subtitrarea finala.",
        _ => IsStandaloneSubtitleMode(item.ItemMode)
            ? "Nu schimb nimic la aceasta subtitrare."
            : "Nu schimb nimic.",
    };

    private static string LastRunStatusLabel(FixSummaryItem item)
    {
        if (string.Equals(item.RestoreStatus, "restored", StringComparison.OrdinalIgnoreCase))
            return "Restaurat";
        if (string.Equals(item.RestoreStatus, "failed", StringComparison.OrdinalIgnoreCase))
            return "Restore esuat";

        return (item.Status ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "ok" => "Reparat",
            "warn" => "Atentie",
            "error" => "Eroare",
            _ => item.Status ?? string.Empty,
        };
    }

    private static Media.Brush RestoreAwareStatusBrush(FixSummaryItem item)
    {
        if (string.Equals(item.RestoreStatus, "restored", StringComparison.OrdinalIgnoreCase))
            return new Media.SolidColorBrush(Media.Color.FromRgb(0x0F, 0x76, 0x78));
        if (string.Equals(item.RestoreStatus, "failed", StringComparison.OrdinalIgnoreCase))
            return TryBrush("SystemFillColorCriticalBrush", Media.Brushes.IndianRed);
        return StatusBrush((item.Status ?? string.Empty).Trim().ToLowerInvariant());
    }

    private static bool IsStandaloneSubtitleMode(string? itemMode) =>
        string.Equals(itemMode?.Trim(), "subtitle-only", StringComparison.OrdinalIgnoreCase);

    private static Media.Brush StatusBrush(string status) => status switch
    {
        "ready" => TryBrush("SystemFillColorSuccessBrush", Media.Brushes.ForestGreen),
        "review" => TryBrush("SystemFillColorCautionBrush", Media.Brushes.Goldenrod),
        "warn" => TryBrush("SystemFillColorCautionBrush", new Media.SolidColorBrush(Media.Color.FromRgb(0xB5, 0x7E, 0x00))),
        "error" => TryBrush("SystemFillColorCriticalBrush", Media.Brushes.IndianRed),
        "ok" => TryBrush("SystemFillColorSuccessBrush", Media.Brushes.ForestGreen),
        _ => Media.Brushes.Gray,
    };

    private static int SeasonSortKey(string seasonKey)
    {
        if (string.IsNullOrEmpty(seasonKey) || seasonKey == "Altele" || seasonKey == "Nesazonat")
            return 999;

        var match = Regex.Match(seasonKey, @"^S(\d+)$", RegexOptions.IgnoreCase);
        if (match.Success && int.TryParse(match.Groups[1].Value, out var season))
            return season;

        return 500;
    }

    private static string NormalizeSeasonKey(string? season) =>
        string.IsNullOrWhiteSpace(season) || string.Equals(season?.Trim(), "Nesazonat", StringComparison.OrdinalIgnoreCase)
            ? "Altele" : season!.Trim();

    private static bool IsNoSeasonKey(string key) =>
        string.IsNullOrWhiteSpace(key) || 
        key == "Altele" || 
        string.Equals(key, "Other", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(key, "Nesazonat", StringComparison.OrdinalIgnoreCase);

    private static string GetMediaKind(string? episode, string? videoName, string? videoPath)
    {
        if (Regex.IsMatch(episode ?? string.Empty, @"^(S\d{1,2}E\d{1,2}|E\d{1,4}|\d{1,4})$", RegexOptions.IgnoreCase))
            return "Seriale";

        var raw = videoName ?? (string.IsNullOrWhiteSpace(videoPath) ? string.Empty : Path.GetFileNameWithoutExtension(videoPath));

        #if DEBUG
        // Regex pt Titlu - 01 sau Titlu 01 la final sau in mijloc
        #endif
        if (Regex.IsMatch(raw, @"[\s._-]+\d{1,4}(?:[\s._-]+|$)", RegexOptions.IgnoreCase))
            return "Seriale";

        return "Filme";
    }

    private static int MediaKindSortKey(string kind) => kind switch
    {
        "Seriale" => 0,
        "Filme" => 1,
        _ => 9,
    };

    private static string BuildMediaGroupKey(string kind, string title) =>
        kind + "|" + title;

    private static string GetMediaTitle(string? videoName, string? videoPath, string? episode)
    {
        var raw = videoName;
        if (string.IsNullOrWhiteSpace(raw) && !string.IsNullOrWhiteSpace(videoPath))
            raw = Path.GetFileName(videoPath);

        raw = Path.GetFileNameWithoutExtension(raw ?? string.Empty);
        if (string.IsNullOrWhiteSpace(raw))
            return "Necategorizat";

        if (Regex.IsMatch(episode ?? string.Empty, @"^(S\d{1,2}E\d{1,2})$", RegexOptions.IgnoreCase))
        {
            var seriesMatch = Regex.Match(raw, @"^(.*?)[\s._-]*S\d{1,2}E\d{1,2}", RegexOptions.IgnoreCase);
            if (seriesMatch.Success && !string.IsNullOrWhiteSpace(seriesMatch.Groups[1].Value))
                raw = seriesMatch.Groups[1].Value;
        }
        else if (Regex.IsMatch(episode ?? string.Empty, @"^(E\d{1,4}|\d{1,4})$", RegexOptions.IgnoreCase) ||
                 Regex.IsMatch(raw, @"^(.*?)[\s._-]+\d{1,4}(?:[\s._-]+|$)", RegexOptions.IgnoreCase))
        {
            var numericEpisodeMatch = Regex.Match(raw, @"^(.*?)[\s._-]+\d{1,4}(?:[\s._-]+|$)", RegexOptions.IgnoreCase);
            if (numericEpisodeMatch.Success && !string.IsNullOrWhiteSpace(numericEpisodeMatch.Groups[1].Value))
                raw = numericEpisodeMatch.Groups[1].Value;
        }
        else
        {
            var movieMatch = Regex.Match(
                raw,
                @"^(.*?)(?:[\s._-]+(?:19|20)\d{2}\b|[\s._-]+(?:480p|720p|1080p|2160p|WEB[- ]DL|WEBRip|BluRay|BRRip|HDRip|DVDRip|AMZN|NF|H\.264|H\.265)\b)",
                RegexOptions.IgnoreCase);
            if (movieMatch.Success && !string.IsNullOrWhiteSpace(movieMatch.Groups[1].Value))
                raw = movieMatch.Groups[1].Value;
        }

        raw = Regex.Replace(raw, @"[._]+", " ");
        raw = Regex.Replace(raw, @"\s+", " ").Trim(' ', '-', '_', '.');
        return string.IsNullOrWhiteSpace(raw) ? "Necategorizat" : raw;
    }

    private static string MediaTitleSortKey(string? title) =>
        Regex.Replace(title ?? string.Empty, @"\s+", " ").Trim().ToLowerInvariant();

    private static string NaturalEpisodeSort(string? episode)
    {
        var value = (episode ?? string.Empty).Trim();
        if (Regex.Match(value, @"^S(?<season>\d{1,2})E(?<ep>\d{1,4})$", RegexOptions.IgnoreCase) is { Success: true } seasonEpisode)
        {
            var season = int.Parse(seasonEpisode.Groups["season"].Value);
            var ep = int.Parse(seasonEpisode.Groups["ep"].Value);
            return $"{season:D4}-{ep:D6}";
        }

        if (Regex.Match(value, @"^E(?<ep>\d{1,4})$", RegexOptions.IgnoreCase) is { Success: true } numericEpisode)
        {
            var ep = int.Parse(numericEpisode.Groups["ep"].Value);
            return $"9998-{ep:D6}";
        }

        if (Regex.Match(value, @"^(?<ep>\d{1,4})$", RegexOptions.IgnoreCase) is { Success: true } rawNumeric)
        {
            var ep = int.Parse(rawNumeric.Groups["ep"].Value);
            return $"9999-{ep:D6}";
        }

        return value;
    }

    private static Media.Brush TryBrush(string resourceKey, Media.Brush fallback)
    {
        if (System.Windows.Application.Current?.TryFindResource(resourceKey) is Media.Brush brush)
            return brush;

        return fallback;
    }

    private static Media.Color GetBrushColor(Media.Brush brush, Media.Color fallback) =>
        brush is Media.SolidColorBrush solid ? solid.Color : fallback;

    private static Media.Brush GetContrastingTextBrush(Media.Color color)
    {
        var brightness = ((0.299 * color.R) + (0.587 * color.G) + (0.114 * color.B)) / 255d;
        return brightness >= 0.62 ? Media.Brushes.Black : Media.Brushes.White;
    }

    private void AppendLogSection(string title)
    {
        if (LogTextBox.Text.Length > 0)
            LogTextBox.AppendText(Environment.NewLine + Environment.NewLine);

        LogTextBox.AppendText("==== " + title + " ====");
        LogTextBox.ScrollToEnd();
    }

    private void AppendLogLine(string line)
    {
        if (LogTextBox.Text.Length > 0)
            LogTextBox.AppendText(Environment.NewLine);
        LogTextBox.AppendText(line);
        LogTextBox.ScrollToEnd();
    }

    private sealed record RestoreOutcome(string VideoPath, bool Success, string Message);
    private sealed record RestoreProgress(int Current, int Total, string Label);
}
