using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Navigation;
using Media = System.Windows.Media;
using SubtitlesFixer.App.Subtitles;
// Aliasuri pentru evitarea ambiguitatii cu System.Windows.Forms (adaugat global implicit de SDK UseWindowsForms)
using WpfBtn   = System.Windows.Controls.Button;
using WpfCombo = System.Windows.Controls.ComboBox;
using WpfHA    = System.Windows.HorizontalAlignment;

namespace SubtitlesFixer.App;

public partial class SubtitleSearchWindow : Wpf.Ui.Controls.FluentWindow
{
    // ────────────────────────────────────────────────────────────────────────
    // State
    // ────────────────────────────────────────────────────────────────────────

    private readonly AppSettings _settings;
    private readonly SubDLProvider _provider;
    private bool _isBusy;

    // Lista de item-uri afisate in UI
    private readonly List<SearchItem> _items = [];

    // ────────────────────────────────────────────────────────────────────────
    // Init
    // ────────────────────────────────────────────────────────────────────────

    public SubtitleSearchWindow(AppSettings settings, string? initialFolder = null)
    {
        InitializeComponent();
        _settings = settings;
        _provider = new SubDLProvider(settings.SubDLApiKey);

        // Preia folderul din fereastra principala daca exista
        if (!string.IsNullOrWhiteSpace(initialFolder) && Directory.Exists(initialFolder))
        {
            FolderPathBox.Text = initialFolder;
            // Auto-scanare la deschidere daca avem folder
            Loaded += (_, _) => ScanButton_Click(this, new RoutedEventArgs());
        }

        // Sincronizeaza checkboxes din setarile salvate
        LangRoCheckBox.IsChecked = settings.PreferredLanguages.Contains("ro");
        LangEnCheckBox.IsChecked = settings.PreferredLanguages.Contains("en");
        RecurseCheckBox.IsChecked = settings.IncludeSubfolders;

        RefreshKeyState();
    }

    // ────────────────────────────────────────────────────────────────────────
    // Stare cheie API — afiseaza setup sau interfata normala
    // ────────────────────────────────────────────────────────────────────────

