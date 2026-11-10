using System.Windows;
using System.Windows.Controls;
using RelevPro.Services;

namespace RelevPro.Views.Dialogs;

public partial class DynamicEditWindow : Window
{
    private readonly List<EditField> _fields;
    private readonly List<Func<bool>> _readers = new();
    /// <summary>Valeur "en direct" (index de combo, texte tapé...) de chaque champ, lue sans attendre la
    /// validation - utilisée pour réévaluer <see cref="EditField.EnabledWhenFieldEquals"/> des autres
    /// champs à chaque changement (voir <see cref="RefreshConditionalFields"/>).</summary>
    private readonly Dictionary<EditField, Func<object?>> _liveGetters = new();
    private readonly Dictionary<EditField, Control> _controls = new();
    public string WindowTitle { get; }

    public DynamicEditWindow(string title, List<EditField> fields)
    {
        InitializeComponent();
        WindowTitle = title;
        Title = title;
        TitleText.Text = title;
        _fields = fields;
        BuildFields();
        RefreshConditionalFields();
    }

    private void BuildFields()
    {
        foreach (var field in _fields)
        {
            var container = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
            container.Children.Add(new TextBlock { Text = field.Label, Margin = new Thickness(0, 0, 0, 4), FontWeight = FontWeights.SemiBold });

            switch (field.Type)
            {
                case EditFieldType.Text:
                {
                    var tb = new TextBox
                    {
                        Text = field.GetValue()?.ToString() ?? string.Empty,
                        CharacterCasing = field.Uppercase ? CharacterCasing.Upper : CharacterCasing.Normal
                    };
                    container.Children.Add(tb);
                    _controls[field] = tb;
                    _liveGetters[field] = () => tb.Text;
                    tb.TextChanged += (_, _) => RefreshConditionalFields();
                    _readers.Add(() => { field.SetValue(tb.Text); return true; });
                    break;
                }
                case EditFieldType.MultilineText:
                {
                    var tb = new TextBox
                    {
                        Text = field.GetValue()?.ToString() ?? string.Empty,
                        AcceptsReturn = true,
                        Height = 70,
                        TextWrapping = TextWrapping.Wrap
                    };
                    container.Children.Add(tb);
                    _controls[field] = tb;
                    _liveGetters[field] = () => tb.Text;
                    tb.TextChanged += (_, _) => RefreshConditionalFields();
                    _readers.Add(() => { field.SetValue(tb.Text); return true; });
                    break;
                }
                case EditFieldType.Int:
                {
                    var tb = new TextBox { Text = field.GetValue()?.ToString() ?? string.Empty };
                    container.Children.Add(tb);
                    _readers.Add(() =>
                    {
                        if (field.Required && string.IsNullOrWhiteSpace(tb.Text)) return false;
                        field.SetValue(tb.Text);
                        return true;
                    });
                    break;
                }
                case EditFieldType.Decimal:
                {
                    var tb = new TextBox { Text = field.GetValue()?.ToString() ?? "0" };
                    container.Children.Add(tb);
                    _readers.Add(() => { field.SetValue(tb.Text); return true; });
                    break;
                }
                case EditFieldType.Date:
                {
                    var dp = new DatePicker();
                    if (field.GetValue() is DateTime dt) dp.SelectedDate = dt;
                    container.Children.Add(dp);
                    _readers.Add(() => { field.SetValue(dp.SelectedDate); return true; });
                    break;
                }
                case EditFieldType.Months:
                {
                    var wrap = new WrapPanel();
                    var current = (field.GetValue() as List<int>) ?? new List<int>();
                    var checkboxes = new List<CheckBox>();
                    string[] names = { "Jan", "Fév", "Mar", "Avr", "Mai", "Jun", "Jul", "Aoû", "Sep", "Oct", "Nov", "Déc" };
                    for (var m = 1; m <= 12; m++)
                    {
                        var cb = new CheckBox
                        {
                            Content = names[m - 1],
                            Tag = m,
                            IsChecked = current.Contains(m),
                            Margin = new Thickness(0, 0, 12, 4)
                        };
                        checkboxes.Add(cb);
                        wrap.Children.Add(cb);
                    }
                    // Préréglages « 2 x par an », « 3 x par an », « 4 x par an » (au-dessus des mois, à cocher à la main) : une
                    // fenêtre demande le mois de départ et les mois suivants se cochent aussitôt (aperçu en direct) ;
                    // « Annuler » rétablit la sélection d'avant.
                    // WrapPanel : les boutons passent à la ligne si la fenêtre est trop étroite (sinon le dernier est coupé).
                    var presets = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
                    presets.Children.Add(new TextBlock { Text = "Préréglages :", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 4) });
                    foreach (var times in new[] { 2, 3, 4 })
                    {
                        var preset = times;
                        var button = new Button { Content = $"{preset} x par an", Margin = new Thickness(0, 0, 6, 4), Padding = new Thickness(8, 3, 8, 3) };
                        if (TryFindResource("SecondaryButton") is Style secondary) button.Style = secondary;
                        button.Click += (_, _) => ApplyMonthsPreset(checkboxes, preset, field.MonthLoad);
                        presets.Children.Add(button);
                    }
                    // « Tous les mois » : les 12 mois d'un coup, sans mois de départ à choisir.
                    var everyMonth = new Button { Content = "Tous les mois", Margin = new Thickness(0, 0, 6, 4), Padding = new Thickness(8, 3, 8, 3) };
                    if (TryFindResource("SecondaryButton") is Style everyStyle) everyMonth.Style = everyStyle;
                    everyMonth.Click += (_, _) => { foreach (var cb in checkboxes) cb.IsChecked = true; };
                    presets.Children.Add(everyMonth);


                    // Charge des mois : rouge pour les mois les plus chargés, vert pour les moins chargés (relatif au mois le
                    // plus chargé de la vue, hors le filtre édité), orange entre les deux. Info-bulle : nombre de filtres.
                    if (field.MonthLoad is { Length: 12 } load && load.Max() > 0)
                    {
                        for (var i = 0; i < 12; i++)
                        {
                            var level = LoadLevel(load, i);
                            var cb = checkboxes[i];
                            cb.Foreground = LoadBrush(level);
                            cb.FontWeight = level == 2 ? FontWeights.SemiBold : FontWeights.Normal;
                            cb.ToolTip = load[i] == 0 ? "Aucun autre filtre ce mois-ci" : $"Charge de ce mois (autres filtres, coefficients appliqués) : {Fmt(load[i])}";
                        }
                        var legend = new TextBlock
                        {
                            TextWrapping = TextWrapping.Wrap, FontSize = 11, Margin = new Thickness(0, 4, 0, 0),
                            Foreground = (System.Windows.Media.Brush)FindResource("BrushTextSecondary"),
                            Text = "Couleur des mois : rouge = chargé, orange = moyen, vert = peu chargé (charge de toutes les vues, coefficients de Paramètres > Calcul de charge appliqués)."
                        };
                        container.Children.Add(presets);
                        container.Children.Add(wrap);
                        container.Children.Add(legend);
                    }
                    else
                    {
                        container.Children.Add(presets);
                        container.Children.Add(wrap);
                    }

                    _readers.Add(() =>
                    {
                        var selected = checkboxes.Where(c => c.IsChecked == true).Select(c => (int)c.Tag!).ToList();
                        field.SetValue(selected);
                        return true;
                    });
                    break;
                }
                case EditFieldType.Checklist:
                {
                    var wrap = new WrapPanel();
                    var current = (field.GetValue() as List<int>) ?? new List<int>();
                    var checkboxes = new List<CheckBox>();
                    var items = field.ComboItems ?? new List<string>();
                    for (var i = 0; i < items.Count; i++)
                    {
                        var cb = new CheckBox
                        {
                            Content = items[i],
                            Tag = i,
                            IsChecked = current.Contains(i),
                            Margin = new Thickness(0, 0, 16, 4)
                        };
                        checkboxes.Add(cb);
                        wrap.Children.Add(cb);
                    }
                    container.Children.Add(wrap);
                    _readers.Add(() =>
                    {
                        var selected = checkboxes.Where(c => c.IsChecked == true).Select(c => (int)c.Tag!).ToList();
                        if (field.Required && selected.Count == 0) return false;
                        field.SetValue(selected);
                        return true;
                    });
                    break;
                }
                case EditFieldType.Combo:
                {
                    var combo = new ComboBox();
                    foreach (var item in field.ComboItems ?? new List<string>()) combo.Items.Add(item);
                    var currentIndex = field.GetValue() is int ci ? ci : 0;
                    combo.SelectedIndex = combo.Items.Count == 0 ? -1 : Math.Clamp(currentIndex, 0, combo.Items.Count - 1);
                    container.Children.Add(combo);
                    _controls[field] = combo;
                    _liveGetters[field] = () => combo.SelectedIndex;
                    combo.SelectionChanged += (_, _) => RefreshConditionalFields();
                    _readers.Add(() =>
                    {
                        if (field.Required && combo.SelectedIndex < 0) return false;
                        field.SetValue(combo.SelectedIndex);
                        return true;
                    });
                    break;
                }
            }

            FieldsPanel.Children.Add(container);
        }
    }

    /// <summary>Réévalue, pour chaque champ déclarant <see cref="EditField.EnabledWhenFieldEquals"/>,
    /// s'il doit rester accessible selon la valeur en direct du champ dont il dépend - un champ désactivé
    /// est vidé (donc enregistré vide/null à la validation).</summary>
    private void RefreshConditionalFields()
    {
        foreach (var field in _fields)
        {
            if (field.EnabledWhenFieldEquals is not { } controller || field.EnabledPredicate is not { } predicate) continue;
            if (!_controls.TryGetValue(field, out var control)) continue;

            var controllingValue = _liveGetters.TryGetValue(controller, out var getter) ? getter() : null;
            var enabled = predicate(controllingValue);
            control.IsEnabled = enabled;
            if (!enabled && control is TextBox tb) tb.Text = string.Empty;
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        foreach (var reader in _readers)
        {
            if (!reader())
            {
                MessageBox.Show(this, "Merci de renseigner tous les champs obligatoires.", "Champs manquants",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    /// <summary>Niveau de charge d'un mois, relatif aux autres mois : 0 = peu chargé (vert), 1 = moyen (orange), 2 = chargé
    /// (rouge). Calculé entre le mois le moins chargé et le plus chargé, de sorte qu'il y a toujours au moins un mois vert
    /// (le moins chargé) et un mois rouge dès que les charges diffèrent ; charges toutes égales : tout est vert.</summary>
    private static string Fmt(double value) => value.ToString("0.#", System.Globalization.CultureInfo.CurrentCulture);

    private static int LoadLevel(double[] load, int month)
    {
        var min = load.Min();
        var max = load.Max();
        if (max == min) return 0;
        var position = (double)(load[month] - min) / (max - min);
        return position >= 2.0 / 3 ? 2 : position <= 1.0 / 3 ? 0 : 1;
    }

    private static System.Windows.Media.Brush LoadBrush(int level) => new System.Windows.Media.SolidColorBrush(level switch
    {
        2 => System.Windows.Media.Color.FromRgb(0xDC, 0x26, 0x26),
        1 => System.Windows.Media.Color.FromRgb(0xD9, 0x77, 0x06),
        _ => System.Windows.Media.Color.FromRgb(0x16, 0xA3, 0x4A)
    });

    /// <summary>Préréglage « N fois par an » : demande le mois de départ dans une petite fenêtre et coche les mois
    /// régulièrement espacés (tous les 12 / N mois) en direct. Annuler rétablit la sélection précédente.</summary>
    private void ApplyMonthsPreset(List<CheckBox> checkboxes, int timesPerYear, double[]? monthLoad)
    {
        var fullNames = new[] { "Janvier", "Février", "Mars", "Avril", "Mai", "Juin", "Juillet", "Août", "Septembre", "Octobre", "Novembre", "Décembre" };
        var before = checkboxes.Select(c => c.IsChecked == true).ToList();
        var interval = 12 / timesPerYear;

        // Charge des mois (autres filtres de la vue) : chaque mois du sélecteur est coloré (rouge chargé, vert peu chargé) et
        // le départ proposé par défaut est celui qui répartit le mieux le travail (somme des charges la plus faible).
        var hasLoad = monthLoad is { Length: 12 } && monthLoad.Max() > 0;
        double TotalLoad(int start) => Enumerable.Range(0, timesPerYear).Sum(i => monthLoad![(start + i * interval) % 12]);

        // Couleur de chaque mois de départ : moyenne de charge de TOUS les mois que ce départ cocherait (ex. 2 x par an : un
        // départ en janvier coche janvier et juillet, donc janvier et juillet ont la même couleur, celle de la moyenne des deux).
        var averages = hasLoad
            ? Enumerable.Range(0, 12).Select(s => TotalLoad(s) / timesPerYear).ToArray()
            : new double[12];

        var combo = new ComboBox { Margin = new Thickness(0, 6, 0, 10) };
        for (var i = 0; i < 12; i++)
        {
            var item = new ComboBoxItem { Content = hasLoad ? $"{fullNames[i]}  (charge moyenne {Fmt(averages[i])})" : fullNames[i] };
            if (hasLoad) item.Foreground = LoadBrush(LoadLevel(averages, i));
            combo.Items.Add(item);
        }
        var bestStart = hasLoad
            ? Enumerable.Range(0, 12).OrderBy(TotalLoad).ThenBy(i => (i - (DateTime.Today.Month - 1) + 12) % 12).First()
            : DateTime.Today.Month - 1;
        combo.SelectedIndex = bestStart;
        var info = new TextBlock { TextWrapping = TextWrapping.Wrap };

        void Refresh()
        {
            var start = Math.Max(0, combo.SelectedIndex);
            var months = Enumerable.Range(0, timesPerYear).Select(i => (start + i * interval) % 12).OrderBy(m => m).ToList();
            for (var i = 0; i < checkboxes.Count; i++) checkboxes[i].IsChecked = months.Contains(i);
            info.Text = $"{timesPerYear} x par an, tous les {interval} mois : {string.Join(", ", months.Select(m => fullNames[m]))}."
                        + (hasLoad ? $" Charge moyenne de ces mois : {Fmt(averages[start])}" + (start == bestStart ? " (meilleur départ)." : ".") : string.Empty);
        }

        var ok = new Button { Content = "Appliquer", IsDefault = true, Width = 100, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Annuler", IsCancel = true, Width = 100 };
        if (TryFindResource("PrimaryButton") is Style primary) ok.Style = primary;
        if (TryFindResource("SecondaryButton") is Style secondary) cancel.Style = secondary;

        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock { Text = $"Mois de départ du calcul ({timesPerYear} x par an) :", FontWeight = FontWeights.SemiBold });
        panel.Children.Add(combo);
        panel.Children.Add(info);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);

        var window = new Window
        {
            Title = "Préréglage de périodicité", Content = panel, Owner = this, Width = 380, SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false
        };
        ok.Click += (_, _) => window.DialogResult = true;
        combo.SelectionChanged += (_, _) => Refresh();
        Refresh();   // aperçu immédiat dès l'ouverture

        if (window.ShowDialog() != true)
            for (var i = 0; i < checkboxes.Count; i++) checkboxes[i].IsChecked = before[i];
    }
}