    private void RefreshKeyState()
    {
        var hasKey = !string.IsNullOrWhiteSpace(_settings.SubDLApiKey);

        SetupCard.Visibility      = hasKey ? Visibility.Collapsed : Visibility.Visible;
        KeyStatusCard.Visibility  = hasKey ? Visibility.Visible   : Visibility.Collapsed;
        SearchConfigCard.Visibility = hasKey ? Visibility.Visible : Visibility.Collapsed;
        ActionPanel.Visibility    = hasKey ? Visibility.Visible   : Visibility.Collapsed;

        if (hasKey)
        {
            var key = _settings.SubDLApiKey!;
            // Afisam doar primele 8 caractere + *** pentru confidentialitate
            var hint = key.Length > 8
                ? key[..8] + new string('*', Math.Min(8, key.Length - 8))
                : new string('*', key.Length);
            KeyHintText.Text = hint;

            // Pre-populeaza campul de input daca userul vrea sa schimbe
            ApiKeyInputBox.Text = key;
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    // Hyperlink handler (deschide in browser)
    // ────────────────────────────────────────────────────────────────────────

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch { /* ignora daca browserul nu se deschide */ }
        e.Handled = true;
    }

    // ────────────────────────────────────────────────────────────────────────
    // Setup — Testeaza cheia
    // ────────────────────────────────────────────────────────────────────────

    private async void ValidateKeyButton_Click(object sender, RoutedEventArgs e)
    {
        var key = ApiKeyInputBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(key))
        {
            ShowKeyValidation("Introdu cheia inainte de testare.", isError: true);
            return;
        }

        SetBusy(true, "Testez cheia...");
        try
        {
            var tempProvider = new SubDLProvider(key);
            var testQuery = new SubtitleSearchQuery
            {
                Title     = "Breaking Bad",
                Season    = 1,
                Episode   = 1,
                Languages = ["en"],
            };
            var results = await tempProvider.SearchAsync(testQuery);
            if (results.Count > 0)
                ShowKeyValidation($"✓  Cheia este valida! ({results.Count} rezultate pentru testul de verificare)", isError: false);
            else
                ShowKeyValidation("Cheia pare valida, dar nu s-au gasit rezultate pentru testul de verificare. Poate fi corecta — salveaz-o si incearca.", isError: false);
        }
        catch (Exception ex)
        {
            ShowKeyValidation("✗  Cheia este invalida sau nu ai conexiune la internet. " + ex.Message, isError: true);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void ShowKeyValidation(string msg, bool isError)
    {
        KeyValidationText.Text       = msg;
        KeyValidationText.Foreground = isError
            ? TryBrush("SystemFillColorCriticalBrush", Media.Brushes.IndianRed)
            : TryBrush("SystemFillColorSuccessBrush",  Media.Brushes.ForestGreen);
        KeyValidationText.Visibility = Visibility.Visible;
    }

    // ────────────────────────────────────────────────────────────────────────
    // Setup — Salveaza cheia
    // ────────────────────────────────────────────────────────────────────────

    private void SaveKeyButton_Click(object sender, RoutedEventArgs e)
    {
        var key = ApiKeyInputBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(key))
        {
            ShowKeyValidation("Introdu cheia inainte sa salvezi.", isError: true);
            return;
        }

        _settings.SubDLApiKey = key;
        _settings.Save();

        // Recreeaza fereastra cu noua cheie
        var newWindow = new SubtitleSearchWindow(_settings, FolderPathBox.Text)
        {
            Owner = Owner,
        };
        newWindow.Show();
        Close();
    }

    // ────────────────────────────────────────────────────────────────────────
    // Schimba cheia (revine la setup)
    // ────────────────────────────────────────────────────────────────────────

    private void ChangeKeyButton_Click(object sender, RoutedEventArgs e)
    {
        SetupCard.Visibility      = Visibility.Visible;
        KeyStatusCard.Visibility  = Visibility.Collapsed;
        KeyValidationText.Visibility = Visibility.Collapsed;
    }

    // ────────────────────────────────────────────────────────────────────────
    // Browse folder
    // ────────────────────────────────────────────────────────────────────────

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        using var dlg = new System.Windows.Forms.FolderBrowserDialog
        {
            Description        = "Alege folderul cu fisiere video",
            UseDescriptionForTitle = true,
        };

        if (!string.IsNullOrWhiteSpace(FolderPathBox.Text) && Directory.Exists(FolderPathBox.Text))
            dlg.SelectedPath = FolderPathBox.Text;

        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            FolderPathBox.Text = dlg.SelectedPath;
            // Auto-scanare dupa selectarea folderului
            ScanButton_Click(sender, e);
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    // Scaneaza folderul — gaseste video fara .ro.srt
    // ────────────────────────────────────────────────────────────────────────

    private void ScanButton_Click(object sender, RoutedEventArgs e)
    {
        var folder = FolderPathBox.Text?.Trim() ?? string.Empty;
        if (!Directory.Exists(folder))
        {
            System.Windows.MessageBox.Show("Selecteaza un folder valid inainte de scanare.",
                "Subtitles Fixer", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Salveaza optiunile curente
        _settings.IncludeSubfolders = RecurseCheckBox.IsChecked == true;
        SaveLangPreferences();
        _settings.Save();

        _items.Clear();
        var recurse = RecurseCheckBox.IsChecked == true;

        var videos = Directory
            .EnumerateFiles(folder, "*.*",
                recurse ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
            .Where(f =>
            {
                var ext = Path.GetExtension(f).ToLowerInvariant();
                return ext is ".mkv" or ".mp4" or ".avi";
            })
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "backup" + Path.DirectorySeparatorChar,
                            StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f)
            .ToList();

        var parsedVideos = videos.ToDictionary(
            video => video,
            video => VideoNameParser.Parse(Path.GetFileName(video)),
            StringComparer.OrdinalIgnoreCase);

        var numericSeriesKeys = parsedVideos.Values
            .Where(info => info.HasNumericEpisodeCandidate)
            .GroupBy(info => info.NumericSeriesKey!, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Select(info => info.NumericEpisodeCandidate!.Value).Distinct().Count() >= 2)
            .Select(g => g.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var needSubtitle = 0;
        foreach (var v in videos)
        {
            var roSrt = Path.ChangeExtension(v, ".ro.srt");
            if (File.Exists(roSrt)) continue; // are deja subtitrare finala

            var info = parsedVideos[v];
            if (info.HasNumericEpisodeCandidate && numericSeriesKeys.Contains(info.NumericSeriesKey!))
                info = info.PromoteNumericEpisode();

            _items.Add(new SearchItem
            {
                VideoPath  = v,
                VideoName  = Path.GetFileName(v),
                OutputPath = roSrt,
                VideoInfo  = info,
                State      = SearchItemState.Pending,
            });
            needSubtitle++;
        }

        BuildResultsPanel();
        ResultsBorder.Visibility = Visibility.Visible;

        if (_items.Count == 0)
        {
            ActionStatusText.Text = $"{videos.Count} video gasit(e) — toate au deja .ro.srt.";
            SearchAllButton.IsEnabled   = false;
            DownloadAllButton.IsEnabled = false;
        }
        else
        {
            ActionStatusText.Text = $"{videos.Count} video gasit(e), {needSubtitle} fara subtitrare finala. Apasa \"Cauta toate subtitrarile\" pentru a incepe cautarea online.";
            SearchAllButton.IsEnabled   = true;
            DownloadAllButton.IsEnabled = false;
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    // Cauta toate
    // ────────────────────────────────────────────────────────────────────────

    private async void SearchAllButton_Click(object sender, RoutedEventArgs e)
    {
        if (_items.Count == 0) return;
        var langs = BuildLangList();
        if (langs.Count == 0)
        {
            System.Windows.MessageBox.Show("Selecteaza cel putin o limba inainte de cautare.",
                "Subtitles Fixer", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        SetBusy(true);
        OperationProgressBar.Value      = 0;
        OperationProgressBar.Visibility = Visibility.Visible;

        var total = _items.Count;
        var done  = 0;
        var errorCount = 0;

        try
        {
            foreach (var item in _items)
            {
                item.State   = SearchItemState.Searching;
                item.Results = [];
                RefreshItemRow(item);
                ActionStatusText.Text = $"Caut... {done + 1}/{total}  —  {item.VideoInfo.Title}" +
                    (item.VideoInfo.IsSeries ? $" S{item.VideoInfo.Season:00}E{item.VideoInfo.Episode:00}" : string.Empty);

                try
                {
                    var query = new SubtitleSearchQuery
                    {
                        Title     = item.VideoInfo.Title,
                        Season    = item.VideoInfo.Season,
                        Episode   = item.VideoInfo.Episode,
                        Languages = langs,
                    };

                    var results = await _provider.SearchAsync(query);
                    item.Results = results.ToList();

                    if (results.Count == 0)
                    {
                        item.State   = SearchItemState.NotFound;
                        item.Message = "Nu s-a gasit nicio subtitrare.";
                    }
                    else
                    {
                        // Alege cel mai bun rezultat in ordinea limbilor preferate
                        var best = PickBest(results, langs);
                        item.ChosenResult = best;
                        item.State  = best.Language == "ro"
                            ? SearchItemState.FoundRo
                            : SearchItemState.FoundEn;
                        item.OutputPath = BuildOutputPath(item.VideoPath, best.Language);
                        item.Message = best.Language == "ro"
                            ? "Am gasit o subtitrare in romana, gata de salvat."
                            : "Am gasit varianta in engleza, gata de salvat ca .en.srt.";
                    }
                }
                catch (Exception ex)
                {
                    item.State   = SearchItemState.Error;
                    item.Message = ex.Message;
                    errorCount++;
                }

                done++;
                OperationProgressBar.Value = (double)done / total * 100;
                RefreshItemRow(item);

                // Pauza intre cereri pentru a nu depasi limita API-ului
                if (done < total)
                    await Task.Delay(350);
            }

            var foundRoCount = _items.Count(i => i.State == SearchItemState.FoundRo);
            var foundEnCount = _items.Count(i => i.State == SearchItemState.FoundEn);
            var notFoundCount = _items.Count(i => i.State == SearchItemState.NotFound);
            var finalErrorCount = _items.Count(i => i.State == SearchItemState.Error);

            ActionStatusText.Text =
                $"Cautare terminata — romana: {foundRoCount}, engleza: {foundEnCount}, lipsa: {notFoundCount}" +
                (finalErrorCount > 0 ? $", erori: {finalErrorCount}" : string.Empty) + ".";
            DownloadAllButton.IsEnabled   = foundRoCount > 0 || foundEnCount > 0;
        }
        catch (Exception ex)
        {
            ActionStatusText.Text = $"Cautarea a esuat: {ex.Message}";
        }
        finally
        {
            OperationProgressBar.Visibility = Visibility.Collapsed;
            SetBusy(false);
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    // Descarca toate
    // ────────────────────────────────────────────────────────────────────────

    private async void DownloadAllButton_Click(object sender, RoutedEventArgs e)
    {
        var toDownload = _items.Where(i => i.ChosenResult is not null
            && i.State is SearchItemState.FoundRo or SearchItemState.FoundEn).ToList();

        if (toDownload.Count == 0) return;

        SetBusy(true);
        OperationProgressBar.Value      = 0;
        OperationProgressBar.Visibility = Visibility.Visible;

        var total = toDownload.Count;
        var done  = 0;
        var ok    = 0;

        try
        {
            foreach (var item in toDownload)
            {
                item.State = SearchItemState.Downloading;
                RefreshItemRow(item);
                ActionStatusText.Text = $"Salvez... {done + 1}/{total}  —  {item.VideoInfo.Title}" +
                    (item.VideoInfo.IsSeries ? $" S{item.VideoInfo.Season:00}E{item.VideoInfo.Episode:00}" : string.Empty);

                var result = await ApplyChosenSubtitleAsync(item);

                if (result.Success)
                {
                    item.State   = SearchItemState.Downloaded;
                    item.Message = "Salvata ca subtitrare finala si pregatita pentru restore din backup.";
                    ok++;
                }
                else
                {
                    item.State   = SearchItemState.Error;
                    item.Message = result.ErrorMessage ?? "Eroare necunoscuta.";
                }

                done++;
                OperationProgressBar.Value = (double)done / total * 100;
                RefreshItemRow(item);

                // Pauza intre download-uri
                if (done < total)
                    await Task.Delay(200);
            }

            var statusText = $"Salvare terminata — {ok}/{total} subtitrari aplicate.";
            if (ok > 0 && !TryPersistLastRunPayload(out var persistError))
                statusText += " Restore-ul nu a putut fi actualizat: " + persistError;

            ActionStatusText.Text = statusText;
        }
        catch (Exception ex)
        {
            ActionStatusText.Text = $"Salvarea a esuat: {ex.Message}";
        }
        finally
        {
            OperationProgressBar.Visibility = Visibility.Collapsed;
            SetBusy(false);
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    // Download per item (buton individual)
    // ────────────────────────────────────────────────────────────────────────

    private async void DownloadItemButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not WpfBtn btn || btn.Tag is not SearchItem item) return;
        if (item.ChosenResult is null) return;
        if (item.State is not (SearchItemState.FoundRo or SearchItemState.FoundEn)) return;

        btn.IsEnabled = false;
        item.State    = SearchItemState.Downloading;
        RefreshItemRow(item);

        var result = await ApplyChosenSubtitleAsync(item);
        item.State   = result.Success ? SearchItemState.Downloaded : SearchItemState.Error;
        item.Message = result.Success
            ? "Salvata ca subtitrare finala si pregatita pentru restore."
            : result.ErrorMessage ?? string.Empty;
        if (result.Success && !TryPersistLastRunPayload(out var persistError))
            ActionStatusText.Text = "Subtitrarea a fost salvata, dar restore-ul nu a putut fi actualizat: " + persistError;
        RefreshItemRow(item);
    }

    // ────────────────────────────────────────────────────────────────────────
    // Selectie varianta alternativa
    // ────────────────────────────────────────────────────────────────────────

    private void AltResultCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not WpfCombo combo || combo.Tag is not SearchItem item) return;
        if (combo.SelectedItem is not SubtitleSearchResult chosen) return;

        item.ChosenResult = chosen;
        item.State = chosen.Language == "ro" ? SearchItemState.FoundRo : SearchItemState.FoundEn;
        item.OutputPath = BuildOutputPath(item.VideoPath, chosen.Language);
        item.Message = chosen.Language == "ro"
            ? "Am gasit o subtitrare in romana, gata de salvat."
            : "Am gasit varianta in engleza, gata de salvat ca .en.srt.";
        RefreshItemRow(item);
        DownloadAllButton.IsEnabled = _items.Any(i => i.State is SearchItemState.FoundRo or SearchItemState.FoundEn);
    }

    // ────────────────────────────────────────────────────────────────────────
    // UI builder — lista item-uri
    // ────────────────────────────────────────────────────────────────────────

    private void BuildResultsPanel()
    {
        ResultsPanel.Children.Clear();

        if (_items.Count == 0)
        {
            ResultsPanel.Children.Add(new TextBlock
            {
                Text       = "Toate fisierele video au deja subtitrare finala (.ro.srt).",
                Foreground = TryBrush("TextFillColorSecondaryBrush", Media.Brushes.Gray),
                Margin     = new Thickness(0, 4, 0, 4),
            });
            return;
        }

        // Header
        var headerGrid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });

        void AddHeaderCell(int col, string text)
        {
            var tb = new TextBlock
            {
                Text       = text,
                FontSize   = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = TryBrush("TextFillColorSecondaryBrush", Media.Brushes.Gray),
                Margin     = new Thickness(col == 0 ? 0 : 6, 0, 0, 0),
            };
            Grid.SetColumn(tb, col);
            headerGrid.Children.Add(tb);
        }

        AddHeaderCell(0, "FISIER VIDEO");
        AddHeaderCell(1, "STATUS");
        AddHeaderCell(2, "SUBTITRARE GASITA");
        AddHeaderCell(3, string.Empty);
        ResultsPanel.Children.Add(headerGrid);

        // Separator
        ResultsPanel.Children.Add(new Separator { Margin = new Thickness(0, 0, 0, 8) });

        foreach (var item in _items)
        {
            item.RowContainer = BuildItemRow(item);
            ResultsPanel.Children.Add(item.RowContainer);
        }
    }

    private Border BuildItemRow(SearchItem item)
    {
        var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });

        // Col 0 — video name
        var nameBlock = new TextBlock
        {
            Text         = item.VideoName,
            TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip      = item.VideoPath,
            VerticalAlignment = VerticalAlignment.Center,
            Margin       = new Thickness(0, 0, 8, 0),
        };
        Grid.SetColumn(nameBlock, 0);
        grid.Children.Add(nameBlock);

        // Col 1 — status badge
        var badge = MakeStateBadge(item.State);
        Grid.SetColumn(badge, 1);
        grid.Children.Add(badge);

        // Col 2 — subtitrare gasita (combo sau text)
        var centerPanel = BuildCenterCell(item);
        Grid.SetColumn(centerPanel, 2);
        grid.Children.Add(centerPanel);

        // Col 3 — buton download
        var dlBtn = new Wpf.Ui.Controls.Button
        {
            Content    = GetDownloadButtonLabel(item),
            Appearance = Wpf.Ui.Controls.ControlAppearance.Primary,
            Margin     = new Thickness(6, 0, 0, 0),
            Tag        = item,
            IsEnabled  = item.ChosenResult is not null
                && item.State is SearchItemState.FoundRo or SearchItemState.FoundEn,
            ToolTip    = "Salveaza subtitrarea in folder si pastreaza sursa in backup.",
        };
        dlBtn.Click += DownloadItemButton_Click;
        Grid.SetColumn(dlBtn, 3);
        grid.Children.Add(dlBtn);

        var border = new Border
        {
            Padding         = new Thickness(8, 6, 8, 6),
            CornerRadius    = new CornerRadius(6),
            Background      = ItemBackground(item.State),
            Child           = grid,
            Margin          = new Thickness(0, 1, 0, 1),
            Tag             = item,
        };

        return border;
    }

    private void RefreshItemRow(SearchItem item)
    {
        if (item.RowContainer is null) return;

        // Riconstrui randul in loc (simplu si reliable)
        var parent = ResultsPanel;
        var index  = -1;
        for (var i = 0; i < parent.Children.Count; i++)
        {
            if (parent.Children[i] is Border b && b.Tag is SearchItem si && si == item)
            {
                index = i;
                break;
            }
        }

        if (index < 0) return;
        var newRow = BuildItemRow(item);
        item.RowContainer = newRow;
        parent.Children.RemoveAt(index);
        parent.Children.Insert(index, newRow);
    }

    private UIElement BuildCenterCell(SearchItem item)
    {
        // Daca are mai multe rezultate, arata combo pentru selectie manuala
        if (item.Results is { Count: > 1 } && item.ChosenResult is not null)
        {
            var combo = new WpfCombo
            {
                ItemsSource         = item.Results,
                SelectedItem        = item.ChosenResult,
                DisplayMemberPath   = nameof(SubtitleSearchResult.ReleaseName),
                Margin              = new Thickness(6, 0, 0, 0),
                VerticalAlignment   = VerticalAlignment.Center,
                Tag                 = item,
                ToolTip             = "Alege varianta preferata",
            };
            combo.SelectionChanged += AltResultCombo_SelectionChanged;
            return combo;
        }

        var text = item.State switch
        {
            SearchItemState.Pending     => "—",
            SearchItemState.Searching   => "Se cauta...",
            SearchItemState.Downloading => "Se salveaza...",
            SearchItemState.Downloaded  => "Salvata: " + Path.GetFileName(item.OutputPath),
            SearchItemState.NotFound    => item.Message ?? "Nu s-a gasit.",
            SearchItemState.Error       => item.Message ?? "Eroare.",
            SearchItemState.FoundRo     => item.ChosenResult?.ReleaseName ?? string.Empty,
            SearchItemState.FoundEn     => item.ChosenResult?.ReleaseName ?? string.Empty,
            _                           => string.Empty,
        };

        return new TextBlock
        {
            Text         = text,
            TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip      = text,
            VerticalAlignment = VerticalAlignment.Center,
            Margin       = new Thickness(6, 0, 0, 0),
            Foreground   = item.State is SearchItemState.Error or SearchItemState.NotFound
                ? TryBrush("SystemFillColorCriticalBrush",  Media.Brushes.IndianRed)
                : item.State is SearchItemState.Downloaded
                    ? TryBrush("SystemFillColorSuccessBrush", Media.Brushes.ForestGreen)
                    : TryBrush("TextFillColorPrimaryBrush", Media.Brushes.Black),
        };
    }

    private static Border MakeStateBadge(SearchItemState state)
    {
        var (label, bg) = state switch
        {
            SearchItemState.Pending     => ("Astept",       Media.Color.FromRgb(0x80, 0x80, 0x80)),
            SearchItemState.Searching   => ("Caut...",      Media.Color.FromRgb(0x39, 0x78, 0xF6)),
            SearchItemState.FoundRo     => ("Romana",       Media.Color.FromRgb(0x11, 0x8A, 0x14)),
            SearchItemState.FoundEn     => ("Doar EN",      Media.Color.FromRgb(0xB5, 0x7E, 0x00)),
            SearchItemState.NotFound    => ("Lipsa",        Media.Color.FromRgb(0xB5, 0x7E, 0x00)),
            SearchItemState.Downloading => ("Salvez...",    Media.Color.FromRgb(0x39, 0x78, 0xF6)),
            SearchItemState.Downloaded  => ("Salvata",      Media.Color.FromRgb(0x11, 0x8A, 0x14)),
            SearchItemState.Error       => ("Eroare",       Media.Color.FromRgb(0xC4, 0x26, 0x26)),
            _                           => ("?",            Media.Color.FromRgb(0x80, 0x80, 0x80)),
        };

        var brush      = new Media.SolidColorBrush(bg);
        var brightness = ((0.299 * bg.R) + (0.587 * bg.G) + (0.114 * bg.B)) / 255d;
        var fg         = brightness >= 0.55 ? Media.Brushes.Black : Media.Brushes.White;

        return new Border
        {
            Background    = brush,
            CornerRadius  = new CornerRadius(4),
            Padding       = new Thickness(6, 2, 6, 2),
            Margin        = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = WpfHA.Left,
            Child = new TextBlock
            {
                Text       = label,
                Foreground = fg,
                FontSize   = 11,
                FontWeight = FontWeights.SemiBold,
            },
        };
    }

    private static Media.Brush ItemBackground(SearchItemState state) => state switch
    {
        SearchItemState.Downloaded => new Media.SolidColorBrush(Media.Color.FromArgb(25, 0x11, 0x8A, 0x14)),
        SearchItemState.Error      => new Media.SolidColorBrush(Media.Color.FromArgb(20, 0xC4, 0x26, 0x26)),
        SearchItemState.NotFound   => new Media.SolidColorBrush(Media.Color.FromArgb(18, 0xB5, 0x7E, 0x00)),
        SearchItemState.FoundEn    => new Media.SolidColorBrush(Media.Color.FromArgb(16, 0xB5, 0x7E, 0x00)),
        _                          => Media.Brushes.Transparent,
    };

    // ────────────────────────────────────────────────────────────────────────
    // Helpers
    // ────────────────────────────────────────────────────────────────────────

    private List<string> BuildLangList()
    {
        var list = new List<string>();
        if (LangRoCheckBox.IsChecked == true) list.Add("ro");
        if (LangEnCheckBox.IsChecked == true) list.Add("en");
        return list;
    }

    private void SaveLangPreferences()
    {
        _settings.PreferredLanguages = BuildLangList();
    }

    private static SubtitleSearchResult PickBest(
        IReadOnlyList<SubtitleSearchResult> results,
        IReadOnlyList<string> langs)
    {
        foreach (var lang in langs)
        {
            var match = results.FirstOrDefault(r =>
                string.Equals(r.Language, lang, StringComparison.OrdinalIgnoreCase));
            if (match is not null) return match;
        }
        return results[0]; // niciuna din limbile preferate, luam primul
    }

    private async Task<SubtitleDownloadResult> ApplyChosenSubtitleAsync(SearchItem item, CancellationToken ct = default)
    {
        var chosen = item.ChosenResult;
        if (chosen is null)
        {
            return new SubtitleDownloadResult
            {
                Success = false,
                ErrorMessage = "Nu exista nicio subtitrare selectata pentru acest fisier.",
            };
        }

        var downloaded = await SubDLProvider.DownloadAsync(chosen, ct);
        if (!downloaded.Success || string.IsNullOrWhiteSpace(downloaded.NormalizedText))
            return downloaded;

        var targetDir = Path.GetDirectoryName(item.OutputPath);
        if (string.IsNullOrWhiteSpace(targetDir))
        {
            return new SubtitleDownloadResult
            {
                Success = false,
                ErrorMessage = "Nu am putut determina folderul in care trebuie salvata subtitrarea.",
            };
        }

        Directory.CreateDirectory(targetDir);
        var backupDir = Path.Combine(targetDir, "backup");
        Directory.CreateDirectory(backupDir);

        var syntheticSourcePath = CreateUniquePath(Path.Combine(
            targetDir,
            Path.GetFileNameWithoutExtension(item.VideoPath) + ".subdl-source.ro.srt"));
        var sourceBackupPath = CreateUniquePath(Path.Combine(backupDir, Path.GetFileName(syntheticSourcePath)));
        var tempTargetPath = CreateSiblingTempPath(item.OutputPath, ".subdl-write");
        string? replacedTargetBackupPath = null;
        var utf8NoBom = new System.Text.UTF8Encoding(false);
        var movedOldTarget = false;
        var wroteSourceBackup = false;

        try
        {
            if (File.Exists(item.OutputPath))
            {
                replacedTargetBackupPath = CreateUniquePath(Path.Combine(backupDir, Path.GetFileName(item.OutputPath)));
                File.Move(item.OutputPath, replacedTargetBackupPath);
                movedOldTarget = true;
            }

            await File.WriteAllTextAsync(sourceBackupPath, downloaded.NormalizedText, utf8NoBom, ct);
            wroteSourceBackup = true;

            await File.WriteAllTextAsync(tempTargetPath, downloaded.NormalizedText, utf8NoBom, ct);
            File.Move(tempTargetPath, item.OutputPath);

            item.LastRunItem = BuildOnlineSummaryItem(
                item,
                downloaded,
                sourceBackupPath,
                replacedTargetBackupPath);

            return new SubtitleDownloadResult
            {
                Success = true,
                DownloadedFileName = downloaded.DownloadedFileName,
            };
        }
        catch (Exception ex)
        {
            TryDeleteFile(tempTargetPath);

            if (wroteSourceBackup)
                TryDeleteFile(sourceBackupPath);

            if (movedOldTarget &&
                !string.IsNullOrWhiteSpace(replacedTargetBackupPath) &&
                File.Exists(replacedTargetBackupPath) &&
                !File.Exists(item.OutputPath))
            {
                File.Move(replacedTargetBackupPath, item.OutputPath);
            }

            item.LastRunItem = null;
            return new SubtitleDownloadResult
            {
                Success = false,
                ErrorMessage = ex.Message,
            };
        }
    }

    private bool TryPersistLastRunPayload(out string? errorMessage)
    {
        errorMessage = null;
        try
        {
            var storedItems = _items
                .Where(i => i.LastRunItem is not null)
                .Select(i => i.LastRunItem!)
                .ToList();

            if (storedItems.Count == 0)
            {
                return true;
            }

            LastRunStore.Save(new FixSummaryPayload
            {
                Totals = new FixTotals
                {
                    Ok = storedItems.Count,
                    Warn = 0,
                    Err = 0,
                },
                Items = storedItems,
            });

            if (Owner is MainWindow mainWindow)
                mainWindow.ReloadLastRunFromStore();

            return true;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            return false;
        }
    }

    private static FixSummaryItem BuildOnlineSummaryItem(
        SearchItem item,
        SubtitleDownloadResult downloaded,
        string sourceBackupPath,
        string? replacedTargetBackupPath)
    {
        return new FixSummaryItem
        {
            Season = item.VideoInfo.IsSeries && item.VideoInfo.Season.HasValue
                ? item.VideoInfo.Season.Value.ToString("00")
                : null,
            Episode = FormatEpisode(item.VideoInfo),
            VideoName = item.VideoName,
            VideoPath = item.VideoPath,
            SubtitleBefore = downloaded.DownloadedFileName ?? Path.GetFileName(sourceBackupPath),
            SubtitleAfter = Path.GetFileName(item.OutputPath),
            EncodingDetected = "SubDL + normalizare RO",
            BackupPath = sourceBackupPath,
            SourceOriginalPath = null,
            SourceBackupPath = sourceBackupPath,
            TargetPath = item.OutputPath,
            ReplacedTargetBackupPath = replacedTargetBackupPath,
            Status = "ok",
            Message = replacedTargetBackupPath is null
                ? "Descarcata din SubDL si salvata ca subtitrare finala."
                : "Descarcata din SubDL, salvata ca subtitrare finala, iar varianta veche a fost mutata in backup.",
            RootPath = Path.GetDirectoryName(item.VideoPath),
        };
    }

    private static string? FormatEpisode(VideoInfo info)
    {
        if (!info.IsSeries || !info.Episode.HasValue)
            return null;

        if (info.NumericEpisodeCandidate.HasValue && info.Season == 1)
            return info.Episode.Value.ToString("D3");

        return info.Season.HasValue
            ? $"S{info.Season.Value:00}E{info.Episode.Value:00}"
            : info.Episode.Value.ToString("D3");
    }

    private static string GetDownloadButtonLabel(SearchItem item) => item.State switch
    {
        SearchItemState.Downloaded => "Salvata",
        SearchItemState.Downloading => "Salvez...",
        _ => "Salveaza",
    };

    private static string CreateUniquePath(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
            return path;

        var directory = Path.GetDirectoryName(path)
                        ?? throw new InvalidOperationException("Calea nu are director parinte.");
        var fileName = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        var index = 1;
        var candidate = Path.Combine(directory, $"{fileName}.{index}{extension}");

        while (File.Exists(candidate) || Directory.Exists(candidate))
        {
            index++;
            candidate = Path.Combine(directory, $"{fileName}.{index}{extension}");
        }

        return candidate;
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

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Ignorat.
        }
    }

    private void SetBusy(bool busy, string? statusText = null)
    {
        _isBusy = busy;
        ScanButton.IsEnabled       = !busy;
        SearchAllButton.IsEnabled  = !busy && _items.Any(i => i.State == SearchItemState.Pending);
        DownloadAllButton.IsEnabled = !busy && _items.Any(i => i.State is SearchItemState.FoundRo or SearchItemState.FoundEn);
        ValidateKeyButton.IsEnabled = !busy;
        SaveKeyButton.IsEnabled     = !busy;
        ChangeKeyButton.IsEnabled   = !busy;
        BrowseButton.IsEnabled      = !busy;

        if (statusText is not null)
            ActionStatusText.Text = statusText;
    }

    private static Media.Brush TryBrush(string key, Media.Brush fallback) =>
        System.Windows.Application.Current?.TryFindResource(key) is Media.Brush b ? b : fallback;

    /// <summary>
    /// Construieste calea de output pe baza limbii: VideoName.ro.srt sau VideoName.en.srt etc.
    /// </summary>
    private static string BuildOutputPath(string videoPath, string language)
    {
        var dir  = Path.GetDirectoryName(videoPath) ?? string.Empty;
        var name = Path.GetFileNameWithoutExtension(videoPath);
        var ext  = string.Equals(language, "ro", StringComparison.OrdinalIgnoreCase)
            ? ".ro.srt"
            : $".{language.ToLowerInvariant()}.srt";
        return Path.Combine(dir, name + ext);
    }

    // ────────────────────────────────────────────────────────────────────────
    // Model item intern
    // ────────────────────────────────────────────────────────────────────────

    private sealed class SearchItem
    {
        public required string    VideoPath   { get; init; }
        public required string    VideoName   { get; init; }
        public required string    OutputPath  { get; set; }
        public required VideoInfo VideoInfo   { get; set; }
        public SearchItemState    State       { get; set; } = SearchItemState.Pending;
        public string?            Message     { get; set; }
        public List<SubtitleSearchResult>? Results      { get; set; }
        public SubtitleSearchResult?       ChosenResult { get; set; }
        public FixSummaryItem?            LastRunItem   { get; set; }
        public Border?                     RowContainer { get; set; }
    }

    private enum SearchItemState
    {
        Pending,
        Searching,
        FoundRo,
        FoundEn,
        NotFound,
        Downloading,
        Downloaded,
        Error,
    }
}
